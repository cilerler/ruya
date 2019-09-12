using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Owin.Hosting;
using Ruya.SignalR.Common;

namespace Ruya.SignalR.Server
{
    class Program
    {
        static void Main()
        {
            Debug.Listeners.Add(new ConsoleTraceListener());

            // This will *ONLY* bind to localhost, if you want to bind to all addresses
            // use http://*:8080 to bind to all addresses. 
            // See http://msdn.microsoft.com/en-us/library/system.net.httplistener.aspx 
            // for more information.
            
            Process.Start(@"..\..\..\Ruya.SignalR.Client\bin\Debug\Ruya.SignalR.Client.exe");

            using (WebApp.Start(Constant.Url))
            {
                Console.WriteLine("Server running on {0}", Constant.Url);

                string input;
                do
                {
                    input = Console.ReadLine();
                    if (input.ToLowerInvariant() == "getgroups")
                    {
                        var groups = MyHub.GroupList.Select(gl => gl.Value)
                                          .Distinct();
                        if (groups.Any())
                        {
                            foreach (var groupItem in groups)
                            {
                                Console.WriteLine(groupItem);
                            }
                        }
                        else
                        {
                            Console.WriteLine("No groups available");
                        }
                    }

                    if (input.ToLowerInvariant() == "getclients")
                    {
                        if (MyHub.ClientList.Any())
                        {
                            foreach (var client in MyHub.ClientList)
                            {
                                Console.WriteLine("{0} {1}", client.Key, client.Value);
                            }
                        }
                        else
                        {
                            Console.WriteLine("No clients available");
                        }
                    }

                } while (!string.IsNullOrWhiteSpace(input));
            }
        }
    }
}
