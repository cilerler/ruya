using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.Extensions.DependencyInjection;
using Ruya.Primitives;
using Startup = Ruya.Primitives.Startup;

namespace Ruya.OpenTelemetry.Tests;

public class MyServiceSettings
{
    public const string ConfigurationSectionName = nameof(MyService);
    public static readonly string FeatureFlag = ConfigurationSectionName;
    public bool Enabled { get; set; }
    public string ConnectionString { get; internal set; } = null!;

    [Required]
    [NoNumericCharacters]
    public string ConnectionStringKey { get; set; } = null!;
}

public interface IMyService
{
    Task<string> DoWorkAsync(CancellationToken cancellationToken);
}

public class MyService : IMyService
{
    private readonly ILogger<MyService> _logger;
    private readonly IDistributedTracing _tracer;
    private readonly Meter _meter;
    private readonly MyServiceSettings _settings;

    private readonly UpDownCounter<int> _myGauge;
    private readonly Counter<long> _workCounter;
    private readonly Histogram<double> _workDuration;

    private readonly HttpClient _httpClient;

    public MyService(ILogger<MyService> logger, IDistributedTracing distributedTracing, IMeterFactory meterFactory, IOptions<MyServiceSettings> options, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _tracer = distributedTracing;
        _meter = meterFactory.Create(new MeterOptions(Startup.AssemblyName)
        {
            Version = Startup.AssemblyVersion,
            Tags = new TagList
                {
                    { "code.namespace", GetType().Namespace },
                    { "code.class", GetType().Name }
                }
        });
        _settings = options.Value;

        _myGauge = _meter.CreateUpDownCounter<int>("app_service_requests", "count", "Count of calls.");
        _workCounter = _meter.CreateCounter<long>("app_work_total", "operations", "Total work operations");
        _workDuration = _meter.CreateHistogram<double>("app_work_duration_seconds", "s", "Work duration");

        // Verify additive configuration
        var extraMeter = meterFactory.Create(new MeterOptions("Extra.Meter"));
        var extraCounter = extraMeter.CreateCounter<long>("extra_meter_counter");
        extraCounter.Add(1);

        _httpClient = httpClientFactory.CreateClient(nameof(MyService));
    }

    public async Task<string> DoWorkAsync(CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var activity = _tracer.StartActivity("DoWork");
        activity.SetTag("service.name", nameof(MyService));

        using (_logger.BeginScope("{TraceId}, {SpanId}", activity.TraceId, activity.SpanId))
        {
            _myGauge.Add(1);
            _workCounter.Add(1);

            try
            {
                _logger.LogInformation("Starting work");

                using var delayActivity = _tracer.StartActivity("SimulatedWork");
                delayActivity.SetTag("delay.seconds", 1);

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                var requestUri = "https://httpbin.org/ip";
                //HttpResponseMessage response = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseContentRead, cancellationToken);
                //response.EnsureSuccessStatusCode();
                //var content = await response.Content.ReadAsStringAsync();

                delayActivity.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
                activity.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);

                _logger.LogInformation("Work completed successfully");

                return requestUri;
            }
            catch (Exception ex)
            {
                activity.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                activity.SetTag("exception.type", ex.GetType().FullName);
                activity.SetTag("exception.message", ex.Message);
                activity.SetTag("exception.stacktrace", ex.StackTrace);
                activity.AddEvent("exception", DateTimeOffset.UtcNow);
                _logger.LogError(ex, "Work failed");
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
}

public static class StartupExtensions
{
    public static IServiceCollection AddMyService(this IServiceCollection serviceCollection, Action<MyServiceSettings>? setupAction = null)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.EnsureServicesRegistered(
            typeof(IDistributedTracing),
            typeof(IMeterFactory),
            typeof(IHttpClientFactory));

        serviceCollection.AddOptions<MyServiceSettings>()
            .BindConfiguration(MyServiceSettings.ConfigurationSectionName)
            .Configure<IConfiguration>((settings, configuration) =>
            {
                //settings.Enabled = FeatureFlags.GetFeatureFlag<MyServiceSettings>(configuration);
                settings.Enabled = true; // Simplified for test
                settings.ConnectionString = configuration.GetConnectionString(settings.ConnectionStringKey) ?? "mock";
            })
            .PostConfigure(settings =>
                settings.Enabled = false
            )
            .ValidateDataAnnotations()
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ConnectionString), "`ConnectionString` cannot be null.")
            .ValidateOnStart();

        if (setupAction != null)
        {
            serviceCollection.Configure(setupAction);
        }

        serviceCollection.AddScoped<IMyService, MyService>();

        return serviceCollection;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class NoNumericCharactersAttribute : ValidationAttribute
{
    public NoNumericCharactersAttribute() : base("The field {0} must not contain numeric characters.")
    {
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null or "")
        {
            return ValidationResult.Success;
        }

        if (value is string stringValue && !stringValue.Any(char.IsDigit))
        {
            return ValidationResult.Success;
        }

        var errorMessage = FormatErrorMessage(validationContext.DisplayName);
        return new ValidationResult(errorMessage, [validationContext.MemberName!]);
    }
}
