resource "aws_security_group" "auth_lambda" {
  name        = "${var.name_prefix}-auth-lambda"
  description = "Lambda CPF: saida apenas para consulta privada no K3s"
  vpc_id      = local.data_outputs.vpc_id
  tags        = { Name = "${var.name_prefix}-auth-lambda" }
}

resource "aws_vpc_security_group_egress_rule" "auth_to_k3s" {
  security_group_id            = aws_security_group.auth_lambda.id
  referenced_security_group_id = local.data_outputs.k3s_security_group_id
  ip_protocol                  = "tcp"
  from_port                    = 30080
  to_port                      = 30080
  description                  = "Consulta privada de CPF na OS"
}

resource "aws_security_group" "auth_secrets_endpoint" {
  name        = "${var.name_prefix}-auth-secrets-endpoint"
  description = "Secrets Manager privado para a Lambda CPF"
  vpc_id      = local.data_outputs.vpc_id
  tags        = { Name = "${var.name_prefix}-auth-secrets-endpoint" }
}

resource "aws_vpc_security_group_egress_rule" "auth_to_secrets_endpoint" {
  security_group_id            = aws_security_group.auth_lambda.id
  referenced_security_group_id = aws_security_group.auth_secrets_endpoint.id
  ip_protocol                  = "tcp"
  from_port                    = 443
  to_port                      = 443
  description                  = "Leitura privada do segredo da OS"
}

resource "aws_vpc_security_group_ingress_rule" "secrets_endpoint_from_auth" {
  security_group_id            = aws_security_group.auth_secrets_endpoint.id
  referenced_security_group_id = aws_security_group.auth_lambda.id
  ip_protocol                  = "tcp"
  from_port                    = 443
  to_port                      = 443
  description                  = "Secrets Manager somente da Lambda CPF"
}

resource "aws_vpc_endpoint" "auth_secrets" {
  vpc_id              = local.data_outputs.vpc_id
  service_name        = "com.amazonaws.${var.aws_region}.secretsmanager"
  vpc_endpoint_type   = "Interface"
  private_dns_enabled = true
  subnet_ids          = [local.data_outputs.private_subnet_ids["first"]]
  security_group_ids  = [aws_security_group.auth_secrets_endpoint.id]
  tags                = { Name = "${var.name_prefix}-auth-secrets" }
}

resource "aws_vpc_security_group_ingress_rule" "k3s_from_auth" {
  security_group_id            = local.data_outputs.k3s_security_group_id
  referenced_security_group_id = aws_security_group.auth_lambda.id
  ip_protocol                  = "tcp"
  from_port                    = 30080
  to_port                      = 30080
  description                  = "NodePort de consulta CPF somente da Lambda"
}

data "aws_iam_policy_document" "lambda_assume_role" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "auth_lambda" {
  name               = "${var.name_prefix}-auth-lambda"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

resource "aws_cloudwatch_log_group" "auth_lambda" {
  name              = "/aws/lambda/${var.name_prefix}-auth"
  retention_in_days = 1
}

data "aws_iam_policy_document" "auth_lambda" {
  statement {
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
    resources = ["${aws_cloudwatch_log_group.auth_lambda.arn}:*"]
  }
  statement {
    actions = [
      "ec2:CreateNetworkInterface",
      "ec2:DescribeNetworkInterfaces",
      "ec2:DeleteNetworkInterface",
      "ec2:AssignPrivateIpAddresses",
      "ec2:UnassignPrivateIpAddresses"
    ]
    resources = ["*"]
  }
  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [local.data_outputs.application_secret_arns["os"]]
    condition {
      test     = "StringEquals"
      variable = "aws:SourceVpce"
      values   = [aws_vpc_endpoint.auth_secrets.id]
    }
  }
}

resource "aws_iam_role_policy" "auth_lambda" {
  name   = "${var.name_prefix}-auth-lambda"
  role   = aws_iam_role.auth_lambda.id
  policy = data.aws_iam_policy_document.auth_lambda.json
}

resource "aws_lambda_function" "auth" {
  function_name    = "${var.name_prefix}-auth"
  role             = aws_iam_role.auth_lambda.arn
  runtime          = "dotnet10"
  handler          = "Oficina.Auth::Oficina.Auth.Function::FunctionHandler"
  filename         = "${path.module}/artifacts/oficina-auth.zip"
  source_code_hash = filebase64sha256("${path.module}/artifacts/oficina-auth.zip")
  timeout          = 20
  memory_size      = 512

  vpc_config {
    subnet_ids         = values(local.data_outputs.private_subnet_ids)
    security_group_ids = [aws_security_group.auth_lambda.id]
  }

  environment {
    variables = {
      AUTH_LOOKUP_URL    = "http://${aws_instance.k3s.private_ip}:30080/internal/customers/lookup"
      AUTH_OS_SECRET_ARN = local.data_outputs.application_secret_arns["os"]
      AUTH_JWT_ISSUER    = "OficinaMvp"
      AUTH_JWT_AUDIENCE  = "OficinaMvpClients"
    }
  }

  depends_on = [aws_iam_role_policy.auth_lambda, aws_cloudwatch_log_group.auth_lambda, aws_vpc_endpoint.auth_secrets]
}

resource "aws_apigatewayv2_api" "auth" {
  name          = "${var.name_prefix}-auth"
  protocol_type = "HTTP"
}

resource "aws_apigatewayv2_integration" "auth" {
  api_id                 = aws_apigatewayv2_api.auth.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.auth.invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "auth" {
  api_id    = aws_apigatewayv2_api.auth.id
  route_key = "POST /auth/token"
  target    = "integrations/${aws_apigatewayv2_integration.auth.id}"
}

resource "aws_apigatewayv2_stage" "auth" {
  api_id      = aws_apigatewayv2_api.auth.id
  name        = "$default"
  auto_deploy = true

  default_route_settings {
    throttling_burst_limit = 10
    throttling_rate_limit  = 5
  }
}

resource "aws_lambda_permission" "auth_gateway" {
  statement_id  = "AllowAuthHttpApi"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.auth.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.auth.execution_arn}/*/POST/auth/token"
}
