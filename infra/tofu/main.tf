terraform {
  required_version = ">= 1.6.0"

  required_providers {
    hcloud = {
      source  = "hetznercloud/hcloud"
      version = "~> 1.47"
    }
    cloudflare = {
      source  = "cloudflare/cloudflare"
      version = "~> 4.36"
    }
    local = {
      source  = "hashicorp/local"
      version = "~> 2.5"
    }
    tls = {
      source  = "hashicorp/tls"
      version = "~> 4.0"
    }
  }

  # Uncomment to store state in Hetzner Object Storage (S3-compatible)
  # backend "s3" {
  #   bucket                      = "property-intelligence-tfstate"
  #   key                         = "prod/terraform.tfstate"
  #   region                      = "eu-central-1"
  #   endpoints = { s3 = "https://fsn1.your-objectstorage.com" }
  #   skip_credentials_validation = true
  #   skip_metadata_api_check     = true
  #   skip_region_validation      = true
  #   force_path_style            = true
  # }
}

# ── Providers ────────────────────────────────────────────────────────────────

provider "hcloud" {
  token = var.hetzner_token
}

provider "cloudflare" {
  api_token = var.cloudflare_api_token
}

# ── SSH key ──────────────────────────────────────────────────────────────────

resource "tls_private_key" "deploy_key" {
  algorithm = "ED25519"
}

resource "hcloud_ssh_key" "deploy_key" {
  name       = "property-intelligence-deploy"
  public_key = tls_private_key.deploy_key.public_key_openssh
}

resource "local_sensitive_file" "private_key" {
  content         = tls_private_key.deploy_key.private_key_openssh
  filename        = "${path.module}/../.ssh/deploy_key"
  file_permission = "0600"
}

# ── Server ───────────────────────────────────────────────────────────────────

resource "hcloud_server" "api" {
  name        = "property-intelligence-api"
  server_type = var.server_type
  image       = "ubuntu-24.04"
  location    = var.hetzner_location
  ssh_keys    = [hcloud_ssh_key.deploy_key.id]

  labels = {
    project     = "property-intelligence"
    environment = var.environment
  }

  user_data = <<-EOT
    #!/bin/bash
    set -e

    # System updates
    apt-get update -y
    apt-get install -y curl git jq

    # Install k3s (single-node, no traefik — Cloudflare Tunnel handles ingress)
    curl -sfL https://get.k3s.io | \
      INSTALL_K3S_EXEC="server --disable traefik --disable servicelb --node-name api-node" \
      sh -

    # Wait for k3s to be ready
    until kubectl get nodes 2>/dev/null | grep -q " Ready"; do sleep 5; done

    # Create property-intelligence namespace
    kubectl create namespace property-intelligence --dry-run=client -o yaml | kubectl apply -f -

    # Copy kubeconfig to a world-readable path for the deploy workflow
    mkdir -p /root/.kube
    cp /etc/rancher/k3s/k3s.yaml /root/.kube/config
    chmod 600 /root/.kube/config
  EOT
}

# ── Firewall ─────────────────────────────────────────────────────────────────

resource "hcloud_firewall" "api" {
  name = "property-intelligence-firewall"

  # Allow SSH from anywhere (tighten in production with known IP ranges)
  rule {
    direction = "in"
    protocol  = "tcp"
    port      = "22"
    source_ips = ["0.0.0.0/0", "::/0"]
  }

  # Allow k3s API server (for kubectl from GitHub Actions)
  rule {
    direction = "in"
    protocol  = "tcp"
    port      = "6443"
    source_ips = ["0.0.0.0/0", "::/0"]
  }

  # Allow HTTP/HTTPS (Cloudflare Tunnel uses outbound only, but keep open for health checks)
  rule {
    direction = "in"
    protocol  = "tcp"
    port      = "80"
    source_ips = ["0.0.0.0/0", "::/0"]
  }

  rule {
    direction = "in"
    protocol  = "tcp"
    port      = "443"
    source_ips = ["0.0.0.0/0", "::/0"]
  }
}

resource "hcloud_firewall_attachment" "api" {
  firewall_id = hcloud_firewall.api.id
  server_ids  = [hcloud_server.api.id]
}

# ── DNS ───────────────────────────────────────────────────────────────────────

resource "cloudflare_record" "api" {
  zone_id = var.cloudflare_zone_id
  name    = var.api_subdomain
  type    = "A"
  value   = hcloud_server.api.ipv4_address
  proxied = true
  ttl     = 1
}
