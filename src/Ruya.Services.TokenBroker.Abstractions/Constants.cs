using System;
using System.Text.Json;

namespace Ruya.Services.TokenBroker;

public static class Constants
{
    /// <summary>
    /// Shared JSON serializer options retained for 8.x compatibility.
    /// </summary>
    [Obsolete("Use TokenBrokerJsonSerializerContext.Default and the generated JsonTypeInfo properties.")]
    public static readonly JsonSerializerOptions JsonSerializerOptions = new(TokenBrokerJsonSerializerContext.Default.Options);

    public const string ActorClaimType = "act";
    public const string OriginalSubjectClaimType = "original_sub";
    public const string ScopeClaimType = "scope";
    public const string ApiKeyHeader = "X-Api-Key";
    public const string ServiceNameHeader = "X-Service-Name";

    public static class CacheKeys
    {
        public const string ApiKeysPrefix = "token-service:api-keys:";
        public const string ServiceNameIndexPrefix = "token-service:service-index:";

        /// <summary>
        /// Released single-key health-check name retained for source compatibility.
        /// </summary>
        [Obsolete("Use HealthCheckPrefix and append a unique operation identifier.")]
        public const string HealthCheck = "token-service:health-check";

        public const string HealthCheckPrefix = "token-service:health-check:";
    }

    public static class Defaults
    {
        public static readonly TimeSpan HealthCheckKeyExpiry = TimeSpan.FromSeconds(10);
        public const int MaximumTokenSizeInBytes = 32 * 1024;
        public const int MaximumActorChainDepth = 8;
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
