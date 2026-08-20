using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Ruya.Testing.Primitives;

public abstract partial class TestBase<T> where T : class
{
	private AsyncServiceScope? _serviceScope;

	protected IServiceProvider ScopeServiceProvider => _serviceScope?.ServiceProvider
		?? throw new InvalidOperationException("The test scope has not been initialized.");
	protected ILogger<T> Logger { get; private set; } = default!;


	public TestContext TestContext { get; set; } = default!;

	[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
#pragma warning disable CA1000 // Do not declare static members on generic types
	public static void BaseClassInitialize(TestContext context)
#pragma warning restore CA1000 // Do not declare static members on generic types
	{
		var rootServiceProvider = TestHost.RootServiceProvider;
		if (rootServiceProvider is null)
			return;
		var logger = rootServiceProvider.GetRequiredService<ILogger<T>>();

		LogClassInitializing(logger, typeof(T).Name);
	}

	[ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
#pragma warning disable CA1000 // Do not declare static members on generic types
	public static void BaseClassCleanup()
#pragma warning restore CA1000 // Do not declare static members on generic types
	{
		var rootServiceProvider = TestHost.RootServiceProvider;
		if (rootServiceProvider is null)
			return;

		var logger = rootServiceProvider.GetRequiredService<ILogger<T>>();
		LogClassCleaning(logger, typeof(T).Name);
	}

	[TestInitialize]
	public void BaseTestInit()
	{
		var rootServiceProvider = TestHost.RootServiceProvider
			?? throw new InvalidOperationException("TestHost not initialized. Run AssemblyInit.");

		_serviceScope = rootServiceProvider.CreateAsyncScope();
		Logger = ScopeServiceProvider.GetRequiredService<ILogger<T>>();

		LogTestStarting(Logger, TestContext.TestName);
	}

	public void BaseTestCleanup()
	{
		LogTestFinished(Logger, TestContext.TestName, TestContext.CurrentTestOutcome);

		_serviceScope?.Dispose();
		_serviceScope = null;
	}

	[TestCleanup]
	public async Task BaseTestCleanupAsync()
	{
		LogTestFinished(Logger, TestContext.TestName, TestContext.CurrentTestOutcome);

		if (_serviceScope is { } scope)
		{
			await scope.DisposeAsync().ConfigureAwait(false);
			_serviceScope = null;
		}
	}

	[LoggerMessage(EventId = 100, EventName = "TestClassInitializing", Level = LogLevel.Trace, Message = "lifetime: [ClassInitialize] ==> {ClassName}")]
	private static partial void LogClassInitializing(ILogger logger, string className);

	[LoggerMessage(EventId = 101, EventName = "TestClassCleaning", Level = LogLevel.Trace, Message = "lifetime: [ClassCleanup] <== {ClassName}")]
	private static partial void LogClassCleaning(ILogger logger, string className);

	[LoggerMessage(EventId = 102, EventName = "TestStarting", Level = LogLevel.Trace, Message = "lifetime: [TestInitialize] --> {TestName}")]
	private static partial void LogTestStarting(ILogger logger, string testName);

	[LoggerMessage(EventId = 103, EventName = "TestFinished", Level = LogLevel.Trace, Message = "lifetime: [TestCleanup] <-- {TestName} ({Outcome})")]
	private static partial void LogTestFinished(ILogger logger, string testName, UnitTestOutcome outcome);

}
