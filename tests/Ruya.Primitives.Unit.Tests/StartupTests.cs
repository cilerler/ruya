using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Primitives.Unit.Tests;

[TestClass]
public sealed class StartupTests
{
	[TestMethod]
	public async Task ValidateAndLogStartupInfoAsync_BuildInfoMissing_ThrowsFileNotFoundException()
	{
		var testDirectory = CreateTestDirectory();
		try
		{
			using var output = new StringWriter();

			var exception = await Assert.ThrowsExactlyAsync<FileNotFoundException>(
				() => Startup.ValidateAndLogStartupInfoAsync(
					testDirectory,
					CreateEnvironment("Development", null),
					output,
					CancellationToken.None));

			Assert.AreEqual(Path.Combine(testDirectory, "BuildInfo.txt"), exception.FileName);
		}
		finally
		{
			Directory.Delete(testDirectory, recursive: true);
		}
	}

	[TestMethod]
	public async Task ValidateAndLogStartupInfoAsync_BuildInfoEmpty_ThrowsInvalidDataException()
	{
		var testDirectory = await CreateTestDirectoryWithBuildInfoAsync("   ");
		try
		{
			using var output = new StringWriter();

			await Assert.ThrowsExactlyAsync<InvalidDataException>(
				() => Startup.ValidateAndLogStartupInfoAsync(
					testDirectory,
					CreateEnvironment("Development", null),
					output,
					CancellationToken.None));
		}
		finally
		{
			Directory.Delete(testDirectory, recursive: true);
		}
	}

	[TestMethod]
	public async Task ValidateAndLogStartupInfoAsync_EnvironmentMissing_ThrowsInvalidOperationException()
	{
		var testDirectory = await CreateTestDirectoryWithBuildInfoAsync("commit=abc123");
		try
		{
			using var output = new StringWriter();

			var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
				() => Startup.ValidateAndLogStartupInfoAsync(
					testDirectory,
					CreateEnvironment(null, null),
					output,
					CancellationToken.None));

			StringAssert.Contains(exception.Message, "DOTNET_ENVIRONMENT", StringComparison.Ordinal);
		}
		finally
		{
			Directory.Delete(testDirectory, recursive: true);
		}
	}

	[TestMethod]
	public async Task ValidateAndLogStartupInfoAsync_AspNetCoreAndDotNetEnvironmentSet_PrefersAspNetCoreEnvironment()
	{
		var testDirectory = await CreateTestDirectoryWithBuildInfoAsync("commit=abc123");
		try
		{
			using var output = new StringWriter();

			await Startup.ValidateAndLogStartupInfoAsync(
				testDirectory,
				CreateEnvironment("Staging", "Development"),
				output,
				CancellationToken.None);

			Assert.AreEqual("Staging", Startup.EnvironmentName);
			StringAssert.Contains(output.ToString(), "commit=abc123", StringComparison.Ordinal);
		}
		finally
		{
			Directory.Delete(testDirectory, recursive: true);
		}
	}

	[TestMethod]
	public async Task ValidateAndLogStartupInfoAsync_CancellationRequested_ThrowsOperationCanceledException()
	{
		var testDirectory = await CreateTestDirectoryWithBuildInfoAsync("commit=abc123");
		try
		{
			using var output = new StringWriter();
			using var cancellationTokenSource = new CancellationTokenSource();
			await cancellationTokenSource.CancelAsync();

			await Assert.ThrowsExactlyAsync<OperationCanceledException>(
				() => Startup.ValidateAndLogStartupInfoAsync(
					testDirectory,
					CreateEnvironment("Development", null),
					output,
					cancellationTokenSource.Token));
		}
		finally
		{
			Directory.Delete(testDirectory, recursive: true);
		}
	}

	private static Dictionary<string, string?> CreateEnvironment(string? aspNetCore, string? dotNet)
		=> new(StringComparer.Ordinal)
		{
			["ASPNETCORE_ENVIRONMENT"] = aspNetCore,
			["DOTNET_ENVIRONMENT"] = dotNet
		};

	private static string CreateTestDirectory()
	{
		var path = Path.Combine(Path.GetTempPath(), $"ruya-primitives-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	private static async Task<string> CreateTestDirectoryWithBuildInfoAsync(string contents)
	{
		var path = CreateTestDirectory();
		await File.WriteAllTextAsync(Path.Combine(path, "BuildInfo.txt"), contents);
		return path;
	}
}
