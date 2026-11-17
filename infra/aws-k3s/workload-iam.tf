locals {
  sqs_send_arns = {
    os        = [local.data_outputs.queue_arns["billing_workorders"], local.data_outputs.queue_arns["execution_commands"]]
    execution = [local.data_outputs.queue_arns["os_events"]]
    billing   = [local.data_outputs.queue_arns["os_events"]]
  }
  sqs_receive_arns = {
    os        = [local.data_outputs.queue_arns["os_events"]]
    execution = [local.data_outputs.queue_arns["execution_commands"]]
    billing   = [local.data_outputs.queue_arns["billing_workorders"]]
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
    resources = [local.data_outputs.billing_table_arn]
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
    resources = values(local.data_outputs.ecr_repository_arns)
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
