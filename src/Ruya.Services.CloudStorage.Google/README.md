# Ruya.Services.CloudStorage.Google

Google Cloud Storage provider for `Ruya.Services.CloudStorage`.

## Configuration

Add the service in `Startup.cs` or `Program.cs`:

```csharp
builder.Services.AddGoogleStorageService(builder.Configuration);
```

Configure `appsettings.json`:

```json
{
  "CloudStorage": {
    "Google": {
      "Credential": {
        "type": "service_account",
        "project_id": "your-project-id",
        "private_key_id": "...",
        "private_key": "...",
        "client_email": "...",
        "client_id": "...",
        "auth_uri": "https://accounts.google.com/o/oauth2/auth",
        "token_uri": "https://oauth2.googleapis.com/token",
        "auth_provider_x509_cert_url": "https://www.googleapis.com/oauth2/v1/certs",
        "client_x509_cert_url": "..."
      }
    }
  }
}
```

## Usage

```csharp
public class BackupService
{
    private readonly ICloudFileService _storage;

    public BackupService(ICloudStorageFactory factory)
    {
        _storage = factory.GetService("Google");
    }

    public async Task UploadBackupAsync(string filename, string localPath)
    {
        // "app-backups" is the bucket name
        await _storage.UploadFileAsync("app-backups", localPath, filename);
    }
}
```
