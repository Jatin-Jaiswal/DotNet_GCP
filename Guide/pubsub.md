# Cloud Pub/Sub Integration

## Library Used
`Google.Cloud.PubSub.V1`

```xml
<!-- backend/src/DotNetGcpApp.csproj -->
<PackageReference Include="Google.Cloud.PubSub.V1" Version="3.7.0" />
```

## Related File
`backend/src/PubSub/PubSubService.cs`

## Why Pub/Sub?
- Decouples services and avoids tight coupling between producers and consumers
- Provides fast responses for users while background tasks run asynchronously
- Supports easy onboarding of new consumers without changes to the producer
- Guarantees at-least-once delivery with built-in retries

## Configuration

**ConfigMap** (`k8s/backend.yaml`)
```yaml
data:
  GCP__ProjectId: "project-84d8bfc9-cd8e-4b3c-b15"
  PubSub__TopicId: "dot-net-topic"
  PubSub__SubscriptionId: "dot-net-sub"
```

**Runtime Resolution**
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

    _topicName = $"projects/{projectId}/topics/{topicId}";
    var topicName = TopicName.FromProjectTopic(projectId, topicId);
    _publisher = PublisherClient.CreateAsync(topicName).GetAwaiter().GetResult();
}
```

## Publishing Patterns

### Publish User Created Event
```csharp
public async Task PublishUserCreatedEventAsync(User user)
{
    var pubSubMessage = new PubSubMessage
    {
        Source = "sql",
        Message = $"User created: {user.Name} ({user.Email})",
        Timestamp = DateTime.UtcNow
    };
    
    await PublishMessageAsync(pubSubMessage);
}
```

### Publish File Uploaded Event
```csharp
public async Task PublishFileUploadedEventAsync(string fileName, string fileUrl)
{
    var pubSubMessage = new PubSubMessage
    {
        Source = "bucket",
        Message = $"File uploaded: {fileName} - URL: {fileUrl}",
        Timestamp = DateTime.UtcNow
    };
    
    await PublishMessageAsync(pubSubMessage);
}
```

### Core Publish Logic
```csharp
private async Task PublishMessageAsync(PubSubMessage message)
{
    var json = JsonSerializer.Serialize(message);
    var pubsubMessage = new Google.Cloud.PubSub.V1.PubsubMessage
    {
        Data = Google.Protobuf.ByteString.CopyFromUtf8(json)
    };
    
    var messageId = await _publisher.PublishAsync(pubsubMessage);
    _logger.LogInformation($"Published message {messageId} to {_topicName}");
}
```

**What Happens**
1. Domain object is converted to JSON
2. JSON is wrapped in a `PubsubMessage` with UTF-8 bytes
3. Google Cloud client library sends HTTPS request to Pub/Sub
4. Pub/Sub stores the message and returns a message ID
5. Pub/Sub fans out delivery to all subscribed consumers

## Message Model
```csharp
public class PubSubMessage
{
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
```

## Controller Usage
```csharp
[HttpPost]
public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
{
    var user = await _postgresService.InsertUserAsync(request.Name, request.Email);
    await _pubSubService.PublishUserCreatedEventAsync(user);

    var latestUsers = await _postgresService.GetLatestUsersAsync(3);
    await _redisService.CacheLatestUsersAsync(latestUsers);

    return Ok(new
    {
        success = true,
        message = "User created successfully",
        user = user,
        pubsubPublished = true
    });
}
```

## Authentication and Workload Identity
- Kubernetes service account `backend-sa` is annotated with the mapped GCP service account
- GCP service account is granted `roles/pubsub.publisher`
- `PublisherClient.CreateAsync` automatically picks up credentials from Workload Identity
- No service account keys or JSON files are stored in the repository or ConfigMaps

## Reliability Characteristics
- At-least-once delivery with automatic retries
- Messages retained for up to seven days if not acknowledged
- Optional ordering keys for ordered delivery
- Elastic scaling for high-throughput publishing
