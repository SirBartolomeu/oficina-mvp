data "aws_iam_policy_document" "github_auth_assume" {
  statement {
    actions = ["sts:AssumeRoleWithWebIdentity"]
    principals {
      type        = "Federated"
      identifiers = [aws_iam_openid_connect_provider.github.arn]
    }
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:sub"
      values   = ["repo:${var.github_owner}/${var.github_auth_repository}:ref:refs/heads/main"]
    }
  }
}

resource "aws_iam_role" "github_auth_deploy" {
  name               = "${var.name_prefix}-auth-github-deploy"
  assume_role_policy = data.aws_iam_policy_document.github_auth_assume.json
}

data "aws_iam_policy_document" "github_auth_deploy" {
  statement {
    actions   = ["lambda:UpdateFunctionCode", "lambda:GetFunctionConfiguration"]
    resources = [aws_lambda_function.auth.arn]
  }
}

resource "aws_iam_role_policy" "github_auth_deploy" {
  name   = "${var.name_prefix}-auth-lambda-deploy"
  role   = aws_iam_role.github_auth_deploy.id
  policy = data.aws_iam_policy_document.github_auth_deploy.json
}
