# Application Deployment Steps After Terraform

This guide provides the exact commands and steps to deploy the application after Terraform creates the infrastructure.

## Prerequisites

- Terraform has successfully created all 31 resources
- You have `gcloud`, `docker`, and `kubectl` installed locally
- You are authenticated with GCP: `gcloud auth login`

---

## Step 1: Get Infrastructure IPs from Terraform

```bash
cd terraform
terraform output
```

**Note down these values:**
- `bastion_external_ip` - Bastion VM public IP
- `cloudsql_private_ip` - Cloud SQL private IP
- `redis_host` - Redis instance IP
- `backend_service_account` - Backend service account email

---

## Step 2: Update Backend Configuration

### 2.1 Get the IPs
```bash
CLOUDSQL_IP=$(cd terraform && terraform output -raw cloudsql_private_ip)
REDIS_IP=$(cd terraform && terraform output -raw redis_host)
echo "Cloud SQL IP: $CLOUDSQL_IP"
echo "Redis IP: $REDIS_IP"
```

### 2.2 Update `k8s/backend.yaml`

Open the file and update these lines in the ConfigMap section:

```yaml
# Line ~13: Update Cloud SQL Host
CloudSql__Host: "10.117.0.3"  # Replace with $CLOUDSQL_IP

# Line ~18: Update Redis Host
Redis__Host: "10.163.53.91"   # Replace with $REDIS_IP
```

**Manual edit:**
```bash
nano k8s/backend.yaml
# OR
code k8s/backend.yaml
```

**Automated update:**
```bash
sed -i "s/CloudSql__Host: \".*\"/CloudSql__Host: \"$CLOUDSQL_IP\"/" k8s/backend.yaml
sed -i "s/Redis__Host: \".*\"/Redis__Host: \"$REDIS_IP\"/" k8s/backend.yaml
```

---

## Step 3: Configure Docker for Artifact Registry

```bash
gcloud auth configure-docker asia-south2-docker.pkg.dev --quiet
```

---

## Step 4: Build and Push Backend Image

```bash
cd backend
docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:latest .
docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/backend:latest
cd ..
```

**Expected output:** `digest: sha256:...` (image successfully pushed)

---

## Step 5: Deploy Backend to GKE

### 5.1 Copy backend.yaml to Bastion VM
```bash
BASTION_IP=$(cd terraform && terraform output -raw bastion_external_ip)
gcloud compute scp k8s/backend.yaml dot-net-bastion-vm:/tmp/backend.yaml \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap
```

### 5.2 Deploy Backend
```bash
gcloud compute ssh dot-net-bastion-vm \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap \
  --command "kubectl apply -f /tmp/backend.yaml"
```

**Expected output:**
```
configmap/backend-config created
serviceaccount/backend-sa created
deployment.apps/backend-deployment created
service/backend-service created
```

### 5.3 Wait for Backend ILB IP (takes 1-2 minutes)
```bash
gcloud compute ssh dot-net-bastion-vm \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap \
  --command "kubectl get svc backend-service -w"
```

**Wait until EXTERNAL-IP shows an IP like `10.0.1.6` (not `<pending>`)**

Press `Ctrl+C` once you see the IP.

### 5.4 Get Backend ILB IP
```bash
BACKEND_ILB_IP=$(gcloud compute ssh dot-net-bastion-vm \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap \
  --command "kubectl get svc backend-service -o jsonpath='{.status.loadBalancer.ingress[0].ip}'")

echo "Backend ILB IP: $BACKEND_ILB_IP"
```

**Example output:** `Backend ILB IP: 10.0.1.6`

---

## Step 6: Update Frontend Configuration

### 6.1 Update `frontend/nginx.conf`

Open the file and update this line:

```nginx
# Line ~43: Update backend proxy_pass
proxy_pass http://10.0.1.6/api/;  # Replace 10.0.1.6 with $BACKEND_ILB_IP
```

**Manual edit:**
```bash
nano frontend/nginx.conf
# OR
code frontend/nginx.conf
```

**Automated update:**
```bash
sed -i "s|proxy_pass http://[0-9.]*\/api\/;|proxy_pass http://$BACKEND_ILB_IP/api/;|" frontend/nginx.conf
```

### 6.2 Update `frontend/src/environments/environment.prod.ts` (Optional)

Update the comment on line ~13 with the correct backend IP:

```typescript
// Line ~13: Update comment
* Bastion nginx routes /api/* to backend Internal Load Balancer (10.0.1.6)
```

---

## Step 7: Build and Push Frontend Image

```bash
cd frontend
docker build -t asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/frontend:latest .
docker push asia-south2-docker.pkg.dev/project-84d8bfc9-cd8e-4b3c-b15/dot-net-repo/frontend:latest
cd ..
```

**Expected output:** `digest: sha256:...` (image successfully pushed)

---

## Step 8: Deploy Frontend to GKE

### 8.1 Copy frontend.yaml to Bastion VM
```bash
gcloud compute scp k8s/frontend.yaml dot-net-bastion-vm:/tmp/frontend.yaml \
  --zone=asia-south2-b \
  --project=project-84d8bfc9-cd8e-4b3c-b15 \
  --tunnel-through-iap
```

### 8.2 Deploy Frontend
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
