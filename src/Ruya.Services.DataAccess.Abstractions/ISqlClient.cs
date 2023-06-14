using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace Ruya.Services.DataAccess.Abstractions;

public interface ISqlClient
{
	int DefaultTimeout { get; set; }
	string ConnectionStringKey { get; set; }
	string ConnectionString { get; }
	void Query(Action<SqlConnection> action);
	int Execute(string sql, object parameters, bool isStoredProcedure = false, int commandTimeout = default);
	IEnumerable<T> Query<T>(string sql, object parameters, bool isStoredProcedure = false, int commandTimeout = default);
	int TransactionalExecute(string sql, object parameters, bool isStoredProcedure = false, int commandTimeout = default);
	IEnumerable<T> TransactionalQuery<T>(string sql, object parameters, bool isStoredProcedure = false, int commandTimeout = default);

	IEnumerable<T> TransactionalQueryReadUncommitted<T>(string sql, object parameters, bool isStoredProcedure = false,
		int commandTimeout = default);

	void BulkInsert<T>(string tableName, IEnumerable<T> items, string[] members, SqlConnection alternateConnection = null,
		SqlTransaction scope = null, int commandTimeout = default);

	void BulkInsertExecute<T>(string tableName, IEnumerable<T> items, string[] members, SqlConnection connection, SqlTransaction scope = null,
		int commandTimeout = default);
}
