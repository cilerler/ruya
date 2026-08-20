using System.Threading.Tasks;

using StackExchange.Redis;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis;

internal interface IRedisConnectionFactory
{
    IConnectionMultiplexer Connect(string connectionString);

    Task<IConnectionMultiplexer> ConnectAsync(string connectionString);
}

internal sealed class RedisConnectionFactory : IRedisConnectionFactory
{
    public IConnectionMultiplexer Connect(string connectionString) =>
        ConnectionMultiplexer.Connect(connectionString);

    public async Task<IConnectionMultiplexer> ConnectAsync(string connectionString) =>
        await ConnectionMultiplexer.ConnectAsync(connectionString).ConfigureAwait(false);
}
