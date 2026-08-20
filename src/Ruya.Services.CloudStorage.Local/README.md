# Ruya.Services.CloudStorage.Local

Local file system provider for `Ruya.Services.CloudStorage`. Ideal for development, testing, or on-premise deployments.

## Configuration

Add the service in `Startup.cs` or `Program.cs`:

```csharp
builder.Services.AddLocalStorageService();
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

Deleting the final file removes empty child directories but always preserves the bucket root directory. File and bucket paths are constrained to the configured storage root and reparse-point traversal is rejected.

```csharp
public class LocalStorageService
{
    private readonly ICloudFileService _storage;

    public LocalStorageService(ICloudStorageFactory factory)
    {
        _storage = factory.GetService("Local");
    }

    public async Task SaveLogAsync(string logName, string content, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        
        // Files will be saved to: C:\Temp\Storage\logs\{logName}
        await _storage.UploadStreamAsync("logs", stream, logName, "text/plain", cancellationToken);
    }
}
```

Logical object names are mapped to file-system paths, so both `\` and `/` are treated as directory separators by this provider. Returned `CloudFileMetadata.Name` values use `/`; reuse that canonical name for metadata, download, delete, copy, prefix, and signed-path operations. Remote providers intentionally treat those later inputs as opaque exact object keys, so using the returned name keeps application code portable.
