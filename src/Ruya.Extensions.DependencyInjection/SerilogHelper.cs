using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.Slack;

namespace Ruya.Extensions.DependencyInjection
{
    public class SerilogHelper
    {
        public static void Register(IConfiguration configuration)
        {
            LoggerConfiguration loggerConfiguration = new LoggerConfiguration().ReadFrom.Configuration(configuration);

            #region Retrieve Serilog Slack Sink (until they fix https://github.com/mgibas/serilog-sinks-slack/issues/15)

            const string sectionName = "SerilogSinkSlack";
            if (configuration.GetSection(sectionName)
                             .Exists())
            {
                var slackSinkOptions = JsonConvert.DeserializeObject<SlackSinkOptions>(JsonConvert.SerializeObject(configuration.GetSection(sectionName)
                                                                                                                                .GetChildren()
                                                                                                                                .ToDictionary(item => item.Key, item => item.Value)));
                loggerConfiguration = loggerConfiguration.WriteTo.Slack(slackSinkOptions, restrictedToMinimumLevel: LogEventLevel.Fatal);
            }

            #endregion

            Log.Logger = loggerConfiguration.CreateLogger();

#if DEBUG
            SelfLog.Enable(Console.Out);
#endif
        }
    }
}
