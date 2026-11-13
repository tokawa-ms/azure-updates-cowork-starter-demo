---
layout: default
title: 学習ガイド
description: Azure Updates Cowork の構成、Foundry 評価、Hosted Agent の統合と改善を学びます。
main_class: home
permalink: /
---

<section class="hero" markdown="1">
<div class="hero-copy" markdown="1">

<p class="eyebrow"><span class="eyebrow-line"></span> LEARNING GUIDE / 01—04</p>

# Hosted Agent を、<br>アプリの仕事の流れにする。

<p class="hero-lead">構成を読み、固定データで品質を確かめ、自社アプリから安全に呼び出す。Azure Updates Cowork の実装と評価記録をたどりながら、エージェントを改善する一連の手順を学びます。</p>

<a class="button button-primary" href="{{ '/architecture/' | relative_url }}">学習を始める <span aria-hidden="true">→</span></a>

</div>
<div class="hero-index">
  <div class="index-heading"><span>LEARNING PATH</span><span>全 4 章</span></div>
  <a href="{{ '/architecture/' | relative_url }}"><span class="index-number">01</span><span>システムの境界を読む</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/evaluation/' | relative_url }}"><span class="index-number">02</span><span>同じ条件で品質を測る</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/integration/' | relative_url }}"><span class="index-number">03</span><span>アプリから Hosted Agent を呼ぶ</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/improvement/' | relative_url }}"><span class="index-number">04</span><span>結果から次の変更を決める</span><span class="index-arrow">↗</span></a>
  <div class="index-foot"><span class="status-dot"></span> ソースと実行記録に基づく教材</div>
</div>
<div class="hero-stamp" aria-hidden="true">BUILD<br>MEASURE<br>REFINE</div>
</section>

<section class="intro-band">
  <div><span class="mini-label">目的</span><p>デモの機能だけでなく、業務アプリへ移すときに必要な境界・権限・評価を理解する。</p></div>
  <div><span class="mini-label">対象</span><p>.NET、Microsoft Foundry、Agent Framework を使うアプリ開発者。</p></div>
  <div><span class="mini-label">読み方</span><p>章の順に進み、各章末の確認項目を実装とレポートで確かめる。</p></div>
</section>

<section class="section-block">
<div class="section-heading">
  <p class="eyebrow">THE SYSTEM, IN FOUR QUESTIONS</p>
  <h2>知りたいことから章を選ぶ</h2>
  <p>一つの完成形を真似するのではなく、なぜその責務を分けたのか、何を証拠に改善と判断するのかを追います。</p>
</div>
<div class="chapter-grid">
  <a class="chapter-card" href="{{ '/architecture/' | relative_url }}">
    <span class="card-top"><span>01 / ARCHITECTURE</span><span class="card-symbol">↗</span></span>
    <h3>どこで何が動いているか</h3>
    <p>ブラウザー、BFF、Hosted Agent、MCP、セッションファイルを役割ごとに読み解きます。</p>
    <span class="card-link">構成図とトレースを見る <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/evaluation/' | relative_url }}">
    <span class="card-top"><span>02 / EVALUATION</span><span class="card-symbol">↗</span></span>
    <h3>Foundry の評価をどう読むか</h3>
    <p>評価用 Agent の実応答を採点し、クラウドの評価器とローカル品質ゲートを使い分けます。</p>
    <span class="card-link">実行手順と結果の見方 <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/integration/' | relative_url }}">
    <span class="card-top"><span>03 / APPLICATION</span><span class="card-symbol">↗</span></span>
    <h3>自社アプリにどう組み込むか</h3>
    <p>マネージド ID、会話、ページ文脈、ストリーミング、成果物の受け渡しを確認します。</p>
    <span class="card-link">統合の境界を確認する <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/improvement/' | relative_url }}">
    <span class="card-top"><span>04 / IMPROVEMENT</span><span class="card-symbol">↗</span></span>
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
