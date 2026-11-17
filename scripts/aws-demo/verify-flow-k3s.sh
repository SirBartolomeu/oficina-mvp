#!/usr/bin/env bash
# Executar no no K3s depois do deploy inicial; cria uma OS de demonstracao.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
kubectl=(sudo /usr/local/bin/k3s kubectl)
temp_dir="$(mktemp -d)"
declare -a pids=()
cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  wait 2>/dev/null || true
  rm -f "$temp_dir"/*.log
  rmdir "$temp_dir"
}
trap cleanup EXIT

export OFICINA_ADMIN_PASSWORD
export OFICINA_EXECUTION_TOKEN
OFICINA_ADMIN_PASSWORD="$("${kubectl[@]}" -n oficina get secret oficina-os-secret -o jsonpath='{.data.AdminCredentials__Password}' | base64 --decode)"
OFICINA_EXECUTION_TOKEN="$("${kubectl[@]}" -n oficina get secret oficina-execution-secret -o jsonpath='{.data.InternalToken}' | base64 --decode)"
if [[ -z "$OFICINA_ADMIN_PASSWORD" || -z "$OFICINA_EXECUTION_TOKEN" ]]; then
  echo 'Credenciais de demonstracao ausentes' >&2
  exit 1
fi

for entry in 'oficina-api:18080' 'execution-api:18081'; do
  service="${entry%%:*}"
  port="${entry##*:}"
  "${kubectl[@]}" -n oficina port-forward --address 127.0.0.1 "svc/$service" "$port:80" >"$temp_dir/$service.log" 2>&1 &
  pids+=("$!")
  ready=false
  for attempt in {1..30}; do
    if curl --silent --fail --max-time 2 "http://127.0.0.1:$port/health/ready" >/dev/null 2>&1; then
      ready=true
      break
    fi
    if ! kill -0 "${pids[-1]}" 2>/dev/null; then break; fi
    sleep 1
  done
  if [[ "$ready" != true ]]; then
    echo "Port-forward ou readiness falhou: $service" >&2
    cat "$temp_dir/$service.log" >&2
    exit 1
  fi
done

export OFICINA_OS_URL='http://127.0.0.1:18080'
export OFICINA_EXECUTION_URL='http://127.0.0.1:18081'
export OFICINA_AUTH_URL="$(python3 - "$root/deployment-config.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding='utf-8-sig') as source:
    print(json.load(source)['authTokenUrl'])
PY
)"
python3 "$root/scripts/aws-demo/verify-flow.py"
