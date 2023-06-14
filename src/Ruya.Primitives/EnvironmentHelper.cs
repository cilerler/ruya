using System;

namespace Ruya.Primitives;

public static class EnvironmentHelper
{
	//TODO DELETE once everything is settled
	//private const string EnvironmentHome = "HOME";
	//private const string EnvironmentUserName = "USERNAME";
	// UNDONE unfortunately couldn't figure out any other way to determine this setting and not sure what will happen if it runs on non-docker LINUX, could be same
	// public static bool IsDocker => Home.Equals("/root") || UserName.Equals("ContainerAdministrator");

	private static string? _environmentName;
	private static string[]? _environmentArgs;

	public static bool IsDevelopment => EnvironmentName.Equals(Constants.Development);
	public static bool IsStaging => EnvironmentName.Equals(Constants.Staging);
	public static bool IsProduction => EnvironmentName.Equals(Constants.Production);

	public static bool IsDocker =>
		bool.TryParse(Environment.GetEnvironmentVariable(EnvironmentDotNetRunningInContainer), out bool isDocker) && isDocker;

	public static string EnvironmentName
	{
		get
		{
			bool nameExist = !string.IsNullOrWhiteSpace(_environmentName);
			if (nameExist)
			{
#pragma warning disable CS8603
				return _environmentName;
#pragma warning restore CS8603
			}

			string environmentVariable = Environment.GetEnvironmentVariable(EnvironmentVariable) ??
			                             Environment.GetEnvironmentVariable(EnvironmentAspNetCore) ??
			                             Environment.GetEnvironmentVariable(EnvironmentDotNetCore) ??
			                             throw new ArgumentNullException(
				                             $"Environment variables {EnvironmentVariable} and {EnvironmentAspNetCore} do not exist.");
			return environmentVariable.ToUpper();
		}
		set
		{
			_environmentName = value;
			Environment.SetEnvironmentVariable(EnvironmentVariable, _environmentName);
			Environment.SetEnvironmentVariable(EnvironmentDotNetCore, _environmentName);
			Environment.SetEnvironmentVariable(EnvironmentAspNetCore, _environmentName);
		}
	}

	public static string[] EnvironmentArgs
	{
		get
		{
			if (_environmentArgs != null) return _environmentArgs;

			const string environmentArguments1 = "ASPNETCORE_ARGS";
			const string environmentArguments2 = "ASPNETCORE_ARGUMENTS";
			string? argumentsHolder = Environment.GetEnvironmentVariable(environmentArguments1) ??
			                          Environment.GetEnvironmentVariable(environmentArguments2);
			_environmentArgs = argumentsHolder?.Split(ControlChars.Space) ?? Array.Empty<string>();
			return _environmentArgs;
		}
	}
// ReSharper disable UnusedMember.Local
#pragma warning disable IDE0051
	private const string EnvironmentVariable = "ENVIRONMENT";
	private const string EnvironmentAspNetCore = "ASPNETCORE_ENVIRONMENT";
	private const string EnvironmentDotNetCore = "DOTNET_ENVIRONMENT";
	private const string EnvironmentAspNetCoreUrls = "ASPNETCORE_URLS";
	private const string EnvironmentAspNetCoreHttpsPort = "ASPNETCORE_HTTPS_PORT";
	private const string EnvironmentAspNetCorePort = "ASPNETCORE_PORT";
	private const string EnvironmentDotNetRunningInContainer = "DOTNET_RUNNING_IN_CONTAINER";
	private const string EnvironmentDotNetUsePollingFileWatcher = "DOTNET_USE_POLLING_FILE_WATCHER";
#pragma warning restore IDE0051
// ReSharper restore UnusedMember.Local
}
