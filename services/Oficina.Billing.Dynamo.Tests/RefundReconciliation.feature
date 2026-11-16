# language: pt
Funcionalidade: Reconciliação segura do estorno no Billing
  A resposta do provedor pode chegar depois do pedido de estorno

  Esquema do Cenário: Estorno <resposta> só é confirmado por consulta
    Dado que o DynamoDB Local está configurado para o Billing
    Quando o POST do estorno tem resposta <resposta>
    Então o Billing não repete o POST e publica RefundConfirmed após confirmação por GET

    Exemplos:
      | resposta   |
      | conclusiva |
      | ambígua    |
