using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.OpenTelemetry;
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using Startup = Ruya.Primitives.Startup;

namespace Ruya.OpenTelemetry.Tests;

[TestClass]
public class OpenTelemetryTests
{
    private const string ConfiguredLibraryName =
        $"{nameof(Ruya)}.{nameof(Ruya.OpenTelemetry)}.{nameof(Ruya.OpenTelemetry.Tests)}.ConfiguredLibrary";

    private static readonly Dictionary<string, string?> AppSettings = new()
    {
        {"Logging:LogLevel:Default", "Trace"},
        {"Logging:LogLevel:System", "Warning"},
        {"Logging:LogLevel:Microsoft", "Information"},
        {"OpenTelemetry:Sampling:Type", "AlwaysOn"},
        {"OpenTelemetry:ActivitySources:0", ConfiguredLibraryName},
        {"OpenTelemetry:Meters:0", ConfiguredLibraryName},
        {"ConnectionStrings:MyConnection", "N/A"},
        {"MyService:ConnectionStringKey", "MyConnection"},
        {"FeatureManagement:MySerivce", "true"},
        {"DistributedTracing",""}
    };

    [TestMethod]
    public async Task ConfigureOpenTelemetry_ApplicationRuns_ExportsSignals()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Environment.EnvironmentName = "Development";
        builder.Configuration.AddInMemoryCollection(AppSettings);
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

        builder.Services.AddHttpClient();
        builder.Services.AddDistributedMemoryCache();

        builder.ConfigureOpenTelemetry();
        // Test additive configuration: Add "Extra.Meter" on top of existing ones
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter("Extra.Meter"));

        var exportedActivities = new List<Activity>();
        builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities));

        var exportedLogs = new List<LogRecord>();
        builder.Logging.AddOpenTelemetry(options => options.AddInMemoryExporter(exportedLogs));

        builder.Services.AddDistributedTracingService();
        builder.Services.AddMyService();

        await using var app = builder.Build();
        app.MapPrometheusScrapingEndpoint();
        app.MapGet("/", (IMyService myService) => myService.DoWorkAsync(CancellationToken.None));

        await app.StartAsync();

        using (var activitySource = new ActivitySource(ConfiguredLibraryName))
        {
            using var activity = activitySource.StartActivity("ConfiguredLibraryActivity");
            Assert.IsNotNull(activity, "The configured activity source was not subscribed.");
        }

        using var libraryMeter = new Meter(ConfiguredLibraryName);
        var libraryCounter = libraryMeter.CreateCounter<long>("configured_library_collection_probe");
        libraryCounter.Add(1);

        // Simulate some work
        using (var scope = app.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMyService>();
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                try
                {
                    await service.DoWorkAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Ignore
                }
            }
        }

        using var client = new HttpClient();
        var baseAddress = new Uri(app.Urls.Single());
        var response = await client.GetStringAsync(new Uri(baseAddress, "/metrics"));

        await app.StopAsync();

        StringAssert.Contains(response, "distributed_tracing", StringComparison.Ordinal);
        StringAssert.Contains(response, "extra_meter_counter", StringComparison.Ordinal);
        StringAssert.Contains(response, "configured_library_collection_probe_total", StringComparison.Ordinal);

        Assert.IsTrue(exportedActivities.Count > 0, "No activities were exported.");
        Assert.IsTrue(exportedActivities.Any(a => a.DisplayName == "ConfiguredLibraryActivity"), "The configured library activity was not exported.");
        Assert.IsTrue(exportedActivities.Any(a => a.DisplayName == "DoWork"), "Activity 'DoWork' not found.");
        Assert.IsTrue(exportedActivities.Any(a => a.DisplayName == "SimulatedWork"), "Activity 'SimulatedWork' not found.");

        Assert.IsTrue(exportedLogs.Count > 0, "No logs were exported.");
        Assert.IsTrue(exportedLogs.Any(l => l.Body == "Starting work"), "Log 'Starting work' not found.");
        Assert.IsTrue(exportedLogs.Any(l => l.Body == "Work completed successfully"), "Log 'Work completed successfully' not found.");
    }
}
