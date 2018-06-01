using System;

namespace Ruya.Primitives
{
    public static class EnvironmentHelper
    {
        private const string EnvironmentAspNetCore = "ASPNETCORE_ENVIRONMENT";
		private const string EnvironmentDotNetRunningInContainer = "DOTNET_RUNNING_IN_CONTAINER";

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
                // ReSharper disable once InvertIf
                if (!nameExist)
                {
					string environmentVariable = Environment.GetEnvironmentVariable(EnvironmentAspNetCore);
	                bool environmentVariableExists = !string.IsNullOrWhiteSpace(environmentVariable);
                    if (!environmentVariableExists)
					{
                        throw new ArgumentNullException($"Environment {EnvironmentAspNetCore} does not exist");
                    }
                    _environmentName = environmentVariable?.ToUpper();
                }
                return _environmentName;
            }
            set
            {
                _environmentName = value;
                Environment.SetEnvironmentVariable(EnvironmentAspNetCore, _environmentName);
            }
        }		
    }
}
