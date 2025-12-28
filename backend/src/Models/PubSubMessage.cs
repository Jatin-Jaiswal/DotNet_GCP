namespace DotNetGcpApp.Models
{
    /// <summary>
    /// Message format for Google Cloud Pub/Sub events
    /// This model defines the structure of messages published to Pub/Sub topic
    /// </summary>
    public class PubSubMessage
    {
        /// <summary>
        /// Source of the event - either "sql" for database operations or "bucket" for file uploads
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Descriptive message about what happened (e.g., "User created", "File uploaded")
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// UTC timestamp when the event occurred
        /// </summary>
        public DateTime Timestamp { get; set; }
    }
}
