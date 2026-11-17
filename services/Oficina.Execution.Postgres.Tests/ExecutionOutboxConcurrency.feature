# language: pt
Funcionalidade: Publicação concorrente da Outbox da Execução
  Duas réplicas da Execução podem disputar mensagens pendentes no PostgreSQL

  Cenário: Apenas um dispatcher publica o evento da Execução
    Dado que a Outbox da Execução usa PostgreSQL isolado
    Quando dois dispatchers da Execução disputam a mesma mensagem
    Então o evento da Execução é publicado uma vez
