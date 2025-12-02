# Ruya.Services.CloudStorage.Azure

Azure Blob Storage provider for `Ruya.Services.CloudStorage`.

## Configuration

Add the service in `Startup.cs` or `Program.cs`:

```csharp
builder.Services.AddAzureStorageService(builder.Configuration);
```

Configure `appsettings.json`:

```json
{
  "CloudStorage": {
    "Azure": {
      "ConnectionStringKey": "AzureStorage"
    }
  },
  "ConnectionStrings": {
    "AzureStorage": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
  }
}
```

## Usage

```csharp
public class DocumentService
{
    private readonly ICloudFileService _storage;

    public DocumentService(ICloudStorageFactory factory)
    {
        _storage = factory.GetService("Azure");
    }

    public async Task UploadDocumentAsync(string filename, Stream content)
    {
        // "documents" is the container name
        await _storage.UploadStreamAsync("documents", content, filename, "application/pdf");
    }
}
```
