# .NET to GCP Integration Overview

## Purpose
This guide explains how the .NET 6 backend communicates with Google Cloud services including Secret Manager, Cloud SQL, Memorystore Redis, Cloud Pub/Sub, and Cloud Storage. Each service-specific document is split out for easier reference:
- `Guide/secret-manager.md`
- `Guide/cloud-sql.md`
- `Guide/redis.md`
- `Guide/pubsub.md`
- `Guide/cloud-storage.md`

## NuGet Packages
```xml
<!-- backend/src/DotNetGcpApp.csproj -->
<PackageReference Include="Google.Cloud.SecretManager.V1" Version="2.3.0" />
<PackageReference Include="Npgsql" Version="7.0.4" />
<PackageReference Include="StackExchange.Redis" Version="2.6.122" />
<PackageReference Include="Google.Cloud.PubSub.V1" Version="3.7.0" />
<PackageReference Include="Google.Cloud.Storage.V1" Version="4.6.0" />
```

## Complete Integration Flows

### User Creates Account
```
1. HTTP POST /api/users
2. UsersController receives request
3. PostgresService.InsertUserAsync() → Npgsql → Cloud SQL
4. PubSubService.PublishUserCreatedEventAsync() → Cloud Pub/Sub
5. RedisService.CacheLatestUsersAsync() → Memorystore Redis
6. API returns success response
```

### User Uploads File
```
1. HTTP POST /api/upload (multipart/form-data)
2. UploadController receives file
3. StorageService.UploadFileAsync() → Cloud Storage
4. PubSubService.PublishFileUploadedEventAsync() → Cloud Pub/Sub
5. API returns file URL
```

## Design Patterns and Practices
- Dependency injection registers each service in `Program.cs`
- Environment-first configuration (`Environment` → `IConfiguration` → default)
- Connection pooling for PostgreSQL and Redis
- Async/await used for all I/O operations
- Structured error handling and logging around cloud calls

## Configuration Summary

| Service | Library | Config Source | Authentication |
|---------|---------|---------------|----------------|
| Cloud SQL | Npgsql 7.0.4 | Secret Manager | Username and password |
| Memorystore Redis | StackExchange.Redis 2.6.122 | Secret Manager | Private network (no password) |
| Cloud Pub/Sub | Google.Cloud.PubSub.V1 3.7.0 | Secret Manager | Workload Identity |
| Cloud Storage | Google.Cloud.Storage.V1 4.6.0 | Secret Manager | Workload Identity |

## Security Practices
- No service account keys stored in the repository; Workload Identity handles authentication
- Parameterized SQL queries prevent injection attacks
- All traffic stays on private IPs within the VPC
- Configuration retrieved from Secret Manager to centralize sensitive data
- Connection strings built at runtime without hardcoded secrets

## Demo Walkthrough
1. **Database connection**
   ```bash
   curl -X POST http://YOUR_IP/api/users \
     -H "Content-Type: application/json" \
     -d '{"name":"Demo User","email":"demo@test.com"}'
   ```
2. **Redis cache hit/miss**
   ```bash
   curl http://YOUR_IP/api/users/latest
   curl http://YOUR_IP/api/users/latest
   ```
3. **Pub/Sub event publishing**
   ```bash
   curl -X POST http://YOUR_IP/api/users \
     -H "Content-Type: application/json" \
     -d '{"name":"Event Test","email":"event@test.com"}'
   curl http://YOUR_IP/api/pubsub/messages
   ```
4. **Cloud Storage upload**
   ```bash
   echo "Demo content" > demo.txt
   curl -X POST http://YOUR_IP/api/upload \
     -F "file=@demo.txt"
   ```

## Workload Identity Overview
- Kubernetes service account `backend-sa` is annotated to impersonate a GCP service account
- Google Cloud libraries automatically obtain tokens from the metadata server
- Tokens rotate automatically; no manual key management is required
- IAM roles (`roles/secretmanager.secretAccessor`, `roles/pubsub.publisher`, `roles/storage.objectAdmin`) are granted to the mapped GCP service account

## Additional Resources
- Npgsql documentation: https://www.npgsql.org/doc/
- StackExchange.Redis: https://stackexchange.github.io/StackExchange.Redis/
- Google Cloud .NET libraries: https://cloud.google.com/dotnet/docs
- Workload Identity overview: https://cloud.google.com/kubernetes-engine/docs/how-to/workload-identity
