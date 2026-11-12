#!/usr/bin/env sh
# リポジトリのルートで . ./tools/env/load-env.sh として読み込む。
export FOUNDRY_PROJECT_ENDPOINT="$(azd env get-value FOUNDRY_PROJECT_ENDPOINT)"
export AZURE_AI_MODEL_DEPLOYMENT_NAME="$(azd env get-value AZURE_AI_MODEL_DEPLOYMENT_NAME)"
export AZURE_TOKEN_CREDENTIALS=AzureCliCredential
export ARTIFACTS_MODE=Local
export ARTIFACTS_LOCAL_DIR="$PWD/.artifacts"
export AZURE_UPDATES_FIXTURES_DIR="$PWD/eval/fixtures/updates"
export PROMPT_SHIELDS_MODE=Off
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
