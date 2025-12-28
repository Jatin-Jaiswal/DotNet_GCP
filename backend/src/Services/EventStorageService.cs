using DotNetGcpApp.Redis;
using System.Text.Json;

namespace DotNetGcpApp.Services
{
    /// <summary>
    /// Singleton service for storing Pub/Sub events in Redis
    /// Shared across all controllers and services
    /// </summary>
    public class EventStorageService
    {
        private readonly RedisService _redisService;
        private readonly ILogger<EventStorageService> _logger;
        private const string EventsKey = "pubsub:events";
        private const int MaxEvents = 10;

        public EventStorageService(RedisService redisService, ILogger<EventStorageService> logger)
        {
            _redisService = redisService;
            _logger = logger;
        }

        /// <summary>
        /// Adds a Pub/Sub event to Redis storage
        /// </summary>
        public async Task AddEventAsync(string source, string message)
        {
            try
            {
                var db = _redisService.GetDatabase();
                
                // Get existing events
                var eventsJson = await db.StringGetAsync(EventsKey);
                var events = eventsJson.IsNullOrEmpty 
                    ? new List<EventInfo>() 
                    : JsonSerializer.Deserialize<List<EventInfo>>(eventsJson!) ?? new List<EventInfo>();

                // Add new event
                events.Add(new EventInfo
                {
                    Source = source,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                });

                // Keep only last 10 events
                if (events.Count > MaxEvents)
                {
                    events = events.OrderByDescending(e => e.Timestamp).Take(MaxEvents).ToList();
                }

                // Save back to Redis
                await db.StringSetAsync(EventsKey, JsonSerializer.Serialize(events));
                
                _logger.LogInformation($"Stored event in Redis: {source} - {message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store event in Redis");
            }
        }

        /// <summary>
        /// Gets all Pub/Sub events from Redis
        /// </summary>
        public async Task<List<EventInfo>> GetEventsAsync()
        {
            try
            {
                var db = _redisService.GetDatabase();
                var eventsJson = await db.StringGetAsync(EventsKey);
                
                if (eventsJson.IsNullOrEmpty)
                {
                    return new List<EventInfo>();
                }

                return JsonSerializer.Deserialize<List<EventInfo>>(eventsJson!) ?? new List<EventInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get events from Redis");
                return new List<EventInfo>();
            }
        }
    }

    /// <summary>
    /// Model for storing event information
    /// </summary>
    public class EventInfo
    {
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
