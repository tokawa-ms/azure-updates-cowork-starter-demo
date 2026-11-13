---
layout: default
title: 06 失敗ケースから改善
description: Foundry Hosted Agent の失敗ケースを調査し、固定データの評価で改善を確認して本番へ反映する反復手順です。
main_class: lesson
permalink: /improvement/
---

<div class="lesson-kicker"><span>06</span><span>IMPROVEMENT</span><span>—</span><span>測定から反映まで</span></div>

# 不合格の証拠から修正箇所を決め、再評価する

<p class="lesson-lead">改善ループは「同じデータで測る」だけではありません。失敗した実応答とツール履歴を調べ、原因をシステムの該当箇所へ割り当て、変更後に同じ条件で再評価します。品質ゲートを通過しても、未見入力の品質が保証されるわけではありません。</p>

<section class="cycle" aria-label="エージェント改善の反復サイクル">
  <article><span>01</span><h2>失敗を集める</h2><p>評価レポート、利用者の低評価、トレースから対象ケースを特定。</p></article>
  <article><span>02</span><h2>原因を切り分ける</h2><p>入力、Agent 応答、ツール、judge の判定を別々に確認。</p></article>
  <article><span>03</span><h2>変更を一つに絞る</h2><p>指示文・スキル・ツール・評価ロジックのうち原因の層を修正。</p></article>
  <article><span>04</span><h2>同じ条件で再評価</h2><p>同じデータ、fixture、judge、しきい値で再実行し、差分を読む。</p></article>
  <article><span>05</span><h2>版を固定して反映</h2><p>未達を隠さず、確認済みの Agent 版を段階的に本番へ配置。</p></article>
  <div class="cycle-return">次の実行で再び失敗を集める <span aria-hidden="true">↻</span></div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">01 / TRIAGE</div>
<div class="section-content" markdown="1">

## 最初に「Agent の失敗」と決めつけない

品質レポートの不合格は、Agent の応答ミスだけを意味しません。judge の入力が欠けた、応答が未取得、成果物をダウンロードできない、といった評価側の問題もあります。必ず根拠を開き、次の観点で分類します。

| 症状 | 見る証拠 | 候補となる改善箇所 |
|---|---|---|
| 期待したツールが呼ばれない | Responses の output items、MCP の引数・結果 | 指示文、スキル、ツール定義 |
| 正しい文脈を使わない | 実際の developer / user 入力、ページ選択状態 | BFF の入力構築、文脈契約、指示文 |
| 引用・資料が不正 | 回答本文、ツール出力、実ファイル | スキル、ツール検証、成果物チェック |
| judge が入力を誤解する | judge に保存された実入力と元の出力 | 評価データ変換、評価器の適用範囲 |
| 実行・取得に失敗する | 状態、エラーコード、応答ファイル、セッション記録 | インフラや再開手順。品質合格として補完しない |

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">02 / EVIDENCE</div>
<div class="section-content" markdown="1">

## このリポジトリでの改善例

30 件の同じデータセットを使った評価 Agent v6 と v12 の結果です。いずれも v6 では全体ゲートが不合格、v12 では全 8 指標が基準に達しました。

<div class="result-compare">
  <div class="result-label"><span>METRIC</span><span>v6</span><span></span><span>v12</span></div>
  <div class="result-row"><b>指示遵守</b><span>20 / 30</span><i aria-hidden="true">→</i><strong>29 / 30</strong></div>
  <div class="result-row"><b>出典</b><span>29 / 30</span><i aria-hidden="true">→</i><strong>30 / 30</strong></div>
  <div class="result-row"><b>固定データの品質ゲート</b><span>未達</span><i aria-hidden="true">→</i><strong>8 指標すべて達成</strong></div>
</div>

原因調査では、ページ未選択でツールが始まる、複数の検索結果を一部しか取得しない、攻撃検出時の通知が抜ける、PowerPoint の必須引数が不足する、といったケースを実際の応答・ツール履歴で特定しました。指示文・スキルの手順や MCP 引数検証を直し、データセットやモデル、judge、しきい値は据え置きました。

### 例: PowerPoint の枚数をプロンプトだけに任せない

「合計3枚」を本文3枚と解釈すると、自動追加する表紙・出典と合わせて5枚になります。指示文を足すだけでなく、[CoworkTools.CreateDeckAsync](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Agent/Tools/CoworkTools.cs) で利用者が依頼した総枚数と本文の数を照合します。次は不一致を扱う分岐の抜粋です。

```csharp
else if (deck?.Slides is { } slides && slides.Count + 2 != totalSlides)
{
    errors.Add($"totalSlides={totalSlides} の指定では deck.slides を {totalSlides - 2} 枚にしてください。表紙と出典は自動で2枚追加します。利用者の指定枚数を変えて回避してはいけません。");
}
```

スキルには「合計 N 枚なら本文 N−2 枚」と書き、ツールは不一致を構造化エラーで返します。検査側も実ファイルの枚数などを確認します。ただし `totalSlides` 自体もモデルの引数なので、利用者の意図との一致はこの条件だけでは保証できません。元の依頼と完成ファイルを評価で照合する必要があります。

指示、実行時の検証、評価を分けると、ツールを修正した結果を judge の点数だけに頼らず確認できます。

<aside class="callout callout-caution">
  <span class="callout-label">比較の限界</span>
  <p>v6 と v12 の間で評価入力の欠落修正やローカル検査の強化も行ったため、完全に同じ採点処理の前後比較ではありません。指標の改善を Agent の変更だけに帰属させないでください。v12 は固定 30 件での一回の合格であり、未見データや反復実行での安定性は保証しません。</p>
</aside>

<a class="text-link" href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/docs/notes/evaluation.md">v6〜v12 の失敗分析・評価履歴を読む <span aria-hidden="true">↗</span></a>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">03 / CHANGE</div>
<div class="section-content" markdown="1">

## 小さな変更を、証拠に結びつける

<ol class="numbered-steps">
  <li><b>ケースを再現する</b><span>元の入力、固定 fixture、Agent バージョン、全 Responses 出力を保存します。</span></li>
  <li><b>失敗箇所を追う</b><span>ツールを呼んでいないなら指示や選択ロジック、ツール結果が誤りなら実装、judge 入力が違えば評価側を調べます。</span></li>
  <li><b>一つの層を修正する</b><span>関連のない指示・モデル・データ・しきい値を同時に変えず、どの変更が何に効いたか追える状態にします。</span></li>
  <li><b>通常のテストを先に実行</b><span><code>dotnet test --filter Category!=Eval</code> で、モデルを呼ばない回帰チェックを行います。</span></li>
  <li><b>固定データ評価をやり直す</b><span>同じ 30 ケース、fixture、judge、thresholds を使い、全指標と失敗した実ケースを比較します。</span></li>
  <li><b>Agent の版を固定して配置</b><span>評価用の結果を確認後、明示したバージョンを本番へ反映し、トレースとスモークテストを確認します。</span></li>
</ol>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">04 / OPERATE</div>
<div class="section-content" markdown="1">

## 改善後も、評価を本番運用と混同しない

<div class="two-column">
  <article><p class="eyebrow">このサンプルで実施</p><h3>固定データの手動評価</h3><ul><li>明示した評価 Agent の版</li><li>固定した 30 ケースと fixture</li><li>実応答の保存、Foundry 採点、決定的検査</li><li>実行後の評価セッション削除</li></ul></article>
  <article class="muted-panel"><p class="eyebrow">別途設計が必要</p><h3>本番トラフィックの継続評価</h3><ul><li>ユーザー入力・応答の記録と同意</li><li>本文の保護、閲覧権限、保持期間</li><li>採点頻度、judge の費用、誤判定対応</li><li>運用担当と品質ゲートの変更承認</li></ul></article>
</div>

このリポジトリでは継続評価ルールを作成・有効化していません。アプリ側で Agent の本文記録を無効にしても、Foundry 基盤が記録する内容まで無効になるとは限りません。実環境で確認した保存範囲と組織ポリシーを先に照合してください。

</div>
</section>

<section class="checkpoint" markdown="1">
<p class="eyebrow">CHECKPOINT</p>

## 改善をリリースする前に

- 各不合格に、応答・ツール・judge 入力の根拠が残っている。
- 未採点や失敗を合格に置き換えず、評価条件としきい値を維持している。
- どの Agent バージョンを評価し、どのバージョンを本番に配置したか記録している。
- 固定データの合格を、未見データや継続評価の保証として説明していない。

<div class="source-links">
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/eval/datasets/core.jsonl">評価データセット <span>↗</span></a>
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/eval/thresholds.json">しきい値の定義 <span>↗</span></a>
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/docs/notes/deploy.md">デプロイとバージョン管理の記録 <span>↗</span></a>
</div>
</section>

<nav class="lesson-next" aria-label="章の移動">
  <a href="{{ '/evaluation/' | relative_url }}"><small>前の章 / 05</small><span>品質評価を自動化する</span></a>
  <a class="next-link" href="{{ '/' | relative_url }}"><small>学習ガイド / END</small><span>トップへ戻る <b aria-hidden="true">→</b></span></a>
</nav>
