using System;
using System.Text.Json;

namespace Ruya.Services.CloudStorage.Azure;

public class Setting
{
    public const string ProviderName = "Azure";
	public const string ConfigurationSectionName = $"CloudStorage:{ProviderName}";

	public string ConnectionStringKey { get; set; }

	private string _container;
	public string Container
	{
		get => _container;
		set => _container = value.ToLower();
	}
}
