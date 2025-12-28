# Deployment Configuration

This folder contains Kubernetes manifests for deploying the application to GKE.

## 📁 Structure

```
k8s/
├── frontend.yaml    # Frontend deployment + service (ILB)
└── backend.yaml     # Backend deployment + service (ILB) + configmap + serviceaccount
```

## 🚀 Deployment Steps

### 1. Set Up Workload Identity

First, create a GCP service account and bind it to the Kubernetes service account:

```bash
# Create GCP service account
gcloud iam service-accounts create backend-gke-sa \
  --display-name="Backend GKE Service Account"

# Grant necessary permissions
gcloud projects add-iam-policy-binding project-84d8bfc9-cd8e-4b3c-b15 \
  --member="serviceAccount:backend-gke-sa@project-84d8bfc9-cd8e-4b3c-b15.iam.gserviceaccount.com" \
  --role="roles/cloudsql.client"

gcloud projects add-iam-policy-binding project-84d8bfc9-cd8e-4b3c-b15 \
  --member="serviceAccount:backend-gke-sa@project-84d8bfc9-cd8e-4b3c-b15.iam.gserviceaccount.com" \
  --role="roles/pubsub.publisher"

gcloud projects add-iam-policy-binding project-84d8bfc9-cd8e-4b3c-b15 \
  --member="serviceAccount:backend-gke-sa@project-84d8bfc9-cd8e-4b3c-b15.iam.gserviceaccount.com" \
  --role="roles/storage.objectCreator"

# Bind GCP SA to Kubernetes SA
gcloud iam service-accounts add-iam-policy-binding \
  backend-gke-sa@project-84d8bfc9-cd8e-4b3c-b15.iam.gserviceaccount.com \
  --role roles/iam.workloadIdentityUser \
  --member "serviceAccount:project-84d8bfc9-cd8e-4b3c-b15.svc.id.goog[default/backend-sa]"
```

### 2. Build and Push Docker Images

```bash
# Configure Docker for Artifact Registry
gcloud auth configure-docker asia-south2-docker.pkg.dev

# Build and push backend
cd backend
docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:latest .
docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:latest

# Build and push frontend
cd ../frontend
docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/frontend:latest .
docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/frontend:latest
```

### 3. Connect to GKE Cluster

```bash
gcloud container clusters get-credentials private-gke-cluster --region asia-south2
```

### 4. Deploy to Kubernetes

```bash
# Deploy backend (includes configmap, serviceaccount, deployment, and service)
kubectl apply -f k8s/backend.yaml

# Deploy frontend (includes deployment and service)
kubectl apply -f k8s/frontend.yaml
```

### 5. Verify Deployment

```bash
# Check pods
kubectl get pods

# Check services
kubectl get services

# Check logs
kubectl logs -f deployment/backend-deployment
kubectl logs -f deployment/frontend-deployment
```

## 📊 Architecture

### Internal Load Balancers (ILB)

Both frontend and backend use Internal Load Balancers with private IPs only:

- **Frontend ILB**: Receives traffic from bastion proxy
- **Backend ILB**: Receives traffic from frontend

### High Availability

- **Backend**: 3 replicas across multiple zones
- **Frontend**: 2 replicas across multiple zones
- **Health Checks**: Liveness and readiness probes configured

### Resource Allocation

**Backend:**
- Requests: 256Mi memory, 250m CPU
- Limits: 512Mi memory, 500m CPU

**Frontend:**
- Requests: 128Mi memory, 100m CPU
- Limits: 256Mi memory, 200m CPU

## 🔐 Security

- **Workload Identity**: No service account keys needed
- **Private ILBs**: No public IP addresses
- **Network Policies**: Can be added for pod-to-pod security
- **ConfigMap**: Environment variables (not secrets)

## 📝 Configuration

All backend configuration is in `configmap.yaml`:
- Cloud SQL connection details
- Redis host and port
- GCP project ID
- Pub/Sub topic name
- Storage bucket name

**To update configuration:**
```bash
kubectl edit configmap backend-config
kubectl rollout restart deployment/backend-deployment
```

## 🔄 Updates and Rollbacks

### Update deployment:
```bash
kubectl set image deployment/backend-deployment backend=asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:v2
```

### Rollback:
```bash
kubectl rollout undo deployment/backend-deployment
```

### Check rollout status:
```bash
kubectl rollout status deployment/backend-deployment
```
