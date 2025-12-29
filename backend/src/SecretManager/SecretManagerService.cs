using Google.Cloud.SecretManager.V1;

namespace DotNetGcpApp.SecretManager
{
    /// <summary>
    /// Centralized service for fetching secrets from Google Cloud Secret Manager
    /// Uses Workload Identity for authentication (no service account key needed)
    /// </summary>
    public class SecretManagerService
    {
        private readonly SecretManagerServiceClient _client;
        private readonly ILogger<SecretManagerService> _logger;
        private readonly string _projectId;

        public SecretManagerService(ILogger<SecretManagerService> _logger)
        {
            this._logger = _logger;
            _client = SecretManagerServiceClient.Create();
            
            // Get project ID from Secret Manager using Google Cloud metadata service
            // When running in GKE, we can get project ID from metadata
            _projectId = GetProjectIdFromMetadata();
            
            _logger.LogInformation("✅ Secret Manager Service initialized");
        }
        
        /// <summary>
        /// Gets GCP Project ID from metadata service (works in GKE)
        /// </summary>
        private string GetProjectIdFromMetadata()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Metadata-Flavor", "Google");
                var response = client.GetStringAsync("http://metadata.google.internal/computeMetadata/v1/project/project-id").Result;
                _logger.LogInformation($"✅ Retrieved project ID from metadata: {response}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Failed to get project ID from metadata: {ex.Message}");
                throw new Exception("Could not retrieve project ID from GKE metadata service", ex);
            }
        }

        /// <summary>
        /// Retrieves secret value from Google Cloud Secret Manager
        /// </summary>
        /// <param name="secretId">The secret ID (name) in Secret Manager</param>
        /// <param name="fallbackProjectId">Fallback project ID for bootstrap (only used for project-id fetch)</param>
        /// <returns>The secret value as string</returns>
        public string GetSecretValue(string secretId, string fallbackProjectId = null)
        {
            try
            {
                var projectIdToUse = _projectId ?? fallbackProjectId;
                if (string.IsNullOrEmpty(projectIdToUse))
                {
                    throw new Exception("Project ID not available for Secret Manager");
                }

                var secretVersionName = new SecretVersionName(projectIdToUse, secretId, "latest");
                var response = _client.AccessSecretVersion(secretVersionName);
                var secretValue = response.Payload.Data.ToStringUtf8();
                _logger.LogInformation($"✅ Retrieved secret: {secretId}");
                return secretValue;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Failed to fetch secret '{secretId}': {ex.Message}");
                throw new Exception($"Could not access secret '{secretId}' from Secret Manager", ex);
            }
        }
    }
}
