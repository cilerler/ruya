using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.Extensions.DependencyInjection;
using Ruya.Primitives;
using Startup = Ruya.Primitives.Startup;

namespace Ruya.OpenTelemetry.Tests;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Created by the options and configuration binding infrastructure.")]
internal sealed class MyServiceSettings
{
    public const string ConfigurationSectionName = nameof(MyService);

    [Required]
    public string ConnectionStringKey { get; set; } = null!;
}

internal interface IMyService
{
    Task<string> DoWorkAsync(CancellationToken cancellationToken);
}

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Created by dependency injection through the IMyService registration.")]
internal sealed class MyService : IMyService
{
    private readonly ILogger<MyService> _logger;
    private readonly IDistributedTracing _tracer;

    private readonly UpDownCounter<int> _myGauge;
    private readonly Counter<long> _workCounter;
    private readonly Histogram<double> _workDuration;

    public MyService(
        ILogger<MyService> logger,
        IDistributedTracing distributedTracing,
        IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(distributedTracing);
        ArgumentNullException.ThrowIfNull(meterFactory);

        _logger = logger;
        _tracer = distributedTracing;
        var meter = meterFactory.Create(new MeterOptions(Startup.AssemblyName)
        {
            Version = Startup.AssemblyVersion,
            Tags = new TagList
                {
                    { "code.namespace", GetType().Namespace },
                    { "code.class", GetType().Name }
                }
        });
        _myGauge = meter.CreateUpDownCounter<int>("app_service_requests", "count", "Count of calls.");
        _workCounter = meter.CreateCounter<long>("app_work_total", "operations", "Total work operations");
        _workDuration = meter.CreateHistogram<double>("app_work_duration_seconds", "s", "Work duration");

        // Verify additive configuration
        var extraMeter = meterFactory.Create(new MeterOptions("Extra.Meter"));
        var extraCounter = extraMeter.CreateCounter<long>("extra_meter_counter");
        extraCounter.Add(1);

    }

    public async Task<string> DoWorkAsync(CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var activity = await _tracer.StartActivityAsync(
            "DoWork",
            cancellationToken: cancellationToken);
        activity.SetTag("service.name", nameof(MyService));

        _myGauge.Add(1);
        _workCounter.Add(1);

        try
        {
            _logger.WorkStarted();

            using var delayActivity = await _tracer.StartActivityAsync(
                "SimulatedWork",
                cancellationToken: cancellationToken);
            delayActivity.SetTag("delay.milliseconds", 10);

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);

            delayActivity.SetStatus(ActivityStatusCode.Ok);
            activity.SetStatus(ActivityStatusCode.Ok);
            _logger.WorkCompleted();

            return "completed";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity.SetStatus(ActivityStatusCode.Unset);
            throw;
        }
        catch (Exception ex)
        {
            activity.SetStatus(ActivityStatusCode.Error);
            activity.SetTag("exception.type", ex.GetType().FullName);
            throw;
        }
        finally
        {
            _myGauge.Add(-1);
            stopwatch.Stop();
            _workDuration.Record(stopwatch.Elapsed.TotalSeconds);
        }
    }
}

internal static class StartupExtensions
{
    public static IServiceCollection AddMyService(this IServiceCollection serviceCollection, Action<MyServiceSettings>? setupAction = null)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.EnsureServicesRegistered(
            typeof(IDistributedTracing),
            typeof(IMeterFactory));

        serviceCollection.AddOptions<MyServiceSettings>()
            .BindConfiguration(MyServiceSettings.ConfigurationSectionName)
            .Configure<IConfiguration>((settings, configuration) =>
            {
                _ = configuration.GetConnectionString(settings.ConnectionStringKey) ??
                    throw new InvalidOperationException("Test connection string is required.");
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (setupAction != null)
        {
            serviceCollection.Configure(setupAction);
        }

        serviceCollection.AddScoped<IMyService, MyService>();

        return serviceCollection;
    }
}

internal static partial class TestLog
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Starting work")]
    public static partial void WorkStarted(this ILogger logger);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Work completed successfully")]
    public static partial void WorkCompleted(this ILogger logger);
}
