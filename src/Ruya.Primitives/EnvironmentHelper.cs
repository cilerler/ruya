using System;

namespace Ruya.Primitives
{
    public static class EnvironmentHelper
    {
		private const string EnvironmentVariable = "ENVIRONMENT";
        private const string EnvironmentAspNetCore = "ASPNETCORE_ENVIRONMENT";
		private const string EnvironmentAspNetCoreUrls = "ASPNETCORE_URLS";
		private const string EnvironmentDotNetRunningInContainer = "DOTNET_RUNNING_IN_CONTAINER";
		private const string EnvironmentDotNetUsePollingFileWatcher = "DOTNET_USE_POLLING_FILE_WATCHER";

        public static bool IsDevelopment => EnvironmentName.Equals(Constants.Development);
        public static bool IsStaging => EnvironmentName.Equals(Constants.Staging);
        public static bool IsProduction => EnvironmentName.Equals(Constants.Production);

		public static bool IsDocker => bool.TryParse(Environment.GetEnvironmentVariable(EnvironmentDotNetRunningInContainer), out bool isDocker) && isDocker;

		//TODO DELETE once everything is settled
		//private const string EnvironmentHome = "HOME";
		//private const string EnvironmentUserName = "USERNAME";
		// UNDONE unfortunately couldn't figure out any other way to determine this setting and not sure what will happen if it runs on non-docker LINUX, could be same
		// public static bool IsDocker => Home.Equals("/root") || UserName.Equals("ContainerAdministrator");

		private static string _environmentName;

		public static string EnvironmentName
		{
			get
			{
				bool nameExist = !string.IsNullOrWhiteSpace(_environmentName);
				if (nameExist)
				{
					return _environmentName;
				}

				string environmentVariable = Environment.GetEnvironmentVariable(EnvironmentVariable);
				bool environmentVariableExists = !string.IsNullOrWhiteSpace(environmentVariable);
				if (!environmentVariableExists)
				{
					string environmentAspNetCore = Environment.GetEnvironmentVariable(EnvironmentAspNetCore);
					bool environmentAspNetCoreExists = !string.IsNullOrWhiteSpace(environmentAspNetCore);
					if (!environmentAspNetCoreExists)
					{
						throw new ArgumentNullException($"Environment variables {EnvironmentVariable} and {EnvironmentAspNetCore} do not exist.");
					}
					environmentVariable = environmentAspNetCore;
				}

				_environmentName = environmentVariable?.ToUpper();
				return _environmentName;
			}
			set
			{
				_environmentName = value;
				Environment.SetEnvironmentVariable(EnvironmentVariable, _environmentName);
				Environment.SetEnvironmentVariable(EnvironmentAspNetCore, _environmentName);
			}
		}

		private static string[] _environmentArgs;
		public static string[] EnvironmentArgs
		{
			get
			{
				if (_environmentArgs != null)
				{
					return _environmentArgs;
				}

				const string environmentArguments1 = "ASPNETCORE_ARGS";
				const string environmentArguments2 = "ASPNETCORE_ARGUMENTS";
				string argumentsHolder = Environment.GetEnvironmentVariable(environmentArguments1) ?? Environment.GetEnvironmentVariable(environmentArguments2);
				_environmentArgs = argumentsHolder?.Split(ControlChars.Space) ?? new string[] {};
				return _environmentArgs;
			}
		}
	}
}
