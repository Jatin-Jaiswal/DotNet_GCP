# Application Deployment Steps After Terraform

This guide provides the exact commands and steps to deploy the application after Terraform creates the infrastructure.

## Prerequisites

- Terraform has successfully created all 62 resources (including Secret Manager)
- You have `gcloud`, `docker`, and `kubectl` installed locally
- You are authenticated with GCP: `gcloud auth login`

## Architecture Overview

This deployment uses **Google Secret Manager** for centralized configuration management:
- All sensitive configuration (Cloud SQL, Redis, Pub/Sub, Storage) is stored in Secret Manager
- Application automatically fetches secrets at runtime using GKE Workload Identity
- No hardcoded credentials in ConfigMaps or environment variables
- Zero manual configuration updates needed when infrastructure changes

---

## Step 1: Verify Infrastructure and Secret Manager

```bash
cd terraform
terraform output
```

**Verify these resources are created:**
- `bastion_external_ip` - Bastion VM public IP (e.g., 34.131.29.2)
- `cloudsql_private_ip` - Cloud SQL private IP (e.g., 10.23.0.3)
- `redis_host` - Redis instance IP (e.g., 10.119.34.243)
- `backend_service_account` - Backend service account email
- `secret_manager_secrets` - List of 10 secrets created

### Verify Secrets are Populated

```bash
# Check Cloud SQL host secret
gcloud secrets versions access latest --secret="cloudsql-host"

# Check Redis host secret  
gcloud secrets versions access latest --secret="redis-host"

# Check GCP project ID secret
gcloud secrets versions access latest --secret="gcp-project-id"
```

**Expected output:** Should show actual IP addresses and project ID (not placeholders)

---

## Step 2: Configure Docker for Artifact Registry

```bash
gcloud auth configure-docker asia-south2-docker.pkg.dev --quiet
```

---

## Step 3: Build and Push Backend Image

The backend application includes Secret Manager integration that:
- Retrieves project ID from GKE metadata service
- Fetches all configuration from Secret Manager at runtime
- No hardcoded values or manual configuration needed

```bash
cd backend
docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:secret-manager-v2 .
docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:secret-manager-v2
cd ..
```

**Expected output:** `digest: sha256:...` (image successfully pushed)

---

## Step 4: Deploy Backend to GKE

### 4.1 Update backend.yaml Image Tag

The `k8s/backend.yaml` file should have:
```yaml
image: asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:secret-manager-v2
```

**Note:** No ConfigMap updates needed - all configuration comes from Secret Manager!

### 4.2 Copy backend.yaml to Bastion VM
```bash
BASTION_IP=$(cd terraform && terraform output -raw bastion_external_ip)
gcloud compute scp k8s/backend.yaml dot-net-bastion-vm:~/ --zone=asia-south2-b
```

### 4.3 Configure kubectl and Deploy Backend
```bash
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --command="gcloud container clusters get-credentials private-gke-cluster --zone=asia-south2-b"
```
```bash
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --command="kubectl apply -f backend.yaml"
```

**Expected output:**
```
configmap/backend-config created
serviceaccount/backend-sa created
deployment.apps/backend-deployment created
service/backend-service created
```

### 4.4 Verify Backend Pods are Running
```bash
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --command="kubectl get pods -l app=backend"
```

**Expected output:** Pods should be `Running` within 30-60 seconds
```
NAME                                  READY   STATUS    RESTARTS   AGE
backend-deployment-xxxxxxxxxx-xxxxx   1/1     Running   0          45s
backend-deployment-xxxxxxxxxx-xxxxx   1/1     Running   0          45s
```

### 4.5 Check Logs to Verify Secret Manager Integration
```bash
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --command="kubectl logs -l app=backend --tail=30 | grep -E '(Retrieved|Secret Manager|PostgreSQL|Redis)'"
```

**Expected output should show:**
```
Retrieved project ID from metadata: project-84d8bfc9-cd8e-4b3c-b15
Retrieved secret: cloudsql-host
Retrieved secret: cloudsql-database
Retrieved secret: cloudsql-username
Retrieved secret: cloudsql-password
PostgreSQL connection initialized with all config from Secret Manager
Retrieved secret: redis-host
Retrieved secret: redis-port
Connected to Redis from Secret Manager config: 10.119.34.243:6379
```

---

## Step 5: Get Backend Load Balancer IP

### 5.1 Wait for Backend ILB IP (takes 1-2 minutes)
```bash
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --command="kubectl get svc backend-service -w"
```

**Wait until EXTERNAL-IP shows an IP like `10.0.2.9` (not `<pending>`)**

Press `Ctrl+C` once you see the IP.

### 5.2 Get and Store Backend ILB IP
```bash
BACKEND_ILB_IP=$(gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --command="kubectl get svc backend-service -o jsonpath='{.status.loadBalancer.ingress[0].ip}'")

echo "Backend ILB IP: $BACKEND_ILB_IP"
```

**Example output:** `Backend ILB IP: 10.0.2.9`

---

## Step 6: Build and Push Frontend Image

```bash
cd frontend
docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/frontend:latest .
docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/frontend:latest
cd ..
```

**Expected output:** `digest: sha256:...` (image successfully pushed)

**Note:** Frontend nginx configuration will be updated after deployment to use the Backend ILB IP.

---

## Step 7: Deploy Frontend to GKE (Optional)

### 7.1 Copy frontend.yaml to Bastion VM
```bash
gcloud compute scp k8s/frontend.yaml dot-net-bastion-vm:~/ --zone=asia-south2-b
```

### 7.2 Deploy Frontend
```bash
gcloud compute ssh dot-net-bastion-vm \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap \
  --command "kubectl apply -f /tmp/frontend.yaml"
```

**Expected output:**
```
deployment.apps/frontend-deployment created
service/frontend-service created
```

### 7.3 Wait for Frontend ILB IP (takes 1-2 minutes)
```bash
gcloud compute ssh dot-net-bastion-vm \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap \
  --command "kubectl get svc frontend-service -w"
```

**Wait until EXTERNAL-IP shows an IP like `10.0.1.7` (not `<pending>`)**

Press `Ctrl+C` once you see the IP.

### 7.4 Get Frontend ILB IP
```bash
FRONTEND_ILB_IP=$(gcloud compute ssh dot-net-bastion-vm \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap \
  --command "kubectl get svc frontend-service -o jsonpath='{.status.loadBalancer.ingress[0].ip}'")

echo "Frontend ILB IP: $FRONTEND_ILB_IP"
```

**Example output:** `Frontend ILB IP: 10.0.1.7`

---

## Step 8: Update Bastion Nginx Configuration

### 8.1 Update `bastion-nginx.conf`

```nginx
proxy_pass http://10.0.1.7/;  # Replace with $FRONTEND_ILB_IP
proxy_pass http://10.0.1.6/api/;  # Replace with $BACKEND_ILB_IP
```

**Automated update:**
```bash
sed -i "s|proxy_pass http://[0-9.]*/;|proxy_pass http://$FRONTEND_ILB_IP/;|" bastion-nginx.conf
sed -i "s|proxy_pass http://[0-9.]*/api/;|proxy_pass http://$BACKEND_ILB_IP/api/;|" bastion-nginx.conf
```

### 8.2 Copy and Apply Nginx Config to Bastion
```bash
gcloud compute scp bastion-nginx.conf dot-net-bastion-vm:/tmp/nginx-default \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap

gcloud compute ssh dot-net-bastion-vm \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap \
  --command "sudo mv /tmp/nginx-default /etc/nginx/sites-available/default && sudo systemctl restart nginx"
```

---

## Step 9: Verify Deployment

### 9.1 Check All Pods Running
```bash
gcloud compute ssh dot-net-bastion-vm \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap \
  --command "kubectl get pods"
```

### 9.2 Check All Services
```bash
gcloud compute ssh dot-net-bastion-vm \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap \
  --command "kubectl get svc"
```

### 9.3 Test Frontend via Bastion
```bash
BASTION_IP=$(cd terraform && terraform output -raw bastion_external_ip)
curl -I http://$BASTION_IP
```

### 9.4 Test Backend API via Bastion
```bash
curl http://$BASTION_IP/api/health
```

### 9.5 Test Backend Users API
```bash
curl http://$BASTION_IP/api/users
```

---

## Step 10: Access the Application
```bash
echo "Application URL: http://$BASTION_IP"
```
Open the URL in a browser to reach the Angular frontend.

---

## Troubleshooting

### Pods not starting?
```bash
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl describe pod -l app=backend"

gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl logs -l app=backend --tail=50"
```

### ILB IP not assigned?
```bash
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl describe svc backend-service"
```

### Nginx errors on bastion?
```bash
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "sudo systemctl status nginx"

gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "sudo tail -n 50 /var/log/nginx/error.log"
```

### Backend connectivity issues?
```bash
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl get configmap backend-config -o yaml"

gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl logs -l app=backend --tail=100 | grep -i error"
```
