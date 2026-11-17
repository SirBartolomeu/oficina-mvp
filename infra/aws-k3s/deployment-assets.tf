data "aws_caller_identity" "current" {}

resource "aws_s3_bucket" "deployment_assets" {
  bucket        = "${substr(var.name_prefix, 0, 20)}-assets-${data.aws_caller_identity.current.account_id}-${var.aws_region}"
  force_destroy = true
}

resource "aws_s3_bucket_public_access_block" "deployment_assets" {
  bucket                  = aws_s3_bucket.deployment_assets.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "deployment_assets" {
  bucket = aws_s3_bucket.deployment_assets.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "deployment_assets" {
  bucket = aws_s3_bucket.deployment_assets.id
  rule {
    id     = "expire-demo-bundles"
    status = "Enabled"
    filter { prefix = "demo/" }
    expiration { days = 2 }
  }
}

data "aws_iam_policy_document" "k3s_read_assets" {
  statement {
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.deployment_assets.arn}/demo/*"]
  }
}

resource "aws_iam_role_policy" "k3s_read_assets" {
  name   = "${var.name_prefix}-k3s-read-assets"
  role   = aws_iam_role.k3s_host.id
  policy = data.aws_iam_policy_document.k3s_read_assets.json
}
