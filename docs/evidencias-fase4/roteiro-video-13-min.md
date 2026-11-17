# Roteiro de gravação — 13 minutos

Gravar somente após atualizar `fluxo-saga-e-pagamento.md` com resultados reais. Não mostrar credenciais, tokens, e-mails de comprador ou segredos.

| Tempo | Conteúdo | Captura a preparar |
|---|---|---|
| 0:00–0:45 | Objetivo FIAP e arquitetura | Diagrama com os seis repositórios e fronteiras de serviços. |
| 0:45–2:00 | AWS e infraestrutura | Terraform outputs sanitizados, RDS/Dynamo/SQS/ECR, EC2/K3s, Lambda e API Gateway. |
| 2:00–3:15 | CI/CD e qualidade | Actions concluídas, testes, cobertura >80%, CodeQL e deploy. |
| 3:15–4:15 | Kubernetes | Node Ready, deployments/services e healthchecks sem dados secretos. |
| 4:15–5:15 | Observabilidade | Grafana, Prometheus, Tempo e trace correlacionado entre serviços. |
| 5:15–8:45 | Saga — sucesso | OS, orçamento aprovado, payment attempt/order, webhook recebido, OS avançada, Execution concluída; exibir IDs correlacionados e estados/eventos. |
| 8:45–11:00 | Saga — falha/compensação | Falha controlada prevista, evento, ações compensatórias e estado persistido consistente. |
| 11:00–12:00 | Mercado Pago sandbox | Checkout de teste, URL HTTPS de webhook e entrega/validação sem mostrar dados do comprador. |
| 12:00–13:00 | Requisitos e encerramento | Checklist FIAP, limitações remanescentes e localização das evidências. |

## Capturas antes da gravação

- [ ] CI dos seis repositórios e checks de CodeQL aplicáveis.
- [ ] Saídas Terraform e recursos AWS com ARNs/IDs não sensíveis.
- [ ] K3s: node, pods, services e readiness.
- [ ] Coverage reports originais e BDD aprovado.
- [ ] Execução real de Saga de sucesso, com IDs, mensagens e estados.
- [ ] Execução real de compensação, sem confundi-la com recusa de orçamento.
- [ ] Mercado Pago: order sandbox, webhook/assinatura aceito e estado final.
- [ ] Trace e dashboards Grafana/Tempo.
- [ ] Swagger/Postman e branch protection.

Gravação ainda não está pronta enquanto os itens de Saga, webhook e BDD permanecerem pendentes no checklist.
