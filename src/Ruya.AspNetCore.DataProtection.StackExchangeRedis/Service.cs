using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ruya.AspNetCore.DataProtection.StackExchangeRedis.Contracts;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.Primitives;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis;

/// <summary>
/// Provides data protection services using ASP.NET Core Data Protection with Redis key storage.
/// </summary>
public sealed class DataProtectionService : IDataProtection
{
    /// <summary>
    /// The default purpose key used when no specific purposes are provided.
    /// </summary>
    public const string DefaultPurpose = "Default";

    private readonly ILogger<DataProtectionService> _logger;
    private readonly IDistributedTracing _tracer;
    private readonly DataProtectionSettings _settings;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ConcurrentDictionary<string, IDataProtector> _protectorsCache = new();

    private readonly Counter<long> _protectOperations;
    private readonly Counter<long> _unprotectOperations;
    private readonly Counter<long> _protectFailures;
    private readonly Counter<long> _unprotectFailures;
    private readonly Histogram<double> _operationDuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataProtectionService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="distributedTracing">The distributed tracing service.</param>
    /// <param name="meterFactory">The meter factory for creating metrics.</param>
    /// <param name="options">The data protection settings.</param>
    /// <param name="dataProtectionProvider">The underlying data protection provider.</param>
    public DataProtectionService(
        ILogger<DataProtectionService> logger,
        IDistributedTracing distributedTracing,
        IMeterFactory meterFactory,
        IOptions<DataProtectionSettings> options,
        IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(distributedTracing);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        _logger = logger;
        _tracer = distributedTracing;
        _settings = options.Value;
        _dataProtectionProvider = dataProtectionProvider;

        // Meter lifecycle is managed by the IMeterFactory, so disposal is not required here
#pragma warning disable CA2000
        var meter = meterFactory.Create(new MeterOptions(Startup.AssemblyName)
        {
            Version = Startup.AssemblyVersion,
            Tags = new TagList
            {
                { "code.namespace", GetType().Namespace },
                { "code.class", GetType().Name }
            }
        });
#pragma warning restore CA2000
        _protectOperations = meter.CreateCounter<long>(
            Constants.ProtectOperations, "{operation}", "Total protect operations");
        _unprotectOperations = meter.CreateCounter<long>(
            Constants.UnprotectOperations, "{operation}", "Total unprotect operations");
        _protectFailures = meter.CreateCounter<long>(
            Constants.ProtectFailures, "{failure}", "Total protect failures");
        _unprotectFailures = meter.CreateCounter<long>(
            Constants.UnprotectFailures, "{failure}", "Total unprotect failures");
        _operationDuration = meter.CreateHistogram<double>(
            Constants.OperationDuration, "s", "Operation duration in seconds");
    }

    /// <inheritdoc />
    public string Protect(string content, IEnumerable<string>? purposes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var purposeList = GetPurposeList(purposes);
        using var activityScope = _tracer.StartActivity(
            "DataProtection.Protect",
            ActivityKind.Internal,
            tags: new KeyValuePair<string, object?>[]
            {
                new("dataprotection.purpose_count", purposeList.Length)
            });

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var dataProtector = GetDataProtector(purposeList);
            var result = dataProtector.Protect(content);

            _protectOperations.Add(1);
            _logger.ProtectionSucceeded(purposeList.Length);
            activityScope.SetStatus(ActivityStatusCode.Ok);

            return result;
        }
        catch (Exception ex)
        {
            _protectFailures.Add(1);
            _logger.ProtectionFailed(ex);
            activityScope.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _operationDuration.Record(stopwatch.Elapsed.TotalSeconds);
        }
    }

    /// <inheritdoc />
    public string Unprotect(string content, IEnumerable<string>? purposes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var purposeList = GetPurposeList(purposes);
        using var activityScope = _tracer.StartActivity(
            "DataProtection.Unprotect",
            ActivityKind.Internal,
            tags: new KeyValuePair<string, object?>[]
            {
                new("dataprotection.purpose_count", purposeList.Length)
            });

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var dataProtector = GetDataProtector(purposeList);
            var result = dataProtector.Unprotect(content);

            _unprotectOperations.Add(1);
            _logger.UnprotectionSucceeded(purposeList.Length);
            activityScope.SetStatus(ActivityStatusCode.Ok);

            return result;
        }
        catch (Exception ex)
        {
            _unprotectFailures.Add(1);
            _logger.UnprotectionFailed(ex);
            activityScope.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _operationDuration.Record(stopwatch.Elapsed.TotalSeconds);
        }
    }

    private string[] GetPurposeList(IEnumerable<string>? purposes)
    {
        var purposeArray = purposes?.ToArray();
        if (purposeArray is null || purposeArray.Length == 0)
        {
            if (_settings.Purposes.TryGetValue(DefaultPurpose, out var defaultPurpose))
            {
                return [defaultPurpose];
            }

            return [DefaultPurpose];
        }

        return purposeArray;
    }

    private IDataProtector GetDataProtector(string[] purposes)
    {
        var key = string.Join(";", purposes);
        return _protectorsCache.GetOrAdd(key, _ => _dataProtectionProvider.CreateProtector(purposes));
    }
}
