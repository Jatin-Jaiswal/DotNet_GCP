# ================================================
# Main Terraform Configuration
# Creates complete GCP infrastructure for DotNet Application
# ================================================

provider "google" {
  project = var.project_id
  region  = var.region
}

provider "google-beta" {
  project = var.project_id
  region  = var.region
}

# ================================================
# NETWORKING
# ================================================

# VPC Network
resource "google_compute_network" "vpc" {
  name                    = "dot-net-vpc"
  auto_create_subnetworks = false
}

# Private Subnet
resource "google_compute_subnetwork" "private_subnet" {
  name          = "private-subnet"
  ip_cidr_range = "10.0.1.0/24"
  region        = var.region
  network       = google_compute_network.vpc.id
  
  private_ip_google_access = true
}

# Public Subnet for Bastion
resource "google_compute_subnetwork" "public_subnet" {
  name          = "public-subnet"
  ip_cidr_range = "10.0.2.0/24"
  region        = var.region
  network       = google_compute_network.vpc.id
}

# Cloud Router for NAT
resource "google_compute_router" "router" {
  name    = "dot-net-router"
  region  = var.region
  network = google_compute_network.vpc.id
}

# Cloud NAT for private instances
resource "google_compute_router_nat" "nat" {
  name                               = "dot-net-nat"
  router                             = google_compute_router.router.name
  region                             = var.region
  nat_ip_allocate_option             = "AUTO_ONLY"
  source_subnetwork_ip_ranges_to_nat = "ALL_SUBNETWORKS_ALL_IP_RANGES"
}

# Firewall - Allow internal traffic
resource "google_compute_firewall" "allow_internal" {
  name    = "allow-internal"
  network = google_compute_network.vpc.name

  allow {
    protocol = "tcp"
    ports    = ["0-65535"]
  }

  allow {
    protocol = "udp"
    ports    = ["0-65535"]
  }

  allow {
    protocol = "icmp"
  }

  source_ranges = ["10.0.0.0/16"]
}

# Firewall - Allow SSH to bastion
resource "google_compute_firewall" "allow_ssh" {
  name    = "allow-ssh"
  network = google_compute_network.vpc.name

  allow {
    protocol = "tcp"
    ports    = ["22"]
  }

  source_ranges = ["0.0.0.0/0"]
  target_tags   = ["bastion"]
}

# Firewall - Allow HTTP/HTTPS to bastion
resource "google_compute_firewall" "allow_http" {
  name    = "allow-http-https"
  network = google_compute_network.vpc.name

  allow {
    protocol = "tcp"
    ports    = ["80", "443"]
  }

  source_ranges = ["0.0.0.0/0"]
  target_tags   = ["bastion"]
}

# Private Service Connection for Cloud SQL
resource "google_compute_global_address" "private_ip_range" {
  name          = "private-ip-range"
  purpose       = "VPC_PEERING"
  address_type  = "INTERNAL"
  prefix_length = 16
  network       = google_compute_network.vpc.id
}

resource "google_service_networking_connection" "private_vpc_connection" {
  network                 = google_compute_network.vpc.id
  service                 = "servicenetworking.googleapis.com"
  reserved_peering_ranges = [google_compute_global_address.private_ip_range.name]
  
  deletion_policy = "ABANDON"
}

# ================================================
# CLOUD SQL (PostgreSQL)
# ================================================

resource "google_sql_database_instance" "postgres" {
  name             = "dot-net-postgres"
  database_version = "POSTGRES_15"
  region           = var.region
  
  deletion_protection = false

  settings {
    tier              = "db-f1-micro"
    availability_type = "ZONAL"
    disk_size         = 10
    disk_type         = "PD_HDD"

    ip_configuration {
      ipv4_enabled    = false
      private_network = google_compute_network.vpc.id
    }

    backup_configuration {
      enabled = false
    }
  }

  depends_on = [google_service_networking_connection.private_vpc_connection]
}

resource "google_sql_database" "database" {
  name            = "dotnetdb"
  instance        = google_sql_database_instance.postgres.name
  deletion_policy = "DELETE"
}

resource "google_sql_user" "postgres_user" {
  name     = "postgres"
  instance = google_sql_database_instance.postgres.name
  password = var.db_password
}

# ================================================
# MEMORYSTORE REDIS
# ================================================

resource "google_redis_instance" "redis" {
  name           = "dot-net-redis"
  tier           = "BASIC"
  memory_size_gb = 1
  region         = var.region

  authorized_network = google_compute_network.vpc.id
  connect_mode       = "DIRECT_PEERING"

  redis_version = "REDIS_7_0"
}

# ================================================
# PUB/SUB
# ================================================

resource "google_pubsub_topic" "topic" {
  name = "dot-net-topic"
}

resource "google_pubsub_subscription" "subscription" {
  name  = "dot-net-sub"
  topic = google_pubsub_topic.topic.name

  ack_deadline_seconds = 20

  retry_policy {
    minimum_backoff = "10s"
    maximum_backoff = "600s"
  }
}

# ================================================
# CLOUD STORAGE
# ================================================

resource "google_storage_bucket" "bucket" {
  name          = var.bucket_name
  location      = var.region
  force_destroy = true

  uniform_bucket_level_access = true

  cors {
    origin          = ["*"]
    method          = ["GET", "HEAD", "PUT", "POST", "DELETE"]
    response_header = ["*"]
    max_age_seconds = 3600
  }
}

# ================================================
# ARTIFACT REGISTRY
# ================================================

resource "google_artifact_registry_repository" "docker_repo" {
  location      = var.region
  repository_id = "dot-net-repo"
  format        = "DOCKER"
}

# ================================================
# GKE CLUSTER
# ================================================

resource "google_container_cluster" "primary" {
  name     = "private-gke-cluster"
  location = "asia-south2-b"  # Single zone instead of regional

  # Use VPC-native cluster
  network    = google_compute_network.vpc.name
  subnetwork = google_compute_subnetwork.private_subnet.name

  # Private cluster configuration
  private_cluster_config {
    enable_private_nodes    = true
    enable_private_endpoint = true
    master_ipv4_cidr_block  = "172.16.0.0/28"
  }

  # Master authorized networks (allow bastion)
  master_authorized_networks_config {
    cidr_blocks {
      cidr_block   = "10.0.2.0/24" # Public subnet
      display_name = "bastion-subnet"
    }
  }

  # IP allocation for pods and services
  ip_allocation_policy {
    cluster_ipv4_cidr_block  = "10.4.0.0/14"
    services_ipv4_cidr_block = "10.8.0.0/20"
  }

  # Workload Identity
  workload_identity_config {
    workload_pool = "${var.project_id}.svc.id.goog"
  }

  # Disable deletion protection for easy cleanup
  deletion_protection = false

  # Remove default node pool
  remove_default_node_pool = true
  initial_node_count       = 1

  # Addons
  addons_config {
    http_load_balancing {
      disabled = false
    }
  }
}

# Node Pool
resource "google_container_node_pool" "primary_nodes" {
  name       = "default-pool"
  location   = "asia-south2-b"  # Match cluster zone
  cluster    = google_container_cluster.primary.name
  node_count = 2

  autoscaling {
    min_node_count = 2
    max_node_count = 4
  }

  node_config {
    machine_type = "e2-medium"
    disk_size_gb = 15
    disk_type    = "pd-standard"

    oauth_scopes = [
      "https://www.googleapis.com/auth/cloud-platform"
    ]

    workload_metadata_config {
      mode = "GKE_METADATA"
    }

    tags = ["gke-node"]
  }
}

# ================================================
# SERVICE ACCOUNTS
# ================================================

# Backend Service Account
resource "google_service_account" "backend_sa" {
  account_id   = "backend-gke-sa"
  display_name = "Backend GKE Service Account"
}

# IAM Bindings for Backend SA
resource "google_project_iam_member" "backend_cloudsql" {
  project = var.project_id
  role    = "roles/cloudsql.client"
  member  = "serviceAccount:${google_service_account.backend_sa.email}"
}

resource "google_project_iam_member" "backend_redis" {
  project = var.project_id
  role    = "roles/redis.editor"
  member  = "serviceAccount:${google_service_account.backend_sa.email}"
}

resource "google_pubsub_subscription_iam_member" "backend_pubsub_subscriber" {
  subscription = google_pubsub_subscription.subscription.name
  role         = "roles/pubsub.subscriber"
  member       = "serviceAccount:${google_service_account.backend_sa.email}"
}

resource "google_pubsub_topic_iam_member" "backend_pubsub_publisher" {
  topic  = google_pubsub_topic.topic.name
  role   = "roles/pubsub.publisher"
  member = "serviceAccount:${google_service_account.backend_sa.email}"
}

resource "google_storage_bucket_iam_member" "backend_storage" {
  bucket = google_storage_bucket.bucket.name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:${google_service_account.backend_sa.email}"
}

# Workload Identity Binding
resource "google_service_account_iam_member" "backend_workload_identity" {
  service_account_id = google_service_account.backend_sa.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "serviceAccount:${var.project_id}.svc.id.goog[default/backend-sa]"
}

# Bastion Service Account
resource "google_service_account" "bastion_sa" {
  account_id   = "bastion-sa"
  display_name = "Bastion VM Service Account"
}

resource "google_project_iam_member" "bastion_gke" {
  project = var.project_id
  role    = "roles/container.developer"
  member  = "serviceAccount:${google_service_account.bastion_sa.email}"
}

# ================================================
# BASTION VM
# ================================================

resource "google_compute_address" "bastion_ip" {
  name = "bastion-ip"
}

resource "google_compute_instance" "bastion" {
  name         = "dot-net-bastion-vm"
  machine_type = "e2-small"
  zone         = "${var.region}-b"

  tags = ["bastion"]

  boot_disk {
    initialize_params {
      image = "ubuntu-os-cloud/ubuntu-2204-lts"
      size  = 20
    }
  }

  network_interface {
    network    = google_compute_network.vpc.name
    subnetwork = google_compute_subnetwork.public_subnet.name
    
    access_config {
      nat_ip = google_compute_address.bastion_ip.address
    }
  }

  service_account {
    email  = google_service_account.bastion_sa.email
    scopes = ["cloud-platform"]
  }

  metadata_startup_script = <<-EOF
    #!/bin/bash
    
    # Update system
    apt-get update
    apt-get install -y apt-transport-https ca-certificates curl software-properties-common nginx
    
    # Install Docker
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg | apt-key add -
    add-apt-repository "deb [arch=amd64] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable"
    apt-get update
    apt-get install -y docker-ce docker-ce-cli containerd.io
    
    # Install kubectl
    curl -LO "https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl"
    install -o root -g root -m 0755 kubectl /usr/local/bin/kubectl
    
    # Install gcloud
    echo "deb [signed-by=/usr/share/keyrings/cloud.google.gpg] https://packages.cloud.google.com/apt cloud-sdk main" | tee -a /etc/apt/sources.list.d/google-cloud-sdk.list
    curl https://packages.cloud.google.com/apt/doc/apt-key.gpg | apt-key --keyring /usr/share/keyrings/cloud.google.gpg add -
    apt-get update && apt-get install -y google-cloud-sdk google-cloud-sdk-gke-gcloud-auth-plugin
    
    # Configure kubectl to connect to GKE
    gcloud container clusters get-credentials ${google_container_cluster.primary.name} --region=${var.region}
    
    # Configure Nginx as reverse proxy
    cat > /etc/nginx/sites-available/default <<'NGINX'
    server {
        listen 80 default_server;
        server_name _;

        # Proxy frontend
        location / {
            proxy_pass http://10.0.2.9/;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        }

        # Proxy backend API
        location /api/ {
            proxy_pass http://10.0.2.9/api/;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        }
    }
NGINX
    
    systemctl restart nginx
    systemctl enable nginx
  EOF

  depends_on = [google_container_cluster.primary]
}
