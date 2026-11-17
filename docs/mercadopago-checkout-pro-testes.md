# Mercado Pago — demonstração de testes do Oficina MVP FIAP

Estado verificado em 17/11/2026. Integração existente no Billing, com Checkout Pro via Orders API. Nenhum código de pagamento foi reimplementado nesta configuração.

## Evidências confirmadas

| Item | Resultado |
|---|---|
| MCP e OAuth | Conexão autenticada; aplicação criada e confirmada por `application_list` |
| Aplicação | `Oficina MVP FIAP` — `5000203132464527` |
| Aplicação do vendedor de teste | `3251250730660971` |
| Vendedor de teste | `3853944840`; `/users/me` confirmou `site_id=MLB` e `test_user` |
| Comprador de teste | `3853944842`, criado automaticamente pelo MCP |
| Credenciais de teste | Obtidas via MCP e verificadas; não gravadas neste documento |
| Produção | Credenciais não ativadas; nenhum pagamento real realizado |
| Teste direto da Orders API | Order `ORDTST01M7KTHHM8EFSMTN17CFB5WP32`, R$ 1,00, status `created` no momento do teste |

A API retornou uma URL de checkout para o mesmo formato de payload usado pelo Billing (`online`, `manual`, `automatic_async`). A order não foi paga e foi criada com expiração de 15 minutos: esta é uma evidência histórica, não um checkout permanente para a apresentação. O teste direto na API não comprova a execução completa da Saga.

## Webhook preparado como placeholder

**URL ilustrativa, não operacional:**

```text
https://billing-test.example.invalid/webhooks/mercado-pago/orders
```

Retorno ilustrativo:

```text
https://billing-test.example.invalid/checkout/return
```

O domínio `.invalid` deixa explícito que o endereço precisa ser substituído. Nenhum webhook foi cadastrado com essa URL. Não foi identificado `cloudflared` ou `ngrok` no PATH desta sessão.

O [modelo de configuração](../config/mercadopago-checkout-test.example.json) contém IDs, tópico `order`, URLs e nomes das variáveis necessárias, sem segredos. É um artefato para apresentação e planejamento; o Billing não carrega esse JSON automaticamente.

## Configuração existente para executar

O [perfil de testes](../compose.checkout-test.yml) usa estas variáveis do ambiente do processo:

| Variável | Origem / uso |
|---|---|
| `MP_TEST_ACCESS_TOKEN` | Credencial da aplicação do vendedor de teste, obtida pelo MCP; não usar a credencial da conta real |
| `MP_TEST_WEBHOOK_SECRET` | Assinatura do webhook do ambiente Teste, após sua configuração remota |
| `MP_TEST_RETURN_URL` | Opcional; URL HTTPS pública real com `/checkout/return`; deixar ausente enquanto houver apenas placeholder |

Após fornecer os segredos pelo ambiente e configurar o webhook real:

```powershell
docker compose -f compose.billing.yml -f compose.checkout-test.yml up -d --build
Invoke-RestMethod http://localhost:8082/health/ready
```

Página local de demonstração: `http://localhost:8082/dev/checkout`. Ela consulta uma tentativa existente usando o token interno do Billing; não cria uma tentativa por si só.

O perfil mantém `Development`, `TestMode=true` e o worker de estorno desligado. O cliente exige uma conta brasileira com `test_user` antes de criar a order. Credenciais não devem ser colocadas em arquivos versionados ou slides.

## O que falta para demonstrar ponta a ponta

1. Disponibilizar um endereço HTTPS público encaminhando a rota do webhook ao Billing local na porta 8082, ou a uma instância de testes.
2. Cadastrar o evento **Order (Mercado Pago)** no ambiente **Teste**, com a URL real, e obter sua assinatura.
3. Injetar token e assinatura no ambiente e iniciar o Billing com o perfil existente.
4. Executar a Saga até criar uma tentativa de pagamento e abrir o checkout retornado pelo Billing.
5. Concluir exclusivamente com comprador e cartão de teste; conferir entrega assinada, reconciliação e evento final da Saga.

A assinatura, o cadastro remoto do webhook e o fluxo completo ainda não foram validados. O Billing não estava em execução na listagem Docker consultada durante esta preparação.

## Texto para apresentar

> A aplicação Oficina MVP FIAP foi provisionada via MCP autenticado com OAuth. Foram criadas contas de teste de vendedor e comprador, e a identidade do vendedor foi confirmada pela API. Uma order de teste de R$ 1,00 foi criada com sucesso usando o contrato implementado no Billing, sem efetuar pagamento. O receptor de webhook está implementado localmente; sua URL pública está representada por um placeholder. A validação ponta a ponta depende da publicação dessa rota e da configuração da assinatura de teste. Produção permanece inativa.

## Referências

- [Billing e execução local](../services/Oficina.Billing/README.md)
- [Criar order](https://www.mercadopago.com.br/developers/pt/docs/checkout-pro-orders/create-order)
- [Credenciais de teste](https://www.mercadopago.com.br/developers/pt/docs/checkout-pro-orders/resources/credentials)
- [Notificações](https://www.mercadopago.com.br/developers/pt/docs/checkout-pro-orders/notifications)
