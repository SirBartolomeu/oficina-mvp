# Reconciliação de estorno pendente

Use este procedimento quando a OS estiver em `RefundRequested` e o Billing em `RefundSubmitting` por tempo anormal. O identificador `paymentAttemptId` consta na OS. As rotas abaixo exigem `X-Internal-Token` e devem ser chamadas apenas pela rede interna.

1. Consulte `GET /internal/payments/{paymentAttemptId}` no Billing. Confira `status`, `providerOrderId`, `refundRequestId` e `refundSubmittedAtUtc`.
2. Confira a order no painel de testes do Mercado Pago. Não envie outro pedido de estorno se o resultado anterior estiver ambíguo.
3. Chame `POST /internal/payments/{paymentAttemptId}/reconcile-refund`. A rota faz somente `GET` da order no provedor, confere referência, moeda e valor e devolve `status` e `reconciled`. Não há novo POST de estorno.
4. Se retornar `Refunded`, a Outbox do Billing publicará `RefundConfirmed` e a Inbox da OS concluirá o cancelamento. Se continuar `RefundSubmitting`, investigue no provedor e preserve a pendência; não altere o estado diretamente no DynamoDB.

A rota retorna `404` para tentativa inexistente, `409` para estado incompatível ou order incoerente e `503` quando a consulta ao provedor falha. Uma nova consulta pode ser feita depois. O worker também consulta periodicamente a order e pode confirmar o estorno sem intervenção. Nenhum procedimento devolve automaticamente ao estoque peças já consumidas no reparo físico.
