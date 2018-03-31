using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Ruya.Helpers.Primitives
{
    public partial class Standalone
    {
        public static async Task<string> GetDataFromExternalSourceAsync(ILogger logger, string url, CancellationToken cancellationToken = default(CancellationToken), HttpContent httpContent = null)
        {
            const string methodName = nameof(GetDataFromExternalSourceAsync);
            var retry = new RetryWithExponentialBackoff(logger);

            bool usePostMethod = httpContent != null;
            string responseBodyAsText = null;
            using (var httpClient = new HttpClient())
            {
                async Task Func(CancellationToken ct)
                {
                    try
                    {
                        HttpResponseMessage response = usePostMethod
                                                           // ReSharper disable AccessToDisposedClosure
                                                           ? await httpClient.PostAsync(url, httpContent, ct)
                                                           : await httpClient.GetAsync(url, ct);
                                                           // ReSharper restore AccessToDisposedClosure
                        response.EnsureSuccessStatusCode();
                        responseBodyAsText = await response.Content.ReadAsStringAsync();
                        logger.LogTrace($"[{methodName}] StatusCode {{StatusCode}} ReasonPhrase {{ReasonPhrase}} ResponseBody {{responseBodyAsText}}", response.StatusCode, response.ReasonPhrase, responseBodyAsText);
                    }
                    catch (OperationCanceledException oce)
                    {
                        logger.LogInformation($"Cancellation requested {oce.Message}");
                    }
                }

                await retry.RunAsync(Func, cancellationToken);
            }
            return responseBodyAsText;
        }
    }
}
