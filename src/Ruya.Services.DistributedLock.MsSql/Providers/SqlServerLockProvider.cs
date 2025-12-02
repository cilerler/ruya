using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.DistributedLock.Common;

namespace Ruya.Services.DistributedLock.MsSql.Providers;

/// <summary>
/// SQL Server-based implementation of distributed lock provider.
/// Uses sp_getapplock/sp_releaseapplock for application-level locks.
/// </summary>
public sealed class SqlServerLockProvider : IDistributedLockProvider, IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<SqlServerLockProvider> _logger;
    private readonly ConcurrentDictionary<string, LockInfo> _activeLocks = new();
    private bool _disposed;

    private sealed class LockInfo
    {
        private DateTimeOffset _expiresAt;
        private readonly object _lock = new();

        public SqlConnection? Connection { get; set; }
        public string LockValue { get; init; } = string.Empty;
        public Timer? ExpiryTimer { get; set; }

        public DateTimeOffset ExpiresAt
        {
            get
            {
                lock (_lock)
                {
                    return _expiresAt;
                }
            }
            set
            {
                lock (_lock)
                {
                    _expiresAt = value;
                }
            }
        }

        public bool IsExpired() => DateTimeOffset.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerLockProvider"/> class.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="logger">The logger instance.</param>
    public SqlServerLockProvider(string connectionString, ILogger<SqlServerLockProvider> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionString = connectionString;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> AcquireLockAsync(
        string lockKey,
        string lockValue,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);
        ObjectDisposedException.ThrowIf(_disposed, this);

        SqlConnection? connection = null;
        try
        {
            connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Try to acquire the lock using sp_getapplock
            await using var command = connection.CreateCommand();
            command.CommandText = "sp_getapplock";
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.AddWithValue("@Resource", lockKey);
            command.Parameters.AddWithValue("@LockMode", "Exclusive");
            command.Parameters.AddWithValue("@LockOwner", "Session");
            command.Parameters.AddWithValue("@LockTimeout", 0); // Don't wait

            var returnValue = command.Parameters.Add("@ReturnValue", SqlDbType.Int);
            returnValue.Direction = ParameterDirection.ReturnValue;

            await command.ExecuteNonQueryAsync(cancellationToken);

            var result = (int)returnValue.Value;

            // Return values: 0 = success, 1 = success after waiting (not applicable with timeout=0)
            // -1 = timeout, -2 = canceled, -3 = deadlock, -999 = parameter/other error
            if (result >= 0)
            {
                var expiresAt = DateTimeOffset.UtcNow.Add(expiry);

                // Create a timer to auto-release the lock after expiry
                // Use synchronous callback to avoid async void issues
                var timer = new Timer(
                    _ =>
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await AutoReleaseLockAsync(lockKey, lockValue);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex,
                                    "Unhandled exception in auto-release timer for key: {LockKey}",
                                    lockKey);
                            }
                        });
                    },
                    null,
                    expiry,
                    Timeout.InfiniteTimeSpan);

                var lockInfo = new LockInfo
                {
                    Connection = connection,
                    LockValue = lockValue,
                    ExpiresAt = expiresAt,
                    ExpiryTimer = timer
                };

                _activeLocks[lockKey] = lockInfo;
                _logger.LogDebug("Acquired SQL Server lock for key: {LockKey}", lockKey);
                return true;
            }

            _logger.LogDebug("Failed to acquire SQL Server lock for key: {LockKey}, Result: {Result}", lockKey, result);
            await connection.CloseAsync();
            await connection.DisposeAsync();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring SQL Server lock for key: {LockKey}", lockKey);

            if (connection != null)
            {
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }

            throw;
        }
    }

    /// <inheritdoc />
    public Task<bool> ExtendLockAsync(
        string lockKey,
        string lockValue,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_activeLocks.TryGetValue(lockKey, out var lockInfo))
        {
            return Task.FromResult(false);
        }

        // Verify the lock value matches and hasn't expired
        // The Value comparison is safe because it's init-only
        // The IsExpired() method is thread-safe due to the property lock
        if (lockInfo.LockValue != lockValue || lockInfo.IsExpired())
        {
            return Task.FromResult(false);
        }

        // Update expiry
        lockInfo.ExpiresAt = DateTimeOffset.UtcNow.Add(expiry);

        // Reset the timer
        lockInfo.ExpiryTimer?.Change(expiry, Timeout.InfiniteTimeSpan);

        _logger.LogDebug("Extended SQL Server lock for key: {LockKey}", lockKey);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseLockAsync(
        string lockKey,
        string lockValue,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // First check if the lock exists and the value matches before removing
        if (!_activeLocks.TryGetValue(lockKey, out var lockInfo))
        {
            return false;
        }

        // Verify the lock value matches before attempting removal
        if (lockInfo.LockValue != lockValue)
        {
            return false;
        }

        // Now try to remove it atomically - use the same instance we validated
        if (!_activeLocks.TryRemove(new KeyValuePair<string, LockInfo>(lockKey, lockInfo)))
        {
            // Someone else modified it between our check and removal
            // Still dispose the timer to prevent resource leak
            lockInfo.ExpiryTimer?.Dispose();
            _logger.LogWarning(
                "Failed to atomically remove lock. Another thread may have modified it. [LockKey = {LockKey}]",
                lockKey);
            return false;
        }

        try
        {
            // Dispose the timer
            lockInfo.ExpiryTimer?.Dispose();

            if (lockInfo.Connection?.State == ConnectionState.Open)
            {
                // Release the lock using sp_releaseapplock
                await using var command = lockInfo.Connection.CreateCommand();
                command.CommandText = "sp_releaseapplock";
                command.CommandType = CommandType.StoredProcedure;

                command.Parameters.AddWithValue("@Resource", lockKey);
                command.Parameters.AddWithValue("@LockOwner", "Session");

                await command.ExecuteNonQueryAsync(cancellationToken);

                _logger.LogDebug("Released SQL Server lock for key: {LockKey}", lockKey);
            }

            if (lockInfo.Connection != null)
            {
                await lockInfo.Connection.CloseAsync();
                await lockInfo.Connection.DisposeAsync();
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing SQL Server lock for key: {LockKey}", lockKey);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<bool> LockExistsAsync(
        string lockKey,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check if lock exists and hasn't expired
        var exists = _activeLocks.TryGetValue(lockKey, out var lockInfo) && !lockInfo.IsExpired();

        return Task.FromResult(exists);
    }

    /// <inheritdoc />
    public string GetProviderName() => "SqlServer";

    private async Task AutoReleaseLockAsync(string lockKey, string lockValue)
    {
        try
        {
            _logger.LogInformation("Auto-releasing expired SQL Server lock for key: {LockKey}", lockKey);
            await ReleaseLockAsync(lockKey, lockValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-releasing SQL Server lock for key: {LockKey}", lockKey);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // Release all active locks
        foreach (var kvp in _activeLocks)
        {
            try
            {
                kvp.Value.ExpiryTimer?.Dispose();
                kvp.Value.Connection?.Close();
                kvp.Value.Connection?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing SQL Server lock for key: {LockKey}", kvp.Key);
            }
        }

        _activeLocks.Clear();
    }
}
