variable "hetzner_token" {
  description = "Hetzner Cloud API token (read/write). Set via TF_VAR_hetzner_token or .tfvars."
  type        = string
  sensitive   = true
}

variable "cloudflare_api_token" {
  description = "Cloudflare API token with Zone:DNS:Edit permission."
  type        = string
  sensitive   = true
}

variable "cloudflare_zone_id" {
  description = "Cloudflare Zone ID for the target domain (found in Cloudflare dashboard)."
  type        = string
}

variable "domain_name" {
  description = "Base domain name, e.g. example.com. API will be at api.<domain_name>."
  type        = string
  default     = "property-intelligence.dev"
}
