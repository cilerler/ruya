using System;
using Ruya.Primitives;

namespace Ruya.EntityFrameworkCore.SqlServer.BatchLock;

public static class SqlQuery
{
	private static readonly Lazy<string> _selectForUpdatePrivate = new(() => AssemblyReference.Assembly.GetEmbeddedResourceContent(Constants.SelectForUpdate));
	public static string SelectForUpdate => _selectForUpdatePrivate.Value;
}
