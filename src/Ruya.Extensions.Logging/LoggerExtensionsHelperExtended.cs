using System;
using Microsoft.Extensions.Logging;

namespace Ruya.Extensions.Logging
{
    public static partial class LoggerExtensionsHelper
    {
        private static readonly Func<ILogger, string, string, IDisposable> Initializing = LoggerMessage.DefineScope<string, string>("{ApplicationName} :: {ApplicationId}");

        private static readonly Action<ILogger, string, bool, bool, int, int, string[], Exception> Started = LoggerMessage.Define<string, bool, bool, int, int, string[]>(LogLevel.Information, 2, $"{new string('-', 19)} Started EnvironmentName {{EnvironmentName}}, Environment.UserInteractive {{UserInteractive}}, Debugger.IsAttached {{DebuggerAttached}}, ProcessId {{ProcessId}}, ThreadManagedId {{ThreadManagedId}}, Args{{Args}}");

        private static readonly Action<ILogger, int, int, Exception> Stopping = LoggerMessage.Define<int, int>(LogLevel.Information, 1, $"{new string('-', 19)} Stopping {{ProcessId}}, {{ThreadManagedId}}");

        public static IDisposable ProgramScope(this ILogger logger, string applicationName, string applicationId) => Initializing(logger, applicationName, applicationId);

        public static void ProgramStarted(this ILogger logger, string environmentName, bool userInteractive, bool debuggerAttached, int processId, int threadManagedId, string[] args, Exception exception = null) => Started(logger, environmentName, userInteractive, debuggerAttached, processId, threadManagedId, args, exception);

        public static void ProgramStopping(this ILogger logger, int processId, int threadManagedId, Exception exception = null) => Stopping(logger, processId, threadManagedId, exception);
    }
}
