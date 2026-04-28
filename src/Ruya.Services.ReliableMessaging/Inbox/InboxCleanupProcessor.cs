using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ruya.Services.ReliableMessaging.Inbox;

/// <summary>
/// Hosted service that periodically deletes processed inbox entries older than <see cref="InboxOptions.ArchiveAfter"/>
/// for a specific persistence context. Disabled when <see cref="InboxOptions.ArchiveAfter"/> is <see cref="TimeSpan.Zero"/>.
/// </summary>
/// <typeparam name="TContext">Marker type identifying the persistence context (typically the consumer's <c>DbContext</c>).</typeparam>
public sealed partial class InboxCleanupProcessor<TContext> : BackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<InboxCleanupProcessor<TContext>> _logger;
	private readonly InboxOptions _options;

	public InboxCleanupProcessor(
		IServiceScopeFactory scopeFactory,
		IOptions<ReliableMessagingOptions> options,
		ILogger<InboxCleanupProcessor<TContext>> logger)
	{
		ArgumentNullException.ThrowIfNull(scopeFactory);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(logger);

		_scopeFactory = scopeFactory;
		_logger = logger;
		_options = options.Value.Inbox;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (_options.ArchiveAfter <= TimeSpan.Zero)
		{
			return; // cleanup disabled
		}

		while (!stoppingToken.IsCancellationRequested)
		{
			// Delay-first ordering: avoids racing the host's startup (SQL connection pool warmup, EF Core
			// first-time query compilation) on the first iteration. Cleanup is a maintenance task — waiting
			// one CleanupInterval before the first run is harmless and eliminates a known startup error chirp.
			try
			{
				await Task.Delay(_options.CleanupInterval, stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}

			try
			{
				await CleanupOnceAsync(stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
#pragma warning disable CA1031 // top-level cleanup must not crash the host; log and retry on next tick
			catch (Exception ex)
			{
				LogCleanupFailure(ex);
			}
#pragma warning restore CA1031
		}
	}

	private async Task CleanupOnceAsync(CancellationToken ct)
	{
		using var scope = _scopeFactory.CreateScope();
		var store = scope.ServiceProvider.GetRequiredService<IInboxStore<TContext>>();

		var threshold = DateTime.UtcNow - _options.ArchiveAfter;
		var removed = await store.CleanupProcessedAsync(threshold, ct).ConfigureAwait(false);
		if (removed > 0)
		{
			LogCleanupRemoved(removed);
		}
	}

	[LoggerMessage(EventId = 6001, Level = LogLevel.Debug, Message = "Inbox cleanup removed {Count} processed entries.")]
	private partial void LogCleanupRemoved(int count);

	[LoggerMessage(EventId = 6002, Level = LogLevel.Error, Message = "Inbox cleanup iteration failed.")]
	private partial void LogCleanupFailure(Exception exception);
}
