variable "aws_region" {
  description = "Região única da demonstração AWS."
  type        = string
}

variable "name_prefix" {
  description = "Prefixo compartilhado pelos estados de dados e K3s."
  type        = string
  default     = "oficina-fiap-demo"

  validation {
    condition     = can(regex("^[a-z0-9-]{3,40}$", var.name_prefix))
    error_message = "Use 3 a 40 letras minúsculas, números ou hífens."
  }
}

variable "rds_instance_class" {
  description = "Classe RDS Single-AZ; confirmar disponibilidade e preço na região."
  type        = string
  default     = "db.t4g.micro"
}
