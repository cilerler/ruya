using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Diagnostics.DistributedTracing;

namespace Ruya.Diagnostics.Unit.Tests;

[TestClass]
public sealed class DistributedTracingTests
{
	[TestMethod]
	public void StartActivity_DefaultAndOperationTagsProvided_OperationTagOverridesDefault()
	{
		using var listener = CreateListener("ruya-diagnostics-tests");
		using var harness = CreateService(
			new DistributedTracingSettings
			{
				DefaultTags = new Dictionary<string, string>
				{
					["app.component"] = "worker",
					["deployment.environment"] = "test"
				}
			});

		using var scope = harness.Service.StartActivity(
			"Dispatch",
			tags: new Dictionary<string, object?> { ["app.component"] = "api" });

		Assert.IsNotNull(scope.Activity);
		Assert.AreEqual("api", scope.Activity.GetTagItem("app.component"));
		Assert.AreEqual("test", scope.Activity.GetTagItem("deployment.environment"));
	}

	[TestMethod]
	public void ContinueActivity_CacheReadFails_DoesNotLogSensitiveCacheKey()
	{
		const string sensitiveCacheKey = "trace:customer:private-identifier";
		var logger = new CapturingLogger<DistributedTracingService>();
		using var harness = CreateService(
			new DistributedTracingSettings(),
			logger,
			new ThrowingDistributedCache());

		using var scope = harness.Service.ContinueActivity("Continue", sensitiveCacheKey);

		Assert.IsTrue(logger.Entries.Any(entry => entry.EventId.Id == 104));
		Assert.IsFalse(logger.Entries.Any(entry => entry.Message.Contains(sensitiveCacheKey, StringComparison.Ordinal)));
	}

	[TestMethod]
	public void StartActivity_DebugLoggingDisabled_DoesNotEmitLifecycleDebugEvents()
	{
		using var listener = CreateListener("ruya-diagnostics-tests");
		var logger = new CapturingLogger<DistributedTracingService>();
		using var harness = CreateService(new DistributedTracingSettings { EnableDebugLogging = false }, logger);

		var scope = harness.Service.StartActivity("Quiet");
		scope.Dispose();

		Assert.IsFalse(logger.Entries.Any(entry => entry.EventId.Id is 102 or 103));
	}

	[TestMethod]
	public async Task StartActivityAsync_CacheKeyProvided_UsesAsyncCacheOperation()
	{
		using var listener = CreateListener("ruya-diagnostics-tests");
		var cache = new AsyncOnlyDistributedCache();
		using var harness = CreateService(new DistributedTracingSettings(), cache: cache);

		using var scope = await harness.Service.StartActivityAsync("AsyncStart", cacheKey: "trace:async");

		Assert.IsNotNull(scope.Activity);
		Assert.IsNotNull(cache.StoredValue);
	}

	[TestMethod]
	public async Task ContinueActivityAsync_CanceledWhileCacheReadPending_ThrowsOperationCanceledException()
	{
		using var harness = CreateService(new DistributedTracingSettings(), cache: new BlockingAsyncDistributedCache());
		using var cancellationSource = new CancellationTokenSource();
		var operation = harness.Service.ContinueActivityAsync(
			"AsyncContinue",
			"trace:pending",
			cancellationToken: cancellationSource.Token).AsTask();

		await cancellationSource.CancelAsync();

		await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
	}

	[TestMethod]
	public void AddDistributedTracingService_ConfigurationSectionMissing_ThrowsOptionsValidationException()
	{
		using var provider = BuildProvider(new Dictionary<string, string?>());

		var exception = Assert.ThrowsExactly<OptionsValidationException>(
			() => _ = provider.GetRequiredService<IOptions<DistributedTracingSettings>>().Value);

		StringAssert.Contains(exception.Message, DistributedTracingSettings.ConfigurationSectionName, StringComparison.Ordinal);
	}

	[TestMethod]
	public void AddDistributedTracingService_AbsoluteExpirationShorterThanSliding_ThrowsOptionsValidationException()
	{
		using var provider = BuildProvider(new Dictionary<string, string?>
		{
			["DistributedTracing:CacheSlidingExpiration"] = "00:10:00",
			["DistributedTracing:CacheAbsoluteExpiration"] = "00:05:00"
		});

		var exception = Assert.ThrowsExactly<OptionsValidationException>(
			() => _ = provider.GetRequiredService<IOptions<DistributedTracingSettings>>().Value);

		StringAssert.Contains(exception.Message, "CacheAbsoluteExpiration", StringComparison.Ordinal);
	}

	[TestMethod]
	public void AddDistributedTracingService_BlankDefaultTag_ThrowsOptionsValidationException()
	{
		using var provider = BuildProvider(new Dictionary<string, string?>
		{
			["DistributedTracing:DefaultTags:app.component"] = " "
		});

		var exception = Assert.ThrowsExactly<OptionsValidationException>(
			() => _ = provider.GetRequiredService<IOptions<DistributedTracingSettings>>().Value);

		StringAssert.Contains(exception.Message, "DefaultTags", StringComparison.Ordinal);
	}

	[TestMethod]
	public void AddDistributedTracingService_MeterFactoryMissing_ThrowsInvalidOperationException()
	{
		var services = new ServiceCollection();
		services.AddSingleton<IDistributedCache, NoOpDistributedCache>();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(
			() => services.AddDistributedTracingService("ruya-diagnostics-options-tests"));

		StringAssert.Contains(exception.Message, nameof(IMeterFactory), StringComparison.Ordinal);
	}

	private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(values)
			.Build();
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddSingleton<IDistributedCache, NoOpDistributedCache>();
		services.AddSingleton<IMeterFactory, TestMeterFactory>();
		services.AddDistributedTracingService("ruya-diagnostics-options-tests");
		return services.BuildServiceProvider();
	}

	private static DistributedTracingHarness CreateService(
		DistributedTracingSettings settings,
		ILogger<DistributedTracingService>? logger = null,
		IDistributedCache? cache = null)
	{
		return new DistributedTracingHarness(settings, logger, cache);
	}

	private static ActivityListener CreateListener(string sourceName)
	{
		var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == sourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded
		};
		ActivitySource.AddActivityListener(listener);
		return listener;
	}

	private sealed class TestMeterFactory : IMeterFactory
	{
		public Meter Create(MeterOptions options) => new(options);
		public void Dispose()
		{
		}
	}

	private sealed class DistributedTracingHarness : IDisposable
	{
		private readonly ActivitySource _activitySource = new("ruya-diagnostics-tests", "1.0.0");
		private readonly TestMeterFactory _meterFactory = new();

		public DistributedTracingHarness(
			DistributedTracingSettings settings,
			ILogger<DistributedTracingService>? logger,
			IDistributedCache? cache)
		{
			Service = new DistributedTracingService(
				logger ?? new CapturingLogger<DistributedTracingService>(),
				_activitySource,
				_meterFactory,
				Options.Create(settings),
				cache ?? new NoOpDistributedCache());
		}

		public DistributedTracingService Service { get; }

		public void Dispose()
		{
			Service.Dispose();
			_activitySource.Dispose();
			_meterFactory.Dispose();
		}
	}

	private sealed class CapturingLogger<T> : ILogger<T>
	{
		public ConcurrentQueue<LogEntry> Entries { get; } = new();

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(
			LogLevel logLevel,
			EventId eventId,
			TState state,
			Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			Entries.Enqueue(new LogEntry(eventId, formatter(state, exception)));
		}
	}

	private sealed record LogEntry(EventId EventId, string Message);

	private class NoOpDistributedCache : IDistributedCache
	{
		public virtual byte[]? Get(string key) => null;
		public virtual Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
		public virtual void Refresh(string key)
		{
		}
		public virtual Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
		public virtual void Remove(string key)
		{
		}
		public virtual Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
		public virtual void Set(string key, byte[] value, DistributedCacheEntryOptions options)
		{
		}
		public virtual Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
	}

	private sealed class ThrowingDistributedCache : NoOpDistributedCache
	{
		public override byte[]? Get(string key) => throw new InvalidOperationException("cache unavailable");
	}

	private sealed class AsyncOnlyDistributedCache : NoOpDistributedCache
	{
		public byte[]? StoredValue { get; private set; }

		public override byte[]? Get(string key) => throw new InvalidOperationException("Synchronous cache access is forbidden.");

		public override void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
			throw new InvalidOperationException("Synchronous cache access is forbidden.");

		public override Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
			Task.FromResult(StoredValue);

		public override Task SetAsync(
			string key,
			byte[] value,
			DistributedCacheEntryOptions options,
			CancellationToken token = default)
		{
			StoredValue = value;
			return Task.CompletedTask;
		}
	}

	private sealed class BlockingAsyncDistributedCache : NoOpDistributedCache
	{
		public override byte[]? Get(string key) => throw new InvalidOperationException("Synchronous cache access is forbidden.");

		public override async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, token);
			return null;
		}
	}
}
