resource "aws_secretsmanager_secret" "application" {
  for_each                = local.services
  name                    = "${var.name_prefix}/${each.key}/application"
  description             = "Configuracao privada do servico ${each.key} na demo FIAP"
  recovery_window_in_days = 0
}

data "aws_iam_policy_document" "k3s_bootstrap_secrets" {
  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_db_instance.postgres.master_user_secret[0].secret_arn]
  }
  statement {
    actions   = ["secretsmanager:GetSecretValue", "secretsmanager:PutSecretValue"]
    resources = [for secret in aws_secretsmanager_secret.application : secret.arn]
  }
}

resource "aws_iam_role_policy" "k3s_bootstrap_secrets" {
  name   = "${var.name_prefix}-k3s-bootstrap-secrets"
  role   = aws_iam_role.k3s_host.id
  policy = data.aws_iam_policy_document.k3s_bootstrap_secrets.json
}
