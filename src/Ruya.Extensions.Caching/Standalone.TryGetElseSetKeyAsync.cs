using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Ruya.Extensions.Caching
{
    public class Helper
    {
        public static async Task<string> TryGetElseSetKeyAsync(string key, ILogger logger, IDistributedCache cache, Func<ILogger, string, Task<string>> externalSourceAsync, string url, TimeSpan absoluteExpirationRelativeToNow)
        {
            // TODO implement ability to bypass CACHE

            const string methodName = nameof(TryGetElseSetKeyAsync);
            string response;
            using (logger.BeginScope("{cacheKey}", key))
            {
                try
                {
                    logger.LogTrace($"[{methodName}] Trying to retrieve data from the cache");
                    response = await cache.GetStringAsync(key);
                    bool existKey = !string.IsNullOrWhiteSpace(response);
                    if (existKey)
                    {
                        logger.LogInformation($"[{methodName}] Data retrieved from the cache");
                        return response;
                    }
                    logger.LogTrace($"[{methodName}] Key does not exist in the cache");
                }
                catch (Exception ex)
                {
                    logger.LogCritical(-1, ex, $"[{methodName}] There is an error occurred while retrieving the data from cache.");
                    response = null;
                    // ReSharper disable once ExpressionIsAlwaysNull
                    return response;
                }

                try
                {
                    logger.LogTrace("Trying to retrieve data from the original source {url}", url);
                    response = await externalSourceAsync(logger, url);

                    logger.LogInformation("Data retrieved from original source.");

                    if (response != null)
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
                    logger.LogCritical(-1, ex, "There is an error occurred while retrieving the data from external source.");
                    response = null;
                }
            }
            return response;
        }
    }
}
