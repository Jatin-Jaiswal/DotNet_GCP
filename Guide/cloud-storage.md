# Cloud Storage Integration

## Library Used
`Google.Cloud.Storage.V1`

```xml
<!-- backend/src/DotNetGcpApp.csproj -->
<PackageReference Include="Google.Cloud.Storage.V1" Version="4.6.0" />
```

## Related File
`backend/src/Storage/StorageService.cs`

## Configuration Source

- Secret Manager secret: `storage-bucket-name`
- Terraform provisions the bucket, writes the secret, and grants `roles/storage.objectAdmin`

**Runtime Resolution**
```csharp
public StorageService(SecretManagerService secretManager, ILogger<StorageService> logger)
{
    _logger = logger;

    _bucketName = secretManager.GetSecretValue("storage-bucket-name");
    _storageClient = StorageClient.Create();

    _logger.LogInformation($"Storage client initialized from Secret Manager: {_bucketName}");
}
```

## Upload Workflow
```csharp
public async Task<string> UploadFileAsync(IFormFile file)
{
    if (file == null || file.Length == 0)
        throw new ArgumentException("File is empty or null");

    var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var fileName = $"{timestamp}_{file.FileName}";

    _logger.LogInformation($"Uploading: {fileName} ({file.Length} bytes)");

    using var stream = file.OpenReadStream();
    var uploadedObject = await _storageClient.UploadObjectAsync(
        bucket: _bucketName,
        objectName: fileName,
        contentType: file.ContentType,
        source: stream
    );

    var fileUrl = $"https://storage.googleapis.com/{_bucketName}/{fileName}";

    _logger.LogInformation($"Upload complete: {fileUrl}");
    return fileUrl;
}
```

**Highlights**
- Validates input and ensures non-empty uploads
- Generates unique names to prevent overwrites
- Streams content to avoid loading large files into memory
- Uses resumable uploads for reliability
- Returns a public URL for downstream use

## Additional Operations

### List Files
```csharp
public async Task<List<string>> ListFilesAsync()
{
    var fileNames = new List<string>();
    var objects = _storageClient.ListObjectsAsync(_bucketName);

    await foreach (var obj in objects)
    {
        fileNames.Add(obj.Name);
    }

    _logger.LogInformation($"Found {fileNames.Count} files");
    return fileNames;
}
```

### Delete File
```csharp
public async Task DeleteFileAsync(string fileName)
{
    await _storageClient.DeleteObjectAsync(_bucketName, fileName);
    _logger.LogInformation($"Deleted: {fileName}");
}
```

## Controller Usage
```csharp
[HttpPost]
public async Task<IActionResult> UploadFile([FromForm] IFormFile file)
{
    var fileUrl = await _storageService.UploadFileAsync(file);
    await _pubSubService.PublishFileUploadedEventAsync(file.FileName, fileUrl);

    return Ok(new
    {
        success = true,
        message = "File uploaded successfully",
        fileName = file.FileName,
        fileUrl = fileUrl,
        pubsubPublished = true
    });
}
```

## Frontend Integration
```typescript
uploadFile(event: any) {
  const file = event.target.files[0];
  const formData = new FormData();
  formData.append('file', file);

  this.http.post(`${this.apiUrl}/upload`, formData).subscribe({
    next: (response: any) => {
      console.log('Upload success:', response.fileUrl);
    },
    error: (error) => {
      console.error('Upload failed:', error);
    }
  });
}
```

## Bucket CORS Configuration (Terraform)
```hcl
resource "google_storage_bucket" "bucket" {
  name          = var.bucket_name
  location      = var.region
  force_destroy = true

  cors {
    origin          = ["*"]
    method          = ["GET", "HEAD", "PUT", "POST", "DELETE"]
    response_header = ["*"]
    max_age_seconds = 3600
  }
}
```

## Workload Identity Permissions
```hcl
resource "google_storage_bucket_iam_member" "backend_storage" {
  bucket = google_storage_bucket.bucket.name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:${google_service_account.backend_sa.email}"
}
```

**Capabilities Granted**
- Upload, read, delete, and list objects within the bucket

## Streaming vs Loading into Memory

**Anti-pattern**
```csharp
byte[] fileBytes = new byte[file.Length];
file.CopyTo(fileBytes);
```
- Large files consume equal amounts of RAM
- Concurrent uploads increase memory pressure

**Recommended Pattern**
```csharp
using var stream = file.OpenReadStream();
await _storageClient.UploadObjectAsync(bucket, name, type, stream);
```
- Keeps memory usage low (~64 KB buffers)
- Supports very large file uploads efficiently
- Handles concurrent uploads without exhausting RAM
