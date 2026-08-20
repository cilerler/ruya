using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Ruya.Services.ReliableMessaging.Inbox;
using Ruya.Services.ReliableMessaging.Outbox;

namespace Ruya.Services.ReliableMessaging.Extensions;

/// <summary>DI registration surface for the reliable-messaging pattern library.</summary>
public static class StartupExtensions
{
	/// <summary>
	/// Registers reliable-messaging core services:
	/// <see cref="ReliableMessagingOptions"/> binding, the default <see cref="IInboxConsumerNameProvider"/>,
	/// a default Polly <see cref="ResiliencePipeline"/> for outbox dispatch (key: <see cref="OutboxResiliencePipelineKey.Dispatch"/>),
	/// and returns an <see cref="IReliableMessagingBuilder"/> for adapter chaining.
	/// </summary>
	/// <remarks>
	/// Override the default dispatch pipeline by calling
	/// <c>services.AddResiliencePipeline(OutboxResiliencePipelineKey.Dispatch, builder =&gt; ...)</c>
	/// either before or after this call. Per-context services (<see cref="IOutboxBuffer{TContext}"/>,
	/// <see cref="IOutboxPublisher{TContext}"/>, <see cref="OutboxProcessor{TContext}"/>,
	/// <see cref="InboxCleanupProcessor{TContext}"/>) are registered by calling
	/// <see cref="AddOutboxContext{TContext}"/> / <see cref="AddInboxContext{TContext}"/>.
	/// An <see cref="IOutboundDispatcher"/> must be contributed by a separate adapter package.
	/// </remarks>
	public static IReliableMessagingBuilder AddReliableMessaging(
		this IServiceCollection services,
		Action<ReliableMessagingOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);

		var optionsBuilder = services.AddOptions<ReliableMessagingOptions>()
			.BindConfiguration(ReliableMessagingOptions.ConfigurationSectionName)
			.ValidateOnStart();

		services.TryAddEnumerable(
			ServiceDescriptor.Singleton<IValidateOptions<ReliableMessagingOptions>, ReliableMessagingOptionsValidator>());

		if (configure is not null)
		{
			optionsBuilder.Configure(configure);
		}

		services.TryAddSingleton<IInboxConsumerNameProvider, TypeNameInboxConsumerNameProvider>();
		services.TryAddSingleton<InboxMetrics>();

		services.AddResiliencePipeline(OutboxResiliencePipelineKey.Dispatch, builder =>
		{
			builder.AddRetry(new RetryStrategyOptions
			{
				MaxRetryAttempts = 3,
				BackoffType = DelayBackoffType.Exponential,
				Delay = TimeSpan.FromSeconds(1),
				MaxDelay = TimeSpan.FromSeconds(30),
				UseJitter = true,
			});
		});

		return new ReliableMessagingBuilder(services);
	}

	/// <summary>
	/// Registers the per-context outbox services (<see cref="IOutboxBuffer{TContext}"/>, <see cref="IOutboxPublisher{TContext}"/>,
	/// and the <see cref="OutboxProcessor{TContext}"/> hosted service) for the given persistence context.
	/// Call this once per <typeparamref name="TContext"/> (typically a <c>DbContext</c>).
	/// </summary>
	/// <remarks>
	/// The <see cref="IOutboxStore{TContext}"/> is NOT registered here — storage adapter packages (e.g. EntityFrameworkCore)
	/// supply it. Registering both is required for the outbox to function.
	/// </remarks>
	public static IReliableMessagingBuilder AddOutboxContext<TContext>(this IReliableMessagingBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		builder.Services.TryAddScoped<IOutboxBuffer<TContext>, OutboxBuffer<TContext>>();
		builder.Services.TryAddScoped<IOutboxPublisher<TContext>, OutboxPublisher<TContext>>();
		builder.Services.AddHostedService<OutboxProcessor<TContext>>();

		return builder;
	}

	/// <summary>
	/// Registers the per-context inbox cleanup hosted service for the given persistence context.
	/// The <see cref="IInboxStore{TContext}"/> is NOT registered here — storage adapter packages (e.g. EntityFrameworkCore) supply it.
	/// </summary>
	public static IReliableMessagingBuilder AddInboxContext<TContext>(this IReliableMessagingBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		builder.Services.AddHostedService<InboxCleanupProcessor<TContext>>();

		return builder;
	}

	/// <summary>
	/// Registers an <see cref="OutboxHealthCheck{TContext}"/> under the given <paramref name="healthCheckName"/>
	/// (default: <c>outbox.{typeof(TContext).Name}</c>). Apply tags to route the check into <c>/healthz/ready</c>
	/// or similar endpoint groups.
	/// </summary>
	public static IReliableMessagingBuilder AddOutboxHealthCheck<TContext>(
		this IReliableMessagingBuilder builder,
		string? healthCheckName = null,
		HealthStatus failureStatus = HealthStatus.Unhealthy,
		IEnumerable<string>? tags = null)
	{
		ArgumentNullException.ThrowIfNull(builder);

		var name = healthCheckName ?? $"outbox.{typeof(TContext).Name}";

		builder.Services.AddHealthChecks()
			.AddCheck<OutboxHealthCheck<TContext>>(name, failureStatus, tags);

		return builder;
	}
}
