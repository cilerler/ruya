namespace Ruya.Observability;

public class TracingSetting
{
	public const string ConfigurationSectionName = "Tracing";
	public const string DefaultVersion = "0.0.0.0";
	public const string DefaultName = "Ruya.Observability";

	public int TraceKeyCacheExpirationMinutes { set; get; } = 45;
	public string TraceCacheInstanceName { set; get; } = "Trace";
	public string ConnectionStringKey { set; get; } = "TraceConnectionString";

	/// <summary>
	///     FileUpload service uses this key to mark the uploaded file so FileMonitoring
	///     service can link its trace to upload trace. All other services use `ImportQueue.Id`
	///     to link their traces
	/// </summary>
	public string FileNameTraceKeyPrefix { set; get; } = "File-";
}
