# ================================================
# Terraform Configuration for .NET on GCP - Complete Setup Guide
# ================================================

This Terraform configuration creates a complete, production-ready infrastructure for deploying a .NET application with Angular frontend on Google Cloud Platform (GCP).

## Architecture Overview

The infrastructure includes:

- **VPC Network**: Custom VPC with public and private subnets
- **GKE Private Cluster**: Kubernetes cluster with Workload Identity enabled
- **Cloud SQL PostgreSQL**: Private PostgreSQL database instance
- **Memorystore Redis**: Private Redis cache instance
- **Cloud Storage**: Bucket for file uploads
- **Pub/Sub**: Topic and subscription for event messaging
- **Artifact Registry**: Docker image repository
- **Bastion VM**: Jump host with nginx reverse proxy and kubectl access
- **Service Accounts**: IAM service accounts with proper permissions
- **Workload Identity**: Secure pod-to-GCP service authentication

## Prerequisites

1. **GCP Project**: A GCP project with billing enabled
2. **gcloud CLI**: Installed and authenticated
   ```bash
   gcloud auth login
   gcloud auth application-default login
   ```
3. **Terraform**: Version 1.0 or higher installed
   ```bash
   terraform --version
   ```
4. **Permissions**: Your GCP user needs these roles:
   - Project Editor or Owner
   - Service Account Admin
   - Kubernetes Engine Admin

## Quick Start

### 1. Clone and Navigate

```bash
cd terraform/
```

### 2. Configure Variables

Edit `variables.tf` or create a `terraform.tfvars` file:

```hcl
project_id  = "your-project-id"
region      = "asia-south2"
db_password = "your-secure-password"
bucket_name = "your-unique-bucket-name"  # Must be globally unique
```

### 3. Initialize Terraform

```bash
terraform init
```

This downloads the required Google Cloud provider plugins.

### 4. Review the Plan

```bash
terraform plan
```

Review all resources that will be created. Terraform will show:
- 30+ resources to be created
- Estimated costs (if cost estimation is enabled)
- Any configuration issues

### 5. Apply the Configuration

```bash
terraform apply
```

Type `yes` when prompted. Infrastructure creation takes approximately **15-20 minutes**.

### 6. Get Output Values

```bash
terraform output
```

This displays critical values:
- `bastion_external_ip`: SSH and web access point
- `cloudsql_private_ip`: PostgreSQL connection string
- `redis_host`: Redis connection endpoint
- `backend_service_account`: GKE workload identity email

## Infrastructure Details

### Network Architecture

```
Internet
   │
   ├─> Bastion VM (34.x.x.x) - public subnet (10.0.2.0/24)
   │   └─> Nginx reverse proxy
   │
   └─> Private Subnet (10.0.1.0/24)
       ├─> GKE Cluster (private nodes)
       │   ├─> Frontend Pods (2 replicas)
       │   └─> Backend Pods (3 replicas)
       │
       ├─> Cloud SQL PostgreSQL (10.92.160.3)
       └─> Memorystore Redis (10.127.80.5)
```

### Resource Specifications

| Resource | Type | Configuration |
|----------|------|---------------|
| **GKE Cluster** | Private | 2-4 nodes (e2-medium), autoscaling |
| **Cloud SQL** | PostgreSQL 15 | db-f1-micro, 10GB SSD |
| **Redis** | Memorystore | BASIC tier, 1GB memory |
| **Bastion VM** | Ubuntu 22.04 | e2-small, Docker + kubectl + gcloud |
| **Cloud Storage** | Regional | Uniform access, CORS enabled |
| **Artifact Registry** | Docker | Regional repository |

### Security Features

1. **Private GKE Cluster**: No public endpoints for worker nodes
2. **VPC-Native Networking**: IP aliasing for pods and services
3. **Workload Identity**: Eliminates need for service account keys
4. **Private Service Connection**: Cloud SQL and Redis on private IPs
5. **Cloud NAT**: Egress traffic for private resources
6. **Firewall Rules**: Minimal access (SSH and HTTP to bastion only)

## Post-Deployment Steps

### 1. Access the Bastion

Get the external IP:
```bash
BASTION_IP=$(terraform output -raw bastion_external_ip)
echo "Bastion IP: $BASTION_IP"
```

SSH into the bastion:
```bash
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b
```

### 2. Verify GKE Cluster Access

From the bastion VM:
```bash
kubectl get nodes
kubectl get namespaces
```

### 3. Create Kubernetes Service Account

The backend pods need a Kubernetes service account linked to the GCP service account:

```bash
# From bastion VM
kubectl create serviceaccount backend-sa

# The Workload Identity binding is already configured via Terraform
```

### 4. Build and Push Docker Images

#### Backend:
```bash
cd backend/
docker build -t asia-south2-docker.pkg.dev/PROJECT_ID/dot-net-repo/dot-net-backend:latest .
docker push asia-south2-docker.pkg.dev/PROJECT_ID/dot-net-repo/dot-net-backend:latest
```

#### Frontend:
```bash
cd frontend/
docker build -t asia-south2-docker.pkg.dev/PROJECT_ID/dot-net-repo/dot-net-frontend:latest .
docker push asia-south2-docker.pkg.dev/PROJECT_ID/dot-net-repo/dot-net-frontend:latest
```

### 5. Create Kubernetes ConfigMap

Update `k8s/backend.yaml` with actual values from Terraform outputs:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: backend-config
data:
  POSTGRES_HOST: "<cloudsql_private_ip>"
  POSTGRES_PORT: "5432"
  POSTGRES_DB: "dotnetdb"
  POSTGRES_USER: "postgres"
  POSTGRES_PASSWORD: "<your_db_password>"
  REDIS_HOST: "<redis_host>"
  REDIS_PORT: "<redis_port>"
  PUBSUB_TOPIC_ID: "dot-net-topic"
  PUBSUB_SUBSCRIPTION_ID: "dot-net-sub"
  STORAGE_BUCKET: "<bucket_name>"
  PROJECT_ID: "<project_id>"
```

### 6. Deploy to Kubernetes

```bash
# From bastion VM
kubectl apply -f k8s/backend.yaml
kubectl apply -f k8s/frontend.yaml

# Check deployment status
kubectl get deployments
kubectl get pods
kubectl get services
```

### 7. Get Internal Load Balancer IP

```bash
kubectl get service backend-service -o jsonpath='{.status.loadBalancer.ingress[0].ip}'
kubectl get service frontend-service -o jsonpath='{.status.loadBalancer.ingress[0].ip}'
```

Update the bastion nginx configuration with the actual ILB IP if different from 10.0.2.9.

### 8. Access the Application

Open browser and navigate to:
```
http://<bastion_external_ip>
```

## Terraform Commands Reference

### Common Operations

```bash
# Preview changes
terraform plan

# Apply changes
terraform apply

# Apply with auto-approve (use carefully)
terraform apply -auto-approve

# Destroy specific resource
terraform destroy -target=google_compute_instance.bastion

# Show current state
terraform show

# List all resources
terraform state list

# Get specific output
terraform output bastion_external_ip

# Format configuration files
terraform fmt

# Validate configuration
terraform validate

# Generate dependency graph
terraform graph | dot -Tpng > graph.png
```

### State Management

```bash
# Backup state file
cp terraform.tfstate terraform.tfstate.backup

# Remove resource from state (doesn't delete actual resource)
terraform state rm google_compute_instance.bastion

# Import existing resource
terraform import google_compute_instance.bastion dot-net-bastion-vm

# Refresh state to match actual infrastructure
terraform refresh
```

## Troubleshooting

### Issue: API Not Enabled Error

**Error**: `Error 403: ... API has not been used in project ... before or it is disabled`

**Solution**: Wait 1-2 minutes after first apply for APIs to propagate, then run:
```bash
terraform apply
```

### Issue: Service Networking Connection Timeout

**Error**: `Error creating service networking connection: timeout while waiting for state`

**Solution**: This is normal for first-time VPC peering. Wait and retry:
```bash
terraform apply
```

### Issue: GKE Cluster Creation Fails

**Error**: `Error creating cluster: ... quota exceeded`

**Solution**: Check and request quota increases:
```bash
gcloud compute project-info describe --project=PROJECT_ID
```

### Issue: Cannot Access Bastion

**Solution**: Check firewall rules allow your IP:
```bash
gcloud compute firewall-rules list --filter="name:allow-ssh"

# Update firewall if needed
gcloud compute firewall-rules update allow-ssh --source-ranges=YOUR_IP/32
```

### Issue: kubectl Not Working on Bastion

**Solution**: Re-configure cluster credentials:
```bash
gcloud container clusters get-credentials private-gke-cluster --region=asia-south2 --internal-ip
```

## Costs Estimation

Approximate monthly costs (as of 2024, us-central1 region):

| Resource | Cost/Month (USD) |
|----------|------------------|
| GKE Cluster (2 e2-medium nodes) | ~$50 |
| Cloud SQL db-f1-micro | ~$15 |
| Memorystore Redis 1GB | ~$35 |
| Bastion e2-small | ~$15 |
| Cloud Storage (minimal usage) | ~$1 |
| Network egress | ~$5-10 |
| **Total** | **~$120-125** |

> **Note**: Costs vary by region and usage. Use [GCP Pricing Calculator](https://cloud.google.com/products/calculator) for accurate estimates.

## Cleanup

To delete all resources:

```bash
terraform destroy
```

Type `yes` when prompted. Destruction takes 5-10 minutes.

**⚠️ Warning**: This permanently deletes:
- All data in Cloud SQL
- All files in Cloud Storage
- All container images in Artifact Registry
- The entire GKE cluster and all deployments

## Advanced Configuration

### Using Remote State

For team collaboration, store state in Cloud Storage:

```hcl
# backend.tf
terraform {
  backend "gcs" {
    bucket = "my-terraform-state-bucket"
    prefix = "dotnet-gcp/state"
  }
}
```

Initialize with:
```bash
terraform init -backend-config="bucket=my-state-bucket"
```

### Environment-Specific Workspaces

```bash
# Create workspaces
terraform workspace new dev
terraform workspace new prod

# Switch workspaces
terraform workspace select dev
terraform apply -var-file=dev.tfvars

terraform workspace select prod
terraform apply -var-file=prod.tfvars
```

### Variable Precedence

Terraform loads variables in this order (last wins):
1. Environment variables: `TF_VAR_project_id`
2. `terraform.tfvars` file
3. `*.auto.tfvars` files
4. `-var` command line flags
5. `-var-file` command line flags

## Files Structure

```
terraform/
├── main.tf           # Main infrastructure resources
├── variables.tf      # Input variable definitions
├── outputs.tf        # Output value definitions
├── versions.tf       # Terraform and provider versions
├── terraform.tfvars  # Variable values (create this, git-ignored)
└── README.md         # This file
```

## Best Practices

1. **Never Commit Secrets**: Add `terraform.tfvars` and `*.tfstate` to `.gitignore`
2. **Use Remote State**: Store state in GCS for team collaboration
3. **Tag Resources**: Add labels for cost tracking and organization
4. **Version Lock**: Specify exact provider versions in production
5. **Plan Before Apply**: Always review `terraform plan` output
6. **Backup State**: Keep state file backups before major changes

## Support and Resources

- **GCP Documentation**: https://cloud.google.com/docs
- **Terraform GCP Provider**: https://registry.terraform.io/providers/hashicorp/google/latest/docs
- **GKE Best Practices**: https://cloud.google.com/kubernetes-engine/docs/best-practices

## License

This Terraform configuration is provided as-is for infrastructure provisioning.
