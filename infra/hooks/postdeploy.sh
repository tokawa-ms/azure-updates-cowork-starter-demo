#!/usr/bin/env sh
set -eu
endpoint=$(azd env get-value FOUNDRY_PROJECT_ENDPOINT)
name=$(azd env get-value AZURE_AI_AGENT_NAME)
account=$(azd env get-value AZURE_AI_ACCOUNT_ID)
for agent in "$name" "${name}-eval"; do
  principal=$(az rest --method get --resource https://ai.azure.com \
    --url "${endpoint%/}/agents/${agent}?api-version=v1" --query instance_identity.principal_id -o tsv)
  test -n "$principal" || { echo "エージェント ID を取得できません: ${agent}" >&2; exit 1; }
  az role assignment create --assignee-object-id "$principal" --assignee-principal-type ServicePrincipal \
    --role "Cognitive Services User" --scope "$account" --only-show-errors -o none
done
printf 'Web: %s\n' "$(azd env get-value WEB_URL)"
