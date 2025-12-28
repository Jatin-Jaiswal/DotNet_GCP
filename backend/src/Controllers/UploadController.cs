using Microsoft.AspNetCore.Mvc;
using DotNetGcpApp.Storage;
using DotNetGcpApp.PubSub;

namespace DotNetGcpApp.Controllers
{
    /// <summary>
    /// API Controller for file upload operations
    /// Handles uploading files to Google Cloud Storage bucket
    /// Publishes Pub/Sub events when files are uploaded
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class UploadController : ControllerBase
    {
        private readonly StorageService _storageService;
        private readonly PubSubService _pubSubService;
        private readonly ILogger<UploadController> _logger;

        /// <summary>
        /// Constructor with dependency injection
        /// Injects Storage and Pub/Sub services
        /// </summary>
        public UploadController(
            StorageService storageService,
            PubSubService pubSubService,
            ILogger<UploadController> logger)
        {
            _storageService = storageService;
            _pubSubService = pubSubService;
            _logger = logger;
        }

        /// <summary>
        /// POST /api/upload
        /// Uploads a file to Google Cloud Storage bucket
        /// Steps: 1) Upload file to GCS, 2) Publish Pub/Sub event
        /// Request: multipart/form-data with file field
        /// Returns: File URL and upload details
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            try
            {
                // Validate that file exists
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "No file uploaded"
                    });
                }

                // Validate file size (max 10MB)
                const long maxFileSize = 10 * 1024 * 1024; // 10MB
                if (file.Length > maxFileSize)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "File size exceeds 10MB limit"
                    });
                }

                _logger.LogInformation($"Uploading file: {file.FileName} ({file.Length} bytes)");

                // Step 1: Upload file to Google Cloud Storage
                var fileUrl = await _storageService.UploadFileAsync(file);

                // Step 2: Publish Pub/Sub event to notify about file upload
                await _pubSubService.PublishFileUploadedEventAsync(file.FileName, fileUrl);

                _logger.LogInformation($"File uploaded successfully: {fileUrl}");

                return Ok(new
                {
                    success = true,
                    message = "File uploaded successfully",
                    data = new
                    {
                        fileName = file.FileName,
                        fileSize = file.Length,
                        contentType = file.ContentType,
                        url = fileUrl
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading file: {file?.FileName}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error uploading file",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// GET /api/upload/files
        /// Lists all files in the Cloud Storage bucket
        /// Returns: List of file names currently stored
        /// </summary>
        [HttpGet("files")]
        public async Task<IActionResult> ListFiles()
        {
            try
            {
                _logger.LogInformation("Listing all files in bucket");

                var files = await _storageService.ListFilesAsync();

                return Ok(new
                {
                    success = true,
                    count = files.Count,
                    data = files
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing files");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error listing files",
                    error = ex.Message
                });
            }
        }
    }
}
