using System.Configuration;
using Ruya.Configuration;
using Ruya.Domain.ConfigurationSettings;

// ReSharper disable once CheckNamespace
namespace Ruya.Domain
{
    public partial class DomainConfigurationSetting
    {
        private const string TagEncryptedSections = "encryptedSections";

        [ConfigurationProperty(TagEncryptedSections)]
        public ConfigurationSettingElementCollection<EncryptedSectionsSetting> EncryptedSections => this[TagEncryptedSections] as ConfigurationSettingElementCollection<EncryptedSectionsSetting>;
    }
}

namespace Ruya.Domain.ConfigurationSettings
{
    public class EncryptedSectionsSetting : ConfigurationSettingElement
    {
        private const string TagName = "name";
        private const string TagDescription = "description";

        [ConfigurationProperty(TagName, IsRequired = true)]
        public override string Name => this[TagName] as string;

        [ConfigurationProperty(TagDescription)]
        public string Description => this[TagDescription] as string;
    }
}