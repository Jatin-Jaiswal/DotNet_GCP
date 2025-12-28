using Google.Cloud.Storage.V1;

namespace DotNetGcpApp.Storage
{
    /// <summary>
    /// Service for uploading files to Google Cloud Storage
    /// Handles file uploads to the dot-net-bucket in asia-south2 region
    /// Uses Workload Identity for authentication (no service account keys needed)
    /// </summary>
    public class StorageService
    {
        private readonly StorageClient _storageClient;
        private readonly ILogger<StorageService> _logger;
        private readonly string _bucketName;

        /// <summary>
        /// Constructor that initializes the Cloud Storage client
        /// Uses environment variables for configuration
        /// Uses Workload Identity for secure authentication from GKE
        /// </summary>
        public StorageService(IConfiguration configuration, ILogger<StorageService> logger)
        {
            _logger = logger;

            // Get bucket name from environment variables
            _bucketName = Environment.GetEnvironmentVariable("STORAGE_BUCKET_NAME") 
                          ?? configuration["Storage:BucketName"] 
                          ?? "dot-net-bucket";

            try
            {
                // Create storage client (uses Workload Identity automatically in GKE)
                _storageClient = StorageClient.Create();

                _logger.LogInformation($"Storage client initialized for bucket: {_bucketName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Storage client");
                throw;
            }
        }

        /// <summary>
        /// Uploads a file to Google Cloud Storage bucket
        /// Generates a unique filename to avoid conflicts
        /// Returns the public URL of the uploaded file
        /// </summary>
        /// <param name="file">The file to upload from HTTP request</param>
        /// <returns>Public URL where the file can be accessed</returns>
        public async Task<string> UploadFileAsync(IFormFile file)
        {
            try
            {
                // Validate that file exists and has content
                if (file == null || file.Length == 0)
                {
                    throw new ArgumentException("File is empty or null");
                }

                // Generate unique filename to prevent overwrites
                // Format: timestamp_originalfilename.ext
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var fileName = $"{timestamp}_{file.FileName}";

                _logger.LogInformation($"Uploading file: {fileName} ({file.Length} bytes)");

                // Upload file to Cloud Storage bucket
                using var stream = file.OpenReadStream();
                
                var uploadedObject = await _storageClient.UploadObjectAsync(
                    bucket: _bucketName,
                    objectName: fileName,
                    contentType: file.ContentType,
                    source: stream
                );

                // Construct the public URL for the uploaded file
                // Format: https://storage.googleapis.com/{bucket}/{filename}
                var fileUrl = $"https://storage.googleapis.com/{_bucketName}/{fileName}";

                _logger.LogInformation($"File uploaded successfully: {fileUrl}");

                return fileUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading file: {file?.FileName}");
                throw;
            }
        }

        /// <summary>
        /// Lists all files in the Cloud Storage bucket
        /// Returns a list of file names currently stored
        /// </summary>
        public async Task<List<string>> ListFilesAsync()
        {
            try
            {
                var fileNames = new List<string>();

                // List all objects in the bucket
                var objects = _storageClient.ListObjectsAsync(_bucketName);

                await foreach (var obj in objects)
                {
                    fileNames.Add(obj.Name);
                }

                _logger.LogInformation($"Listed {fileNames.Count} files from bucket");
                return fileNames;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing files from bucket");
                throw;
            }
        }

        /// <summary>
        /// Deletes a file from Cloud Storage bucket
        /// Used for cleanup or removing unwanted files
        /// </summary>
        public async Task DeleteFileAsync(string fileName)
        {
            try
            {
                await _storageClient.DeleteObjectAsync(_bucketName, fileName);
                _logger.LogInformation($"File deleted: {fileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting file: {fileName}");
                throw;
            }
        }
    }
}
