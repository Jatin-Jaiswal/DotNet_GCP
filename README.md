# DotNet GCP Full-Stack Application

A production-ready full-stack application demonstrating best practices for deploying .NET 6 backend and Angular 20 frontend on Google Cloud Platform (GCP).

## 📚 Documentation

This repository includes comprehensive documentation to help you understand, deploy, and maintain the application:

- **[ARCHITECTURE.md](ARCHITECTURE.md)** - Detailed architecture, design decisions, and technical specifications
- **[DEPLOYMENT-GUIDE.md](DEPLOYMENT-GUIDE.md)** - Step-by-step deployment instructions from scratch
- **[QUICK-REFERENCE.md](QUICK-REFERENCE.md)** - Quick commands and troubleshooting guide
- **[CONFIGURATION.md](CONFIGURATION.md)** - Configuration details for all components
- **[NETWORK-CONNECTIVITY.md](NETWORK-CONNECTIVITY.md)** - Network architecture and connectivity
- **[CHANGES.md](CHANGES.md)** - Change log and version history
- **[terraform/README.md](terraform/README.md)** - Infrastructure as Code documentation

## 🚀 Quick Start

**For first-time setup:**
1. Read [DEPLOYMENT-GUIDE.md](DEPLOYMENT-GUIDE.md) for complete instructions
2. Navigate to `terraform/` directory
3. Follow Terraform setup in [terraform/README.md](terraform/README.md)

**For quick reference:**
- See [QUICK-REFERENCE.md](QUICK-REFERENCE.md) for common commands and troubleshooting

## 🏗️ Architecture Overview

```
User → Bastion VM → Frontend ILB → Backend ILB → GCP Services
                         ↓               ↓
                    Angular App     .NET 6 API
                                        ↓
                    ┌───────────────────┴────────────────────┐
                    ↓           ↓           ↓                ↓
              Cloud SQL     Redis     Pub/Sub      Cloud Storage
           (PostgreSQL)  (Memorystore)              (Bucket)
```

**📖 For detailed architecture and design decisions, see [ARCHITECTURE.md](ARCHITECTURE.md)**

## 📁 Project Structure

```
root/
│
├── frontend/                  # Angular 20 application
│   ├── src/                   # Source code
│   │   ├── app/              # Main app component
│   │   ├── index.html        # Entry HTML
│   │   ├── main.ts           # Bootstrap file
│   │   └── styles.scss       # Global styles
│   ├── Dockerfile            # Multi-stage build
│   ├── nginx.conf            # Nginx configuration
│   ├── package.json          # Dependencies
│   ├── angular.json          # Angular CLI config
│   └── README.md             # Frontend documentation
│
├── backend/                   # .NET 6 Web API
│   ├── src/                   # Source code
│   │   ├── CloudSql/         # PostgreSQL service
│   │   ├── Redis/            # Redis caching service
│   │   ├── PubSub/           # Pub/Sub messaging service
│   │   ├── Storage/          # Cloud Storage service
│   │   ├── Models/           # Data models
│   │   ├── Controllers/      # API controllers
│   │   ├── Program.cs        # Application entry point
│   │   ├── appsettings.json  # Configuration
│   │   └── DotNetGcpApp.csproj  # Project file
│   ├── Dockerfile            # Multi-stage build
│   └── README.md             # Backend documentation
│
├── k8s/                       # Kubernetes manifests
│   ├── backend.yaml          # Backend resources
│   ├── frontend.yaml         # Frontend resources
│   └── README.md             # Deployment guide
│
└── README.md                  # This file
```

## 🌐 Application Features

### Frontend (Angular 20)
- **Single-page UI** with Material Design
- **Real-time updates** (auto-refresh every 5 seconds)
- **User management** (create and view users)
- **File upload** to Cloud Storage
- **Event monitoring** (Pub/Sub messages)
- **Responsive design** (mobile and desktop)

### Backend (.NET 6)
- **RESTful API** with Swagger documentation
- **Cloud SQL integration** (PostgreSQL 15)
- **Redis caching** (60-second TTL for latest users)
- **Pub/Sub messaging** (event-driven architecture)
- **Cloud Storage** (file uploads)
- **Workload Identity** (secure GCP authentication)

## 🔧 GCP Services Used

| Service | Instance Name | Purpose | Connection |
|---------|---------------|---------|------------|
| **Cloud SQL** | `dot-net-db` | PostgreSQL database | Private IP: `10.92.160.3` |
| **Memorystore Redis** | `dot-net-redis` | Caching layer | Private IP: `10.127.80.5` |
| **Pub/Sub** | `dot-net-topic` | Event messaging | Workload Identity |
| **Cloud Storage** | `dot-net-bucket` | File storage | Workload Identity |
| **GKE** | `private-gke-cluster` | Container orchestration | Regional cluster |
| **Artifact Registry** | `dot-net-repo` | Docker image storage | asia-south2 |
| **VPC** | `dot-net-vpc` | Private network | Custom mode |

## 🚀 Quick Start

### Prerequisites
- GCP account with billing enabled
- gcloud CLI installed and configured
- kubectl installed
- Docker installed
- Node.js 20+ (for local frontend development)
- .NET 6 SDK (for local backend development)

### 0. Configuration Setup

**⚠️ IMPORTANT: Before running the application, set up environment configuration!**

Read the complete configuration guide: **[CONFIGURATION.md](./CONFIGURATION.md)**

#### Backend Configuration
```bash
# Copy the example .env file
cp backend/.env.example backend/.env

# Edit .env with your actual GCP service credentials
# The file already contains the correct values for this project
```

#### Frontend Configuration
```bash
# For local development, edit:
frontend/src/environments/environment.ts

# For production deployment, edit:
frontend/src/environments/environment.prod.ts
```

See [CONFIGURATION.md](./CONFIGURATION.md) for detailed setup instructions.

### 1. Set Up GCP Infrastructure

All GCP services are already created:
- ✅ VPC network (`dot-net-vpc`) with subnets
- ✅ Cloud SQL PostgreSQL instance
- ✅ Memorystore Redis instance
- ✅ Pub/Sub topic and subscription
- ✅ Cloud Storage bucket
- ✅ GKE private cluster
- ✅ Artifact Registry repository

### 2. Build and Deploy

#### Step 1: Configure Docker for Artifact Registry
```bash
gcloud auth configure-docker asia-south2-docker.pkg.dev
```

#### Step 2: Build and Push Images
```bash
# Backend
cd backend
docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:latest .
docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:latest

# Frontend
cd ../frontend
docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/frontend:latest .
docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/frontend:latest
```

#### Step 3: Set Up Workload Identity
```bash
# Create GCP service account
gcloud iam service-accounts create backend-gke-sa \
  --display-name="Backend GKE Service Account"

# Grant permissions
gcloud projects add-iam-policy-binding project-84d8bfc9-cd8e-4b3c-b15 \
  --member="serviceAccount:backend-gke-sa@project-84d8bfc9-cd8e-4b3c-b15.iam.gserviceaccount.com" \
  --role="roles/cloudsql.client"

gcloud projects add-iam-policy-binding project-84d8bfc9-cd8e-4b3c-b15 \
  --member="serviceAccount:backend-gke-sa@project-84d8bfc9-cd8e-4b3c-b15.iam.gserviceaccount.com" \
  --role="roles/pubsub.publisher"

gcloud projects add-iam-policy-binding project-84d8bfc9-cd8e-4b3c-b15 \
  --member="serviceAccount:backend-gke-sa@project-84d8bfc9-cd8e-4b3c-b15.iam.gserviceaccount.com" \
  --role="roles/storage.objectCreator"

# Bind to Kubernetes SA
gcloud iam service-accounts add-iam-policy-binding \
  backend-gke-sa@project-84d8bfc9-cd8e-4b3c-b15.iam.gserviceaccount.com \
  --role roles/iam.workloadIdentityUser \
  --member "serviceAccount:project-84d8bfc9-cd8e-4b3c-b15.svc.id.goog[default/backend-sa]"
```

#### Step 4: Connect to GKE
```bash
gcloud container clusters get-credentials private-gke-cluster --region asia-south2
```

#### Step 5: Deploy to Kubernetes
```bash
kubectl apply -f k8s/backend.yaml
kubectl apply -f k8s/frontend.yaml
```

#### Step 6: Verify Deployment
```bash
# Check pods
kubectl get pods

# Check services and get ILB IPs
kubectl get services

# View logs
kubectl logs -f deployment/backend-deployment
kubectl logs -f deployment/frontend-deployment
```

## 📊 API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/users` | Get all users from SQL |
| GET | `/api/users/latest` | Get last 3 users (Redis cache) |
| POST | `/api/users` | Create new user |
| POST | `/api/upload` | Upload file to Cloud Storage |
| GET | `/api/upload/files` | List files in bucket |
| GET | `/api/pubsub/events` | Get recent Pub/Sub events |
| GET | `/health` | Health check |

## 🔐 Security Features

- **Private GKE cluster** with private nodes and endpoint
- **Internal Load Balancers** (no public IPs)
- **Workload Identity** (no service account keys)
- **VPC network isolation**
- **Bastion VM** for external access control
- **Private IP** connections to Cloud SQL and Redis

## 📈 Monitoring

```bash
# Get service IPs
kubectl get services

# Check pod health
kubectl get pods -o wide

# View real-time logs
kubectl logs -f deployment/backend-deployment
kubectl logs -f deployment/frontend-deployment

# Check resource usage
kubectl top pods
kubectl top nodes
```

## 🔄 Updates and Rollbacks

### Update Application
```bash
# Build new version
docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:v2 .
docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:v2

# Update deployment
kubectl set image deployment/backend-deployment backend=asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:v2
```

### Rollback
```bash
kubectl rollout undo deployment/backend-deployment
```

## 🧹 Cleanup

**⚠️ Using Terraform? See [terraform/README.md](terraform/README.md) for cleanup instructions.**

To delete all resources:

```bash
# Option 1: Using Terraform (Recommended)
cd terraform/
terraform destroy

# Option 2: Manual cleanup
# Delete Kubernetes resources
kubectl delete -f k8s/backend.yaml
kubectl delete -f k8s/frontend.yaml

# Delete GKE cluster
gcloud container clusters delete private-gke-cluster --region asia-south2

# Delete other GCP resources
gcloud sql instances delete dot-net-db
gcloud redis instances delete dot-net-redis --region asia-south2
gcloud pubsub topics delete dot-net-topic
gcloud storage rm -r gs://dot-net-bucket
```

## 📖 Additional Resources

### Documentation
- **[ARCHITECTURE.md](ARCHITECTURE.md)** - Complete architecture documentation
  - Design decisions and rationale
  - Security architecture
  - Scalability and performance
  - Cost optimization strategies
  - Future enhancements

- **[DEPLOYMENT-GUIDE.md](DEPLOYMENT-GUIDE.md)** - Step-by-step deployment
  - Phase 1: Infrastructure Setup (Terraform)
  - Phase 2: Docker Images
  - Phase 3: Kubernetes Deployment
  - Phase 4: Verification
  - Phase 5: Monitoring

- **[QUICK-REFERENCE.md](QUICK-REFERENCE.md)** - Quick command reference
  - Common commands
  - Troubleshooting quick fixes
  - Configuration reference
  - Update workflow

- **[terraform/README.md](terraform/README.md)** - Infrastructure as Code
  - Terraform setup and usage
  - Variable configuration
  - Output values
  - Best practices

### Component READMEs
- [backend/README.md](backend/README.md) - Backend API documentation
- [frontend/README.md](frontend/README.md) - Frontend application docs
- [k8s/README.md](k8s/README.md) - Kubernetes deployment guide

### Network and Configuration
- [NETWORK-CONNECTIVITY.md](NETWORK-CONNECTIVITY.md) - Network architecture
- [CONFIGURATION.md](CONFIGURATION.md) - Configuration details
- [CHANGES.md](CHANGES.md) - Change log

## 🤝 Contributing

This is a reference implementation showcasing GCP best practices. Feel free to:
- Report issues
- Suggest improvements
- Fork for your own projects

## 📝 License

This project is for educational and reference purposes.

---

**Project Status**: Production-ready ✅  
**Last Updated**: 2024  
**GCP Region**: asia-south2 (Delhi)

## 📚 Documentation

- **[Backend README](./backend/README.md)** - .NET 6 API documentation
- **[Frontend README](./frontend/README.md)** - Angular 20 app documentation
- **[Deployment README](./k8s/README.md)** - Kubernetes deployment guide

## 🤝 Contributing

This is a demonstration project showcasing GCP best practices. Feel free to use it as a template for your own applications.

## 📝 License

MIT License - feel free to use this code for your own projects.

## 🙏 Acknowledgments

Built with:
- .NET 6
- Angular 20
- Google Cloud Platform
- Kubernetes
- Docker
