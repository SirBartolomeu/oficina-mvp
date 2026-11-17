# language: pt
Funcionalidade: Saga da ordem de serviço distribuída
  Para acompanhar uma ordem de serviço sem duplicar efeitos
  Como cliente ou administrador da oficina
  Quero que orçamento, estoque, pagamento e execução mudem de estado de forma consistente

  Cenário: Orçamento aprovado solicita reserva antes do pagamento
    Dado que uma ordem de serviço foi aberta com cotação válida
    Quando o Billing aprova a versão atual do orçamento
    Então a OS solicita a reserva de estoque
    E somente após a reserva confirmada solicita o pagamento

  Esquema do Cenário: Pagamento sem valor recebido libera a reserva
    Dado que a OS aguarda pagamento com estoque reservado
    Quando a order termina como <estado> sem valor pago
    Então o Billing publica uma recusa de pagamento
    E a OS solicita a liberação do estoque
    E a OS só conclui o cancelamento após a confirmação da liberação

    Exemplos:
      | estado    |
      | expired   |
      | canceled  |

  Cenário: Recusa de reparo pago exige revisão antes do estorno
    Dado que o pagamento foi confirmado e a execução começou
    Quando a Execução recusa o reparo
    Então a OS entra em revisão manual sem finalizar
    E somente a decisão administrativa solicita o estorno

  Cenário: Estorno confirmado encerra a OS uma vez
    Dado que existe um pedido administrativo de estorno para pagamento confirmado
    Quando o provedor confirma a devolução por consulta da order
    Então o Billing publica RefundConfirmed uma vez
    E a OS valida a tentativa, o pedido, o valor e a filial
    E a OS encerra como cancelada sem processar duplicatas

  Cenário: Resposta ambígua de estorno não repete o pedido
    Dado que o Billing registrou RefundSubmitting antes de enviar o estorno
    Quando a resposta do POST é inconclusiva
    Então uma nova passagem do worker consulta a order sem repetir o POST
    E uma reconciliação por GET confirma o estorno quando o provedor o registra

  Cenário: Falha transitória no envio preserva a mensagem
    Dado que a abertura da OS está gravada com uma mensagem na Outbox
    Quando o primeiro envio para a fila falha
    Então a mensagem permanece pendente com a tentativa registrada
    E uma nova tentativa publica o mesmo evento sem recriar a OS

  Cenário: Reentrega de pagamento confirmado não consome estoque duas vezes
    Dado que PaymentConfirmed já foi aplicado na OS
    Quando a mesma identidade de evento chega novamente por uma fila
    Então a Inbox ignora a reentrega
    E a OS mantém um único comando ConsumeStockRequested

  Esquema do Cenário: Mensagem inválida vai para a fila de erro
    Dado que a fila de <servico> tem uma DLQ configurada
    Quando um evento desconhecido é recebido repetidamente
    Então o consumidor não confirma a mensagem inválida
    E a mensagem chega à DLQ após o limite de tentativas

    Exemplos:
      | servico  |
      | OS       |
      | Billing  |
      | Execução |

  Cenário: Duas réplicas da OS não publicam a mesma linha da Outbox ao mesmo tempo
    Dado que há uma mensagem pendente na Outbox PostgreSQL da OS
    Quando um publicador a seleciona e mantém o envio em andamento
    Então o segundo publicador não seleciona a mesma linha
    E somente um envio é confirmado após o primeiro concluir
