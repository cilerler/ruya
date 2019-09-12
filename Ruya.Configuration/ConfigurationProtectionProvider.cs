using System.ComponentModel;

namespace Ruya.Configuration
{
    public enum ConfigurationProtectionProvider
    {
        [Description("RsaProtectedConfigurationProvider")]
        RsaProtected,
        [Description("DataProtectionConfigurationProvider")]
        DataProtection,
        [Description("TripleDesConfigurationProvider")]
        TripleDes
    }
}