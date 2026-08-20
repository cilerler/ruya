namespace Ruya.Services.CloudStorage.Azure;

public class Setting
{
    public const string ProviderName = nameof(Ruya.Services.CloudStorage.Azure);
	public const string ConfigurationSectionName = $"{nameof(Ruya.Services.CloudStorage)}:{ProviderName}";

	public string ConnectionStringKey { get; set; } = null!;

	private string _container = null!;
	public string Container
	{
		get => _container;
		set => _container = value?.ToLowerInvariant() ?? null!;
	}
}
