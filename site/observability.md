---
layout: default
title: "05 見る: トレースと監視"
description: Web の要求から Word の保存までを 1 本のトレースで追い、失敗と回復、時間、トークン、推定コストを Aspire Dashboard と Foundry ポータルで確かめる手順です。
main_class: lesson
permalink: /observability/
---

<div class="lesson-kicker"><span>05</span><span>OBSERVE</span><span>—</span><span>見る: トレースと監視</span></div>

# 1 回の依頼を最後まで追い、<br>時間と費用を把握する

<p class="lesson-lead">応答の文章が正しく見えても、途中でツールが失敗していたり、依頼によって費用が大きく違ったりします。この章では、Web の要求から Word の保存までを 1 本のトレースで追い、失敗と回復、時間、トークン、推定コストを確かめます。ローカルでは Aspire Dashboard、Azure では Foundry ポータルと Application Insights を使います。</p>

<nav class="lesson-toc" aria-label="この章の内容">
  <a href="#spans">スパンの親子関係</a><a href="#code">スパンを出すコード</a><a href="#failure">失敗と回復</a><a href="#cost">時間と費用</a><a href="#local">ローカルで確かめる</a><a href="#azure">Azure で確かめる</a><a href="#content">本文の記録</a>
</nav>

<figure class="diagram">
  <a href="{{ '/assets/09-observability.png' | relative_url }}" title="原寸で開く"><img src="{{ '/assets/09-observability.png' | relative_url }}" alt="Word を作るときの主なスパンの親子関係と、Aspire Dashboard、Application Insights、Foundry ポータル、KQL で確認する場所を示す図"></a>
  <figcaption><span>図 09</span> トレースの親子関係と、見る場所。ローカルは Aspire Dashboard、Azure では Application Insights と Foundry ポータルを使います。</figcaption>
</figure>

<section class="lesson-section" markdown="1">
<div class="section-marker">01 / SPANS</div>
<div class="section-content" markdown="1">

## Web の要求から Word の保存までが、1 本のトレースでつながる
{: #spans}

Word の作成を依頼すると、Web の `chat.turn` から Office の生成とファイルの保存までが、同じトレースに入ります。Application Insights では同じ `operation_Id` を持ちます。次の画面は、Foundry ポータルで開いた実際の依頼です。この依頼は 69 スパン、モデル呼び出し 11 回、ツール呼び出し 15 回で、処理に 12.9 秒かかりました。

<figure class="diagram diagram-small">
  <a href="{{ '/assets/screens/foundry-trace-word.png' | relative_url }}" title="原寸で開く"><img src="{{ '/assets/screens/foundry-trace-word.png' | relative_url }}" alt="Foundry ポータルのトレース。POST /api/chat、chat.turn、Responses への HTTP 呼び出し、invoke_agent、execute_tool create_word_report、POST /mcp、tools/call、mcp.tool.execute、office.word.build、artifact.save が親子関係で並ぶ"></a>
  <figcaption><span>画面 05</span> Word を作った依頼のトレース。途中のモデル呼び出しとツール実行は省略し、プロジェクト名は隠しています。</figcaption>
</figure>

スパンはそれぞれ別の部品が出します。どの部品が何を記録するかを知っておくと、失敗した位置から調べる場所を決められます。

| スパン | 出す部品 | 分かること |
|---|---|---|
| `chat.turn` | Web（自作の ActivitySource） | 依頼の種類（`auc.action`）、最初の文字が出るまでの時間（`auc.ttft_ms`） |
| `POST …/responses` | HttpClient の計装 | BFF から Hosted Agent への呼び出しと結果 |
| `invoke_agent`、`chat`、`execute_tool` | Agent Framework | エージェントの実行、モデル呼び出し、ツール呼び出し |
| `POST /mcp/`、`tools/call` | ASP.NET Core と MCP SDK | 非公開 MCP サーバーへの呼び出し |
| `mcp.tool.execute`、`office.word.build`、`artifact.save` | Agent（自作の ActivitySource） | ツールの結果、ファイルの生成と保存 |

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">02 / CODE</div>
<div class="section-content" markdown="1">

## 送り先と購読するスパンを、起動コードで決める
{: #code}

[Agent の Program.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Agent/Program.cs) は、テレメトリの送り先を設定から選び、購読する ActivitySource を明示しています。

```csharp
builder.UseMicrosoftOpenTelemetry(o => o.Exporters = TelemetrySetup.ResolveExporters(builder.Configuration));
builder.Services.AddOpenTelemetry().ConfigureResource(resource =>
    resource.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? TelemetryNames.Sources.Agent));
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
    tracing.AddSource("Azure.AI.AgentServer.Responses", TelemetryNames.Sources.Agent, TelemetryNames.Sources.McpSdk));
```

[TelemetrySetup.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Shared/Telemetry/TelemetrySetup.cs) の `ResolveExporters` は、`APPLICATIONINSIGHTS_CONNECTION_STRING` があれば Azure Monitor へ、`OTEL_EXPORTER_OTLP_ENDPOINT` があれば OTLP へ送ります。Hosted Agent では基盤が接続文字列を渡すため、コードを変えずに Application Insights へ届きます。ローカルでは OTLP の送り先を指定して、Aspire Dashboard へ送ります。

| 確認すること | このリポジトリの設定 |
|---|---|
| Azure での送り先 | 基盤が渡す `APPLICATIONINSIGHTS_CONNECTION_STRING` |
| ローカルでの送り先 | `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` |
| サービス名（`cloud_RoleName`） | Agent は `UpdatesCowork.Agent`。Foundry のエージェント名ではありません |
| MCP SDK のスパン | `Experimental.ModelContextProtocol` を購読します。`ModelContextProtocol` だけでは出ません |
| 入出力の本文 | 第 2 章の `UseOpenTelemetry` の `EnableSensitiveData` を、環境変数 `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` で切り替えます |

### ツールの失敗を、スパンの状態に反映する

業務ツールは、検証の失敗を例外ではなく `ok: false` の結果で返します。[CoworkMcpServer.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Agent/Mcp/CoworkMcpServer.cs) のフィルターは、その結果を MCP のエラー結果に変え、自作の `mcp.tool.execute` スパンの状態をエラーにします。次はその部分の抜粋です。

```csharp
var result = await next(request, cancellationToken).ConfigureAwait(false);
var json = CoworkMcpClient.ReadResult(result);
if (json.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
{
    result.IsError = true;
    activity?.SetStatus(ActivityStatusCode.Error);
}

activity?.SetTag(TelemetryNames.Attributes.Outcome,
    result.IsError == true ? TelemetryNames.Outcomes.Error : TelemetryNames.Outcomes.Ok);
return result;
```

成功を装った応答で失敗を隠すと、トレースでもモニターでも見つけられません。失敗は失敗としてスパンに残し、その後の回復も同じトレースで追えるようにします。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">03 / FAILURE</div>
<div class="section-content" markdown="1">

## 失敗と回復も、同じトレースに残る
{: #failure}

第 2 章で見た PowerPoint の例を、トレースで確かめます。1 回目の `create_powerpoint_deck` は検証で拒否されてエラーになり、2 回目は成功して `office.pptx.build` と `artifact.save` までつながっています。

<figure class="diagram diagram-small diagram-narrow">
  <a href="{{ '/assets/screens/foundry-trace-pptx.png' | relative_url }}" title="原寸で開く"><img src="{{ '/assets/screens/foundry-trace-pptx.png' | relative_url }}" alt="Foundry ポータルのトレース。上の execute_tool create_powerpoint_deck と tools/call create_powerpoint_deck に失敗の印が付き、下の 2 回目の呼び出しは mcp.tool.execute、office.pptx.build、artifact.save まで成功している"></a>
  <figcaption><span>画面 06</span> 赤い印が失敗したスパン、下が 2 回目の成功です。モデル名とプロジェクト名は隠しています。</figcaption>
</figure>

失敗したスパンを開くと、次の属性が記録されていました。

| 属性 | 記録された値 |
|---|---|
| `gen_ai.operation.name` | `execute_tool` |
| `gen_ai.tool.name` | `create_powerpoint_deck` |
| `mcp.method.name` | `tools/call` |
| `error.type` | `tool_error` |
| 状態（`status_code`） | `Error` |

本番の自作スパンには入出力の本文を記録しないため、属性から分かるのはツール名やエラーの種類です。拒否の理由の文面は、画面右上の「学習モード」で確かめられます。学習モードをオンにすると、直近の応答のツールの引数と結果、トレース ID、トークン数が表示されます。「KQL をコピー」を押すと、そのトレースを Application Insights で調べる KQL を取得できます。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">04 / COST</div>
<div class="section-content" markdown="1">

## 依頼ごとの時間・トークン・推定コストを比べる
{: #cost}

Foundry ポータルのトレースの一覧には、依頼ごとの処理時間、トークン数、推定コストが並びます。2026 年 11 月 13 日と 14 日の 11 件では、入力トークンが 5,640 から 112,412、処理時間が 5.7 秒から 24.9 秒まで、依頼によって違いました。

<figure class="diagram diagram-small">
  <a href="{{ '/assets/screens/foundry-traces.png' | relative_url }}" title="原寸で開く"><img src="{{ '/assets/screens/foundry-traces.png' | relative_url }}" alt="Foundry ポータルのトレース一覧。状態、作成日時、期間（秒）、受信と送信のトークン数、推定コストの列が並ぶ"></a>
  <figcaption><span>画面 07</span> トレースの一覧から抜粋。トークン（受信）が入力、トークン（送信）が出力のトークン数です。</figcaption>
</figure>

<div class="metric-grid">
  <article><span class="metric-number">約 20 倍</span><h3>入力トークンの差</h3><p>1 回の依頼の中のモデル呼び出しの回数と、毎回渡す文脈の長さで変わります。入力が大きい依頼は、指示文やスキルを見直す候補です。</p></article>
  <article><span class="metric-number">約 4 倍</span><h3>処理時間の差</h3><p>主に、モデル呼び出しとツール呼び出しの回数で変わります。トレースで時間のかかったスパンを特定します。</p></article>
  <article><span class="metric-number">&lt;$0.01</span><h3>1 回の推定コスト</h3><p>ポータルの金額は推定値です。請求額の確認には Azure の Cost Management を使います。</p></article>
</div>

### モニターで、エージェント単位の推移を見る

エージェントのモニター（プレビュー）では、期間ごとのトークン使用量、遅延、実行の成功率、評価の結果を確かめられます。データは接続した Application Insights に保存され、保持期間と課金は Application Insights の設定に従います。

<figure class="diagram diagram-small">
  <a href="{{ '/assets/screens/foundry-monitor.png' | relative_url }}" title="原寸で開く"><img src="{{ '/assets/screens/foundry-monitor.png' | relative_url }}" alt="Foundry ポータルのモニターの概要。運用メトリックの見積原価とトークン使用量、評価とスケジュールされた評価の構成ボタン、エージェントの実行数のグラフ、Gen AI errors の一覧がある"></a>
  <figcaption><span>画面 08</span> モニターの概要。「評価」と「スケジュールされた評価」には構成の入口があります。このリポジトリでは構成していません。</figcaption>
</figure>

<figure class="diagram diagram-small">
  <a href="{{ '/assets/screens/foundry-monitor-cost.png' | relative_url }}" title="原寸で開く"><img src="{{ '/assets/screens/foundry-monitor-cost.png' | relative_url }}" alt="モニターのコストのタブ。エージェントのコスト、モデルのトークン使用量、エージェントの実行数、1 回あたりの平均コストが並ぶ"></a>
  <figcaption><span>画面 09</span> コストのタブ（プレビュー）。金額はすべて推定値です。</figcaption>
</figure>

個々の依頼の調査はトレース、費用と傾向の把握はモニターと、目的で使い分けます。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">05 / LOCAL</div>
<div class="section-content" markdown="1">

## ローカルの Aspire Dashboard で、親子関係を確かめる
{: #local}

第 2 章のエージェントと第 4 章の Web を、テレメトリを送る設定で起動します。リポジトリのルートで Aspire Dashboard を起動してから、2 つの PowerShell の両方で、`dotnet run` の前に送り先を設定します。

```powershell
docker compose -p auc-otel -f tools\otel\compose.yaml up -d
$env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://localhost:4317'
```

<ol class="numbered-steps">
  <li><b>Word を依頼する</b><span><code>https://localhost:7101</code> を開き、更新を選んで「Wordにする」を押します。</span></li>
  <li><b>トレースを開く</b><span><code>http://localhost:18888</code> の Traces で、<code>POST /api/chat</code> から始まるトレースを開きます。</span></li>
  <li><b>親子関係をたどる</b><span><code>execute_tool create_word_report</code> を展開し、Office の生成と保存までたどります。</span></li>
</ol>

次は、主なスパンだけを抜き出した親子関係です。ツールの順序やモデル呼び出しの回数は、実行ごとに変わります。

```text
POST /api/chat
  chat.turn
    POST /responses
      invoke_agent updates-cowork
        chat <モデルのデプロイ名>
        execute_tool load_skill
        execute_tool get_azure_update
        execute_tool create_word_report
          POST /mcp/
            tools/call create_word_report
              mcp.tool.execute
                office.word.build
                artifact.save
```

Aspire Dashboard の記憶領域は永続化していないため、再起動するとトレースは消えます。共有の Dashboard にはほかのアプリのデータも届くため、自分のトレース ID で絞って確かめます。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">06 / AZURE</div>
<div class="section-content" markdown="1">

## Azure では、Foundry ポータルと KQL で確かめる
{: #azure}

Azure に配置した環境では、同じ情報を Foundry ポータルと Application Insights で確かめます。接続先の Application Insights に対する Log Analytics Reader の権限が必要です。

<ol class="numbered-steps">
  <li><b>トレースを開く</b><span>Foundry ポータルでエージェント <code>updates-cowork</code> を開き、Traces で期間を実行した時刻に合わせます。</span></li>
  <li><b>親子関係をたどる</b><span>依頼を開き、<code>invoke_agent</code> から業務ツール、<code>POST /mcp/</code>、Office の生成まで展開します。</span></li>
  <li><b>傾向を見る</b><span>Monitor で、トークン使用量、遅延、実行の成功率を確かめます。</span></li>
  <li><b>KQL で絞り込む</b><span>Application Insights の Logs で、トレース ID やツール名を指定して調べます。</span></li>
</ol>

1 本のトレースの構造だけを見る例です。`<trace ID>` は学習モードやポータルで確かめた値に置き換えます。

```text
union requests, dependencies
| where timestamp > ago(1d)
| where operation_Id == "<trace ID>"
| project timestamp, cloud_RoleName, name, id, operation_ParentId, duration, success
| order by timestamp asc
```

MCP のツール呼び出しの回数、失敗数、所要時間を見る例です。

```text
union requests, dependencies
| where timestamp > ago(2h)
| where cloud_RoleName == "UpdatesCowork.Agent"
| where name startswith "tools/call" or name == "mcp.tool.execute"
| summarize calls=count(), failures=countif(success == false), p95_ms=percentile(duration, 95) by name
```

`cloud_RoleName` で絞るときは `UpdatesCowork.Agent` を指定します。`updates-cowork` では該当しません。本番と評価用は、同じトレースに含まれる `invoke_agent updates-cowork` と `invoke_agent updates-cowork-eval` で区別します。

利用者がチャットの応答に付けた評価も、その応答のトレースに入ります。Web は応答を返した `chat.turn` のスパンを親にして `feedback.record` を開き、ログ `auc.feedback` と、Foundry のトレースの注釈と同じ形のログ `gen_ai.evaluation.result` を出します。低い評価の割合を集計する KQL とアラートは、第 8 章で扱います。

<a class="text-link" href="{{ '/automation/' | relative_url }}#establish">第 8 章: 利用者の評価をトレースに付ける <span aria-hidden="true">→</span></a>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">07 / CONTENT</div>
<div class="section-content" markdown="1">

## 本文の記録範囲を決めてから、トレースを使う
{: #content}

入出力の本文がトレースに残ると、調査や採点に使える一方で、利用者の入力も保存されます。このリポジトリでは、本番の自作スパンには本文を記録せず、評価用だけ記録しています。次は、2 時間の観測で本文の有無を数えた結果です。

| 発生元 | 設定 | 本文があったスパン |
|---|---|---|
| 本番の自作 Agent | `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=false` | 調べた 144 スパン中 0 件 |
| 本番の Foundry `responsesapi` | アプリの設定とは別 | 調べた 26 スパン中 20 件 |
| 評価用の自作 Agent | `true`（固定データの評価だけ） | 調べた 1,426 スパン中 168 件 |

<aside class="callout callout-caution">
  <span class="callout-label">記録範囲の注意</span>
  <p>アプリ側の <code>false</code> は、Foundry 基盤側で本文を記録しない保証にはなりません。機密情報を入力しないでください。本番の応答を採点するには本文が必要になるため、記録の範囲、閲覧権限、保持期間を先に決めます（第 8 章）。本文を含むトレースの JSON やスクリーンショットは、公開リポジトリに追加しないでください。</p>
</aside>

</div>
</section>

<section class="checkpoint" markdown="1">
<p class="eyebrow">CHECKPOINT</p>

## 次章へ進む前に

- `chat.turn` から `artifact.save` まで、どの部品がどのスパンを出すか説明できる。
- 失敗したツール呼び出しを、スパンの状態と属性から見つけられる。
- 依頼ごとの時間、トークン、推定コストを、トレースの一覧とモニターで比べられる。
- 本番で本文を記録するかどうかが、調査と採点にどう影響するか説明できる。

<div class="source-links">
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/docs/notes/observability.md">MCP とトレースを見る（観測の記録） <span>↗</span></a>
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/docs/design.md">設計書 §8 観測性 <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/observability/concepts/trace-agent-concept">Microsoft Learn: Agent tracing overview <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/observability/how-to/how-to-monitor-agents-dashboard">Microsoft Learn: Agent Monitoring Dashboard <span>↗</span></a>
</div>
</section>

<nav class="lesson-next" aria-label="章の移動">
  <a href="{{ '/integration/' | relative_url }}"><small>前の章 / 04</small><span>Web アプリへ組み込む</span></a>
  <a class="next-link" href="{{ '/evaluation/' | relative_url }}"><small>次の章 / 06</small><span>測る: 品質評価を自動化する <b aria-hidden="true">→</b></span></a>
</nav>
