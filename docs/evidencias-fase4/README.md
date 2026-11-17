# Evidências da Fase 4

Coleta: 17/11/2026. Ambiente demonstrativo de testes em `us-east-1`, conta `879310855568`.

## Estado comprovado

- OS, Billing e Execution implantados no K3s; readiness HTTP 200 e pods `1/1 Ready`.
- Autenticação JWT pela API Gateway/Lambda e consulta de OS do cliente funcionaram.
- Saga criou orçamento, recebeu aprovação pelo Billing e criou payment attempt correlacionado à order de teste Mercado Pago.
- Order Mercado Pago está `created`; checkout está disponível. Nenhum pagamento foi confirmado.
- Prometheus e Grafana estão saudáveis. Tempo recebe traces dos três serviços; um trace de leitura da OS está consultável.

## Arquivos

- [Arquitetura e recursos AWS](arquitetura-e-recursos.md)
- [Pipelines e saúde dos serviços](pipelines-e-saude.md)
- [Fluxo Saga, Mercado Pago e observabilidade](fluxo-saga-e-pagamento.md)
- [Checklist FIAP Fase 4](checklist-fiap-fase4.md)
- [Roteiro de vídeo de 13 minutos](roteiro-video-13-min.md)

## Pendências para apresentar o fluxo completo

1. Realizar o checkout usando o comprador de teste no painel Mercado Pago. A senha não é retornada pelo MCP e precisa ser vista/recuperada no [painel de test users](https://www.mercadopago.com.br/developers/panel/app/793687825808927/test-users).
2. Aprovar o job `deploy` do workflow [#44188488424](https://github.com/SirBartolomeu/oficina-k8s-infra/actions/runs/44188488424) no GitHub Environment `aws-demo`. O plano revisado prevê 10 criações, 2 alterações in-place e zero destruições.
3. Depois do deploy, configurar a URL de sandbox `https://un67q1d3jk.execute-api.us-east-1.amazonaws.com/webhooks/mercado-pago/orders`, obter a chave de assinatura no painel de desenvolvedores e armazená-la no segredo do Billing sem compartilhá-la no chat.
4. Confirmar webhook assinado, atualização de OS, execução e compensação pós-pagamento.

As evidências distinguem explicitamente estado observado de validação pendente; nenhum pagamento ou resultado foi inventado.
