using System;
using System.Threading.Tasks;

using StackExchange.Redis;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis;

internal sealed class RedisConnectionLifetime : IDisposable, IAsyncDisposable
{
    private readonly Func<Task<IConnectionMultiplexer>> _connectionFactory;
    private readonly object _sync = new();
    private ConnectionAttempt? _currentAttempt;
    private IConnectionMultiplexer? _ownedConnection;
    private bool _disposed;
    private bool _projectedToContainer;

    public RedisConnectionLifetime(Func<Task<IConnectionMultiplexer>> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
        Connection = new AsyncLazy<IConnectionMultiplexer>(StartConnectionAttempt);
    }

    public AsyncLazy<IConnectionMultiplexer> Connection { get; }

    public IConnectionMultiplexer GetContainerOwnedConnection()
    {
        var connection = Connection.Value.GetAwaiter().GetResult();

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _projectedToContainer = true;
            _ownedConnection = null;
            return connection;
        }
    }

    public void Dispose()
    {
        IConnectionMultiplexer? connection;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            connection = _projectedToContainer ? null : _ownedConnection;
            _ownedConnection = null;
        }

        connection?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        IConnectionMultiplexer? connection;
        Task? pendingCleanup;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            connection = _projectedToContainer ? null : _ownedConnection;
            _ownedConnection = null;
            pendingCleanup = !_projectedToContainer &&
                _currentAttempt is { Cleanup.IsCompleted: false } attempt
                    ? attempt.Cleanup
                    : null;
        }

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        if (pendingCleanup is not null)
        {
            await pendingCleanup.ConfigureAwait(false);
        }
    }

    private Task<IConnectionMultiplexer> StartConnectionAttempt()
    {
        ConnectionAttempt attempt;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            attempt = new ConnectionAttempt();
            _currentAttempt = attempt;
        }

        _ = RunConnectionAttemptAsync(attempt);
        return attempt.Connection;
    }

    private async Task RunConnectionAttemptAsync(ConnectionAttempt attempt)
    {
        IConnectionMultiplexer connection;
        try
        {
            connection = await _connectionFactory().ConfigureAwait(false);
        }
        // CA1031: This detached runner must transfer every factory failure to its owned task.
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            bool disposed;
            lock (_sync)
            {
                disposed = _disposed;
            }

            attempt.CompleteWithoutConnection(ex, disposed);
            ClearCurrentAttempt(attempt);
            return;
        }

        bool disposeConnection;
        lock (_sync)
        {
            disposeConnection = _disposed;
            if (!disposeConnection)
            {
                _ownedConnection = connection;
            }
        }

        if (disposeConnection)
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                attempt.CompleteAfterDisposal();
            }
            // CA1031: Disposal failures must reach the provider's DisposeAsync task.
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                attempt.CompleteAfterDisposalFailure(ex);
            }
        }
        else
        {
            attempt.CompleteWithConnection(connection);
        }

        ClearCurrentAttempt(attempt);
    }

    private void ClearCurrentAttempt(ConnectionAttempt attempt)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_currentAttempt, attempt))
            {
                _currentAttempt = null;
            }
        }
    }

    private sealed class ConnectionAttempt
    {
        private readonly TaskCompletionSource _cleanup =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<IConnectionMultiplexer> _connection =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Cleanup => _cleanup.Task;

        public Task<IConnectionMultiplexer> Connection => _connection.Task;

        public void CompleteWithConnection(IConnectionMultiplexer connection)
        {
            _cleanup.TrySetResult();
            _connection.TrySetResult(connection);
        }

        public void CompleteWithoutConnection(Exception exception, bool disposed)
        {
            _cleanup.TrySetResult();
            if (disposed)
            {
                _connection.TrySetCanceled();
            }
            else
            {
                _connection.TrySetException(exception);
            }
        }

        public void CompleteAfterDisposal()
        {
            _cleanup.TrySetResult();
            _connection.TrySetCanceled();
        }

        public void CompleteAfterDisposalFailure(Exception exception)
        {
            _cleanup.TrySetException(exception);
            _connection.TrySetCanceled();
        }
    }
}
