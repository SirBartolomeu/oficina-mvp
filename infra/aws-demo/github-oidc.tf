resource "aws_iam_openid_connect_provider" "github" {
  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]
}

data "aws_iam_policy_document" "github_assume_role" {
  for_each = local.services

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
      values   = ["repo:${var.github_owner}/${var.github_repositories[each.key]}:ref:refs/heads/main"]
    }
  }
}

resource "aws_iam_role" "github_push" {
  for_each           = local.services
  name               = "${var.name_prefix}-${each.key}-github-push"
  assume_role_policy = data.aws_iam_policy_document.github_assume_role[each.key].json
}

data "aws_iam_policy_document" "ecr_push" {
  for_each = local.services

  statement {
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }
  statement {
    actions = [
      "ecr:BatchCheckLayerAvailability",
      "ecr:BatchGetImage",
      "ecr:CompleteLayerUpload",
      "ecr:DescribeImages",
      "ecr:InitiateLayerUpload",
      "ecr:PutImage",
      "ecr:UploadLayerPart"
    ]
    resources = [aws_ecr_repository.service[each.key].arn]
  }
}

resource "aws_iam_role_policy" "ecr_push" {
  for_each = local.services
  name     = "${var.name_prefix}-${each.key}-ecr-push"
  role     = aws_iam_role.github_push[each.key].id
  policy   = data.aws_iam_policy_document.ecr_push[each.key].json
}
