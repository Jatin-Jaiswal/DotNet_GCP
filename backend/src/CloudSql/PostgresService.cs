using Npgsql;
using DotNetGcpApp.Models;

namespace DotNetGcpApp.CloudSql
{
    /// <summary>
    /// Service for managing PostgreSQL database operations
    /// Connects to Google Cloud SQL (PostgreSQL 15) instance using private IP
    /// Handles user data storage and retrieval with connection pooling
    /// </summary>
    public class PostgresService
    {
        private readonly string _connectionString;
        private readonly ILogger<PostgresService> _logger;

        /// <summary>
        /// Constructor that initializes the PostgreSQL connection string
        /// Uses environment variables for configuration (from .env file or Kubernetes ConfigMap)
        /// </summary>
        public PostgresService(IConfiguration configuration, ILogger<PostgresService> logger)
        {
            _logger = logger;
            
            // Build connection string using environment variables
            // Priority: Environment variables > Configuration file > Default values
            var host = Environment.GetEnvironmentVariable("CLOUDSQL_HOST") 
                       ?? configuration["CloudSql:Host"] 
                       ?? "10.92.160.3";
            
            var database = Environment.GetEnvironmentVariable("CLOUDSQL_DATABASE") 
                           ?? configuration["CloudSql:Database"] 
                           ?? "dotnetdb";
            
            var username = Environment.GetEnvironmentVariable("CLOUDSQL_USERNAME") 
                           ?? configuration["CloudSql:Username"] 
                           ?? "postgres";
            
            var password = Environment.GetEnvironmentVariable("CLOUDSQL_PASSWORD") 
                           ?? configuration["CloudSql:Password"] 
                           ?? "postgres";

            _connectionString = $"Host={host};Database={database};Username={username};Password={password};Pooling=true;MinPoolSize=0;MaxPoolSize=100;ConnectionLifetime=0;";
        }

        /// <summary>
        /// Creates the users table if it doesn't exist
        /// This method is called at application startup to ensure the database schema is ready
        /// </summary>
        public async Task InitializeDatabaseAsync()
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS users (
                        id SERIAL PRIMARY KEY,
                        name VARCHAR(255) NOT NULL,
                        email VARCHAR(255) NOT NULL UNIQUE,
                        created_at TIMESTAMP NOT NULL DEFAULT NOW()
                    )";

                using var command = new NpgsqlCommand(createTableQuery, connection);
                await command.ExecuteNonQueryAsync();

                _logger.LogInformation("Database initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing database");
                throw;
            }
        }

        /// <summary>
        /// Inserts a new user into the PostgreSQL database
        /// Uses parameterized queries to prevent SQL injection
        /// Returns the newly created user with generated ID
        /// </summary>
        public async Task<User> InsertUserAsync(string name, string email)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                // Insert query with RETURNING clause to get the generated ID
                var insertQuery = @"
                    INSERT INTO users (name, email, created_at) 
                    VALUES (@name, @email, @created_at) 
                    RETURNING id, name, email, created_at";

                using var command = new NpgsqlCommand(insertQuery, connection);
                command.Parameters.AddWithValue("name", name);
                command.Parameters.AddWithValue("email", email);
                command.Parameters.AddWithValue("created_at", DateTime.UtcNow);

                using var reader = await command.ExecuteReaderAsync();
                
                if (await reader.ReadAsync())
                {
                    var user = new User
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Email = reader.GetString(2),
                        CreatedAt = reader.GetDateTime(3)
                    };

                    _logger.LogInformation($"User created: {user.Id} - {user.Name}");
                    return user;
                }

                throw new Exception("Failed to insert user");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error inserting user: {name}");
                throw;
            }
        }

        /// <summary>
        /// Retrieves all users from the database
        /// Results are ordered by creation date (newest first)
        /// </summary>
        public async Task<List<User>> GetAllUsersAsync()
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var selectQuery = "SELECT id, name, email, created_at FROM users ORDER BY created_at DESC";

                using var command = new NpgsqlCommand(selectQuery, connection);
                using var reader = await command.ExecuteReaderAsync();

                var users = new List<User>();

                while (await reader.ReadAsync())
                {
                    users.Add(new User
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Email = reader.GetString(2),
                        CreatedAt = reader.GetDateTime(3)
                    });
                }

                _logger.LogInformation($"Retrieved {users.Count} users from database");
                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving users");
                throw;
            }
        }

        /// <summary>
        /// Gets the last N users from the database
        /// Used for retrieving recent users to cache in Redis
        /// </summary>
        public async Task<List<User>> GetLatestUsersAsync(int count)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var selectQuery = $"SELECT id, name, email, created_at FROM users ORDER BY created_at DESC LIMIT @count";

                using var command = new NpgsqlCommand(selectQuery, connection);
                command.Parameters.AddWithValue("count", count);

                using var reader = await command.ExecuteReaderAsync();

                var users = new List<User>();

                while (await reader.ReadAsync())
                {
                    users.Add(new User
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Email = reader.GetString(2),
                        CreatedAt = reader.GetDateTime(3)
                    });
                }

                _logger.LogInformation($"Retrieved {users.Count} latest users");
                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving latest users");
                throw;
            }
        }
    }
}
