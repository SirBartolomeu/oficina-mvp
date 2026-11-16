# language: pt
Funcionalidade: Publicação concorrente da Outbox da OS
  Duas réplicas da OS podem disputar mensagens pendentes no PostgreSQL

  Cenário: Apenas um dispatcher publica o evento da OS
    Dado que a Outbox da OS usa PostgreSQL isolado
    Quando dois dispatchers disputam a mesma mensagem
    Então a mensagem é publicada uma vez e marcada como publicada
