using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ruya.ConsoleHost
{
    public class StartupInjector
    {
        private string[] _args;
        private Func<ILoggingBuilder, ILoggingBuilder> _externalLoggingBuilder;
        private Action<IServiceCollection, IConfiguration> _externalServices;
        private Func<Dictionary<string, string>> _inMemoryCollection;

        public bool CustomDirectoryNameExists => !string.IsNullOrWhiteSpace(CustomDirectoryName);
        public bool ExternalServicesExist { get; private set; }
        public bool InMemoryCollectionExists { get; private set; }
        public bool ExternalLoggingBuilderExists { get; private set; }
        public bool ArgsExist { get; private set; }
        public string CustomDirectoryName { set; get; }

        public Action<IServiceCollection, IConfiguration> ExternalServices
        {
            get => _externalServices;
            set
            {
                _externalServices = value;
                ExternalServicesExist = value != null;
            }
        }

        public string[] Args
        {
            get => _args;
            set
            {
                _args = value;
                ArgsExist = value != null;
            }
        }

        public Func<Dictionary<string, string>> InMemoryCollection
        {
            get => _inMemoryCollection;
            set
            {
                _inMemoryCollection = value;
                InMemoryCollectionExists = value != null;
            }
        }

        public Func<ILoggingBuilder, ILoggingBuilder> ExternalLoggingBuilder
        {
            get => _externalLoggingBuilder;
            set
            {
                _externalLoggingBuilder = value;
                ExternalLoggingBuilderExists = value != null;
            }
        }

        #region Singleton

        private static readonly Lazy<StartupInjector> Lazy = new Lazy<StartupInjector>(() => new StartupInjector());
        public static StartupInjector Instance => Lazy.Value;

        private StartupInjector()
        {
        }

        #endregion
    }
}
