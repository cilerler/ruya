using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ruya.Services.CloudStorage.Google
{
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

				if (!string.IsNullOrWhiteSpace(_projectId))
				{
					return _projectId;
				}

				JObject credential;
				try
				{
					credential = JObject.Parse(Credential);
				}
				// ReSharper disable once RedundantCatchClause
				catch (JsonException)
				{
					throw;
				}

				if (!credential.TryGetValue(ProjectIdInCredentials, out JToken projectId))
				{
					_projectId = null;
					return _projectId;
				}
				var output = projectId.Value<string>();
				return output;
			}
		}

		public string Credential { get; set; }
	}
}
