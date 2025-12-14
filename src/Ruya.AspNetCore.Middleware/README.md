# Ruya.AspNetCore.Middleware

ASP.NET Core middleware for adding application environment information to HTTP response headers.

## Features

- Adds configurable response headers with application metadata
- Feature flag support for enabling/disabling at runtime
- Security-conscious defaults (machine name disabled by default)

## Headers

| Header | Description | Default |
|--------|-------------|---------|
| `X-ApplicationVersion` | Assembly version | Enabled |
| `X-ApplicationName` | Assembly name | Enabled |
| `X-Environment` | Environment name (from `ASPNETCORE_ENVIRONMENT` or `DOTNET_ENVIRONMENT`) | Enabled |
| `X-MachineName` | Server machine name | **Disabled** (security) |

## Installation

```bash
dotnet add package Ruya.AspNetCore.Middleware
```

## Usage

### Program.cs

```csharp
using Ruya.AspNetCore.Middleware.AppEnvironmentResponseHeaders;
using Ruya.Primitives;

var builder = WebApplication.CreateBuilder(args);

// Important: Call ValidateAndLogStartupInfoAsync early to set EnvironmentName
await Startup.ValidateAndLogStartupInfoAsync();

// Add services
builder.Services.AddAppEnvironmentResponseHeaders();

var app = builder.Build();

// Add middleware (early in pipeline to ensure headers are added)
app.UseAppEnvironmentResponseHeaders();

app.MapGet("/", () => "Hello World!");

app.Run();
```

### Configuration

#### appsettings.json

```json
{
  "FeatureManagement": {
    "AppEnvironmentResponseHeaders": true
  },
  "AppEnvironmentResponseHeaders": {
    "IncludeVersion": true,
    "IncludeName": true,
    "IncludeEnvironment": true,
    "IncludeMachineName": false
  }
}
```

#### Programmatic Configuration

```csharp
builder.Services.AddAppEnvironmentResponseHeaders(options =>
{
    options.IncludeVersion = true;
    options.IncludeName = true;
    options.IncludeEnvironment = true;
    options.IncludeMachineName = false; // Enable with caution - exposes infrastructure
});
```

## Response Example

```http
HTTP/1.1 200 OK
X-ApplicationVersion: 1.0.0.0
X-ApplicationName: MyWebApi
X-Environment: Production
Content-Type: application/json
```

## Security Considerations

- `X-MachineName` is disabled by default as it exposes infrastructure details
- Consider disabling headers in production if they provide information useful to attackers
- Use feature flags to control header emission per environment

## Dependencies

- `Ruya.Primitives` - Provides `Startup.EnvironmentName`, `Startup.AssemblyVersion`, etc.
- `Ruya.Extensions.Configuration` - Provides feature flag support via `GetFeatureFlag<T>()`
