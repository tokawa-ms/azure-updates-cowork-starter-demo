---
layout: default
title: 学習ガイド
description: Agent Framework で作ったエージェントを Foundry Hosted Agent で動かし、トレースと評価で品質を確かめ、確立、特定、最適化、検証の 4 つのステップで改善し続ける方法を、実コードと実行記録で学びます。
main_class: home
permalink: /
---

<section class="hero" markdown="1">
<div class="hero-copy" markdown="1">

<p class="eyebrow"><span class="eyebrow-line"></span> LEARNING GUIDE / 01—09</p>

# エージェントを作り、<br>品質を測り、<br>改善し続ける

<p class="hero-lead">Microsoft Agent Framework で作ったエージェントを Foundry Hosted Agent で動かし、トレースと評価の記録で品質を説明できる運用（Agent Ops）にします。リリースした後も、利用者の評価を評価データに戻し、直した版の差を統計的に確かめて、品質を上げ続けます。Azure Updates Cowork の実コード、Foundry ポータルの画面、評価の実行記録を使い、手を動かしながら順に学びます。</p>

<a class="button button-primary" href="{{ '/architecture/' | relative_url }}">学習を始める <span aria-hidden="true">→</span></a>

</div>
<div class="hero-index">
  <div class="index-heading"><span>LEARNING PATH</span><span>全 9 章</span></div>
  <a href="{{ '/architecture/' | relative_url }}"><span class="index-number">01</span><span>構成を読む</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/implementation/' | relative_url }}"><span class="index-number">02</span><span>作る: Agent Framework で実装する</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/hosting/' | relative_url }}"><span class="index-number">03</span><span>動かす: Hosted Agent でホストする</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/integration/' | relative_url }}"><span class="index-number">04</span><span>Web アプリへ組み込む</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/observability/' | relative_url }}"><span class="index-number">05</span><span>見る: トレースと監視</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/evaluation/' | relative_url }}"><span class="index-number">06</span><span>測る: 品質評価を自動化する</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/improvement/' | relative_url }}"><span class="index-number">07</span><span>直す: 失敗ケースから改善する</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/automation/' | relative_url }}"><span class="index-number">08</span><span>回し続ける: 継続的改善のループ</span><span class="index-arrow">↗</span></a>
  <a href="{{ '/adoption/' | relative_url }}"><span class="index-number">09</span><span>導入する: 自社のエージェントに当てはめる</span><span class="index-arrow">↗</span></a>
  <div class="index-foot"><span class="status-dot"></span> ソース、実行記録、ポータルの画面に基づく教材</div>
</div>
<div class="hero-stamp" aria-hidden="true">BUILD<br>MEASURE<br>REFINE</div>
</section>

<section class="intro-band">
  <div><span class="mini-label">到達点</span><p>1 回の依頼をトレースで追い、版ごとに品質を採点し、原因の層を直して再評価する流れを、自分の環境で再現できるようになります。本番の利用者の評価を評価データに戻し、版の差が偶然の範囲かを確かめて、出した版を次の基準にする流れも組み立てられます。</p></div>
  <div><span class="mini-label">対象</span><p>.NET と Microsoft Foundry で業務エージェントを作る開発者と、その品質を運用で確かめる担当者。</p></div>
  <div><span class="mini-label">読み方</span><p>各章は、実コードの抜粋、設定と実行、期待する結果の順に進みます。第 8 章は、Ignite 2026 で示された継続的改善の 4 つのステップに沿って、このリポジトリで実装した部分と、有効にしていない Foundry の機能を分けて扱います。</p></div>
</section>

<section class="section-block">
<div class="section-heading">
  <p class="eyebrow">AGENT OPS</p>
  <h2>5 つの活動を、1 つの循環で回す</h2>
  <p>業務を任せるエージェントの品質は、応答時間やエラー率だけでは説明できません。次の 4 つの理由から、途中の判断と結果を記録し、変更のたびに同じ基準で測る仕組みが必要です。このガイドでは、その仕組みを、作る、動かす、見る、測る、直すの循環として Agent Framework と Microsoft Foundry で組み立てます。</p>
</div>
<div class="principle-grid principle-grid-4">
  <article><span>01</span><h3>手順が依頼で変わる</h3><p>同じ種類の依頼でも、呼ぶツールや回数、順序が変わります。結果だけでは途中の判断が分かりません。</p></article>
  <article><span>02</span><h3>原因が層をまたぐ</h3><p>誤りの原因は、指示文、モデル、ツール、外部データ、評価の入力のどこにもあり得ます。</p></article>
  <article><span>03</span><h3>修正が別の所に響く</h3><p>1 か所の修正で、別のシナリオの品質が下がることがあります。変更のたびに全体を測ります。</p></article>
  <article><span>04</span><h3>費用が依頼で変わる</h3><p>このサンプルでは、入力トークンが依頼によって 5,640 から 112,412 まで違いました。</p></article>
</div>
<div class="cycle" aria-label="Agent Ops の循環">
  <article><span>第 2 章</span><h3>作る</h3><p>指示文、スキル、ツール、ミドルウェアを部品として組み立てる。</p></article>
  <article><span>第 3、4 章</span><h3>動かす</h3><p>Hosted Agent に版として配置し、本番と評価用を分ける。</p></article>
  <article><span>第 5 章</span><h3>見る</h3><p>1 回の依頼をトレースで追い、時間とトークンを把握する。</p></article>
  <article><span>第 6 章</span><h3>測る</h3><p>固定したシナリオを、評価器とコードの検査で採点する。</p></article>
  <article><span>第 7 章</span><h3>直す</h3><p>不合格の記録から原因の層を特定し、直して再評価する。</p></article>
  <div class="cycle-return">本番で見つかった失敗を次の評価に加え、もう一度回す（第 8 章） <span aria-hidden="true">↻</span></div>
</div>
</section>

<section class="section-block">
<div class="section-heading">
  <p class="eyebrow">CONTINUOUS IMPROVEMENT</p>
  <h2>リリースした後も、4 つのステップで品質を上げ続ける</h2>
  <p>Microsoft は Ignite 2026 で、エージェントの品質を上げ続ける進め方を、確立、特定、最適化、検証して繰り返すの 4 つのステップとして示しました。1 周ごとに評価データと比較の基準が更新され、出した版が次の基準になります。このガイドの章は、4 つのステップに次のように対応します。</p>
</div>
<div class="principle-grid principle-grid-4">
  <article><span>STEP 1 / 第 6、8 章</span><h3>確立する</h3><p>固定シナリオと 8 指標で基準を決める。利用者の評価をトレースに付け、低い評価を評価データに戻す。</p></article>
  <article><span>STEP 2 / 第 5、8 章</span><h3>特定する</h3><p>トレース、KQL、アラートで、繰り返す失敗と低い評価の増加を見つける。</p></article>
  <article><span>STEP 3 / 第 7、8 章</span><h3>最適化する</h3><p>原因の層だけを直す。エージェントの最適化で改善案を作る方法も確かめる。</p></article>
  <article><span>STEP 4 / 第 6〜8 章</span><h3>検証して繰り返す</h3><p>品質ゲートと統計的な比較で差を確かめ、版を固定して出す。出した版を次の基準にする。</p></article>
</div>
</section>

<section class="section-block">
<div class="section-heading">
  <p class="eyebrow">SOURCE CODE AND HANDS-ON</p>
  <h2>実装する順に読み進める</h2>
  <p>各章のコードには元ファイルへのリンクがあります。抜粋の前後や関連テストまで追うと、別の業務へ移すときに何を変更するか判断できます。</p>
</div>
<div class="chapter-grid chapter-grid-3">
  <a class="chapter-card" href="{{ '/architecture/' | relative_url }}">
    <span class="card-top"><span>01 / ARCHITECTURE</span><span class="card-symbol">↗</span></span>
    <h3>どこで何が動いているか</h3>
    <p>画面の操作、BFF、Hosted Agent、MCP、セッション領域を、1 回の依頼の流れで読み解きます。</p>
    <span class="card-link">画面と構成図を見る <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/implementation/' | relative_url }}">
    <span class="card-top"><span>02 / BUILD</span><span class="card-symbol">↗</span></span>
    <h3>Agent Framework で<br>何を書くか</h3>
    <p>AsAIAgent、SKILL.md、MCP ツール、ミドルウェアを組み合わせ、ローカルで実応答を確かめます。</p>
    <span class="card-link">C# とローカル演習 <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/hosting/' | relative_url }}">
    <span class="card-top"><span>03 / HOST</span><span class="card-symbol">↗</span></span>
    <h3>Hosted Agent として<br>どう動かすか</h3>
    <p>基盤が用意する版や ID、Responses のホスト登録、azure.yaml、本番と評価用の分離を確認します。</p>
    <span class="card-link">起動コードと配置設定 <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/integration/' | relative_url }}">
    <span class="card-top"><span>04 / APPLICATION</span><span class="card-symbol">↗</span></span>
    <h3>自社アプリにどう組み込むか</h3>
    <p>TokenCredential、要求 JSON、会話 ID、SSE と成果物台帳を、BFF のコードに沿って実装します。</p>
    <span class="card-link">BFF の実装と確認手順 <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/observability/' | relative_url }}">
    <span class="card-top"><span>05 / OBSERVE</span><span class="card-symbol">↗</span></span>
    <h3>1 回の依頼をどう追うか</h3>
    <p>Aspire Dashboard、Foundry のトレースとモニター、KQL で、処理の流れ、失敗、費用を確かめます。</p>
    <span class="card-link">トレースと監視 <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/evaluation/' | relative_url }}">
    <span class="card-top"><span>06 / EVALUATE</span><span class="card-symbol">↗</span></span>
    <h3>品質評価をどう自動化するか</h3>
    <p>JSONL のケース、Foundry 評価器へのマッピング、決定的検査、Actions の終了コードまで追います。</p>
    <span class="card-link">評価コードと CI 設定 <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/improvement/' | relative_url }}">
    <span class="card-top"><span>07 / IMPROVE</span><span class="card-symbol">↗</span></span>
    <h3>不合格からどう直すか</h3>
    <p>失敗ケースを分類し、原因の層だけを直して、同じ条件で再評価する手順を学びます。</p>
    <span class="card-link">改善の記録をたどる <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/automation/' | relative_url }}">
    <span class="card-top"><span>08 / IMPROVE CONTINUOUSLY</span><span class="card-symbol">↗</span></span>
    <h3>改善をどう回し続けるか</h3>
    <p>利用者の評価の注釈、低い評価のアラート、版の統計的な比較、品質ゲートで、確立、特定、最適化、検証のループをつなぎます。</p>
    <span class="card-link">継続的改善のループを回す <span aria-hidden="true">→</span></span>
  </a>
  <a class="chapter-card" href="{{ '/adoption/' | relative_url }}">
    <span class="card-top"><span>09 / ADOPT</span><span class="card-symbol">↗</span></span>
    <h3>自社のエージェントに<br>どう当てはめるか</h3>
    <p>導入前に決める 5 つのこと、機能の提供状況、3 つの段階の進め方をまとめます。</p>
    <span class="card-link">導入の進め方 <span aria-hidden="true">→</span></span>
  </a>
</div>
</section>

<section class="notice-row">
  <div class="notice-icon" aria-hidden="true">!</div>
  <div>
    <p class="eyebrow">BEFORE YOU RUN IT</p>
    <h2>実行には Azure の権限と料金が伴います</h2>
    <p>ローカルからのモデル呼び出しや Foundry 評価も Azure に接続し、モデルと judge の利用料金が発生します。機密情報を入力しないでください。評価の実行には、配置済みの評価用 Agent、適切な Foundry 権限、Azure CLI のサインインが必要です。第 8 章で扱う Insights、エージェントの最適化、継続評価などの Foundry の機能は、このリポジトリでは有効にしていません。多くはプレビューです。</p>
    <a class="text-link" href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/README.md">リポジトリの起動手順と注意事項 <span aria-hidden="true">↗</span></a>
  </div>
</section>
