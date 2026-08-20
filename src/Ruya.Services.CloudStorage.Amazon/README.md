# Ruya.Services.CloudStorage.Amazon

Amazon S3 provider for `Ruya.Services.CloudStorage`.

## Configuration

Add the service in `Startup.cs` or `Program.cs`:

```csharp
builder.Services.AddAmazonStorageService();
```

Configure `appsettings.json`:

```json
{
  "CloudStorage": {
    "Amazon": {
      "Region": "us-east-1"
    }
  }
}
```

Credentials are resolved through the standard AWS credential chain. Do not commit access keys to settings files. If the optional `AccessKey` and `SecretKey` settings are used for a local test environment, provide both through an application secret provider.

The DI-created client owns and disposes the `IAmazonS3` client it creates. The public constructor that accepts an existing `IAmazonS3` instance treats it as caller-owned and never disposes it.

## Usage

```csharp
public class ImageService
{
    private readonly ICloudFileService _storage;

    public ImageService(ICloudStorageFactory factory)
    {
        _storage = factory.GetService("Amazon");
    }

    public async Task SaveImageAsync(string key, Stream imageStream, CancellationToken cancellationToken)
    {
        // "my-app-images" is the bucket name
        await _storage.UploadStreamAsync("my-app-images", imageStream, key, "image/jpeg", cancellationToken);
    }
}
```

Only an upload `targetPath` is normalized to `/`. Metadata/download/delete `fileName`, list `prefix`, copy source and destination names, and signed-URL `filename` values are exact S3 object keys. Because `\` can be a literal S3 key character, reuse the canonical `CloudFileMetadata.Name` returned by an upload instead of normalizing later key inputs.
