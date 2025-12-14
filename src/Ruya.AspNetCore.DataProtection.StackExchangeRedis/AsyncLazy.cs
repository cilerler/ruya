using System;
using System.Threading.Tasks;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis;

/// <summary>
/// Provides support for asynchronous lazy initialization.
/// </summary>
/// <typeparam name="T">The type of object that is being lazily initialized.</typeparam>
public sealed class AsyncLazy<T>
{
    private readonly Lazy<Task<T>> _lazy;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncLazy{T}"/> class.
    /// </summary>
    /// <param name="factory">The asynchronous factory method to create the value.</param>
    public AsyncLazy(Func<Task<T>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _lazy = new Lazy<Task<T>>(factory);
    }

    /// <summary>
    /// Gets a value indicating whether the value has been created.
    /// </summary>
    public bool IsValueCreated => _lazy.IsValueCreated && _lazy.Value.IsCompletedSuccessfully;

    /// <summary>
    /// Gets the lazily initialized value.
    /// </summary>
    public Task<T> Value => _lazy.Value;

    /// <summary>
    /// Gets the value if it has been created and completed successfully, otherwise returns default.
    /// </summary>
    public T? ValueOrDefault => IsValueCreated ? _lazy.Value.Result : default;
}
