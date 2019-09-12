using System.ComponentModel;
using System.Reflection;
using Ruya.Configuration;

namespace Ruya.Domain
{
    public partial class DomainConfigurationSetting : ConfigurationSetting
    {
        public enum DefaultConfigSectionName
        {
            // HARD-CODED constant
            [Description("domainConfiguration")]Unencrypted,
            [Description("domainConfigurationEncrypted")]Encrypted
        }

        public new string ConfigFileSectionName { get; set; }
        
        public new DomainConfigurationSetting Current => Provider.GetSection<DomainConfigurationSetting>(ConfigFileSectionName);
        
        public DomainConfigurationSetting()
        {   
        }
        
        public DomainConfigurationSetting(Assembly assembly) : base(assembly)
        {   
        }
    }
}
