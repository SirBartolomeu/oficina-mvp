# Relatorio de Vulnerabilidades - Tech Challenge Fase 1

## 1. Contexto da Execucao

- Data/hora (UTC): `2026-04-24T13:58:14.0520118Z`
- Maquina: `M24281`
- Usuario: `aamjof`
- Branch: `N/A`
- Commit: `N/A`

## 2. Ferramentas Utilizadas

- `.NET SDK`: `9.0.301`
- `Python`: `Python 3.11.9`
- `Docker`: `Docker version 29.4.0, build 9d7ad9f`
- `Semgrep`: `1.161.0`
- `Docker Scout`: `v1.20.4`

## 3. Resumo Executivo

| Ferramenta | Critical | High | Medium | Low | Unspecified | Total |
|---|---:|---:|---:|---:|---:|---:|
| dotnet-nuget | 0 | 4 | 0 | 0 | 0 | 4 |
| semgrep | 0 | 1 | 0 | 0 | 0 | 1 |
| docker-scout | 0 | 0 | 2 | 27 | 0 | 29 |
| TOTAL | 0 | 5 | 2 | 27 | 0 | 34 |

## 4. Achados Consolidados

| Ferramenta | ID | Severidade | Ativo | Localizacao | Detalhe |
|---|---|---|---|---|---|
| dotnet-nuget | GHSA-7jgj-8wvc-jh57 | high | System.Net.Http | tests/OficinaMvp.Domain.Tests/OficinaMvp.Domain.Tests.csproj | https://github.com/advisories/GHSA-7jgj-8wvc-jh57 |
| dotnet-nuget | GHSA-7jgj-8wvc-jh57 | high | System.Net.Http | tests/OficinaMvp.Integration.Tests/OficinaMvp.Integration.Tests.csproj | https://github.com/advisories/GHSA-7jgj-8wvc-jh57 |
| dotnet-nuget | GHSA-cmhx-cq75-c4mj | high | System.Text.RegularExpressions | tests/OficinaMvp.Domain.Tests/OficinaMvp.Domain.Tests.csproj | https://github.com/advisories/GHSA-cmhx-cq75-c4mj |
| dotnet-nuget | GHSA-cmhx-cq75-c4mj | high | System.Text.RegularExpressions | tests/OficinaMvp.Integration.Tests/OficinaMvp.Integration.Tests.csproj | https://github.com/advisories/GHSA-cmhx-cq75-c4mj |
| semgrep | dockerfile.security.missing-user-entrypoint.missing-user-entrypoint | high | src/OficinaMvp.Api/Dockerfile | src/OficinaMvp.Api/Dockerfile:20 | By not specifying a USER, a program in the container may run as 'root'. This is a security hazard. If an attacker can control a process running as root, they may have control over the container. Ensure that the last U... |
| docker-scout | CVE-2025-45582 | medium | container-image | /usr/share/doc/tar/copyright | Vulnerability :CVE-2025-45582 Severity :MEDIUM Package :pkg:deb/debian/tar@1.34%2Bdfsg-1.2%2Bdeb12u1?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>=1.34+dfsg-1.2+deb12u1 Fixed version :not fixed EPS... |
| docker-scout | CVE-2026-41989 | medium | container-image | /usr/share/doc/libgcrypt20/copyright | Vulnerability :CVE-2026-41989 Severity :MEDIUM Package :pkg:deb/debian/libgcrypt20@1.10.1-3?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.000120 EPSS Percent... |
| docker-scout | CVE-2005-2541 | low | container-image | /usr/share/doc/tar/copyright | Vulnerability :CVE-2005-2541 Severity :LOW Package :pkg:deb/debian/tar@1.34%2Bdfsg-1.2%2Bdeb12u1?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.037630 EPSS Pe... |
| docker-scout | CVE-2007-5686 | low | container-image | /usr/share/doc/login/copyright | Vulnerability :CVE-2007-5686 Severity :LOW Package :pkg:deb/debian/shadow@1%3A4.13%2Bdfsg1-1%2Bdeb12u2?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.001960 E... |
| docker-scout | CVE-2010-0928 | low | container-image | /usr/share/doc/libssl3/copyright | Vulnerability :CVE-2010-0928 Severity :LOW Package :pkg:deb/debian/openssl@3.0.19-1~deb12u2?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>=3.0.11-1~deb12u2 Fixed version :not fixed EPSS Score :0.000... |
| docker-scout | CVE-2010-4756 | low | container-image | /usr/share/doc/libc-bin/copyright | Vulnerability :CVE-2010-4756 Severity :LOW Package :pkg:deb/debian/glibc@2.36-9%2Bdeb12u13?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.003940 EPSS Percenti... |
| docker-scout | CVE-2011-3374 | low | container-image | /usr/share/doc/apt/copyright | Vulnerability :CVE-2011-3374 Severity :LOW Package :pkg:deb/debian/apt@2.6.1?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.015090 EPSS Percentile :0.812530 |
| docker-scout | CVE-2011-3389 | low | container-image | /usr/share/doc/libgnutls30/copyright | Vulnerability :CVE-2011-3389 Severity :LOW Package :pkg:deb/debian/gnutls28@3.7.9-2%2Bdeb12u6?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.038320 EPSS Perce... |
| docker-scout | CVE-2011-4116 | low | container-image | /usr/share/doc/perl-base/copyright | Vulnerability :CVE-2011-4116 Severity :LOW Package :pkg:deb/debian/perl@5.36.0-7%2Bdeb12u3?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.002000 EPSS Percenti... |
| docker-scout | CVE-2013-4392 | low | container-image | /usr/share/doc/libsystemd0/copyright | Vulnerability :CVE-2013-4392 Severity :LOW Package :pkg:deb/debian/systemd@252.39-1~deb12u1?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.000420 EPSS Percent... |
| docker-scout | CVE-2017-18018 | low | container-image | /usr/share/doc/coreutils/copyright | Vulnerability :CVE-2017-18018 Severity :LOW Package :pkg:deb/debian/coreutils@9.1-1?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.000570 EPSS Percentile :0.1... |
| docker-scout | CVE-2018-20796 | low | container-image | /usr/share/doc/libc-bin/copyright | Vulnerability :CVE-2018-20796 Severity :LOW Package :pkg:deb/debian/glibc@2.36-9%2Bdeb12u13?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.014920 EPSS Percent... |
| docker-scout | CVE-2018-6829 | low | container-image | /usr/share/doc/libgcrypt20/copyright | Vulnerability :CVE-2018-6829 Severity :LOW Package :pkg:deb/debian/libgcrypt20@1.10.1-3?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.005150 EPSS Percentile ... |
| docker-scout | CVE-2019-1010022 | low | container-image | /usr/share/doc/libc-bin/copyright | Vulnerability :CVE-2019-1010022 Severity :LOW Package :pkg:deb/debian/glibc@2.36-9%2Bdeb12u13?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.001310 EPSS Perce... |
| docker-scout | CVE-2019-1010023 | low | container-image | /usr/share/doc/libc-bin/copyright | Vulnerability :CVE-2019-1010023 Severity :LOW Package :pkg:deb/debian/glibc@2.36-9%2Bdeb12u13?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.003130 EPSS Perce... |
| docker-scout | CVE-2019-1010024 | low | container-image | /usr/share/doc/libc-bin/copyright | Vulnerability :CVE-2019-1010024 Severity :LOW Package :pkg:deb/debian/glibc@2.36-9%2Bdeb12u13?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.006460 EPSS Perce... |
| docker-scout | CVE-2019-1010025 | low | container-image | /usr/share/doc/libc-bin/copyright | Vulnerability :CVE-2019-1010025 Severity :LOW Package :pkg:deb/debian/glibc@2.36-9%2Bdeb12u13?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.008560 EPSS Perce... |
| docker-scout | CVE-2019-9192 | low | container-image | /usr/share/doc/libc-bin/copyright | Vulnerability :CVE-2019-9192 Severity :LOW Package :pkg:deb/debian/glibc@2.36-9%2Bdeb12u13?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.007900 EPSS Percenti... |
| docker-scout | CVE-2022-0563 | low | container-image | /usr/share/doc/bsdutils/copyright | Vulnerability :CVE-2022-0563 Severity :LOW Package :pkg:deb/debian/util-linux@2.38.1-5%2Bdeb12u3?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.000250 EPSS Pe... |
| docker-scout | CVE-2022-27943 | low | container-image | /usr/share/doc/gcc-12-base/copyright | Vulnerability :CVE-2022-27943 Severity :LOW Package :pkg:deb/debian/gcc-12@12.2.0-14%2Bdeb12u1?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.000500 EPSS Perc... |
| docker-scout | CVE-2022-3219 | low | container-image | /usr/share/doc/gpgv/copyright | Vulnerability :CVE-2022-3219 Severity :LOW Package :pkg:deb/debian/gnupg2@2.2.40-1.1%2Bdeb12u2?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.000150 EPSS Perc... |
| docker-scout | CVE-2023-31437 | low | container-image | /usr/share/doc/libsystemd0/copyright | Vulnerability :CVE-2023-31437 Severity :LOW Package :pkg:deb/debian/systemd@252.39-1~deb12u1?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.001700 EPSS Percen... |
| docker-scout | CVE-2023-31438 | low | container-image | /usr/share/doc/libsystemd0/copyright | Vulnerability :CVE-2023-31438 Severity :LOW Package :pkg:deb/debian/systemd@252.39-1~deb12u1?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.001340 EPSS Percen... |
| docker-scout | CVE-2023-31439 | low | container-image | /usr/share/doc/libsystemd0/copyright | Vulnerability :CVE-2023-31439 Severity :LOW Package :pkg:deb/debian/systemd@252.39-1~deb12u1?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.001250 EPSS Percen... |
| docker-scout | CVE-2023-31486 | low | container-image | /usr/share/doc/perl-base/copyright | Vulnerability :CVE-2023-31486 Severity :LOW Package :pkg:deb/debian/perl@5.36.0-7%2Bdeb12u3?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.005980 EPSS Percent... |
| docker-scout | CVE-2024-2236 | low | container-image | /usr/share/doc/libgcrypt20/copyright | Vulnerability :CVE-2024-2236 Severity :LOW Package :pkg:deb/debian/libgcrypt20@1.10.1-3?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.005880 EPSS Percentile ... |
| docker-scout | CVE-2025-14104 | low | container-image | /usr/share/doc/bsdutils/copyright | Vulnerability :CVE-2025-14104 Severity :LOW Package :pkg:deb/debian/util-linux@2.38.1-5%2Bdeb12u3?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.000070 EPSS P... |
| docker-scout | CVE-2025-27587 | low | container-image | /usr/share/doc/libssl3/copyright | Vulnerability :CVE-2025-27587 Severity :LOW Package :pkg:deb/debian/openssl@3.0.19-1~deb12u2?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.002240 EPSS Percen... |
| docker-scout | CVE-2025-5278 | low | container-image | /usr/share/doc/coreutils/copyright | Vulnerability :CVE-2025-5278 Severity :LOW Package :pkg:deb/debian/coreutils@9.1-1?os_distro=bookworm&os_name=debian&os_version=12 Affected range :>0 Fixed version :not fixed EPSS Score :0.001300 EPSS Percentile :0.32... |

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
