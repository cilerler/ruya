# Ruya.Services.CloudStorage.Local

Local file system provider for `Ruya.Services.CloudStorage`. Ideal for development, testing, or on-premise deployments.

## Configuration

Add the service in `Startup.cs` or `Program.cs`:

```csharp
builder.Services.AddLocalStorageService(builder.Configuration);
```

Configure `appsettings.json`:

```json
{
  "CloudStorage": {
    "Local": {
      "Path": "C:\\Temp\\Storage" // Base directory for all containers
    }
  }
}
```

## Usage

In the Local provider, "containers" are mapped to subdirectories under the configured base path.

```csharp
public class LocalStorageService
{
    private readonly ICloudFileService _storage;

    public LocalStorageService(ICloudStorageFactory factory)
    {
        _storage = factory.GetService("Local");
    }

    public async Task SaveLogAsync(string logName, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        
        // Files will be saved to: C:\Temp\Storage\logs\{logName}
        await _storage.UploadStreamAsync("logs", stream, logName, "text/plain");
    }
}
```
