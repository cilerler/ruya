using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis;

/// <summary>
/// Provides support for asynchronous lazy initialization.
/// </summary>
/// <typeparam name="T">The type of object that is being lazily initialized.</typeparam>
public sealed class AsyncLazy<T>
{
    private readonly Func<Task<T>> _factory;
    private Lazy<Task<T>> _lazy;
    private T? _completedValue;
    private int _hasCompletedValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncLazy{T}"/> class.
    /// </summary>
    /// <param name="factory">The asynchronous factory method to create the value.</param>
    public AsyncLazy(Func<Task<T>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _lazy = CreateLazy();
    }

    private Lazy<Task<T>> CreateLazy()
    {
        Lazy<Task<T>>? owner = null;
        owner = new Lazy<Task<T>>(
            () => ExecuteFactoryAsync(owner!),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return owner;
    }

    private async Task<T> ExecuteFactoryAsync(Lazy<Task<T>> owner)
    {
        try
        {
            var value = await _factory().ConfigureAwait(false);
            _completedValue = value;
            Volatile.Write(ref _hasCompletedValue, 1);
            return value;
        }
        catch
        {
            _ = Interlocked.CompareExchange(ref _lazy, CreateLazy(), owner);
            throw;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the value has been created.
    /// </summary>
    public bool IsValueCreated => Volatile.Read(ref _hasCompletedValue) == 1;

    /// <summary>
    /// Gets the lazily initialized value.
    /// </summary>
    public Task<T> Value => Volatile.Read(ref _lazy).Value;

    /// <summary>
    /// Gets the value if it has been created and completed successfully, otherwise returns default.
    /// </summary>
    public T? ValueOrDefault => Volatile.Read(ref _hasCompletedValue) == 1 ? _completedValue : default;
}
