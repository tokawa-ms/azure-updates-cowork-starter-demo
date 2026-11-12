$ErrorActionPreference = 'Stop'
$values = azd env get-values --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'azd の環境を読み込めません。' }
$endpoint = $values.FOUNDRY_PROJECT_ENDPOINT.TrimEnd('/')
$name = $values.AZURE_AI_AGENT_NAME
if (-not $endpoint -or -not $name) {
    throw '先に azd provision を実行してください。'
}
$names = @($name, "$name-eval")
$roles = @(
    @{ Role = 'Cognitive Services User'; Scope = $values.AZURE_AI_ACCOUNT_ID }
)
foreach ($agentName in $names) {
    $principal = az rest --method get --resource https://ai.azure.com `
        --url "$endpoint/agents/$($agentName)?api-version=v1" --query instance_identity.principal_id --output tsv
    if ($LASTEXITCODE -ne 0 -or -not $principal) { throw "エージェント ID を取得できません: $agentName。両方のエージェントを先にデプロイしてください。" }
    foreach ($assignment in $roles) {
        az role assignment create --assignee-object-id $principal --assignee-principal-type ServicePrincipal `
            --role $assignment.Role --scope $assignment.Scope --only-show-errors --output none
        if ($LASTEXITCODE -ne 0) { throw "ロールの割り当てに失敗しました: $agentName / $($assignment.Role)" }
    }
}
Write-Output "Web: $($values.WEB_URL)"
