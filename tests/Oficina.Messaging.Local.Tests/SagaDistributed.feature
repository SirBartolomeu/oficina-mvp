# language: pt
Funcionalidade: Saga distribuída com filas e bancos locais
  Os três serviços processam mensagens sem perder transições e compensações

  Cenário: Abertura percorre aprovação, reserva, pagamento e execução
    Dado que os emuladores SQS e DynamoDB estão configurados
    Quando a ordem percorre o fluxo de abertura até execução
    Então o fluxo distribuído termina com as verificações de estado e idempotência aprovadas

  Cenário: Estorno confirmado atravessa Billing e OS
    Dado que os emuladores SQS e DynamoDB estão configurados
    Quando um pedido de estorno é processado pelos serviços
    Então o fluxo distribuído termina com as verificações de estado e idempotência aprovadas

  Cenário: Recusa da execução atravessa a fila e leva a OS para revisão manual
    Dado que o emulador SQS está configurado
    Quando a Execução recusa um reparo já iniciado
    Então o fluxo distribuído termina com as verificações de estado e idempotência aprovadas

  Esquema do Cenário: Pagamento <estado> libera reserva e restaura estoque
    Dado que os emuladores SQS e DynamoDB estão configurados
    Quando um pagamento sem valor recebido termina como <estado>
    Então o fluxo distribuído termina com as verificações de estado e idempotência aprovadas

    Exemplos:
      | estado   |
      | expired  |
      | canceled |

  Cenário: Confirmação antecipada espera reserva e não duplica consumo
    Quando a confirmação de pagamento chega antes da reserva e é reentregue
    Então o fluxo distribuído termina com as verificações de estado e idempotência aprovadas

  Esquema do Cenário: Mensagem inválida chega à DLQ de cada serviço
    Dado que o emulador SQS está configurado
    Quando <servico> recebe uma mensagem inválida repetidamente
    Então o fluxo distribuído termina com as verificações de estado e idempotência aprovadas

    Exemplos:
      | servico   |
      | os        |
      | billing   |
      | execution |
