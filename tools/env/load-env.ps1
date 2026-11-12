$ErrorActionPreference = 'Stop'
$values = azd env get-values --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'azd の環境を読み込めません。' }
foreach ($name in @('FOUNDRY_PROJECT_ENDPOINT', 'AZURE_AI_MODEL_DEPLOYMENT_NAME')) {
    if ($values.$name) { Set-Item "Env:$name" $values.$name }
}
$env:AZURE_TOKEN_CREDENTIALS = 'AzureCliCredential'
$env:ARTIFACTS_MODE = 'Local'
$env:ARTIFACTS_LOCAL_DIR = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\.artifacts'))
$env:AZURE_UPDATES_FIXTURES_DIR = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\eval\fixtures\updates'))
$env:PROMPT_SHIELDS_MODE = 'Off'
$env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://localhost:4317'
