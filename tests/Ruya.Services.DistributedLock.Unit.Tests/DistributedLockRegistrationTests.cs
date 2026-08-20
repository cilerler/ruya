using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.DistributedLock.Configuration;
using Ruya.Services.DistributedLock.Extensions;
using Ruya.Services.DistributedLock.InMemory.Extensions;
using Ruya.Services.DistributedLock.Telemetry;

namespace Ruya.Services.DistributedLock.Tests;

[TestClass]
public sealed class DistributedLockRegistrationTests
{
    [TestMethod]
    public void AddDistributedLockCore_BindsCanonicalConfigurationSection()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["DistributedLock:LockExpiry"] = "00:00:30",
            ["DistributedLock:InstanceName"] = "orders"
        });

        DistributedLockSettings settings = provider
            .GetRequiredService<IOptions<DistributedLockSettings>>()
            .Value;

        Assert.AreEqual("orders", settings.InstanceName);
        Assert.AreEqual(System.TimeSpan.FromSeconds(30), settings.LockExpiry);
    }

    [TestMethod]
    public void AddDistributedLockCore_WhenInstanceNameIsBlank_FailsOptionsValidation()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["DistributedLock:InstanceName"] = "   "
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<DistributedLockSettings>>().Value);
    }

    [TestMethod]
    public void AddDistributedLockCore_WhenInstanceNameLeavesNoRoomForSeparatorAndKey_FailsOptionsValidation()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["DistributedLock:InstanceName"] = new string('x', 254)
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<DistributedLockSettings>>().Value);
    }

    [TestMethod]
    public void AddInMemoryDistributedLock_WithProgrammaticSettings_DoesNotRequireConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryDistributedLock(settings =>
        {
            settings.InstanceName = "programmatic";
            settings.LockExpiry = System.TimeSpan.FromSeconds(12);
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        DistributedLockSettings settings = provider
            .GetRequiredService<IOptions<DistributedLockSettings>>()
            .Value;

        Assert.AreEqual("programmatic", settings.InstanceName);
        Assert.AreEqual(System.TimeSpan.FromSeconds(12), settings.LockExpiry);
    }

    [TestMethod]
    public void AddDistributedLockMetrics_AfterCoreRegistration_UsesExplicitMeterName()
    {
        string? publishedMeterName = null;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Name == "lock_acquired_total")
                {
                    publishedMeterName = instrument.Meter.Name;
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.Start();

        var services = new ServiceCollection();
        services.AddDistributedLockCore();
        services.AddDistributedLockMetrics("Contoso.CustomLocks");

        using ServiceProvider provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<DistributedLockMetrics>();

        Assert.AreEqual("Contoso.CustomLocks", publishedMeterName);
    }

    [TestMethod]
    public void RecordLockReleaseFailed_WhenProviderReleaseIsUnconfirmed_KeepsActiveLockCount()
    {
        const string meterName = "Contoso.ReleaseFailureLocks";
        long activeLockDelta = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == "active_locks")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => activeLockDelta += measurement);
        listener.Start();

        using var metrics = new DistributedLockMetrics(meterName);
        metrics.RecordLockAcquired("test", 1);
        metrics.RecordLockReleaseFailed("test", 2);

        Assert.AreEqual(1L, activeLockDelta);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddDistributedLockCore();
        return services.BuildServiceProvider();
    }
}
