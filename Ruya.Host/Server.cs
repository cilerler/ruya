using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Owin.Hosting;
using Ruya.Diagnostics;
using Ruya.IO;

namespace Ruya.Host
{
    public static class Server
    {
        private static AutoResetEvent _autoResetEvent;

        public static void Stop()
        {
            _autoResetEvent.Set();
        }

        public static void Start(IList<string> serverAddresses, Action action, bool setAddressToMemory)
        {
            if (serverAddresses == null)
            {
                throw new ArgumentNullException(nameof(serverAddresses));
            }
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            
            var options = new StartOptions();
            foreach (string serverAddress in serverAddresses)
            {
                options.Urls.Add(serverAddress);
            }
            string serverAddressesJoined = string.Join(",", serverAddresses);
            
            using (WebApp.Start(options))
            {
                FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetEntryAssembly().Location);

                MemoryMappedFile memoryMappedFile = null;
                if (setAddressToMemory)
                {
                    int capacity = Encoding.UTF8.GetByteCount(serverAddressesJoined);
                    MemoryMappedFileHelper.Write(serverAddressesJoined, Ruya.Connector.Interfaces.Constants.ServerMemoryMappedFileName, capacity, fileVersionInfo.ProductName, out memoryMappedFile);
                }
                
                // HARD-CODED constant
                string traceMessage1 = $"{fileVersionInfo.ProductName} running on {serverAddressesJoined}";
                Tracer.Instance.TraceEvent(TraceEventType.Start, 0, traceMessage1);
                if (Environment.UserInteractive)
                {
                    Console.WriteLine(traceMessage1);
                }
                                
                Task.Run(action);

                _autoResetEvent = new AutoResetEvent(false);
                _autoResetEvent.WaitOne();

                // HARD-CODED constant
                string traceMessage2 = $"{fileVersionInfo.ProductName} is no longer running on {serverAddressesJoined}";
                Tracer.Instance.TraceEvent(TraceEventType.Stop, 0, traceMessage2);
                if (Environment.UserInteractive)
                {
                    Console.WriteLine(traceMessage2);
                }

                memoryMappedFile?.Dispose();
            }
        }        
    }
}