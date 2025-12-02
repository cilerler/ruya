# Ruya.Services.CloudStorage.Amazon

Amazon S3 provider for `Ruya.Services.CloudStorage`.

## Configuration

Add the service in `Startup.cs` or `Program.cs`:

```csharp
builder.Services.AddAmazonStorageService(builder.Configuration);
```

Configure `appsettings.json`:

```json
{
  "CloudStorage": {
    "Amazon": {
      "AccessKey": "your-access-key",
      "SecretKey": "your-secret-key",
      "Region": "us-east-1"
    }
  }
}
```

## Usage

```csharp
public class ImageService
{
    private readonly ICloudFileService _storage;

    public ImageService(ICloudStorageFactory factory)
    {
        _storage = factory.GetService("Amazon");
    }

    public async Task SaveImageAsync(string key, Stream imageStream)
    {
        // "my-app-images" is the bucket name
        await _storage.UploadStreamAsync("my-app-images", imageStream, key, "image/jpeg");
    }
}
```
