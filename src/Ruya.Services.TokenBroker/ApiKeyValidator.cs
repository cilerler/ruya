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

using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Abstractions.Models;
using Ruya.Services.TokenBroker.Contracts;
using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker;

public sealed class ApiKeyValidator : IApiKeyValidator
{
    private static readonly LockOptions RotationLockOptions = new()
    {
        CustomExpiry = TimeSpan.FromSeconds(30),
        EnableHeartbeat = true,
        HeartbeatInterval = TimeSpan.FromSeconds(10)
    };

    private readonly IDistributedCache _cache;
    private readonly IDistributedLock? _distributedLock;
    private readonly ILogger<ApiKeyValidator> _logger;
    private readonly TokenBrokerSettings _settings;
    private readonly Counter<long> _validationCounter;
    private readonly Counter<long> _validationFailureCounter;
    private readonly Counter<long> _registrationCounter;
    private readonly Counter<long> _removalCounter;

    [Obsolete("Register an IDistributedLock and use the constructor that accepts it. Registration and rotation fail closed without a distributed lock.")]
    public ApiKeyValidator(
        ILogger<ApiKeyValidator> logger,
        IMeterFactory meterFactory,
        IOptions<TokenBrokerSettings> options,
        IDistributedCache distributedCache)
        : this(logger, meterFactory, options, distributedCache, distributedLock: null)
    {
    }

    public ApiKeyValidator(
        ILogger<ApiKeyValidator> logger,
        IMeterFactory meterFactory,
        IOptions<TokenBrokerSettings> options,
        IDistributedCache distributedCache,
        IDistributedLock? distributedLock)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(distributedCache);

        _logger = logger;
        _settings = options.Value;
        _cache = distributedCache;
        _distributedLock = distributedLock;

        var meter = meterFactory.Create(MetricConstants.MeterName);
        _validationCounter = meter.CreateCounter<long>(
            MetricConstants.ApiKeyValidations, "validations", "Total API key validation attempts");
        _validationFailureCounter = meter.CreateCounter<long>(
            MetricConstants.ApiKeyValidationFailures, "failures", "Total API key validation failures");
        _registrationCounter = meter.CreateCounter<long>(
            MetricConstants.ServiceRegistrations, "registrations", "Total service registrations");
        _removalCounter = meter.CreateCounter<long>(
            MetricConstants.ServiceRemovals, "removals", "Total service removals");
    }

    public async Task<ServiceRegistration?> ValidateApiKeyAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        cancellationToken.ThrowIfCancellationRequested();
        _validationCounter.Add(1);

        var apiKeyHash = HashApiKey(apiKey);
        var cacheKey = GetApiKeyCacheKey(apiKeyHash);
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached is null)
            {
                return ValidationFailed("key not found");
            }

            var registration = JsonSerializer.Deserialize(
                cached,
                TokenBrokerJsonSerializerContext.Default.ServiceRegistration);
            if (registration is null || string.IsNullOrWhiteSpace(registration.ServiceName))
            {
                return ValidationFailed("invalid registration data");
            }

            var currentHash = await _cache.GetStringAsync(
                GetServiceIndexKey(registration.ServiceName),
                cancellationToken);
            if (currentHash is null || !FixedTimeEquals(currentHash, apiKeyHash))
            {
                return ValidationFailed("key is no longer current");
            }

            _logger.ApiKeyValidated(registration.ServiceName);
            return registration;
        }
        catch (JsonException)
        {
            return ValidationFailed("invalid registration data");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _validationFailureCounter.Add(1);
            _logger.ApiKeyValidationError(ex.GetType().Name);
            throw;
        }
    }

    public async Task RegisterServiceAsync(
        ServiceRegistration registration,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.ServiceName);
        cancellationToken.ThrowIfCancellationRequested();

        var distributedLock = GetRequiredDistributedLock();
        var apiKeyHash = HashApiKey(apiKey);
        var cacheKey = GetApiKeyCacheKey(apiKeyHash);
        var indexKey = GetServiceIndexKey(registration.ServiceName);
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _settings.ApiKeyCacheDuration
        };

        var result = await distributedLock.AcquireAndExecuteWithLockAsync(
            async lockCancellationToken =>
            {
                using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    lockCancellationToken,
                    cancellationToken);
                var operationToken = operationCancellation.Token;
                var existingApiKeyHash = await _cache.GetStringAsync(indexKey, operationToken);
                var storedRegistration = registration with { ApiKeyHash = apiKeyHash };
                var json = JsonSerializer.Serialize(
                    storedRegistration,
                    TokenBrokerJsonSerializerContext.Default.ServiceRegistration);

                var isSameCredential = existingApiKeyHash is not null
                    && FixedTimeEquals(existingApiKeyHash, apiKeyHash);

                await _cache.SetStringAsync(cacheKey, json, cacheOptions, operationToken);
                if (isSameCredential)
                {
                    await _cache.SetStringAsync(indexKey, apiKeyHash, cacheOptions, operationToken);
                }
                else
                {
                    try
                    {
                        await _cache.SetStringAsync(indexKey, apiKeyHash, cacheOptions, operationToken);
                    }
                    catch
                    {
                        _logger.ServiceRegistrationRollback();
                        await TryRemoveUncommittedPayloadAsync(indexKey, cacheKey, apiKeyHash);
                        throw;
                    }
                }

                if (existingApiKeyHash is not null && !isSameCredential)
                {
                    try
                    {
                        await _cache.RemoveAsync(GetApiKeyCacheKey(existingApiKeyHash), operationToken);
                        _logger.RemovedPreviousApiKey(registration.ServiceName);
                    }
                    catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.PreviousApiKeyCleanupFailed(registration.ServiceName, ex.GetType().Name);
                    }
                }
            },
            GetRegistrationLockKey(registration.ServiceName),
            lockValue: null,
            options: RotationLockOptions,
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("Could not acquire the distributed service-registration lock.");
        }

        _registrationCounter.Add(1);
        _logger.RegisteredService(registration.ServiceName);
    }

    public async Task RemoveServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        cancellationToken.ThrowIfCancellationRequested();

        var distributedLock = GetRequiredDistributedLock();
        var removed = false;
        var result = await distributedLock.AcquireAndExecuteWithLockAsync(
            async lockCancellationToken =>
            {
                using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    lockCancellationToken,
                    cancellationToken);
                var operationToken = operationCancellation.Token;
                var indexKey = GetServiceIndexKey(serviceName);
                var apiKeyHash = await _cache.GetStringAsync(indexKey, operationToken);
                if (apiKeyHash is null)
                {
                    _logger.ServiceNotFoundForRemoval(serviceName);
                    return;
                }

                // Removing the index first makes the credential unusable before best-effort payload cleanup.
                await _cache.RemoveAsync(indexKey, operationToken);
                await _cache.RemoveAsync(GetApiKeyCacheKey(apiKeyHash), operationToken);
                removed = true;
            },
            GetRegistrationLockKey(serviceName),
            lockValue: null,
            options: RotationLockOptions,
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("Could not acquire the distributed service-registration lock.");
        }

        if (removed)
        {
            _removalCounter.Add(1);
            _logger.RemovedService(serviceName);
        }
    }

    private ServiceRegistration? ValidationFailed(string reason)
    {
        _validationFailureCounter.Add(1);
        _logger.ApiKeyValidationFailed(reason);
        return null;
    }

    private IDistributedLock GetRequiredDistributedLock()
    {
        return _distributedLock
            ?? throw new InvalidOperationException(
                "Atomic API-key registration and rotation require an IDistributedLock registration.");
    }

    private async Task TryRemoveUncommittedPayloadAsync(
        string indexKey,
        string cacheKey,
        string proposedApiKeyHash)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var currentApiKeyHash = await _cache.GetStringAsync(indexKey, cleanupTimeout.Token);
            if (currentApiKeyHash is not null && FixedTimeEquals(currentApiKeyHash, proposedApiKeyHash))
            {
                // The distributed write may have committed before the client observed its failure.
                // Preserve the matching payload so the active index never points to a missing credential.
                return;
            }

            await _cache.RemoveAsync(cacheKey, cleanupTimeout.Token);
        }
        catch (Exception ex)
        {
            _logger.ServiceRegistrationRollbackFailed(ex.GetType().Name);
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string HashApiKey(string apiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToBase64String(bytes);
    }

    private static string GetApiKeyCacheKey(string apiKeyHash) =>
        $"{Constants.CacheKeys.ApiKeysPrefix}{apiKeyHash}";

    private static string GetServiceIndexKey(string serviceName) =>
        $"{Constants.CacheKeys.ServiceNameIndexPrefix}{serviceName.ToUpperInvariant()}";

    private static string GetRegistrationLockKey(string serviceName) =>
        $"token-service:service-registration-lock:{serviceName.ToUpperInvariant()}";
}
