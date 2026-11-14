---
layout: default
title: 01 構成を読む
description: Azure Updates Cowork のブラウザー、BFF、Hosted Agent、MCP、Session Files、トレースの役割を学びます。
main_class: lesson
permalink: /architecture/
---

<div class="lesson-kicker"><span>01</span><span>ARCHITECTURE</span><span>—</span><span>境界と責務</span></div>

# 実装を追うために、システムの境界を確認する

<p class="lesson-lead">ブラウザーは画面と利用者の操作を担当します。Hosted Agent の呼び出し、権限確認、成果物の持ち主確認はサーバー側の BFF が担います。エージェントのツールは限定した業務機能だけを実行します。</p>

<figure class="diagram" markdown="1">
![ブラウザーから Web BFF、Microsoft Foundry Hosted Agent、非公開 MCP、更新情報 API、セッションファイル、Application Insights への接続を示す構成図]({{ '/assets/01-architecture-overview.png' | relative_url }})
<figcaption><span>図 01</span> 全体構成。MCP は Hosted Agent のコンテナー内で loopback のみを待ち受け、外部公開しません。</figcaption>
</figure>

<section class="lesson-section" markdown="1">
<div class="section-marker">01 / SCREEN</div>
<div class="section-content" markdown="1">

## 画面から、要約や資料の作成を依頼する

左で Azure の更新情報を選び、右のチャットから要約、Word、PowerPoint の作成を依頼します。画面は Azure Container Apps で動く Web アプリ、依頼を処理するのは Microsoft Foundry の Hosted Agent です。

<figure class="diagram diagram-small">
  <a href="{{ '/assets/screens/app-overview.png' | relative_url }}" title="原寸で開く"><img src="{{ '/assets/screens/app-overview.png' | relative_url }}" alt="Azure Updates Cowork の画面。左に更新情報の検索と一覧、中央に選択した記事、右に作業ボタン、進行状況、成果物カード、チャットの入力欄がある"></a>
  <figcaption><span>画面 01</span> 番号は下の説明と対応します。2026 年 11 月 14 日に撮影しました。</figcaption>
</figure>

<ol class="marker-list">
  <li><b>更新情報の検索と、作業する記事の選択</b><br>選んだ記事の ID、タイトル、公式 URL を、依頼の文脈としてエージェントに渡します。記事の本文は渡しません。</li>
  <li><b>ボタンで要約、Word、PowerPoint を依頼</b><br>ボタンは定型の依頼文を送ります。ボタンの種類は、BFF がテレメトリの属性（<code>auc.action</code>）にだけ使います。</li>
  <li><b>エージェントが立てた手順と進み具合</b><br>エージェントが <code>update_plan</code> ツールで報告した計画を、処理の途中で表示します。</li>
  <li><b>成果物カードからのダウンロード</b><br>BFF が持ち主を確かめてから、セッション領域のファイルを返します。</li>
</ol>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">02 / REQUEST</div>
<div class="section-content" markdown="1">

## 一つの依頼を、境界に沿って追う

<div class="flow-list">
  <article class="flow-item"><span class="flow-number">1</span><div><h3>ブラウザー → Web BFF</h3><p>ブラウザーは同一オリジンの Web API を呼びます。選択中の更新 ID・タイトル・公式 URL を会話の文脈として渡します。記事本文は画面で表示するだけで、ブラウザーからモデルへ送りません。</p></div><code>POST /api/chat</code></article>
  <article class="flow-item"><span class="flow-number">2</span><div><h3>Web BFF → Hosted Agent</h3><p>BFF はマネージド ID で Entra ID トークンを取得し、Responses プロトコルで会話を作成して応答をストリーミングします。会話 ID と利用者の対応は BFF の台帳で管理します。</p></div><code>Responses + SSE</code></article>
  <article class="flow-item"><span class="flow-number">3</span><div><h3>Hosted Agent → MCP ツール</h3><p>指示文とスキルが作業手順を定め、MCP が更新検索・詳細取得・計画・Office 作成の境界になります。取得した記事本文は信頼しないデータとして扱い、ツールの取得先ホストは固定です。</p></div><code>Streamable HTTP</code></article>
  <article class="flow-item"><span class="flow-number">4</span><div><h3>セッションファイル → BFF → 利用者</h3><p>Word と PowerPoint は Agent の Hosted Session Files に置きます。BFF は利用者との所有関係を台帳で確認し、本人の成果物だけをダウンロードさせます。</p></div><code>Session Files API</code></article>
</div>

### Word を依頼したときの呼び出し順

1 回の依頼で、手順書（スキル）の読み込み、計画の報告、記事の取得、Word の生成と保存までを進めます。進行状況（`plan.updated`）と成果物の通知（`artifact.created`）は、処理の途中で画面に届きます。

<figure class="diagram diagram-small">
  <a href="{{ '/assets/03-chat-turn-sequence.png' | relative_url }}" title="原寸で開く"><img src="{{ '/assets/03-chat-turn-sequence.png' | relative_url }}" alt="画面、Web BFF、Hosted Agent、非公開 MCP、データ API、セッション領域の間で、Word を作る 1 回の依頼が進む順序を示すシーケンス図"></a>
  <figcaption><span>図 03</span> チャット 1 回分の処理。この流れを第 5 章でトレースとして追い、第 6 章で評価の対象にします。</figcaption>
</figure>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">03 / AGENT</div>
<div class="section-content" markdown="1">

## 「エージェント」は一つのプロンプトではない

このサンプルは、変更点を観測・評価できるように役割を分けています。特定の失敗を見つけたら、まず関係する部品を特定できます。

<figure class="diagram diagram-small" markdown="1">
![エージェントの指示文、スキル、ツール、進行状況、成果物の5要素]({{ '/assets/05-agent-building-blocks.png' | relative_url }})
<figcaption><span>図 05</span> 指示文、スキル、ツール、進行状況、成果物。各部品を独立して確認します。</figcaption>
</figure>

| 部品 | このサンプルでの役割 | 主な確認場所 |
|---|---|---|
| 指示文 | 判断順序、事実の扱い、安全な応答の制約 | `src/UpdatesCowork.Agent/Instructions/` |
| スキル | 要約、比較、Word、PowerPoint の作業手順 | `src/UpdatesCowork.Agent/Skills/` |
| ツール | 更新情報の検索・取得、計画、成果物作成 | `src/UpdatesCowork.Agent/Mcp/` |
| 進行状況・成果物 | 処理の可視化と、検証済みファイルの受け渡し | `ChatEventMapper`、`Artifacts/` |

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">04 / TRACE</div>
<div class="section-content" markdown="1">

## 応答ではなく、処理のつながりを見る

応答の文章だけでは、正しいツールが実行されたか、成果物が検証されたかは分かりません。Web の `chat.turn` から Word の保存までが 1 本のトレースでつながるため、親子関係と失敗した位置を確認できます。

<div class="trace-strip" aria-label="トレースの例"><span>chat.turn</span><b>→</b><span>invoke_agent</span><b>→</b><span>execute_tool</span><b>→</b><span>tools/call</span><b>→</b><span>office.word.build</span><b>→</b><span>artifact.save</span></div>

ローカルの Aspire Dashboard、Azure の Application Insights と Foundry ポータルでの確認手順は、第 5 章で扱います。

<aside class="callout callout-caution">
  <span class="callout-label">運用上の注意</span>
  <p>この環境では自作 Agent の本番メッセージ本文記録を無効にしていますが、Foundry の <code>responsesapi</code> 側には本文が記録されることを確認しています。機密情報を入力しないでください。</p>
</aside>

</div>
</section>

<section class="checkpoint" markdown="1">
<p class="eyebrow">CHECKPOINT</p>

## この章を終える前に

- 画面のボタンや選択が、どの API と文脈としてエージェントに渡るか説明できる。
- ブラウザーが直接 Hosted Agent を呼ばず、BFF が仲介する理由を説明できる。
- 会話 ID・利用者 ID・成果物 ID の所有関係を BFF が確認する理由を説明できる。
- Word を作る 1 回の依頼が、どの順序でツールを呼ぶか説明できる。

<div class="source-links">
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/docs/design.md">設計書 §3、§5、§7、§8 <span>↗</span></a>
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/docs/notes/observability.md">MCP とトレースを見る <span>↗</span></a>
</div>
</section>

<nav class="lesson-next" aria-label="章の移動">
  <a href="{{ '/' | relative_url }}"><small>学習ガイド</small><span>トップへ戻る</span></a>
  <a class="next-link" href="{{ '/implementation/' | relative_url }}"><small>次の章 / 02</small><span>作る: Agent Framework で実装する <b aria-hidden="true">→</b></span></a>
</nav>
