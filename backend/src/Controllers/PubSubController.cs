using Microsoft.AspNetCore.Mvc;
using DotNetGcpApp.Services;

namespace DotNetGcpApp.Controllers
{
    /// <summary>
    /// API Controller for Pub/Sub event information
    /// Provides information about recent Pub/Sub events
    /// Stores events in Redis so they're shared across all pods
    /// </summary>
    [ApiController]
    [Route("api/pubsub")]
    public class PubSubController : ControllerBase
    {
        private readonly ILogger<PubSubController> _logger;
        private readonly EventStorageService _eventStorage;

        public PubSubController(ILogger<PubSubController> logger, EventStorageService eventStorage)
        {
            _logger = logger;
            _eventStorage = eventStorage;
        }

        /// <summary>
        /// GET /api/pubsub/events
        /// Returns the last 10 Pub/Sub events from Redis
        /// Events are shared across all pods
        /// </summary>
        [HttpGet("events")]
        public async Task<IActionResult> GetEvents()
        {
            try
            {
                var events = await _eventStorage.GetEventsAsync();
                
                return Ok(new
                {
                    success = true,
                    count = events.Count,
                    data = events.OrderByDescending(e => e.Timestamp).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting events");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error retrieving events",
                    error = ex.Message
                });
            }
        }
    }
}
