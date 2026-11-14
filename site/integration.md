---
layout: default
title: 04 Web アプリへ組み込む
description: Microsoft Foundry Hosted Agent を自社アプリから安全に呼び出し、会話や成果物、トレースを統合する方法です。
main_class: lesson
permalink: /integration/
---

<div class="lesson-kicker"><span>04</span><span>APPLICATION</span><span>—</span><span>Hosted Agent の接続</span></div>

# BFF で認証し、会話とストリームを Web へつなぐ

<p class="lesson-lead">Hosted Agent は推論とツール実行のホスティングを担います。認証、利用者と会話の対応、入力文脈の検証、操作制限、成果物の所有確認は、組み込む側のアプリに残る責任です。</p>

<nav class="lesson-toc" aria-label="この章の内容">
  <a href="#identity">認証と接続設定</a><a href="#context">要求本文</a><a href="#session">会話管理</a><a href="#stream">SSE</a><a href="#try">動作確認</a>
</nav>

<figure class="diagram" markdown="1">
![Hosted Agent 内のResponsesホスト、MAFエージェント、MCPクライアント、loopback専用MCPサーバーの関係]({{ '/assets/06-agent-mcp-internals.png' | relative_url }})
<figcaption><span>図 06</span> この例では業務ツールを Agent コンテナー内の MCP 境界に置き、外部の URL 取得ツールを公開しません。</figcaption>
</figure>

<section class="lesson-section" markdown="1">
<div class="section-marker">01 / IDENTITY</div>
<div class="section-content" markdown="1">

## ブラウザーではなく、バックエンドから呼ぶ
{: #identity}

ブラウザーへ Foundry のアクセストークンを渡す設計にはしません。BFF がマネージド ID を使い、最小限の Foundry Agent Consumer ロールで Hosted Agent を呼びます。ローカル開発では開発者の Entra ID 資格情報を使います。

### 接続先を設定する

Hosted Agent を呼ぶ Web プロセスには次を設定します。これは配置済み Agent への接続例で、リソースを作成するコマンドではありません。ローカルの Agent を呼ぶ場合は Development 設定が `Mode=Local`、`BaseUrl=http://localhost:8088` に切り替えます。

```powershell
$env:Agent__Mode = 'Foundry'
$env:Agent__BaseUrl = 'https://<account>.services.ai.azure.com/api/projects/<project>/agents/updates-cowork/endpoint/protocols/openai'
$env:Agent__ApiVersion = 'v1'
$env:Agent__ConversationMode = 'Conversations'
$env:Agent__VersionOverride = '<本番Agentの版>'
$env:Artifacts__Mode = 'SessionFiles'
```

`BaseUrl` の末尾に `/responses` は付けません。Gateway が `/conversations` と `/responses` を使い分けます。Azure では `AZURE_CLIENT_ID` で Web 用のユーザー割り当てマネージド ID を指定し、ローカルでは `DefaultAzureCredential` を使います。

### HttpClient にトークンを付ける

[Web の Program.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/Program.cs) は型付きクライアントに次のハンドラーを登録しています。

```csharp
builder.Services.AddTransient<BearerTokenHandler>();
builder.Services.AddHttpClient<SessionFilesArtifactReader>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .AddHttpMessageHandler<BearerTokenHandler>();
builder.Services.AddTransient<ISessionArtifactManager>(sp => sp.GetRequiredService<SessionFilesArtifactReader>());
builder.Services.AddHttpClient<ResponsesAgentGateway>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .AddHttpMessageHandler<BearerTokenHandler>();
```

[BearerTokenHandler.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/AgentGateway/BearerTokenHandler.cs) の `SendAsync` 内の抜粋です。取得するスコープは `https://ai.azure.com/.default` で、有効期限前に更新します。ローカル Agent にはトークンを付けません。

```csharp
if (options.Value.Mode == AgentMode.Foundry)
{
    var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
```

<div class="boundary-card">
  <div><span class="mini-label">BROWSER</span><strong>自社アプリの API を呼ぶ</strong><p>入力と現在の画面文脈を送信。Foundry token は保持しない。</p></div>
  <span class="boundary-arrow" aria-hidden="true">→</span>
  <div><span class="mini-label">BFF</span><strong>認証・会話・制限を管理</strong><p>利用者を検証し、トークンを付けて Agent へ接続。</p></div>
  <span class="boundary-arrow" aria-hidden="true">→</span>
  <div><span class="mini-label">HOSTED AGENT</span><strong>指示に沿って処理する</strong><p>許可した業務ツールを通じてデータや成果物を扱う。</p></div>
</div>

<aside class="callout callout-caution">
  <span class="callout-label">ロールの注意</span>
  <p>このサンプルでは BFF に Foundry User を付けません。Foundry User は広いセッション操作権限を持つため、用途に合った最小権限を確認してください。</p>
</aside>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">02 / CONTEXT</div>
<div class="section-content" markdown="1">

## 画面の文脈を、検証可能な形で渡す
{: #context}

エージェントに「現在のページ」を推測させず、サーバーが許可した型付きの文脈として明示します。この例では更新 ID、タイトル、公式 URL を developer メッセージに含めます。記事本文は BFF からモデルへ転送せず、Agent の固定接続先ツールが取得します。

[AgentRequestBuilder.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/AgentGateway/AgentRequestBuilder.cs) の次の部分が、会話の継続方法を決めます。両方を送らず、会話 ID を優先します。

```csharp
if (!string.IsNullOrEmpty(request.ConversationId))
{
    writer.WriteString("conversation", request.ConversationId);
}
else if (!string.IsNullOrEmpty(request.PreviousResponseId))
{
    writer.WriteString("previous_response_id", request.PreviousResponseId);
}
```

通常ターンの要求形状は次のようになります。これは値を置き換えた JSON 例です。`PageContextValidator` を通った文脈だけを [PageContextText.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Shared/PageContextText.cs) で整形し、外部由来の値をエスケープして囲みます。

```json
{
  "conversation": "<作成した会話ID>",
  "stream": true,
  "input": [
    {
      "type": "message",
      "role": "developer",
      "content": [{
        "type": "input_text",
        "text": "<page_context>\n<untrusted_content>\nupstream_url: https://azure.microsoft.com/updates?id=559242\nupdate_id: 559242\ntitle: Azure Container Apps Express\nlocale: ja-JP\n</untrusted_content>\n</page_context>"
      }]
    },
    {
      "type": "message",
      "role": "user",
      "content": [{ "type": "input_text", "text": "この更新を要約してください" }]
    }
  ]
}
```

developer ロールに載せてもタイトルを信頼できる指示にはしません。記事本文はここにはなく、Agent が ID を使ってツールから取得します。未選択なら developer のページ情報自体を送らず、Agent に対象を確認させます。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">03 / SESSION</div>
<div class="section-content" markdown="1">

## 会話 ID を利用者の認可に使わない
{: #session}

会話 ID は継続性を表しますが、単独では利用者に対する認可の証明になりません。BFF はランダムな利用者キーとスレッドを対応付け、各要求で所有者を確認します。別利用者のスレッドや成果物には 404 を返します。

<div class="principle-grid">
  <article><span>01</span><h3>会話を作る</h3><p>最初のターンで Foundry Conversation を作成し、BFF 台帳へ保存。</p></article>
  <article><span>02</span><h3>要求を続ける</h3><p>以後の Responses 要求へ会話 ID を付け、ストリームを BFF が中継。</p></article>
  <article><span>03</span><h3>終了を試みる</h3><p>新規会話や終了要求で Agent セッションを削除し、台帳から関連情報を除去。</p></article>
</div>

<p class="inline-note">終了時の削除はブラウザー終了のようなケースでは best effort です。要求が届かなければセッションが最大 30 日残ることがあります。実運用では保持と削除の要件を明示してください。</p>

実装は [ChatEndpoints.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/Endpoints/ChatEndpoints.cs) → [ChatOrchestrator.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/Chat/ChatOrchestrator.cs) の順に追います。前者が利用者キーとスレッドの所有者を照合し、後者が初回の `CreateConversationAsync` と台帳更新を行います。ブラウザーから Foundry の会話 ID をそのまま受け取る API にはしていません。

Azure では `ConversationMode=Conversations` を使い、同じセッションのファイルも継続利用します。ローカル Development の `PreviousResponseId` 方式とは区別してください。台帳と Cookie の暗号化キーはメモリーだけに置くため、Web の再起動で失われます。複数レプリカへ拡張するなら、この状態管理を先に設計する必要があります。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">04 / STREAM</div>
<div class="section-content" markdown="1">

## 生の Agent イベントをアプリの UI 契約へ変換する
{: #stream}

Hosted Agent の Responses ストリームにはテキストだけでなく、ツール呼び出し、ツール結果、完了・失敗が含まれます。BFF は入力を検証し、これらを利用者向けの進行状況・成果物・回答イベントに変換します。

<div class="event-map">
  <div><code>function_call</code><span>→</span><b>tool.started</b><small>処理中の表示</small></div>
  <div><code>function_call_output</code><span>→</span><b>tool.completed / artifact.created</b><small>完了通知・成果物カード</small></div>
  <div><code>output_text.delta</code><span>→</span><b>text.delta</b><small>回答を逐次表示</small></div>
  <div><code>response.completed</code><span>→</span><b>done</b><small>応答 ID・使用量の記録</small></div>
</div>

ブラウザーが接続を切ったときはキャンセルを Agent 呼び出しへ伝播し、ターンの時間・同時実行数・利用者ごとの頻度にも上限を設けます。

### 全本文の受信を待たない

[ResponsesAgentGateway.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/AgentGateway/ResponsesAgentGateway.cs) の `SendAsync` 内では、`ResponseHeadersRead` を指定します。既定の全本文バッファリングでは SSE を逐次処理できません。

```csharp
response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
    .ConfigureAwait(false);
```

`StreamAsync` は受信ストリームを `AgentSseReader` へ渡します。その先の [ChatEventMapper.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/Chat/ChatEventMapper.cs) が画面向けイベントに変換し、Orchestrator が15秒ごとの ping と既定200秒のターン上限を担当します。接続が切れた場合と時間超過は異なる終了理由として扱います。

### 成果物は登録してから通知する

`ChatOrchestrator` の次の箇所では、検証済み成果物を利用者・スレッドと紐付けてから、BFF 内のダウンロード URL を通知します。モデルの文章に含まれるファイル URL は使いません。

```csharp
await registry.AddArtifactAsync(thread.UserKey, ToRecord(artifact, thread.ThreadId), ct).ConfigureAwait(false);
await WriteAsync(new ArtifactCreatedEvent(
    artifact.ArtifactId,
    artifact.FileName,
    artifact.Kind,
    artifact.SizeBytes,
    $"/api/artifacts/{artifact.ArtifactId}")).ConfigureAwait(false);
```

取得時は [ArtifactEndpoints.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/Endpoints/ArtifactEndpoints.cs) が再び所有者を照合し、`SessionFilesArtifactReader` が取得します。Agent の `DataContent` が自動でブラウザーのダウンロードになるわけではありません。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">05 / TOOLS</div>
<div class="section-content" markdown="1">

## ツールを、業務で許可した能力に限定する

<ul class="rule-list">
  <li><b>固定した接続先</b><span>モデルに任意 URL を取得させない。外部データのホストはアプリ側で固定する。</span></li>
  <li><b>引数の検証</b><span>モデルのツール引数も外部入力として検証する。空の成功応答や内部例外の漏えいで失敗を隠さない。</span></li>
  <li><b>実行の追跡</b><span>ツール開始・終了、結果、エラーをトレースし、本文や秘密情報は記録しない。</span></li>
  <li><b>ファイルの検査</b><span>許可した形式だけを生成・取得し、サイズ・妥当性・所有者を確認してからダウンロードさせる。</span></li>
</ul>

このサンプルの業務 5 ツールは非公開 MCP で提供されます。起動時にツール名を許可リストと照合し、MCP で発見した入力スキーマをモデルに渡します。サーバーは loopback の動的ポートで待ち受け、要求サイズと起動時の秘密値も検査します。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">06 / TRY</div>
<div class="section-content" markdown="1">

## Web から要約、会話継続、Word を確認する
{: #try}

第2章のローカル Agent を起動したまま、**新しい PowerShell** で次を実行します。前の接続例の `Agent__*` を引き継いでいる場合はローカルの設定に戻してください。

```powershell
$env:Agent__Mode = 'Local'
$env:Agent__BaseUrl = 'http://localhost:8088'
$env:Agent__ApiVersion = ''
$env:Agent__ConversationMode = 'PreviousResponseId'
$env:Artifacts__Mode = 'Local'
$env:Artifacts__LocalDir = Join-Path $PWD '.artifacts'
$env:AZURE_UPDATES_FIXTURES_DIR = Join-Path $PWD 'eval\fixtures\updates'
dotnet run --project src\UpdatesCowork.Web
```

開発用証明書を信頼した環境で `https://localhost:7101` を開きます。ブラウザーの開発者ツールで Network を確認しながら進めてください。

| 操作 | 確認する結果 |
|---|---|
| 一覧から更新を選び「要約」を押す | `POST /api/chat` の `pageContext` が選択した ID。応答が `text/event-stream` |
| 画面の進捗を見る | スキルと更新取得の動きが表示され、回答が逐次増える |
| 「今要約した更新の Word を作って」と続ける | 前ターンの対象を使い、成果物カードから実ファイルを取得できる |
| カードの URL を別のプライベートウィンドウで開く | 他利用者の成果物は取得できない（404） |
| 実行中に「停止」を押す | UI が停止状態になり、BFF のキャンセルが Agent 呼び出しへ伝わる |

フロントエンドの要求送信は [app.js](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/wwwroot/js/app.js) と [sse.js](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/wwwroot/js/sse.js) を参照します。POST の SSE なのでブラウザー標準の `EventSource` ではなく `fetch` のストリームを読みます。回答 Markdown と記事 HTML は DOMPurify を通してから表示します。

Azure 接続へ切り替えた後は、同じ操作で Conversation と Session Files の経路も確認します。ローカル確認だけで Azure の RBAC やファイル取得まで合格したことにはしません。

</div>
</section>

<section class="checkpoint" markdown="1">
<p class="eyebrow">CHECKPOINT</p>

## 自社アプリへ適用するときの確認

- 認証、利用者ごとの会話対応、ツール、成果物の所有権をどのサービスが担うか決めた。
- モデルへ渡す文脈と、信頼しない外部データの境界を定義した。
- キャンセル、レート制限、エラー表示、セッション削除、トレースの情報範囲を決めた。

<div class="source-links">
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/AgentGateway/ResponsesAgentGateway.cs">ResponsesAgentGateway.cs <span>↗</span></a>
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Agent/Program.cs">Agent の起動と Hosted Responses <span>↗</span></a>
  <a href="https://learn.microsoft.com/en-us/azure/foundry/how-to/develop/framework-hosted-agents">Microsoft Learn: Framework Hosted Agent <span>↗</span></a>
  <a href="https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/manage-hosted-sessions">Microsoft Learn: セッションとファイル <span>↗</span></a>
</div>
</section>

<nav class="lesson-next" aria-label="章の移動">
  <a href="{{ '/hosting/' | relative_url }}"><small>前の章 / 03</small><span>動かす: Hosted Agent でホストする</span></a>
  <a class="next-link" href="{{ '/observability/' | relative_url }}"><small>次の章 / 05</small><span>見る: トレースと監視 <b aria-hidden="true">→</b></span></a>
</nav>
