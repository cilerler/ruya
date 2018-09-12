using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Ruya.Extensions.Caching
{
    public class Helper
    {
        public static async Task<string> TryGetElseSetKeyAsync(string key, ILogger logger, IDistributedCache cache, Func<ILogger, string, CancellationToken, HttpContent, bool, Task<string>> externalSourceAsync, string url, TimeSpan absoluteExpirationRelativeToNow, bool enableCache)
        {
			string response;
            using (logger.BeginScope("{cacheKey}", key))
            {
				if (enableCache)
				{
					try
					{
						logger.LogTrace("Trying to retrieve data from the cache");
						response = await cache.GetStringAsync(key);
						bool existKey = !string.IsNullOrWhiteSpace(response);
						if (existKey)
						{
							logger.LogInformation("Data retrieved from the cache");
							return response;
						}
						logger.LogTrace("Key does not exist in the cache");
					}
					catch (Exception ex)
					{
						logger.LogWarning(ex, "There is an error occurred while retrieving the data from cache.");
						response = null;
					}
				}
                try
                {
                    logger.LogTrace("Trying to retrieve data from the original source {url}", url);
					response = await externalSourceAsync(logger, url, default, null, true);

                    logger.LogInformation("Data retrieved from original source.");

                    if (enableCache && response != null)
                    {
                        logger.LogTrace("Saving response into the cache");
                        await cache.SetStringAsync(key,
                                                   response,
                                                   new DistributedCacheEntryOptions
                                                   {
                                                       AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow
                                                   });
                        logger.LogTrace("Data has been saved into the cache.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "There is an error occurred while retrieving the data from external source.");
                    response = null;
                }
            }
            return response;
        }
    }
}
