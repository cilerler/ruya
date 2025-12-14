using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ruya.Services.TokenBroker;

public static class Constants
{
    /// <summary>
    /// Shared JSON serializer options for consistent serialization across all Token Service components.
    /// </summary>
    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public const string ActorClaimType = "act";
    public const string OriginalSubjectClaimType = "original_sub";
    public const string ScopeClaimType = "scope";
    public const string ApiKeyHeader = "X-Api-Key";
    public const string ServiceNameHeader = "X-Service-Name";

    public static class CacheKeys
    {
        public const string ApiKeysPrefix = "token-service:api-keys:";
        public const string ServiceNameIndexPrefix = "token-service:service-index:";
        public const string HealthCheck = "token-service:health-check";
    }

    public static class Defaults
    {
        public static readonly TimeSpan HealthCheckKeyExpiry = TimeSpan.FromSeconds(10);
    }

    public static class Errors
    {
        public const string InvalidApiKey = "INVALID_API_KEY";
        public const string InvalidToken = "INVALID_TOKEN";
        public const string TokenExpired = "TOKEN_EXPIRED";
        public const string MissingSubject = "MISSING_SUBJECT";
        public const string ExchangeNotAllowed = "EXCHANGE_NOT_ALLOWED";
    }
}
