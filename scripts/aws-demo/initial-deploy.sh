#!/usr/bin/env bash
# Executar na EC2 via Session Manager apos upload do pacote privado no S3.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
config="$root/deployment-config.json"
if [[ ! -f "$config" ]]; then
  echo 'deployment-config.json ausente do pacote' >&2
  exit 2
fi

value() {
  python3 - "$config" "$@" <<'PY'
import json, sys
with open(sys.argv[1], encoding='utf-8-sig') as handle:
    current = json.load(handle)
for key in sys.argv[2:]:
    current = current[key]
print(current)
PY
}

region="$(value awsRegion)"
declare -A images
for service in os execution billing; do
  repository="$(value ecrRepositoryNames "$service")"
  uri="$(value ecrRepositories "$service")"
  if ! tag="$(aws ecr describe-images --region "$region" --repository-name "$repository" --output json |
    python3 "$root/scripts/aws-demo/select-ecr-sha.py")"; then
    echo "Imagem ECR com SHA ausente para $service; publique os tres pipelines antes do deploy inicial." >&2
    exit 1
  fi
  images[$service]="$uri:$tag"
done

bash "$root/scripts/aws-demo/bootstrap-rds-and-app-secrets.sh" \
  "$region" \
  "$(value rdsEndpoint)" \
  "$(value rdsMasterSecretArn)" \
  "$(value applicationSecretArns os)" \
  "$(value applicationSecretArns execution)" \
  "$(value applicationSecretArns billing)"

bash "$root/scripts/aws-demo/prepare-workload-credentials.sh" \
  "$region" \
  "$(value workloadRoleArns os)" \
  "$(value workloadRoleArns execution)" \
  "$(value workloadRoleArns billing)"

kubectl=(sudo /usr/local/bin/k3s kubectl)
"${kubectl[@]}" apply -k "$root/k8s/monitoring"
"${kubectl[@]}" kustomize "$root/k8s/distributed" |
  python3 "$root/scripts/aws-demo/render-distributed.py" "$config" "${images[os]}" "${images[execution]}" "${images[billing]}" |
  "${kubectl[@]}" apply -f -

for deployment in oficina-api execution-api billing-api; do
  "${kubectl[@]}" -n oficina rollout status "deployment/$deployment" --timeout=300s
done
bash "$root/scripts/aws-demo/smoke-k3s.sh"
"${kubectl[@]}" -n oficina get pods,svc,hpa
echo 'Deploy inicial concluido; verificar as APIs por healthcheck e registrar evidencias.'
