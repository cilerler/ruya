using System.Diagnostics;
using System.Reflection;

namespace Ruya.Observability;

public class DistributedTracing
{
	public static readonly string AssemblyName = Assembly.GetEntryAssembly()?.GetName()?.Name ?? TracingSetting.DefaultName;
	public static readonly string Version = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? TracingSetting.DefaultVersion;

	public static ActivitySource ActivitySource = new(AssemblyName!, Version);
}
