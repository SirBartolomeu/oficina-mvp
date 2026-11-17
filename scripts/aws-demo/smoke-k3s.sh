#!/usr/bin/env bash
# Executar no no K3s; valida readiness HTTP dos tres servicos sem expor portas publicas.
set -euo pipefail

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

for entry in 'oficina-api:18080' 'execution-api:18081' 'billing-api:18082'; do
  service="${entry%%:*}"
  port="${entry##*:}"
  "${kubectl[@]}" -n oficina port-forward --address 127.0.0.1 "svc/$service" "$port:80" >"$temp_dir/$service.log" 2>&1 &
  pids+=("$!")
  ready=false
  for attempt in {1..30}; do
    if curl --silent --show-error --fail --max-time 2 "http://127.0.0.1:$port/health/ready" >/dev/null 2>&1; then
      ready=true
      break
    fi
    if ! kill -0 "${pids[-1]}" 2>/dev/null; then break; fi
    sleep 1
  done
  if [[ "$ready" != true ]]; then
    echo "Readiness HTTP falhou: $service" >&2
    cat "$temp_dir/$service.log" >&2
    exit 1
  fi
  echo "$service /health/ready: HTTP 200"
done

"${kubectl[@]}" -n oficina get deployments,pods,services
