# Ruya.Testing.Primitives

Base classes and utilities for testing Ruya applications.

## Features

-   **TestBase**: Base class for unit tests with common setup.
-   **TestHost**: Isolated root service provider for integration tests.
-   **MSTest lifecycle logging**: Stable event IDs around class and test setup/cleanup.

## Usage

```csharp
[TestClass]
public class MyServiceTests : TestBase<MyServiceTests>
{
    [TestMethod]
    public void TestSomething()
    {
        // ...
    }
}
```

Initialize once per test assembly and always clean up the owned provider:

```csharp
[TestClass]
public static class AssemblySetup
{
    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        TestHost.Initialize((services, configuration) =>
            services.AddScoped<IMyService, MyService>());
    }

    [AssemblyCleanup]
    public static async Task Cleanup() => await TestHost.CleanupAsync();
}
```

Only environment variables beginning with `RUYA_TEST_` are loaded by default. For example,
`RUYA_TEST_ConnectionStrings__TestServer` overrides `ConnectionStrings:TestServer` without importing unrelated
machine or CI configuration. Use the two-argument `Initialize` overload when a suite needs its own non-empty prefix.

`Initialize` validates the complete dependency graph and refuses a second initialization until cleanup disposes
and clears the current provider. Prefer `CleanupAsync` so async-only singleton services are disposed. The inherited
`TestBase` lifecycle also creates and asynchronously disposes one scope per test. `Cleanup` remains available for
compatibility with suites whose services are synchronously disposable.
