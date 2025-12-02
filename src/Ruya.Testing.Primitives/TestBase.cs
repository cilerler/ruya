using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ruya.Testing.Primitives;

public abstract partial class TestBase<T> where T : class
{
	private IServiceScope? _serviceScope;

	protected IServiceProvider ScopeServiceProvider => _serviceScope?.ServiceProvider!;
	protected ILogger<T> Logger { get; private set; } = default!;


	public TestContext TestContext { get; set; } = default!;

	[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
#pragma warning disable CA1000 // Do not declare static members on generic types
	public static void BaseClassInitialize(TestContext context)
#pragma warning restore CA1000 // Do not declare static members on generic types
	{
		if (TestHost.RootServiceProvider is null)
			return;
		var logger = TestHost.RootServiceProvider.GetRequiredService<ILogger<T>>();

		LogClassInitializing(logger, typeof(T).Name);
	}

	[ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
#pragma warning disable CA1000 // Do not declare static members on generic types
	public static void BaseClassCleanup()
#pragma warning restore CA1000 // Do not declare static members on generic types
	{
		if (TestHost.RootServiceProvider is null)
			return;

		var logger = TestHost.RootServiceProvider.GetRequiredService<ILogger<T>>();
		LogClassCleaning(logger, typeof(T).Name);
	}

	[TestInitialize]
	public void BaseTestInit()
	{
		if (TestHost.RootServiceProvider is null)
			throw new InvalidOperationException("TestHost not initialized. Run AssemblyInit.");

		_serviceScope = TestHost.RootServiceProvider.CreateScope();
		Logger = ScopeServiceProvider.GetRequiredService<ILogger<T>>();

		LogTestStarting(Logger, TestContext.TestName);
	}

	[TestCleanup]
	public void BaseTestCleanup()
	{
		LogTestFinished(Logger, TestContext.TestName, TestContext.CurrentTestOutcome.ToString());

		_serviceScope?.Dispose();
	}

	[LoggerMessage(Level = LogLevel.Trace, Message = "lifetime: [ClassInitialize] ==> {ClassName}")]
	private static partial void LogClassInitializing(ILogger logger, string className);

	[LoggerMessage(Level = LogLevel.Trace, Message = "lifetime: [ClassCleanup] <== {ClassName}")]
	private static partial void LogClassCleaning(ILogger logger, string className);

	[LoggerMessage(Level = LogLevel.Trace, Message = "lifetime: [TestInitialize] --> {TestName}")]
	private static partial void LogTestStarting(ILogger logger, string testName);

	[LoggerMessage(Level = LogLevel.Trace, Message = "lifetime: [TestCleanup] <-- {TestName} ({Outcome})")]
	private static partial void LogTestFinished(ILogger logger, string testName, string outcome);

}
