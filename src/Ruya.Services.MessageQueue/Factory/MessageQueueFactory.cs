using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;

namespace Ruya.Services.MessageQueue.Factory;

/// <summary>
/// Default implementation of IMessageQueueFactory
/// </summary>
public sealed class MessageQueueFactory : IMessageQueueFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MessageQueueFactory> _logger;
    private readonly MessageQueueOptions _options;
    private readonly Dictionary<string, IMessageQueueProvider> _providers;
    private readonly Dictionary<string, IMessageQueue> _queueInstances;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private volatile bool _disposed;

    public MessageQueueFactory(
        IServiceProvider serviceProvider,
        IEnumerable<IMessageQueueProvider> providers,
        IOptions<MessageQueueOptions> options,
        ILogger<MessageQueueFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _providers = providers?.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase)
            ?? throw new ArgumentNullException(nameof(providers));
        _queueInstances = new Dictionary<string, IMessageQueue>(StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation(
            "MessageQueueFactory initialized with {ProviderCount} providers: {Providers}",
            _providers.Count,
            string.Join(", ", _providers.Keys));
    }

    /// <inheritdoc />
    public async Task<IMessageQueue> CreateQueueAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Queue name cannot be null or whitespace", nameof(name));

        ObjectDisposedException.ThrowIf(_disposed, this);

        // Always acquire lock to avoid race conditions on dictionary access
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Check for existing instance
            if (_queueInstances.TryGetValue(name, out var existingQueue))
            {
                _logger.LogDebug("Returning existing message queue instance: {Name}", name);
                return existingQueue;
            }

            // Find provider configuration
            if (!_options.Providers.TryGetValue(name, out var providerConfig))
            {
                throw new InvalidOperationException(
                    $"No provider configuration found for '{name}'. Available providers: {string.Join(", ", _options.Providers.Keys)}");
            }

            if (!providerConfig.Enabled)
            {
                throw new InvalidOperationException($"Provider '{name}' is not enabled");
            }

            // Get provider implementation
            if (!_providers.TryGetValue(providerConfig.Type, out var provider))
            {
                throw new InvalidOperationException(
                    $"No provider implementation found for type '{providerConfig.Type}'. Available types: {string.Join(", ", _providers.Keys)}");
            }

            // Create queue instance ASYNCHRONOUSLY (no blocking!)
            _logger.LogInformation("Creating message queue instance: {Name} using provider: {Provider}", name, provider.ProviderName);
            var queue = await provider.CreateAsync(name, cancellationToken);

            _queueInstances[name] = queue;

            _logger.LogInformation(
                "Message queue '{Name}' created successfully. Capabilities: Priority={SupportsPriority}, Delay={SupportsDelay}, DLQ={SupportsDLQ}, Replay={SupportsReplay}",
                name,
                provider.Capabilities.SupportsPriority,
                provider.Capabilities.SupportsDelayedDelivery,
                provider.Capabilities.SupportsDeadLetterQueue,
                provider.Capabilities.SupportsReplay);

            return queue;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetRegisteredProviders()
    {
        return _providers.Keys.ToList();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await _lock.WaitAsync();
        try
        {
            if (_disposed) return;

            _disposed = true;  // Set FIRST after double-check to prevent new operations

            _logger.LogInformation("Disposing MessageQueueFactory and {Count} queue instances", _queueInstances.Count);

            // Dispose all queues ASYNCHRONOUSLY (no blocking!)
            foreach (var queue in _queueInstances.Values)
            {
                try
                {
                    await queue.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing message queue: {Name}", queue.Name);
                }
            }

            _queueInstances.Clear();

            _logger.LogInformation("MessageQueueFactory disposed");
        }
        finally
        {
            _lock.Release();
        }

        // Dispose lock OUTSIDE try-finally to ensure it always gets disposed
        _lock.Dispose();
    }
}
