using StackExchange.Redis;
using System.Text.Json;
using DotNetGcpApp.Models;

namespace DotNetGcpApp.Redis
{
    /// <summary>
    /// Service for managing Google Cloud Memorystore Redis operations
    /// Provides caching functionality for frequently accessed data
    /// Caches the last 3 users with a 60-second TTL (Time To Live)
    /// </summary>
    public class RedisService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _database;
        private readonly ILogger<RedisService> _logger;
        private const int CACHE_TTL_SECONDS = 60; // Cache expires after 60 seconds
        private const string LATEST_USERS_KEY = "users:latest:3"; // Redis key for storing latest users

        /// <summary>
        /// Constructor that establishes connection to Memorystore Redis
        /// Uses environment variables for configuration
        /// </summary>
        public RedisService(IConfiguration configuration, ILogger<RedisService> logger)
        {
            _logger = logger;

            try
            {
                // Get Redis connection details from environment variables
                var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") 
                                ?? configuration["Redis:Host"] 
                                ?? "10.127.80.5";
                
                var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") 
                                ?? configuration["Redis:Port"] 
                                ?? "6379";

                // Connect to Redis using private IP (no authentication required for Memorystore)
                var configOptions = new ConfigurationOptions
                {
                    EndPoints = { $"{redisHost}:{redisPort}" },
                    ConnectTimeout = 5000,
                    SyncTimeout = 5000,
                    AbortOnConnectFail = false
                };

                _redis = ConnectionMultiplexer.Connect(configOptions);
                _database = _redis.GetDatabase();

                _logger.LogInformation($"Connected to Redis at {redisHost}:{redisPort}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to Redis");
                throw;
            }
        }

        /// <summary>
        /// Caches the latest 3 users in Redis
        /// Data is serialized to JSON and stored with 60-second expiration
        /// This method is called after a new user is created in the database
        /// </summary>
        public async Task CacheLatestUsersAsync(List<User> users)
        {
            try
            {
                // Serialize the user list to JSON format
                var json = JsonSerializer.Serialize(users);

                // Store in Redis with TTL (Time To Live) of 60 seconds
                await _database.StringSetAsync(
                    LATEST_USERS_KEY,
                    json,
                    TimeSpan.FromSeconds(CACHE_TTL_SECONDS)
                );

                _logger.LogInformation($"Cached {users.Count} latest users in Redis with {CACHE_TTL_SECONDS}s TTL");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching users in Redis");
                // Don't throw - caching failure shouldn't break the application
            }
        }

        /// <summary>
        /// Retrieves the cached latest users from Redis
        /// Returns null if cache is empty or expired
        /// Frontend can use this for faster data access without hitting the database
        /// </summary>
        public async Task<List<User>?> GetCachedLatestUsersAsync()
        {
            try
            {
                // Try to get cached data from Redis
                var cachedJson = await _database.StringGetAsync(LATEST_USERS_KEY);

                // If cache miss, return null
                if (!cachedJson.HasValue)
                {
                    _logger.LogInformation("Cache miss for latest users");
                    return null;
                }

                // Deserialize JSON back to User list
                var users = JsonSerializer.Deserialize<List<User>>(cachedJson.ToString());

                _logger.LogInformation($"Cache hit: Retrieved {users?.Count ?? 0} users from Redis");
                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving cached users from Redis");
                return null; // Return null on error, let caller fetch from database
            }
        }

        /// <summary>
        /// Clears the cached latest users from Redis
        /// Used when cache needs to be invalidated manually
        /// </summary>
        public async Task ClearCacheAsync()
        {
            try
            {
                await _database.KeyDeleteAsync(LATEST_USERS_KEY);
                _logger.LogInformation("Cleared latest users cache from Redis");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing Redis cache");
            }
        }

        /// <summary>
        /// Gets the Redis database instance for direct operations
        /// Used by other services that need direct Redis access
        /// </summary>
        public IDatabase GetDatabase()
        {
            return _database;
        }

        /// <summary>
        /// Checks if Redis connection is healthy
        /// Used for health checks and monitoring
        /// </summary>
        public bool IsConnected()
        {
            return _redis.IsConnected;
        }
    }
}
