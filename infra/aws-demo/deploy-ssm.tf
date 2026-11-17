locals {
  deployments = {
    os        = "oficina-api"
    execution = "execution-api"
    billing   = "billing-api"
  }
}

resource "aws_ssm_document" "deploy_service" {
  for_each        = local.services
  name            = "${var.name_prefix}-${each.key}-deploy"
  document_type   = "Command"
  document_format = "JSON"
  content = jsonencode({
    schemaVersion = "2.2"
    description   = "Atualiza apenas o Deployment ${local.deployments[each.key]} no K3s da demo"
    parameters = {
      imageTag = {
        type           = "String"
        allowedPattern = "^[0-9a-f]{40}$"
      }
    }
    mainSteps = [{
      action = "aws:runShellScript"
      name   = "deploy"
      inputs = {
        timeoutSeconds = "360"
        runCommand = [
          "set -eu",
          "/usr/local/bin/k3s kubectl -n oficina set image deployment/${local.deployments[each.key]} api=${aws_ecr_repository.service[each.key].repository_url}:{{ imageTag }}",
          "/usr/local/bin/k3s kubectl -n oficina rollout status deployment/${local.deployments[each.key]} --timeout=300s",
          "/usr/local/bin/k3s kubectl -n oficina get deployment/${local.deployments[each.key]} -o wide"
        ]
      }
    }]
  })
}

data "aws_iam_policy_document" "github_ssm_deploy" {
  for_each = local.services

  statement {
    actions   = ["ssm:SendCommand"]
    resources = [aws_ssm_document.deploy_service[each.key].arn, aws_instance.k3s.arn]
  }
  statement {
    actions   = ["ssm:GetCommandInvocation"]
    resources = ["*"]
  }
}

resource "aws_iam_role_policy" "github_ssm_deploy" {
  for_each = local.services
  name     = "${var.name_prefix}-${each.key}-ssm-deploy"
  role     = aws_iam_role.github_push[each.key].id
  policy   = data.aws_iam_policy_document.github_ssm_deploy[each.key].json
}
