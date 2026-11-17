# Fechamento da entrega Fase 4

1. Após publicar os seis repositórios e gravar o vídeo de até 15 minutos, copie `docs/submission-metadata.example.json` para `docs/submission-metadata.json` se o arquivo local ainda não existir.
2. Preencha nome e RM de cada participante, URLs HTTPS dos repositórios OS, Execução, Billing, Auth Lambda, infraestrutura Kubernetes e infraestrutura do banco, URL pública ou não listada do vídeo e URL da documentação arquitetural. Marque `soat_architecture_added` como `true` somente após confirmar o colaborador nos seis repositórios. Não inclua tokens ou credenciais.
3. Execute `python scripts/generate-phase4-submission.py --output output/pdf/fiap-fase4-entrega.pdf` usando Python com `reportlab` instalado.
4. Abra as duas páginas. Confira identificação, URLs clicáveis, arquitetura, Saga, tecnologia, confirmação do colaborador e ausência do aviso **RASCUNHO**. Teste os links antes de enviar o arquivo ao portal FIAP.

O gerador com links pendentes produz `output/pdf/fiap-fase4-entrega-rascunho.pdf` com aviso visível. Esse arquivo não é a entrega final. O documento solicitado pelos enunciados depende dos seis repositórios, da documentação e do vídeo publicados.
