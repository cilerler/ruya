namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis;

/// <summary>
/// Constants for metrics and HTTP client.
/// </summary>
internal static class Constants
{
    public const string ProtectOperations = "dataprotection.protect.operations";
    public const string UnprotectOperations = "dataprotection.unprotect.operations";
    public const string ProtectFailures = "dataprotection.protect.failures";
    public const string UnprotectFailures = "dataprotection.unprotect.failures";
    public const string OperationDuration = "dataprotection.operation.duration";

}

/// <summary>
/// Event IDs for structured logging.
/// </summary>
internal static class LogEvents
{
    public const int ProtectionSucceeded = 1001;
    public const int ProtectionFailed = 1002;
    public const int UnprotectionSucceeded = 1003;
    public const int UnprotectionFailed = 1004;
    public const int HealthCheckSucceeded = 1005;
    public const int HealthCheckFailed = 1006;
    public const int RedisConnectionFailed = 1007;
    public const int SettingsFetchFailed = 1008;
    public const int SettingsFetchSucceeded = 1009;
}
