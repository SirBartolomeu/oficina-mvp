output "ecr_repositories" {
  description = "URIs das imagens imutáveis dos três serviços."
  value       = { for name, repo in aws_ecr_repository.service : name => repo.repository_url }
}

output "ecr_repository_names" {
  description = "Nomes a configurar nas variables ECR_REPOSITORY_* dos workflows."
  value       = { for name, repo in aws_ecr_repository.service : name => repo.name }
}

output "queue_urls" {
  description = "URLs a injetar nos ConfigMaps de OS, Execução e Billing."
  value       = { for name, queue in aws_sqs_queue.main : name => queue.id }
}

output "billing_table_name" {
  description = "Tabela própria do Billing."
  value       = aws_dynamodb_table.billing.name
}

output "rds_endpoint" {
  description = "Endpoint privado; criar os bancos e usuários próprios de OS e Execução no bootstrap."
  value       = aws_db_instance.postgres.address
}

output "rds_master_secret_arn" {
  description = "Segredo gerenciado pelo RDS; não imprimir o valor da senha."
  value       = aws_db_instance.postgres.master_user_secret[0].secret_arn
}

output "k3s_security_group_id" {
  description = "Security group para a futura instância EC2/K3s."
  value       = aws_security_group.k3s.id
}

output "k3s_subnet_id" {
  description = "Subnet pública para o futuro nó K3s acessado via SSM."
  value       = aws_subnet.public.id
}

output "k3s_instance_id" {
  description = "Usar no Session Manager ou Run Command; não há SSH público."
  value       = aws_instance.k3s.id
}

output "github_ecr_push_role_arns" {
  description = "Configurar cada ARN como variável do workflow GitHub correspondente."
  value       = { for name, role in aws_iam_role.github_push : name => role.arn }
}

output "workload_role_arns" {
  description = "Roles temporárias distintas para OS, Execução e Billing no K3s."
  value       = { for name, role in aws_iam_role.workload : name => role.arn }
}

output "application_secret_arns" {
  description = "Segredos vazios; o bootstrap do RDS grava os valores fora do Terraform state."
  value       = { for name, secret in aws_secretsmanager_secret.application : name => secret.arn }
}

output "ssm_deploy_document_names" {
  description = "Documentos SSM restritos aos Deployments de cada serviço."
  value       = { for name, document in aws_ssm_document.deploy_service : name => document.name }
}

output "deployment_assets_bucket" {
  description = "Bucket privado para o pacote inicial de scripts e manifests; objetos expiram em dois dias."
  value       = aws_s3_bucket.deployment_assets.bucket
}

output "aws_region" {
  description = "Região única usada pelo bootstrap."
  value       = var.aws_region
}

output "auth_lambda_name" {
  description = "Function de CPF; bootstrap de ambiente deve injetar JWT e token de consulta antes do primeiro uso."
  value       = aws_lambda_function.auth.function_name
}

output "github_auth_deploy_role_arn" {
  description = "Role OIDC de deploy da Function no repositório Auth."
  value       = aws_iam_role.github_auth_deploy.arn
}

output "auth_token_url" {
  description = "URL publica do POST /auth/token da HTTP API."
  value       = "${aws_apigatewayv2_api.auth.api_endpoint}/auth/token"
}
