using System.Configuration;
using System.Reflection;
using Ruya.Diagnostics;

namespace Ruya.Configuration
{
    public class ConfigurationSetting : ConfigurationSection
    {
        // make sure that *.config file's "Copy to Output Direcory" property is marked as "Copy always"
        public string ConfigFileSectionName { get; set; } = Assembly.GetExecutingAssembly().GetFileVersionInfo().ProductName;

        protected ConfigurationProvider Provider { get; set; } = new ConfigurationProvider();

        protected ConfigurationSetting Current => Provider.GetSection<ConfigurationSetting>(ConfigFileSectionName);

        #region Singleton
        //x private static readonly Lazy<Settings> Lazy = new Lazy<Settings>(() => new Settings());
        //x public static Settings Instance => Lazy.Value;
        //x private Settings(){}
        #endregion

        public ConfigurationSetting() { }

        protected ConfigurationSetting(Assembly assembly)
        {
            Provider = Provider.SetAssembly(assembly);
        }
        
        private const string TagXmlNamespaceSchemaInstance = "xmlns:xsi";
        [ConfigurationProperty(TagXmlNamespaceSchemaInstance, IsRequired = false)]
        public string XmlNamespaceSchemaInstance => this[TagXmlNamespaceSchemaInstance] as string;

        private const string TagXmlNoNamespaceSchemaLocation = "xsi:noNamespaceSchemaLocation";
        [ConfigurationProperty(TagXmlNoNamespaceSchemaLocation, IsRequired = false)]
        public string XmlNoNamespaceSchemaLocation => this[TagXmlNoNamespaceSchemaLocation] as string;
    }
}
