using System;

using Microsoft.Extensions.Logging;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis;

internal static partial class Log
{
    [LoggerMessage(
        EventId = LogEvents.ProtectionSucceeded,
        Level = LogLevel.Debug,
        Message = "Content protected successfully with {PurposeCount} purposes")]
    public static partial void ProtectionSucceeded(this ILogger logger, int purposeCount);

    [LoggerMessage(
        EventId = LogEvents.ProtectionFailed,
        Level = LogLevel.Error,
        Message = "Failed to protect content")]
    public static partial void ProtectionFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = LogEvents.UnprotectionSucceeded,
        Level = LogLevel.Debug,
        Message = "Content unprotected successfully with {PurposeCount} purposes")]
    public static partial void UnprotectionSucceeded(this ILogger logger, int purposeCount);

    [LoggerMessage(
        EventId = LogEvents.UnprotectionFailed,
        Level = LogLevel.Error,
        Message = "Failed to unprotect content")]
    public static partial void UnprotectionFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = LogEvents.HealthCheckSucceeded,
        Level = LogLevel.Debug,
        Message = "Data protection health check succeeded")]
    public static partial void HealthCheckSucceeded(this ILogger logger);

    [LoggerMessage(
        EventId = LogEvents.HealthCheckFailed,
        Level = LogLevel.Error,
        Message = "Data protection health check failed")]
    public static partial void HealthCheckFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = LogEvents.RedisConnectionFailed,
        Level = LogLevel.Error,
        Message = "Failed to connect to Redis ({ExceptionType})")]
    public static partial void RedisConnectionFailed(this ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = LogEvents.SettingsFetchFailed,
        Level = LogLevel.Error,
        Message = "Failed to fetch data protection settings from {Endpoint} ({ExceptionType})")]
    public static partial void SettingsFetchFailed(
        this ILogger logger,
        string endpoint,
        string exceptionType);

    [LoggerMessage(
        EventId = LogEvents.SettingsFetchSucceeded,
        Level = LogLevel.Information,
        Message = "Data protection settings fetched successfully from {Endpoint}")]
    public static partial void SettingsFetchSucceeded(this ILogger logger, string endpoint);
}
