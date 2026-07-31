output "server_ip" {
  description = "Public IPv4 address of the API server."
  value       = hcloud_server.api.ipv4_address
}

output "server_ipv6" {
  description = "Public IPv6 address of the API server."
  value       = hcloud_server.api.ipv6_address
}

output "ssh_private_key_path" {
  description = "Path to the generated SSH private key for manual access."
  value       = local_sensitive_file.private_key.filename
  sensitive   = true
}

output "kubeconfig_command" {
  description = "SSH command to retrieve the k3s kubeconfig from the server."
  value       = "ssh -i ${local_sensitive_file.private_key.filename} root@${hcloud_server.api.ipv4_address} cat /root/.kube/config"
}

output "api_url" {
  description = "Public URL for the Property Intelligence API."
  value       = "https://${var.api_subdomain}.${data.cloudflare_zone.current.name}"
}

data "cloudflare_zone" "current" {
  zone_id = var.cloudflare_zone_id
}
