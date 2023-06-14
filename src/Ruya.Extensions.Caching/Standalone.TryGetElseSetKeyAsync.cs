using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Ruya.Extensions.Caching;

public class Helper
{
	public static async Task<string> TryGetElseSetKeyAsync(string key, ILogger logger, IDistributedCache cache,
		Func<ILogger, string, CancellationToken, HttpContent, bool, Task<string>> externalSource, string url,
		TimeSpan absoluteExpirationRelativeToNow, bool enableCache)
	{
		string response;
		using (logger.BeginScope("{cacheKey}", key))
		{
			if (enableCache)
				try
				{
					logger.LogTrace("Attempting to retrieve data from the cache");
					response = await cache.GetStringAsync(key);
					bool existKey = !string.IsNullOrWhiteSpace(response);
					if (existKey)
					{
						logger.LogTrace("Successfully retrieved data from the cache");
						return response;
					}

					logger.LogDebug("The specified key does not exist in the cache");
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex, "An error occurred while retrieving the data from the cache");
					response = null;
				}

			try
			{
				logger.LogTrace("Attempting to retrieve data from the original source at {url}", url);
				response = await externalSource(logger, url, default, null, true);

				logger.LogInformation("Retrieved data from the original source");

				if (enableCache && response != null)
				{
					logger.LogTrace("Saving the response into the cache");
					await cache.SetStringAsync(key, response,
						new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow });
					logger.LogTrace("Data has been successfully saved into the cache");
				}
			}
			catch (Exception ex)
			{
				logger.LogCritical(ex, "An error occurred while retrieving the data from the external source");
				response = null;
			}
		}

		return response;
	}
}
