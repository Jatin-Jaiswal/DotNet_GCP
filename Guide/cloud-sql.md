# Cloud SQL (PostgreSQL) Integration

## Library Used
`Npgsql` (PostgreSQL .NET driver)

```xml
<!-- backend/src/DotNetGcpApp.csproj -->
<PackageReference Include="Npgsql" Version="7.0.4" />
```

## Related File
`backend/src/CloudSql/PostgresService.cs`

## Configuration Flow
```
Secret Manager → Fetch at Runtime → .NET Code → Cloud SQL Connection
```

### ConfigMap After Moving to Secret Manager
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: backend-config
data:
  ASPNETCORE_URLS: "http://+:8080"
  ASPNETCORE_ENVIRONMENT: "Production"
```
All service configuration now comes from Secret Manager.

### Secret Retrieval in .NET Code
```csharp
public PostgresService(SecretManagerService secretManager, ILogger<PostgresService> logger)
{
    _logger = logger;
    
    var host = secretManager.GetSecretValue("cloudsql-host");
    var database = secretManager.GetSecretValue("cloudsql-database");
    var username = secretManager.GetSecretValue("cloudsql-username");
    var password = secretManager.GetSecretValue("cloudsql-password");

    _connectionString = $"Host={host};Database={database};Username={username};Password={password};Pooling=true;MinPoolSize=0;MaxPoolSize=100;ConnectionLifetime=0;";
    
    _logger.LogInformation("PostgreSQL connection initialized with all config from Secret Manager");
}
```

### Legacy Pattern (Removed)
```csharp
var host = Environment.GetEnvironmentVariable("CLOUDSQL_HOST") 
           ?? configuration["CloudSql:Host"] 
           ?? "10.92.160.3";
```
This fallback pattern is no longer required because Secret Manager supplies the values.

---

## Core Operations

### Initialize Database (Create Table)
```csharp
public async Task InitializeDatabaseAsync()
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
}
```

**What Happens**
1. Opens network connection from GKE pod to Cloud SQL
2. Sends SQL DDL command over the PostgreSQL protocol
3. Cloud SQL executes the command and returns the result
4. Connection closes automatically via the `using` block

### Insert Data (Parameterized Query)
```csharp
public async Task<User> InsertUserAsync(string name, string email)
{
    using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync();

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
        return new User
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Email = reader.GetString(2),
            CreatedAt = reader.GetDateTime(3)
        };
    }

    throw new InvalidOperationException("Insert returned no rows");
}
```

**Why Parameterized Queries Matter**
- Prevent SQL injection by separating SQL text from user input
- PostgreSQL receives query plan and parameter values independently
- Driver handles type conversion and escaping

### Query Data
```csharp
public async Task<List<User>> GetAllUsersAsync()
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

    return users;
}
```

**Key Points**
- Uses async APIs for scalability
- Data reader pattern keeps memory usage low
- Automatic type mapping (e.g., `GetInt32`, `GetString`)

---

## End-to-End Flow
- Application fetches credentials from Secret Manager at startup
- Npgsql resolves the Cloud SQL private IP and opens a TCP connection on port 5432
- PostgreSQL authentication uses username and password from Secret Manager
- Connection pooling keeps connections ready for reuse
- All traffic stays on the Google Cloud VPC (no public exposure)

## Why Npgsql?
- Official PostgreSQL driver for .NET
- High performance and mature connection pooling
- Async/await friendly API surface
- Works with Cloud SQL without extra adapters
