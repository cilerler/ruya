using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Core;
using Ruya.Domain;
using Ruya.Domain.ConfigurationSettings;

namespace Ruya.Configuration.Tests
{
    [TestClass]
    public class ConfigurationSettingTest
    {
        [TestMethod, ExpectedException(typeof (ArgumentNullException))]
        public void ConfigurationSettingNotFound()
        {
            // Arrange
            string settingFile = DomainConfigurationSetting.DefaultConfigSectionName.Unencrypted.GetDescription();
            var settings = new DomainConfigurationSetting(Assembly.GetEntryAssembly())
                           {
                               ConfigFileSectionName = settingFile
                           };
            ConfigurationSettingElementCollection<EncryptedSectionsSetting> schemaConfigurationSettingElement = settings.Current.EncryptedSections;


            // Act
            Dictionary<string, string> actualValues = schemaConfigurationSettingElement.All.ToDictionary(schemaSetting => schemaSetting.Name, schemaSetting => schemaSetting.Description);
        }

        [TestMethod]
        public void ConfigurationSettingFound()
        {
            // Arrange
            var expectedValues = new Dictionary<string, string>
                                 {
                                     {
                                         "domainConfigurationEncrypted", "Encrypted Domain Configuration"
                                     },
                                     {
                                         "connectionStrings", "Encrypted connection strings"
                                     }
                                 };
            string sectionName = DomainConfigurationSetting.DefaultConfigSectionName.Unencrypted.GetDescription();
            var settings = new DomainConfigurationSetting(Assembly.GetExecutingAssembly())
                           {
                               ConfigFileSectionName = sectionName
                           };
            ConfigurationSettingElementCollection<EncryptedSectionsSetting> schemaConfigurationSettingElement = settings.Current.EncryptedSections;


            // Act
            Dictionary<string, string> actualValues = schemaConfigurationSettingElement.All.ToDictionary(schemaSetting => schemaSetting.Name, schemaSetting => schemaSetting.Description);

            // Assert
            CollectionAssert.AreEquivalent(expectedValues, actualValues);
        }

        [TestMethod]
        public void ConfigurationSettingEncryptedProtectSection()
        {
            // Arrange            
            string sectionName = DomainConfigurationSetting.DefaultConfigSectionName.Encrypted.GetDescription();
            var configurationProvider = new ConfigurationProvider();
            configurationProvider.SetAssembly(Assembly.GetExecutingAssembly());
            var configurationSection = configurationProvider.GetSection(sectionName) as ConfigurationSection;
            configurationProvider.SetProtectionProvider(ConfigurationProtectionProvider.TripleDes)
                                 .ProtectSection();

            // Act            
            bool actualValue = configurationSection?.SectionInformation.IsProtected ?? false;

            // Assert
            Assert.IsTrue(actualValue);
        }

        [TestMethod]
        public void ConfigurationSettingEncryptedFound()
        {
            // Arrange
            var expectedValues = new Dictionary<string, string>
                                 {
                                     {
                                         "ftp", "12b3"
                                     },
                                     {
                                         "sql", "45a6"
                                     }
                                 };
            string sectionName = DomainConfigurationSetting.DefaultConfigSectionName.Encrypted.GetDescription();
            var settings = new DomainConfigurationSetting(Assembly.GetExecutingAssembly())
                           {
                               ConfigFileSectionName = sectionName
                           };
            ConfigurationSettingElementCollection<PasswordSetting> encryptedConfigurationSettingElement = settings.Current.Passwords;


            // Act
            Dictionary<string, string> actualValues = encryptedConfigurationSettingElement.All.ToDictionary(schemaSetting => schemaSetting.Name, schemaSetting => schemaSetting.Value);

            // Assert
            CollectionAssert.AreEquivalent(expectedValues, actualValues);
        }

        [TestMethod]
        public void ConfigurationSettingEncryptedUnprotectSection()
        {
            // Arrange            
            string sectionName = DomainConfigurationSetting.DefaultConfigSectionName.Encrypted.GetDescription();
            var configurationProvider = new ConfigurationProvider();
            var configurationSection = configurationProvider.SetAssembly(Assembly.GetExecutingAssembly())
                                                            .SetProtectionProvider(ConfigurationProtectionProvider.TripleDes)
                                                            .GetSection(sectionName) as ConfigurationSection;
            configurationProvider.UnprotectSection();

            // Act            
            bool actualValue = configurationSection?.SectionInformation.IsProtected ?? false;

            // Assert
            Assert.IsFalse(actualValue);
        }
    }
}