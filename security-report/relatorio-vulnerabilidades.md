# Relatorio de Vulnerabilidades - Tech Challenge Fase 2

## 1. Contexto da Execucao

- Data/hora (UTC): `2026-07-13T18:11:11.0051766Z`
- Maquina: `M24281`
- Usuario: `local-user`
- Branch: `N/A`
- Commit: `N/A`

## 2. Ferramentas Utilizadas

- `.NET SDK`: `9.0.313`
- `Python`: `Python 3.14.5`
- `Docker`: `Docker version 29.4.0, build 9d7ad9f`
- `Semgrep`: `1.169.0`
- `Scanner de imagem`: `docker-scout`

## 3. Resumo Executivo

| Ferramenta | Critical | High | Medium | Low | Unspecified | Total |
|---|---:|---:|---:|---:|---:|---:|
| dotnet-nuget | 0 | 0 | 0 | 0 | 0 | 0 |
| semgrep | 0 | 0 | 0 | 0 | 0 | 0 |
| docker-scout | 0 | 0 | 1 | 0 | 0 | 1 |
| TOTAL | 0 | 0 | 1 | 0 | 0 | 1 |

## 4. Achados Consolidados

| Ferramenta | ID | Severidade | Ativo | Localizacao | Detalhe |
|---|---|---|---|---|---|
| docker-scout | CVE-2025-60876 | medium | container-image | /lib/apk/db/installed | Vulnerability :CVE-2025-60876 Severity :MEDIUM Package :pkg:apk/alpine/busybox@1.37.0-r30?os_name=alpine&os_version=3.23 Affected range :<=1.37.0-r30 Fixed version :not fixed EPSS Score :0.002850 EPSS Percentile :0.20... |

## 5. Analise de Risco e Recomendacoes

1. Priorizar correcao imediata de achados **Critical** e **High**.
2. Atualizar dependencias NuGet vulneraveis para versoes com patch.
3. Revisar validacoes de entrada e tratamento de erros nos pontos sinalizados pelo SAST.
4. Reexecutar scan apos cada correcao e anexar comparativo de antes/depois.
5. Manter este pipeline em rotina de pre-release.

## 6. Limitacoes

- O relatorio representa um recorte no instante do scan.
- A ausencia de achados nao garante ausencia total de riscos.
- Recomendado complementar com testes manuais de seguranca e revisao arquitetural.

## 7. Evidencias Brutas

Arquivos gerados em `security-report/raw/`:

- `metadata.json`
- `dotnet-vulnerabilities.json`
- `semgrep.json`
- `docker-scout-cves.sarif.json`
