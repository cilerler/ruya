using Microsoft.Extensions.Logging;
using System;

namespace Ruya.Services.TokenBroker.Client;

internal static partial class Log
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Failed to get token from service: {ErrorType}")]
    public static partial void FailedToGetToken(this ILogger logger, string errorType);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Failed to get token from service: HTTP {StatusCode}")]
    public static partial void FailedToGetTokenStatus(this ILogger logger, System.Net.HttpStatusCode statusCode);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Obtained new token for {ServiceName}, expires {ExpiresAt}")]
    public static partial void ObtainedNewToken(this ILogger logger, string serviceName, DateTimeOffset expiresAt);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Failed to exchange token: {ErrorType}")]
    public static partial void FailedToExchangeToken(this ILogger logger, string errorType);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Error,
        Message = "Failed to exchange token: HTTP {StatusCode}")]
    public static partial void FailedToExchangeTokenStatus(this ILogger logger, System.Net.HttpStatusCode statusCode);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Debug,
        Message = "Exchanged token, new token expires {ExpiresAt}")]
    public static partial void ExchangedToken(this ILogger logger, DateTimeOffset expiresAt);
}
