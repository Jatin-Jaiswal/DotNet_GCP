# ================================================
# Terraform Variables
# ================================================

variable "project_id" {
  description = "GCP Project ID"
  type        = string
  default     = "project-84d8bfc9-cd8e-4b3c-b15"
}

variable "region" {
  description = "GCP Region"
  type        = string
  default     = "asia-south2"
}

variable "db_password" {
  description = "PostgreSQL password"
  type        = string
  default     = "DotNet@123"
  sensitive   = true
}

variable "bucket_name" {
  description = "Cloud Storage bucket name (must be globally unique)"
  type        = string
  default     = "dot-net-bucket"
}
