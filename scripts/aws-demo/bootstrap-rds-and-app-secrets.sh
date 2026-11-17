#!/usr/bin/env bash
# Executar na EC2 K3s uma vez apos o RDS ficar disponivel; nao usar bash -x.
set -euo pipefail
umask 077

if [[ $# -ne 6 ]]; then
  echo 'Uso: bootstrap-rds-and-app-secrets.sh REGIAO RDS_HOST MASTER_SECRET_ARN OS_SECRET_ARN EXECUTION_SECRET_ARN BILLING_SECRET_ARN' >&2
  exit 2
fi

region="$1"
rds_host="$2"
master_arn="$3"
os_arn="$4"
execution_arn="$5"
billing_arn="$6"
kubectl=(sudo /usr/local/bin/k3s kubectl)

if ! command -v psql >/dev/null; then
  sudo dnf install -y postgresql16 >/dev/null
fi

master_json="$(aws secretsmanager get-secret-value --region "$region" --secret-id "$master_arn" --query SecretString --output text)"
mapfile -t master < <(printf '%s' "$master_json" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d["username"]); print(d["password"])')
unset master_json
if [[ ${#master[@]} -ne 2 || -z ${master[0]} || -z ${master[1]} ]]; then
  echo 'Segredo mestre do RDS incompleto' >&2
  exit 1
fi

os_password="$(openssl rand -hex 24)"
execution_password="$(openssl rand -hex 24)"
execution_token="$(openssl rand -hex 32)"
export PGPASSWORD="${master[1]}"
psql -h "$rds_host" -U "${master[0]}" -d postgres -v ON_ERROR_STOP=1 <<SQL >/dev/null
SELECT 'CREATE ROLE oficina_os LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oficina_os') \gexec
ALTER ROLE oficina_os PASSWORD '$os_password';
SELECT 'CREATE ROLE oficina_execution LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oficina_execution') \gexec
ALTER ROLE oficina_execution PASSWORD '$execution_password';
SELECT 'CREATE DATABASE oficina_os OWNER oficina_os' WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'oficina_os') \gexec
SELECT 'CREATE DATABASE oficina_execution OWNER oficina_execution' WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'oficina_execution') \gexec
REVOKE CONNECT ON DATABASE oficina_os FROM PUBLIC;
REVOKE CONNECT ON DATABASE oficina_execution FROM PUBLIC;
GRANT CONNECT ON DATABASE oficina_os TO oficina_os;
GRANT CONNECT ON DATABASE oficina_execution TO oficina_execution;
SQL
unset PGPASSWORD master

temp_dir="$(mktemp -d)"
cleanup() {
  rm -f "$temp_dir/os.json" "$temp_dir/execution.json" "$temp_dir/billing.json"
  rmdir "$temp_dir"
}
trap cleanup EXIT

printf '{"ConnectionStrings__DefaultConnection":"Host=%s;Port=5432;Database=oficina_os;Username=oficina_os;Password=%s","Jwt__Key":"%s","AdminCredentials__Password":"%s","Integration__Token":"%s","Auth__LookupToken":"%s","Execution__InternalToken":"%s"}' \
  "$rds_host" "$os_password" "$(openssl rand -hex 32)" "$(openssl rand -hex 20)" "$(openssl rand -hex 32)" "$(openssl rand -hex 32)" "$execution_token" > "$temp_dir/os.json"
printf '{"ConnectionStrings__DefaultConnection":"Host=%s;Port=5432;Database=oficina_execution;Username=oficina_execution;Password=%s","InternalToken":"%s"}' \
  "$rds_host" "$execution_password" "$execution_token" > "$temp_dir/execution.json"
printf '{"InternalToken":"%s"}' "$(openssl rand -hex 32)" > "$temp_dir/billing.json"
unset os_password execution_password execution_token

aws secretsmanager put-secret-value --region "$region" --secret-id "$os_arn" --secret-string "file://$temp_dir/os.json" >/dev/null
aws secretsmanager put-secret-value --region "$region" --secret-id "$execution_arn" --secret-string "file://$temp_dir/execution.json" >/dev/null
aws secretsmanager put-secret-value --region "$region" --secret-id "$billing_arn" --secret-string "file://$temp_dir/billing.json" >/dev/null

"${kubectl[@]}" create namespace oficina --dry-run=client -o yaml | "${kubectl[@]}" apply -f - >/dev/null
for service in os execution billing; do
  secret_arn="${service}_arn"
  aws secretsmanager get-secret-value --region "$region" --secret-id "${!secret_arn}" --query SecretString --output text |
    python3 -c '
import base64, json, sys
values = json.load(sys.stdin)
data = {key: base64.b64encode(value.encode()).decode() for key, value in values.items()}
print(json.dumps({"apiVersion": "v1", "kind": "Secret", "metadata": {"name": "oficina-" + sys.argv[1] + "-secret", "namespace": "oficina"}, "type": "Opaque", "data": data}))
' "$service" | "${kubectl[@]}" apply -f - >/dev/null
done

echo 'Databases, usuarios e Secrets de aplicacao preparados. Nao exibir os valores em logs ou capturas.'
