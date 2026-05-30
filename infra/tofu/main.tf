terraform {
  required_version = ">= 1.6"
  required_providers {
    hcloud = {
      source  = "hetznercloud/hcloud"
      version = "~> 1.45"
    }
    cloudflare = {
      source  = "cloudflare/cloudflare"
      version = "~> 4.0"
    }
  }
}

provider "hcloud" {
  token = var.hetzner_token
}

provider "cloudflare" {
  api_token = var.cloudflare_api_token
}

# ── SSH Key ──────────────────────────────────────────────────────────────────
resource "hcloud_ssh_key" "property_intelligence" {
  name       = "property-intelligence-deploy"
  public_key = file("~/.ssh/id_ed25519.pub")
}

# ── Firewall ─────────────────────────────────────────────────────────────────
resource "hcloud_firewall" "property_intelligence" {
  name = "property-intelligence-fw"

  # SSH (restrict to your IP in production)
  rule {
    direction = "in"
    port      = "22"
    protocol  = "tcp"
    source_ips = ["0.0.0.0/0", "::/0"]
  }

  # HTTP (Cloudflare Tunnel only — WAF handles public HTTPS)
  rule {
    direction = "in"
    port      = "80"
    protocol  = "tcp"
    source_ips = ["0.0.0.0/0", "::/0"]
  }

  # HTTPS
  rule {
    direction = "in"
    port      = "443"
    protocol  = "tcp"
    source_ips = ["0.0.0.0/0", "::/0"]
  }

  # K3s API server
  rule {
    direction = "in"
    port      = "6443"
    protocol  = "tcp"
    source_ips = ["0.0.0.0/0", "::/0"]
  }
}

# ── VPS: Hetzner CX31 (4 vCPU, 8 GB RAM, Ubuntu 24.04, Falkenstein) ─────────
resource "hcloud_server" "property_intelligence" {
  name         = "property-intelligence"
  server_type  = "cx31"
  image        = "ubuntu-24.04"
  location     = "fsn1"  # Falkenstein, Germany
  ssh_keys     = [hcloud_ssh_key.property_intelligence.id]
  firewall_ids = [hcloud_firewall.property_intelligence.id]

  user_data = <<-EOF
    #!/bin/bash
    set -e

    # ── System update ─────────────────────────────────────────────────────────
    apt-get update -y && apt-get upgrade -y

    # ── K3s (lightweight Kubernetes) ──────────────────────────────────────────
    curl -sfL https://get.k3s.io | INSTALL_K3S_VERSION="v1.30.4+k3s1" sh -
    sleep 10

    # ── Cloudflare Tunnel (cloudflared) ───────────────────────────────────────
    curl -fsSL https://pkg.cloudflare.com/cloudflare-main.gpg \
      | gpg --dearmor -o /usr/share/keyrings/cloudflare-archive-keyring.gpg
    echo "deb [signed-by=/usr/share/keyrings/cloudflare-archive-keyring.gpg] \
      https://pkg.cloudflare.com/cloudflared $(lsb_release -cs) main" \
      > /etc/apt/sources.list.d/cloudflared.list
    apt-get update && apt-get install -y cloudflared

    # ── kubectl alias for convenience ─────────────────────────────────────────
    echo "export KUBECONFIG=/etc/rancher/k3s/k3s.yaml" >> /etc/environment
  EOF

  labels = {
    project     = "property-intelligence"
    environment = "production"
    managed_by  = "opentofu"
  }
}

# ── Cloudflare DNS A Record ───────────────────────────────────────────────────
resource "cloudflare_record" "property_intelligence" {
  zone_id = var.cloudflare_zone_id
  name    = "api"
  type    = "A"
  value   = hcloud_server.property_intelligence.ipv4_address
  ttl     = 1      # Auto (proxied)
  proxied = true   # Enable Cloudflare WAF + TLS termination
}
