namespace Ruya.Services.TokenBroker;

/// <summary>
/// Centralized log event IDs for the Token Broker service.
/// These constants can be used with LoggerMessage attributes and for log filtering.
/// </summary>
public static class LogEvents
{
    // Token operations (1xxx)
    /// <summary>Token created successfully.</summary>
    public const int TokenCreated = 1001;

    /// <summary>Token exchanged successfully.</summary>
    public const int TokenExchanged = 1002;

    /// <summary>Token validation failed.</summary>
    public const int TokenValidationFailed = 1003;

    /// <summary>Token creation was rejected.</summary>
    public const int TokenCreationRejected = 1004;

    /// <summary>Token creation failed.</summary>
    public const int TokenCreationFailed = 1005;

    /// <summary>Token exchange was rejected.</summary>
    public const int TokenExchangeRejected = 1006;

    /// <summary>Token exchange failed.</summary>
    public const int TokenExchangeFailed = 1007;

    /// <summary>Service name mismatch detected.</summary>
    public const int ServiceNameMismatch = 1008;

    /// <summary>Disallowed scopes were requested.</summary>
    public const int DisallowedScopes = 1009;

    // API key validation (2xxx)
    /// <summary>API key validation failed.</summary>
    public const int ApiKeyValidationFailed = 2001;

    /// <summary>API key validated successfully.</summary>
    public const int ApiKeyValidated = 2002;

    /// <summary>API key validation encountered an error.</summary>
    public const int ApiKeyValidationError = 2003;

    // Service registration (4xxx)
    /// <summary>Previous API key removed during key rotation.</summary>
    public const int PreviousApiKeyRemoved = 4001;

    /// <summary>Service registered successfully.</summary>
    public const int ServiceRegistered = 4002;

    /// <summary>Service not found for removal.</summary>
    public const int ServiceNotFoundForRemoval = 4003;

    /// <summary>Service removed successfully.</summary>
    public const int ServiceRemoved = 4004;

    // Health check (5xxx)
    /// <summary>Health check cache mismatch.</summary>
    public const int HealthCheckCacheMismatch = 5001;

    /// <summary>Health check failed.</summary>
    public const int HealthCheckFailed = 5002;

    /// <summary>Health check cleanup failed.</summary>
    public const int HealthCheckCleanupFailed = 5003;

    // Cache operations (6xxx)
    /// <summary>Service registration rollback initiated.</summary>
    public const int ServiceRegistrationRollback = 6005;

    /// <summary>Service registration rollback failed.</summary>
    public const int ServiceRegistrationRollbackFailed = 6006;
}
