data "terraform_remote_state" "data" {
  backend = "s3"
  config = {
    bucket = var.data_state_bucket
    key    = var.data_state_key
    region = var.aws_region
  }
}

locals {
  services     = toset(["os", "execution", "billing"])
  data_outputs = data.terraform_remote_state.data.outputs
}

check "same_environment" {
  assert {
    condition     = local.data_outputs.aws_region == var.aws_region && local.data_outputs.name_prefix == var.name_prefix
    error_message = "Os estados data e K3s precisam usar a mesma região e o mesmo name_prefix."
  }
}
