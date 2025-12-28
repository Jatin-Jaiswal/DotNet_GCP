# .NET GCP Full-Stack Application with Terraform

A production-ready full-stack application demonstrating Infrastructure as Code (IaC) deployment of .NET 6 backend and Angular frontend on Google Cloud Platform using Terraform.

## 🌟 Overview

This project showcases a complete enterprise-grade cloud infrastructure deployment using Terraform, featuring:
- **Infrastructure as Code** - Complete GCP infrastructure defined in Terraform
- **Private GKE Cluster** - Secure Kubernetes environment with Workload Identity
- **Microservices Architecture** - Containerized backend and frontend
- **Cloud-Native Services** - Cloud SQL, Redis, Pub/Sub, Cloud Storage
- **Security Best Practices** - Private clusters, service accounts, IAM policies
- **Automated Deployment** - One-command infrastructure and application deployment

## 📚 Documentation

- **[DEPLOYMENT-STEPS.md](DEPLOYMENT-STEPS.md)** - Complete step-by-step deployment guide after Terraform
- **[terraform/README.md](terraform/README.md)** - Terraform infrastructure documentation

## 🚀 Quick Start

### Prerequisites

```bash
# Required tools
- Terraform >= 1.0
- gcloud CLI
- Docker
- kubectl

# GCP Authentication
gcloud auth login
gcloud auth application-default login
```

### 1. Deploy Infrastructure with Terraform

```bash
cd terraform
cp terraform.tfvars.example terraform.tfvars
# Edit terraform.tfvars with your project details
terraform init
terraform plan
terraform apply
```

**✅ This creates 31 resources:**
- VPC with private/public subnets
- Private GKE cluster (2 nodes)
- Cloud SQL PostgreSQL
- Redis (Memorystore)
- Pub/Sub topic and subscription
- Cloud Storage bucket
- Artifact Registry
- Bastion VM
- Service accounts and IAM bindings

### 2. Deploy Application

Follow the detailed steps in **[DEPLOYMENT-STEPS.md](DEPLOYMENT-STEPS.md)** or use the automated script:

```bash
# One-command deployment (after Terraform)
./deploy-app.sh
```

## 🏗️ Architecture

```
Internet
   ↓
Bastion VM (Public IP: 34.131.236.231)
   ↓
Nginx Reverse Proxy
   ├─→ Frontend ILB (10.0.1.7) → Angular Pods
   └─→ Backend ILB (10.0.1.6) → .NET Pods
                                    ↓
                    ┌───────────────┴────────────────────┐
                    ↓               ↓                    ↓
              Cloud SQL (10.117.0.3)  Redis (10.163.53.91)  Pub/Sub + Storage
           (PostgreSQL 15)          (1GB Memory)          (Event-driven)
```

### Key Features

**Security:**
- ✅ Private GKE cluster (no public endpoints)
- ✅ Workload Identity for pod authentication
- ✅ Service accounts with least-privilege IAM
- ✅ Private IP addresses for all services
- ✅ Bastion VM as single entry point

**Scalability:**
- ✅ GKE autoscaling (2-4 nodes)
- ✅ Internal load balancers
- ✅ Horizontal pod autoscaling ready
- ✅ Cloud SQL connection pooling

**Reliability:**
- ✅ Multi-zone GKE nodes
- ✅ Health checks and readiness probes
- ✅ Cloud SQL automated backups
- ✅ Redis persistence

## 📁 Project Structure

```
.
├── backend/                      # .NET 6 Web API
│   ├── src/
│   │   ├── CloudSql/            # PostgreSQL service
│   │   ├── Redis/               # Redis caching
│   │   ├── PubSub/              # Pub/Sub messaging
│   │   ├── Storage/             # Cloud Storage
│   │   ├── Controllers/         # API endpoints
│   │   ├── Models/              # Data models
│   │   └── Program.cs           # Entry point
│   ├── Dockerfile               # Container image
│   └── .env                     # Configuration
│
├── frontend/                    # Angular Frontend
│   ├── src/
│   │   ├── app/                # Components
│   │   └── environments/       # Config files
│   ├── Dockerfile              # Container image
│   └── nginx.conf              # Web server config
│
├── terraform/                   # Infrastructure as Code
│   ├── main.tf                 # Main infrastructure
│   ├── variables.tf            # Input variables
│   ├── outputs.tf              # Output values
│   ├── versions.tf             # Provider versions
│   ├── terraform.tfvars        # Your values
│   └── README.md               # Terraform docs
│
├── k8s/                        # Kubernetes Manifests
│   ├── backend.yaml            # Backend deployment
│   └── frontend.yaml           # Frontend deployment
│
├── bastion-nginx.conf          # Bastion reverse proxy
├── DEPLOYMENT-STEPS.md         # Deployment guide
└── README.md                   # This file
```

## 🛠️ Technology Stack

**Frontend:**
- Angular 20
- TypeScript
- RxJS
- Nginx (Alpine)

**Backend:**
- .NET 6 (ASP.NET Core)
- C#
- Entity Framework Core
- NuGet Packages:
  - `Npgsql.EntityFrameworkCore.PostgreSQL` - PostgreSQL
  - `StackExchange.Redis` - Redis client
  - `Google.Cloud.PubSub.V1` - Pub/Sub
  - `Google.Cloud.Storage.V1` - Cloud Storage

**Infrastructure:**
- **IaC**: Terraform 1.0+
- **Container Orchestration**: Google Kubernetes Engine (GKE)
- **Database**: Cloud SQL PostgreSQL 15
- **Cache**: Memorystore Redis 7.0
- **Messaging**: Cloud Pub/Sub
- **Storage**: Cloud Storage
- **Registry**: Artifact Registry
- **Networking**: VPC, Internal Load Balancers
- **Security**: Service Accounts, Workload Identity, IAM

## 🌐 Application Features

### Backend API (.NET 6)
- **User Management** - CRUD operations with PostgreSQL
- **File Upload** - Store files in Cloud Storage
- **Pub/Sub Events** - Publish/subscribe messaging
- **Redis Caching** - Fast data access
- **Health Checks** - Kubernetes readiness/liveness probes
- **CORS Enabled** - Cross-origin requests
- **Swagger UI** - API documentation at `/swagger`

**API Endpoints:**
```
GET    /health              - Health check
GET    /api/users           - Get all users
POST   /api/users           - Create user
GET    /api/users/latest    - Get latest users
POST   /api/upload          - Upload file
GET    /api/upload/files    - List files
POST   /api/pubsub/publish  - Publish message
GET    /api/pubsub/events   - Get published events
```

### Frontend (Angular)
- **Single-page application** with responsive UI
- **User management** - Create and view users
- **File upload** - Upload to Cloud Storage
- **Event monitoring** - View Pub/Sub messages
- **Auto-refresh** - Real-time data updates (5s interval)
- **Material Design** - Modern UI components

## 💰 Cost Estimation

**Approximate monthly costs (with Terraform infrastructure):**

| Service | Configuration | ~Monthly Cost |
|---------|--------------|---------------|
| GKE Cluster | 2 e2-medium nodes (zonal) | ~$50 |
| Cloud SQL | db-f1-micro, 10GB HDD | ~$15 |
| Redis | BASIC tier, 1GB | ~$30 |
| Pub/Sub | 1GB messages | ~$2 |
| Cloud Storage | 10GB storage | ~$0.30 |
| Network | Internal LB, egress | ~$10 |
| Artifact Registry | 5GB storage | ~$0.50 |
| **Total** | | **~$108/month** |

*Costs can be reduced by:*
- Using Spot VMs for GKE nodes
- Smaller Redis instance
- Stopping resources when not in use

## 🔧 Configuration

### GCP Services Created by Terraform

| Service | Name | Configuration |
|---------|------|---------------|
| **VPC** | `dot-net-vpc` | Private (10.0.1.0/24) + Public (10.0.2.0/24) subnets |
| **GKE** | `private-gke-cluster` | Zonal, 2-4 e2-medium nodes, Workload Identity |
| **Cloud SQL** | `dot-net-postgres` | PostgreSQL 15, db-f1-micro, 10GB HDD |
| **Redis** | `dot-net-redis` | BASIC tier, 1GB memory, REDIS_7_0 |
| **Pub/Sub** | `dot-net-topic` | Topic + subscription |
| **Storage** | `dot-net-bucket` | Regional bucket with CORS |
| **Registry** | `dot-net-repo` | Docker repository |
| **Bastion VM** | `dot-net-bastion-vm` | e2-small with Nginx, Docker, kubectl |

### Environment Variables

**Backend (.env):**
```bash
CloudSql__Host=10.117.0.3          # From Terraform output
CloudSql__Database=dotnetdb
CloudSql__Username=postgres
CloudSql__Password=DotNet@123

Redis__Host=10.163.53.91           # From Terraform output
Redis__Port=6379

GCP__ProjectId=project-84d8bfc9-cd8e-4b3c-b15
PubSub__TopicId=dot-net-topic
PubSub__SubscriptionId=dot-net-sub
Storage__BucketName=dot-net-bucket
```

**Frontend (environment.prod.ts):**
```typescript
export const environment = {
  production: true,
  apiUrl: '/api',  // Proxied through bastion nginx
};
```

## 📦 Deployment

### Option 1: Automated Deployment

After running `terraform apply`, use the automated script:
```bash
chmod +x deploy-app.sh
./deploy-app.sh
```

### Option 2: Manual Step-by-Step

Follow the complete guide in **[DEPLOYMENT-STEPS.md](DEPLOYMENT-STEPS.md)** with detailed commands for each step.

### Quick Verification

```bash
# Check all pods running
kubectl get pods

# Check services
kubectl get svc

# Test backend API
BASTION_IP=$(cd terraform && terraform output -raw bastion_external_ip)
curl http://$BASTION_IP/api/health

# Open in browser
echo "http://$BASTION_IP"
```

## 🧪 Testing

### Test Backend API

```bash
# Health check
curl http://BASTION_IP/health

# Create user
curl -X POST http://BASTION_IP/api/users \
  -H "Content-Type: application/json" \
  -d '{"name":"Test User","email":"test@example.com"}'

# Get users
curl http://BASTION_IP/api/users

# Upload file
curl -X POST http://BASTION_IP/api/upload \
  -F "file=@test.txt"

# List files
curl http://BASTION_IP/api/upload/files

# Publish message
curl -X POST http://BASTION_IP/api/pubsub/publish \
  -H "Content-Type: application/json" \
  -d '{"message":"Hello Pub/Sub!"}'

# Get events
curl http://BASTION_IP/api/pubsub/events
```

### Access Swagger UI

```
http://BASTION_IP/swagger
```

## 🔍 Monitoring & Logs

### View Application Logs

```bash
# Backend logs
kubectl logs -l app=backend --tail=100

# Frontend logs
kubectl logs -l app=frontend --tail=100

# Follow logs
kubectl logs -l app=backend -f
```

### Check Pod Status

```bash
# Get all pods
kubectl get pods

# Describe pod
kubectl describe pod <pod-name>

# Get events
kubectl get events --sort-by='.lastTimestamp'
```

### GKE Monitoring

```bash
# Open GKE console
gcloud console

# View metrics
https://console.cloud.google.com/kubernetes/workload
```

## 🛠️ Troubleshooting

### Common Issues

**Pods not starting?**
```bash
kubectl describe pod <pod-name>
kubectl logs <pod-name>
```

**Can't connect to Cloud SQL?**
```bash
# Verify private IP
gcloud sql instances describe dot-net-postgres --format="get(ipAddresses[0].ipAddress)"

# Check Workload Identity
kubectl describe serviceaccount backend-sa
```

**Redis connection failed?**
```bash
# Verify Redis instance
gcloud redis instances describe dot-net-redis --region=asia-south2
```

**Images not pulling?**
```bash
# Verify Artifact Registry
gcloud artifacts docker images list asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo

# Re-authenticate Docker
gcloud auth configure-docker asia-south2-docker.pkg.dev
```

## 🧹 Cleanup

### Delete Application

```bash
kubectl delete -f k8s/backend.yaml
kubectl delete -f k8s/frontend.yaml
```

### Destroy Infrastructure

```bash
cd terraform
terraform destroy
```

**⚠️ Warning:** This will delete:
- GKE cluster and all pods
- Cloud SQL database (all data)
- Redis instance (all cached data)
- Pub/Sub topic and messages
- Cloud Storage bucket (all files)
- VPC and networking resources
- Service accounts and IAM bindings

## 📚 Additional Resources

- **[DEPLOYMENT-STEPS.md](DEPLOYMENT-STEPS.md)** - Complete deployment guide
- **[terraform/README.md](terraform/README.md)** - Terraform documentation
- [GKE Documentation](https://cloud.google.com/kubernetes-engine/docs)
- [Cloud SQL Documentation](https://cloud.google.com/sql/docs)
- [.NET on GCP](https://cloud.google.com/dotnet)
- [Angular Documentation](https://angular.io/docs)

## 📝 License

This project is for educational and demonstration purposes.

## 👤 Author

**Jatin Jaiswal**
- GitHub: [@Jatin-Jaiswal](https://github.com/Jatin-Jaiswal)
- Repository: [DotNet_GCP](https://github.com/Jatin-Jaiswal/DotNet_GCP)

## 🤝 Contributing

Contributions, issues, and feature requests are welcome!

## ⭐ Show Your Support

Give a ⭐️ if this project helped you learn about GCP, Terraform, .NET, and Kubernetes!

---

**Built with ❤️ using Terraform, .NET 6, Angular, and Google Cloud Platform**
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
