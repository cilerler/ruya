using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Primitives;

#pragma warning disable S2094 // Classes should not be empty
public record ApplicationLog; //* Do not delete, this is used to create the logger scope.
#pragma warning restore S2094 // Classes should not be empty

public static class Startup
{
	private const string _unknown = "Unknown";
	private static readonly Assembly _entryAssembly = Assembly.GetEntryAssembly()
		?? throw new InvalidOperationException("Entry assembly not found.");

	public static string AssemblyName { get; } = _entryAssembly.GetName().Name ?? _unknown;
	public static string AssemblyVersion { get; } = _entryAssembly.GetName().Version?.ToString() ?? _unknown;
	public static string AssemblyConfiguration { get; } = _entryAssembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? _unknown;
	private const string _aspnetCoreEnvironment = "ASPNETCORE_ENVIRONMENT";
	private const string _dotnetEnvironment = "DOTNET_ENVIRONMENT";
	private const string _dotnetRunningInContainer = "DOTNET_RUNNING_IN_CONTAINER";
	private const string _dotnetAspireContainerRuntime = "DOTNET_ASPIRE_CONTAINER_RUNTIME";
	private const string _runningInKubernetes = "KUBERNETES_SERVICE_HOST";

	/// <summary>
	/// Gets the current environment name from ASPNETCORE_ENVIRONMENT or DOTNET_ENVIRONMENT.
	/// Set by <see cref="ValidateAndLogStartupInfoAsync()"/>.
	/// </summary>
	public static string EnvironmentName { get; private set; } = _unknown;

	/// <summary>
	/// Validates environment and prints diagnostic info. Call at the very start of Program.cs.
	/// Throws a descriptive exception when required startup state is missing.
	/// </summary>
	public static Task ValidateAndLogStartupInfoAsync()
		=> ValidateAndLogStartupInfoAsync(CancellationToken.None);

	/// <summary>
	/// Validates environment and prints diagnostic info. Call at the very start of Program.cs.
	/// </summary>
	/// <param name="cancellationToken">Cancels startup validation and file access.</param>
	/// <returns>A task that completes after startup information has been validated and printed.</returns>
	public static Task ValidateAndLogStartupInfoAsync(CancellationToken cancellationToken)
		=> ValidateAndLogStartupInfoAsync(
			AppContext.BaseDirectory,
			GetEnvironmentVariables(),
			Console.Out,
			cancellationToken);

	internal static async Task ValidateAndLogStartupInfoAsync(
		string baseDirectory,
		IReadOnlyDictionary<string, string?> environmentVariables,
		TextWriter output,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
		ArgumentNullException.ThrowIfNull(environmentVariables);
		ArgumentNullException.ThrowIfNull(output);
		cancellationToken.ThrowIfCancellationRequested();

		var buildInfo = await ReadBuildInfoAsync(baseDirectory, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(buildInfo))
		{
			throw new InvalidDataException("Required startup build information is empty.");
		}

		var aspNetCoreEnvironment = GetEnvironmentValue(environmentVariables, _aspnetCoreEnvironment);
		var dotNetEnvironment = GetEnvironmentValue(environmentVariables, _dotnetEnvironment);
		if (string.IsNullOrWhiteSpace(aspNetCoreEnvironment) && string.IsNullOrWhiteSpace(dotNetEnvironment))
		{
			throw new InvalidOperationException("Neither ASPNETCORE_ENVIRONMENT nor DOTNET_ENVIRONMENT is set.");
		}

		EnvironmentName = !string.IsNullOrWhiteSpace(aspNetCoreEnvironment)
			? aspNetCoreEnvironment
			: dotNetEnvironment!;
		PrintStartupInfo(buildInfo, environmentVariables, output);
	}

	public static void ConfigureCulture(string cultureName = "en-US")
	{
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		var cultureInfo = new System.Globalization.CultureInfo(cultureName);
		System.Globalization.CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
		System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
	}

	internal static async Task<string> ReadBuildInfoAsync(string baseDirectory, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
		var filePath = Path.Combine(baseDirectory, "BuildInfo.txt");
		if (!File.Exists(filePath))
		{
			throw new FileNotFoundException("Required startup build information was not found.", filePath);
		}

		return await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
	}

	private static Dictionary<string, string?> GetEnvironmentVariables()
		=> new()
		{
			[_aspnetCoreEnvironment] = Environment.GetEnvironmentVariable(_aspnetCoreEnvironment),
			[_dotnetEnvironment] = Environment.GetEnvironmentVariable(_dotnetEnvironment),
			[_dotnetRunningInContainer] = Environment.GetEnvironmentVariable(_dotnetRunningInContainer),
			[_dotnetAspireContainerRuntime] = Environment.GetEnvironmentVariable(_dotnetAspireContainerRuntime),
			[_runningInKubernetes] = Environment.GetEnvironmentVariable(_runningInKubernetes)
		};

	private static string? GetEnvironmentValue(
		IReadOnlyDictionary<string, string?> environmentVariables,
		string name)
		=> environmentVariables.TryGetValue(name, out var value) ? value : null;

	private static void PrintStartupInfo(
		string buildInfo,
		IReadOnlyDictionary<string, string?> environmentVariables,
		TextWriter output)
	{
		var separator = new string('=', 20);
		var stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("BUILD-TIME");
		stringBuilder.AppendLine(separator);
		stringBuilder.AppendLine(buildInfo);
		stringBuilder.AppendLine("RUN-TIME");
		stringBuilder.AppendLine(separator);
		stringBuilder.Append("ASSEMBLY NAME .................: ").AppendLine(AssemblyName);
		stringBuilder.Append("ASSEMBLY CONFIGURATION.........: ").AppendLine(AssemblyConfiguration);
		stringBuilder.Append("ASSEMBLY VERSION ..............: ").AppendLine(AssemblyVersion);
		stringBuilder.Append("PRODUCT VERSION ...............: ").AppendLine(ProductVersion);
		stringBuilder.Append("COMPANY NAME ..................: ").AppendLine(CompanyName);
		stringBuilder.Append("MACHINE NAME ..................: ").AppendLine(Environment.MachineName);
		stringBuilder.Append("DOTNET RUNNING IN CONTAINER ...: ").AppendLine(GetEnvironmentValue(environmentVariables, _dotnetRunningInContainer));
		stringBuilder.Append("DOTNET ENVIRONMENT ............: ").AppendLine(GetEnvironmentValue(environmentVariables, _dotnetEnvironment));
		stringBuilder.Append("ASPNETCORE ENVIRONMENT ........: ").AppendLine(GetEnvironmentValue(environmentVariables, _aspnetCoreEnvironment));
		stringBuilder.Append("DOTNET_ASPIRE_CONTAINER_RUNTIME: ").AppendLine(GetEnvironmentValue(environmentVariables, _dotnetAspireContainerRuntime));
		stringBuilder.Append("KUBERNETES SERVICE HOST .......: ").AppendLine(GetEnvironmentValue(environmentVariables, _runningInKubernetes));
		output.WriteLine(stringBuilder.ToString());
	}

	private static readonly Lazy<string?> _productVersion = new(() =>
    {
        var assembly = _entryAssembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return ShortenVersionInfoHash(version, 5);
    });

    private static readonly Lazy<string?> _companyName = new(() =>
		_entryAssembly?.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);

    public static string? CompanyName => _companyName.Value;
    public static string? ProductVersion => _productVersion.Value;

    private static string? ShortenVersionInfoHash(string? versionInfo, int maxCharsAfterPlus)
    {
        if (string.IsNullOrWhiteSpace(versionInfo))
            return versionInfo;

        int plusIndex = versionInfo.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex == -1)
            return versionInfo;

        int maxLength = Math.Min(maxCharsAfterPlus, versionInfo.Length - (plusIndex + 1));
        return versionInfo[..(plusIndex + 1 + maxLength)];
    }
}
