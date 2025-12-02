# Ruya.Testing.Primitives

Base classes and utilities for testing Ruya applications.

## Features

-   **TestBase**: Base class for unit tests with common setup.
-   **TestHost**: Helper for setting up `IHost` for integration tests.
-   **MSTest Settings**: Standard settings for MSTest.

## Usage

```csharp
public class MyServiceTests : TestBase
{
    [TestMethod]
    public void TestSomething()
    {
        // ...
    }
}
```
