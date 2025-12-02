using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Ruya.Services.MessageQueue.Abstractions;
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
    private readonly string? _queueName;

    /// <summary>
    /// Creates a health check for all message queue instances
    /// </summary>
    public MessageQueueHealthCheck(IMessageQueueFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Creates a health check for a specific message queue instance
    /// </summary>
    public MessageQueueHealthCheck(IMessageQueueFactory factory, string queueName)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
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
                // Check all registered providers
                var providers = _factory.GetRegisteredProviders();
                var results = new Dictionary<string, bool>();

                foreach (var provider in providers)
                {
                    try
                    {
                        var queue = await _factory.CreateQueueAsync(provider, cancellationToken);
                        results[provider] = await queue.IsHealthyAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        results[provider] = false;
                        context.Registration.FailureStatus = HealthStatus.Degraded;
                    }
                }

                var healthyCount = results.Count(r => r.Value);
                var totalCount = results.Count;

                if (healthyCount == totalCount)
                {
                    return HealthCheckResult.Healthy(
                        $"All {totalCount} message queue provider(s) are healthy",
                        results.ToDictionary(r => r.Key, r => (object)r.Value));
                }
                else if (healthyCount > 0)
                {
                    return HealthCheckResult.Degraded(
                        $"{healthyCount}/{totalCount} message queue provider(s) are healthy",
                        data: results.ToDictionary(r => r.Key, r => (object)r.Value));
                }
                else
                {
                    return HealthCheckResult.Unhealthy(
                        "No message queue providers are healthy",
                        data: results.ToDictionary(r => r.Key, r => (object)r.Value));
                }
            }
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
            sp => new MessageQueueHealthCheck(sp.GetRequiredService<IMessageQueueFactory>()),
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
            sp => new MessageQueueHealthCheck(sp.GetRequiredService<IMessageQueueFactory>(), queueName),
            failureStatus,
            tags,
            timeout));
    }
}
