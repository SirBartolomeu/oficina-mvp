#!/usr/bin/env bash
# Executar na EC2 K3s pouco antes da apresentacao, depois do deploy inicial.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
config="$root/deployment-config.json"
if [[ ! -f "$config" ]]; then
  echo 'deployment-config.json ausente do pacote' >&2
  exit 2
fi

mapfile -t settings < <(python3 - "$config" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8-sig") as source:
    config = json.load(source)
print(config["awsRegion"])
for service in ("os", "execution", "billing"):
    print(config["workloadRoleArns"][service])
PY
)
if [[ ${#settings[@]} -ne 4 ]]; then
  echo 'Regiao ou roles de workload ausentes' >&2
  exit 2
fi

bash "$root/scripts/aws-demo/prepare-workload-credentials.sh" "${settings[@]}"
kubectl=(sudo /usr/local/bin/k3s kubectl)
for deployment in oficina-api execution-api billing-api; do
  "${kubectl[@]}" -n oficina rollout restart "deployment/$deployment"
  "${kubectl[@]}" -n oficina rollout status "deployment/$deployment" --timeout=300s
done
bash "$root/scripts/aws-demo/smoke-k3s.sh"
echo 'Credenciais temporarias renovadas e tres servicos prontos para a demo.'
