using System.Configuration;
using Ruya.Configuration;
using Ruya.Domain.ConfigurationSettings;

// ReSharper disable once CheckNamespace
namespace Ruya.Domain
{
    public partial class DomainConfigurationSetting
    {
        private const string TagPasswords = "passwords";

        [ConfigurationProperty(TagPasswords)]
        public ConfigurationSettingElementCollection<PasswordSetting> Passwords => this[TagPasswords] as ConfigurationSettingElementCollection<PasswordSetting>;
    }
}

namespace Ruya.Domain.ConfigurationSettings
{
        public class PasswordSetting : ConfigurationSettingElement
    {
        private const string TagName = "name";
        private const string TagValue = "value";

        [ConfigurationProperty(TagName, IsRequired = true)]
        public override string Name => this[TagName] as string;

        [ConfigurationProperty(TagValue)]
        public string Value => this[TagValue] as string;
    }
}