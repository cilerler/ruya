using System;
using System.Collections;

namespace Ruya.Primitives
{
    public static class EnvironmentHelper
    {
        private const string EnvironmentVariable = "ASPNETCORE_ENVIRONMENT";
        private const string EnvironmentHome = "HOME";
        private const string EnvironmentUserName = "USERNAME";

        private static string _environmentName;
        private static string _home;
        private static string _userName;

        public static bool IsDevelopment => EnvironmentName.Equals(Constants.Development);
        public static bool IsStaging => EnvironmentName.Equals(Constants.Staging);
        public static bool IsProduction => EnvironmentName.Equals(Constants.Production);

        // UNDONE unfortunately couldn't figure out any other way to determine this setting and not sure what will happen if it runs on non-docker LINUX, could be same
        public static bool IsDocker => Home.Equals("/root") || UserName.Equals("ContainerAdministrator");

        public static string EnvironmentName
        {
            get
            {
                bool nameExist = !string.IsNullOrWhiteSpace(_environmentName);
                // ReSharper disable once InvertIf
                if (!nameExist)
                {
                    IDictionary environmentVariables = Environment.GetEnvironmentVariables();
                    if (!environmentVariables.Contains(EnvironmentVariable))
                    {
                        throw new ArgumentNullException($"Environment {EnvironmentVariable} does not exist");
                    }
                    string value = environmentVariables[EnvironmentVariable] as string;
                    _environmentName = value?.ToUpper();
                }
                return _environmentName;
            }
        }

        public static string Home
        {
            get
            {
                bool homeExist = !string.IsNullOrWhiteSpace(_home);
                // ReSharper disable once InvertIf
                if (!homeExist)
                {
                    IDictionary environmentVariables = Environment.GetEnvironmentVariables();
                    if (!environmentVariables.Contains(EnvironmentHome))
                    {
                        _home = string.Empty;
                    }
                    _home = environmentVariables[EnvironmentHome] as string ?? string.Empty;
                }
                return _home;
            }
        }

        public static string UserName
        {
            get
            {
                bool usernameExist = !string.IsNullOrWhiteSpace(_userName);
                // ReSharper disable once InvertIf
                if (!usernameExist)
                {
                    IDictionary environmentVariables = Environment.GetEnvironmentVariables();
                    if (!environmentVariables.Contains(EnvironmentUserName))
                    {
                        _userName = string.Empty;
                    }
                    _userName = environmentVariables[EnvironmentUserName] as string ?? string.Empty;
                }
                return _userName;
            }
        }
    }
}
