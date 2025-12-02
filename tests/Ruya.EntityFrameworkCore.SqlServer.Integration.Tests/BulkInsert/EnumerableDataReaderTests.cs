using System;
using System.Collections.Generic;
using System.Data.Common;
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
        public readonly string ReadOnlyField = "ReadOnly";
        public string WriteOnlyField = "WriteOnly";
    }

    [TestMethod]
    public void Read_WhenCalledOnNonEmptyCollection_ReturnsTrueAndAdvances()
    {
        // Arrange
        var data = new[] { new TestEntity { Id = 1, Name = "A" } };
        using var reader = new EnumerableDataReader<TestEntity>(data, new[] { "Id", "Name" });

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
        using var reader = new EnumerableDataReader<TestEntity>(data, new[] { "Id", "Name" });

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
        using var reader = new EnumerableDataReader<TestEntity>(data, new[] { "Id", "Name", "Price" });
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
        using var reader = new EnumerableDataReader<TestEntity>(data, new[] { "Price" });
        reader.Read();

        // Act
        var price = reader.GetValue(0);

        // Assert
        Assert.AreEqual(DBNull.Value, price);
    }

    [TestMethod]
    public void FieldCount_ReturnsCorrectNumberOfMembers()
    {
        // Arrange
        var data = Array.Empty<TestEntity>();
        using var reader = new EnumerableDataReader<TestEntity>(data, new[] { "Id", "Name", "Price" });

        // Assert
        Assert.AreEqual(3, reader.FieldCount);
    }

    [TestMethod]
    public void GetOrdinal_WithValidName_ReturnsCorrectIndex()
    {
        // Arrange
        var data = Array.Empty<TestEntity>();
        using var reader = new EnumerableDataReader<TestEntity>(data, new[] { "Id", "Name", "Price" });

        // Act & Assert
        Assert.AreEqual(0, reader.GetOrdinal("Id"));
        Assert.AreEqual(1, reader.GetOrdinal("Name"));
        Assert.AreEqual(2, reader.GetOrdinal("Price"));
    }

    [TestMethod]
    public void GetOrdinal_WithInvalidName_ThrowsIndexOutOfRangeException()
    {
        // Arrange
        var data = Array.Empty<TestEntity>();
        using var reader = new EnumerableDataReader<TestEntity>(data, new[] { "Id" });

        // Act & Assert
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("InvalidName"));
    }

    [TestMethod]
    public void GetName_WithValidIndex_ReturnsCorrectName()
    {
        // Arrange
        var data = Array.Empty<TestEntity>();
        using var reader = new EnumerableDataReader<TestEntity>(data, new[] { "Id", "Name" });

        // Act & Assert
        Assert.AreEqual("Id", reader.GetName(0));
        Assert.AreEqual("Name", reader.GetName(1));
    }

    [TestMethod]
    public void IsDbNull_WithNullValue_ReturnsTrue()
    {
        // Arrange
        var data = new[] { new TestEntity { Price = null } };
        using var reader = new EnumerableDataReader<TestEntity>(data, new[] { "Price" });
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
        using var reader = new EnumerableDataReader<TestEntity>(data, new[] { "Price" });
        reader.Read();

        // Act
        var result = reader.IsDBNull(0);

        // Assert
        Assert.IsFalse(result);
    }
}