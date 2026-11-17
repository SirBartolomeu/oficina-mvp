#!/usr/bin/env python3
"""Generate the Phase 4 portal PDF from local, non-secret submission metadata."""

import argparse
import json
from pathlib import Path

from reportlab.lib.colors import HexColor, white
from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas


ROOT = Path(__file__).resolve().parents[1]
NAVY = HexColor("#17324d")
BLUE = HexColor("#dfeef8")
GREEN = HexColor("#e2f1e8")
GRAY = HexColor("#536170")
LIGHT = HexColor("#f4f7fa")
AMBER = HexColor("#fff0cb")


def paragraph(pdf, text, x, y, width, size=10, leading=15, color=GRAY):
    pdf.setFont("Helvetica", size)
    pdf.setFillColor(color)
    words = text.split()
    line = ""
    for word in words:
        candidate = f"{line} {word}".strip()
        if line and pdf.stringWidth(candidate, "Helvetica", size) > width:
            pdf.drawString(x, y, line)
            y -= leading
            line = word
        else:
            line = candidate
    if line:
        pdf.drawString(x, y, line)
        y -= leading
    return y


def heading(pdf, text, x, y, size=13):
    pdf.setFont("Helvetica-Bold", size)
    pdf.setFillColor(NAVY)
    pdf.drawString(x, y, text)
    return y - 20


def box(pdf, x, y, width, height, title, fill=BLUE, size=8):
    pdf.setFillColor(fill)
    pdf.setStrokeColor(HexColor("#a9c0cf"))
    pdf.roundRect(x, y, width, height, 6, fill=1, stroke=1)
    pdf.setFillColor(NAVY)
    pdf.setFont("Helvetica-Bold", size)
    pdf.drawCentredString(x + width / 2, y + height / 2 - size / 3, title)


def arrow(pdf, x1, y1, x2, y2):
    pdf.setStrokeColor(GRAY)
    pdf.setFillColor(GRAY)
    pdf.setLineWidth(1)
    pdf.line(x1, y1, x2, y2)
    if abs(x2 - x1) >= abs(y2 - y1):
        sign = 1 if x2 > x1 else -1
        points = [(x2, y2), (x2 - sign * 6, y2 + 3), (x2 - sign * 6, y2 - 3)]
    else:
        sign = 1 if y2 > y1 else -1
        points = [(x2, y2), (x2 - 3, y2 - sign * 6), (x2 + 3, y2 - sign * 6)]
    path = pdf.beginPath()
    path.moveTo(*points[0])
    path.lineTo(*points[1])
    path.lineTo(*points[2])
    path.close()
    pdf.drawPath(path, fill=1, stroke=0)


def footer(pdf, page):
    width, _ = A4
    pdf.setStrokeColor(HexColor("#d6dde4"))
    pdf.line(42, 45, width - 42, 45)
    pdf.setFont("Helvetica", 8)
    pdf.setFillColor(GRAY)
    pdf.drawString(42, 31, "FIAP - SOAT - Tech Challenge Fase 4")
    pdf.drawRightString(width - 42, 31, f"Página {page}/2")


def metadata_row(pdf, label, value, x, y, width):
    pdf.setFont("Helvetica-Bold", 9)
    pdf.setFillColor(NAVY)
    pdf.drawString(x, y, label)
    x_value = x + 94
    shown = value or "PENDENTE"
    color = HexColor("#9b5c0b") if not value or value.startswith("URL ") else HexColor("#245985")
    y_next = paragraph(pdf, shown, x_value, y, width - 94, size=9, leading=12, color=color)
    if value and value.startswith("https://"):
        pdf.linkURL(value, (x_value, y_next + 10, x + width, y + 10), relative=0)
    return y_next - 4


def architecture(pdf):
    x, y, width, height = 42, 221, A4[0] - 84, 324
    pdf.setFillColor(LIGHT)
    pdf.roundRect(x, y, width, height, 10, fill=1, stroke=0)
    box(pdf, 56, 495, 95, 31, "Cliente")
    box(pdf, 204, 495, 171, 31, "HTTP API + Lambda CPF")
    box(pdf, 429, 495, 110, 31, "Lookup privado OS")
    arrow(pdf, 151, 510, 204, 510)
    arrow(pdf, 375, 510, 429, 510)

    box(pdf, 58, 414, 125, 40, "OS + Saga", GREEN, 9)
    box(pdf, 227, 414, 125, 40, "Execução", GREEN, 9)
    box(pdf, 396, 414, 125, 40, "Billing", GREEN, 9)
    pdf.setStrokeColor(GRAY)
    pdf.line(484, 495, 484, 472)
    pdf.line(484, 472, 120, 472)
    arrow(pdf, 120, 472, 120, 454)
    box(pdf, 183, 350, 229, 34, "SQS + DLQ / eventos assíncronos", BLUE, 8)
    pdf.line(120, 414, 120, 367)
    arrow(pdf, 120, 367, 183, 367)
    arrow(pdf, 289, 414, 289, 384)
    pdf.line(458, 414, 458, 367)
    arrow(pdf, 458, 367, 412, 367)
    box(pdf, 58, 286, 125, 37, "RDS: oficina_os", white, 8)
    box(pdf, 227, 286, 125, 37, "RDS: oficina_execution", white, 7)
    box(pdf, 396, 286, 125, 37, "DynamoDB: Billing", white, 8)
    pdf.setFont("Helvetica", 7)
    pdf.setFillColor(GRAY)
    pdf.drawCentredString(298, 330, "persistência própria por serviço")
    box(pdf, 151, 237, 294, 28, "OpenTelemetry / Prometheus / Grafana", white, 8)


def draw_first_page(pdf, data, complete):
    width, height = A4
    pdf.setFillColor(NAVY)
    pdf.rect(0, height - 91, width, 91, fill=1, stroke=0)
    pdf.setFillColor(white)
    pdf.setFont("Helvetica-Bold", 19)
    pdf.drawString(42, height - 49, "Tech Challenge - Fase 4")
    pdf.setFont("Helvetica", 10)
    pdf.drawString(42, height - 69, "Arquitetura distribuída da oficina mecânica")
    if not complete:
        pdf.setFillColor(AMBER)
        pdf.roundRect(42, 720, width - 84, 24, 5, fill=1, stroke=0)
        pdf.setFillColor(HexColor("#8a5500"))
        pdf.setFont("Helvetica-Bold", 9)
        pdf.drawCentredString(width / 2, 728, "RASCUNHO - links ou confirmação do colaborador pendentes")

    y = 704 if not complete else 729
    y = heading(pdf, "Identificação e links", 42, y)
    participants = data.get("participants") or []
    names = "; ".join(f"{item.get('name', '')} (RM {item.get('rm', '')})"
                      for item in participants if item.get("name") and item.get("rm"))
    y = metadata_row(pdf, "Participantes", names, 42, y, width - 84)
    repositories = data.get("repositories") or {}
    for label, key in (("OS", "os"), ("Execução", "execution"), ("Billing", "billing"),
                       ("Auth Lambda", "auth"), ("Infra K8s", "k8s_infra"),
                       ("Infra Banco", "data_infra")):
        y = metadata_row(pdf, label, repositories.get(key, ""), 42, y, width - 84)
    y = metadata_row(pdf, "Vídeo (15 min)", data.get("video_url", ""), 42, y, width - 84)
    y = metadata_row(pdf, "Documentação", data.get("documentation_url", ""), 42, y, width - 84)
    collaborator = ("Confirmado nos seis repositórios" if data.get("soat_architecture_added") else "")
    y = metadata_row(pdf, "soat-architecture", collaborator, 42, y, width - 84)

    architecture_heading_y = min(568, y - 14)
    heading(pdf, "Arquitetura geral", 42, architecture_heading_y)
    pdf.saveState()
    pdf.translate(0, architecture_heading_y - 568)
    architecture(pdf)
    pdf.restoreState()
    footer(pdf, 1)
    pdf.showPage()


def draw_second_page(pdf):
    width, height = A4
    pdf.setFillColor(NAVY)
    pdf.rect(0, height - 77, width, 77, fill=1, stroke=0)
    pdf.setFillColor(white)
    pdf.setFont("Helvetica-Bold", 17)
    pdf.drawString(42, height - 47, "Saga, divisão e tecnologias")
    y = heading(pdf, "Estratégia: Saga orquestrada pela OS", 42, 735)
    y = paragraph(pdf,
                  "A OS é dona do estado da ordem e coordena as transições. Cada serviço grava "
                  "sua transação local e publica eventos por Outbox; os consumidores usam Inbox "
                  "e idempotência para tolerar duplicações e reprocessamento.",
                  42, y, width - 84, size=10, leading=15) - 8
    steps = [
        "1. Abertura: Execução fornece cotação; a OS persiste a ordem e publica WorkOrderOpened.",
        "2. Billing gera o orçamento e publica BudgetReady; a OS aguarda a aprovação.",
        "3. Após aprovação, Execução reserva estoque; falha de reserva encerra a tentativa sem cobrar.",
        "4. Billing cria tentativa de pagamento idempotente; só confirmação verificada libera execução.",
        "5. Execução consome a reserva, inicia e conclui o reparo; a OS registra o histórico.",
        "6. Expiração sem pagamento libera estoque. Recusa de reparo iniciado leva a OS para revisão manual e permite solicitar estorno."
    ]
    for step in steps:
        y = paragraph(pdf, step, 47, y, width - 94, size=9.5, leading=14) - 6

    y -= 4
    y = heading(pdf, "Justificativa da divisão", 42, y)
    y = paragraph(pdf,
                  "OS mantém clientes, veículos, ciclo de vida e orquestração. Execução concentra "
                  "catálogo, estoque e reparo, onde transações SQL e concorrência são importantes. "
                  "Billing concentra orçamento, tentativas e reconciliação de pagamentos, com "
                  "operações condicionais no DynamoDB. Essa separação permite pipelines e dados "
                  "independentes e reaproveita as regras .NET existentes.",
                  42, y, width - 84, size=10, leading=15) - 12

    y = heading(pdf, "Tecnologias e custo da demonstração", 42, y)
    y = paragraph(pdf,
                  ".NET 10 nos três serviços e na Lambda; PostgreSQL para OS/Execução; DynamoDB "
                  "para Billing; SQS com DLQ; K3s em uma EC2; Terraform; GitHub Actions com OIDC; "
                  "OpenTelemetry, Prometheus e Grafana. Uma única EC2 e RDS Single-AZ reduzem "
                  "o custo da janela curta. A infraestrutura AWS só deve ser criada após revisão "
                  "do plan e autorização, e removida após a apresentação autorizada.",
                  42, y, width - 84, size=10, leading=15)
    y -= 15
    y = paragraph(pdf,
                  "OS e Execução têm databases e usuários PostgreSQL próprios na instância RDS. "
                  "Billing usa uma tabela DynamoDB própria. Nenhum serviço acessa diretamente "
                  "o banco de outro. REST atende cotações; SQS transporta eventos e comandos.",
                  42, y, width - 84, size=9, leading=13) - 9
    y = paragraph(pdf,
                  "GitHub Actions usa OIDC para publicar imagens no ECR e acionar rollout K3s por SSM. "
                  "A autenticação CPF/JWT é uma Lambda .NET exposta por HTTP API; a consulta à OS "
                  "ocorre pela rede privada. Terraform provisiona a infraestrutura da demonstração.",
                  42, y, width - 84, size=9, leading=13) - 15
    pdf.setFillColor(LIGHT)
    pdf.roundRect(42, y - 40, width - 84, 51, 6, fill=1, stroke=0)
    paragraph(pdf,
              "Evidências, limites e instruções de execução: consultar os READMEs de cada "
              "repositório, a matriz docs/evidence/phase4-status.md e o roteiro docs/demo-15-min.md.",
              53, y - 3, width - 106, size=9, leading=13)
    footer(pdf, 2)
    pdf.showPage()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--metadata", type=Path, default=ROOT / "docs/submission-metadata.json")
    parser.add_argument("--output", type=Path,
                        default=ROOT / "output/pdf/fiap-fase4-entrega-rascunho.pdf")
    args = parser.parse_args()
    data = json.loads(args.metadata.read_text(encoding="utf-8")) if args.metadata.exists() else {}
    participants = data.get("participants") or []
    repositories = data.get("repositories") or {}
    complete = (bool(participants) and all(item.get("name") and item.get("rm") for item in participants)
                and all(str(repositories.get(key, "")).startswith("https://")
                        for key in ("os", "execution", "billing", "auth", "k8s_infra", "data_infra"))
                and str(data.get("video_url", "")).startswith("https://")
                and str(data.get("documentation_url", "")).startswith("https://")
                and data.get("soat_architecture_added") is True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    pdf = canvas.Canvas(str(args.output), pagesize=A4)
    pdf.setTitle("Tech Challenge FIAP - Fase 4" + ("" if complete else " - Rascunho"))
    pdf.setAuthor(", ".join(item["name"] for item in participants if item.get("name"))
                  or "Equipe Tech Challenge")
    draw_first_page(pdf, data, complete)
    draw_second_page(pdf)
    pdf.save()
    print(f"PDF: {args.output}")
    print("Estado: pronto para revisão" if complete else "Estado: rascunho; preencher metadados e gerar novamente")


if __name__ == "__main__":
    main()
