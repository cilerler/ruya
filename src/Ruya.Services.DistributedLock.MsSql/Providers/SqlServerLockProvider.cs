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
    private readonly TimeProvider _timeProvider;
    private readonly Action? _applicationLockAcquired;
    private readonly Func<CancellationToken, Task>? _ownershipVerificationDelay;
    private readonly ConcurrentDictionary<string, LockInfo> _activeLocks = new();
    private int _disposeState;

    private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

    private sealed class LockInfo
    {
        public required SqlConnection Connection { get; init; }
        public required string LockValue { get; init; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public ITimer? ExpiryTimer { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerLockProvider"/> class.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="logger">The logger instance.</param>
    public SqlServerLockProvider(string connectionString, ILogger<SqlServerLockProvider> logger)
        : this(connectionString, logger, TimeProvider.System)
    {
    }

    internal SqlServerLockProvider(
        string connectionString,
        ILogger<SqlServerLockProvider> logger,
        TimeProvider timeProvider,
        Action? applicationLockAcquired = null,
        Func<CancellationToken, Task>? ownershipVerificationDelay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _connectionString = CreateLockSessionConnectionString(connectionString);
        _logger = logger;
        _timeProvider = timeProvider;
        _applicationLockAcquired = applicationLockAcquired;
        _ownershipVerificationDelay = ownershipVerificationDelay;
    }

    internal static string CreateLockSessionConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                // sp_getapplock uses Session ownership. An unconfirmed release therefore must
                // close the physical SQL session, not return it to an application-shared pool.
                Pooling = false
            };
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            // Preserve the released direct-constructor behavior: invalid syntax is reported
            // through the normal acquisition failure/logging path. Valid configured values
            // always use the unpooled session policy above.
            return connectionString;
        }
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
        ExpiryValidation.Validate(expiry);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        SqlConnection? connection = null;
        bool applicationLockAcquired = false;
        try
        {
            connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "sp_getapplock";
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.Add("@Resource", SqlDbType.NVarChar, LockValidation.MaxLockKeyLength).Value = lockKey;
            command.Parameters.Add("@LockMode", SqlDbType.VarChar, 32).Value = "Exclusive";
            command.Parameters.Add("@LockOwner", SqlDbType.VarChar, 32).Value = "Session";
            command.Parameters.Add("@LockTimeout", SqlDbType.Int).Value = 0;

            SqlParameter returnValue = command.Parameters.Add("@ReturnValue", SqlDbType.Int);
            returnValue.Direction = ParameterDirection.ReturnValue;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            int result = (int)returnValue.Value;

            if (result < 0)
            {
                _logger.LogDebug(
                    LogEvents.AcquireRejected,
                    "Failed to acquire SQL Server lock for key: {LockKey}, Result: {Result}",
                    lockKey,
                    result);
                await CloseConnectionAsync(connection, lockKey).ConfigureAwait(false);
                connection = null;
                return false;
            }

            applicationLockAcquired = true;
            _applicationLockAcquired?.Invoke();

            // A cancellation can arrive after SQL Server grants the session lock. Do not
            // publish the acquisition unless the caller still wants it; closing the session
            // in the catch path releases the application lock.
            cancellationToken.ThrowIfCancellationRequested();

            var lockInfo = new LockInfo
            {
                Connection = connection,
                LockValue = lockValue,
                ExpiresAt = _timeProvider.GetUtcNow().Add(expiry)
            };
            lockInfo.ExpiryTimer = _timeProvider.CreateTimer(
                _ => _ = AutoReleaseLockAsync(lockKey, lockInfo),
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);

            if (!_activeLocks.TryAdd(lockKey, lockInfo))
            {
                await lockInfo.ExpiryTimer.DisposeAsync().ConfigureAwait(false);
                await ReleaseOwnedSessionAndCloseAsync(connection, lockKey, CancellationToken.None).ConfigureAwait(false);
                connection = null;
                return false;
            }

            await lockInfo.Gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (cancellationToken.IsCancellationRequested || IsDisposed)
                {
                    await AbandonLockUnderGateAsync(lockKey, lockInfo).ConfigureAwait(false);
                    connection = null;
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new ObjectDisposedException(nameof(SqlServerLockProvider));
                }

                TimeSpan remaining = lockInfo.ExpiresAt - _timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero ||
                    !lockInfo.ExpiryTimer.Change(remaining, Timeout.InfiniteTimeSpan))
                {
                    await AbandonLockUnderGateAsync(lockKey, lockInfo).ConfigureAwait(false);
                    connection = null;
                    return false;
                }

                connection = null; // Ownership transferred to LockInfo.
            }
            finally
            {
                lockInfo.Gate.Release();
            }

            _logger.LogDebug(LogEvents.Acquired, "Acquired SQL Server lock for key: {LockKey}", lockKey);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(LogEvents.AcquireFailed, ex, "Error acquiring SQL Server lock for key: {LockKey}", lockKey);
            throw;
        }
        finally
        {
            if (connection is not null)
            {
                if (applicationLockAcquired)
                {
                    await ReleaseOwnedSessionAndCloseAsync(connection, lockKey, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    await CloseConnectionAsync(connection, lockKey).ConfigureAwait(false);
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExtendLockAsync(
        string lockKey,
        string lockValue,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);
        ExpiryValidation.Validate(expiry);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_activeLocks.TryGetValue(lockKey, out LockInfo? lockInfo))
        {
            return false;
        }

        await lockInfo.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrentLock(lockKey, lockInfo) || lockInfo.LockValue != lockValue)
            {
                return false;
            }

            if (_ownershipVerificationDelay is not null)
            {
                await _ownershipVerificationDelay(cancellationToken).ConfigureAwait(false);
            }

            if (_timeProvider.GetUtcNow() >= lockInfo.ExpiresAt)
            {
                await AbandonLockUnderGateAsync(lockKey, lockInfo).ConfigureAwait(false);
                return false;
            }

            if (lockInfo.Connection.State != ConnectionState.Open)
            {
                await AbandonLockUnderGateAsync(lockKey, lockInfo).ConfigureAwait(false);
                return false;
            }

            string? lockMode;
            try
            {
                lockMode = await GetApplicationLockModeAsync(
                    lockInfo.Connection,
                    lockKey,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(LogEvents.OwnershipVerificationFailed, ex, "Error verifying SQL Server lock ownership for key: {LockKey}", lockKey);
                await AbandonLockUnderGateAsync(lockKey, lockInfo).ConfigureAwait(false);
                throw;
            }

            if (!string.Equals(lockMode, "Exclusive", StringComparison.OrdinalIgnoreCase))
            {
                await AbandonLockUnderGateAsync(lockKey, lockInfo).ConfigureAwait(false);
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            lockInfo.ExpiresAt = _timeProvider.GetUtcNow().Add(expiry);
            if (lockInfo.ExpiryTimer?.Change(expiry, Timeout.InfiniteTimeSpan) != true)
            {
                await AbandonLockUnderGateAsync(lockKey, lockInfo).ConfigureAwait(false);
                return false;
            }

            _logger.LogDebug(LogEvents.Extended, "Extended SQL Server lock for key: {LockKey}", lockKey);
            return true;
        }
        finally
        {
            lockInfo.Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseLockAsync(
        string lockKey,
        string lockValue,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        LockValidation.ValidateLockValue(lockValue);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_activeLocks.TryGetValue(lockKey, out LockInfo? lockInfo))
        {
            return false;
        }

        return await ReleaseLockInfoAsync(
            lockKey,
            lockInfo,
            lockValue,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> LockExistsAsync(
        string lockKey,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_activeLocks.TryGetValue(lockKey, out LockInfo? lockInfo))
        {
            return false;
        }

        await lockInfo.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrentLock(lockKey, lockInfo))
            {
                return false;
            }

            if (_timeProvider.GetUtcNow() >= lockInfo.ExpiresAt)
            {
                await AbandonLockUnderGateAsync(lockKey, lockInfo).ConfigureAwait(false);
                return false;
            }

            if (lockInfo.Connection.State != ConnectionState.Open)
            {
                await AbandonLockUnderGateAsync(lockKey, lockInfo).ConfigureAwait(false);
                return false;
            }

            string? mode;
            try
            {
                mode = await GetApplicationLockModeAsync(
                    lockInfo.Connection,
                    lockKey,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(LogEvents.OwnershipVerificationFailed, ex, "Error verifying SQL Server lock ownership for key: {LockKey}", lockKey);
                await AbandonLockUnderGateAsync(lockKey, lockInfo).ConfigureAwait(false);
                throw;
            }
            if (string.Equals(mode, "Exclusive", StringComparison.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return true;
            }

            await AbandonLockUnderGateAsync(lockKey, lockInfo).ConfigureAwait(false);
            return false;
        }
        finally
        {
            lockInfo.Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ForceReleaseLockAsync(
        string lockKey,
        CancellationToken cancellationToken = default)
    {
        LockValidation.ValidateLockKey(lockKey);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_activeLocks.TryGetValue(lockKey, out LockInfo? lockInfo))
        {
            return false;
        }

        return await ReleaseLockInfoAsync(
            lockKey,
            lockInfo,
            requiredLockValue: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string GetProviderName() => "SqlServer";

    private async Task<bool> ReleaseLockInfoAsync(
        string lockKey,
        LockInfo lockInfo,
        string? requiredLockValue,
        CancellationToken cancellationToken)
    {
        await lockInfo.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrentLock(lockKey, lockInfo) ||
                requiredLockValue is not null && lockInfo.LockValue != requiredLockValue)
            {
                return false;
            }

            if (!_activeLocks.TryRemove(new KeyValuePair<string, LockInfo>(lockKey, lockInfo)))
            {
                return false;
            }

            StopTimerUnderGate(lockInfo);
            bool releaseConfirmed = await ReleaseOwnedSessionAndCloseAsync(
                lockInfo.Connection,
                lockKey,
                cancellationToken).ConfigureAwait(false);

            if (releaseConfirmed)
            {
                _logger.LogDebug(LogEvents.Released, "Released SQL Server lock for key: {LockKey}", lockKey);
            }

            return releaseConfirmed;
        }
        finally
        {
            lockInfo.Gate.Release();
        }
    }

    private async Task AutoReleaseLockAsync(string lockKey, LockInfo lockInfo)
    {
        await lockInfo.Gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!IsCurrentLock(lockKey, lockInfo))
            {
                return;
            }

            TimeSpan remaining = lockInfo.ExpiresAt - _timeProvider.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                // A timer callback can already be queued while an extension resets the
                // timer. Re-check under the same gate and reschedule instead of releasing.
                lockInfo.ExpiryTimer?.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }

            _logger.LogInformation(LogEvents.AutoReleaseStarted, "Auto-releasing expired SQL Server lock for key: {LockKey}", lockKey);
            await AbandonLockUnderGateAsync(lockKey, lockInfo).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(LogEvents.AutoReleaseFailed, ex, "Error auto-releasing SQL Server lock for key: {LockKey}", lockKey);
        }
        finally
        {
            lockInfo.Gate.Release();
        }
    }

    private bool IsCurrentLock(string lockKey, LockInfo lockInfo) =>
        _activeLocks.TryGetValue(lockKey, out LockInfo? current) && ReferenceEquals(current, lockInfo);

    private async Task AbandonLockUnderGateAsync(string lockKey, LockInfo lockInfo)
    {
        _activeLocks.TryRemove(new KeyValuePair<string, LockInfo>(lockKey, lockInfo));
        StopTimerUnderGate(lockInfo);
        await ReleaseOwnedSessionAndCloseAsync(lockInfo.Connection, lockKey, CancellationToken.None).ConfigureAwait(false);
    }

    private static void StopTimerUnderGate(LockInfo lockInfo)
    {
        // DisposeAsync can wait for a callback that is itself waiting on LockInfo.Gate,
        // so synchronous non-blocking disposal is intentional while this gate is held.
#pragma warning disable CA1849, S6966
        lockInfo.ExpiryTimer?.Dispose();
#pragma warning restore CA1849, S6966
    }

    private static async Task<string?> GetApplicationLockModeAsync(
        SqlConnection connection,
        string lockKey,
        CancellationToken cancellationToken)
    {
        using SqlCommand command = connection.CreateCommand();
        command.CommandText = SqlResources.GetApplicationLockMode;
        command.CommandType = CommandType.Text;
        command.Parameters.Add("@Resource", SqlDbType.NVarChar, LockValidation.MaxLockKeyLength).Value = lockKey;
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is DBNull or null
            ? null
            : Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ReleaseApplicationLockAsync(
        SqlConnection connection,
        string lockKey,
        CancellationToken cancellationToken)
    {
        using SqlCommand command = connection.CreateCommand();
        command.CommandText = "sp_releaseapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add("@Resource", SqlDbType.NVarChar, LockValidation.MaxLockKeyLength).Value = lockKey;
        command.Parameters.Add("@LockOwner", SqlDbType.VarChar, 32).Value = "Session";

        SqlParameter returnValue = command.Parameters.Add("@ReturnValue", SqlDbType.Int);
        returnValue.Direction = ParameterDirection.ReturnValue;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return (int)returnValue.Value >= 0;
    }

    private async Task<bool> ReleaseOwnedSessionAndCloseAsync(
        SqlConnection connection,
        string lockKey,
        CancellationToken cancellationToken)
    {
        bool releaseConfirmed = false;
        try
        {
            if (connection.State == ConnectionState.Open)
            {
                releaseConfirmed = await ReleaseApplicationLockAsync(
                    connection,
                    lockKey,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                LogEvents.ReleaseFailed,
                ex,
                "Error releasing SQL Server lock for key: {LockKey}",
                lockKey);
        }
        finally
        {
            // Lock-session connections have pooling disabled, so Close/Dispose logs out
            // the physical session even when explicit release could not be confirmed.
            await CloseConnectionAsync(connection, lockKey).ConfigureAwait(false);
        }

        return releaseConfirmed;
    }

    private async Task CloseConnectionAsync(
        SqlConnection connection,
        string lockKey)
    {
        try
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupException)
        {
            _logger.LogWarning(
                LogEvents.SessionCloseFailed,
                cleanupException,
                "Error closing SQL Server lock session for key: {LockKey}",
                lockKey);
        }

        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupException)
        {
            _logger.LogWarning(
                LogEvents.SessionDisposeFailed,
                cleanupException,
                "Error disposing SQL Server lock session for key: {LockKey}",
                lockKey);
        }

    }

    private void ReleaseOwnedSessionAndClose(string lockKey, SqlConnection connection)
    {
        try
        {
            if (connection.State == ConnectionState.Open)
            {
                using SqlCommand command = connection.CreateCommand();
                command.CommandText = "sp_releaseapplock";
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.Add("@Resource", SqlDbType.NVarChar, LockValidation.MaxLockKeyLength).Value = lockKey;
                command.Parameters.Add("@LockOwner", SqlDbType.VarChar, 32).Value = "Session";
                SqlParameter returnValue = command.Parameters.Add("@ReturnValue", SqlDbType.Int);
                returnValue.Direction = ParameterDirection.ReturnValue;
                command.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                LogEvents.ReleaseFailed,
                ex,
                "Error releasing SQL Server lock for key: {LockKey}",
                lockKey);
        }
        finally
        {
            try
            {
                connection.Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    LogEvents.ProviderDisposeFailed,
                    ex,
                    "Error closing SQL Server lock session for key: {LockKey}",
                    lockKey);
            }

            try
            {
                connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    LogEvents.ProviderDisposeFailed,
                    ex,
                    "Error disposing SQL Server lock session for key: {LockKey}",
                    lockKey);
            }
        }

    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        foreach (KeyValuePair<string, LockInfo> entry in _activeLocks)
        {
            LockInfo lockInfo = entry.Value;
            // IDisposable is a synchronous boundary. Wait for the same per-lock gate used by
            // extension, release, ownership checks, and timer expiry before touching the
            // session. This prevents Dispose from closing a connection beneath an in-flight
            // operation.
            lockInfo.Gate.Wait();
            try
            {
                if (!_activeLocks.TryRemove(new KeyValuePair<string, LockInfo>(entry.Key, lockInfo)))
                {
                    continue;
                }

                lockInfo.ExpiryTimer?.Dispose();
                ReleaseOwnedSessionAndClose(entry.Key, lockInfo.Connection);
            }
            catch (Exception ex)
            {
                _logger.LogError(LogEvents.ProviderDisposeFailed, ex, "Error disposing SQL Server lock for key: {LockKey}", entry.Key);
            }
            finally
            {
                lockInfo.Gate.Release();
            }
        }
    }
}
