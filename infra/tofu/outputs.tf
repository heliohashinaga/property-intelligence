output "server_ip" {
  description = "Public IPv4 address of the Hetzner CX31 server."
  value       = hcloud_server.property_intelligence.ipv4_address
}

output "server_id" {
  description = "Hetzner server ID."
  value       = hcloud_server.property_intelligence.id
}

output "api_url" {
  description = "Public API URL (via Cloudflare)."
  value       = "https://api.${var.domain_name}/v1/property/analyze"
}

output "kubeconfig_note" {
  description = "Retrieve kubeconfig from server after provisioning."
  value       = "ssh root@${hcloud_server.property_intelligence.ipv4_address} cat /etc/rancher/k3s/k3s.yaml"
}
