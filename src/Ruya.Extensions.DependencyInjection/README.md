# Ruya.Extensions.DependencyInjection

Extension methods for `Microsoft.Extensions.DependencyInjection` that provide service collection validation capabilities.

## Features

- **Service Registration Validation**: Ensure all required services are registered in the dependency injection container before the application starts.

## Usage

### EnsureServicesRegistered

Use `EnsureServicesRegistered` to validate that specific services have been registered in the service collection. This is useful for catching configuration errors early during application startup.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Ruya.Extensions.DependencyInjection;

public interface IMyService { }
public interface IAnotherService { }

public class MyService : IMyService { }
public class AnotherService : IAnotherService { }

// In your Program.cs or Startup.cs
var services = new ServiceCollection();

// Register your services
services.AddSingleton<IMyService, MyService>();
services.AddScoped<IAnotherService, AnotherService>();

// Validate that required services are registered
services.EnsureServicesRegistered(
    typeof(IMyService),
    typeof(IAnotherService)
);

// If any service is missing, an InvalidOperationException is thrown
// with a message listing all missing services
```

### Error Handling

If any of the specified services are not registered, an `InvalidOperationException` is thrown with a descriptive message:

```csharp
var services = new ServiceCollection();

// This will throw: "Missing required services: IMyService, IAnotherService"
services.EnsureServicesRegistered(
    typeof(IMyService),
    typeof(IAnotherService)
);
```

### Fluent API

The extension method returns `IServiceCollection`, allowing for method chaining:

```csharp
services
    .AddSingleton<IMyService, MyService>()
    .AddScoped<IAnotherService, AnotherService>()
    .EnsureServicesRegistered(typeof(IMyService), typeof(IAnotherService))
    .AddLogging();
```

