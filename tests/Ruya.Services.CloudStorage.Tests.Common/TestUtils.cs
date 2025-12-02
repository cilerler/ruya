using System.Net;
using System.Net.Sockets;

namespace Ruya.Services.CloudStorage.Tests.Common;

public static class TestUtils
{
    public static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
