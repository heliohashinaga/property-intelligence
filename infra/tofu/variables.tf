variable "hetzner_token" {
  description = "Hetzner Cloud API token (Settings → API Tokens → Read+Write)."
  type        = string
  sensitive   = true
}

variable "cloudflare_api_token" {
  description = "Cloudflare API token with Zone:Edit + DNS:Edit permissions."
  type        = string
  sensitive   = true
}

variable "cloudflare_zone_id" {
  description = "Cloudflare Zone ID for the domain (found in the domain overview page)."
  type        = string
}

variable "api_subdomain" {
  description = "DNS subdomain for the API endpoint (e.g. 'api' → api.yourdomain.com)."
  type        = string
  default     = "api"
}

variable "environment" {
  description = "Deployment environment label (prod, staging, dev)."
  type        = string
  default     = "prod"
}

variable "server_type" {
  description = "Hetzner server type. CX31 (4 vCPU, 8 GB RAM) for production."
  type        = string
  default     = "cx31"
}

variable "hetzner_location" {
  description = "Hetzner datacenter location (fsn1 = Falkenstein, nbg1 = Nuremberg, hel1 = Helsinki)."
  type        = string
  default     = "nbg1"
}
