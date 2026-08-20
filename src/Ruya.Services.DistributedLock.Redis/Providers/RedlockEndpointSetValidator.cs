using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

using StackExchange.Redis;

namespace Ruya.Services.DistributedLock.Redis.Providers;

internal static class RedlockEndpointSetValidator
{
    public static bool IsValid(IReadOnlyCollection<string> connectionStrings)
    {
        if (connectionStrings.Count < 3 || connectionStrings.Count % 2 == 0)
        {
            return false;
        }

        string?[] identities = connectionStrings.Select(GetIndependentNodeIdentity).ToArray();
        return identities.All(identity => identity is not null) &&
            identities.Distinct(StringComparer.Ordinal).Count() == identities.Length;
    }

    private static string? GetIndependentNodeIdentity(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            ConfigurationOptions options = ConfigurationOptions.Parse(connectionString);
            EndPoint[] endpoints = options.EndPoints.ToArray();
            if (endpoints.Length != 1)
            {
                // One Redlock vote must represent exactly one independently operated node.
                return null;
            }

            int canonicalPort = endpoints[0] switch
            {
                DnsEndPoint dns when dns.Port > 0 => dns.Port,
                IPEndPoint ip when ip.Port > 0 => ip.Port,
                _ => options.Ssl ? 6380 : 6379
            };

            return endpoints[0] switch
            {
                DnsEndPoint dns => $"dns:{dns.Host.TrimEnd('.').ToUpperInvariant()}:{canonicalPort}",
                IPEndPoint ip => $"ip:{ip.Address.MapToIPv6()}:{canonicalPort}",
                _ => endpoints[0].ToString()?.ToUpperInvariant()
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
