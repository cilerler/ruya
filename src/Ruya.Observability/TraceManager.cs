using System;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ruya.Observability;

public class TraceManager
{
	private readonly DistributedCacheEntryOptions _cacheOptions;
	private readonly IServiceProvider _serviceProvider;
	private readonly TracingSetting _settings;

	public TraceManager(IServiceProvider serviceProvider, IOptions<TracingSetting> options)
	{
		_serviceProvider = serviceProvider;
		_settings = options.Value;
		_cacheOptions = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(_settings.TraceKeyCacheExpirationMinutes) };
	}

	public Activity? StartActivity(string name, ActivityContext parent = default)
	{
		if (Activity.Current != null && parent == default) parent = Activity.Current.Context;

		return DistributedTracing.ActivitySource.StartActivity(name, ActivityKind.Server, parent);
	}

	public void AddEvent(string eventName)
	{
		Activity? activity = Activity.Current;
		if (activity == null)
			return;

		activity.AddEvent(new ActivityEvent(eventName));
	}

	public void AddTags(params (string, string)[] tags)
	{
		Activity? activity = Activity.Current;
		if (activity == null)
			return;

		foreach (( string key, string value ) in tags) activity.AddTag(key, value);
	}

	public void AddTag(string key, string value)
	{
		Activity? activity = Activity.Current;
		if (activity == null)
			return;

		activity.AddTag(key, value);
	}

	public Activity? StartImportQueueActivity(string name, long importId)
	{
		ActivityContext parent = GetParentImportIdTrace(importId);
		Activity? activity = StartActivity(name, parent);
		TrySetActivityAsParent(importId, activity);
		AddTag("importqueue.id", importId.ToString());
		return activity;
	}

	/// <summary>
	///     Sets the given activity as the parent of import queue id if it doesn't exist
	/// </summary>
	public void TrySetActivityAsParent(long importId, Activity? activity = null)
	{
		if (activity == null)
			activity = Activity.Current;

		IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
		using IServiceScope scope = scopeFactory.CreateScope();
		IDistributedCache _distributedCache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

		string? parentTraceId = _distributedCache.GetString($"{_settings.TraceCacheInstanceName}:{importId}");
		if (activity != null && parentTraceId == null)
			_distributedCache.SetString($"{_settings.TraceCacheInstanceName}:{importId}", activity.Id, _cacheOptions);
	}

	/// <summary>
	///     Gets the parent trace from id
	/// </summary>
	private ActivityContext GetParentImportIdTrace(long id)
	{
		if (id <= 0)
			return default;

		IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
		using IServiceScope scope = scopeFactory.CreateScope();
		IDistributedCache _distributedCache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

		string? parentTraceId = _distributedCache.GetString($"{_settings.TraceCacheInstanceName}:{id}");
		ActivityContext parent = default;

		if (parentTraceId != null) parent = ActivityContext.Parse(parentTraceId, null);

		return parent;
	}

	/// <summary>
	///     Returns the root trace that is linked with given file name
	/// </summary>
	public ActivityContext GetFileNameTrace(string fileName)
	{
		if (string.IsNullOrEmpty(fileName))
			return default;

		IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
		using IServiceScope scope = scopeFactory.CreateScope();
		IDistributedCache _distributedCache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

		string? parentTraceId = _distributedCache.GetString($"{_settings.TraceCacheInstanceName}:{_settings.FileNameTraceKeyPrefix}{fileName}");
		ActivityContext parent = default;

		if (parentTraceId != null) parent = ActivityContext.Parse(parentTraceId, null);
		return parent;
	}

	/// <summary>
	///     Sets the parent trace for given file name
	/// </summary>
	public ActivityContext TrySetFileNameTrace(string fileName, Activity? activity)
	{
		if (string.IsNullOrEmpty(fileName))
			return default;

		IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
		using IServiceScope scope = scopeFactory.CreateScope();
		IDistributedCache _distributedCache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

		string? parentTraceId = _distributedCache.GetString($"{_settings.TraceCacheInstanceName}:{_settings.FileNameTraceKeyPrefix}{fileName}");
		ActivityContext parent = default;

		if (activity != null && parentTraceId == null)
			_distributedCache.SetString($"{_settings.TraceCacheInstanceName}:{_settings.FileNameTraceKeyPrefix}{fileName}", activity.Id,
				_cacheOptions);
		return parent;
	}
}
