output "aws_region" {
  value = var.aws_region
}

output "name_prefix" {
  value = var.name_prefix
}

output "vpc_id" {
  value = aws_vpc.demo.id
}

output "public_subnet_id" {
  value = aws_subnet.public.id
}

output "private_subnet_ids" {
  value = { for name, subnet in aws_subnet.private : name => subnet.id }
}

output "k3s_security_group_id" {
  value = aws_security_group.k3s.id
}

output "rds_endpoint" {
  value = aws_db_instance.postgres.address
}

output "rds_master_secret_arn" {
  value = aws_db_instance.postgres.master_user_secret[0].secret_arn
}

output "billing_table_name" {
  value = aws_dynamodb_table.billing.name
}

output "billing_table_arn" {
  value = aws_dynamodb_table.billing.arn
}

output "queue_urls" {
  value = { for name, queue in aws_sqs_queue.main : name => queue.id }
}

output "queue_arns" {
  value = { for name, queue in aws_sqs_queue.main : name => queue.arn }
}

output "ecr_repositories" {
  value = { for name, repo in aws_ecr_repository.service : name => repo.repository_url }
}

output "ecr_repository_names" {
  value = { for name, repo in aws_ecr_repository.service : name => repo.name }
}

output "ecr_repository_arns" {
  value = { for name, repo in aws_ecr_repository.service : name => repo.arn }
}

output "application_secret_arns" {
  value = { for name, secret in aws_secretsmanager_secret.application : name => secret.arn }
}
