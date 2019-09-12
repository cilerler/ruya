using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Owin.Hosting;
using Ruya.Diagnostics;

namespace Ruya.SignalR
{
    public static class Starter
    {
        public static void RunServer(Uri uri, bool displayStatusMessage, Func<bool> validateEntry)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            if (validateEntry == null)
            {
                throw new ArgumentNullException(nameof(validateEntry));
            }
            using (WebApp.Start(uri.ToString()))
            {
                FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetEntryAssembly().Location);
                if (displayStatusMessage)
                {
                    // HARD-CODED constant
                    Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, "{0} running on {1}", fileVersionInfo.ProductName, uri);
                }

                bool go;
                do
                {
                    go = validateEntry();
                }
                while (go);

            }
        }
    }
}