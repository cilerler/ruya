# Ruya.Services.CloudStorage.Azure

Azure Blob Storage provider for `Ruya.Services.CloudStorage`.

## Configuration

Add the service in `Startup.cs` or `Program.cs`:

```csharp
builder.Services.AddAzureStorageService();
```

Configure `appsettings.json`:

```json
{
  "CloudStorage": {
    "Azure": {
      "ConnectionStringKey": "AzureStorage"
    }
  }
}
```

`ConnectionStringKey` is a catalog entry. Supply the matching `ConnectionStrings:AzureStorage` value through an application secret provider; do not commit storage credentials to normal settings files.

## Usage

```csharp
public class DocumentService
{
    private readonly ICloudFileService _storage;

    public DocumentService(ICloudStorageFactory factory)
    {
        _storage = factory.GetService("Azure");
    }

    public async Task UploadDocumentAsync(string filename, Stream content, CancellationToken cancellationToken)
    {
        // "documents" is the container name
        await _storage.UploadStreamAsync("documents", content, filename, "application/pdf", cancellationToken);
    }
}
```

Only an upload `targetPath` is normalized to `/`. Metadata/download/delete `fileName`, list `prefix`, copy source and destination names, and signed-URL `filename` values are exact blob names. Because `\` can be a literal blob-name character, reuse the canonical `CloudFileMetadata.Name` returned by an upload instead of normalizing later key inputs.
