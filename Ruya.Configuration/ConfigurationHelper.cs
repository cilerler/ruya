using System.Configuration;

namespace Ruya.Configuration
{
    public static class ConfigurationHelper
    {
        public static string GetSettingFromConfiguration(string settingName, System.Configuration.Configuration configuration = null)
        {
            if (configuration == null)
            {
                configuration = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            }
            SettingElement mySetting = null;
            var keyFound = false;
            foreach (ConfigurationSectionGroup sectionGroup in configuration.SectionGroups)
            {
                foreach (ConfigurationSection section in sectionGroup.Sections)
                {
                    string sectionName = section.SectionInformation.Name;
                    ClientSettingsSection clientSettingSection = null;
                    var compatibleSection = true;
                    try
                    {
                        clientSettingSection = (ClientSettingsSection)sectionGroup.Sections[sectionName];
                    }
                    catch
                    {
                        compatibleSection = false;
                    }
                    if (!compatibleSection)
                    {
                        break;
                    }
                    foreach (SettingElement setting in clientSettingSection.Settings)
                    {
                        keyFound = setting.Name.Equals(settingName);
                        if (!keyFound)
                        {
                            continue;
                        }
                        mySetting = setting;
                        break;
                    }
                    if (keyFound)
                    {
                        break;
                    }
                }
                if (keyFound)
                {
                    break;
                }
            }
            string appSettings = mySetting?.Value.ValueXml.InnerText;
            return appSettings;
        }
    }
}
