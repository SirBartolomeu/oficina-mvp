resource "aws_secretsmanager_secret" "application" {
  for_each                = local.services
  name                    = "${var.name_prefix}/${each.key}/application"
  description             = "Configuracao privada do servico ${each.key} na demo FIAP"
  recovery_window_in_days = 0
}
