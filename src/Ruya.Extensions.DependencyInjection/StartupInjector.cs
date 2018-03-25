using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ruya.Primitives;

namespace Ruya.Extensions.DependencyInjection
{
    public class StartupInjector
    {
        private Action<IServiceCollection, IConfigurationRoot> _registerExternalServices;
        private Func<Dictionary<string, string>> _registerInMemoryCollection;
        private Func<ILoggingBuilder, ILoggingBuilder> _registerExternalLoggingBuilder;

        public bool CustomDirectoryNameExists { get => !string.IsNullOrWhiteSpace(CustomDirectoryName); }
        public bool ExternalServicesExist { get; private set; }
        public bool InMemoryCollectionExist { get; private set; }
        public bool ExternalLoggingBuilder { get; private set; }

        private string _environmentName;
        public string EnvironmentName
        {
            get
            {
                bool environmentNameExist = !string.IsNullOrWhiteSpace(_environmentName);
                if (!environmentNameExist)
                {
#if DEBUG
                    _environmentName = Constants.Development;
#else
                    _environmentName = Constants.Production;
#endif
                    _environmentName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(_environmentName.ToLower());
                }
                return _environmentName;
            }
            set => _environmentName = value;
        }

        public string CustomDirectoryName { set; get; }
        public Action<IServiceCollection, IConfigurationRoot> RegisterExternalServices
        {
            get => _registerExternalServices;
            set
            {
                _registerExternalServices = value;
                ExternalServicesExist = value != null;
            }
        }

        public Func<Dictionary<string, string>> RegisterInMemoryCollection
        {
            get => _registerInMemoryCollection;
            set
            {
                _registerInMemoryCollection = value;
                InMemoryCollectionExist = value != null;
            }
        }

        public Func<ILoggingBuilder, ILoggingBuilder> RegisterExternalLoggingBuilder
        {
            get => _registerExternalLoggingBuilder;
            set
            {
                _registerExternalLoggingBuilder = value;
                ExternalLoggingBuilder = value != null;
            }
        }

        #region Singleton

        private static readonly Lazy<StartupInjector> Lazy = new Lazy<StartupInjector>(() => new StartupInjector());
        public static StartupInjector Instance => Lazy.Value;

        private StartupInjector()
        { }

        #endregion
    }
}
