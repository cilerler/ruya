using System;
using System.Collections;
using System.Linq;

namespace Ruya.Primitives
{
    public static class EnvironmentHelper
    {
        private const string EnvironmentVariable = "ASPNETCORE_ENVIRONMENT";
        private const string EnvironmentHome = "HOME";
        private const string EnvironmentUserName = "USERNAME";

        private static string _name;
        private static string _home;
        private static string _username;

        public static bool IsDevelopment => Name.Equals(Constants.Development);
        public static bool IsStaging => Name.Equals(Constants.Staging);
        public static bool IsProduction => Name.Equals(Constants.Production);

        // UNDONE unfortunately couldn't figure out any other way to determine this and not sure what will happen if it runs on non-docker LINUX, could be same
        public static bool IsDocker => Home.Equals("/root") || UserName.Equals("ContainerAdministrator");

        public static string Name
        {
            get
            {
                bool nameExist = (_name != null) && _name.Any();
                // ReSharper disable once InvertIf
                if (!nameExist)
                {
                    IDictionary environmentVariables = Environment.GetEnvironmentVariables();
                    if (!environmentVariables.Contains(EnvironmentVariable))
                    {
                        throw new ArgumentNullException($"Environment {EnvironmentVariable} does not exist");
                    }
                    string value = environmentVariables[EnvironmentVariable] as string;
                    _name = value?.ToUpper();
                }
                return _name;
            }
        }

        public static string Home
        {
            get
            {
                bool homeExist = (_home != null) && _home.Any();
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
                bool usernameExist = (_username != null) && _username.Any();
                // ReSharper disable once InvertIf
                if (!usernameExist)
                {
                    IDictionary environmentVariables = Environment.GetEnvironmentVariables();
                    if (!environmentVariables.Contains(EnvironmentUserName))
                    {
                        _username = string.Empty;
                    }
                    _username = environmentVariables[EnvironmentUserName] as string ?? string.Empty;
                }
                return _username;
            }
        }
    }
}
