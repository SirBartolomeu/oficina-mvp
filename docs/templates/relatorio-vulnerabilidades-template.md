# Relatorio de Vulnerabilidades - Tech Challenge Fase 1

## 1. Contexto da Execucao

- Data/hora (UTC): `{{generated_at_utc}}`
- Maquina: `{{machine_name}}`
- Usuario: `{{user_name}}`
- Branch: `{{git_branch}}`
- Commit: `{{git_commit}}`

## 2. Ferramentas Utilizadas

- `.NET SDK`: `{{dotnet_version}}`
- `Python`: `{{python_version}}`
- `Docker`: `{{docker_version}}`
- `Semgrep`: `{{semgrep_version}}`
- `Docker Scout`: `{{scout_version}}`

## 3. Resumo Executivo

{{summary_table}}

## 4. Achados Consolidados

{{findings_table}}

## 5. Analise de Risco e Recomendacoes

{{recommendations}}

## 6. Limitacoes

{{limitations}}

## 7. Evidencias Brutas

Arquivos gerados em `security-report/raw/`:

- `metadata.json`
- `dotnet-vulnerabilities.json`
- `semgrep.json`
- `docker-scout-cves.sarif.json`
