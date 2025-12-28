using Google.Cloud.PubSub.V1;
using System.Text.Json;
using DotNetGcpApp.Models;
using DotNetGcpApp.Services;

namespace DotNetGcpApp.PubSub
{
    /// <summary>
    /// Background service that subscribes to Pub/Sub messages
    /// Receives events from the Pub/Sub topic and stores them for display in the UI
    /// Runs continuously as a hosted service
    /// </summary>
    public class PubSubSubscriberService : BackgroundService
    {
        private readonly ILogger<PubSubSubscriberService> _logger;
        private readonly EventStorageService _eventStorage;
        private readonly string _subscriptionName;
        private SubscriberClient? _subscriber;

        public PubSubSubscriberService(IConfiguration configuration, ILogger<PubSubSubscriberService> logger, EventStorageService eventStorage)
        {
            _logger = logger;
            _eventStorage = eventStorage;

            // Get project ID and subscription name from environment variables
            var projectId = Environment.GetEnvironmentVariable("GCP_PROJECT_ID") 
                            ?? configuration["GCP:ProjectId"] 
                            ?? "project-84d8bfc9-cd8e-4b3c-b15";
            
            var subscriptionId = Environment.GetEnvironmentVariable("PUBSUB_SUBSCRIPTION_ID") 
                                 ?? configuration["PubSub:SubscriptionId"] 
                                 ?? "dot-net-sub";

            _subscriptionName = $"projects/{projectId}/subscriptions/{subscriptionId}";
        }

        /// <summary>
        /// Main execution method - runs in background continuously
        /// Subscribes to Pub/Sub and processes incoming messages
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation($"Starting Pub/Sub subscriber for: {_subscriptionName}");

                // Extract project and subscription IDs from the full name
                var parts = _subscriptionName.Split('/');
                var projectId = parts[1];
                var subscriptionId = parts[3];

                var subscriptionName = SubscriptionName.FromProjectSubscription(projectId, subscriptionId);
                
                // Create subscriber client
                _subscriber = await SubscriberClient.CreateAsync(subscriptionName);

                // Start listening for messages
                _subscriber.StartAsync(async (msg, cancellationToken) =>
                {
                    try
                    {
                        // Deserialize the message
                        var json = msg.Data.ToStringUtf8();
                        var pubSubMessage = JsonSerializer.Deserialize<PubSubMessage>(json);

                        if (pubSubMessage != null)
                        {
                            _logger.LogInformation($"Received Pub/Sub message: {pubSubMessage.Source} - {pubSubMessage.Message}");

                            // Add the event to Redis (shared across all pods)
                            try
                            {
                                await _eventStorage.AddEventAsync(pubSubMessage.Source, pubSubMessage.Message);
                                _logger.LogInformation("Successfully stored event in Redis");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to store event in Redis");
                            }
                        }

                        // Acknowledge the message (mark as processed)
                        return SubscriberClient.Reply.Ack;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing Pub/Sub message");
                        // Nack the message so it can be redelivered
                        return SubscriberClient.Reply.Nack;
                    }
                });                _logger.LogInformation("Pub/Sub subscriber started successfully and listening for messages");

                // Wait until cancellation is requested (keep subscriber running)
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in Pub/Sub subscriber service");
            }
        }

        /// <summary>
        /// Cleanup when service stops
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Pub/Sub subscriber service");
            
            if (_subscriber != null)
            {
                await _subscriber.StopAsync(TimeSpan.FromSeconds(5));
            }

            await base.StopAsync(cancellationToken);
        }
    }
}
