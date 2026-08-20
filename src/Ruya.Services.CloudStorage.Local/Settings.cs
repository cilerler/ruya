using System.ComponentModel.DataAnnotations;

namespace Ruya.Services.CloudStorage.Local;

public class StorageServiceSettings
{
    public const string ProviderName = nameof(Ruya.Services.CloudStorage.Local);
	public const string ConfigurationSectionName = $"{nameof(Ruya.Services.CloudStorage)}:{ProviderName}";

	[Required]
	public string Path { get; set; } = null!;
}
