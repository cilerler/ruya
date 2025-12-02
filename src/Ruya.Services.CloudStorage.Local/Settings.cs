using System.ComponentModel.DataAnnotations;

namespace Ruya.Services.CloudStorage.Local;

public class StorageServiceSettings
{
    public const string ProviderName = "Local";
	public const string ConfigurationSectionName = $"CloudStorage:{ProviderName}";

	[Required]
	public string Path { get; set; } = null!;
}
