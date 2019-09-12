using System.Configuration;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Configuration.Tests
{
    [TestClass]
    public class ConfigurationProviderTest
    {
        [TestMethod, ExpectedException(typeof (ConfigurationException))]
        public void GetSettingValueNotFound()
        {
            // Arrange
            Assembly assembly = Assembly.GetExecutingAssembly();
            ConfigurationProvider configurationProvider = new ConfigurationProvider().SetAssembly(assembly);
            const string expectedValue = null;

            // Act
            string actualValue = configurationProvider.GetSettingValue("default");

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void GetSettingValueValueFound()
        {
            // Arrange
            Assembly assembly = Assembly.GetExecutingAssembly();
            ConfigurationProvider configurationProvider = new ConfigurationProvider().SetAssembly(assembly);
            const string expectedValue = "defaultValue";

            // Act
            string actualValue = configurationProvider.GetSettingValue("defaultKey");

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod, ExpectedException(typeof (ConfigurationException))]
        public void GetSectionNotFound()
        {
            // Arrange
            const string sectionName = "defaultConfig";
            Assembly assembly = Assembly.GetExecutingAssembly();
            ConfigurationProvider configurationProvider = new ConfigurationProvider().SetAssembly(assembly);

            // Act            
            configurationProvider.SetSection(sectionName);

            // Assert
        }

        [TestMethod]
        public void GetSectionFound()
        {
            // Arrange
            const string sectionName = "defaultConfiguration";
            Assembly assembly = Assembly.GetExecutingAssembly();
            System.Configuration.Configuration configuration = ConfigurationManager.OpenExeConfiguration(assembly.Location);
            ConfigurationSection expectedValue = configuration.GetSection(sectionName);

            ConfigurationProvider configurationProvider = new ConfigurationProvider().SetAssembly(assembly);


            // Act
            var actualValue = configurationProvider.GetSection(sectionName) as ConfigurationSection;

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }
        
    }
}