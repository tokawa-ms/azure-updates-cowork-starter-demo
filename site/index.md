---
layout: default
title: 学習ガイド
description: C# の実装から Hosted Agent のホスト、Web 統合、自動品質評価まで、ソースと実行手順で学びます。
main_class: home
permalink: /
---

<section class="hero" markdown="1">
<div class="hero-copy" markdown="1">

<p class="eyebrow"><span class="eyebrow-line"></span> LEARNING GUIDE / 01—06</p>

# C# でエージェントを作り、<br>Web から使い、品質を測る

<p class="hero-lead">Agent Framework の実装、Foundry Hosted Agent のホスト、BFF からのストリーミング、GitHub Actions での品質ゲート。Azure Updates Cowork の実コードと実行手順を使い、要約と資料作成が動くまでを追います。</p>

<a class="button button-primary" href="{{ '/architecture/' | relative_url }}">学習を始める <span aria-hidden="true">→</span></a>

</div>
<div class="hero-index">
  <div class="index-heading"><span>LEARNING PATH</span><span>全 6 章</span></div>
  <a href="{{ '/architecture/' | relative_url }}"><span class="index-number">01</span><span>システムの境界を読む</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/implementation/' | relative_url }}"><span class="index-number">02</span><span>Agent Framework で実装する</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/hosting/' | relative_url }}"><span class="index-number">03</span><span>Hosted Agent でホストする</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/integration/' | relative_url }}"><span class="index-number">04</span><span>Web アプリへ組み込む</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/evaluation/' | relative_url }}"><span class="index-number">05</span><span>品質評価を自動化する</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/improvement/' | relative_url }}"><span class="index-number">06</span><span>失敗ケースから改善する</span><span class="index-arrow">↗</span></a>
  <div class="index-foot"><span class="status-dot"></span> ソースと実行記録に基づく教材</div>
</div>
<div class="hero-stamp" aria-hidden="true">BUILD<br>MEASURE<br>REFINE</div>
</section>

<section class="intro-band">
  <div><span class="mini-label">到達点</span><p>要約を呼び出し、Word を受け取り、変更後の応答を自動評価する手順を理解する。</p></div>
  <div><span class="mini-label">対象</span><p>.NET、Microsoft Foundry、Agent Framework を使うアプリ開発者。</p></div>
  <div><span class="mini-label">読み方</span><p>実コードの抜粋 → 設定と実行 → 期待する結果の順で確認する。既存環境の再配置は必須ではありません。</p></div>
</section>

<section class="section-block">
<div class="section-heading">
  <p class="eyebrow">SOURCE CODE AND HANDS-ON</p>
  <h2>実装する順に読み進める</h2>
  <p>各章のコードには元ファイルへのリンクがあります。抜粋の前後や関連テストまで追い、別の業務へ移すときに何を変更するか判断できます。</p>
</div>
<div class="chapter-grid">
  <a class="chapter-card" href="{{ '/architecture/' | relative_url }}">
    <span class="card-top"><span>01 / ARCHITECTURE</span><span class="card-symbol">↗</span></span>
    <h3>どこで何が動いているか</h3>
    <p>ブラウザー、BFF、Hosted Agent、MCP、セッションファイルを役割ごとに読み解きます。</p>
    <span class="card-link">構成図とトレースを見る <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/implementation/' | relative_url }}">
    <span class="card-top"><span>02 / IMPLEMENTATION</span><span class="card-symbol">↗</span></span>
    <h3>Agent Framework で何を書くか</h3>
    <p>AsAIAgent、SKILL.md、MCP ツール、ミドルウェアを組み合わせ、ローカルで実応答を確認します。</p>
    <span class="card-link">C# とローカル演習 <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/hosting/' | relative_url }}">
    <span class="card-top"><span>03 / HOSTING</span><span class="card-symbol">↗</span></span>
    <h3>Hosted Agent としてどう動かすか</h3>
    <p>Responses のホスト登録、Dockerfile、azure.yaml、実行 ID、セッションの保存領域を確認します。</p>
    <span class="card-link">起動コードと配置設定 <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/integration/' | relative_url }}">
    <span class="card-top"><span>04 / APPLICATION</span><span class="card-symbol">↗</span></span>
    <h3>自社アプリにどう組み込むか</h3>
    <p>TokenCredential、要求 JSON、会話 ID、SSE と成果物台帳を、BFF のコードに沿って実装します。</p>
    <span class="card-link">BFF の実装と確認手順 <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/evaluation/' | relative_url }}">
    <span class="card-top"><span>05 / EVALUATION</span><span class="card-symbol">↗</span></span>
    <h3>品質評価をどう自動化するか</h3>
    <p>JSONL のケース、Foundry 評価器へのマッピング、決定的検査、Actions の終了コードまで追います。</p>
    <span class="card-link">評価コードと CI 設定 <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/improvement/' | relative_url }}">
    <span class="card-top"><span>06 / IMPROVEMENT</span><span class="card-symbol">↗</span></span>
    <h3>改善をどう繰り返すか</h3>
    <p>失敗ケースを分類し、変更を限定して再評価し、本番へ反映するサイクルを学びます。</p>
    <span class="card-link">改善ループをたどる <span aria-hidden="true">→</span></span>
  </a>
</div>
</section>

<section class="notice-row">
  <div class="notice-icon" aria-hidden="true">!</div>
  <div>
    <p class="eyebrow">BEFORE YOU RUN IT</p>
    <h2>実行には Azure の権限と料金が伴います</h2>
    <p>ローカルからのモデル呼び出しや Foundry 評価も Azure に接続し、モデルと judge の利用料金が発生します。機密情報を入力しないでください。評価の実行には、配置済みの評価用 Agent、適切な Foundry 権限、Azure CLI のサインインが必要です。</p>
    <a class="text-link" href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/README.md">リポジトリの起動手順と注意事項 <span aria-hidden="true">↗</span></a>
  </div>
</section>
