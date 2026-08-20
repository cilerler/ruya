using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace Ruya.Diagnostics.DistributedTracing;

/// <summary>
/// - Use StartActivity for the initiator (stores to cache)
/// - Use ContinueActivity for followers (reads from cache, never stores)
/// </summary>
public interface IDistributedTracing
{
    /// <summary>
    /// Starts a new root activity. Optionally stores the trace context in distributed cache
    /// for other instances to continue the trace.
    /// Use this when YOU are the initiator of a distributed operation.
    /// </summary>
    /// <param name="activityName">Name of the activity/span.</param>
    /// <param name="activityKind">Kind of activity (Internal, Server, Client, Producer, Consumer).</param>
    /// <param name="parentId">Optional explicit W3C trace parent ID to chain from.</param>
    /// <param name="cacheKey">If provided, stores activity ID in distributed cache for other instances.</param>
    /// <param name="tags">Optional tags to add to the activity.</param>
    /// <returns>An ActivityScope that manages the activity lifecycle.</returns>
    ActivityScope StartActivity(
        string activityName,
        ActivityKind activityKind = ActivityKind.Internal,
        string? parentId = null,
        string? cacheKey = null,
        IEnumerable<KeyValuePair<string, object?>>? tags = null);

    /// <summary>
    /// Asynchronously starts a new root activity and stores its trace context when a cache key is supplied.
    /// Implementations should use non-blocking distributed-cache operations.
    /// </summary>
    ValueTask<ActivityScope> StartActivityAsync(
        string activityName,
        ActivityKind activityKind = ActivityKind.Internal,
        string? parentId = null,
        string? cacheKey = null,
        IEnumerable<KeyValuePair<string, object?>>? tags = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(StartActivity(activityName, activityKind, parentId, cacheKey, tags));
    }

    /// <summary>
    /// Continues an existing trace by looking up parent context from distributed cache.
    /// NEVER stores to cache - this prevents race conditions in multi-instance deployments.
    /// Use this when you are a FOLLOWER in a distributed operation.
    /// </summary>
    /// <param name="activityName">Name of the activity/span.</param>
    /// <param name="cacheKey">Cache key to look up the parent trace context.</param>
    /// <param name="activityKind">Kind of activity.</param>
    /// <param name="fallbackParentId">Optional parent ID to use if cache lookup fails.</param>
    /// <param name="tags">Optional tags to add to the activity.</param>
    /// <returns>An ActivityScope that manages the activity lifecycle.</returns>
    ActivityScope ContinueActivity(
        string activityName,
        string cacheKey,
        ActivityKind activityKind = ActivityKind.Internal,
        string? fallbackParentId = null,
        IEnumerable<KeyValuePair<string, object?>>? tags = null);

    /// <summary>
    /// Asynchronously continues an existing trace using a non-blocking distributed-cache lookup.
    /// </summary>
    ValueTask<ActivityScope> ContinueActivityAsync(
        string activityName,
        string cacheKey,
        ActivityKind activityKind = ActivityKind.Internal,
        string? fallbackParentId = null,
        IEnumerable<KeyValuePair<string, object?>>? tags = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ContinueActivity(activityName, cacheKey, activityKind, fallbackParentId, tags));
    }

    /// <summary>
    /// Creates a linked activity that references another trace without being a child.
    /// Useful for batch processing or fan-out scenarios where multiple traces converge.
    /// </summary>
    /// <param name="activityName">Name of the activity/span.</param>
    /// <param name="linkedContext">The ActivityContext to link to.</param>
    /// <param name="activityKind">Kind of activity.</param>
    /// <param name="tags">Optional tags to add to the activity.</param>
    /// <returns>An ActivityScope that manages the activity lifecycle.</returns>
    ActivityScope CreateLinkedActivity(
        string activityName,
        ActivityContext linkedContext,
        ActivityKind activityKind = ActivityKind.Internal,
        IEnumerable<KeyValuePair<string, object?>>? tags = null);
}
