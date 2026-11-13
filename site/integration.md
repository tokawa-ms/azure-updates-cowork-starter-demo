---
layout: default
title: 03 アプリへ組み込む
description: Microsoft Foundry Hosted Agent を自社アプリから安全に呼び出し、会話や成果物、トレースを統合する方法です。
main_class: lesson
permalink: /integration/
---

<div class="lesson-kicker"><span>03</span><span>APPLICATION</span><span>—</span><span>Hosted Agent の接続</span></div>

# Agent を呼ぶだけでなく、<br>業務アプリの責任を保つ。

<p class="lesson-lead">Hosted Agent は推論とツール実行のホスティングを担います。認証、利用者と会話の対応、入力文脈の検証、操作制限、成果物の所有確認は、組み込む側のアプリに残る責任です。</p>

<figure class="diagram" markdown="1">
![Hosted Agent 内のResponsesホスト、MAFエージェント、MCPクライアント、loopback専用MCPサーバーの関係]({{ '/assets/06-agent-mcp-internals.png' | relative_url }})
<figcaption><span>図 06</span> この例では業務ツールを Agent コンテナー内の MCP 境界に置き、外部の URL 取得ツールを公開しません。</figcaption>
</figure>

<section class="lesson-section" markdown="1">
<div class="section-marker">01 / IDENTITY</div>
<div class="section-content" markdown="1">

## ブラウザーではなく、バックエンドから呼ぶ

ブラウザーへ Foundry のアクセストークンを渡す設計にはしません。BFF がマネージド ID を使い、最小限の Foundry Agent Consumer ロールで Hosted Agent を呼びます。ローカル開発では開発者の Entra ID 資格情報を使います。

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

エージェントに「現在のページ」を推測させず、サーバーが許可した型付きの文脈として明示します。この例では更新 ID、タイトル、公式 URL を developer メッセージに含めます。記事本文は BFF からモデルへ転送せず、Agent の固定接続先ツールが取得します。

<div class="code-frame">
  <div class="code-head"><span>文脈のイメージ（概念例）</span><span class="code-tag">NOT A COPY-PASTE API</span></div>
  <pre><code>&lt;page_context&gt;
  update_id: 検証済みの更新 ID
  title: 画面に表示しているタイトル
  url: 許可した公式サイトの URL
&lt;/page_context&gt;
&lt;untrusted_content&gt;
  外部から取得した記事本文はここで扱う
&lt;/untrusted_content&gt;</code></pre>
</div>

<p class="inline-note">形式はこのサンプルの境界を説明する概念例です。実際の要求本文、検証規則、メッセージ構造は <code>AgentRequestBuilder</code> とデータ契約を確認してください。</p>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">03 / SESSION</div>
<div class="section-content" markdown="1">

## 会話 ID を利用者の認可に使わない

会話 ID は継続性を表しますが、単独では利用者に対する認可の証明になりません。BFF はランダムな利用者キーとスレッドを対応付け、各要求で所有者を確認します。別利用者のスレッドや成果物には 404 を返します。

<div class="principle-grid">
  <article><span>01</span><h3>会話を作る</h3><p>最初のターンで Foundry Conversation を作成し、BFF 台帳へ保存。</p></article>
  <article><span>02</span><h3>要求を続ける</h3><p>以後の Responses 要求へ会話 ID を付け、ストリームを BFF が中継。</p></article>
  <article><span>03</span><h3>終了を試みる</h3><p>新規会話や終了要求で Agent セッションを削除し、台帳から関連情報を除去。</p></article>
</div>

<p class="inline-note">終了時の削除はブラウザー終了のようなケースでは best effort です。要求が届かなければセッションが最大 30 日残ることがあります。実運用では保持と削除の要件を明示してください。</p>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">04 / STREAM</div>
<div class="section-content" markdown="1">

## 生の Agent イベントをアプリの UI 契約へ変換する

Hosted Agent の Responses ストリームにはテキストだけでなく、ツール呼び出し、ツール結果、完了・失敗が含まれます。BFF は入力を検証し、これらを利用者向けの進行状況・成果物・回答イベントに変換します。

<div class="event-map">
  <div><code>function_call</code><span>→</span><b>tool.started</b><small>処理中の表示</small></div>
  <div><code>function_call_output</code><span>→</span><b>tool.completed / artifact.created</b><small>完了通知・成果物カード</small></div>
  <div><code>output_text.delta</code><span>→</span><b>text.delta</b><small>回答を逐次表示</small></div>
  <div><code>response.completed</code><span>→</span><b>done</b><small>応答 ID・使用量の記録</small></div>
</div>

ブラウザーが接続を切ったときはキャンセルを Agent 呼び出しへ伝播し、ターンの時間・同時実行数・利用者ごとの頻度にも上限を設けます。

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

このサンプルの業務 5 ツールは非公開 MCP で提供されます。起動時に許可リストと実スキーマの一致を確認し、loopback・動的ポート・要求サイズ・起動時の秘密値を検査します。

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
  <a href="{{ '/evaluation/' | relative_url }}"><small>前の章 / 02</small><span>Foundry で評価する</span></a>
  <a class="next-link" href="{{ '/improvement/' | relative_url }}"><small>次の章 / 04</small><span>改善を続ける <b aria-hidden="true">→</b></span></a>
</nav>
