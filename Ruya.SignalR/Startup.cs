using System;
using System.Diagnostics;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin;
using Owin;
using Microsoft.Owin.Cors;
using Ruya.SignalR;

[assembly: OwinStartup("FrameworkStartup", typeof(Startup))]
namespace Ruya.SignalR
{
    public static class Startup
    {
        public static bool InitialValuesSet = false;

        public static void Configuration(IAppBuilder appBuilder)
        {
            // For more information on how to configure your application, visit http://go.microsoft.com/fwlink/?LinkID=316888

            if (!InitialValuesSet)
            { 
                GlobalHost.TraceManager.Switch.Level = SourceLevels.All;

                try
                {
                    GlobalHost.HubPipeline.AddModule(new ErrorHandlingHubPipelineModule());
                }
                catch (Exception ex)
                {
                }
                try
                {
                    GlobalHost.HubPipeline.AddModule(new LoggingHubPipelineModule());
                }
                catch (Exception ex)
                {
                }

                // Make long polling connections wait a maximum of 110 seconds for a
                // response. When that time expires, trigger a timeout command and
                // make the client reconnect.
                GlobalHost.Configuration.ConnectionTimeout = TimeSpan.FromSeconds(110);

                // Wait a maximum of 30 seconds after a transport connection is lost
                // before raising the Disconnected event to terminate the SignalR connection.
                GlobalHost.Configuration.DisconnectTimeout = TimeSpan.FromSeconds(30);

                // For transports other than long polling, send a keepalive packet every
                // 10 seconds. 
                // This value must be no more than 1/3 of the DisconnectTimeout value.
                GlobalHost.Configuration.KeepAlive = TimeSpan.FromSeconds(10);

                InitialValuesSet = true;
            }
            appBuilder.UseCors(CorsOptions.AllowAll);
            var hubConfiguration = new HubConfiguration
            {
                EnableDetailedErrors = true,
                EnableJavaScriptProxies = true,
                //x EnableJSONP = true
            };

            const string mapPath = "/signalr";
            appBuilder.MapSignalR(mapPath, hubConfiguration);
        }
    }
}
