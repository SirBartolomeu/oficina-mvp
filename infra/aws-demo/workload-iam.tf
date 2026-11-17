locals {
  sqs_send_arns = {
    os        = [aws_sqs_queue.main["billing_workorders"].arn, aws_sqs_queue.main["execution_commands"].arn]
    execution = [aws_sqs_queue.main["os_events"].arn]
    billing   = [aws_sqs_queue.main["os_events"].arn]
  }
  sqs_receive_arns = {
    os        = [aws_sqs_queue.main["os_events"].arn]
    execution = [aws_sqs_queue.main["execution_commands"].arn]
    billing   = [aws_sqs_queue.main["billing_workorders"].arn]
  }
}

data "aws_iam_policy_document" "host_assume_workload" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "AWS"
      identifiers = [aws_iam_role.k3s_host.arn]
    }
  }
}

resource "aws_iam_role" "workload" {
  for_each             = local.services
  name                 = "${var.name_prefix}-${each.key}-workload"
  assume_role_policy   = data.aws_iam_policy_document.host_assume_workload.json
  max_session_duration = 3600
}

data "aws_iam_policy_document" "workload_sqs" {
  for_each = local.services

  statement {
    actions   = ["sqs:SendMessage"]
    resources = local.sqs_send_arns[each.key]
  }
  statement {
    actions   = ["sqs:ReceiveMessage", "sqs:DeleteMessage"]
    resources = local.sqs_receive_arns[each.key]
  }
}

resource "aws_iam_role_policy" "workload_sqs" {
  for_each = local.services
  name     = "${var.name_prefix}-${each.key}-sqs"
  role     = aws_iam_role.workload[each.key].id
  policy   = data.aws_iam_policy_document.workload_sqs[each.key].json
}

data "aws_iam_policy_document" "billing_dynamodb" {
  statement {
    actions = [
      "dynamodb:DescribeTable",
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:UpdateItem",
      "dynamodb:DeleteItem",
      "dynamodb:Query",
      "dynamodb:TransactWriteItems"
    ]
    resources = [aws_dynamodb_table.billing.arn]
  }
}

resource "aws_iam_role_policy" "billing_dynamodb" {
  name   = "${var.name_prefix}-billing-dynamodb"
  role   = aws_iam_role.workload["billing"].id
  policy = data.aws_iam_policy_document.billing_dynamodb.json
}

data "aws_iam_policy_document" "k3s_host_pull_and_assume" {
  statement {
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }
  statement {
    actions   = ["ecr:BatchGetImage", "ecr:GetDownloadUrlForLayer", "ecr:DescribeImages"]
    resources = [for repo in aws_ecr_repository.service : repo.arn]
  }
  statement {
    actions   = ["sts:AssumeRole"]
    resources = [for role in aws_iam_role.workload : role.arn]
  }
}

resource "aws_iam_role_policy" "k3s_host_pull_and_assume" {
  name   = "${var.name_prefix}-k3s-pull-and-assume"
  role   = aws_iam_role.k3s_host.id
  policy = data.aws_iam_policy_document.k3s_host_pull_and_assume.json
}
