using DotNetGcpApp.CloudSql;
using DotNetGcpApp.Redis;
using DotNetGcpApp.PubSub;
using DotNetGcpApp.Storage;
using DotNetGcpApp.Services;

/// <summary>
/// Main entry point for the .NET 6 Web API application
/// This file configures all services, middleware, and routing
/// Integrates with GCP services: Cloud SQL, Redis, Pub/Sub, and Storage
/// </summary>

var builder = WebApplication.CreateBuilder(args);

// ======================================
// LOAD ENVIRONMENT VARIABLES FROM .env FILE
// ======================================
// This allows us to use .env file for local development
// In production (Kubernetes), environment variables come from ConfigMap
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            continue;

        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
        }
    }
}

// ======================================
// CONFIGURE LOGGING
// ======================================
// Set up logging to console with detailed information
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// ======================================
// CONFIGURE CORS (Cross-Origin Requests)
// ======================================
// Allow the Angular frontend to call this API
// This is needed because frontend and backend run on different ports/domains
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins(
                  "http://localhost:4200", 
                  "http://localhost", 
                  "http://localhost:80",
                  "http://34.131.29.2",      // Bastion VM public IP
                  "http://10.0.2.10"         // Frontend Internal Load Balancer
              )
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// ======================================
// REGISTER GCP SERVICES
// ======================================
// Register all custom services as singletons
// Singletons are created once and reused throughout the application lifetime

// Cloud SQL PostgreSQL service for database operations
builder.Services.AddSingleton<PostgresService>();

// Redis service for caching
builder.Services.AddSingleton<RedisService>();

// Pub/Sub service for event publishing
builder.Services.AddSingleton<PubSubService>();

// Cloud Storage service for file uploads
builder.Services.AddSingleton<StorageService>();

// Event storage service for Pub/Sub events (singleton so all pods share)
builder.Services.AddSingleton<EventStorageService>();

// ======================================
// REGISTER BACKGROUND SERVICES
// ======================================
// Pub/Sub subscriber runs in the background to receive messages
builder.Services.AddHostedService<PubSubSubscriberService>();

// ======================================
// CONFIGURE MVC AND CONTROLLERS
// ======================================
builder.Services.AddControllers();

// Add API documentation with Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ======================================
// BUILD THE APPLICATION
// ======================================
var app = builder.Build();

// ======================================
// INITIALIZE DATABASE ON STARTUP
// ======================================
// Create the users table if it doesn't exist
// This ensures the database is ready before accepting requests
try
{
    var postgresService = app.Services.GetRequiredService<PostgresService>();
    await postgresService.InitializeDatabaseAsync();
    app.Logger.LogInformation("Database initialized successfully");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Failed to initialize database - application will continue but database operations may fail");
}

// ======================================
// CONFIGURE MIDDLEWARE PIPELINE
// ======================================

// Enable Swagger UI in all environments (for testing and documentation)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "DotNet GCP API v1");
    c.RoutePrefix = string.Empty; // Serve Swagger UI at root URL
});

// Enable CORS (must be before UseAuthorization)
app.UseCors("AllowAngularApp");

// Enable HTTPS redirection for security
app.UseHttpsRedirection();

// Enable authorization middleware
app.UseAuthorization();

// Map controller routes
app.MapControllers();

// ======================================
// HEALTH CHECK ENDPOINT
// ======================================
// Simple health check endpoint to verify the API is running
app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow,
        version = "1.0.0"
    });
});

// ======================================
// START THE APPLICATION
// ======================================
app.Logger.LogInformation("Starting DotNet GCP Application");
app.Logger.LogInformation("Listening on: http://0.0.0.0:8080");

app.Run();
