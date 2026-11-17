data "aws_ssm_parameter" "al2023_ami" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64"
}

data "aws_iam_policy_document" "ec2_assume_role" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ec2.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "k3s_host" {
  name               = "${var.name_prefix}-k3s-host"
  assume_role_policy = data.aws_iam_policy_document.ec2_assume_role.json
}

resource "aws_iam_role_policy_attachment" "k3s_ssm" {
  role       = aws_iam_role.k3s_host.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_instance_profile" "k3s_host" {
  name = "${var.name_prefix}-k3s-host"
  role = aws_iam_role.k3s_host.name
}

resource "aws_instance" "k3s" {
  ami                         = data.aws_ssm_parameter.al2023_ami.value
  instance_type               = var.k3s_instance_type
  subnet_id                   = aws_subnet.public.id
  vpc_security_group_ids      = [aws_security_group.k3s.id]
  iam_instance_profile        = aws_iam_instance_profile.k3s_host.name
  associate_public_ip_address = true
  user_data                   = templatefile("${path.module}/user-data/k3s.sh.tftpl", { k3s_version = var.k3s_version })
  user_data_replace_on_change = true

  root_block_device {
    volume_type           = "gp3"
    volume_size           = 30
    encrypted             = true
    delete_on_termination = true
  }

  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "required"
    http_put_response_hop_limit = 1
    instance_metadata_tags      = "disabled"
  }

  tags = { Name = "${var.name_prefix}-k3s" }

  depends_on = [aws_iam_role_policy_attachment.k3s_ssm]
}
