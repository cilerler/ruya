using System;
using Ruya.SignalR.Common;

namespace Ruya.SignalR.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            var writer = Console.Out;
            var client = new Operations(writer);
            client.RunAsync(Constant.Url)
                  .Wait();
        }
    }
}
