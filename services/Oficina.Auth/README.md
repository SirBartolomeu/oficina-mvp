# Oficina.Auth

Function Serverless .NET 10 para autenticação de cliente por CPF na Fase 3. O handler `Oficina.Auth::Oficina.Auth.Function::FunctionHandler` recebe o evento HTTP API v2 do API Gateway como `Stream`; a rota planejada é `POST /auth/token`. A função valida os dígitos do CPF, consulta a OS por `POST /internal/customers/lookup` com token de serviço e emite JWT de cliente com validade de 15 minutos. CPF não entra na URL, no JWT ou nos logs da função. Cliente ausente, inativo ou CPF inválido recebe a mesma resposta 401.

Configuração por ambiente/segredos:

| Variável | Uso |
|---|---|
| `AUTH_LOOKUP_URL` | URL completa do endpoint interno da OS |
| `AUTH_OS_SECRET_ARN` | ARN da entrada da OS no Secrets Manager, contendo `Auth__LookupToken` e `Jwt__Key` |
| `AUTH_JWT_ISSUER`, `AUTH_JWT_AUDIENCE` | Mesmo emissor e audiência aceitos pela OS |

Execução local: `dotnet test services/Oficina.Auth.Tests`. `scripts/aws-demo/build-auth-lambda.ps1` gera o ZIP usado pelo Terraform em `infra/aws-demo`. O módulo cria runtime gerenciado `dotnet10`, HTTP API, acesso privado à OS e endpoint privado do Secrets Manager. A Function busca o segredo da OS na primeira invocação bem sucedida e o mantém em memória durante a vida da instância aquecida; o ARN, sem valores, fica no Terraform. Não é necessário pacote `Amazon.Lambda.Core` porque o handler recebe e devolve `Stream`. O deploy remoto e a invocação real continuam pendentes.

CPF sozinho atende ao enunciado acadêmico, mas não comprova posse da identidade; limitar taxa no gateway e usar segundo fator antes de exposição real de dados pessoais. O serviço consulta a API da OS, sem acesso ao PostgreSQL.
