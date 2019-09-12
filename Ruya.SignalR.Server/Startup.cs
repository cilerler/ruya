using System;
using System.Diagnostics;
using Microsoft.AspNet.SignalR;
using Owin;
using Microsoft.Owin.Cors;
using Microsoft.Owin;
using Ruya.SignalR.Server;

[assembly: OwinStartup(typeof(Startup))]
namespace Ruya.SignalR.Server
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            // For more information on how to configure your application, visit http://go.microsoft.com/fwlink/?LinkID=316888

            GlobalHost.TraceManager.Switch.Level = SourceLevels.Verbose;

            GlobalHost.HubPipeline.AddModule(new ErrorHandlingPipelineModule());
            GlobalHost.HubPipeline.AddModule(new LoggingPipelineModule());

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

            app.UseCors(CorsOptions.AllowAll);
            var hubConfiguration = new HubConfiguration
                                   {
                                       EnableDetailedErrors = true,
                                       EnableJavaScriptProxies = false
                                   };
            app.MapSignalR("/signalr", hubConfiguration);
        }
    }
}
