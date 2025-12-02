using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    private static Dictionary<string, string?> AppSettings = new Dictionary<string, string?>
    {
        {"Logging:LogLevel:Default", "Trace"},
        {"Logging:LogLevel:System", "Warning"},
        {"Logging:LogLevel:Microsoft", "Information"},
        {"OTEL_EXPORTER_OTLP_ENDPOINT", "http://host.docker.internal:18889"},
        {"OTEL_EXPORTER_OTLP_PROTOCOL","grpc"},
        {"ConnectionStrings:MyConnection", "N/A"},
        {"MyService:ConnectionStringKey", "MyConnection"},
        {"FeatureManagement:MySerivce", "true"},
        {"DistributedTracing",""}
    };

    [TestMethod]
    public async Task VerifyOpenTelemetry()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder(new string[] { });
        builder.Environment.EnvironmentName = "Development";
        builder.Configuration.AddInMemoryCollection(AppSettings);
        builder.WebHost.UseUrls("http://localhost:5000");

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

        var app = builder.Build();
        app.MapPrometheusScrapingEndpoint();
        app.MapGet("/", (IMyService myService) => myService.DoWorkAsync(CancellationToken.None));

        // Act
        _ = app.RunAsync();

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

        // Give some time for metrics to be collected
        await Task.Delay(2000);

        // Assert
        using var client = new HttpClient();
        string response = "";
        try 
        {
            response = await client.GetStringAsync("http://localhost:5000/metrics");
            Console.WriteLine("Metrics output:");
            Console.WriteLine(response);
        }
        catch(Exception ex)
        {
             Assert.Fail($"Failed to fetch metrics: {ex.Message}");
        }
        
        await app.StopAsync();

        Assert.IsTrue(response.Contains("distributed_tracing"), "Distributed tracing metrics NOT found.");
        Assert.IsTrue(response.Contains("extra_meter_counter"), "Extra meter metrics NOT found.");

        Assert.IsTrue(exportedActivities.Count > 0, "No activities were exported.");
        Assert.IsTrue(exportedActivities.Any(a => a.DisplayName == "DoWork"), "Activity 'DoWork' not found.");
        Assert.IsTrue(exportedActivities.Any(a => a.DisplayName == "SimulatedWork"), "Activity 'SimulatedWork' not found.");

        Assert.IsTrue(exportedLogs.Count > 0, "No logs were exported.");
        Assert.IsTrue(exportedLogs.Any(l => l.Body == "Starting work"), "Log 'Starting work' not found.");
        Assert.IsTrue(exportedLogs.Any(l => l.Body == "Work completed successfully"), "Log 'Work completed successfully' not found.");
    }
}
