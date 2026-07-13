terraform {
  required_version = ">= 1.6.0"

  required_providers {
    kind = {
      source  = "tehcyx/kind"
      version = "~> 0.5"
    }
  }
}

provider "kind" {}

variable "cluster_name" {
  description = "Nome do cluster Kubernetes local criado com kind."
  type        = string
  default     = "oficina-local"
}

variable "api_host_port" {
  description = "Porta local reservada para demos via ingress/node port quando aplicavel."
  type        = number
  default     = 8080
}

resource "kind_cluster" "oficina" {
  name           = var.cluster_name
  wait_for_ready = true

  kind_config {
    kind        = "Cluster"
    api_version = "kind.x-k8s.io/v1alpha4"

    node {
      role = "control-plane"

      extra_port_mappings {
        container_port = 80
        host_port      = var.api_host_port
        protocol       = "TCP"
      }
    }
  }
}

output "cluster_name" {
  value       = kind_cluster.oficina.name
  description = "Nome do cluster kind criado."
}

output "kubectl_context" {
  value       = "kind-${kind_cluster.oficina.name}"
  description = "Contexto kubectl usado para aplicar os manifests."
}

output "next_steps" {
  value = [
    "kubectl config use-context kind-${kind_cluster.oficina.name}",
    "kubectl apply -k ../../k8s",
    "kubectl -n oficina rollout status deploy/oficina-api --timeout=120s"
  ]
}
