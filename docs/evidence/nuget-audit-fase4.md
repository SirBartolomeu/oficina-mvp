# NuGet Audit local — Fase 4

Consulta em 16/11/2026 com .NET SDK 10.0.403:

```text
dotnet list <projeto.csproj> package --vulnerable --include-transitive --format json
```

O CLI indicou `https://api.nuget.org/v3/index.json` como fonte. Em todos os oito projetos abaixo, retornou código 0 e JSON sem pacotes vulneráveis listados:

| Projeto | Local consultado | Advisories reportados |
|---|---|---:|
| OS API | `exports/oficina-os/src/OficinaMvp.Api` | 0 |
| OS Domain | `src/OficinaMvp.Domain` | 0 |
| OS Application | `src/OficinaMvp.Application` | 0 |
| OS Infrastructure | `src/OficinaMvp.Infrastructure` | 0 |
| Execução | `exports/oficina-execution/services/Oficina.Execution` | 0 |
| Billing | `exports/oficina-billing/services/Oficina.Billing` | 0 |
| Auth Lambda | `exports/oficina-auth-lambda/services/Oficina.Auth` | 0 |
| Auth local | `services/Oficina.Auth.Local` | 0 |

É um retrato dos advisories NuGet conhecidos no momento da consulta. Não cobre o código por SAST, imagens Docker, configuração AWS ou execução remota. O arquivo `security-report/relatorio-vulnerabilidades.md` é de julho/2026 e da Fase 2; não deve ser apresentado como scan da Fase 4.
