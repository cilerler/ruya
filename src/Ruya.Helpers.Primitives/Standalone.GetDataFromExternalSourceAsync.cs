using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Ruya.Helpers.Primitives
{
    public partial class Standalone
    {
		public static async Task<string> GetDataFromExternalSourceAsync(ILogger logger, string url, CancellationToken cancellationToken = default, HttpContent httpContent = null, bool ensureSuccessStatusCode = true)
		{
			const string methodName = nameof(GetDataFromExternalSourceAsync);
			logger.LogTrace("[{MethodName}] {Url}", methodName, url);

			var retry = new RetryWithExponentialBackoff(logger);

			bool usePostMethod = httpContent != null;
			string responseBodyAsText = null;

			async Task Func(CancellationToken ct)
			{
				responseBodyAsText = null;
				using (var httpClient = new HttpClient())
				{
					try
					{
						HttpResponseMessage response = usePostMethod
							                               ? await httpClient.PostAsync(url
							                                                          , httpContent
							                                                          , ct)
							                               : await httpClient.GetAsync(url
							                                                         , ct);
						responseBodyAsText = await response.Content.ReadAsStringAsync();
						logger.LogTrace("[{MethodName}] StatusCode {StatusCode} ReasonPhrase {ReasonPhrase} ResponseBody {responseBodyAsText}"
						              , methodName
						              , response.StatusCode
						              , response.ReasonPhrase
						              , responseBodyAsText);
						if (ensureSuccessStatusCode)
						{
							// UNDONE once https://github.com/dotnet/corefx/issues/26684 get deployed
							//response.EnsureSuccessStatusCode();
							if (!response.IsSuccessStatusCode)
							{
								logger.LogWarning("[{MethodName}] StatusCode {StatusCode} ReasonPhrase {ReasonPhrase} ResponseBody {responseBodyAsText}"
											, methodName
											, response.StatusCode
											, response.ReasonPhrase
											, responseBodyAsText);
							}
						}
					}
					catch (OperationCanceledException oce)
					{
						logger.LogInformation($"Cancellation requested {oce.Message}");
					}
				}

			}

			await retry.RunAsync(Func
			                   , cancellationToken);

			return responseBodyAsText;
        }
    }
}
