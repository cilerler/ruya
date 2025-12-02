using System;

namespace Ruya.Services.DistributedLock.Abstractions.Models;

/// <summary>
/// Represents the result of a lock operation.
/// Implements the Result pattern for explicit success/failure handling.
/// </summary>
public sealed class LockResult
{
    /// <summary>
    /// Gets the status of the lock operation.
    /// </summary>
    public LockStatus Status { get; }

    /// <summary>
    /// Gets a value indicating whether the lock operation was successful.
    /// </summary>
    public bool IsSuccess => Status == LockStatus.Success;

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; }

    private LockResult(LockStatus status, string? errorMessage = null)
    {
        Status = status;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Creates a successful lock result.
    /// </summary>
    public static LockResult Succeeded() => new(LockStatus.Success);

    /// <summary>
    /// Creates a failed lock result.
    /// </summary>
    /// <param name="status">The failure status.</param>
    /// <param name="errorMessage">Optional error message.</param>
    public static LockResult Failed(LockStatus status, string? errorMessage = null)
    {
        if (status == LockStatus.Success)
        {
            throw new ArgumentException("Cannot create a failed result with Success status.", nameof(status));
        }

        return new LockResult(status, errorMessage);
    }

    /// <summary>
    /// Deconstructs the result into status and error message.
    /// </summary>
    public void Deconstruct(out bool isSuccess, out LockStatus status, out string? errorMessage)
    {
        isSuccess = IsSuccess;
        status = Status;
        errorMessage = ErrorMessage;
    }
}
