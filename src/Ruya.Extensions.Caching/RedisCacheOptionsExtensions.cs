using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Redis;
using StackExchange.Redis;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    public static class RedisCacheOptionsExtensions
    {
        public static void ResolveDns(this RedisCacheOptions options)
        {
            string hostWithPort = options.Configuration;
            string resolved = TryResolveDns(hostWithPort);
            string replaced = options.Configuration.Replace(hostWithPort, resolved);
            options.Configuration = replaced;
        }

        private static string TryResolveDns(string redisUrl)
        {
            ConfigurationOptions config = ConfigurationOptions.Parse(redisUrl);

            foreach (EndPoint endPoint in config.EndPoints)
            {
                var addressEndpoint = (DnsEndPoint)endPoint;
                int port = addressEndpoint.Port;
                bool isIp = IsIpAddress(addressEndpoint.Host);
                // ReSharper disable once InvertIf
                if (!isIp)
                {
                    IPHostEntry ip = Dns.GetHostEntryAsync(addressEndpoint.Host)
                                        .GetAwaiter()
                                        .GetResult();
                    return $"{ip.AddressList.First(x => IsIpAddress(x.ToString()))}:{port}";
                }
            }

            return redisUrl;
        }

        private static bool IsIpAddress(string host) => Regex.IsMatch(host, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
    }
}
