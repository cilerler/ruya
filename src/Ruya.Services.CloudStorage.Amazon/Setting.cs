namespace Ruya.Services.CloudStorage.Amazon;

public class Setting
{
    public const string ProviderName = "Amazon";
	public const string ConfigurationSectionName = $"CloudStorage:{ProviderName}";

	public string? AccessKey { get; set; }
	public string? SecretKey { get; set; }
	public string? Region { get; set; }
    public string? ServiceUrl { get; set; }
}
