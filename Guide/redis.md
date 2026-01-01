# Memorystore Redis Integration

## Library Used
`StackExchange.Redis`

```xml
<!-- backend/src/DotNetGcpApp.csproj -->
<PackageReference Include="StackExchange.Redis" Version="2.6.122" />
```

## Related File
`backend/src/Redis/RedisService.cs`

## Configuration

**ConfigMap** (`k8s/backend.yaml`)
```yaml
data:
  Redis__Host: "10.84.245.243"
  Redis__Port: "6379"
```

**Runtime Resolution**
```csharp
public RedisService(IConfiguration configuration, ILogger<RedisService> logger)
{
    _logger = logger;

    var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") 
                    ?? configuration["Redis:Host"] 
                    ?? "10.127.80.5";
    
    var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") 
                    ?? configuration["Redis:Port"] 
                    ?? "6379";

    var configOptions = new ConfigurationOptions
    {
        EndPoints = { $"{redisHost}:{redisPort}" },
        ConnectTimeout = 5000,
        SyncTimeout = 5000,
        AbortOnConnectFail = false
    };

    _redis = ConnectionMultiplexer.Connect(configOptions);
    _database = _redis.GetDatabase();
}
```

## Core Operations

### Cache Latest Users
```csharp
public async Task CacheLatestUsersAsync(List<User> users)
{
    const string CACHE_KEY = "users:latest:3";
    const int CACHE_TTL_SECONDS = 60;
    
    var json = JsonSerializer.Serialize(users);

    await _database.StringSetAsync(
        CACHE_KEY,
        json,
        TimeSpan.FromSeconds(CACHE_TTL_SECONDS)
    );

    _logger.LogInformation($"Cached {users.Count} users with {CACHE_TTL_SECONDS}s TTL");
}
```

**Flow**
1. Serializes .NET objects to JSON
2. Sends `SET users:latest:3 {json} EX 60` over the Redis protocol
3. Redis stores the value in RAM and applies a TTL

### Retrieve Cached Users
```csharp
public async Task<List<User>?> GetCachedLatestUsersAsync()
{
    const string CACHE_KEY = "users:latest:3";
    var cachedJson = await _database.StringGetAsync(CACHE_KEY);
    
    if (cachedJson.IsNullOrEmpty)
    {
        _logger.LogInformation("Cache miss");
        return null;
    }

    _logger.LogInformation("Cache hit");
    return JsonSerializer.Deserialize<List<User>>(cachedJson!);
}
```

### Clear Cache
```csharp
public async Task ClearCacheAsync()
{
    const string CACHE_KEY = "users:latest:3";
    await _database.KeyDeleteAsync(CACHE_KEY);
    _logger.LogInformation("Cache cleared");
}
```

## Controller Usage
```csharp
[HttpGet("latest")]
public async Task<IActionResult> GetLatestUsers()
{
    var cachedUsers = await _redisService.GetCachedLatestUsersAsync();

    if (cachedUsers != null)
    {
        return Ok(new
        {
            success = true,
            source = "redis-cache",
            data = cachedUsers
        });
    }

    var users = await _postgresService.GetLatestUsersAsync(3);
    await _redisService.CacheLatestUsersAsync(users);

    return Ok(new
    {
        success = true,
        source = "database",
        data = users
    });
}
```

## Why Cache?
- Database reads typically take 50-100 ms
- Redis responses arrive in approximately 1-5 ms
- Cache-aside strategy keeps data fresh while reducing load

## Operational Notes
- Memorystore Redis is reachable via private IP, so no password is required
- Connection multiplexer keeps a single shared connection for the application
- TTL ensures automatic eviction and prevents stale data
