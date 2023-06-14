using System;
using Microsoft.Extensions.Logging;

namespace Ruya.Extensions.Logging;

public static partial class LoggerExtensionsHelper
{
	private static readonly Func<ILogger, string, string, string, IDisposable> Scope =
		LoggerMessage.DefineScope<string, string, string>("{ApplicationName}, {ApplicationVersion}, {ApplicationId}");

	private static readonly Action<ILogger, int, int, Exception> Started =
		LoggerMessage.Define<int, int>(LogLevel.Information, 1, $"{new string('-', 19)} Started {{ProcessId}}, {{ThreadManagedId}}");

	private static readonly Action<ILogger, int, int, Exception> Stopping =
		LoggerMessage.Define<int, int>(LogLevel.Information, 1, $"{new string('-', 19)} Stopping {{ProcessId}}, {{ThreadManagedId}}");

	private static readonly Action<ILogger, string, bool, bool, bool, bool, string[], Exception> Initial =
		LoggerMessage.Define<string, bool, bool, bool, bool, string[]>(LogLevel.Information, 2,
			"Environment.Name {EnvironmentName}, Environment.IsDocker {IsDocker}, Environment.UserInteractive {UserInteractive}, Debugger.IsAttached {DebuggerAttached}, ArgsRetrievedFromEnvironmentVariable {argsRetrievedFromEnvironmentVariable}, Args{Args}");

	public static IDisposable ProgramScope(this ILogger logger, string applicationName, string applicationVersion, string applicationId)
	{
		return Scope(logger, applicationName, applicationVersion, applicationId);
	}

	public static void ProgramStarted(this ILogger logger, int processId, int threadManagedId, Exception exception = null)
	{
		Started(logger, processId, threadManagedId, exception);
	}

	public static void ProgramStopping(this ILogger logger, int processId, int threadManagedId, Exception exception = null)
	{
		Stopping(logger, processId, threadManagedId, exception);
	}

	public static void ProgramInitial(this ILogger logger, string environmentName, bool isDocker, bool userInteractive, bool debuggerAttached,
		bool argsRetrievedFromEnvironmentVariable, string[] args, Exception exception = null)
	{
		Initial(logger, environmentName, isDocker, userInteractive, debuggerAttached, argsRetrievedFromEnvironmentVariable, args, exception);
	}
}
