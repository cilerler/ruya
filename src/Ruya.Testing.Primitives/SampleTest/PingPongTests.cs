using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Testing.Primitives.SampleTest;

[TestClass]
public class PingPongTests : TestBase<PingPongTests>
{
	[TestCategory("Category1")]
	[Priority(1)]
	[TestMethod]
	public void Ping_InputProvided_ReturnsPong()
	{
		Logger.LogInformation("Starting upload test...");

		var pingPongService = ScopeServiceProvider.GetRequiredService<IPingPongService>();
		var result = pingPongService.Ping("Testing");

		Assert.IsNotNull(pingPongService);
		Assert.AreEqual("Pong: Testing", result);
	}

	[TestCategory("Category1")]
	[Priority(2)]
	[Timeout(2000)]
	[DataTestMethod]
	[DataRow("Alpha", DisplayName = "PingPong Check: Alpha")]
	[DataRow("Beta")]
	[DataRow("Gamma")]
	public void Ping_MultipleInputsProvided_ReturnsPong(string input)
	{
		Logger.LogInformation("Starting test '{TestName}' with input: {Input}", TestContext.TestName, input);

		var pingPongService = ScopeServiceProvider.GetRequiredService<IPingPongService>();
		var result = pingPongService.Ping(input);

		Assert.AreEqual($"Pong: {input}", result);
	}

	[TestCategory("Category2")]
	[Priority(3)]
	[Timeout(2000)]
	[DataTestMethod]
	[DynamicData(nameof(GetComplexScenarios))]
	public void Ping_ComplexScenarioProvided_ReturnsExpectedPrefix(PingPongScenario scenario)
	{
		Logger.LogInformation("Testing Scenario: {Input}, Active: {Active}", scenario.Input, scenario.IsActive);

		var pingPongService = ScopeServiceProvider.GetRequiredService<IPingPongService>();
		var result = pingPongService.Ping(scenario.Input);

		Assert.AreEqual($"{scenario.ExpectedPrefix}: {scenario.Input}", result);
	}

	public static IEnumerable<object[]> GetComplexScenarios
	{
		get
		{
			yield return new object[] { new PingPongScenario("Alpha", "Pong", true) };
			yield return new object[] { new PingPongScenario("Beta", "Pong", false) };
			yield return new object[] { new PingPongScenario("Gamma", "Pong", true) };
		}
	}


	public static string GetCustomDisplayName(MethodInfo methodInfo, object[] data)
	{
		var scenario = data[0] as PingPongScenario;
		return $"{methodInfo.Name} ({scenario.Input} - {(scenario.IsActive ? "Active" : "Inactive")})";
	}

	[TestCategory("Category2")]
	[Priority(2)]
	[Timeout(2000)]
	[DataTestMethod]
	[DynamicData(nameof(GetComplexScenarios), DynamicDataDisplayName = nameof(GetCustomDisplayName))]
	public void Ping_ComplexScenarioWithCustomDisplayName_ReturnsExpectedPrefix(PingPongScenario scenario)
	{
		Logger.LogInformation("Testing Scenario: {Input}", scenario.Input);

		var pingPongService = ScopeServiceProvider.GetRequiredService<IPingPongService>();
		var result = pingPongService.Ping(scenario.Input);

		Assert.AreEqual($"{scenario.ExpectedPrefix}: {scenario.Input}", result);
	}
}
