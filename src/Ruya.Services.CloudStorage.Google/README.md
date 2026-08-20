# Ruya.Services.CloudStorage.Google

Google Cloud Storage provider for `Ruya.Services.CloudStorage`.

## Configuration

Add the service in `Startup.cs` or `Program.cs`:

```csharp
builder.Services.AddGoogleStorageService();
```

The provider requires the service-account credential JSON at `CloudStorage:Google:Credential`. Supply that value through an application secret provider; do not commit the credential or private key to normal settings files.

Both a JSON string and a hierarchical configuration object are accepted, so environment-specific secret providers can expose the credential in their native shape.

The DI-created client owns and disposes the `StorageClient` it creates. The public constructor that accepts an existing `StorageClient` treats that client as caller-owned and never disposes it.

## Usage

```csharp
public class BackupService
{
    private readonly ICloudFileService _storage;

    public BackupService(ICloudStorageFactory factory)
    {
        _storage = factory.GetService("Google");
    }

    public async Task UploadBackupAsync(string filename, string localPath, CancellationToken cancellationToken)
    {
        // "app-backups" is the bucket name
        await _storage.UploadFileAsync("app-backups", localPath, filename, cancellationToken);
    }
}
```

Only an upload `targetPath` is normalized to `/`. Metadata/download/delete `fileName`, list `prefix`, copy source and destination names, and signed-URL `filename` values are exact Google Cloud Storage object keys. Because `\` can be a literal object-name character, reuse the canonical `CloudFileMetadata.Name` returned by an upload instead of normalizing later key inputs.
