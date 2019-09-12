using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Ruya.Configuration;
using Ruya.Core;
using Ruya.Diagnostics;
using Ruya.Domain.Properties;

namespace Ruya.Domain
{
    public static class DomainHelper
    {
        private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();

        public static void DetectCurrentDirectory()
        {
            //! do not use "AppDomain.CurrentDomain.BaseDirectory" it behaves differently based on environment (Command Prompt vs Windows Service)
            string applicationDirectory = Path.GetDirectoryName(Assembly.Location);
            if (applicationDirectory == null)
            {
                throw new DomainException(Resources.SetCurrentDirectory);
            }
            Directory.SetCurrentDirectory(applicationDirectory);
        }

        public static void ProtectSection(string[] args, Assembly assembly)
        {
            bool guard = !args.Any();
            if (guard)
            {
                return;
            }
            const string runOnceParameter = "build";

            // TODO Get section name config file
            string encryptedSectionName = DomainConfigurationSetting.DefaultConfigSectionName.Encrypted.GetDescription();
            bool protectTheSection = !string.IsNullOrEmpty(encryptedSectionName) && args.First().Equals(runOnceParameter);
            if (protectTheSection)
            {
                var configurationProvider = new ConfigurationProvider();
                configurationProvider.SetAssembly(assembly)
                                     .SetProtectionProvider(ConfigurationProtectionProvider.TripleDes)
                                     .SetSection(encryptedSectionName)
                                     .ProtectSection();
                Environment.Exit(0);
            }
        }
    }
}