data "aws_iam_policy_document" "k3s_bootstrap_secrets" {
  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [local.data_outputs.rds_master_secret_arn]
  }
  statement {
    actions   = ["secretsmanager:GetSecretValue", "secretsmanager:PutSecretValue"]
    resources = values(local.data_outputs.application_secret_arns)
  }
}

resource "aws_iam_role_policy" "k3s_bootstrap_secrets" {
  name   = "${var.name_prefix}-k3s-bootstrap-secrets"
  role   = aws_iam_role.k3s_host.id
  policy = data.aws_iam_policy_document.k3s_bootstrap_secrets.json
}
