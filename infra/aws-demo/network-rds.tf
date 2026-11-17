data "aws_availability_zones" "available" {
  state = "available"
}

locals {
  private_subnets = {
    first  = { cidr = "10.42.10.0/24", az = data.aws_availability_zones.available.names[0] }
    second = { cidr = "10.42.11.0/24", az = data.aws_availability_zones.available.names[1] }
  }
}

resource "aws_vpc" "demo" {
  cidr_block           = "10.42.0.0/16"
  enable_dns_support   = true
  enable_dns_hostnames = true
  tags                 = { Name = "${var.name_prefix}-vpc" }
}

resource "aws_internet_gateway" "demo" {
  vpc_id = aws_vpc.demo.id
  tags   = { Name = "${var.name_prefix}-igw" }
}

resource "aws_subnet" "public" {
  vpc_id                  = aws_vpc.demo.id
  cidr_block              = "10.42.0.0/24"
  availability_zone       = data.aws_availability_zones.available.names[0]
  map_public_ip_on_launch = true
  tags                    = { Name = "${var.name_prefix}-public" }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.demo.id
  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.demo.id
  }
  tags = { Name = "${var.name_prefix}-public" }
}

resource "aws_route_table_association" "public" {
  subnet_id      = aws_subnet.public.id
  route_table_id = aws_route_table.public.id
}

resource "aws_subnet" "private" {
  for_each          = local.private_subnets
  vpc_id            = aws_vpc.demo.id
  cidr_block        = each.value.cidr
  availability_zone = each.value.az
  tags              = { Name = "${var.name_prefix}-private-${each.key}" }
}

resource "aws_security_group" "k3s" {
  name        = "${var.name_prefix}-k3s"
  description = "No K3s; sem entrada publica ate configurar TLS e ingress"
  vpc_id      = aws_vpc.demo.id
  tags        = { Name = "${var.name_prefix}-k3s" }
}

resource "aws_vpc_security_group_egress_rule" "k3s_https" {
  security_group_id = aws_security_group.k3s.id
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
  description       = "AWS APIs, ECR e downloads HTTPS sem NAT Gateway"
}

resource "aws_vpc_security_group_egress_rule" "k3s_http" {
  security_group_id = aws_security_group.k3s.id
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 80
  to_port           = 80
  description       = "Downloads HTTP de bootstrap; restringir apos instalar K3s"
}

resource "aws_vpc_security_group_egress_rule" "k3s_postgres" {
  security_group_id            = aws_security_group.k3s.id
  referenced_security_group_id = aws_security_group.rds.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
  description                  = "Acesso privado ao PostgreSQL"
}

resource "aws_security_group" "rds" {
  name        = "${var.name_prefix}-rds"
  description = "PostgreSQL somente para o no K3s"
  vpc_id      = aws_vpc.demo.id
  tags        = { Name = "${var.name_prefix}-rds" }
}

resource "aws_vpc_security_group_ingress_rule" "rds_from_k3s" {
  security_group_id            = aws_security_group.rds.id
  referenced_security_group_id = aws_security_group.k3s.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
  description                  = "PostgreSQL privado para os servicos no K3s"
}

resource "aws_db_subnet_group" "demo" {
  name       = "${var.name_prefix}-db"
  subnet_ids = [for subnet in aws_subnet.private : subnet.id]
  tags       = { Name = "${var.name_prefix}-db" }
}

resource "aws_db_instance" "postgres" {
  identifier                  = "${var.name_prefix}-postgres"
  engine                      = "postgres"
  instance_class              = var.rds_instance_class
  allocated_storage           = 20
  storage_type                = "gp3"
  storage_encrypted           = true
  username                    = "oficina_admin"
  manage_master_user_password = true
  db_subnet_group_name        = aws_db_subnet_group.demo.name
  vpc_security_group_ids      = [aws_security_group.rds.id]
  publicly_accessible         = false
  multi_az                    = false
  backup_retention_period     = 0
  deletion_protection         = false
  skip_final_snapshot         = true
  apply_immediately           = true
  auto_minor_version_upgrade  = false
  tags                        = { Name = "${var.name_prefix}-postgres" }
}
