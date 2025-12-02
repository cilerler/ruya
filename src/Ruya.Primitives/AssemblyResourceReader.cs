using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Ruya.Primitives;

public static class AssemblyResourceReader
{
	public const string ResourceDirectory = "Resources";
	public static string GetEmbeddedResourceContent(this Assembly assembly, string fileName, string? prefix = ResourceDirectory)
	{
        ArgumentNullException.ThrowIfNull(assembly);
		var assemblyName = assembly.GetName().Name;
		var resourceName = string.IsNullOrWhiteSpace(prefix)
			? $"{assemblyName}.{fileName}"
			: $"{assemblyName}.{prefix}.{fileName}";

		using var stream = assembly.GetManifestResourceStream(resourceName)
			?? throw new InvalidOperationException($"Resource not found: {resourceName}");

		using var reader = new StreamReader(stream, Encoding.UTF8);
		return reader.ReadToEnd();
	}
}
