# Checklist FIAP Fase 4

Status baseado na especificação do Tech Challenge Fase 4 e nas evidências coletadas em 17/11/2026.

| Requisito | Estado | Evidência ou pendência |
|---|---|---|
| Três microsserviços independentes, bancos isolados e comunicação por eventos | Atendido | [Arquitetura e recursos](arquitetura-e-recursos.md); OS, Billing e Execution em repositórios próprios. |
| Saga com compensação e consistência | Parcial | Orçamento aprovado e tentativa de pagamento persistida; nenhum cenário terminal de pagamento/compensação executado ainda. Rejeição de orçamento não conta como compensação. |
| Testes unitários e cobertura mínima de 80% por serviço | Parcial/atendido nos serviços medidos | OS 81,73%, Execution 84,54%, Billing 85,73%; Auth CI 13/13 testes, sem métrica de cobertura equivalente registrada. |
| BDD cobrindo o fluxo completo | Pendente de comprovação | Há feature em `docs/bdd/saga-fase4.feature`; falta evidência de execução aprovada do cenário ponta a ponta na AWS. |
| CI/CD independente com build, testes e deploy automatizado | Atendido para pipelines registrados | Links e SHA em [pipelines e saúde](pipelines-e-saude.md). O novo PR do webhook ainda aguarda o check `validate`. |
| Proteção da branch principal e checks obrigatórios | Parcial | `main` protegida nos seis repositórios de serviço/infra com checks obrigatórios; `oficina-mvp` retornou 404 “Branch not protected”. Exigir ao menos um review: não configurado. |
| Qualidade estática / CodeQL | Parcial | CodeQL é check obrigatório em OS, Execution e Billing; resultado dos workflows citados foi sucesso. Auth e infra exigem apenas seus checks próprios. |
| Kubernetes, deploy e observabilidade | Atendido para o estado observado | Pods Ready, healthchecks HTTP 200, Prometheus/Grafana/Tempo e trace consultável; ver [arquitetura](arquitetura-e-recursos.md), [pipelines](pipelines-e-saude.md) e [Saga](fluxo-saga-e-pagamento.md). |
| Checkout, webhook assinado e confirmação de pagamento | Pendente | Checkout sandbox disponível, sem pagamento; endpoint HTTPS e configuração sandbox do webhook ainda dependem do PR/plan/apply e do segredo de assinatura. |
| Repositórios, Docker, manifests e pipelines | Atendido | Seis repositórios e links de workflows listados em [pipelines e saúde](pipelines-e-saude.md). |
| Swagger/Postman | Verificar na entrega final | Confirmar links e exportações acessíveis nos repositórios antes de gravar; não incluídos como captura real neste conjunto local. |
| Vídeo de até 15 minutos e relatório final | Preparação pendente | Usar [roteiro de 13 minutos](roteiro-video-13-min.md); gravar depois de concluir os dois cenários e atualizar as evidências. |

Não apresentar os cenários de sucesso/compensação como concluídos até registrar estados persistidos e eventos correlacionados.
