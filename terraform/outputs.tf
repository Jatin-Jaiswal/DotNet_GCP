# ================================================
# Terraform Outputs
# ================================================

output "bastion_external_ip" {
  description = "External IP address of the bastion VM"
  value       = google_compute_instance.bastion.network_interface[0].access_config[0].nat_ip
}

output "backend_ilb_ip" {
  description = "Internal Load Balancer IP for backend"
  value       = "10.0.2.9"
}

output "cloudsql_private_ip" {
  description = "Private IP address of Cloud SQL"
  value       = google_sql_database_instance.postgres.private_ip_address
}

output "redis_host" {
  description = "Redis instance host"
  value       = google_redis_instance.redis.host
}

output "redis_port" {
  description = "Redis instance port"
  value       = google_redis_instance.redis.port
}

output "gke_cluster_name" {
  description = "GKE cluster name"
  value       = google_container_cluster.primary.name
}

output "gke_cluster_endpoint" {
  description = "GKE cluster endpoint"
  value       = google_container_cluster.primary.endpoint
  sensitive   = true
}

output "artifact_registry_repository" {
  description = "Artifact Registry repository URL"
  value       = google_artifact_registry_repository.docker_repo.name
}

output "pubsub_topic" {
  description = "Pub/Sub topic name"
  value       = google_pubsub_topic.topic.name
}

output "pubsub_subscription" {
  description = "Pub/Sub subscription name"
  value       = google_pubsub_subscription.subscription.name
}

output "storage_bucket" {
  description = "Cloud Storage bucket name"
  value       = google_storage_bucket.bucket.name
}

output "backend_service_account" {
  description = "Backend GKE service account email"
  value       = google_service_account.backend_sa.email
}

output "bastion_service_account" {
  description = "Bastion service account email"
  value       = google_service_account.bastion_sa.email
}
