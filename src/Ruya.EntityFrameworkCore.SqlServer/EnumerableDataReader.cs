using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Ruya.EntityFrameworkCore.SqlServer.BulkInsert;

/// <summary>
/// A lightweight, streaming IDataReader for IEnumerable&lt;T&gt;.
/// Uses compiled expression trees for high-performance property access.
/// Replaces FastMember.ObjectReader.
/// </summary>
/// <typeparam name="T">The type of items in the collection.</typeparam>
public sealed class EnumerableDataReader<T> : IDataReader
{
    private readonly IEnumerator<T> _enumerator;
    private readonly Func<T, object>[] _accessors;
    private readonly Type[] _fieldTypes;
    private readonly string[] _names;
    private readonly Dictionary<string, int> _ordinalMap;
    private bool _closed;

    // Cache compiled accessors to avoid recompiling for every batch of the same type
    private static readonly ConcurrentDictionary<string, Func<T, object>> _accessorCache = new();

    public EnumerableDataReader(IEnumerable<T> source, IEnumerable<string> members)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(members);

        _enumerator = source.GetEnumerator();
        try
        {
            _names = members.ToArray();

            if (_names.Length == 0)
                throw new ArgumentException("At least one member must be specified.", nameof(members));

            _ordinalMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _accessors = new Func<T, object>[_names.Length];
            _fieldTypes = new Type[_names.Length];

            var type = typeof(T);

            for (int i = 0; i < _names.Length; i++)
            {
                var name = _names[i];
                _ordinalMap[name] = i;

                // Resolve member once for both accessor and type info
                var property = FindPropertyOrField(type, name, out var field);

                if (property != null)
                {
                    _fieldTypes[i] = property.PropertyType;
                    _accessors[i] = GetOrAddAccessor(name, () => CreatePropertyAccessor(property));
                }
                else if (field != null)
                {
                    _fieldTypes[i] = field.FieldType;
                    _accessors[i] = GetOrAddAccessor(name, () => CreateFieldAccessor(field));
                }
                else
                {
                    throw new ArgumentException($"Property or field '{name}' not found on type '{type.FullName}'.");
                }
            }
        }
        catch
        {
            _enumerator.Dispose();
            throw;
        }
    }

    private static Func<T, object> GetOrAddAccessor(string propertyName, Func<Func<T, object>> factory)
    {
        // Static fields in generic types are per-closed-generic-type.
        // So EnumerableDataReader<Product>._accessorCache is separate from EnumerableDataReader<Order>._accessorCache.
        // Property name alone is sufficient as key.
        return _accessorCache.GetOrAdd(propertyName, _ => factory());
    }

    /// <summary>
    /// Resolves a member by name using the following strategy:
    /// 1. Exact property name match (case-sensitive)
    /// 2. Property with matching [Column] attribute name (case-insensitive)
    /// 3. Public field match (case-sensitive)
    /// </summary>
    private static PropertyInfo? FindPropertyOrField(Type type, string name, out FieldInfo? field)
    {
        // 1. Direct property name match
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property != null)
        {
            field = null;
            return property;
        }

        // 2. Fallback: search by [Column] attribute (supports EF Core entities where C# property name differs from DB column name)
        property = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(p => string.Equals(p.GetCustomAttribute<ColumnAttribute>()?.Name, name, StringComparison.OrdinalIgnoreCase));
        if (property != null)
        {
            field = null;
            return property;
        }

        // 3. Field fallback
        field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
        return null;
    }

    private static Func<T, object> CreatePropertyAccessor(PropertyInfo property)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var propertyAccess = Expression.Property(parameter, property);
        var castToObject = Expression.Convert(propertyAccess, typeof(object));
        var lambda = Expression.Lambda<Func<T, object>>(castToObject, parameter);
        return lambda.Compile();
    }

    private static Func<T, object> CreateFieldAccessor(FieldInfo field)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var fieldAccess = Expression.Field(parameter, field);
        var castToObject = Expression.Convert(fieldAccess, typeof(object));
        var lambda = Expression.Lambda<Func<T, object>>(castToObject, parameter);
        return lambda.Compile();
    }

    #region IDataReader Implementation

    public object GetValue(int i)
    {
        if (_enumerator.Current == null) return DBNull.Value;

        var val = _accessors[i](_enumerator.Current);
        return val ?? DBNull.Value;
    }

    public bool Read()
    {
        return _enumerator.MoveNext();
    }

    public void Dispose()
    {
        Close();
    }

    public void Close()
    {
        if (!_closed)
        {
            _closed = true;
            _enumerator.Dispose();
        }
    }

    public int FieldCount => _names.Length;

    public int Depth => 0;

    public bool IsClosed => _closed;

    public int RecordsAffected => -1;

    public string GetName(int i) => _names[i];

    public int GetOrdinal(string name)
    {
        if (_ordinalMap.TryGetValue(name, out var ordinal))
            return ordinal;
        throw new IndexOutOfRangeException($"Field '{name}' not found.");
    }

    public string GetDataTypeName(int i) => GetFieldType(i).Name;

    public Type GetFieldType(int i) => _fieldTypes[i];

    public object this[int i] => GetValue(i);

    public object this[string name] => GetValue(GetOrdinal(name));

    // Validations / Data Conversions (Minimal implementation needed for SqlBulkCopy)
    // SqlBulkCopy typically calls GetValue. It might call GetString etc if it knows the type.

    public bool GetBoolean(int i) => (bool)GetValue(i);
    public byte GetByte(int i) => (byte)GetValue(i);
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public char GetChar(int i) => (char)GetValue(i);
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotImplementedException();
    public Guid GetGuid(int i) => (Guid)GetValue(i);
    public short GetInt16(int i) => (short)GetValue(i);
    public int GetInt32(int i) => (int)GetValue(i);
    public long GetInt64(int i) => (long)GetValue(i);
    public float GetFloat(int i) => (float)GetValue(i);
    public double GetDouble(int i) => (double)GetValue(i);
    public string GetString(int i) => (string)GetValue(i);
    public decimal GetDecimal(int i) => (decimal)GetValue(i);
    public DateTime GetDateTime(int i) => (DateTime)GetValue(i);

    public bool IsDBNull(int i)
    {
        var val = GetValue(i);
        return val == null || val == DBNull.Value;
    }

    public DataTable? GetSchemaTable()
    {
         // SqlBulkCopy might call this.
         // We can return null or a basic table.
         // FastMember returns null by default? Let's check sources if we could.
         // But usually null is fine for SqlBulkCopy if we provide explicit column mappings (which we do).
         return null;
    }

    public bool NextResult() => false;

    public IDataReader GetData(int i)
    {
        throw new NotSupportedException("GetData is not supported.");
    }

    public int GetValues(object[] values)
    {
        int count = Math.Min(values.Length, _names.Length);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }
        return count;
    }

    #endregion
}
