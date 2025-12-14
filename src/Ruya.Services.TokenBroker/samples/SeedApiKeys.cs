// SeedApiKeys.cs - Run this once to register service API keys in Redis
//
// ============================================================================
// WARNING: EXAMPLE CODE ONLY - DO NOT USE IN PRODUCTION AS-IS
// ============================================================================
// The API keys below are for demonstration purposes only.
// For production use:
//   1. Generate cryptographically secure API keys: openssl rand -base64 32
//   2. Store API keys securely (Azure Key Vault, AWS Secrets Manager, etc.)
//   3. Never commit real API keys to source control
//   4. Rotate keys regularly according to your security policy
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using Ruya.Services.TokenBroker;
using Ruya.Services.TokenBroker.Models;

// Configure Redis connection
var redisOptions = Options.Create(new RedisCacheOptions
{
    Configuration = "localhost:6379" // Change to your Redis connection
});

var cache = new RedisCache(redisOptions);

// Define services and their API keys
// WARNING: These are example keys for development only - generate secure keys for production!
// Generate with: openssl rand -base64 32
var services = new[]
{
    new
    {
        ServiceName = "service-a",
        ApiKey = "service-a-api-key-12345", // WARNING: Replace with secure key in production
        AllowedScopes = new[] { "read:orders", "write:orders" },
        CanExchangeTokens = true
    },
    new
    {
        ServiceName = "service-b",
        ApiKey = "service-b-api-key-67890", // WARNING: Replace with secure key in production
        AllowedScopes = new[] { "read:orders", "read:inventory" },
        CanExchangeTokens = true
    },
    new
    {
        ServiceName = "service-c",
        ApiKey = "service-c-api-key-abcde", // WARNING: Replace with secure key in production
        AllowedScopes = new[] { "read:inventory" },
        CanExchangeTokens = false // Terminal service, doesn't need to exchange
    }
};

foreach (var service in services)
{
    var apiKeyHash = HashApiKey(service.ApiKey);
    var cacheKey = $"{Constants.CacheKeys.ApiKeysPrefix}{apiKeyHash}";

    var registration = new ServiceRegistration
    {
        ServiceName = service.ServiceName,
        ApiKeyHash = apiKeyHash,
        AllowedScopes = service.AllowedScopes,
        CanExchangeTokens = service.CanExchangeTokens
    };

    var json = JsonSerializer.Serialize(registration, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    await cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(365)
    });

    Console.WriteLine($"Registered: {service.ServiceName}");
    Console.WriteLine($"  API Key Hash: {apiKeyHash}");
    Console.WriteLine($"  Allowed Scopes: {string.Join(", ", service.AllowedScopes)}");
    Console.WriteLine($"  Can Exchange: {service.CanExchangeTokens}");
    Console.WriteLine();
}

Console.WriteLine("Done! API keys registered in Redis.");

static string HashApiKey(string apiKey)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
    return Convert.ToBase64String(bytes);
}
