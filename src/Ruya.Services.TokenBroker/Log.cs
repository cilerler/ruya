using System;

using Microsoft.Extensions.Logging;

namespace Ruya.Services.TokenBroker;

internal static partial class Log
{
    [LoggerMessage(
        EventId = LogEvents.TokenCreated,
        Level = LogLevel.Debug,
        Message = "Token created: {Jti} for {Subject}, expires {ExpiresAt}")]
    public static partial void TokenCreated(this ILogger logger, string jti, string subject, DateTime expiresAt);

    [LoggerMessage(
        EventId = LogEvents.TokenExchanged,
        Level = LogLevel.Information,
        Message = "Token exchanged: {Jti} for {Subject}, actor {Actor}, expires {ExpiresAt}")]
    public static partial void TokenExchanged(this ILogger logger, string jti, string subject, string actor, DateTime expiresAt);

    [LoggerMessage(
        EventId = LogEvents.TokenValidationFailed,
        Level = LogLevel.Warning,
        Message = "Token validation failed")]
    public static partial void TokenValidationFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = LogEvents.ApiKeyValidationFailed,
        Level = LogLevel.Warning,
        Message = "{Message}")]
    public static partial void ApiKeyValidationFailed(this ILogger logger, string message);

    [LoggerMessage(
        EventId = LogEvents.ApiKeyValidationError,
        Level = LogLevel.Error,
        Message = "Error validating API key")]
    public static partial void ApiKeyValidationError(this ILogger logger, Exception ex);

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
        Message = "Token Service health check failed")]
    public static partial void HealthCheckFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = LogEvents.HealthCheckCleanupFailed,
        Level = LogLevel.Debug,
        Message = "Failed to clean up health check key")]
    public static partial void HealthCheckCleanupFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = LogEvents.ServiceRegistrationRollback,
        Level = LogLevel.Warning,
        Message = "Service registration rollback: removing registration after index write failure")]
    public static partial void ServiceRegistrationRollback(this ILogger logger);

    [LoggerMessage(
        EventId = LogEvents.ServiceRegistrationRollbackFailed,
        Level = LogLevel.Error,
        Message = "Service registration rollback failed")]
    public static partial void ServiceRegistrationRollbackFailed(this ILogger logger, Exception ex);
}
