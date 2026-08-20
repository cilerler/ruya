using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.EntityFrameworkCore.SqlServer.BulkInsert;

namespace Ruya.EntityFrameworkCore.SqlServer.Tests.BulkInsert;

[TestClass]
[TestCategory("Unit")]
public class EnumerableDataReaderTests
{
    public class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal? Price { get; set; }
        public byte[] Data { get; set; } = [];
        public string Text { get; set; } = string.Empty;
        public readonly string ReadOnlyField = "ReadOnly";
        public string WriteOnlyField = "WriteOnly";
    }

    public class ColumnMappedEntity
    {
        public int Id { get; set; }

        [Column("OrgId")]
        public int OrganizationId { get; set; }

        [Column("DisplayName")]
        public string FullName { get; set; } = string.Empty;

        public string UnmappedProperty { get; set; } = string.Empty;
    }

    [TestMethod]
    public void Read_WhenCalledOnNonEmptyCollection_ReturnsTrueAndAdvances()
    {
        // Arrange
        var data = new[] { new TestEntity { Id = 1, Name = "A" } };
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Id), nameof(TestEntity.Name)]);

        // Act
        var result = reader.Read();

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void Read_WhenCalledOnEmptyCollection_ReturnsFalse()
    {
        // Arrange
        var data = Array.Empty<TestEntity>();
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Id), nameof(TestEntity.Name)]);

        // Act
        var result = reader.Read();

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void GetValue_AfterRead_ReturnsCorrectValue()
    {
        // Arrange
        var data = new[] { new TestEntity { Id = 1, Name = "A", Price = 10.5m } };
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Id), nameof(TestEntity.Name), nameof(TestEntity.Price)]);
        reader.Read();

        // Act
        var id = reader.GetValue(0);
        var name = reader.GetValue(1);
        var price = reader.GetValue(2);

        // Assert
        Assert.AreEqual(1, id);
        Assert.AreEqual("A", name);
        Assert.AreEqual(10.5m, price);
    }

    [TestMethod]
    public void GetValue_WithNullPropertyValue_ReturnsDbNull()
    {
        // Arrange
        var data = new[] { new TestEntity { Id = 1, Name = "A", Price = null } };
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Price)]);
        reader.Read();

        // Act
        var price = reader.GetValue(0);

        // Assert
        Assert.AreEqual(DBNull.Value, price);
    }

    [TestMethod]
    public void GetBytes_WithNullBuffer_ReturnsSourceLength()
    {
        var data = new[] { new TestEntity { Data = [1, 2, 3, 4] } };
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Data)]);
        Assert.IsTrue(reader.Read());

        var length = reader.GetBytes(0, 0, null, 0, 0);

        Assert.AreEqual(4L, length);
    }

    [TestMethod]
    public void GetBytes_WithOffsets_CopiesRequestedRange()
    {
        var data = new[] { new TestEntity { Data = [1, 2, 3, 4] } };
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Data)]);
        Assert.IsTrue(reader.Read());
        var destination = new byte[5];

        var copied = reader.GetBytes(0, 1, destination, 2, 2);

        Assert.AreEqual(2L, copied);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 2, 3, 0 }, destination);
    }

    [TestMethod]
    public void GetChars_WithOffsets_CopiesRequestedRange()
    {
        var data = new[] { new TestEntity { Text = "abcdef" } };
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Text)]);
        Assert.IsTrue(reader.Read());
        var destination = new char[4];

        var copied = reader.GetChars(0, 2, destination, 1, 3);

        Assert.AreEqual(3L, copied);
        CollectionAssert.AreEqual(new[] { '\0', 'c', 'd', 'e' }, destination);
    }

    [TestMethod]
    public void FieldCount_ReturnsCorrectNumberOfMembers()
    {
        // Arrange
        var data = Array.Empty<TestEntity>();
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Id), nameof(TestEntity.Name), nameof(TestEntity.Price)]);

        // Assert
        Assert.AreEqual(3, reader.FieldCount);
    }

    [TestMethod]
    public void GetOrdinal_WithValidName_ReturnsCorrectIndex()
    {
        // Arrange
        var data = Array.Empty<TestEntity>();
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Id), nameof(TestEntity.Name), nameof(TestEntity.Price)]);

        // Act & Assert
        Assert.AreEqual(0, reader.GetOrdinal(nameof(TestEntity.Id)));
        Assert.AreEqual(1, reader.GetOrdinal(nameof(TestEntity.Name)));
        Assert.AreEqual(2, reader.GetOrdinal(nameof(TestEntity.Price)));
    }

    [TestMethod]
    public void GetOrdinal_WithInvalidName_ThrowsIndexOutOfRangeException()
    {
        // Arrange
        var data = Array.Empty<TestEntity>();
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Id)]);

        // Act & Assert
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("InvalidName"));
    }

    [TestMethod]
    public void GetName_WithValidIndex_ReturnsCorrectName()
    {
        // Arrange
        var data = Array.Empty<TestEntity>();
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Id), nameof(TestEntity.Name)]);

        // Act & Assert
        Assert.AreEqual(nameof(TestEntity.Id), reader.GetName(0));
        Assert.AreEqual(nameof(TestEntity.Name), reader.GetName(1));
    }

    [TestMethod]
    public void IsDbNull_WithNullValue_ReturnsTrue()
    {
        // Arrange
        var data = new[] { new TestEntity { Price = null } };
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Price)]);
        reader.Read();

        // Act
        var result = reader.IsDBNull(0);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsDbNull_WithNonNullValue_ReturnsFalse()
    {
        // Arrange
        var data = new[] { new TestEntity { Price = 10.5m } };
        using var reader = new EnumerableDataReader<TestEntity>(data, [nameof(TestEntity.Price)]);
        reader.Read();

        // Act
        var result = reader.IsDBNull(0);

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void GetValue_WithColumnAttribute_ResolvesByColumnName()
    {
        // Arrange
        var data = new[] { new ColumnMappedEntity { OrganizationId = 42, FullName = "Acme Corp" } };
        using var reader = new EnumerableDataReader<ColumnMappedEntity>(data, new[] { "OrgId", "DisplayName" });
        reader.Read();

        // Act
        var orgId = reader.GetValue(0);
        var displayName = reader.GetValue(1);

        // Assert
        Assert.AreEqual(42, orgId);
        Assert.AreEqual("Acme Corp", displayName);
    }

    [TestMethod]
    public void GetFieldType_WithColumnAttribute_ReturnsCorrectType()
    {
        // Arrange
        var data = Array.Empty<ColumnMappedEntity>();
        using var reader = new EnumerableDataReader<ColumnMappedEntity>(data, new[] { "OrgId", "DisplayName" });

        // Act & Assert
        Assert.AreEqual(typeof(int), reader.GetFieldType(0));
        Assert.AreEqual(typeof(string), reader.GetFieldType(1));
    }

    [TestMethod]
    public void Constructor_WithColumnAttributeMixedWithDirectNames_ResolvesBoth()
    {
        // Arrange — "Id" resolves by property name, "OrgId" resolves by [Column] attribute
        var data = new[] { new ColumnMappedEntity { Id = 1, OrganizationId = 99, UnmappedProperty = "test" } };
        using var reader = new EnumerableDataReader<ColumnMappedEntity>(data, [nameof(ColumnMappedEntity.Id), "OrgId", nameof(ColumnMappedEntity.UnmappedProperty)]);
        reader.Read();

        // Act & Assert
        Assert.AreEqual(1, reader.GetValue(0));
        Assert.AreEqual(99, reader.GetValue(1));
        Assert.AreEqual("test", reader.GetValue(2));
    }

    [TestMethod]
    public void GetOrdinal_WithColumnAttributeName_ReturnsCorrectIndex()
    {
        // Arrange
        var data = Array.Empty<ColumnMappedEntity>();
        using var reader = new EnumerableDataReader<ColumnMappedEntity>(data, [nameof(ColumnMappedEntity.Id), "OrgId"]);

        // Act & Assert
        Assert.AreEqual(0, reader.GetOrdinal(nameof(ColumnMappedEntity.Id)));
        Assert.AreEqual(1, reader.GetOrdinal("OrgId"));
    }

    [TestMethod]
    public void Constructor_WithInvalidMember_DisposesEnumerator()
    {
        // Arrange
        var tracker = new DisposalTrackingEnumerable<ColumnMappedEntity>(Array.Empty<ColumnMappedEntity>());

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new EnumerableDataReader<ColumnMappedEntity>(tracker, new[] { "NonExistentProperty" }));

        Assert.IsTrue(tracker.EnumeratorDisposed, "Enumerator should be disposed when constructor throws.");
    }

    [TestMethod]
    public void Constructor_WithEmptyMembers_DisposesEnumerator()
    {
        // Arrange
        var tracker = new DisposalTrackingEnumerable<TestEntity>(Array.Empty<TestEntity>());

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new EnumerableDataReader<TestEntity>(tracker, Array.Empty<string>()));

        Assert.IsTrue(tracker.EnumeratorDisposed, "Enumerator should be disposed when constructor throws.");
    }

    /// <summary>
    /// Helper that wraps an IEnumerable and tracks whether its enumerator was disposed.
    /// </summary>
    private sealed class DisposalTrackingEnumerable<TItem> : IEnumerable<TItem>
    {
        private readonly IEnumerable<TItem> _inner;
        public bool EnumeratorDisposed { get; private set; }

        public DisposalTrackingEnumerable(IEnumerable<TItem> inner) => _inner = inner;

        public IEnumerator<TItem> GetEnumerator() => new TrackingEnumerator(this, _inner.GetEnumerator());

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class TrackingEnumerator : IEnumerator<TItem>
        {
            private readonly DisposalTrackingEnumerable<TItem> _owner;
            private readonly IEnumerator<TItem> _inner;

            public TrackingEnumerator(DisposalTrackingEnumerable<TItem> owner, IEnumerator<TItem> inner)
            {
                _owner = owner;
                _inner = inner;
            }

            public TItem Current => _inner.Current;
            object? IEnumerator.Current => Current;
            public bool MoveNext() => _inner.MoveNext();
            public void Reset() => _inner.Reset();

            public void Dispose()
            {
                _owner.EnumeratorDisposed = true;
                _inner.Dispose();
            }
        }
    }
}
