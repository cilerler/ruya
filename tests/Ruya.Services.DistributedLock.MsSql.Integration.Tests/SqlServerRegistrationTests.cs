using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.DistributedLock.MsSql.Configuration;
using Ruya.Services.DistributedLock.MsSql.Extensions;
using Ruya.Services.DistributedLock.MsSql.Providers;

namespace Ruya.Services.DistributedLock.MsSql.Tests;

[TestClass]
public sealed class SqlServerRegistrationTests
{
    [TestMethod]
    public void AddSqlServerDistributedLock_ValidatesCatalogWithoutCopyingSecretIntoOptions()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["DistributedLock:SqlServer:ConnectionStringKey"] = "LockDatabase",
            ["ConnectionStrings:LockDatabase"] = "Server=localhost;Integrated Security=true"
        });

        SqlServerLockSettings settings = provider.GetRequiredService<IOptions<SqlServerLockSettings>>().Value;

        Assert.AreEqual("LockDatabase", settings.ConnectionStringKey);
        Assert.IsNull(settings.ConnectionString);
    }

    [TestMethod]
    public void AddSqlServerDistributedLock_WhenCatalogEntryIsMissing_FailsOptionsValidation()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["DistributedLock:SqlServer:ConnectionStringKey"] = "MissingDatabase"
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<SqlServerLockSettings>>().Value);
    }

    [TestMethod]
    public void CreateLockSessionConnectionString_DisablesPoolingForSessionOwnedLocks()
    {
        string effective = SqlServerLockProvider.CreateLockSessionConnectionString(
            "Server=localhost;Database=locks;Integrated Security=true;Pooling=true");

        var builder = new SqlConnectionStringBuilder(effective);

        Assert.IsFalse(builder.Pooling);
        Assert.AreEqual("localhost", builder.DataSource);
        Assert.AreEqual("locks", builder.InitialCatalog);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSqlServerDistributedLock();
        return services.BuildServiceProvider();
    }
}
