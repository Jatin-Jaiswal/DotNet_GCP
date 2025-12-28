using Google.Cloud.PubSub.V1;
using System.Text.Json;
using DotNetGcpApp.Models;

namespace DotNetGcpApp.PubSub
{
    /// <summary>
    /// Service for publishing messages to Google Cloud Pub/Sub
    /// Used to notify other services about important events like user creation or file uploads
    /// Uses Workload Identity for authentication (no service account keys needed)
    /// </summary>
    public class PubSubService
    {
        private readonly PublisherClient _publisher;
        private readonly ILogger<PubSubService> _logger;
        private readonly string _topicName;

        /// <summary>
        /// Constructor that creates a Pub/Sub publisher client
        /// Uses environment variables for configuration
        /// Uses Workload Identity for secure authentication from GKE
        /// </summary>
        public PubSubService(IConfiguration configuration, ILogger<PubSubService> logger)
        {
            _logger = logger;

            // Get project ID and topic name from environment variables
            var projectId = Environment.GetEnvironmentVariable("GCP_PROJECT_ID") 
                            ?? configuration["GCP:ProjectId"] 
                            ?? "project-84d8bfc9-cd8e-4b3c-b15";
            
            var topicId = Environment.GetEnvironmentVariable("PUBSUB_TOPIC_ID") 
                          ?? configuration["PubSub:TopicId"] 
                          ?? "dot-net-topic";

            // Build the full topic name in the format: projects/{project}/topics/{topic}
            _topicName = $"projects/{projectId}/topics/{topicId}";

            try
            {
                // Create publisher client (uses Workload Identity automatically in GKE)
                var topicName = TopicName.FromProjectTopic(projectId, topicId);
                _publisher = PublisherClient.CreateAsync(topicName).GetAwaiter().GetResult();

                _logger.LogInformation($"Pub/Sub publisher initialized for topic: {_topicName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Pub/Sub publisher");
                throw;
            }
        }

        /// <summary>
        /// Publishes a message to Pub/Sub when a new user is created in the database
        /// The message contains: source="sql", message about user creation, and timestamp
        /// </summary>
        public async Task PublishUserCreatedEventAsync(User user)
        {
            try
            {
                // Create the Pub/Sub message structure
                var pubSubMessage = new PubSubMessage
                {
                    Source = "sql",
                    Message = $"User created: {user.Name} ({user.Email})",
                    Timestamp = DateTime.UtcNow
                };

                // Publish the message
                await PublishMessageAsync(pubSubMessage);

                _logger.LogInformation($"Published user created event for: {user.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error publishing user created event for: {user.Name}");
                // Don't throw - Pub/Sub failure shouldn't break user creation
            }
        }

        /// <summary>
        /// Publishes a message to Pub/Sub when a file is uploaded to Cloud Storage
        /// The message contains: source="bucket", message about file upload, and timestamp
        /// </summary>
        public async Task PublishFileUploadedEventAsync(string fileName, string fileUrl)
        {
            try
            {
                // Create the Pub/Sub message structure
                var pubSubMessage = new PubSubMessage
                {
                    Source = "bucket",
                    Message = $"File uploaded: {fileName} - URL: {fileUrl}",
                    Timestamp = DateTime.UtcNow
                };

                // Publish the message
                await PublishMessageAsync(pubSubMessage);

                _logger.LogInformation($"Published file uploaded event for: {fileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error publishing file uploaded event for: {fileName}");
                // Don't throw - Pub/Sub failure shouldn't break file upload
            }
        }

        /// <summary>
        /// Internal method that actually publishes a message to Pub/Sub
        /// Serializes the message to JSON and sends it to the topic
        /// </summary>
        private async Task PublishMessageAsync(PubSubMessage message)
        {
            try
            {
                // Convert message object to JSON string
                var json = JsonSerializer.Serialize(message);

                // Create Pub/Sub message with JSON as payload
                var pubsubMessage = new Google.Cloud.PubSub.V1.PubsubMessage
                {
                    Data = Google.Protobuf.ByteString.CopyFromUtf8(json)
                };

                // Publish the message and wait for confirmation
                var messageId = await _publisher.PublishAsync(pubsubMessage);

                _logger.LogInformation($"Message published with ID: {messageId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing message to Pub/Sub");
                throw;
            }
        }

        /// <summary>
        /// Shuts down the publisher gracefully
        /// Should be called when the application is stopping
        /// </summary>
        public async Task ShutdownAsync()
        {
            if (_publisher != null)
            {
                await _publisher.ShutdownAsync(TimeSpan.FromSeconds(5));
                _logger.LogInformation("Pub/Sub publisher shut down");
            }
        }
    }
}
