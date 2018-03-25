using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Ruya.Helpers.Primitives
{
    public partial class Standalone
    {
        public static async Task<string> GetDataFromExternalSourceAsync(ILogger logger, string url, CancellationToken cancellationToken, HttpContent httpContent = null)
        {
            const string methodName = nameof(GetDataFromExternalSourceAsync);
            bool usePostMethod = httpContent != null;
            string responseBodyAsText;
            using (var httpClient = new HttpClient())
            {
                string status = string.Empty;
                try
                {
                    HttpResponseMessage response = usePostMethod ? await httpClient.PostAsync(url, httpContent, cancellationToken) : await httpClient.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    status = $"[{methodName}] {response.StatusCode} {response.ReasonPhrase}";
                    responseBodyAsText = await response.Content.ReadAsStringAsync();
                    logger.LogDebug($"[{methodName}] ResponseBody {{responseBodyAsText}}", responseBodyAsText);
                }
                catch (HttpRequestException hre)
                {
                    logger.LogError(-1, hre, status);
                    responseBodyAsText = null;
                }
            }
            return responseBodyAsText;
        }
    }
}
