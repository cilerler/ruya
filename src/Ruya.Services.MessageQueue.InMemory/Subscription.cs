using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Serialization;
using Ruya.Services.MessageQueue.Middleware;

using Ruya.Services.MessageQueue.Utilities;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;

namespace Ruya.Services.MessageQueue.InMemory;

/// <summary>
/// Represents an active in-memory subscription
/// </summary>
internal sealed class InMemorySubscription<TMessage> : IMessageSubscription where TMessage : class
{
    private readonly string _topic;
    private readonly string _consumerGroup;
    private readonly ChannelReader<MessageWrapper> _channelReader;
    private readonly Func<MessageContext<TMessage>, Task<MessageResult>> _handler;
    private readonly SubscribeOptions? _options;
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly InMemoryOptions _config;
    private readonly ChannelWriter<DeadLetterMessage> _deadLetterQueueWriter;

    private readonly ILogger _logger;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly CancellationTokenSource _stopTokenSource;

    // Track active processing tasks to await them during disposal
    private readonly ConcurrentBag<Task> _processingTasks = new();

    private Task? _consumerTask;
    private readonly SemaphoreSlim _pauseLock = new SemaphoreSlim(1, 1);
    private int _pauseCount = 0;  // Track pause state to prevent deadlock
    private volatile bool _isActive;
    private volatile bool _disposed;

    public InMemorySubscription(
        string topic,
        string consumerGroup,
        ChannelReader<MessageWrapper> channelReader,
        Func<MessageContext<TMessage>, Task<MessageResult>> handler,
        SubscribeOptions? options,
        IMessageSerializer serializer,
        MiddlewarePipeline pipeline,
        InMemoryOptions config,
        ChannelWriter<DeadLetterMessage> deadLetterQueueWriter,

        ILogger logger)
    {
        _topic = topic;
        _consumerGroup = consumerGroup;
        _channelReader = channelReader;
        _handler = handler;
        _options = options;
        _serializer = serializer;
        _pipeline = pipeline;
        _config = config;
        _deadLetterQueueWriter = deadLetterQueueWriter;
        _logger = logger;

        var maxConcurrency = options?.MaxConcurrency ?? 1;
        _concurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _stopTokenSource = new CancellationTokenSource();

        SubscriptionId = Guid.NewGuid().ToString();
    }

    public string SubscriptionId { get; }

    public IReadOnlyList<string> Topics => new[] { _topic };

    public bool IsActive => _isActive && !_disposed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_isActive)
        {
            throw new InvalidOperationException("Subscription is already active");
        }

        _isActive = true;

        // Start consumer task
        _consumerTask = Task.Run(async () => await ConsumeMessagesAsync(_stopTokenSource.Token), cancellationToken);

        await Task.CompletedTask;
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        // Use Interlocked to prevent multiple pause calls from acquiring lock multiple times (deadlock prevention)
        if (Interlocked.CompareExchange(ref _pauseCount, 1, 0) == 0)
        {
            await _pauseLock.WaitAsync(cancellationToken);
            _isActive = false;
            _logger.LogInformation("Subscription {SubscriptionId} paused for topic '{Topic}'", SubscriptionId, _topic);
        }
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InMemorySubscription<TMessage>));
        }

        // Use Interlocked to prevent multiple resume calls from releasing lock multiple times
        if (Interlocked.CompareExchange(ref _pauseCount, 0, 1) == 1)
        {
            _isActive = true;
            _pauseLock.Release();
            _logger.LogInformation("Subscription {SubscriptionId} resumed for topic '{Topic}'", SubscriptionId, _topic);
        }

        return Task.CompletedTask;
    }

    private async Task ConsumeMessagesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Started consuming messages for topic '{Topic}'", _topic);

        try
        {
            await foreach (var messageWrapper in _channelReader.ReadAllAsync(cancellationToken))
            {
                // Wait if paused (blocks without consuming CPU)
                await _pauseLock.WaitAsync(cancellationToken);
                _pauseLock.Release();

                if (!_isActive)
                {
                    continue; // Skip processing when paused
                }

                // Check routing pattern match
                if (_options?.RoutingPatterns != null)
                {
                    if (!RoutingPatternMatcher.MatchesAny(messageWrapper.RoutingKey, _options.RoutingPatterns))
                    {
                        continue; // Skip messages that don't match any pattern
                    }
                }
                else if (_options?.RoutingPattern != null)
                {
                    if (!RoutingPatternMatcher.Matches(messageWrapper.RoutingKey, _options.RoutingPattern))
                    {
                        continue; // Skip messages that don't match the pattern
                    }
                }
                // If no routing pattern specified, accept all messages (default behavior)

                // Enforce concurrency limit
                await _concurrencySemaphore.WaitAsync(cancellationToken);

                // Process message with retry logic (track task to await during disposal)
                var processingTask = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessMessageWithRetryAsync(messageWrapper, cancellationToken);
                    }
                    finally
                    {
                        _concurrencySemaphore.Release();
                    }
                }, cancellationToken);

                // Track the task so we can await it during disposal
                _processingTasks.Add(processingTask);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Message consumption canceled for topic '{Topic}'", _topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consuming messages for topic '{Topic}'", _topic);
        }
    }

    private async Task ProcessMessageWithRetryAsync(MessageWrapper wrapper, CancellationToken cancellationToken)
    {
        var serializedMessage = wrapper.SerializedMessage;
        var messageId = wrapper.MessageId;
        var expiresAt = wrapper.ExpiresAt;

        // Check TTL
        if (expiresAt.HasValue && DateTimeOffset.UtcNow > expiresAt.Value)
        {
            _logger.LogWarning(
                "Message {MessageId} expired (TTL exceeded), sending to DLQ",
                messageId);

            await SendToDeadLetterQueueAsync(messageId, serializedMessage, "TTL_EXPIRED", 0);
            return;
        }

        int attempt = 0;
        while (attempt < _config.MaxRetryAttempts)
        {
            attempt++;

            try
            {
                var envelope = _serializer.Deserialize<MessageEnvelope<TMessage>>(serializedMessage);

                var context = new MessageContext<TMessage>
                {
                    Envelope = envelope,
                    Topic = _topic,
                    ConsumerGroup = _options?.ConsumerGroup,
                    DeliveryCount = attempt,
                    CancellationToken = cancellationToken
                };

                var result = await _pipeline.ExecuteConsumeAsync(context, _handler, cancellationToken);

                if (result.Status == MessageStatus.Success)
                {
                    _logger.LogTrace("Successfully processed message {MessageId} (Attempt: {Attempt})",
                        messageId, attempt);
                    return;
                }

                if (result.Status == MessageStatus.Reject)
                {
                    _logger.LogWarning(
                        "Message {MessageId} rejected by handler: {Reason}, sending to DLQ",
                        messageId, result.Reason);

                    await SendToDeadLetterQueueAsync(messageId, serializedMessage, result.Reason ?? "REJECTED", attempt);
                    return;
                }

                // Retry status
                if (attempt < _config.MaxRetryAttempts)
                {
                    _logger.LogWarning(
                        "Message {MessageId} processing failed (Attempt {Attempt}/{MaxAttempts}), retrying after {Delay}ms. Reason: {Reason}",
                        messageId, attempt, _config.MaxRetryAttempts, _config.RetryDelay.TotalMilliseconds, result.Reason);

                    await Task.Delay(_config.RetryDelay, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                if (attempt < _config.MaxRetryAttempts)
                {
                    _logger.LogError(ex,
                        "Exception processing message {MessageId} (Attempt {Attempt}/{MaxAttempts}), retrying",
                        messageId, attempt, _config.MaxRetryAttempts);

                    await Task.Delay(_config.RetryDelay, cancellationToken);
                }
                else
                {
                    _logger.LogError(ex,
                        "Exception processing message {MessageId} after {Attempts} attempts, sending to DLQ",
                        messageId, attempt);
                }
            }
        }

        // Max retries exceeded, send to DLQ
        await SendToDeadLetterQueueAsync(messageId, serializedMessage, "MAX_RETRIES_EXCEEDED", attempt);
    }

    private async Task SendToDeadLetterQueueAsync(
        string messageId,
        byte[] serializedMessage,  // Changed from string to byte[] to match MessageWrapper type
        string reason,
        int attemptCount)
    {
        if (!_config.EnableDeadLetterQueue)
        {
            _logger.LogWarning(
                "Message {MessageId} failed but DLQ is disabled. Message lost. Reason: {Reason}",
                messageId, reason);
            return;
        }

        try
        {
            var deadLetterMessage = new DeadLetterMessage(
                _topic,
                messageId,
                serializedMessage,
                reason,
                attemptCount,
                DateTimeOffset.UtcNow
            );

            await _deadLetterQueueWriter.WriteAsync(deadLetterMessage);


            _logger.LogWarning(
                "Sent message {MessageId} to DLQ (Reason: {Reason}, Attempts: {Attempts})",
                messageId, reason, attemptCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message {MessageId} to DLQ", messageId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _logger.LogDebug("Disposing subscription {SubscriptionId} for topic '{Topic}'", SubscriptionId, _topic);

        // Set flags FIRST to prevent new operations
        _disposed = true;
        _isActive = false;

        // Signal cancellation to stop consumer task
        _stopTokenSource.Cancel();

        // Wait for consumer task to complete (stops creating new processing tasks)
        if (_consumerTask != null)
        {
            try
            {
                await _consumerTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when task is cancelled
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Consumer task threw exception during disposal for subscription {SubscriptionId}",
                    SubscriptionId);
            }
        }

        // Wait for all active processing tasks to complete BEFORE disposing semaphore
        if (_processingTasks.Count > 0)
        {
            _logger.LogDebug("Waiting for {Count} active processing tasks to complete for subscription {SubscriptionId}",
                _processingTasks.Count, SubscriptionId);

            try
            {
                await Task.WhenAll(_processingTasks);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Processing tasks cancelled during disposal");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error waiting for processing tasks during disposal for subscription {SubscriptionId}",
                    SubscriptionId);
            }
        }

        // NOW safe to dispose resources (all tasks are fully stopped)
        _concurrencySemaphore.Dispose();
        _stopTokenSource.Dispose();
        _pauseLock.Dispose();

        _logger.LogInformation("Subscription {SubscriptionId} disposed", SubscriptionId);
    }
}
