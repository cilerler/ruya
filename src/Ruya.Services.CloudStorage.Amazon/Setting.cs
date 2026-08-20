namespace Ruya.Services.CloudStorage.Amazon;

public class Setting
{
    public const string ProviderName = nameof(Ruya.Services.CloudStorage.Amazon);
	public const string ConfigurationSectionName = $"{nameof(Ruya.Services.CloudStorage)}:{ProviderName}";

	public string? AccessKey { get; set; }
	public string? SecretKey { get; set; }
	public string? Region { get; set; }
    public string? ServiceUrl { get; set; }
}
