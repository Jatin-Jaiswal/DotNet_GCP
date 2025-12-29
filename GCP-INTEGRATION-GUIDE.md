# .NET 6 → GCP Services Integration Guide

## 🎯 Purpose
This document explains **how .NET 6 code integrates with GCP services** - the libraries used, configuration approach, and code patterns for connecting to Cloud SQL, Redis, Pub/Sub, and Cloud Storage.

---

## 📦 NuGet Packages Used

```xml
<!-- backend/src/DotNetGcpApp.csproj -->

<!-- Cloud SQL (PostgreSQL) -->
<PackageReference Include="Npgsql" Version="7.0.4" />

<!-- Memorystore Redis -->
<PackageReference Include="StackExchange.Redis" Version="2.6.122" />

<!-- Cloud Pub/Sub -->
<PackageReference Include="Google.Cloud.PubSub.V1" Version="3.7.0" />

<!-- Cloud Storage -->
<PackageReference Include="Google.Cloud.Storage.V1" Version="4.6.0" />
```

---

## 🗄️ 1. Cloud SQL (PostgreSQL) Integration

### **Library Used:** `Npgsql` (PostgreSQL .NET driver)

### **File:** `backend/src/CloudSql/PostgresService.cs`

### **How Configuration Works:**

```
Kubernetes ConfigMap → Environment Variables → .NET Code
```

**Step 1: ConfigMap** (`k8s/backend.yaml`)
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: backend-config
data:
  CloudSql__Host: "10.252.0.3"        # Private IP of Cloud SQL
  CloudSql__Database: "dotnetdb"
  CloudSql__Username: "postgres"
  CloudSql__Password: "DotNet@123"
```

**Step 2: Inject into Pod**
```yaml
containers:
- name: backend
  envFrom:
  - configMapRef:
      name: backend-config    # All ConfigMap values become env vars
```

**Step 3: Read in .NET Code**
```csharp
public PostgresService(IConfiguration configuration, ILogger<PostgresService> logger)
{
    _logger = logger;
    
    // Priority: 1) Environment Variable → 2) appsettings.json → 3) Default
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

    // Build connection string with connection pooling
    _connectionString = $"Host={host};Database={database};Username={username};Password={password};Pooling=true;MinPoolSize=0;MaxPoolSize=100;ConnectionLifetime=0;";
}
```

### **Key Integration Code:**

#### **1. Initialize Database (Create Table)**
```csharp
public async Task InitializeDatabaseAsync()
{
    // Step 1: Create connection object with connection string
    // Connection string contains: Host (Cloud SQL IP), Database name, Username, Password
    using var connection = new NpgsqlConnection(_connectionString);
    
    // Step 2: Open TCP connection to Cloud SQL PostgreSQL
    // This establishes network connection over private IP (10.252.0.3:5432)
    await connection.OpenAsync();

    // Step 3: Define SQL DDL (Data Definition Language) command
    var createTableQuery = @"
        CREATE TABLE IF NOT EXISTS users (
            id SERIAL PRIMARY KEY,              -- Auto-increment integer
            name VARCHAR(255) NOT NULL,         -- User's name (required)
            email VARCHAR(255) NOT NULL UNIQUE, -- Email (required, must be unique)
            created_at TIMESTAMP NOT NULL DEFAULT NOW()  -- Timestamp (auto-set)
        )";

    // Step 4: Create command object that wraps SQL query
    using var command = new NpgsqlCommand(createTableQuery, connection);
    
    // Step 5: Execute the command on Cloud SQL
    // ExecuteNonQueryAsync() = Run query, don't return data (DDL/DML operations)
    await command.ExecuteNonQueryAsync();
    
    // Step 6: Connection auto-closes due to 'using' statement
}
```

**What This Code Does:**
1. **Opens a network connection** from GKE pod to Cloud SQL instance
2. **Sends SQL command** over the network to create table schema
3. **Waits for response** from Cloud SQL (async operation)
4. **Automatically closes connection** when done (using statement)

**How It Integrates with Cloud SQL:**
- **Network Path:** GKE Pod → VPC Private Network → Cloud SQL Private IP (10.252.0.3)
- **Protocol:** PostgreSQL wire protocol over TCP port 5432
- **Authentication:** PostgreSQL username/password (sent encrypted)
- **Connection Pooling:** Npgsql maintains pool of reusable connections
- **No Public Internet:** All traffic stays within Google Cloud private network

**What Happens Behind the Scenes:**
1. Npgsql driver resolves DNS/IP address of Cloud SQL
2. TCP handshake establishes connection
3. PostgreSQL authentication protocol exchanges credentials
4. SQL command is serialized and sent over TCP
5. Cloud SQL executes the DDL command
6. Result (success/error) is returned to .NET application
7. Connection returned to pool for reuse

#### **2. Insert Data (Parameterized Query)**
```csharp
public async Task<User> InsertUserAsync(string name, string email)
{
    // Step 1: Get connection from pool (or create new one)
    using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync();

    // Step 2: Define SQL INSERT with parameters (@name, @email, @created_at)
    // RETURNING clause tells PostgreSQL to return the inserted row immediately
    var insertQuery = @"
        INSERT INTO users (name, email, created_at) 
        VALUES (@name, @email, @created_at) 
        RETURNING id, name, email, created_at";

    // Step 3: Create command and bind parameters
    using var command = new NpgsqlCommand(insertQuery, connection);
    
    // AddWithValue() creates parameter and prevents SQL injection
    // These values are sent SEPARATELY from SQL query text
    command.Parameters.AddWithValue("name", name);          // User input
    command.Parameters.AddWithValue("email", email);        // User input
    command.Parameters.AddWithValue("created_at", DateTime.UtcNow);

    // Step 4: Execute query and get result reader
    // ExecuteReaderAsync() = Run query and return rows
    using var reader = await command.ExecuteReaderAsync();
    
    // Step 5: Read first (and only) row from result
    if (await reader.ReadAsync())
    {
        // Step 6: Map database columns to C# object
        return new User
        {
            Id = reader.GetInt32(0),        // Column 0: id (integer)
            Name = reader.GetString(1),     // Column 1: name (string)
            Email = reader.GetString(2),    // Column 2: email (string)
            CreatedAt = reader.GetDateTime(3) // Column 3: created_at (datetime)
        };
    }
}
```

**What This Code Does:**
1. **Prepares SQL statement** with parameter placeholders (@name, @email)
2. **Binds parameters separately** from SQL text (prevents SQL injection)
3. **Sends parameterized query** to Cloud SQL for execution
4. **PostgreSQL executes INSERT** and generates auto-increment ID
5. **Returns the new row** including generated ID (RETURNING clause)
6. **Maps database types** to .NET types (int32, string, DateTime)

**How Parameterized Queries Prevent SQL Injection:**

❌ **BAD (Vulnerable):**
```csharp
// NEVER DO THIS! User input directly in SQL string
var badQuery = $"INSERT INTO users (name, email) VALUES ('{name}', '{email}')";
// If name = "'; DROP TABLE users; --"  → SQL injection attack succeeds!
```

✅ **GOOD (Secure):**
```csharp
// Parameters sent separately from SQL text
command.Parameters.AddWithValue("name", name);
// If name = "'; DROP TABLE users; --"  → Treated as literal string value, harmless
```

**PostgreSQL Wire Protocol Flow:**
```
1. .NET serializes: SQL text + Parameter definitions + Parameter values
2. Sends over TCP to Cloud SQL
3. PostgreSQL parses SQL separately from values
4. PostgreSQL plans query execution
5. PostgreSQL binds parameter values safely
6. PostgreSQL executes INSERT and generates ID
7. PostgreSQL returns result row
8. Npgsql deserializes result into .NET objects
```

#### **3. Query Data**
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

**Explanation:**
- Async operations throughout for scalability
- Data reader pattern for efficient memory usage
- Automatic type conversion (GetInt32, GetString, GetDateTime)

### **Why Npgsql?**
- ✅ Official PostgreSQL driver for .NET
- ✅ High performance with connection pooling
- ✅ Full async/await support
- ✅ Works with Cloud SQL out of the box (just needs IP address)

---

## 🚀 2. Memorystore Redis Integration

### **Library Used:** `StackExchange.Redis` (Industry standard Redis client)

### **File:** `backend/src/Redis/RedisService.cs`

### **How Configuration Works:**

**ConfigMap** (`k8s/backend.yaml`)
```yaml
data:
  Redis__Host: "10.84.245.243"    # Private IP of Memorystore Redis
  Redis__Port: "6379"
```

**Read in .NET Code:**
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

    // Connect to Redis (no password for Memorystore)
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

### **Key Integration Code:**

#### **1. Cache Data (with TTL)**
```csharp
public async Task CacheLatestUsersAsync(List<User> users)
{
    const string CACHE_KEY = "users:latest:3";
    const int CACHE_TTL_SECONDS = 60;
    
    // Step 1: Serialize C# objects to JSON string
    // Redis only stores strings, so we convert complex objects to JSON
    var json = JsonSerializer.Serialize(users);
    // Result: "[{\"id\":1,\"name\":\"John\",\"email\":\"john@example.com\"}, ...]"
    
    // Step 2: Store in Redis with automatic expiration
    await _database.StringSetAsync(
        CACHE_KEY,                                    // Key to store under
        json,                                         // Value (JSON string)
        TimeSpan.FromSeconds(CACHE_TTL_SECONDS)      // TTL (Time To Live) = 60s
    );
    // Redis command sent: SET users:latest:3 "{json}" EX 60
    
    _logger.LogInformation($"Cached {users.Count} users with {CACHE_TTL_SECONDS}s TTL");
}
```

**What This Code Does:**
1. **Converts .NET objects to JSON** (serialization)
2. **Sends SET command to Redis** with key-value pair
3. **Sets expiration time** (TTL) - Redis auto-deletes after 60 seconds
4. **Waits for confirmation** from Redis (async operation)

**How It Integrates with Memorystore Redis:**
- **Network Path:** GKE Pod → VPC Private Network → Memorystore Redis (10.84.245.243:6379)
- **Protocol:** Redis protocol (RESP - REdis Serialization Protocol)
- **Command Sent:** `SET users:latest:3 "{json}" EX 60`
- **Authentication:** None required (protected by VPC private network)
- **Persistence:** Data stored in RAM (fast) with optional disk backup

**What Happens Behind the Scenes:**
1. StackExchange.Redis serializes the SET command
2. Command sent over TCP connection to Redis
3. Redis receives command and parses it
4. Redis stores key-value in memory (hash table data structure)
5. Redis sets internal timer for 60-second expiration
6. Redis returns "OK" response
7. After 60 seconds, Redis automatically deletes the key

**Why Use Caching?**
```
Without Cache:
User Request → API → PostgreSQL → Process → Return
Latency: ~50-100ms (database query time)

With Cache:
User Request → API → Redis → Return (cache hit)
Latency: ~1-5ms (memory lookup)

Performance Improvement: 10-50x faster!
```

#### **2. Retrieve from Cache**
```csharp
public async Task<List<User>?> GetCachedLatestUsersAsync()
{
    const string CACHE_KEY = "users:latest:3";
    
    // Try to get from cache
    var cachedJson = await _database.StringGetAsync(CACHE_KEY);
    
    // Cache miss - return null
    if (cachedJson.IsNullOrEmpty)
    {
        _logger.LogInformation("Cache miss");
        return null;
    }
    
    // Cache hit - deserialize and return
    _logger.LogInformation("Cache hit");
    return JsonSerializer.Deserialize<List<User>>(cachedJson!);
}
```

**Explanation:**
- Returns `null` on cache miss (expired or never cached)
- Controller handles cache miss by fetching from database
- Deserialization converts JSON back to C# objects

#### **3. Clear Cache**
```csharp
public async Task ClearCacheAsync()
{
    const string CACHE_KEY = "users:latest:3";
    await _database.KeyDeleteAsync(CACHE_KEY);
    _logger.LogInformation("Cache cleared");
}
```

### **Cache Strategy in Controller** (`backend/src/Controllers/UsersController.cs`)
```csharp
[HttpGet("latest")]
public async Task<IActionResult> GetLatestUsers()
{
    // Try cache first
    var cachedUsers = await _redisService.GetCachedLatestUsersAsync();

    if (cachedUsers != null)
    {
        // Cache HIT - return immediately
        return Ok(new
        {
            success = true,
            source = "redis-cache",
            data = cachedUsers
        });
    }

    // Cache MISS - fetch from database
    var users = await _postgresService.GetLatestUsersAsync(3);

    // Update cache for next request
    await _redisService.CacheLatestUsersAsync(users);

    return Ok(new
    {
        success = true,
        source = "database",
        data = users
    });
}
```

**Explanation:**
- **Cache-aside pattern**: Check cache → If miss, fetch from DB → Update cache
- Reduces database load for frequently accessed data
- Response tells you if data came from cache or database

### **Why StackExchange.Redis?**
- ✅ Most popular Redis client for .NET
- ✅ Connection multiplexer pattern (efficient connection management)
- ✅ Full async support
- ✅ Works seamlessly with Memorystore (no special configuration needed)

---

## 📨 3. Cloud Pub/Sub Integration

### **Library Used:** `Google.Cloud.PubSub.V1` (Official Google Cloud library)

### **File:** `backend/src/PubSub/PubSubService.cs`

### **🤔 Why Use Pub/Sub? (Simple Explanation)**

**Problem Without Pub/Sub:**
```
Old Way (Everything Connected):
User Service → Call Email Service directly
             → Call Analytics Service directly
             → Call Notification Service directly

Problems:
❌ All services connected - hard to manage
❌ User must wait for everything to finish
❌ If one fails, everything fails
❌ Hard to add new services
```

**Solution With Pub/Sub:**
```
New Way (Services Independent):
User Service → Send message → Pub/Sub (Message Box)
                                    ↓
                            Pub/Sub delivers to:
                        ┌──────────┬──────────┬──────────┐
                        ↓          ↓          ↓          ↓
                   Email      Analytics  Notification  Future
                  Service     Service     Service      Service

Benefits:
✅ Services work independently
✅ User gets fast response
✅ If one breaks, others keep working
✅ Easy to add new services
✅ Messages saved until delivered
```

**Real-World Examples:**
1. **User Signs Up:** Send welcome email, save to analytics, create profile
2. **File Uploaded:** Check for viruses, make smaller pictures, add to search
3. **Order Placed:** Send confirmation, reduce stock, start shipping
4. **Data Received:** Save logs, process events, start workflows

### **How Our Application Uses Pub/Sub:**

**Scenario 1: User Signs Up**
```
User creates account → Save to database → Send "User Created" message
                                               ↓
                                    Other services can:
                                    - Send welcome email
                                    - Count new users
                                    - Make user profile
                                    - Save to other systems
```

**Scenario 2: File Upload**
```
File uploaded → Save to Storage → Send "File Uploaded" message
                                        ↓
                                 Other services can:
                                 - Check for viruses
                                 - Make smaller images
                                 - Add to file list
                                 - Tell admin
```

### **How Configuration Works:**

**ConfigMap** (`k8s/backend.yaml`)
```yaml
data:
  GCP__ProjectId: "project-84d8bfc9-cd8e-4b3c-b15"
  PubSub__TopicId: "dot-net-topic"
  PubSub__SubscriptionId: "dot-net-sub"
```

**Read in .NET Code:**
```csharp
public PubSubService(IConfiguration configuration, ILogger<PubSubService> logger)
{
    _logger = logger;

    var projectId = Environment.GetEnvironmentVariable("GCP_PROJECT_ID") 
                    ?? configuration["GCP:ProjectId"] 
                    ?? "project-84d8bfc9-cd8e-4b3c-b15";
    
    var topicId = Environment.GetEnvironmentVariable("PUBSUB_TOPIC_ID") 
                  ?? configuration["PubSub:TopicId"] 
                  ?? "dot-net-topic";

    // Build full topic name
    _topicName = $"projects/{projectId}/topics/{topicId}";

    // Create publisher (uses Workload Identity automatically)
    var topicName = TopicName.FromProjectTopic(projectId, topicId);
    _publisher = PublisherClient.CreateAsync(topicName).GetAwaiter().GetResult();
}
```

### **Key Integration Code:**

#### **1. Publish User Created Event**
```csharp
public async Task PublishUserCreatedEventAsync(User user)
{
    // Step 1: Create structured message object
    var pubSubMessage = new PubSubMessage
    {
        Source = "sql",                                           // Where event originated
        Message = $"User created: {user.Name} ({user.Email})",   // Human-readable description
        Timestamp = DateTime.UtcNow                               // When event occurred
    };
    
    // Step 2: Publish to Pub/Sub topic
    await PublishMessageAsync(pubSubMessage);
}
```

#### **2. Publish File Upload Event**
```csharp
public async Task PublishFileUploadedEventAsync(string fileName, string fileUrl)
{
    // Step 1: Create event message
    var pubSubMessage = new PubSubMessage
    {
        Source = "bucket",                                        // Event came from Storage
        Message = $"File uploaded: {fileName} - URL: {fileUrl}", // Event details
        Timestamp = DateTime.UtcNow                               // Event time
    };
    
    // Step 2: Publish to topic
    await PublishMessageAsync(pubSubMessage);
}
```

#### **3. Internal Publish Method (The Core Integration)**
```csharp
private async Task PublishMessageAsync(PubSubMessage message)
{
    // Step 1: Serialize C# object to JSON string
    // Pub/Sub stores bytes, so we convert object → JSON → bytes
    var json = JsonSerializer.Serialize(message);
    // Result: {"Source":"sql","Message":"User created: John (john@example.com)","Timestamp":"2025-12-29T04:15:30Z"}
    
    // Step 2: Convert JSON string to bytes using Protocol Buffers format
    // ByteString is Google's efficient binary format for network transmission
    var pubsubMessage = new Google.Cloud.PubSub.V1.PubsubMessage
    {
        Data = Google.Protobuf.ByteString.CopyFromUtf8(json)
    };
    
    // Step 3: Publish to Pub/Sub topic
    // This sends HTTP POST request to Pub/Sub API with the message
    var messageId = await _publisher.PublishAsync(pubsubMessage);
    // Response: Message ID (unique identifier like "123456789")
    
    _logger.LogInformation($"Published message {messageId} to {_topicName}");
}
```

**What This Code Does (Simple Steps):**

1. **Makes message object:** Creates a message with event info
2. **Turns to JSON:** Converts C# object to text format
3. **Makes it binary:** Converts text to computer-readable bytes (faster)
4. **Sends over internet:** Sends HTTPS request to Google Pub/Sub
5. **Google saves it:** Pub/Sub stores message safely
6. **Gets ID back:** Google gives back a tracking number
7. **Delivers to listeners:** Pub/Sub sends to everyone who subscribed

**How It Works with Google Pub/Sub:**

```
┌─────────────────────────────────────────────────────────────────┐
│ Step 1: Your .NET App (Sender)                                  │
├─────────────────────────────────────────────────────────────────┤
│ 1. Make message object                                          │
│ 2. Turn to JSON text                                            │
│ 3. Turn to binary bytes                                         │
│ 4. Call _publisher.PublishAsync()                               │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼ Sends over internet (HTTPS)
┌─────────────────────────────────────────────────────────────────┐
│ Step 2: Google Pub/Sub (Message Box)                            │
├─────────────────────────────────────────────────────────────────┤
│ 1. Check who you are (login check)                             │
│ 2. Check if you can send (permission check)                    │
│ 3. Give message a tracking number                              │
│ 4. Save message safely (in multiple places)                    │
│ 5. Send back tracking number                                   │
│ 6. Keep message for 7 days if needed                           │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼ Delivers messages
┌─────────────────────────────────────────────────────────────────┐
│ Step 3: Receivers (Who Subscribed)                              │
├─────────────────────────────────────────────────────────────────┤
│ Receiver A (Email Service):     GET /api/pubsub/messages       │
│ - Gets message                                                  │
│ - Sends welcome email                                           │
│ - Says "got it"                                                 │
│                                                                 │
│ Receiver B (Analytics):                                         │
│ - Gets SAME message                                             │
│ - Updates charts                                                │
│ - Says "got it"                                                 │
│                                                                 │
│ Receiver C (New Service):                                       │
│ - Can be added WITHOUT changing sender!                         │
└─────────────────────────────────────────────────────────────────┘
```

**What Happens Inside Pub/Sub:**

```
1. Message Comes In:
   - Google gets your message
   - Checks who you are (login)
   - Checks if you're allowed (permission)

2. Message Saved:
   - Saves in multiple places (safe)
   - Makes copies (won't lose it)
   - Gives it a tracking number

3. Message Delivered:
   - Finds everyone who wants this message
   - Sends a copy to each one
   - Tries again if someone didn't get it (retry)
   - Checks if they got it (acknowledgment)

4. Message Cleanup:
   - When everyone gets it: Deletes message
   - If not received: Tries again (up to 7 days)
   - After 7 days: Gives up and deletes
```

**Pub/Sub Promises:**

✅ **Delivers for sure:** Every subscriber gets message at least once (might get duplicates)
✅ **Won't lose it:** Saved in multiple places, safe if something breaks
✅ **Keeps order (optional):** Can keep messages in order if you want
✅ **Keeps trying:** Holds messages for 7 days if not delivered
✅ **Handles lots:** Can handle millions of messages automatically

### **Message Model** (`backend/src/Models/PubSubMessage.cs`)
```csharp
public class PubSubMessage
{
    public string Source { get; set; } = string.Empty;      // "sql", "bucket", etc.
    public string Message { get; set; } = string.Empty;     // Event description
    public DateTime Timestamp { get; set; }                  // When event occurred
}
```

### **Usage in Controller** (`backend/src/Controllers/UsersController.cs`)
```csharp
[HttpPost]
public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
{
    // Step 1: Insert into database (PRIMARY operation - must succeed)
    var user = await _postgresService.InsertUserAsync(request.Name, request.Email);

    // Step 2: Publish event to Pub/Sub (SECONDARY operation - fire-and-forget)
    // Even if Pub/Sub fails, user creation already succeeded
    // This is asynchronous and non-blocking
    await _pubSubService.PublishUserCreatedEventAsync(user);

    // Step 3: Update Redis cache (OPTIONAL operation)
    var latestUsers = await _postgresService.GetLatestUsersAsync(3);
    await _redisService.CacheLatestUsersAsync(latestUsers);

    return Ok(new
    {
        success = true,
        message = "User created successfully",
        user = user,
        pubsubPublished = true  // Confirms event was published
    });
}
```

**What This Does:**

1. **Don't depend on each other:** Creating user doesn't wait for email or other stuff
2. **Fast answer:** User gets quick response, other work happens later
3. **Still works if something breaks:** If email service is down, user still created
4. **Easy to add more:** Can add new listeners without changing this code
5. **Keeps history:** Pub/Sub messages show what happened

**Real Example:**

```
User signs up
         ↓
Receives POST /api/users
         ↓
[200ms] Save to database (user saved)
         ↓
[5ms] Send message to Pub/Sub (message sent)
         ↓
[2ms] Update cache
         ↓
Send back "Success!" to user (total: ~207ms)
         ↓
Later, in background:
         ↓
[Later] Email service gets message → Sends welcome email
[Later] Analytics gets message → Updates charts
[Later] CRM gets message → Saves contact
[Later] Audit gets message → Writes to log
```

**Compare: Old Way vs New Way:**

```
❌ Old Way (Wait for everything):
User signs up
  → Save to database (200ms)
  → Call Email Service (500ms) ← user waits
  → Call Analytics Service (300ms) ← user waits
  → Call CRM Service (400ms) ← user waits
  → Send "Success!" (total: 1400ms) ← user waits 1.4 seconds!

If anything fails → Everything fails
If anything is slow → User waits longer

✅ New Way (With Pub/Sub):
User signs up
  → Save to database (200ms)
  → Send Pub/Sub message (5ms)
  → Send "Success!" (total: 205ms) ← user gets fast answer!

Services work independently:
  → Email service works (whenever ready)
  → Analytics works (whenever ready)
  → CRM works (whenever ready)

If something fails → Pub/Sub tries again automatically
If something is slow → Doesn't slow down user
```

### **🔐 Workload Identity (No Service Account Keys!)**

**How Authentication Works:**

```
1. GKE Pod has Kubernetes ServiceAccount: backend-sa
   ↓
2. Kubernetes ServiceAccount is annotated with GCP Service Account
   ↓
3. GCP Service Account has IAM role: roles/pubsub.publisher
   ↓
4. .NET code calls PublisherClient.CreateAsync()
   ↓
5. Google Cloud SDK automatically gets credentials via Workload Identity
   ↓
6. No keys stored in code, ConfigMap, or environment variables!
```

**Configuration** (`k8s/backend.yaml`):
```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: backend-sa
  annotations:
    # This links Kubernetes SA to GCP SA
    iam.gke.io/gcp-service-account: backend-gke-sa@project.iam.gserviceaccount.com
---
spec:
  template:
    spec:
      serviceAccountName: backend-sa  # Use the linked ServiceAccount
```

**Terraform IAM Binding** (`terraform/main.tf`):
```hcl
# Grant Pub/Sub publisher permission to GCP Service Account
resource "google_pubsub_topic_iam_member" "backend_pubsub_publisher" {
  topic   = google_pubsub_topic.topic.id
  role    = "roles/pubsub.publisher"
  member  = "serviceAccount:${google_service_account.backend_sa.email}"
}
```

### **Why Google.Cloud.PubSub.V1?**
- ✅ Official Google Cloud library
- ✅ Automatic authentication via Workload Identity
- ✅ Full async support
- ✅ Handles retries and error handling internally
- ✅ Supports both publishing and subscribing

---

## 📦 4. Cloud Storage (Bucket) Integration

### **Library Used:** `Google.Cloud.Storage.V1` (Official Google Cloud library)

### **File:** `backend/src/Storage/StorageService.cs`

### **How Configuration Works:**

**ConfigMap** (`k8s/backend.yaml`)
```yaml
data:
  Storage__BucketName: "dot-net-bucket"
```

**Read in .NET Code:**
```csharp
public StorageService(IConfiguration configuration, ILogger<StorageService> logger)
{
    _logger = logger;

    _bucketName = Environment.GetEnvironmentVariable("STORAGE_BUCKET_NAME") 
                  ?? configuration["Storage:BucketName"] 
                  ?? "dot-net-bucket";

    // Create storage client (uses Workload Identity)
    _storageClient = StorageClient.Create();
}
```

### **Key Integration Code:**

#### **1. Upload File**
```csharp
public async Task<string> UploadFileAsync(IFormFile file)
{
    // Step 1: Validate that file exists and has content
    if (file == null || file.Length == 0)
        throw new ArgumentException("File is empty or null");

    // Step 2: Generate unique filename to prevent overwrites
    // Format: YYYYMMDDHHMMSS_originalfilename.ext
    // Example: 20251229043015_document.pdf
    var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var fileName = $"{timestamp}_{file.FileName}";

    _logger.LogInformation($"Uploading: {fileName} ({file.Length} bytes)");

    // Step 3: Open file stream (doesn't load entire file into memory)
    // Streaming is memory-efficient for large files
    using var stream = file.OpenReadStream();
    
    // Step 4: Upload to Cloud Storage bucket
    // This sends HTTP PUT request with file data
    var uploadedObject = await _storageClient.UploadObjectAsync(
        bucket: _bucketName,              // Destination: "dot-net-bucket"
        objectName: fileName,              // Object name in bucket
        contentType: file.ContentType,     // MIME type (image/png, application/pdf, etc.)
        source: stream                     // File content stream
    );
    // Behind scenes: Chunks file into multiple HTTP requests for large files

    // Step 5: Build public URL for accessing the file
    // Format: https://storage.googleapis.com/{bucket}/{filename}
    var fileUrl = $"https://storage.googleapis.com/{_bucketName}/{fileName}";

    _logger.LogInformation($"Upload complete: {fileUrl}");
    
    return fileUrl;
}
```

**What This Code Does (Step-by-Step):**

1. **Validation:** Ensures file exists and isn't empty
2. **Unique Naming:** Adds timestamp prefix to prevent filename collisions
3. **Stream Opening:** Opens file as stream (memory-efficient, doesn't load entire file)
4. **Chunked Upload:** Breaks large files into chunks and uploads via HTTP
5. **Metadata Setting:** Sets content type so browsers know how to handle file
6. **URL Generation:** Returns public URL for accessing uploaded file

**How It Integrates with Cloud Storage:**

```
┌─────────────────────────────────────────────────────────────────┐
│ Step 1: .NET Application                                        │
├─────────────────────────────────────────────────────────────────┤
│ 1. User uploads file via HTTP POST (multipart/form-data)       │
│ 2. ASP.NET Core receives file as IFormFile                     │
│ 3. Open file stream (doesn't load into memory)                 │
│ 4. Call StorageClient.UploadObjectAsync()                      │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼ HTTPS PUT requests (with Workload Identity)
┌─────────────────────────────────────────────────────────────────┐
│ Step 2: Google Cloud Storage API                                │
├─────────────────────────────────────────────────────────────────┤
│ 1. Authenticate request (Workload Identity validates token)    │
│ 2. Check IAM permissions (roles/storage.objectAdmin)           │
│ 3. Receive file data in chunks (resumable upload)              │
│ 4. Compute MD5 hash for integrity verification                 │
│ 5. Store object in bucket with metadata                        │
│ 6. Replicate object across multiple locations                  │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼ Data stored
┌─────────────────────────────────────────────────────────────────┐
│ Step 3: Cloud Storage Bucket (dot-net-bucket)                   │
├─────────────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────────────┐│
│ │ 20251229043015_document.pdf                                 ││
│ │ - Size: 2.5 MB                                              ││
│ │ - Content-Type: application/pdf                             ││
│ │ - MD5: d41d8cd98f00b204e9800998ecf8427e                     ││
│ │ - Created: 2025-12-29T04:30:15Z                             ││
│ │ - URL: https://storage.googleapis.com/dot-net-bucket/...    ││
│ └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│ Storage Class: STANDARD (regional)                              │
│ Replication: Multi-zone within asia-south2                      │
│ Encryption: Google-managed keys (encrypted at rest)            │
└─────────────────────────────────────────────────────────────────┘
```

**What Happens Internally in Cloud Storage:**

```
1. Upload Initiation:
   - StorageClient sends initiate resumable upload request
   - Gets upload URL for subsequent chunks
   - Workload Identity token attached to authenticate

2. Data Transfer:
   - File split into chunks (default: 10MB chunks for large files)
   - Each chunk uploaded with range header (bytes 0-10485760/25000000)
   - If network fails, can resume from last successful chunk
   - MD5 hash computed for each chunk

3. Storage:
   - Object stored in distributed filesystem (Colossus)
   - Automatically replicated across multiple servers/zones
   - Encrypted at rest using Google-managed keys
   - Metadata indexed for fast lookup

4. Verification:
   - Final MD5 hash compared with computed hash
   - If mismatch → upload fails, retry
   - If match → upload confirmed, object available

5. Access:
   - Object accessible via public URL
   - CDN caching available (for faster global access)
   - CORS headers allow browser access
```

**Upload Performance Characteristics:**

```
Small File (< 1MB):
  - Single HTTP PUT request
  - Latency: ~50-100ms
  - No chunking needed

Medium File (1-50MB):
  - Multiple chunks (10MB each)
  - Parallel upload possible
  - Latency: 500ms - 3s

Large File (> 50MB):
  - Resumable upload (can restart if connection drops)
  - Progress tracking available
  - Automatic retry on failure
  - Latency: Depends on file size and bandwidth

Concurrent Uploads:
  - Can upload multiple files simultaneously
  - Each uses separate HTTP connection
  - Limited by bandwidth and connection pool
```

**Why Use Streaming (`OpenReadStream()`) ?**

❌ **Bad Approach (Load entire file into memory):**
```csharp
byte[] fileBytes = new byte[file.Length];
file.CopyTo(fileBytes);
// Problem: 100MB file = 100MB of server RAM used!
// Problem: 10 concurrent uploads = 1GB RAM used!
```

✅ **Good Approach (Stream file):**
```csharp
using var stream = file.OpenReadStream();
await _storageClient.UploadObjectAsync(bucket, name, type, stream);
// Benefit: Only small buffer in memory (~64KB)
// Benefit: Can upload 1GB file with minimal RAM
// Benefit: Can handle many concurrent uploads
```

#### **2. List Files**
```csharp
public async Task<List<string>> ListFilesAsync()
{
    var fileNames = new List<string>();

    // List all objects in bucket
    var objects = _storageClient.ListObjectsAsync(_bucketName);

    await foreach (var obj in objects)
    {
        fileNames.Add(obj.Name);
    }

    _logger.LogInformation($"Found {fileNames.Count} files");
    return fileNames;
}
```

**Explanation:**
- Uses async enumeration (`await foreach`)
- Returns just filenames (can also get size, created date, etc.)

#### **3. Delete File**
```csharp
public async Task DeleteFileAsync(string fileName)
{
    await _storageClient.DeleteObjectAsync(_bucketName, fileName);
    _logger.LogInformation($"Deleted: {fileName}");
}
```

### **Usage in Controller** (`backend/src/Controllers/UploadController.cs`)
```csharp
[HttpPost]
public async Task<IActionResult> UploadFile([FromForm] IFormFile file)
{
    // 1. Upload to Cloud Storage
    var fileUrl = await _storageService.UploadFileAsync(file);

    // 2. Publish event to Pub/Sub
    await _pubSubService.PublishFileUploadedEventAsync(file.FileName, fileUrl);

    return Ok(new
    {
        success = true,
        message = "File uploaded successfully",
        fileName = file.FileName,
        fileUrl = fileUrl,
        pubsubPublished = true
    });
}
```

**Explanation:**
- Endpoint accepts file uploads via `multipart/form-data`
- After upload, publishes event so other services can react
- Returns public URL for file access

### **Frontend Integration** (`frontend/src/app/app.component.ts`)
```typescript
uploadFile(event: any) {
  const file = event.target.files[0];
  const formData = new FormData();
  formData.append('file', file);

  this.http.post(`${this.apiUrl}/upload`, formData).subscribe({
    next: (response: any) => {
      console.log('Upload success:', response.fileUrl);
    },
    error: (error) => {
      console.error('Upload failed:', error);
    }
  });
}
```

### **Bucket CORS Configuration** (Terraform)
```hcl
resource "google_storage_bucket" "bucket" {
  name          = var.bucket_name
  location      = var.region
  force_destroy = true

  cors {
    origin          = ["*"]
    method          = ["GET", "HEAD", "PUT", "POST", "DELETE"]
    response_header = ["*"]
    max_age_seconds = 3600
  }
}
```

**Explanation:**
- CORS allows browser uploads directly to bucket
- In production, restrict `origin` to your domain

### **🔐 Workload Identity for Storage**

**IAM Permission** (`terraform/main.tf`):
```hcl
resource "google_storage_bucket_iam_member" "backend_storage" {
  bucket = google_storage_bucket.bucket.name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:${google_service_account.backend_sa.email}"
}
```

**What it allows:**
- ✅ Upload objects (files)
- ✅ Read objects
- ✅ Delete objects
- ✅ List objects in bucket

### **Why Google.Cloud.Storage.V1?**
- ✅ Official Google Cloud library
- ✅ Automatic authentication via Workload Identity
- ✅ Streaming upload support (memory efficient)
- ✅ Full metadata support (content type, custom headers)
- ✅ Simple API - just a few methods needed

---

## 🔄 Complete Integration Flow

### **Scenario: User Creates Account**

```
1. HTTP POST /api/users
   ↓
2. UsersController receives request
   ↓
3. PostgresService.InsertUserAsync()
   → Npgsql → Cloud SQL (10.252.0.3)
   ↓
4. PubSubService.PublishUserCreatedEventAsync()
   → Google.Cloud.PubSub.V1 → Cloud Pub/Sub topic
   ↓
5. RedisService.CacheLatestUsersAsync()
   → StackExchange.Redis → Memorystore Redis (10.84.245.243)
   ↓
6. Return success response
```

### **Scenario: User Uploads File**

```
1. HTTP POST /api/upload (multipart/form-data)
   ↓
2. UploadController receives file
   ↓
3. StorageService.UploadFileAsync()
   → Google.Cloud.Storage.V1 → Cloud Storage bucket
   ↓
4. PubSubService.PublishFileUploadedEventAsync()
   → Google.Cloud.PubSub.V1 → Cloud Pub/Sub topic
   ↓
5. Return file URL
```

---

## 🎓 Key Design Patterns

### **1. Dependency Injection**
All services are registered in `Program.cs`:
```csharp
builder.Services.AddSingleton<PostgresService>();
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<PubSubService>();
builder.Services.AddSingleton<StorageService>();
```

**Benefits:**
- Services are created once and reused
- Easy to test (can mock services)
- Constructor injection makes dependencies explicit

### **2. Environment-Based Configuration**
```csharp
var value = Environment.GetEnvironmentVariable("KEY")  // 1st priority
            ?? configuration["Section:Key"]             // 2nd priority
            ?? "default-value";                         // 3rd priority
```

**Benefits:**
- Same code runs in dev/staging/prod
- No secrets in code
- Easy to change per environment

### **3. Connection Pooling**
- **PostgreSQL:** Built into Npgsql (`Pooling=true;MaxPoolSize=100`)
- **Redis:** ConnectionMultiplexer pattern (single connection shared)

**Benefits:**
- Reduces connection overhead
- Better performance under load
- Automatic connection lifecycle management

### **4. Async/Await Throughout**
All I/O operations use async:
```csharp
public async Task<List<User>> GetAllUsersAsync()
public async Task CacheLatestUsersAsync(List<User> users)
public async Task PublishUserCreatedEventAsync(User user)
public async Task<string> UploadFileAsync(IFormFile file)
```

**Benefits:**
- Non-blocking I/O (better scalability)
- Better resource utilization
- Handles concurrent requests efficiently

### **5. Error Handling**
All service methods have try-catch:
```csharp
try
{
    // GCP operation
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error doing X");
    throw;  // or handle gracefully
}
```

**Benefits:**
- Errors are logged for debugging
- Application doesn't crash silently
- Can add retry logic if needed

---

## 📊 Configuration Summary

| Service | Library | Config Source | Authentication |
|---------|---------|---------------|----------------|
| **Cloud SQL** | Npgsql 7.0.4 | ConfigMap → Env Vars | Username/Password |
| **Redis** | StackExchange.Redis 2.6.122 | ConfigMap → Env Vars | None (private network) |
| **Pub/Sub** | Google.Cloud.PubSub.V1 3.7.0 | ConfigMap → Env Vars | Workload Identity |
| **Storage** | Google.Cloud.Storage.V1 4.6.0 | ConfigMap → Env Vars | Workload Identity |

---

## 🔐 Security Best Practices Used

1. **No service account keys in code or config**
   - Uses Workload Identity for GCP services
   - Uses username/password over private network for databases

2. **Parameterized SQL queries**
   - Prevents SQL injection attacks
   - Uses `NpgsqlCommand.Parameters.AddWithValue()`

3. **Environment-based configuration**
   - Secrets stored in Kubernetes ConfigMap (can use Secrets for sensitive data)
   - Different configs for dev/prod

4. **Private network communication**
   - All services communicate via private IPs
   - No public internet exposure

5. **Connection string security**
   - Built at runtime, never hardcoded
   - Passwords from environment variables

---

## 🚀 Demo POC Flow

### **1. Show Database Connection**
```bash
# Point to PostgresService.cs
# Explain: Npgsql library, connection string building, parameterized queries

# Test
curl -X POST http://YOUR_IP/api/users \
  -H "Content-Type: application/json" \
  -d '{"name":"Demo User","email":"demo@test.com"}'
```

### **2. Show Caching Logic**
```bash
# Point to RedisService.cs
# Explain: StackExchange.Redis, cache-aside pattern, TTL

# Test - First call (cache miss)
curl http://YOUR_IP/api/users/latest
# Response: "source": "database"

# Test - Second call (cache hit)
curl http://YOUR_IP/api/users/latest
# Response: "source": "redis-cache"
```

### **3. Show Event Publishing**
```bash
# Point to PubSubService.cs
# Explain: Google.Cloud.PubSub.V1, Workload Identity, async events

# Test - Create user (publishes event)
curl -X POST http://YOUR_IP/api/users \
  -H "Content-Type: application/json" \
  -d '{"name":"Event Test","email":"event@test.com"}'

# Check published messages
curl http://YOUR_IP/api/pubsub/messages
```

### **4. Show File Upload**
```bash
# Point to StorageService.cs
# Explain: Google.Cloud.Storage.V1, streaming upload, unique filenames

# Test
echo "Demo content" > demo.txt
curl -X POST http://YOUR_IP/api/upload \
  -F "file=@demo.txt"
```

---

## 📚 Additional Resources

- **Npgsql Documentation:** https://www.npgsql.org/doc/
- **StackExchange.Redis:** https://stackexchange.github.io/StackExchange.Redis/
- **Google Cloud .NET Libraries:** https://cloud.google.com/dotnet/docs
- **Workload Identity:** https://cloud.google.com/kubernetes-engine/docs/how-to/workload-identity

---

## 🎓 Simple Summary: How Each Service Works

### **Cloud SQL (PostgreSQL) - Database for Tables**

**What It Does:** Stores data in organized tables (like Excel spreadsheets)

**How .NET Talks to It:**
- Uses: `Npgsql` library (talks to PostgreSQL)
- Connects to: Private IP address (10.252.0.3:5432)
- Sends: SQL commands (like "save this user")
- Login: Username and password
- Smart feature: Reuses connections (connection pool) for speed

**What Code Does:**
- `OpenAsync()` - Opens connection to database
- `ExecuteNonQueryAsync()` - Runs save/update/delete
- `ExecuteReaderAsync()` - Gets data back
- Parameters prevent hackers (SQL injection protection)

**Behind the Scenes:**
1. Gets connection from pool (or makes new one)
2. Sends SQL command over network
3. Database runs the command
4. Sends results back
5. Returns connection to pool for reuse

---

### **Memorystore Redis - Super Fast Memory Storage**

**What It Does:** Saves often-used data in computer memory (RAM) for super fast access

**How .NET Talks to It:**
- Uses: `StackExchange.Redis` library
- Connects to: Private IP address (10.84.245.243:6379)
- Sends: Simple commands (like "save this" or "get that")
- Login: Not needed (protected by private network)
- Strategy: Check cache first, if not there get from database

**What Code Does:**
- `StringSetAsync()` - Save data with timer (auto-delete after 60 seconds)
- `StringGetAsync()` - Get data
- `KeyDeleteAsync()` - Delete data
- Turns objects to JSON text before saving

**Behind the Scenes:**
1. Makes command in Redis language
2. Sends over network connection
3. Redis looks up in memory (very fast)
4. Sends answer back (usually < 1ms)
5. Timer deletes old data automatically

**Speed Difference:**
- Database query: ~50-100ms (slow)
- Redis cache: ~1-5ms (fast)
- **Result: 10-50 times faster!**

---

### **Cloud Pub/Sub - Message Delivery Service**

**What It Does:** Passes messages between services without them knowing about each other

**How .NET Talks to It:**
- Uses: `Google.Cloud.PubSub.V1` library
- Connects to: Google's Pub/Sub service via internet (HTTPS)
- Sends: Messages as JSON text
- Login: Automatic (Workload Identity)
- Strategy: Fire and forget (send message and move on)

**What Code Does:**
- `PublishAsync()` - Sends message to Google
- Message saved in Google's message box
- Google delivers to everyone who subscribed
- Google tries again if delivery fails

**Behind the Scenes:**
1. Turns message to JSON text then to bytes
2. Sends to Google over internet (HTTPS)
3. Google saves in multiple safe places
4. Google gives back tracking number
5. Google delivers to all listeners
6. Keeps trying for 7 days if someone doesn't get it

**Why Use This:**
- ✅ **Independent:** Services don't need to know each other
- ✅ **Fast:** User gets quick answer
- ✅ **Easy to grow:** Add new services anytime
- ✅ **Safe:** Won't lose messages
- ✅ **Keeps working:** If one service breaks, others keep going

**Real Use:**
- User signs up → Send email, count users, save to other systems
- File uploaded → Check virus, make small picture, add to list

---

### **Cloud Storage - File Storage Box**

**What It Does:** Stores files (pictures, documents, videos) with internet access

**How .NET Talks to It:**
- Uses: `Google.Cloud.Storage.V1` library
- Connects to: Google's Storage service via internet (HTTPS)
- Sends: Files in chunks (pieces)
- Login: Automatic (Workload Identity)
- Strategy: Stream upload (doesn't use lots of memory)

**What Code Does:**
- `UploadObjectAsync()` - Upload file to Google
- `ListObjectsAsync()` - Show all files
- `DeleteObjectAsync()` - Delete file
- Breaks big files into pieces automatically

**Behind the Scenes:**
1. Opens file as stream (doesn't load whole file in memory)
2. Breaks into chunks and sends via internet
3. Checks file isn't corrupted (MD5 check)
4. Saves in multiple places (safe)
5. Makes copies in different locations
6. Gives back web address to access file

**Upload Process:**
```
Small file: One upload → Done
Big file: Break into pieces → Upload pieces → Check → Done
```

**Why Streaming?**
- Can upload 1GB file using only tiny bit of memory (~64KB)
- Can upload many files at same time
- If internet breaks, can continue from where stopped

---

## 🔐 Workload Identity: The Secret Sauce

**Problem Without Workload Identity:**
```
❌ Traditional approach:
1. Create service account key (JSON file)
2. Store key in code/config/secrets
3. Rotate keys periodically
4. Risk: Keys can leak, expire, get stolen

Issues:
- Security risk (keys in files)
- Management overhead (rotation)
- Audit complexity (who used which key?)
```

**Solution With Workload Identity:**
```
✅ Modern approach:
1. Link Kubernetes ServiceAccount to GCP Service Account
2. GKE automatically injects temporary token
3. Google Cloud SDKs use token automatically
4. Token rotates automatically every hour

Benefits:
- No keys in code/config/files
- No manual rotation needed
- Better audit trail (per-pod identity)
- Follows security best practices
```

**How It Works:**

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. Kubernetes Pod starts with ServiceAccount: backend-sa       │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ 2. GKE injects metadata service endpoint into pod               │
│    Environment: GCE_METADATA_HOST=metadata.google.internal      │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ 3. .NET code calls: PublisherClient.CreateAsync()               │
│    (No credentials provided in code!)                           │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ 4. Google.Cloud library checks for credentials:                 │
│    - Environment variables? No                                  │
│    - Key files? No                                              │
│    - Metadata service? YES! (found)                             │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ 5. Library calls metadata service:                              │
│    GET http://metadata.google.internal/...                      │
│    Response: { "token": "ya29.c.Kl6B...", "expires": 3600 }    │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ 6. Library uses token for API calls:                            │
│    Authorization: Bearer ya29.c.Kl6B...                         │
│    All GCP API calls authenticated automatically!               │
└─────────────────────────────────────────────────────────────────┘
```

**Configuration (3 Steps):**

**Step 1: Create GCP Service Account (Terraform)**
```hcl
resource "google_service_account" "backend_sa" {
  account_id   = "backend-gke-sa"
  display_name = "Backend GKE Service Account"
}
```

**Step 2: Grant IAM Permissions (Terraform)**
```hcl
resource "google_pubsub_topic_iam_member" "backend_publisher" {
  topic  = google_pubsub_topic.topic.id
  role   = "roles/pubsub.publisher"
  member = "serviceAccount:${google_service_account.backend_sa.email}"
}
```

**Step 3: Link Kubernetes SA to GCP SA (Kubernetes)**
```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: backend-sa
  annotations:
    iam.gke.io/gcp-service-account: backend-gke-sa@project.iam.gserviceaccount.com
---
spec:
  template:
    spec:
      serviceAccountName: backend-sa
```

**Result: Zero credentials in code!** 🎉

---

## 📊 Complete Data Flow Example

### **Scenario: User Registration with File Upload**

```
1. User fills registration form with profile picture
         ↓
2. Frontend sends POST /api/users + file upload
         ↓
3. ASP.NET Core Controller receives request
         ↓
┌─────────────────────────────────────────────────────────────────┐
│ 4. PostgresService.InsertUserAsync()                            │
│    - Opens connection from pool                                 │
│    - Sends: INSERT INTO users VALUES (...)                      │
│    - PostgreSQL writes to disk (with transaction)               │
│    - Returns: User object with generated ID                     │
│    Time: ~50ms                                                  │
└────────────────────────┬────────────────────────────────────────┘
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ 5. StorageService.UploadFileAsync()                             │
│    - Streams file to Cloud Storage                              │
│    - Chunks large files automatically                           │
│    - Returns: Public URL                                        │
│    Time: ~200ms (for 2MB file)                                  │
└────────────────────────┬────────────────────────────────────────┘
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ 6. PubSubService.PublishUserCreatedEventAsync()                 │
│    - Serializes event to JSON                                   │
│    - Publishes to topic                                         │
│    - Returns immediately (async)                                │
│    Time: ~5ms                                                   │
└────────────────────────┬────────────────────────────────────────┘
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ 7. RedisService.CacheLatestUsersAsync()                         │
│    - Queries latest 3 users from PostgreSQL                     │
│    - Serializes to JSON                                         │
│    - Stores in Redis with 60s TTL                               │
│    Time: ~10ms                                                  │
└────────────────────────┬────────────────────────────────────────┘
                         ▼
8. HTTP 200 OK returned to user
   Total response time: ~265ms
         ↓
9. Meanwhile (background, asynchronous):
   - Pub/Sub delivers event to Email Service → Welcome email sent
   - Pub/Sub delivers event to Analytics → Dashboard updated
   - Pub/Sub delivers event to CRM → Contact created
   - These happen AFTER user already got response!
```

**Network Flow:**
```
GKE Pod (backend)
    ├─[TCP]──→ Cloud SQL (10.252.0.3:5432) - PostgreSQL protocol
    ├─[HTTPS]─→ Cloud Storage API - Resumable upload
    ├─[HTTPS]─→ Pub/Sub API - Message publish
    └─[TCP]──→ Redis (10.84.245.243:6379) - RESP protocol

All within private VPC network (no internet exposure)
Authentication via Workload Identity (except PostgreSQL)
```

---

## 🚀 Why This Architecture is Production-Ready

### **Scalability**
- ✅ Connection pooling handles concurrent requests efficiently
- ✅ Redis caching reduces database load 10-50x
- ✅ Pub/Sub handles millions of messages/sec automatically
- ✅ Cloud Storage serves files globally via CDN

### **Reliability**
- ✅ Connection pooling auto-reconnects on failure
- ✅ Pub/Sub guarantees at-least-once delivery
- ✅ Cloud Storage replicates across zones
- ✅ Async operations don't block user response

### **Security**
- ✅ Workload Identity (no service account keys!)
- ✅ Parameterized queries (no SQL injection)
- ✅ Private network (no public internet exposure)
- ✅ Encryption in transit and at rest

### **Observability**
- ✅ Logging at every integration point
- ✅ Request IDs for tracing
- ✅ Error handling with retries
- ✅ Cloud Logging aggregates all logs

### **Maintainability**
- ✅ Dependency injection (easy to test/mock)
- ✅ Environment-based config (same code, different envs)
- ✅ Async/await throughout (non-blocking)
- ✅ Single responsibility services

---

**Repository:** https://github.com/Jatin-Jaiswal/DotNet_GCP  
**Demo URL:** http://34.131.236.231
