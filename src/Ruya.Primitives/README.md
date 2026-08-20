# Ruya.Primitives

Common primitives, constants, and extensions used across the Ruya Common Libraries framework.

## Features

-   **Startup Helpers**: `Startup.AssemblyName`, `Startup.AssemblyVersion`.
-   **Enum Extensions**: Fast enum parsing and description retrieval.
-   **Resource Readers**: Helpers for reading embedded resources.

## Startup validation

Call startup validation before building the application host. The generated `BuildInfo.txt` file and either
`ASPNETCORE_ENVIRONMENT` or `DOTNET_ENVIRONMENT` are required. Missing state raises a descriptive exception;
the library never terminates the process.

```csharp
await Startup.ValidateAndLogStartupInfoAsync(cancellationToken);
```
