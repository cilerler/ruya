using System;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ruya.Services.TokenBroker.Contracts;
using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker;

public sealed class ApiKeyValidator : IApiKeyValidator
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<ApiKeyValidator> _logger;
    private readonly TokenBrokerSettings _settings;
    private readonly Counter<long> _validationCounter;
    private readonly Counter<long> _validationFailureCounter;
    private readonly Counter<long> _registrationCounter;
    private readonly Counter<long> _removalCounter;

    public ApiKeyValidator(
        ILogger<ApiKeyValidator> logger,
        IMeterFactory meterFactory,
        IOptions<TokenBrokerSettings> options,
        IDistributedCache distributedCache)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(distributedCache);

        _logger = logger;
        _settings = options.Value;
        _cache = distributedCache;

        var meter = meterFactory.Create(MetricConstants.MeterName);
        _validationCounter = meter.CreateCounter<long>(
            MetricConstants.ApiKeyValidations,
            "validations",
            "Total API key validation attempts");
        _validationFailureCounter = meter.CreateCounter<long>(
            MetricConstants.ApiKeyValidationFailures,
            "failures",
            "Total API key validation failures");
        _registrationCounter = meter.CreateCounter<long>(
            MetricConstants.ServiceRegistrations,
            "registrations",
            "Total service registrations");
        _removalCounter = meter.CreateCounter<long>(
            MetricConstants.ServiceRemovals,
            "removals",
            "Total service removals");
    }

    public async Task<ServiceRegistration?> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _validationCounter.Add(1);

        var apiKeyHash = HashApiKey(apiKey);
        var cacheKey = $"{Constants.CacheKeys.ApiKeysPrefix}{apiKeyHash}";

        try
        {
            var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);

            if (cached is null)
            {
                _validationFailureCounter.Add(1);
                _logger.ApiKeyValidationFailed("key not found");
                return null;
            }

            var registration = JsonSerializer.Deserialize<ServiceRegistration>(cached, Constants.JsonSerializerOptions);

            if (registration is null)
            {
                _validationFailureCounter.Add(1);
                _logger.ApiKeyValidationFailed("invalid registration data");
                return null;
            }

            _logger.ApiKeyValidated(registration.ServiceName);
            return registration;
        }
        catch (Exception ex)
        {
            _validationFailureCounter.Add(1);
            _logger.ApiKeyValidationError(ex);
            throw;
        }
    }

    public async Task RegisterServiceAsync(ServiceRegistration registration, string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.ServiceName);

        var apiKeyHash = HashApiKey(apiKey);
        var cacheKey = $"{Constants.CacheKeys.ApiKeysPrefix}{apiKeyHash}";
        var indexKey = $"{Constants.CacheKeys.ServiceNameIndexPrefix}{registration.ServiceName.ToUpperInvariant()}";

        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _settings.ApiKeyCacheDuration
        };

        // Clean up any existing registration for this service (handles key rotation)
        var existingApiKeyHash = await _cache.GetStringAsync(indexKey, cancellationToken);
        if (existingApiKeyHash is not null && existingApiKeyHash != apiKeyHash)
        {
            var oldCacheKey = $"{Constants.CacheKeys.ApiKeysPrefix}{existingApiKeyHash}";
            await _cache.RemoveAsync(oldCacheKey, cancellationToken);
            _logger.RemovedPreviousApiKey(registration.ServiceName);
        }

        var registrationWithHash = registration with { ApiKeyHash = apiKeyHash };
        var json = JsonSerializer.Serialize(registrationWithHash, Constants.JsonSerializerOptions);

        // Store registration by API key hash first
        await _cache.SetStringAsync(cacheKey, json, cacheOptions, cancellationToken);

        // Store service name to API key hash index for removal support
        // Use compensating transaction pattern: if this fails, rollback the first write
        try
        {
            await _cache.SetStringAsync(indexKey, apiKeyHash, cacheOptions, cancellationToken);
        }
        catch
        {
            // Compensate: remove the registration we just wrote
            _logger.ServiceRegistrationRollback();
            try
            {
                await _cache.RemoveAsync(cacheKey, CancellationToken.None);
            }
            catch (Exception rollbackEx)
            {
                _logger.ServiceRegistrationRollbackFailed(rollbackEx);
            }
            throw;
        }

        _registrationCounter.Add(1);
        _logger.RegisteredService(registration.ServiceName);
    }

    public async Task RemoveServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var indexKey = $"{Constants.CacheKeys.ServiceNameIndexPrefix}{serviceName.ToUpperInvariant()}";

        // Look up the API key hash from the service name index
        var apiKeyHash = await _cache.GetStringAsync(indexKey, cancellationToken);

        if (apiKeyHash is null)
        {
            _logger.ServiceNotFoundForRemoval(serviceName);
            return;
        }

        var cacheKey = $"{Constants.CacheKeys.ApiKeysPrefix}{apiKeyHash}";

        // Remove both the registration and the index entry
        // Order: remove registration first (more important), then index
        await _cache.RemoveAsync(cacheKey, cancellationToken);
        await _cache.RemoveAsync(indexKey, cancellationToken);

        _removalCounter.Add(1);
        _logger.RemovedService(serviceName);
    }

    private static string HashApiKey(string apiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToBase64String(bytes);
    }
}
