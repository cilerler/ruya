using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Abstractions;
using Ruya.Services.MessageQueue.Configuration;
using Ruya.Services.MessageQueue.Factory;
using System.Threading;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace Ruya.Services.MessageQueue.Health;

/// <summary>
/// Health check for message queue instances
/// </summary>
public sealed class MessageQueueHealthCheck : IHealthCheck
{
    private readonly IMessageQueueFactory _factory;
    private readonly MessageQueueOptions? _options;
    private readonly string? _queueName;

    /// <summary>
    /// Creates a health check for all message queue instances
    /// </summary>
    [Obsolete("Use MessageQueueHealthCheck(IMessageQueueFactory, IOptions<MessageQueueOptions>) so configured queue instance names and EnableHealthChecks are honored. This constructor will be removed in version 9.0.")]
    public MessageQueueHealthCheck(IMessageQueueFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _options = (factory as MessageQueueFactory)?.Options;
    }

    /// <summary>
    /// Creates a health check for all configured message queue instances.
    /// </summary>
    public MessageQueueHealthCheck(
        IMessageQueueFactory factory,
        IOptions<MessageQueueOptions> options)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Creates a health check for a specific message queue instance
    /// </summary>
    public MessageQueueHealthCheck(IMessageQueueFactory factory, string queueName)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _options = (factory as MessageQueueFactory)?.Options;
        _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
    }

    /// <summary>
    /// Creates a health check for a specific queue while honoring global health-check options.
    /// </summary>
    public MessageQueueHealthCheck(
        IMessageQueueFactory factory,
        IOptions<MessageQueueOptions> options,
        string queueName)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_options?.EnableHealthChecks == false)
            {
                return HealthCheckResult.Healthy("Message queue health checks are disabled by configuration");
            }

            if (!string.IsNullOrEmpty(_queueName))
            {
                // Check specific queue instance
                var queue = await _factory.CreateQueueAsync(_queueName, cancellationToken);
                var isHealthy = await queue.IsHealthyAsync(cancellationToken);

                return isHealthy
                    ? HealthCheckResult.Healthy($"Message queue '{_queueName}' is healthy")
                    : HealthCheckResult.Unhealthy($"Message queue '{_queueName}' is not responding");
            }
            else
            {
                // Check configured queue instances. Provider implementation names such as
                // "RabbitMQ" are types; CreateQueueAsync expects the application-owned instance
                // name such as "orders-rabbitmq".
                var queueNames = _options is null
                    ? _factory.GetRegisteredProviders()
                        .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                    : _options.Providers
                        .Where(static provider => provider.Value.Enabled)
                        .Select(static provider => provider.Key)
                        .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                var results = new Dictionary<string, bool>();

                foreach (var queueName in queueNames)
                {
                    try
                    {
                        var queue = await _factory.CreateQueueAsync(queueName, cancellationToken);
                        results[queueName] = await queue.IsHealthyAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        results[queueName] = false;
                    }
                }

                var healthyCount = results.Count(r => r.Value);
                var totalCount = results.Count;

                if (healthyCount == totalCount)
                {
                    return HealthCheckResult.Healthy(
                        totalCount == 0
                            ? "No enabled message queue instances are configured"
                            : $"All {totalCount} message queue instance(s) are healthy",
                        results.ToDictionary(r => r.Key, r => (object)r.Value));
                }
                else if (healthyCount > 0)
                {
                    return HealthCheckResult.Degraded(
                        $"{healthyCount}/{totalCount} message queue instance(s) are healthy",
                        data: results.ToDictionary(r => r.Key, r => (object)r.Value));
                }
                else
                {
                    return HealthCheckResult.Unhealthy(
                        "No message queue instances are healthy",
                        data: results.ToDictionary(r => r.Key, r => (object)r.Value));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to check message queue health",
                ex);
        }
    }
}

/// <summary>
/// Extension methods for adding message queue health checks
/// </summary>
public static class MessageQueueHealthCheckExtensions
{
    /// <summary>
    /// Adds a health check for all message queue instances
    /// </summary>
    public static IHealthChecksBuilder AddMessageQueueHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "messagequeue",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new MessageQueueHealthCheck(
                sp.GetRequiredService<IMessageQueueFactory>(),
                sp.GetRequiredService<IOptions<MessageQueueOptions>>()),
            failureStatus,
            tags,
            timeout));
    }

    /// <summary>
    /// Adds a health check for a specific message queue instance
    /// </summary>
    public static IHealthChecksBuilder AddMessageQueueHealthCheck(
        this IHealthChecksBuilder builder,
        string queueName,
        string name,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new MessageQueueHealthCheck(
                sp.GetRequiredService<IMessageQueueFactory>(),
                sp.GetRequiredService<IOptions<MessageQueueOptions>>(),
                queueName),
            failureStatus,
            tags,
            timeout));
    }
}
