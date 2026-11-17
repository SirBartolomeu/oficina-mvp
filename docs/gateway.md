# Gateway da demonstração

O K3s planejado inclui Traefik. `k8s/ingress.yaml` usa `ingressClassName: traefik` e encaminha `/api` à OS sem reescrever caminhos. `/internal/customers/lookup` não está na rota pública; a Function o consulta pela rede interna com `X-Internal-Token`. O token de serviço e a chave JWT devem vir de segredos, nunca da imagem.

O teste `CustomerAuthFlowTests` executa localmente CPF → handler da Function → consulta HTTP interna à OS → JWT → tracking protegido, sem criar recursos AWS.

## Gateway local

`docker compose -f docker-compose.yml -f compose.gateway.yml up -d --build gateway` inicia OS, PostgreSQL, Mailpit, adaptador da Function e Traefik. O endereço de demonstração é `https://localhost:8443`. O certificado é gerado pelo Traefik e não tem uma CA confiável; clientes de teste devem ignorar a validação **somente neste ambiente local**. O adaptador `services/Oficina.Auth.Local` entrega HTTP ao mesmo `FunctionHandler` previsto para a Lambda, sem duplicar a regra de autenticação. Ele não publica porta no host. O Traefik encaminha `/auth/token` ao adaptador e `/api` à OS, sem expor `/internal`. A rota de CPF tem limite de 5 requisições por segundo, com burst de 10. Somente 8443 é publicada no loopback.

Validação executada em 14/11/2026: CPF válido retornou JWT de 900 segundos; o token consultou a própria OS por `/api/client/work-orders/{id}` com 200. CPF inválido retornou 401, tracking anônimo 401, `/internal/customers/lookup` 404 e 20 de 30 pedidos simultâneos à rota de CPF receberam 429. A porta HTTP local não ficou publicada. O primeiro POST da OS retornou 500 porque Mailpit não havia sido iniciado; a OS persistiu antes da notificação. O Compose do gateway agora inicia Mailpit também. Os 11 testes de `Oficina.Auth.Tests` e o teste de integração `CustomerAuthFlowTests` passaram. A ligação pública da Lambda, certificado confiável e a validação em K3s/AWS continuam pendentes.
