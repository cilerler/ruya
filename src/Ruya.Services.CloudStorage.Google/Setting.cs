using System.Text.Json;

namespace Ruya.Services.CloudStorage.Google;

public class Setting
{
	public const string ConfigurationSectionName = "CloudStorage_Google";
	private const string ProjectIdInCredentials = "project_id";
	private string _projectId;

	public string ProjectId
	{
		get
		{
			if (string.IsNullOrWhiteSpace(Credential))
			{
				_projectId = null;
				return _projectId;
			}

			if (!string.IsNullOrWhiteSpace(_projectId)) return _projectId;

			JsonDocument credential;
			try
			{
				credential = JsonDocument.Parse(Credential);
			}
			// ReSharper disable once RedundantCatchClause
			catch (JsonException)
			{
				throw;
			}

			if (!credential.RootElement.TryGetProperty(ProjectIdInCredentials, out JsonElement projectId))
			{
				_projectId = null;
				return _projectId;
			}

			string? output = projectId.GetString();
			return output;
		}
	}

	public string Credential { get; set; }
}
