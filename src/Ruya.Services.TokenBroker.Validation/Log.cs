using Microsoft.Extensions.Logging;

namespace Ruya.Services.TokenBroker;

internal static partial class ValidationLog
{
    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Warning,
        Message = "JWT authentication failed")]
    public static partial void JwtAuthenticationFailed(this ILogger logger);
}
