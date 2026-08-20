using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.EntityFrameworkCore.SqlServer.BulkInsert;

namespace Ruya.EntityFrameworkCore.SqlServer.Tests.BulkInsert;

[TestClass]
public sealed class BulkInsertSecurityTests
{
    [TestMethod]
    public void BulkInsertOperationsSettings_ZeroTimeout_FailsValidation()
    {
        var settings = new BulkInsertOperationsSettings { Timeout = 0 };

        var results = Validate(settings);

        Assert.HasCount(1, results);
        Assert.AreEqual(nameof(BulkInsertOperationsSettings.Timeout), results[0].MemberNames.Single());
    }

    [TestMethod]
    public void BulkInsertOptions_ZeroBatchSize_FailsValidation()
    {
        var options = new BulkInsertOptions { BatchSize = 0 };

        var results = Validate(options);

        Assert.HasCount(1, results);
        Assert.AreEqual(nameof(BulkInsertOptions.BatchSize), results[0].MemberNames.Single());
    }

    [TestMethod]
    public void NormalizeDestinationTableName_SqlMetacharacters_QuotesAsIdentifier()
    {
        var result = BulkInsertOperations.NormalizeDestinationTableName("dbo.Products; DROP TABLE Users");

        Assert.AreEqual("[dbo].[Products; DROP TABLE Users]", result);
    }

    [TestMethod]
    public void NormalizeDestinationTableName_UnbalancedBracket_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => BulkInsertOperations.NormalizeDestinationTableName("dbo.[Products"));
    }

    private static List<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return results;
    }
}
