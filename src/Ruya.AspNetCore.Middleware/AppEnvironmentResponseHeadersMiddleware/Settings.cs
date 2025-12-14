namespace Ruya.AspNetCore.Middleware.AppEnvironmentResponseHeaders;

/// <summary>
/// Configuration settings for the AppEnvironmentResponseHeaders middleware.
/// </summary>
public sealed class AppEnvironmentResponseHeadersSettings
{
	/// <summary>
	/// The configuration section name for binding.
	/// </summary>
	public const string ConfigurationSectionName = nameof(AppEnvironmentResponseHeaders);

	/// <summary>
	/// Feature flag name for enabling/disabling the middleware.
	/// </summary>
	public static readonly string FeatureFlag = ConfigurationSectionName;

	/// <summary>
	/// Gets whether the middleware is enabled. Set via FeatureManagement configuration.
	/// </summary>
	public bool Enabled { get; internal set; }

	/// <summary>
	/// Gets or sets whether to include the X-ApplicationVersion header.
	/// Default: true.
	/// </summary>
	public bool IncludeVersion { get; set; } = true;

	/// <summary>
	/// Gets or sets whether to include the X-ApplicationName header.
	/// Default: true.
	/// </summary>
	public bool IncludeName { get; set; } = true;

	/// <summary>
	/// Gets or sets whether to include the X-Environment header.
	/// Default: true.
	/// </summary>
	public bool IncludeEnvironment { get; set; } = true;

	/// <summary>
	/// Gets or sets whether to include the X-MachineName header.
	/// Default: false (disabled for security - exposes infrastructure details).
	/// </summary>
	public bool IncludeMachineName { get; set; }
}
