#!/usr/bin/env bash
# Executar na EC2 K3s, por Session Manager, pouco antes do deploy.
set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo 'Uso: prepare-workload-credentials.sh REGIAO ROLE_OS ROLE_EXECUTION ROLE_BILLING' >&2
  exit 2
fi

region="$1"
declare -A roles=(
  [os]="$2"
  [execution]="$3"
  [billing]="$4"
)

kubectl=(sudo /usr/local/bin/k3s kubectl)
"${kubectl[@]}" get namespace oficina >/dev/null

account="$(aws sts get-caller-identity --region "$region" --query Account --output text)"
registry="$account.dkr.ecr.$region.amazonaws.com"
aws ecr get-login-password --region "$region" |
  python3 -c '
import base64, json, sys
password = sys.stdin.read().strip()
if not password:
    raise SystemExit("ECR nao retornou token")
registry = sys.argv[1]
auth = base64.b64encode(("AWS:" + password).encode()).decode()
docker_config = {"auths": {registry: {"auth": auth}}}
encoded = base64.b64encode(json.dumps(docker_config).encode()).decode()
secret = {"apiVersion": "v1", "kind": "Secret", "metadata": {"name": "oficina-ecr-pull", "namespace": "oficina"}, "type": "kubernetes.io/dockerconfigjson", "data": {".dockerconfigjson": encoded}}
print(json.dumps(secret))
' "$registry" | "${kubectl[@]}" apply -f - >/dev/null
echo 'Credencial temporaria de pull ECR configurada (validade maxima: 12h).'

for service in os execution billing; do
  role="${roles[$service]}"
  if [[ "$role" != arn:aws:iam::*:role/* ]]; then
    echo "ARN invalido para $service" >&2
    exit 2
  fi

  read -r access_key secret_key session_token < <(
    aws sts assume-role \
      --region "$region" \
      --role-arn "$role" \
      --role-session-name "oficina-demo-$service" \
      --duration-seconds 3600 \
      --query 'Credentials.[AccessKeyId,SecretAccessKey,SessionToken]' \
      --output text
  )
  if [[ -z "$access_key" || -z "$secret_key" || -z "$session_token" ]]; then
    echo "STS nao retornou credenciais para $service" >&2
    exit 1
  fi

  name="oficina-$service-aws-temporary"
  "${kubectl[@]}" -n oficina create secret generic "$name" \
    --from-literal=AWS_ACCESS_KEY_ID="$access_key" \
    --from-literal=AWS_SECRET_ACCESS_KEY="$secret_key" \
    --from-literal=AWS_SESSION_TOKEN="$session_token" \
    --from-literal=AWS_REGION="$region" \
    --dry-run=client -o yaml | "${kubectl[@]}" apply -f - >/dev/null
  unset access_key secret_key session_token
  echo "Credencial temporaria de $service configurada (validade maxima: 1h)."
done

echo 'Reinicie os Deployments apos renovar as credenciais; remova os Secrets ao encerrar a demo.'
