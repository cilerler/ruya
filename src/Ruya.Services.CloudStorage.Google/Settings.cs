using System.ComponentModel.DataAnnotations;

namespace Ruya.Services.CloudStorage.Google;

public class StorageServiceSettings
{
    public const string ProviderName = nameof(Ruya.Services.CloudStorage.Google);
	public const string ConfigurationSectionName = $"{nameof(Ruya.Services.CloudStorage)}:{ProviderName}";

	[Required]
	public string Credential { get; internal set; } = null!;
}
