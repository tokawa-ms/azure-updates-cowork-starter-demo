---
layout: default
title: "09 導入する: 自社への適用"
description: Agent Ops の仕組みを自社のエージェントに当てはめるために、機能の対応、導入前に決めること、機能の提供状況、3 つの段階の進め方をまとめます。
main_class: lesson
permalink: /adoption/
---

<div class="lesson-kicker"><span>09</span><span>ADOPT</span><span>—</span><span>導入する: 自社への適用</span></div>

# 自社のエージェントに Agent Ops を当てはめる

<p class="lesson-lead">このガイドで確かめた仕組みは、Agent Framework と Microsoft Foundry にそろっています。自社の業務に移すときは、部品と評価シナリオを置き換え、記録、費用、プレビュー機能、自動化の範囲を先に決めます。</p>

<section class="lesson-section" markdown="1">
<div class="section-marker">01 / MAP</div>
<div class="section-content" markdown="1">

## 活動ごとに、使う機能と確かめたことを整理する

Agent Ops の活動ごとに、使う機能、このリポジトリで確かめたこと、対応する章をまとめました。自社の業務に移すときは、この表の「使う機能」を出発点に、部品と評価シナリオを置き換えます。

| 活動 | 使う機能 | このリポジトリで確かめたこと | 章 |
|---|---|---|---|
| 作る | Agent Framework（指示文、スキル、ツール、ミドルウェア） | 部品ごとに直し、同じ評価で比べた | [第 2 章]({{ '/implementation/' | relative_url }}) |
| 動かす | Foundry Hosted Agent（版、エージェント ID、セッション） | 本番と評価用を同じコードから配置した | [第 3 章]({{ '/hosting/' | relative_url }})、[第 4 章]({{ '/integration/' | relative_url }}) |
| 見る | トレースとモニター（Application Insights） | Web から Word の保存までを 1 本で追跡した | [第 5 章]({{ '/observability/' | relative_url }}) |
| 測る | Foundry の評価器と、コードによる検査 | 固定 30 件、8 指標で版ごとに判定した | [第 6 章]({{ '/evaluation/' | relative_url }}) |
| 直す | 判定の理由とトレースの照合 | v6 から v12 で、8 指標すべてが基準に届いた | [第 7 章]({{ '/improvement/' | relative_url }}) |
| 回し続ける | 継続評価、Insights、最適化、版の固定 | 実施していない（設計だけ） | [第 8 章]({{ '/automation/' | relative_url }}) |

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">02 / DECIDE</div>
<div class="section-content" markdown="1">

## 導入の前に、5 つのことを決める

Agent Ops の仕組みを本番で使う前に、次の 5 点を自社のポリシーと照らし合わせます。

| 項目 | 留意点 | 進め方 |
|---|---|---|
| 本文の記録 | 本番の採点には、トレースに残る入出力の本文が必要です。アプリの設定とは別に、基盤側で本文が記録される場合もあります | 記録の範囲、同意、閲覧権限、保持期間を決める |
| 評価の範囲 | 固定シナリオでの合格は、未知の入力での品質を保証しません | 本番で失敗した依頼を、評価シナリオに加え続ける |
| 費用 | セッション、トレースの量、判定用と最適化用のモデルに費用がかかります | 採点の頻度、サンプル数、保持期間を調整する |
| プレビュー | 継続評価、アラート、Insights、最適化、一部の評価器などはプレビューです | 本番の判断に使う範囲を決め、段階的に取り入れる |
| 自動化 | 改善案を自動で反映すると、意図しない変更が本番に入るおそれがあります | 反映前の承認と、前の版に戻す手順を用意する |

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">03 / STATUS</div>
<div class="section-content" markdown="1">

## 機能の提供状況を確かめる

GA の機能とプレビューの機能を分け、本番の判断に使う範囲を決めます。次の表は、2026 年 11 月 14 日に Microsoft Learn で確かめた内容です。

| 機能 | 提供状況 | このリポジトリ |
|---|---|---|
| Hosted Agent | <span class="status status-ga">GA</span> | 使用 |
| トレース（プロンプト エージェントと Hosted Agent） | <span class="status status-ga">GA</span> | 使用 |
| 評価 | <span class="status status-ga">GA</span> 一部の評価器と機能はプレビュー | 使用 |
| 評価器: タスクの遵守、意図の解決 | <span class="status status-preview">プレビュー</span> | 使用 |
| モニター（Agent Monitoring Dashboard） | <span class="status status-preview">プレビュー</span> | 画面で確認 |
| 継続評価、スケジュールされた評価 | <span class="status status-preview">プレビュー</span> | 未実施 |
| カスタム評価器 | <span class="status status-preview">プレビュー</span> | 未実施 |
| アラート | <span class="status status-preview">プレビュー</span> | 未実施 |
| Insights | <span class="status status-preview">プレビュー</span> | 未実施 |
| トレースから評価データを作る | <span class="status status-preview">プレビュー</span> | 未実施 |
| 評価用の azd 拡張（`azure.ai.evaluations`） | <span class="status status-preview">プレビュー</span> | 未実施 |
| エージェントの最適化 | <span class="status status-limited">限定プレビュー</span> | 未実施 |
| レッドチーミング | <span class="status status-ga">GA</span> モニターの定期スキャンはプレビュー | 未実施 |

<p class="inline-note">ツール呼び出しの精度の評価器には、プレビューの表示がありません。提供状況は変わるため、導入するときに「Microsoft Foundry portal general availability overview」と各機能のページで最新の情報を確かめてください。</p>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">04 / STEPS</div>
<div class="section-content" markdown="1">

## 3 つの段階で、観測から改善の自動化まで整える

最初は 1 つの業務に絞ります。各段階の終わりに結果を確かめ、次の段階へ進むかを判断します。

<div class="principle-grid">
  <article><span>STEP 1 / 見る</span><h3>観測の土台</h3><ul><li>Hosted Agent への配置</li><li>トレースの接続と確認</li><li>本文を記録する範囲の決定</li></ul><p class="stage-result">完了の目安: 1 回の依頼を端から端まで追跡できる</p></article>
  <article><span>STEP 2 / 測る</span><h3>品質ゲート</h3><ul><li>代表シナリオと固定データ</li><li>評価器と合格基準の決定</li><li>評価用エージェントの分離</li></ul><p class="stage-result">完了の目安: 版ごとの合否レポートが出る</p></article>
  <article><span>STEP 3 / 回す</span><h3>改善の自動化</h3><ul><li>本番の継続評価とアラート</li><li>本番の失敗の評価データ化</li><li>最適化と承認による反映</li></ul><p class="stage-result">完了の目安: 承認した改善だけが本番に入る</p></article>
</div>

### 最初の一歩

<ol class="numbered-steps">
  <li><b>対象業務を選ぶ</b><span>エージェントに任せる業務と、成功の条件を決めます。</span></li>
  <li><b>評価シナリオを作る</b><span>代表的な依頼と期待する動きを 30 件程度そろえます。形式は <code>eval/datasets/core.jsonl</code> を参考にします。</span></li>
  <li><b>検証環境で試す</b><span>Hosted Agent に配置し、トレースと評価の結果を確かめます。</span></li>
</ol>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">05 / REFERENCES</div>
<div class="section-content" markdown="1">

## 参考資料: Microsoft Learn

### 構築、配置、観測

1. [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/overview/): エージェントの組み立て方の全体像。
2. [What are hosted agents?](https://learn.microsoft.com/azure/foundry/agents/concepts/hosted-agents): 版、エージェント ID、セッション、観測の仕組み。
3. [Manage hosted agents](https://learn.microsoft.com/azure/foundry/agents/how-to/manage-hosted-agent): 本番を固定したまま新しい版を出す手順。
4. [Set up CI/CD for hosted agents with the Azure Developer CLI](https://learn.microsoft.com/azure/foundry/agents/how-to/set-up-ci-cd-cli): 配置と評価のパイプライン。
5. [Agent tracing overview](https://learn.microsoft.com/azure/foundry/observability/concepts/trace-agent-concept): トレースで記録される内容。
6. [Monitor agents with the Agent Monitoring Dashboard](https://learn.microsoft.com/azure/foundry/observability/how-to/how-to-monitor-agents-dashboard): モニター、継続評価、アラートの設定。
7. [Microsoft Foundry portal general availability overview](https://learn.microsoft.com/azure/foundry/concepts/general-availability): 機能ごとの提供状況。

### 評価と改善

{:start="8"}
8. [Evaluation in Microsoft Foundry](https://learn.microsoft.com/azure/foundry/concepts/evaluation-overview): 評価の種類と選び方。
9. [Agent evaluators](https://learn.microsoft.com/azure/foundry/concepts/evaluation-evaluators/agent-evaluators): エージェント向けの評価器と入力。
10. [Evaluate individual interactions from deployed models and agents](https://learn.microsoft.com/azure/foundry/observability/how-to/cloud-evaluation-deployed-interactions): 本番のトレースの採点。
11. [Convert agent traces into evaluation datasets](https://learn.microsoft.com/azure/foundry/observability/how-to/traces-to-dataset): トレースから評価データを作る手順。
12. [Use Insights in Foundry (preview)](https://learn.microsoft.com/azure/foundry/observability/how-to/agent-insights): 問題の分類と修正案。
13. [What is the agent optimizer?](https://learn.microsoft.com/azure/foundry/agents/concepts/agent-optimizer-overview): 最適化の仕組みと結果の読み方。
14. [Make your agent optimizer-ready](https://learn.microsoft.com/azure/foundry/agents/how-to/make-agent-optimizer-ready): 最適化に対応させるコードの変更。
15. [Optimize skills in Microsoft Foundry (preview)](https://learn.microsoft.com/azure/foundry/agents/how-to/optimize-skills-from-traces): トレースからのスキルの改善。

</div>
</section>

<section class="checkpoint" markdown="1">
<p class="eyebrow">CHECKPOINT</p>

## ガイドを終える前に

- 自社のエージェントについて、作る、動かす、見る、測る、直すのそれぞれで使う機能を挙げられる。
- 本番で本文を記録する範囲と、採点に使う指標を決められる。
- プレビューの機能を、本番の判断にどこまで使うか説明できる。
- 3 つの段階のうち最初に取り組む段階と、その完了の目安を決められる。

<div class="source-links">
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/README.md">README（起動手順と注意事項） <span>↗</span></a>
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/docs/design.md">設計書 <span>↗</span></a>
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/docs/notes/evaluation.md">評価の実行記録 <span>↗</span></a>
</div>
</section>

<nav class="lesson-next" aria-label="章の移動">
  <a href="{{ '/automation/' | relative_url }}"><small>前の章 / 08</small><span>回し続ける: 改善を自動化する</span></a>
  <a class="next-link" href="{{ '/' | relative_url }}"><small>学習ガイド / END</small><span>トップへ戻る <b aria-hidden="true">→</b></span></a>
</nav>
