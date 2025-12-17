using System;
using Ruya.Primitives;

namespace Ruya.EntityFrameworkCore.SqlServer.BatchLock;

public static class SqlQuery
{
	private const string _resourcePrefix = $"{nameof(BatchLock)}.Resources";
	private static readonly Lazy<string> _selectForUpdatePrivate = new(() => AssemblyReference.Assembly.GetEmbeddedResourceContent(Constants.SelectForUpdate, _resourcePrefix));
	public static string SelectForUpdate => _selectForUpdatePrivate.Value;
}
