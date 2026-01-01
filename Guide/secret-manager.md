# Google Secret Manager Integration (Production-Grade)

## Why Secret Manager?
- No hardcoded credentials in code or ConfigMaps
- Centralized secret management across all environments
- Automatic secret rotation without redeployment
- Audit logging for all secret access
- Fine-grained IAM permissions per secret

## Library Used
`Google.Cloud.SecretManager.V1`

```xml
<!-- backend/src/DotNetGcpApp.csproj -->
<PackageReference Include="Google.Cloud.SecretManager.V1" Version="2.3.0" />
```

## Related File
`backend/src/SecretManager/SecretManagerService.cs`

## Integration Flow Overview
```
GKE Metadata Service → Get Project ID → Secret Manager API → Fetch All Secrets → Application
```

### Step 1: Bootstrap Project ID from GKE Metadata Service

```csharp
private string GetProjectIdFromMetadata()
{
    try
    {
        // GKE pods have access to metadata service (no authentication needed)
        var request = WebRequest.Create("http://metadata.google.internal/computeMetadata/v1/project/project-id");
        request.Headers.Add("Metadata-Flavor", "Google");  // Required header
        
        using var response = request.GetResponse();
        using var reader = new StreamReader(response.GetResponseStream());
        var projectId = reader.ReadToEnd();
        
        _logger.LogInformation($"Retrieved project ID from metadata: {projectId}");
        return projectId;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to retrieve project ID from metadata service");
        throw;
    }
}
```

**Details**
- Queries GKE's metadata service (no credentials needed)
- Returns the GCP project ID (`project-84d8bfc9-cd8e-4b3c-b15`)
- Enables constructing secret names without hardcoding

### Step 2: Fetch Secrets from Secret Manager

```csharp
public string GetSecretValue(string secretId)
{
    try
    {
        var secretVersionName = new SecretVersionName(_projectId, secretId, "latest");
        var response = _client.AccessSecretVersion(secretVersionName);
        
        _logger.LogInformation($"Retrieved secret: {secretId}");
        return response.Payload.Data.ToStringUtf8();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"Failed to retrieve secret: {secretId}");
        throw;
    }
}
```

**Details**
- Calls Secret Manager API with the latest secret version
- Uses GKE Workload Identity for authentication
- Returns decrypted secret value and logs access

### Step 3: Use Secrets in Services

**Example: PostgreSQL Service**
```csharp
public PostgresService(SecretManagerService secretManager, ILogger<PostgresService> logger)
{
    _logger = logger;
    
    var host = secretManager.GetSecretValue("cloudsql-host");
    var database = secretManager.GetSecretValue("cloudsql-database");
    var username = secretManager.GetSecretValue("cloudsql-username");
    var password = secretManager.GetSecretValue("cloudsql-password");
    
    _connectionString = $"Host={host};Database={database};Username={username};Password={password};Pooling=true;";
    
    _logger.LogInformation("PostgreSQL connection initialized with all config from Secret Manager");
}
```

**Example: Redis Service**
```csharp
public RedisService(SecretManagerService secretManager, ILogger<RedisService> logger)
{
    _logger = logger;
    
    var host = secretManager.GetSecretValue("redis-host");
    var port = secretManager.GetSecretValue("redis-port");
    
    _connection = ConnectionMultiplexer.Connect($"{host}:{port}");
    _database = _connection.GetDatabase();
    
    _logger.LogInformation($"Connected to Redis from Secret Manager config: {host}:{port}");
}
```

## Secrets Created by Terraform

| Secret ID | Description | Example Value |
|-----------|-------------|---------------|
| `cloudsql-host` | Cloud SQL private IP | `10.23.0.3` |
| `cloudsql-database` | Database name | `dotnetdb` |
| `cloudsql-username` | Database username | `postgres` |
| `cloudsql-password` | Database password | `DotNet@123` |
| `redis-host` | Redis private IP | `10.119.34.243` |
| `redis-port` | Redis port | `6379` |
| `gcp-project-id` | GCP project ID | `project-84d8bfc9-cd8e-4b3c-b15` |
| `pubsub-topic-id` | Pub/Sub topic name | `dot-net-topic` |
| `pubsub-subscription-id` | Pub/Sub subscription | `dot-net-sub` |
| `storage-bucket-name` | Storage bucket name | `dot-net-bucket` |

## Authentication Flow with GKE Workload Identity
```
1. Pod starts with serviceAccount: backend-sa (Kubernetes service account)
2. Kubernetes service account annotated with: iam.gke.io/gcp-service-account: backend-gke-sa@...
3. GKE maps the Kubernetes identity to the GCP service account
4. Secret Manager API call uses the GCP service account identity
5. IAM checks whether backend-gke-sa has roles/secretmanager.secretAccessor
6. On success, returns decrypted secret; otherwise, the call is denied
```

## Terraform Configuration (Automatic Secret Creation)

```hcl
resource "google_secret_manager_secret" "cloudsql_host" {
  secret_id = "cloudsql-host"
  replication { auto {} }
}

resource "google_secret_manager_secret_version" "cloudsql_host_version" {
  secret      = google_secret_manager_secret.cloudsql_host.id
  secret_data = google_sql_database_instance.postgres.private_ip_address
}

resource "google_secret_manager_secret_iam_member" "cloudsql_host_access" {
  secret_id = google_secret_manager_secret.cloudsql_host.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.backend_sa.email}"
}
```

**Result**
- IP changes in Cloud SQL are propagated automatically
- Application code does not require redeployment for configuration updates

## Benefits Over Environment Variables

| Aspect | Environment Variables | Secret Manager |
|--------|----------------------|----------------|
| Security | Visible in `kubectl describe pod` | Never exposed |
| Rotation | Requires pod restart | Application can refetch |
| Audit | No audit trail | Cloud Logging tracks access |
| Centralization | Scattered across ConfigMaps | Single source of truth |
| Versioning | No versioning | Full version history |
| Terraform Integration | Manual updates | Auto-populated |

## Secret Rotation Example

```bash
# 1. Update secret in Secret Manager
gcloud secrets versions add cloudsql-password --data-file=- <<< "NewPassword456"

# 2. Update Cloud SQL password
gcloud sql users set-password postgres --instance=dot-net-postgres --password="NewPassword456"

# 3. Restart pods to pick up new secret
kubectl rollout restart deployment backend-deployment
```

No code changes, ConfigMap updates, or redeployment are required.
