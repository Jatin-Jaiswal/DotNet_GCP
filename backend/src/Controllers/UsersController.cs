using Microsoft.AspNetCore.Mvc;
using DotNetGcpApp.CloudSql;
using DotNetGcpApp.Redis;
using DotNetGcpApp.PubSub;

namespace DotNetGcpApp.Controllers
{
    /// <summary>
    /// API Controller for managing user operations
    /// Handles user creation, retrieval, and caching
    /// Integrates with Cloud SQL, Redis, and Pub/Sub
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly PostgresService _postgresService;
        private readonly RedisService _redisService;
        private readonly PubSubService _pubSubService;
        private readonly ILogger<UsersController> _logger;

        /// <summary>
        /// Constructor with dependency injection
        /// All services are injected and managed by the .NET dependency injection container
        /// </summary>
        public UsersController(
            PostgresService postgresService,
            RedisService redisService,
            PubSubService pubSubService,
            ILogger<UsersController> logger)
        {
            _postgresService = postgresService;
            _redisService = redisService;
            _pubSubService = pubSubService;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/users
        /// Retrieves all users from the PostgreSQL database
        /// Returns: List of all users ordered by creation date
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            try
            {
                _logger.LogInformation("Getting all users from database");
                
                var users = await _postgresService.GetAllUsersAsync();
                
                return Ok(new
                {
                    success = true,
                    count = users.Count,
                    data = users
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all users");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error retrieving users from database",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// GET /api/users/latest
        /// Retrieves the last 3 users from Redis cache if available
        /// If cache miss, fetches from database and caches the result
        /// Returns: Last 3 users with cache status
        /// </summary>
        [HttpGet("latest")]
        public async Task<IActionResult> GetLatestUsers()
        {
            try
            {
                _logger.LogInformation("Getting latest 3 users");

                // Try to get from Redis cache first
                var cachedUsers = await _redisService.GetCachedLatestUsersAsync();

                if (cachedUsers != null)
                {
                    // Cache hit - return cached data
                    _logger.LogInformation("Returning cached users");
                    return Ok(new
                    {
                        success = true,
                        source = "redis-cache",
                        count = cachedUsers.Count,
                        data = cachedUsers
                    });
                }

                // Cache miss - fetch from database
                _logger.LogInformation("Cache miss - fetching from database");
                var users = await _postgresService.GetLatestUsersAsync(3);

                // Cache the result for future requests
                await _redisService.CacheLatestUsersAsync(users);

                return Ok(new
                {
                    success = true,
                    source = "database",
                    count = users.Count,
                    data = users
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting latest users");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error retrieving latest users",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// POST /api/users
        /// Creates a new user in the database
        /// Steps: 1) Insert into PostgreSQL, 2) Publish Pub/Sub event, 3) Update Redis cache
        /// Request body: { "name": "John Doe", "email": "john@example.com" }
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Name and Email are required"
                    });
                }

                _logger.LogInformation($"Creating user: {request.Name}");

                // Step 1: Insert user into PostgreSQL database
                var user = await _postgresService.InsertUserAsync(request.Name, request.Email);

                // Step 2: Publish Pub/Sub event to notify other services
                await _pubSubService.PublishUserCreatedEventAsync(user);

                // Step 3: Update Redis cache with latest 3 users
                var latestUsers = await _postgresService.GetLatestUsersAsync(3);
                await _redisService.CacheLatestUsersAsync(latestUsers);

                _logger.LogInformation($"User created successfully: {user.Id}");

                return Ok(new
                {
                    success = true,
                    message = "User created successfully",
                    data = user
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                
                // Check if it's a duplicate email error
                if (ex.Message.Contains("duplicate key") || ex.Message.Contains("unique constraint"))
                {
                    return Conflict(new
                    {
                        success = false,
                        message = "Email already exists"
                    });
                }

                return StatusCode(500, new
                {
                    success = false,
                    message = "Error creating user",
                    error = ex.Message
                });
            }
        }
    }

    /// <summary>
    /// Request model for creating a new user
    /// Used for deserializing POST request body
    /// </summary>
    public class CreateUserRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}
