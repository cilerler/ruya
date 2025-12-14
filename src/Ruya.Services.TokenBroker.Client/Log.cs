using Microsoft.Extensions.Logging;
using System;

namespace Ruya.Services.TokenBroker.Client;

internal static partial class Log
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Failed to get token from service")]
    public static partial void FailedToGetToken(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Failed to get token from service: {StatusCode} {Content}")]
    public static partial void FailedToGetTokenStatus(this ILogger logger, System.Net.HttpStatusCode statusCode, string content);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Obtained new token for {ServiceName}, expires {ExpiresAt}")]
    public static partial void ObtainedNewToken(this ILogger logger, string serviceName, DateTime expiresAt);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Failed to exchange token")]
    public static partial void FailedToExchangeToken(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Error,
        Message = "Failed to exchange token: {StatusCode} {Content}")]
    public static partial void FailedToExchangeTokenStatus(this ILogger logger, System.Net.HttpStatusCode statusCode, string content);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Debug,
        Message = "Exchanged token, new token expires {ExpiresAt}")]
    public static partial void ExchangedToken(this ILogger logger, DateTime expiresAt);
}
