using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using Dapper;
using FastMember;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ruya.Services.DataAccess.Abstractions;

namespace Ruya.Services.DataAccess.Sql;

public class Client : ISqlClient
{
	private readonly IConfiguration _configuration;
	private readonly ILogger _logger;
	private string _connectionStringKey = "DefaultConnectionString";


	// ReSharper disable once SuggestBaseTypeForParameter
	public Client(ILogger<Client> logger, IConfiguration configuration)
	{
		_logger = logger;
		_configuration = configuration;
	}

	public int BulkCopyTimeout { set; get; } = 0;

	public string ConnectionString => _configuration.GetConnectionString(ConnectionStringKey);

	#region IDataAccessService Members

	public string ConnectionStringKey
	{
		set
		{
			if (!string.IsNullOrWhiteSpace(_connectionStringKey)) _connectionStringKey = value;
		}
		get => _connectionStringKey;
	}

	public int DefaultTimeout { set; get; } = 240;

	public const string ElapsedTimeMessage = "Elapsed time for database operation {Elapsed}";

	public void Query(Action<SqlConnection> action)
	{
		const string methodName = nameof(Query);
		if (string.IsNullOrWhiteSpace(ConnectionString)) throw new Exception("There is a problem with ConnectionString");

		using (var connection = new SqlConnection(ConnectionString))
		{
			_logger.LogTrace("Openning connection to {DataSource}.{Database}", connection.DataSource, connection.Database);
			var stopwatch = Stopwatch.StartNew();
			connection.Open();
			action(connection);
			connection.Close();
			stopwatch.Stop();
			_logger.LogTrace("Connection to {DataSource}.{Database} has been closed", connection.DataSource, connection.Database);
			_logger.LogTrace(ElapsedTimeMessage, stopwatch.Elapsed);
		}
	}

	public IEnumerable<T> Query<T>(string sql, object parameters, bool isStoredProcedure = false, int commandTimeout = default)
	{
		const string methodName = nameof(Query);
		_logger.LogTrace("Query {sql}", sql);
		IEnumerable<T> result = null;
		Query(connection =>
		{
			result = connection.Query<T>(sql,
				parameters,
				commandType: isStoredProcedure
					? CommandType.StoredProcedure
					: CommandType.Text,
				commandTimeout: commandTimeout == default
					? DefaultTimeout
					: commandTimeout);
		});
		return result;
	}

	public int Execute(string sql, object parameters, bool isStoredProcedure = false, int commandTimeout = default)
	{
		const string methodName = nameof(Execute);
		_logger.LogTrace("Query {sql}", sql);
		var result = 0;
		Query(connection =>
		{
			result = connection.Execute(sql,
				parameters,
				commandType: isStoredProcedure
					? CommandType.StoredProcedure
					: CommandType.Text,
				commandTimeout: commandTimeout == default
					? DefaultTimeout
					: commandTimeout);
		});
		return result;
	}


	public int TransactionalExecute(string sql, object parameters, bool isStoredProcedure = false, int commandTimeout = default)
	{
		const string methodName = nameof(TransactionalExecute);
		_logger.LogTrace("Query {sql}", sql);
		var result = 0;
		Query(connection =>
		{
			using (SqlTransaction sqlTransaction = connection.BeginTransaction())
			{
				try
				{
					result = connection.Execute(sql,
						parameters,
						sqlTransaction,
						commandType: isStoredProcedure
							? CommandType.StoredProcedure
							: CommandType.Text,
						commandTimeout: commandTimeout == default
							? DefaultTimeout
							: commandTimeout);
					sqlTransaction.Commit();
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "{Message} {sql}", ex.Message, sql);
					sqlTransaction.Rollback();
					_logger.LogInformation("Rollback successful");
					throw;
				}
			}
		});
		return result;
	}

	public IEnumerable<T> TransactionalQuery<T>(string sql, object parameters, bool isStoredProcedure = false, int commandTimeout = default)
	{
		const string methodName = nameof(TransactionalQuery);
		_logger.LogTrace("Query {sql}", sql);
		IEnumerable<T> result = null;
		Query(connection =>
		{
			using (SqlTransaction sqlTransaction = connection.BeginTransaction())
			{
				try
				{
					result = connection.Query<T>(sql,
						parameters,
						sqlTransaction,
						commandType: isStoredProcedure
							? CommandType.StoredProcedure
							: CommandType.Text,
						commandTimeout: commandTimeout == default
							? DefaultTimeout
							: commandTimeout);
					sqlTransaction.Commit();
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "{Message} {sql}", ex.Message, sql);
					sqlTransaction.Rollback();
					_logger.LogInformation("Rollback successful");
					throw;
				}
			}
		});
		return result;
	}

	public IEnumerable<T> TransactionalQueryReadUncommitted<T>(string sql, object parameters, bool isStoredProcedure = false,
		int commandTimeout = default)
	{
		const string methodName = nameof(TransactionalQueryReadUncommitted);
		_logger.LogTrace("Query {sql}", sql);
		IEnumerable<T> result = null;
		Query(connection =>
		{
			using (SqlTransaction sqlTransaction = connection.BeginTransaction(IsolationLevel.ReadUncommitted))
			{
				try
				{
					result = connection.Query<T>(sql,
						parameters,
						sqlTransaction,
						commandType: isStoredProcedure
							? CommandType.StoredProcedure
							: CommandType.Text,
						commandTimeout: commandTimeout == default
							? DefaultTimeout
							: commandTimeout);
					sqlTransaction.Commit();
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "{Message} {sql}", ex.Message, sql);
					sqlTransaction.Rollback();
					_logger.LogInformation("Rollback successful");
					throw;
				}
			}
		});
		return result;
	}

	public void BulkInsert<T>(string tableName, IEnumerable<T> items, string[] members, SqlConnection alternateConnection = null,
		SqlTransaction scope = null, int commandTimeout = default)
	{
		const string methodName = nameof(BulkInsert);

		if (alternateConnection == null)
		{
			_logger.LogTrace("Alternate connection does not exist.");
			Query(connection =>
			{
				BulkInsertExecute(tableName, items, members, connection, scope);
			});
		}
		else
		{
			_logger.LogTrace("Alternate connection exist.");
			BulkInsertExecute(tableName, items, members, alternateConnection, scope);
		}
	}

	public void BulkInsertExecute<T>(string tableName, IEnumerable<T> items, string[] members, SqlConnection connection,
		SqlTransaction scope = null, int commandTimeout = default)
	{
		const string methodName = nameof(BulkInsertExecute);
		IList<T> enumerable = items as IList<T> ?? items.ToList();

		try
		{
			if (members == null) throw new ArgumentNullException(nameof(members));

			SqlBulkCopyOptions bulkCopyOptions = SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers;
			using (var bulkCopy = new SqlBulkCopy(connection, bulkCopyOptions, scope))
			{
				bulkCopy.BatchSize = 100;
				bulkCopy.BulkCopyTimeout = commandTimeout == default
					? BulkCopyTimeout
					: commandTimeout;
				bulkCopy.DestinationTableName = tableName;

				_logger.LogTrace("Mapping columns");
				foreach (string propertyName in members) bulkCopy.ColumnMappings.Add(propertyName, propertyName);

				_logger.LogTrace("BulkCopy will write to server");
				using (var itemsTable = ObjectReader.Create(enumerable, members))
				{
					bool connectionExist = connection.State == ConnectionState.Open && connection.State != ConnectionState.Broken;
					if (!connectionExist) throw new Exception("The SQL connection was closed unexpectedly.");
					bulkCopy.WriteToServer(itemsTable);
				}

				_logger.LogTrace("BulkCopy complete");
			}
		}
		catch (Exception ex)
		{
			if (ex.Message == "Object reference not set to an instance of an object")
				_logger.LogError(ex, "{Message} {items}", ex.Message, enumerable);

			throw;
		}
	}

	#endregion
}
