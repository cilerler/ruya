using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Ruya.Extensions.Caching;

public interface ILockManager
{
	// ReSharper disable InconsistentNaming
	public Task<bool> AcquireAndExecuteWithLockAsync(Func<Task> callback, string lockKey, string lockValue, bool deleteAfterRelease = true);
	// ReSharper enable InconsistentNaming
}

public class LockManager : ILockManager
{
	private readonly ILogger _logger;
	private readonly LockManagerSetting _setting;
	private readonly IDatabase _redisDb;

	public LockManager(ILogger<LockManager> logger, IOptions<LockManagerSetting> options, IConnectionMultiplexer redisConnection)
	{
		_logger = logger;
		_setting = options.Value;
		_redisDb = redisConnection.GetDatabase();
	}

	public async Task<bool> AcquireAndExecuteWithLockAsync(Func<Task> callback, string lockKey, string lockValue, bool deleteAfterRelease = true)
	{
		var internalLockKey = lockKey;
		if (!string.IsNullOrWhiteSpace(_setting.InstanceName))
		{
			internalLockKey = $"{_setting.InstanceName}{lockKey}";
		}

		bool output = false;
		using (_logger.BeginScope("{LockKey}", internalLockKey))
		{

			// Don't remove `[LockKey = {LockKey}]` from the messages below.
			// Until `LOGQL` supports either `regexp` or `json` capable of extracting the same key with varying values and consolidating them, the issue remains.
			// The primary challenge is that a nested lock produces multiple `LockKey` entries under the `Scope` array.
			// As a workaround, the lock key is added manually to each log entry."

			try
			{
				bool lockExists = await _redisDb.KeyExistsAsync(internalLockKey);
				if (lockExists)
				{
					_logger.Log(LogLevel.Information, "Lock Status: Lock is already acquired by another instance. [LockKey = {LockKey}]", internalLockKey);
					return output;
				}
				bool lockAcquired = await _redisDb.LockTakeAsync(internalLockKey, lockValue, _setting.LockExpiry);
                if (!lockAcquired)
                {
                    _logger.Log(LogLevel.Warning, "Lock Status: Failed to acquire the lock. Another instance may be holding it. [LockKey = {LockKey}]", internalLockKey);
                    return output;
                }

				_logger.Log(LogLevel.Information, "Lock Status: Acquired. Running external process. [LockKey = {LockKey}]", internalLockKey);
				try
				{
					await callback();
					_logger.Log(LogLevel.Debug, "Lock Status: External process completed. [LockKey = {LockKey}]", internalLockKey);
					output = true;
				}
				finally
				{
					_logger.Log(LogLevel.Debug, "Lock Status: Releasing the lock. [LockKey = {LockKey}]", internalLockKey);
					if (_redisDb.LockRelease(internalLockKey, lockValue))
					{
						_logger.Log(LogLevel.Debug, "Lock Status: Lock successfully released. [LockKey = {LockKey}]", internalLockKey);
						if (deleteAfterRelease)
						{
							_logger.Log(LogLevel.Debug, "Lock Status: Deleting the lock. [LockKey = {LockKey}]", internalLockKey);
							if (_redisDb.KeyDelete(internalLockKey))
							{
								_logger.Log(LogLevel.Debug, "Lock Status: Lock successfully deleted. [LockKey = {LockKey}]", internalLockKey);
							} else {
								_logger.Log(LogLevel.Error, "Lock Status: Failed to delete lock. [LockKey = {LockKey}]", internalLockKey);
							}
						}
					} else {
						_logger.Log(LogLevel.Error, "Lock Status: Failed to release lock. [LockKey = {LockKey}]", internalLockKey);
					}
				}
            }
			catch (RedisException ex)
			{
				ex.Data.Add(nameof(lockKey), internalLockKey);
				_logger.Log(LogLevel.Error, ex, "Lock Status: An error occurred while processing the lock. [LockKey = {LockKey}] {errorMessage}", internalLockKey, ex.Message);
			}
		}
		return output;
	}
}

public class LockManagerSetting
{
	public const string ConfigurationSectionName = nameof(LockManager);
	public TimeSpan LockExpiry { set; get; } = TimeSpan.FromHours(24);
	internal string InstanceName { set; get; }
}

public static partial class StartupExtensions
{
	public const string DistributedCacheRedisConfigurationSectionName = "DistributedCache:Redis";

	public static IServiceCollection AddLockManager(this IServiceCollection serviceCollection, IConfiguration configuration, bool addStackExchangeRedisCache, Action<LockManagerSetting>? setupAction = null)
	{
		ArgumentNullException.ThrowIfNull(serviceCollection);
		ArgumentNullException.ThrowIfNull(configuration);

		string? redisConnectionString = configuration.GetConnectionString(configuration.GetValue<string>($"{DistributedCacheRedisConfigurationSectionName}:ConnectionStringKey"));
		string? redisInstanceName = configuration.GetValue<string>($"{DistributedCacheRedisConfigurationSectionName}:InstanceName");
		int redisConnectTimeout = Convert.ToInt32(configuration.GetValue<TimeSpan>($"{DistributedCacheRedisConfigurationSectionName}:ConnectTimeout").TotalMilliseconds);

		var configOptions = ConfigurationOptions.Parse(redisConnectionString);
		configOptions.ConnectTimeout = redisConnectTimeout;
		configOptions.AbortOnConnectFail = false;
		var connectionMultiplexer = ConnectionMultiplexer.Connect(configOptions);

		serviceCollection.AddSingleton<IConnectionMultiplexer>(connectionMultiplexer);

		if (addStackExchangeRedisCache)
		{
			serviceCollection.AddStackExchangeRedisCache(options =>
			{
				//x Configuration.GetSection(DistributedCacheRedisConfigurationSectionName).Bind(options);
				options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(connectionMultiplexer);
				options.InstanceName = redisInstanceName;
				// options.Configuration = redisConnectionString;
				// options.ConfigurationOptions = ConfigurationOptions.Parse(options.Configuration);
				// options.ConfigurationOptions.AbortOnConnectFail = true;
			});
		}

		//serviceCollection.Configure<LockManagerSetting>(configuration.GetSection(LockManagerSetting.ConfigurationSectionName));
		serviceCollection.AddOptions<LockManagerSetting>().Bind(configuration.GetSection(LockManagerSetting.ConfigurationSectionName)).ValidateDataAnnotations();
		serviceCollection.PostConfigure<LockManagerSetting>(options => options.InstanceName = redisInstanceName);

		if (setupAction != null)
		{
			serviceCollection.Configure(setupAction);
		}
		serviceCollection.TryAddTransient<ILockManager, LockManager>();

		return serviceCollection;
	}
}
