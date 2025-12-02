using System.ComponentModel.DataAnnotations;

namespace Ruya.Services.CloudStorage.Google;

public class StorageServiceSettings
{
    public const string ProviderName = "Google";
	public const string ConfigurationSectionName = $"CloudStorage:{ProviderName}";

	[Required]
	public string Credential { get; internal set; } = null!;
}
