using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
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
	/// Validates environment and prints diagnostic info. Call at the very start of Program.cs.
	/// Exits with code 1 if environment is not properly configured.
	/// </summary>
	public static async Task ValidateAndLogStartupInfoAsync()
	{
		var environmentVariables = new Dictionary<string, string?> {
				{ _aspnetCoreEnvironment, Environment.GetEnvironmentVariable(_aspnetCoreEnvironment) ?? string.Empty },
				{ _dotnetEnvironment, Environment.GetEnvironmentVariable(_dotnetEnvironment) ?? string.Empty },
				{ _dotnetRunningInContainer, Environment.GetEnvironmentVariable(_dotnetRunningInContainer) ?? string.Empty },
				{ _dotnetAspireContainerRuntime, Environment.GetEnvironmentVariable(_dotnetAspireContainerRuntime) ?? string.Empty },
				{ _runningInKubernetes, Environment.GetEnvironmentVariable(_runningInKubernetes) ?? string.Empty }
			};
		bool environmentVariablesSet = !(string.IsNullOrWhiteSpace(environmentVariables[_aspnetCoreEnvironment]) && string.IsNullOrWhiteSpace(environmentVariables[_dotnetEnvironment]));

		var buildInfo = await ReadBuildInfoAsync();
		if (string.IsNullOrWhiteSpace(buildInfo))
		{
			Environment.Exit(1);
		}

		PrintStartupInfo(buildInfo, environmentVariables);

		if (!environmentVariablesSet)
		{
			Console.Error.WriteLine("Neither ASPNETCORE_ENVIRONMENT nor DOTNET_ENVIRONMENT are set.");
			Environment.Exit(1);
		}
	}

	public static void ConfigureCulture(string cultureName = "en-US")
	{
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		var cultureInfo = new System.Globalization.CultureInfo(cultureName);
		System.Globalization.CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
		System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
	}

	private static async Task<string?> ReadBuildInfoAsync()
	{
		var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BuildInfo.txt");
		if (!File.Exists(filePath))
		{
			Console.Error.WriteLine($"'{filePath}' not found.");
			return null;
		}

		return await File.ReadAllTextAsync(filePath);
	}

	private static void PrintStartupInfo(string buildInfo, Dictionary<string, string?> environmentVariables)
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
		stringBuilder.Append("DOTNET RUNNING IN CONTAINER ...: ").AppendLine(environmentVariables[_dotnetRunningInContainer]);
		stringBuilder.Append("DOTNET ENVIRONMENT ............: ").AppendLine(environmentVariables[_dotnetEnvironment]);
		stringBuilder.Append("ASPNETCORE ENVIRONMENT ........: ").AppendLine(environmentVariables[_aspnetCoreEnvironment]);
		stringBuilder.Append("DOTNET_ASPIRE_CONTAINER_RUNTIME: ").AppendLine(environmentVariables[_dotnetAspireContainerRuntime]);
		stringBuilder.Append("KUBERNETES SERVICE HOST .......: ").AppendLine(environmentVariables[_runningInKubernetes]);
		Console.WriteLine(stringBuilder.ToString());
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

        int plusIndex = versionInfo.IndexOf('+');
        if (plusIndex == -1)
            return versionInfo;

        int maxLength = Math.Min(maxCharsAfterPlus, versionInfo.Length - (plusIndex + 1));
        return versionInfo[..(plusIndex + 1 + maxLength)];
    }
}
