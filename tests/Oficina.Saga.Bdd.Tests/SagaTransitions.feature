# language: pt
Funcionalidade: Transições da Saga na ordem de serviço
  A ordem de serviço coordena reserva, pagamento, execução e compensações

  Cenário: A aprovação exige reserva antes do pagamento
    Dado uma OS com orçamento disponível
    Quando o orçamento é aprovado
    Então a OS aguarda a reserva de estoque
    Quando a reserva é confirmada
    Então a OS aguarda o pagamento

  Esquema do Cenário: Pagamento sem valor recebido exige liberação
    Dado uma OS com estoque reservado aguardando pagamento
    Quando o pagamento termina como <estado> sem valor recebido
    Então a OS aguarda a liberação de estoque
    Quando a liberação é confirmada
    Então a OS fica cancelada

    Exemplos:
      | estado   |
      | expired  |
      | canceled |

  Cenário: Recusa após pagamento exige revisão e confirmação do estorno
    Dado uma OS paga em execução
    Quando a execução recusa o reparo
    Então a OS aguarda revisão manual
    Quando a administração solicita o estorno
    Então a OS aguarda a confirmação do estorno
    Quando o estorno é confirmado
    Então a OS fica cancelada
