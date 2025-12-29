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
✅ Retrieved project ID from metadata: project-84d8bfc9-cd8e-4b3c-b15
✅ Retrieved secret: cloudsql-host
✅ Retrieved secret: cloudsql-database
✅ Retrieved secret: cloudsql-username
✅ Retrieved secret: cloudsql-password
✅ PostgreSQL connection initialized with all config from Secret Manager
✅ Retrieved secret: redis-host
✅ Retrieved secret: redis-port
✅ Connected to Redis from Secret Manager config: 10.119.34.243:6379
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

## Step 7: Deploy Frontend to GKE (Optional - if using Angular frontend)

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

### 8.3 Wait for Frontend ILB IP (takes 1-2 minutes)
```bash
gcloud compute ssh dot-net-bastion-vm \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap \
  --command "kubectl get svc frontend-service -w"
```

**Wait until EXTERNAL-IP shows an IP like `10.0.1.7` (not `<pending>`)**

Press `Ctrl+C` once you see the IP.

### 8.4 Get Frontend ILB IP
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

## Step 9: Update Bastion Nginx Configuration

### 9.1 Update `bastion-nginx.conf`

Open the file and update these proxy_pass lines:

```nginx
# Line ~8: Proxy frontend
proxy_pass http://10.0.1.7/;  # Replace 10.0.1.7 with $FRONTEND_ILB_IP

# Line ~15: Proxy backend API
proxy_pass http://10.0.1.6/api/;  # Replace 10.0.1.6 with $BACKEND_ILB_IP
```

**Manual edit:**
```bash
nano bastion-nginx.conf
# OR
code bastion-nginx.conf
```

**Automated update:**
```bash
sed -i "s|proxy_pass http://[0-9.]*/;|proxy_pass http://$FRONTEND_ILB_IP/;|" bastion-nginx.conf
sed -i "s|proxy_pass http://[0-9.]*/api/;|proxy_pass http://$BACKEND_ILB_IP/api/;|" bastion-nginx.conf
```

### 9.2 Copy and Apply Nginx Config to Bastion
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

**No output means success!**

---

## Step 10: Verify Deployment

### 10.1 Check All Pods Running
```bash
gcloud compute ssh dot-net-bastion-vm \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap \
  --command "kubectl get pods"
```

**Expected output:** All pods should show `STATUS: Running` and `READY: 1/1`
```
NAME                                   READY   STATUS    RESTARTS   AGE
backend-deployment-xxxxx-xxxxx         1/1     Running   0          5m
backend-deployment-xxxxx-xxxxx         1/1     Running   0          5m
frontend-deployment-xxxxx-xxxxx        1/1     Running   0          3m
frontend-deployment-xxxxx-xxxxx        1/1     Running   0          3m
```

### 10.2 Check All Services
```bash
gcloud compute ssh dot-net-bastion-vm \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap \
  --command "kubectl get svc"
```

**Expected output:** Both services should have EXTERNAL-IP assigned
```
NAME               TYPE           CLUSTER-IP    EXTERNAL-IP   PORT(S)        AGE
backend-service    LoadBalancer   10.8.x.x      10.0.1.6      80:31536/TCP   5m
frontend-service   LoadBalancer   10.8.x.x      10.0.1.7      80:30123/TCP   3m
```

### 10.3 Test Frontend via Bastion
```bash
BASTION_IP=$(cd terraform && terraform output -raw bastion_external_ip)
curl -I http://$BASTION_IP
```

**Expected output:**
```
HTTP/1.1 200 OK
Server: nginx
Content-Type: text/html
```

### 10.4 Test Backend API via Bastion
```bash
curl http://$BASTION_IP/api/health
```

**Expected output:**
```
Healthy
```

### 10.5 Test Backend Users API
```bash
curl http://$BASTION_IP/api/users
```

**Expected output:** JSON array (may be empty initially)
```json
[]
```

---

## Step 11: Access the Application

### Open in Browser
```bash
echo "Application URL: http://$BASTION_IP"
```

**Copy the URL and open it in your browser.**

You should see the Angular frontend application!

---

## Troubleshooting

### Pods not starting?
```bash
# Check pod details
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl describe pod -l app=backend"

# Check pod logs
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl logs -l app=backend --tail=50"
```

### ILB IP not assigned?
```bash
# Check service events
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl describe svc backend-service"
```

### Nginx errors on bastion?
```bash
# Check nginx status
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "sudo systemctl status nginx"

# Check nginx logs
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "sudo tail -n 50 /var/log/nginx/error.log"
```

### Backend can't connect to Cloud SQL/Redis?
```bash
# Verify ConfigMap
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl get configmap backend-config -o yaml"

# Check backend logs for connection errors
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl logs -l app=backend --tail=100 | grep -i error"
```

---

## Summary of IPs After Deployment

| Component | IP Address | Type |
|-----------|------------|------|
| Bastion VM | `34.131.236.231` (example) | External (Public) |
| Backend ILB | `10.0.1.6` (example) | Internal (Private) |
| Frontend ILB | `10.0.1.7` (example) | Internal (Private) |
| Cloud SQL | `10.117.0.3` (example) | Internal (Private) |
| Redis | `10.163.53.91` (example) | Internal (Private) |

**Access Flow:**
```
Internet → Bastion (34.131.236.231) → Frontend ILB (10.0.1.7) → Frontend Pods
                                    → Backend ILB (10.0.1.6) → Backend Pods
                                                             → Cloud SQL (10.117.0.3)
                                                             → Redis (10.163.53.91)
```

---

## Complete Automated Deployment Script

Save this as `deploy-app.sh` for one-command deployment:

```bash
#!/bin/bash
set -e

echo "=== Getting Infrastructure IPs from Terraform ==="
cd terraform
CLOUDSQL_IP=$(terraform output -raw cloudsql_private_ip)
REDIS_IP=$(terraform output -raw redis_host)
BASTION_IP=$(terraform output -raw bastion_external_ip)
cd ..

echo "Cloud SQL IP: $CLOUDSQL_IP"
echo "Redis IP: $REDIS_IP"
echo "Bastion IP: $BASTION_IP"

echo -e "\n=== Updating Backend Configuration ==="
sed -i "s/CloudSql__Host: \".*\"/CloudSql__Host: \"$CLOUDSQL_IP\"/" k8s/backend.yaml
sed -i "s/Redis__Host: \".*\"/Redis__Host: \"$REDIS_IP\"/" k8s/backend.yaml

echo -e "\n=== Building and Pushing Backend Image ==="
cd backend
docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:latest .
docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:latest
cd ..

echo -e "\n=== Deploying Backend to GKE ==="
gcloud compute scp k8s/backend.yaml dot-net-bastion-vm:/tmp/backend.yaml \
  --zone=asia-south2-b --tunnel-through-iap
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl apply -f /tmp/backend.yaml"

echo -e "\n=== Waiting for Backend ILB IP (60 seconds) ==="
sleep 60

BACKEND_ILB_IP=$(gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl get svc backend-service -o jsonpath='{.status.loadBalancer.ingress[0].ip}'")
echo "Backend ILB IP: $BACKEND_ILB_IP"

echo -e "\n=== Updating Frontend Configuration ==="
sed -i "s|proxy_pass http://[0-9.]*\/api\/;|proxy_pass http://$BACKEND_ILB_IP/api/;|" frontend/nginx.conf

echo -e "\n=== Building and Pushing Frontend Image ==="
cd frontend
docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/frontend:latest .
docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/frontend:latest
cd ..

echo -e "\n=== Deploying Frontend to GKE ==="
gcloud compute scp k8s/frontend.yaml dot-net-bastion-vm:/tmp/frontend.yaml \
  --zone=asia-south2-b --tunnel-through-iap
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl apply -f /tmp/frontend.yaml"

echo -e "\n=== Waiting for Frontend ILB IP (60 seconds) ==="
sleep 60

FRONTEND_ILB_IP=$(gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "kubectl get svc frontend-service -o jsonpath='{.status.loadBalancer.ingress[0].ip}'")
echo "Frontend ILB IP: $FRONTEND_ILB_IP"

echo -e "\n=== Updating Bastion Nginx Configuration ==="
sed -i "s|proxy_pass http://[0-9.]*/;|proxy_pass http://$FRONTEND_ILB_IP/;|" bastion-nginx.conf
sed -i "s|proxy_pass http://[0-9.]*/api/;|proxy_pass http://$BACKEND_ILB_IP/api/;|" bastion-nginx.conf

gcloud compute scp bastion-nginx.conf dot-net-bastion-vm:/tmp/nginx-default \
  --zone=asia-south2-b --tunnel-through-iap
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap \
  --command "sudo mv /tmp/nginx-default /etc/nginx/sites-available/default && sudo systemctl restart nginx"

echo -e "\n=== Deployment Complete! ==="
echo "Application URL: http://$BASTION_IP"
echo -e "\nVerifying deployment..."
curl -I http://$BASTION_IP
curl http://$BASTION_IP/api/health
echo -e "\n✅ All done! Open http://$BASTION_IP in your browser"
```

**Make it executable and run:**
```bash
chmod +x deploy-app.sh
./deploy-app.sh
```

---

## Quick Reference - Just the Commands

```bash
# 1. Get IPs
cd terraform && terraform output && cd ..

# 2. Update backend config (manual or sed)
nano k8s/backend.yaml

# 3. Build & push backend
cd backend && docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:latest . && docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:latest && cd ..

# 4. Deploy backend
gcloud compute scp k8s/backend.yaml dot-net-bastion-vm:/tmp/backend.yaml --zone=asia-south2-b --tunnel-through-iap && gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap --command "kubectl apply -f /tmp/backend.yaml"

# 5. Get backend ILB IP (wait 60s first)
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap --command "kubectl get svc backend-service"

# 6. Update frontend config (manual or sed)
nano frontend/nginx.conf

# 7. Build & push frontend
cd frontend && docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/frontend:latest . && docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/frontend:latest && cd ..

# 8. Deploy frontend
gcloud compute scp k8s/frontend.yaml dot-net-bastion-vm:/tmp/frontend.yaml --zone=asia-south2-b --tunnel-through-iap && gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap --command "kubectl apply -f /tmp/frontend.yaml"

# 9. Get frontend ILB IP (wait 60s first)
gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap --command "kubectl get svc frontend-service"

# 10. Update bastion nginx (manual or sed)
nano bastion-nginx.conf

# 11. Apply bastion nginx
gcloud compute scp bastion-nginx.conf dot-net-bastion-vm:/tmp/nginx-default --zone=asia-south2-b --tunnel-through-iap && gcloud compute ssh dot-net-bastion-vm --zone=asia-south2-b --tunnel-through-iap --command "sudo mv /tmp/nginx-default /etc/nginx/sites-available/default && sudo systemctl restart nginx"

# 12. Test
curl http://$(cd terraform && terraform output -raw bastion_external_ip)
```
