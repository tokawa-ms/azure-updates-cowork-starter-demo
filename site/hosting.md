---
layout: default
title: 03 Hosted Agent でホスト
description: Agent Framework のエージェントを Responses で公開し、コンテナーと azure.yaml で Foundry Hosted Agent に配置する実装を読みます。
main_class: lesson
permalink: /hosting/
---

<div class="lesson-kicker"><span>03</span><span>HOSTING</span><span>—</span><span>ローカルから Foundry へ</span></div>

# 同じエージェントを Responses でホストする

<p class="lesson-lead">Agent Framework はエージェントの処理を組み立てます。Foundry のホスティングパッケージはその処理を HTTP プロトコルに接続し、Hosted Agent 基盤がコンテナー、セッション、ID を管理します。この3つを分けて設定を読みます。</p>

<section class="lesson-section" markdown="1">
<div class="section-marker">01 / HOST</div>
<div class="section-content" markdown="1">

## DI 登録とエンドポイントの公開

[Program.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Agent/Program.cs) は環境に応じて資格情報を選び、エージェントを通常の DI と名前付き DI に登録します。

```csharp
var options = AgentRuntimeOptions.FromConfiguration(builder.Configuration);
TokenCredential credential = string.IsNullOrEmpty(builder.Configuration["FOUNDRY_AGENT_NAME"])
    ? new DefaultAzureCredential()
    : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
ToolServices.AddTo(builder.Services, options, credential, builder.Configuration);
builder.Services.AddSingleton(sp => AgentFactory.Create(options, credential, sp));
builder.Services.AddKeyedSingleton<AIAgent>(builder.Configuration["FOUNDRY_AGENT_NAME"] ?? AgentNames.AgentName,
    (sp, _) => sp.GetRequiredService<AIAgent>());
builder.AddFoundryResponses();
```

`AddSingleton` のファクトリーはこの行で直ちに実行されません。そのため、続く起動処理で MCP を接続してからエージェントを解決できます。同じファイル内の該当箇所を抜粋すると、次の順です。途中の OpenTelemetry 登録はここでは省略しています。

```csharp
await using var app = builder.Build();
var mcpServer = app.Services.GetRequiredService<CoworkMcpServer>();
await using var mcpHost = mcpServer.CreateHost(app.Services);
await mcpHost.StartAsync().ConfigureAwait(false);
using var mcpHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
mcpHttp.DefaultRequestHeaders.Add(CoworkMcpServer.AccessHeader, mcpServer.AccessToken);
using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await app.Services.GetRequiredService<CoworkMcpClient>().ConnectAsync(
    new Uri(mcpHost.Urls.Single() + "/mcp"), mcpHttp, app.Services.GetRequiredService<ILoggerFactory>(),
    startupTimeout.Token).ConfigureAwait(false);
app.MapFoundryResponses();
```

この後で `app.RunAsync()` を待ち、終了時には `finally` で MCP ホストも停止します。公開するのは Responses であって MCP ではありません。MCP は loopback の動的ポートに限定しています。

### プレビューの API は固定版に合わせる

このリポジトリの [Directory.Packages.props](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/Directory.Packages.props) は `Microsoft.Agents.AI` を `1.28.0`、`Microsoft.Agents.AI.Foundry.Hosting` を `1.28.0-preview.261106.1`、`Azure.AI.Projects` を `3.0.0-beta.5` に固定しています。

2026-11-13 に参照した [公式の Framework Hosted Agent 解説](https://learn.microsoft.com/en-us/azure/foundry/how-to/develop/framework-hosted-agents) には `builder.Services.AddFoundryResponses(agent)` の例もありますが、このサンプルは上の名前付き DI と `builder.AddFoundryResponses()` を使います。別の版のサンプルを混ぜず、固定版の XML ドキュメントと実コードを確認してください。必要な依存関係は [Agent.csproj](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Agent/UpdatesCowork.Agent.csproj) にまとまっています。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">02 / CONTAINER</div>
<div class="section-content" markdown="1">

## 8088 番とセッション用ファイル領域を用意する

[Agent の Dockerfile](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Agent/Dockerfile) は .NET 10 で publish した成果物を実行用イメージへコピーします。リポジトリ全体がビルドコンテキストなので、Shared や Office などの参照プロジェクトもビルドできます。以下は実行用ステージの抜粋です。

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
COPY eval/fixtures/updates /app/eval-fixtures
ENV ASPNETCORE_HTTP_PORTS=8088
EXPOSE 8088
```

`EXPOSE` だけでは待ち受けアドレスは設定されません。`ASPNETCORE_HTTP_PORTS` がコンテナー内の HTTP 待ち受けを設定し、ホストパッケージが `/responses` と `/readiness` を提供します。ローカルの既定待ち受けは `http://localhost:8088` です。

成果物は Hosted Session の `$HOME/artifacts` へ保存します。この Agent イメージでは、基盤が用意する `$HOME` に書き込める既定ユーザーを使っています。Web の非 root 設定をそのままコピーすると、この環境では書き込み権限エラーになりました。これは当サンプルで確認した制約であり、すべてのコンテナーを root で動かす推奨ではありません。

| 設定 | ローカル | Hosted Agent |
|---|---|---|
| 資格情報 | `DefaultAzureCredential` | Agent のマネージド ID |
| プロジェクト | 開発者が環境変数で指定 | 基盤が `FOUNDRY_PROJECT_ENDPOINT` を注入 |
| モデル | 既存デプロイ名を指定 | `azure.yaml` の `env` で指定 |
| 保存方式 | `ARTIFACTS_MODE=Local` | `ARTIFACTS_MODE=SessionFiles` |
| 保存先 | `ARTIFACTS_LOCAL_DIR` | 基盤の `$HOME` |
| 更新データ | fixture または実 API | 本番は実 API、評価用だけ fixture |

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">03 / MANIFEST</div>
<div class="section-content" markdown="1">

## azure.yaml のサービス定義を読む

以下は [azure.yaml](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/azure.yaml) の本番 `agent` サービス冒頭の抜粋です。`host` が配置先、`kind` が Agent の種類、`protocols` がホストと外側の接続契約を表します。

```yaml
agent:
  host: azure.ai.agent
  kind: hosted
  name: updates-cowork
  project: src/UpdatesCowork.Agent
  language: docker
  uses:
    - ai-project
  docker:
    path: Dockerfile
    context: ../..
    remoteBuild: true
  protocols:
    - protocol: responses
      version: 2.0.0
  container:
    resources:
      cpu: "1"
      memory: 2Gi
```

このブロックだけでは配置できません。実ファイルには `ai-project`、RAI ポリシー、環境変数、Web、postdeploy フックもあります。このサンプルは ACR リモートビルドでコンテナーを配置する方式です。公式資料のソース ZIP 配置とは手順が異なります。

評価用の `agent-eval` は同じプロジェクトを別名 `updates-cowork-eval` で配置し、`AZURE_UPDATES_FIXTURES_DIR=/app/eval-fixtures` を設定します。本番へ評価用 fixture や本文記録設定を持ち込まないことが重要です。

### 配置を試す前に確認すること

1. 既存 Foundry・Application Insights の ARM ID とモデルデプロイ名を設定する。未指定だと新設の分岐になります。
2. 配置担当者の権限と、Web・Agent それぞれの実行 ID に必要な RBAC を分けて確認する。
3. 基盤の preview を読み、共有リソースの意図しない作成・変更がないことを確認する。
4. RAI ポリシーの完全な ARM ID が版の設定に反映されたか確認する。

<aside class="callout callout-caution">
  <span class="callout-label">既知の配置上の制約</span>
  <p>検証した azd 拡張では <code>raiPolicyName</code> の環境変数参照が展開されず、一時的な直接指定が必要でした。また postdeploy は本番・評価用の両 Agent が存在する前提です。<code>azd up</code> だけで初回配置が完成すると案内できる状態ではありません。実行前に下の記録にある条件と復元手順を確認してください。環境固有 ID はコミットしません。</p>
</aside>

[既存リソースの設定、preview、配置と RAI の復元手順](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/docs/notes/deploy.md#既存foundryの再利用)を順に確認します。この教材の閲覧・ローカル演習だけなら Azure 基盤を再配置する必要はありません。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">04 / VERIFY</div>
<div class="section-content" markdown="1">

## 「配置成功」を実行可能な状態と取り違えない

| 確認点 | 合格と判断する証拠 | 失敗時に見る場所 |
|---|---|---|
| ホスト起動 | ローカル `/readiness` が 200、Azure の版が active | Agent 起動ログ、MCP 接続・ツール発見 |
| モデル接続 | Responses の呼び出しが完了 | モデルデプロイ名、Agent ID の RBAC |
| 会話継続 | 同じ Conversation で前ターンを参照できる | BFF が送った `conversation` |
| 成果物 | Word を生成し、本人の画面から取得できる | `$HOME` の権限、Session Files、BFF 台帳 |
| 配置した版 | 応答の `agent_reference` が意図した名前と版 | 版の設定、呼び出しヘッダー |

本番と評価用の版番号は独立です。評価用 v12 と同じソースを本番へ配置しても、本番が v12 になるとは限りません。ソース・イメージと版の対応を記録します。

`424` が続く場合、無条件再試行で隠さず、ホスト登録 API、起動ログ、モデル権限を確認します。BFF の再試行は `424` に対し1回だけです。具体的な呼び出しと失敗処理は次章で扱います。

</div>
</section>

<section class="checkpoint" markdown="1">
<p class="eyebrow">CHECKPOINT</p>

## 次章へ進む前に

- `AIAgent`、Responses ホスト、Hosted Agent 基盤の担当を区別できる。
- Dockerfile の待ち受け設定と、ローカル／クラウドの保存先を説明できる。
- 本番と評価用を別 Agent にし、配置したソースと版の対応を確認できる。

</section>

<nav class="lesson-next" aria-label="章の移動">
  <a href="{{ '/implementation/' | relative_url }}"><small>前の章 / 02</small><span>Agent Framework で実装する</span></a>
  <a class="next-link" href="{{ '/integration/' | relative_url }}"><small>次の章 / 04</small><span>Web アプリへ組み込む <b aria-hidden="true">→</b></span></a>
</nav>
