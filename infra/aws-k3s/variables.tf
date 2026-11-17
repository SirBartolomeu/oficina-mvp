variable "aws_region" {
  description = "Região única da demonstração AWS."
  type        = string
}

variable "name_prefix" {
  description = "Mesmo prefixo usado no estado de dados."
  type        = string
  default     = "oficina-fiap-demo"

  validation {
    condition     = can(regex("^[a-z0-9-]{3,40}$", var.name_prefix))
    error_message = "Use 3 a 40 letras minúsculas, números ou hífens."
  }
}

variable "data_state_bucket" {
  description = "Bucket S3 privado do estado de dados, criado antes do apply."
  type        = string
}

variable "data_state_key" {
  description = "Chave do state de dados no mesmo bucket."
  type        = string
  default     = "oficina-fiap-demo/data.tfstate"
}

variable "k3s_instance_type" {
  description = "Instância x86 para a janela curta de demonstração."
  type        = string
  default     = "t3.large"
}

variable "k3s_version" {
  description = "Versão fixada para reproduzir o bootstrap do K3s."
  type        = string
  default     = "v1.36.6+k3s1"
}

variable "github_owner" {
  description = "Usuário ou organização proprietária dos repositórios GitHub."
  type        = string

  validation {
    condition     = can(regex("^[A-Za-z0-9-]{1,39}$", var.github_owner))
    error_message = "Informe o owner GitHub sem URL ou barra."
  }
}

variable "github_repositories" {
  description = "Nomes dos repositórios de cada serviço."
  type        = map(string)
  default = {
    os        = "oficina-os"
    execution = "oficina-execution"
    billing   = "oficina-billing"
  }

  validation {
    condition     = length(var.github_repositories) == 3 && alltrue([for name in ["os", "execution", "billing"] : contains(keys(var.github_repositories), name)])
    error_message = "Defina exatamente os repositórios os, execution e billing."
  }
}

variable "github_auth_repository" {
  description = "Repositório independente da Function Auth."
  type        = string
  default     = "oficina-auth-lambda"
}
