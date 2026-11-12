[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProjectEndpoint,
    [Parameter(Mandatory)][ValidatePattern('^\d+$')][string]$AgentVersion,
    [ValidatePattern('^[a-zA-Z0-9-]+-eval$')][string]$AgentName = 'updates-cowork-eval',
    [string]$Judge = 'gpt-5-mini',
    [ValidateRange(1, 100)][int]$Limit = 100,
    [string]$Out = (Join-Path $PSScriptRoot ('out\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [ValidateRange(1, 120)][int]$MaxWaitMinutes = 45,
    [switch]$Resume
)
$ErrorActionPreference = 'Stop'
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONUTF8 = '1'
$endpoint = [Uri]$ProjectEndpoint
if ($endpoint.Scheme -ne 'https' -or $endpoint.UserInfo -or $endpoint.Query -or $endpoint.Fragment `
    -or -not $endpoint.Host.EndsWith('.services.ai.azure.com') -or $endpoint.AbsolutePath -notmatch '^/api/projects/[^/]+/?$') {
    throw 'Foundry プロジェクトの HTTPS エンドポイントを指定してください。'
}
$ProjectEndpoint = $ProjectEndpoint.TrimEnd('/')
$Out = [IO.Path]::GetFullPath($Out)
$runner = Join-Path $PSScriptRoot 'UpdatesCowork.EvalRunner'
$azCommand = (Get-Command az -ErrorAction Stop).Source
$azPrefix = @()
if ($IsWindows -and [IO.Path]::GetExtension($azCommand) -eq '.cmd') {
    # Windows の .cmd を挟むと URL の & が分割されるため、同じ公式 CLI のエントリーポイントを使う。
    $azCommand = [IO.Path]::GetFullPath((Join-Path (Split-Path $azCommand) '..\python.exe'))
    $azPrefix = @('-IBm', 'azure.cli')
}

function Invoke-Foundry([string]$Method, [string]$RelativePath, [string]$File, [string]$Body,
    [string[]]$Headers = @(), [switch]$RetrySessionCreation) {
    $arguments = @('rest', '--method', $Method, '--resource', 'https://ai.azure.com',
        '--url', "$ProjectEndpoint/$RelativePath", '--only-show-errors', '--output', 'none')
    if ($File) { $arguments += @('--output-file', $File) }
    if ($Body) { $Headers += 'Content-Type=application/json'; $arguments += @('--body', "@$Body") }
    if ($Headers.Count) { $arguments += @('--headers') + $Headers }
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $messages = @(& $azCommand @azPrefix @arguments 2>&1)
        $code = $LASTEXITCODE
        $messages | ForEach-Object { Write-Output $_ }
        if ($code -eq 0) { return }
        # 作成に失敗したと明示されたセッションだけを再試行し、応答生成や評価登録を無条件に重複させない。
        $retryable = $RetrySessionCreation -and $Method -eq 'post' `
            -and $RelativePath -eq "agents/$AgentName/endpoint/protocols/openai/responses?api-version=v1" `
            -and ($messages -join "`n") -match '"code"\s*:\s*"session_creation_failed"'
        if (-not $retryable -or $attempt -eq 3) {
            throw "Foundry の要求が失敗しました: $Method $RelativePath"
        }
        $delay = 10 * $attempt
        Write-Warning "Foundry のセッション作成に一時的に失敗しました。$delay 秒後に再試行します ($($attempt + 1)/3)。"
        Start-Sleep -Seconds $delay
    }
}

function Invoke-Runner([string[]]$Arguments) {
    dotnet run --no-build --project $runner -- @Arguments
    if ($LASTEXITCODE -ne 0) { throw "評価ツールが失敗しました (exit $LASTEXITCODE)。" }
}

dotnet build $runner --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw '評価ツールのビルドに失敗しました。' }

New-Item $Out -ItemType Directory -Force | Out-Null
$runFile = Join-Path $Out 'run.json'
if (-not $Resume) {
    if (Test-Path $runFile) { throw '同じ出力先に実行記録があります。再取得は -Resume、新しい評価は別の -Out を使ってください。' }
    if (Get-ChildItem (Join-Path $Out 'responses') -Filter '*.json' -ErrorAction SilentlyContinue) {
        throw '収集済みの応答があります。-Resume または新しい -Out を使ってください。'
    }
    Invoke-Runner -Arguments @('prepare', '--dataset', (Join-Path $PSScriptRoot 'datasets\core.jsonl'),
        '--fixtures', (Join-Path $PSScriptRoot 'fixtures\updates'), '--agent', $AgentName,
        '--agent-version', $AgentVersion, '--judge', $Judge, '--out', $Out, '--limit', "$Limit")
}
$requestFile = Join-Path $Out 'run-request.json'
$request = Get-Content $requestFile -Raw | ConvertFrom-Json -AsHashtable
if ($request.metadata.agent_name -ne $AgentName -or $request.metadata.agent_version -ne $AgentVersion `
    -or $request.metadata.response_source -ne 'recorded_hosted_response') {
    throw '-Resume の対象エージェントと版が保存された要求と一致しません。'
}
if (-not (Test-Path $runFile)) {
    $responsesDirectory = Join-Path $Out 'responses'
    New-Item $responsesDirectory -ItemType Directory -Force | Out-Null
    foreach ($row in $request.data_source.source.content) {
        $id = $row.item.id
        if ($id -notmatch '^[A-Za-z0-9-]{1,200}$') { throw '評価ケース ID の形式が不正です。' }
        $responseFile = Join-Path $responsesDirectory ($id + '.json')
        if (-not (Test-Path $responseFile)) {
            Write-Output "固定データで応答を収集: $id"
            Invoke-Foundry 'post' "agents/$AgentName/endpoint/protocols/openai/responses?api-version=v1" `
                $responseFile (Join-Path $Out "requests\$id.json") @("x-agent-version-override=$AgentVersion") -RetrySessionCreation
        }
        $response = Get-Content $responseFile -Raw | ConvertFrom-Json
        if ($response.status -ne 'completed') { Write-Warning "エージェントの実行は $($response.status) です: $id" }
    }
    Invoke-Runner -Arguments @('attach', '--out', $Out)
    $definitionFile = Join-Path $Out 'evaluation.json'
    if (-not (Test-Path $definitionFile)) {
        Invoke-Foundry 'post' 'openai/v1/evals' $definitionFile (Join-Path $Out 'definition.json')
    }
    $evaluation = Get-Content $definitionFile -Raw | ConvertFrom-Json
    if ($evaluation.id -notmatch '^eval_[a-zA-Z0-9]+$') { throw '評価 ID の形式が不正です。' }
    Invoke-Foundry 'post' "openai/v1/evals/$($evaluation.id)/runs" $runFile $requestFile
}
$run = Get-Content $runFile -Raw | ConvertFrom-Json
if ($run.id -notmatch '^evalrun_[a-zA-Z0-9]+$' -or $run.eval_id -notmatch '^eval_[a-zA-Z0-9]+$') {
    throw '保存された実行 ID の形式が不正です。'
}
$runPath = "openai/v1/evals/$($run.eval_id)/runs/$($run.id)"
$deadline = [DateTimeOffset]::UtcNow.AddMinutes($MaxWaitMinutes)
Write-Output "評価を実行中: $($run.id)"
while ($run.status -notin @('completed', 'failed', 'canceled', 'cancelled')) {
    if ([DateTimeOffset]::UtcNow -gt $deadline) { throw "待機時間を超えました。$Out を -Resume で再取得できます。" }
    Start-Sleep -Seconds 10
    Invoke-Foundry 'get' $runPath $runFile
    $run = Get-Content $runFile -Raw | ConvertFrom-Json
}

$items = [Collections.Generic.List[object]]::new()
$after = ''
do {
    $pageFile = Join-Path $Out 'output-page.json'
    $suffix = if ($after) { '&after=' + [Uri]::EscapeDataString($after) } else { '' }
    Invoke-Foundry 'get' "$runPath/output_items?limit=100$suffix" $pageFile
    $page = Get-Content $pageFile -Raw | ConvertFrom-Json
    foreach ($item in $page.data) { $items.Add($item) }
    $next = if ($page.has_more -and $page.data.Count -gt 0) { $page.data[-1].id } else { '' }
    if ($page.has_more -and (-not $next -or $next -eq $after)) { throw '評価結果のページングが進みません。' }
    $after = $next
} while ($after)
@{ data = $items.ToArray() } | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $Out 'output-items.json') -Encoding utf8
Invoke-Runner -Arguments @('collect', '--out', $Out)
$artifactsDirectory = Join-Path $Out 'artifacts'
New-Item $artifactsDirectory -ItemType Directory -Force | Out-Null
$downloadsComplete = $true
foreach ($record in (Get-Content (Join-Path $Out 'artifacts.json') -Raw | ConvertFrom-Json)) {
    $artifact = $record.artifact
    if ($artifact.artifact_id -notmatch '^art_[0-9A-HJKMNP-TV-Z]{26}$' -or $artifact.kind -notin @('docx', 'pptx') `
        -or $artifact.session_id -notmatch '^[A-Za-z0-9_-]{1,200}$' `
        -or $artifact.location -notmatch ('^artifacts/' + $artifact.artifact_id + '/[^/\\]+$') `
        -or $artifact.size_bytes -le 0 -or $artifact.size_bytes -gt 20000000) {
        throw '成果物の保存先の形式が不正です。'
    }
    $filePath = Join-Path $artifactsDirectory ($artifact.artifact_id + '.' + $artifact.kind)
    if ((Test-Path $filePath) -and (Get-Item $filePath).Length -eq $artifact.size_bytes) { continue }
    $fileUrl = "agents/$AgentName/endpoint/sessions/$($artifact.session_id)/files/content?api-version=v1&path=$([Uri]::EscapeDataString($artifact.location))"
    try { Invoke-Foundry 'get' $fileUrl $filePath }
    catch {
        $downloadsComplete = $false
        Write-Warning "成果物を取得できませんでした: $($record.case_id)。検証結果は不合格になります。セッションを残すので -Resume で再取得してください。"
    }
    if (-not (Test-Path $filePath) -or (Get-Item $filePath).Length -ne $artifact.size_bytes) {
        $downloadsComplete = $false
        Write-Warning "成果物のサイズが一致しません: $($record.case_id)。セッションは削除しません。"
    }
}
dotnet run --no-build --project $runner -- check --out $Out --thresholds (Join-Path $PSScriptRoot 'thresholds.json')
$result = $LASTEXITCODE
if ($result -notin @(0, 1)) { exit $result }

if ($downloadsComplete -and $run.status -eq 'completed' -and $items.Count -eq $request.data_source.source.content.Count) {
    $cleanupDirectory = Join-Path $Out 'session-cleanup'
    New-Item $cleanupDirectory -ItemType Directory -Force | Out-Null
    foreach ($row in $request.data_source.source.content) {
        $response = Get-Content (Join-Path $Out "responses\$($row.item.id).json") -Raw | ConvertFrom-Json
        $session = $response.agent_session_id
        if ($response.agent_reference.name -ne $AgentName -or $response.agent_reference.version -ne $AgentVersion `
            -or $session -notmatch '^[A-Za-z0-9_-]{1,200}$') {
            throw '削除対象の評価セッションを確認できません。'
        }
        $marker = Join-Path $cleanupDirectory ($session + '.json')
        if (Test-Path $marker) { continue }
        try {
            Invoke-Foundry 'delete' "agents/$AgentName/endpoint/sessions/${session}?api-version=v1"
            @{ session_id = $session; deleted_at = [DateTimeOffset]::UtcNow.ToString('O') } |
                ConvertTo-Json | Set-Content $marker -Encoding utf8
        }
        catch {
            Write-Warning "評価セッションの削除に失敗しました: $($row.item.id)。同じ出力先を -Resume で再実行してください。"
            $result = 2
        }
    }
}
else {
    Write-Warning '評価結果または成果物が不足しているため、再取得用に評価セッションを残しました。'
}
exit $result
