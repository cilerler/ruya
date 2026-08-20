using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ruya.Services.TokenBroker;
using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker.Client;

public sealed class TokenClient : ITokenClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TokenClient> _logger;
    private readonly TokenClientSettings _settings;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private readonly Counter<long> _tokenRequestsCounter;
    private readonly Counter<long> _tokenRequestFailuresCounter;
    private readonly Counter<long> _tokenCacheHitsCounter;
    private readonly Counter<long> _tokenExchangesCounter;
    private readonly Counter<long> _tokenExchangeFailuresCounter;
    private readonly Histogram<double> _tokenRequestDuration;

    public TokenClient(
        HttpClient httpClient,
        IMemoryCache cache,
        ILogger<TokenClient> logger,
        IOptions<TokenClientSettings> options,
        IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(meterFactory);

        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _settings = options.Value;

        _httpClient.BaseAddress = new Uri(_settings.TokenBrokerUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Add(Constants.ApiKeyHeader, _settings.ApiKey);
        _httpClient.DefaultRequestHeaders.Add(Constants.ServiceNameHeader, _settings.ServiceName);

        var meter = meterFactory.Create(MetricConstants.ClientMeterName);
        _tokenRequestsCounter = meter.CreateCounter<long>(
            MetricConstants.ClientRequests, "requests", "Total token requests");
        _tokenRequestFailuresCounter = meter.CreateCounter<long>(
            MetricConstants.ClientRequestFailures, "failures", "Total token request failures");
        _tokenCacheHitsCounter = meter.CreateCounter<long>(
            MetricConstants.ClientCacheHits, "hits", "Total token cache hits");
        _tokenExchangesCounter = meter.CreateCounter<long>(
            MetricConstants.ClientExchanges, "exchanges", "Total token exchanges");
        _tokenExchangeFailuresCounter = meter.CreateCounter<long>(
            MetricConstants.ClientExchangeFailures, "failures", "Total token exchange failures");
        _tokenRequestDuration = meter.CreateHistogram<double>(
            MetricConstants.ClientRequestDuration, "s", "Token request duration");
    }

    public async Task<string> GetTokenAsync(string[]? scopes = null, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Validate scopes if provided
        if (scopes is not null && scopes.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Scopes array cannot contain null or whitespace values.", nameof(scopes));
        }

        var cacheKey = $"token:{_settings.ServiceName}:{string.Join(",", scopes ?? [])}";

        // First check without lock for fast path (skip if force refresh)
        if (!forceRefresh && _cache.TryGetValue<CachedToken>(cacheKey, out var cached) && cached is not null)
        {
            if (cached.ExpiresAt > DateTimeOffset.UtcNow.Add(_settings.TokenRefreshBuffer))
            {
                _tokenCacheHitsCounter.Add(1);
                return cached.AccessToken;
            }
        }

        // Acquire lock to prevent concurrent token requests for the same cache key
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock - another thread may have already refreshed (skip if force refresh)
            if (!forceRefresh && _cache.TryGetValue<CachedToken>(cacheKey, out cached) && cached is not null)
            {
                if (cached.ExpiresAt > DateTimeOffset.UtcNow.Add(_settings.TokenRefreshBuffer))
                {
                    _tokenCacheHitsCounter.Add(1);
                    return cached.AccessToken;
                }
            }

            _tokenRequestsCounter.Add(1);
            var stopwatch = Stopwatch.StartNew();

            var request = new CreateTokenApiRequest
            {
                Scopes = scopes?.ToList()
            };

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsJsonAsync(
                    "api/v1/token", request, TokenBrokerJsonSerializerContext.Default.CreateTokenApiRequest, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _tokenRequestFailuresCounter.Add(1);
                _logger.FailedToGetToken(ex.GetType().Name);
                throw;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _tokenRequestFailuresCounter.Add(1);
                _logger.FailedToGetToken(ex.GetType().Name);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                _tokenRequestDuration.Record(stopwatch.Elapsed.TotalSeconds);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    _tokenRequestFailuresCounter.Add(1);
                    _logger.FailedToGetTokenStatus(response.StatusCode);
                    response.EnsureSuccessStatusCode();
                }

                var tokenResponse = await response.Content.ReadFromJsonAsync(
                    TokenBrokerJsonSerializerContext.Default.TokenResponse, cancellationToken)
                    ?? throw new InvalidOperationException("Failed to deserialize token response");

                _cache.Set(cacheKey, new CachedToken(tokenResponse.AccessToken, tokenResponse.ExpiresAtUtc),
                    tokenResponse.ExpiresAtUtc.Subtract(_settings.TokenRefreshBuffer));

                _logger.ObtainedNewToken(_settings.ServiceName, tokenResponse.ExpiresAtUtc);

                return tokenResponse.AccessToken;
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<string> ExchangeTokenAsync(string originalToken, string[]? narrowedScopes = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Validate narrowed scopes if provided
        if (narrowedScopes is not null && narrowedScopes.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("NarrowedScopes array cannot contain null or whitespace values.", nameof(narrowedScopes));
        }

        _tokenExchangesCounter.Add(1);
        var stopwatch = Stopwatch.StartNew();

        var request = new ExchangeTokenApiRequest
        {
            Token = originalToken,
            NarrowedScopes = narrowedScopes?.ToList()
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(
                "api/v1/token/exchange", request, TokenBrokerJsonSerializerContext.Default.ExchangeTokenApiRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _tokenExchangeFailuresCounter.Add(1);
            _logger.FailedToExchangeToken(ex.GetType().Name);
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _tokenExchangeFailuresCounter.Add(1);
            _logger.FailedToExchangeToken(ex.GetType().Name);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _tokenRequestDuration.Record(stopwatch.Elapsed.TotalSeconds);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _tokenExchangeFailuresCounter.Add(1);
                _logger.FailedToExchangeTokenStatus(response.StatusCode);
                response.EnsureSuccessStatusCode();
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync(
                TokenBrokerJsonSerializerContext.Default.TokenResponse, cancellationToken)
                ?? throw new InvalidOperationException("Failed to deserialize token response");

            _logger.ExchangedToken(tokenResponse.ExpiresAtUtc);

            return tokenResponse.AccessToken;
        }
    }

    public void Dispose()
    {
        _tokenLock.Dispose();
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);
}
