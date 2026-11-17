# Fluxo distribuído, Saga e Mercado Pago

## Fluxo real executado

- SSM `c3be80c1-8327-4b01-b947-8dd15ca5792e`: criou OS `d3e64514-a8d1-40ec-a855-a8c64dd4b204` em `AwaitingApproval`; a função Auth emitiu JWT do cliente e o endpoint de acompanhamento retornou a mesma OS.
- Billing criou orçamento `30318354-2fc8-4fe0-bcaa-606fad9ab4cd`, versão 1, total **R$ 125,00**. Aprovação foi registrada pelo endpoint interno do Billing (`a5668582-4bb4-400c-ba47-c33be02db550`); o endpoint direto da OS é bloqueado no modo distribuído por projeto.
- A OS avançou para `AwaitingPayment`. A tabela DynamoDB persistiu `PAYMENT#7e307a15853b47ce80b22a194a62b87a`, com `workOrderId`, orçamento, status `Created`, `providerOrderId` e `checkoutUrl` correlacionados.
- Consulta autenticada à Orders API retornou `ORDTST01M7NQP4N3877ZB3R1W4SKMCHP`, status `created`, valor `125.00`, `external_reference=pay_7e307a15853b47ce80b22a194a62b87a` e checkout disponível.

### Checkout sandbox

[Abrir checkout de teste atual (R$ 125,00)](https://www.mercadopago.com.br/checkout/v1/redirect?order_id=ORDTST01M7NQP4N3877ZB3R1W4SKMCHP&pref_id=3853944840-66ee92d3-aea1-4243-be36-a8756cfc10c9)

Comprador de teste Mercado Pago: ID `3853944842`, perfil `buyer`, saldo sandbox carregado de R$ 200 pelo MCP. A ferramenta mascarou a senha; consultar as credenciais no [painel de test users](https://www.mercadopago.com.br/developers/panel/app/793687825808927/test-users). O checkout ainda precisa ser concluído. Não houve captura ou pagamento real.

## Estado não concluído

- A order atual está `created` e a OS `AwaitingPayment`; nenhum webhook de pagamento foi recebido. A order sandbox anterior passou a `canceled` e não deve ser usada; o provedor não retornou motivo detalhado.
- O endpoint HTTPS público será `https://un67q1d3jk.execute-api.us-east-1.amazonaws.com/webhooks/mercado-pago/orders`. O código de forwarding (Lambda privada → NodePort privado do Billing) foi aprovado no [PR #3](https://github.com/SirBartolomeu/oficina-k8s-infra/pull/3); a declaração para preservar o acesso existente do K3s ao endpoint Secrets Manager foi integrada no [PR #4](https://github.com/SirBartolomeu/oficina-k8s-infra/pull/4). O workflow [#44188488424](https://github.com/SirBartolomeu/oficina-k8s-infra/actions/runs/44188488424) tem plan bem-sucedido e aguarda aprovação do environment `aws-demo`; a URL ainda não deve ser tratada como ativa.
- Após o deploy, ainda falta cadastrar essa URL somente no ambiente sandbox do Mercado Pago e guardar a chave de assinatura no segredo do Billing. A assinatura será validada no Billing e a order confirmada pela API antes da persistência.
- Execução da OS, confirmação de pagamento, webhook assinado e compensação pós-pagamento ainda não foram validados.
- Um segundo orçamento foi recusado (`772a1782-7281-46a2-b2cf-33e0bc57e79f`); a OS permaneceu `AwaitingApproval` para nova decisão. Isso é recusa de orçamento, não evidência de compensação financeira da Saga.

## Observabilidade

- Grafana está saudável e provisiona Prometheus e Tempo (`http://grafana.oficina.svc.cluster.local:3000`, somente interno).
- Tempo `/api/search` retornou traces com root services `oficina-os`, `oficina-execution` e `oficina-billing`.
- Trace `82a6b9d87ce1a2fe61d2a4711ca561ee` de leitura da OS `d3e64514-a8d1-40ec-a855-a8c64dd4b204` ficou consultável no Tempo (SSM `69492d67-354c-48ee-b523-982a82033924`).
- A criação original da OS ocorreu antes do Tempo e aquele trace ID não estava armazenado; o trace acima comprova consulta distribuída posterior da mesma OS.
