output "aws_region" {
  value = var.aws_region
}

output "k3s_instance_id" {
  value = aws_instance.k3s.id
}

output "github_ecr_push_role_arns" {
  value = { for name, role in aws_iam_role.github_push : name => role.arn }
}

output "workload_role_arns" {
  value = { for name, role in aws_iam_role.workload : name => role.arn }
}

output "ssm_deploy_document_names" {
  value = { for name, document in aws_ssm_document.deploy_service : name => document.name }
}

output "deployment_assets_bucket" {
  value = aws_s3_bucket.deployment_assets.bucket
}

output "auth_lambda_name" {
  value = aws_lambda_function.auth.function_name
}

output "github_auth_deploy_role_arn" {
  value = aws_iam_role.github_auth_deploy.arn
}

output "auth_token_url" {
  value = "${aws_apigatewayv2_api.auth.api_endpoint}/auth/token"
}

# Bootstrap packaging reads one consolidated output set from the K3s state.
output "rds_endpoint" {
  value = local.data_outputs.rds_endpoint
}

output "rds_master_secret_arn" {
  value = local.data_outputs.rds_master_secret_arn
}

output "application_secret_arns" {
  value = local.data_outputs.application_secret_arns
}

output "ecr_repository_names" {
  value = local.data_outputs.ecr_repository_names
}

output "ecr_repositories" {
  value = local.data_outputs.ecr_repositories
}

output "queue_urls" {
  value = local.data_outputs.queue_urls
}

output "billing_table_name" {
  value = local.data_outputs.billing_table_name
}
