using System;

using Microsoft.Extensions.Logging;

namespace Ruya.Services.TokenBroker;

internal static partial class Log
{
    [LoggerMessage(
        EventId = LogEvents.TokenCreationRejected,
        Level = LogLevel.Warning,
        Message = "Token creation rejected for service {ServiceName}: {Reason}")]
    public static partial void TokenCreationRejected(this ILogger logger, string serviceName, string reason);

    [LoggerMessage(
        EventId = LogEvents.ServiceNameMismatch,
        Level = LogLevel.Warning,
        Message = "Token request rejected: claimed service {ClaimedService} does not match registered service {RegisteredService}")]
    public static partial void ServiceNameMismatch(this ILogger logger, string claimedService, string registeredService);

    [LoggerMessage(
        EventId = LogEvents.DisallowedScopes,
        Level = LogLevel.Warning,
        Message = "Token creation rejected for service {ServiceName}: {DisallowedCount} disallowed scopes")]
    public static partial void DisallowedScopes(this ILogger logger, string serviceName, int disallowedCount);

    [LoggerMessage(
        EventId = LogEvents.TokenCreated,
        Level = LogLevel.Information,
        Message = "Token created for service {ServiceName}; scope count {ScopeCount}; expires {ExpiresAt}")]
    public static partial void TokenCreatedForService(
        this ILogger logger,
        string serviceName,
        int scopeCount,
        DateTimeOffset expiresAt);

    [LoggerMessage(
        EventId = LogEvents.TokenCreationFailed,
        Level = LogLevel.Warning,
        Message = "Token creation failed for service {ServiceName}: {ErrorCode}")]
    public static partial void TokenCreationFailed(
        this ILogger logger,
        string serviceName,
        string errorCode);

    [LoggerMessage(
        EventId = LogEvents.TokenExchangeRejected,
        Level = LogLevel.Warning,
        Message = "Token exchange rejected for service {ServiceName}: {Reason}")]
    public static partial void TokenExchangeRejected(this ILogger logger, string serviceName, string reason);

    [LoggerMessage(
        EventId = LogEvents.TokenExchanged,
        Level = LogLevel.Information,
        Message = "Token exchanged by service {ServiceName}; expires {ExpiresAt}")]
    public static partial void TokenExchangedForService(
        this ILogger logger,
        string serviceName,
        DateTimeOffset expiresAt);

    [LoggerMessage(
        EventId = LogEvents.TokenExchangeFailed,
        Level = LogLevel.Warning,
        Message = "Token exchange failed for service {ServiceName}: {Reason}")]
    public static partial void TokenExchangeFailed(
        this ILogger logger,
        string serviceName,
        string reason);

    [LoggerMessage(
        EventId = LogEvents.TokenCreated,
        Level = LogLevel.Debug,
        Message = "Token created; expires {ExpiresAt}")]
    public static partial void TokenCreated(this ILogger logger, DateTime expiresAt);

    [LoggerMessage(
        EventId = LogEvents.TokenExchanged,
        Level = LogLevel.Information,
        Message = "Token exchanged by actor service {ActorService}; expires {ExpiresAt}")]
    public static partial void TokenExchanged(this ILogger logger, string actorService, DateTime expiresAt);

    [LoggerMessage(
        EventId = LogEvents.TokenValidationFailed,
        Level = LogLevel.Warning,
        Message = "Token validation failed")]
    public static partial void TokenValidationFailed(this ILogger logger);

    [LoggerMessage(
        EventId = LogEvents.ApiKeyValidationFailed,
        Level = LogLevel.Warning,
        Message = "{Message}")]
    public static partial void ApiKeyValidationFailed(this ILogger logger, string message);

    [LoggerMessage(
        EventId = LogEvents.ApiKeyValidationError,
        Level = LogLevel.Error,
        Message = "Error validating API key: {ErrorType}")]
    public static partial void ApiKeyValidationError(this ILogger logger, string errorType);

    [LoggerMessage(
        EventId = LogEvents.ApiKeyValidated,
        Level = LogLevel.Debug,
        Message = "API key validated for service {ServiceName}")]
    public static partial void ApiKeyValidated(this ILogger logger, string serviceName);

    [LoggerMessage(
        EventId = LogEvents.PreviousApiKeyRemoved,
        Level = LogLevel.Information,
        Message = "Removed previous API key for service {ServiceName} during key rotation")]
    public static partial void RemovedPreviousApiKey(this ILogger logger, string serviceName);

    [LoggerMessage(
        EventId = LogEvents.PreviousApiKeyCleanupFailed,
        Level = LogLevel.Warning,
        Message = "Failed to clean up the previous API-key payload for service {ServiceName}; the credential is already invalidated by the active index; error type {ErrorType}")]
    public static partial void PreviousApiKeyCleanupFailed(this ILogger logger, string serviceName, string errorType);

    [LoggerMessage(
        EventId = LogEvents.ServiceRegistered,
        Level = LogLevel.Information,
        Message = "Registered service {ServiceName}")]
    public static partial void RegisteredService(this ILogger logger, string serviceName);

    [LoggerMessage(
        EventId = LogEvents.ServiceNotFoundForRemoval,
        Level = LogLevel.Warning,
        Message = "Cannot remove service {ServiceName}: not found in index")]
    public static partial void ServiceNotFoundForRemoval(this ILogger logger, string serviceName);

    [LoggerMessage(
        EventId = LogEvents.ServiceRemoved,
        Level = LogLevel.Information,
        Message = "Removed service {ServiceName}")]
    public static partial void RemovedService(this ILogger logger, string serviceName);

    [LoggerMessage(
        EventId = LogEvents.HealthCheckCacheMismatch,
        Level = LogLevel.Warning,
        Message = "Cache read/write mismatch in health check")]
    public static partial void HealthCheckCacheMismatch(this ILogger logger);

    [LoggerMessage(
        EventId = LogEvents.HealthCheckFailed,
        Level = LogLevel.Error,
        Message = "Token Service health check failed: {ErrorType}")]
    public static partial void HealthCheckFailed(this ILogger logger, string errorType);

    [LoggerMessage(
        EventId = LogEvents.HealthCheckCleanupFailed,
        Level = LogLevel.Debug,
        Message = "Failed to clean up health check key: {ErrorType}")]
    public static partial void HealthCheckCleanupFailed(this ILogger logger, string errorType);

    [LoggerMessage(
        EventId = LogEvents.ServiceRegistrationRollback,
        Level = LogLevel.Warning,
        Message = "Service registration rollback: removing registration after index write failure")]
    public static partial void ServiceRegistrationRollback(this ILogger logger);

    [LoggerMessage(
        EventId = LogEvents.ServiceRegistrationRollbackFailed,
        Level = LogLevel.Error,
        Message = "Service registration rollback failed: {ErrorType}")]
    public static partial void ServiceRegistrationRollbackFailed(this ILogger logger, string errorType);
}
