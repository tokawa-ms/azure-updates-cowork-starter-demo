$ErrorActionPreference = 'Stop'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot '..\run-foundry.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors.Message -join "`n") }
$function = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-Foundry'
}, $true)
if (-not $function) { throw 'Invoke-Foundry が見つかりません。' }
# スクリプト全体を起動せず、通信境界だけを実際の関数定義で検査する。
. ([scriptblock]::Create($function.Extent.Text))

$ProjectEndpoint = 'https://offline.invalid/api/projects/test'
$AgentName = 'test-eval'
$azCommand = 'Invoke-FakeAzureCli'
$azPrefix = @()
function Invoke-FakeAzureCli {
    $script:calls++
    if ($script:calls -le $script:failures) {
        $global:LASTEXITCODE = 1
        Write-Output ('ERROR: {"error":{"code":"' + $script:errorCode + '"}}')
    }
    else { $global:LASTEXITCODE = 0 }
}
function Start-Sleep([int]$Seconds) { $script:delays += $Seconds }

$responsePath = "agents/$AgentName/endpoint/protocols/openai/responses?api-version=v1"
$cases = @(
    @{ Name = 'transient session creation'; Method = 'post'; Path = $responsePath; Retry = $true; Failures = 2; ErrorCode = 'session_creation_failed'; Calls = 3; Failed = $false; Delays = @(10, 20) },
    @{ Name = 'retry limit'; Method = 'post'; Path = $responsePath; Retry = $true; Failures = 10; ErrorCode = 'session_creation_failed'; Calls = 3; Failed = $true; Delays = @(10, 20) },
    @{ Name = 'other response failure'; Method = 'post'; Path = $responsePath; Retry = $true; Failures = 1; ErrorCode = 'internal_error'; Calls = 1; Failed = $true; Delays = @() },
    @{ Name = 'evaluation registration'; Method = 'post'; Path = 'openai/v1/evals'; Retry = $true; Failures = 1; ErrorCode = 'session_creation_failed'; Calls = 1; Failed = $true; Delays = @() },
    @{ Name = 'retry not requested'; Method = 'post'; Path = $responsePath; Retry = $false; Failures = 1; ErrorCode = 'session_creation_failed'; Calls = 1; Failed = $true; Delays = @() }
)
foreach ($case in $cases) {
    $script:calls = 0
    $script:delays = @()
    $script:failures = $case.Failures
    $script:errorCode = $case.ErrorCode
    $failure = $null
    try {
        Invoke-Foundry -Method $case.Method -RelativePath $case.Path -RetrySessionCreation:$case.Retry > $null 3> $null
    }
    catch { $failure = $_.Exception.Message }
    $expectedFailure = if ($case.Failed) { "Foundry の要求が失敗しました: $($case.Method) $($case.Path)" } else { $null }
    if ($failure -ne $expectedFailure -or $script:calls -ne $case.Calls `
        -or ($script:delays -join ',') -ne ($case.Delays -join ',')) {
        throw "失敗: $($case.Name)。calls=$script:calls、failure=$failure"
    }
}
Write-Output "$($cases.Count) 件の通信回復テストに成功しました。ネットワーク要求はありません。"
exit 0
