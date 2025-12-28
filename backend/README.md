# Backend .NET 6 Web API

This folder contains the backend API built with .NET 6 that integrates with various Google Cloud Platform services.

## 📁 Folder Structure

```
backend/
├── src/
│   ├── CloudSql/         # PostgreSQL database integration
│   ├── Redis/            # Memorystore Redis caching
│   ├── PubSub/           # Pub/Sub event publishing
│   ├── Storage/          # Cloud Storage file uploads
│   ├── Models/           # Data models (User, PubSubMessage)
│   ├── Controllers/      # API endpoints
│   ├── Program.cs        # Application entry point
│   ├── appsettings.json  # Configuration file
│   └── DotNetGcpApp.csproj  # Project dependencies
└── docker/
    └── Dockerfile        # Multi-stage Docker build
```

## 🌐 API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/users` | Get all users from SQL |
| GET | `/api/users/latest` | Get last 3 users (from Redis cache) |
| POST | `/api/users` | Create a new user (SQL + Pub/Sub + Redis) |
| POST | `/api/upload` | Upload file to Cloud Storage |
| GET | `/api/upload/files` | List all files in bucket |
| GET | `/api/pubsub/events` | Get recent Pub/Sub events |
| GET | `/health` | Health check endpoint |

## 🔧 GCP Services Integration

### Cloud SQL (PostgreSQL)
- **Instance:** `dot-net-db`
- **Private IP:** `10.92.160.3`
- **Database:** `dotnetdb`
- **Features:** Connection pooling, auto table creation

### Memorystore Redis
- **Instance:** `dot-net-redis`
- **Private IP:** `10.127.80.5`
- **TTL:** 60 seconds
- **Caches:** Last 3 users

### Pub/Sub
- **Topic:** `dot-net-topic`
- **Events:** User creation, file uploads
- **Auth:** Workload Identity

### Cloud Storage
- **Bucket:** `dot-net-bucket`
- **Region:** `asia-south2`
- **Auth:** Workload Identity

## 🚀 Running Locally

```bash
cd backend/src
dotnet restore
dotnet run
```

The API will be available at `http://localhost:8080`

## 🐳 Building Docker Image

```bash
cd backend
docker build -t dot-net-backend:latest .
```

## 📝 Environment Variables

All configuration is in `appsettings.json`, but can be overridden with environment variables:

- `CloudSql__Host`: PostgreSQL IP
- `CloudSql__Database`: Database name
- `CloudSql__Username`: DB username
- `CloudSql__Password`: DB password
- `Redis__Host`: Redis IP
- `GCP__ProjectId`: GCP project ID
- `PubSub__TopicId`: Pub/Sub topic name
- `Storage__BucketName`: GCS bucket name

## 🔐 Authentication

The application uses **Workload Identity** when running in GKE, which provides automatic authentication to GCP services without needing service account keys.
