using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Ruya.Primitives;

namespace Ruya.AppDomain;

[Serializable]
public class UnhandledExceptionHelper
{
	private const string FileExtension = ".txt";
	private ILogger? _logger;

	public UnhandledExceptionHelper()
	{
		ApplicationDomain = System.AppDomain.CurrentDomain;
	}

	public System.AppDomain ApplicationDomain { get; }

	public void SetLogger(ILoggerFactory logger)
	{
		_logger = logger.CreateLogger(nameof(UnhandledExceptionHelper));
	}


	public void Register(bool ignoreDebugger = false)
	{
		if (ignoreDebugger || !Debugger.IsAttached) ApplicationDomain.UnhandledException += UnhandledExceptionHandler;
	}

	public void LogExistingCrashes(bool deleteLogFile)
	{
		if (_logger == null) return;
		string[] files = Directory.GetFiles(Directory.GetCurrentDirectory(),
			$"{nameof(UnhandledExceptionEventHandler)}{Constants.WildCardStar}{FileExtension}", SearchOption.TopDirectoryOnly);
		if (!files.Any()) _logger.LogDebug("No UnhandledException file found.");
		for (var index = 0;
		     index < files.Length;
		     index++)
		{
			string file = files[index];
			string content = File.ReadAllText(file);
			string suffix = Path.GetFileNameWithoutExtension(file)
				.Replace(nameof(UnhandledExceptionEventHandler), string.Empty)
				.TrimStart();
			if (DateTimeOffset.TryParseExact(suffix, Constants.FileSystemSafeDateTimeOffset, CultureInfo.InvariantCulture, DateTimeStyles.None,
				    out DateTimeOffset dateTimeOffset))
				_logger.LogWarning("Unhandled exception file/s detected. [{index}] [{datetime}] {content}", index / files.Length, dateTimeOffset,
					content);
			if (deleteLogFile) File.Delete(file);
		}
	}

	public void Unregister()
	{
		ApplicationDomain.UnhandledException -= UnhandledExceptionHandler;
	}

	private static string GetExceptionMessage(Exception? exception)
	{
		const char mainSeparator = '=';
		const char subSeparator = '-';
		var contents = new StringBuilder();
		contents.AppendLine(new string(mainSeparator, 79));
		// ReSharper disable once InvertIf
		if (exception != null)
		{
			contents.AppendLine($"{nameof(UnhandledExceptionHelper)} {exception.Message}");
			contents.AppendLine(new string(subSeparator, 39));
			contents.AppendLine(exception.StackTrace);
			if (exception.InnerException != null) contents.AppendLine(GetExceptionMessage(exception.InnerException));
		}

		return contents.ToString();
	}

	private void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs args)
	{
		if (args == null) throw new ArgumentNullException(nameof(args));

		#region template

		var contents = new StringBuilder();

		const char separator = '*';
		contents.AppendLine(new string(separator, 79));

		const string runtimeTerminating = "Runtime terminating:";
		contents.AppendLine($"{runtimeTerminating} {args.IsTerminating}");

		var exception = args.ExceptionObject as Exception;
		contents.AppendLine(GetExceptionMessage(exception));

		contents.AppendLine(new string(separator, 79));

		#endregion


		var output = contents.ToString();
		var path =
			$"{nameof(UnhandledExceptionEventHandler)} {DateTimeOffset.UtcNow.ToString(Constants.FileSystemSafeDateTimeOffset)}{FileExtension}";
		try
		{
			File.WriteAllText(path, output);
		}
		catch (Exception ex)
		{
			throw new Exception(ex.Message, ex);
		}
		finally
		{
			Environment.Exit(-1);
		}

		_logger.LogCritical(output);
	}
}
