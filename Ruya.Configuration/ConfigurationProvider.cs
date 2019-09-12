using System;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Ruya.Configuration.Properties;
using Ruya.Core;
using Ruya.Diagnostics;

namespace Ruya.Configuration
{
    public class ConfigurationProvider
    {
        /// <summary>
        ///     Config object used to read a custom configuration file.
        ///     If not set the default file will be used.
        /// </summary>
        private System.Configuration.Configuration _configuration;

        private ConfigurationProtectionProvider _protectionProvider;
        private ConfigurationSection _section;
        public KeyValueConfigurationCollection Settings => _configuration.AppSettings.Settings;

        /// <summary>
        ///     Returns an XML node object that represents the associated configuration-section object.
        /// </summary>
        /// <returns>
        ///     The XML representation for this configuration section.
        /// </returns>
        /// <exception cref="T:System.InvalidOperationException">This configuration object is locked and cannot be edited.</exception>
        public string GetRawXml => _section?.SectionInformation.GetRawXml();

        public ConfigurationProvider SetAssembly(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }
            _configuration = ConfigurationManager.OpenExeConfiguration(assembly.Location);
            return this;
        }

        public ConfigurationProvider SetConfigurationFile(string configFileName)
        {
            var fileMap = new ExeConfigurationFileMap
                          {
                              ExeConfigFilename = configFileName
                          };
            _configuration = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);
            return this;
        }

        public ConfigurationProvider SetProtectionProvider(ConfigurationProtectionProvider protectionProvider)
        {
            _protectionProvider = protectionProvider;
            return this;
        }

        public TSection GetSection<TSection>(string sectionName) where TSection : ConfigurationSection, new()
        {
            if (string.IsNullOrEmpty(sectionName))
            {
                // HARD-CODED constant
                string errorMessage = string.Format(CultureInfo.InvariantCulture, "Section name is empty");
                throw new ConfigurationException(errorMessage);
            }
            var section = GetSection(sectionName) as TSection;
            if (section == null)
            {
                string errorMessage = string.Format(CultureInfo.InvariantCulture, Resources.ConfigurationSectionHelper_Section_KeyNotFound, sectionName);
                throw new ConfigurationException(errorMessage);
            }
            return section;
        }

        public ConfigurationProvider SetSection(string sectionName)
        {
            GetSection(sectionName);
            return this;
        }

        /// <summary>
        ///     Get the section from the default configuration file or from the custom one.
        /// </summary>
        /// <returns></returns>
        public object GetSection(string sectionName)
        {
            if (string.IsNullOrEmpty(sectionName))
            {
                throw new ArgumentNullException(nameof(sectionName));
            }

            object output;
            try
            {
                ConfigurationManager.RefreshSection(sectionName);
                output = _configuration != null
                             ? _configuration.GetSection(sectionName)
                             : ConfigurationManager.GetSection(sectionName);
            }
            catch (ConfigurationErrorsException cee)
            {
                throw new ConfigurationException(cee.Message, cee);
            }
            bool keyNotFound = output == null;
            if (keyNotFound)
            {
                string errorMessage = string.Format(CultureInfo.InvariantCulture, Resources.ConfigurationSectionHelper_Section_KeyNotFound, sectionName);
                throw new ConfigurationException(errorMessage);
            }

            _section = output as ConfigurationSection;
            keyNotFound = _section == null;
            if (keyNotFound)
            {
                string errorMessage = string.Format(CultureInfo.InvariantCulture, Resources.ConfigurationSectionHelper_Section_KeyNotFound, sectionName);
                throw new ConfigurationException(errorMessage);
            }
            bool sectionIsLockedOrNotDeclared = _section.SectionInformation.IsLocked || !_section.SectionInformation.IsDeclared;
            if (sectionIsLockedOrNotDeclared)
            {
                string errorMessage = string.Format(CultureInfo.InvariantCulture, Resources.ConfigurationSectionHelper_SectionLocked, sectionName);
                throw new ConfigurationException(errorMessage);
            }
            return output;
        }

        public string GetSettingValue(string key)
        {
            if (Settings[key] == null)
            {
                string errorMessage = string.Format(CultureInfo.InvariantCulture, Resources.ConfigurationSectionHelper_Value_KeyNotFound, key);
                throw new ConfigurationException(errorMessage);
            }
            string output = Settings[key].Value;
            return output;
        }

        /// <summary>
        ///     Marks a configuration section for protection.
        /// </summary>
        /// <exception cref="T:System.InvalidOperationException">
        ///     The
        ///     <see cref="P:System.Configuration.SectionInformation.AllowLocation" /> property is set to false.
        /// </exception>
        public void ProtectSection()
        {
            if (_section == null)
            {
                throw new ConfigurationException();
            }
            if (!_section.SectionInformation.IsProtected)
            {
                string protectionProvider = _protectionProvider.GetDescription();
                SectionInformation sectionInformation = _section.SectionInformation;
                sectionInformation.ProtectSection(protectionProvider);
                Save(Resources.ConfigurationSectionHelper_ProtectSection);
            }
        }

        /// <summary>
        ///     Removes the protected configuration encryption from the associated configuration section.
        /// </summary>
        public void UnprotectSection()
        {
            if (_section == null)
            {
                throw new ConfigurationException();
            }
            if (_section.SectionInformation.IsProtected)
            {
                SectionInformation sectionInformation = _section.SectionInformation;
                sectionInformation.UnprotectSection();
                Save(Resources.ConfigurationSectionHelper_UnprotectSection);
            }
        }

        public void Save(string message)
        {
            if (_section == null)
            {
                throw new ConfigurationException();
            }
            _section.SectionInformation.ForceSave = true;
            _configuration.Save(ConfigurationSaveMode.Minimal, true);
            Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, message);
        }
    }
}