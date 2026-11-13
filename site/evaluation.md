---
layout: default
title: 02 Foundry で評価
description: 品質ゲートの価値と限界を理解し、Foundry 評価とローカル検査を実行・確認する手順です。
main_class: lesson
permalink: /evaluation/
---

<div class="lesson-kicker"><span>02</span><span>EVALUATION</span><span>—</span><span>同じ条件で測る</span></div>

# 評価の実行完了と、<br>品質の合格を分けて読む。

<p class="lesson-lead">同じ業務シナリオで応答、ツールの動き、生成ファイルを確認し、Agent の更新判断に使える根拠を残します。Foundry の評価だけでなく、ファイルや出典などの機械的な検査も組み合わせます。</p>

<figure class="diagram" markdown="1">
![30件の固定データを評価用Hosted Agentに送り、応答を記録し、Foundryの3評価器とローカルの決定的な検査に渡す評価パイプライン]({{ '/assets/10-evaluation-pipeline.png' | relative_url }})
<figcaption><span>図 10</span> 本番とは別の評価用 Agent と固定 fixture を使います。継続評価や本番トラフィックの自動採点は有効にしていません。</figcaption>
</figure>

<section class="lesson-section" markdown="1">
<div class="section-marker">01 / PURPOSE</div>
<div class="section-content" markdown="1">

## 品質ゲートが揃える判断材料

このゲートは、定義したケースの範囲で、回答の印象だけでなく業務処理が最後まで正しく行われたかを確認します。評価用 Agent と固定データを使うため、同じ条件で版ごとの結果を比べられます。

<div class="metric-grid">
  <article><span class="metric-number">01</span><h3>変更前後を比較</h3><p>固定したシナリオと基準を使い、Agent の版ごとの結果を追います。</p></article>
  <article><span class="metric-number">02</span><h3>回答以外も確認</h3><p>ツールの実行、公式出典、実際に生成した Word・PowerPoint を調べます。</p></article>
  <article><span class="metric-number">03</span><h3>改善の根拠を残す</h3><p>応答、検査結果、不合格理由をレポートに保存し、修正箇所を特定します。</p></article>
</div>

<aside class="callout callout-info">
  <span class="callout-label">合格の意味</span>
  <p>ゲート通過は、定義したケースと基準を満たしたという判断です。あらゆる入力や将来の応答が正しいことを保証するものではありません。</p>
</aside>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">02 / BEFORE</div>
<div class="section-content" markdown="1">

## 実行前に揃えるもの

<ul class="check-list">
  <li><span>配置済みの評価用 Agent</span><code>updates-cowork-eval</code> の特定バージョンを指定します。</li>
  <li><span>評価用の Foundry プロジェクト</span>Agent と judge model を使える Entra ID 権限が必要です。</li>
  <li><span>Azure CLI のサインイン</span>コマンド実行者の ID で認証します。API キーは使いません。</li>
  <li><span>秘密情報を含まない固定データ</span>このサンプルは更新情報 fixture と 30 件のデータセットを使います。</li>
</ul>

<aside class="callout callout-caution">
  <span class="callout-label">料金とデータ</span>
  <p>Agent の応答生成と Foundry の judge 呼び出しには料金が発生します。評価データと応答は保存されるため、機密情報を追加しないでください。</p>
</aside>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">03 / RUN</div>
<div class="section-content" markdown="1">

## バージョンを固定して実行する

リポジトリのルートから、評価 Agent の実在するバージョンと新しい出力先を指定します。30 件を使う例です。

<div class="code-frame">
  <div class="code-head"><span>PowerShell 7 / Foundry の既存プロジェクトを指定</span><button type="button" class="copy-button" data-copy="eval-command">コピー</button></div>
  <pre><code id="eval-command">.\eval\run-foundry.ps1 `
  -ProjectEndpoint 'https://&lt;account&gt;.services.ai.azure.com/api/projects/&lt;project&gt;' `
  -AgentVersion '&lt;評価用Agentの版&gt;' `
  -Limit 30 -Out eval\out\core</code></pre>
</div>

<ol class="numbered-steps">
  <li><b>prepare</b><span>データセット、fixture、Agent の版を検証し、実行要求とハッシュ値を保存します。</span></li>
  <li><b>responses</b><span>固定ケースごとに Hosted Agent を呼び、ツール呼び出しを含む完全な応答を記録します。</span></li>
  <li><b>evaluate</b><span>保存済み応答を Foundry の 3 評価器に渡します。応答を失わず、後から同じ入力を検査できます。</span></li>
  <li><b>check</b><span>ローカルの決定的検査を加えて 8 指標を集計し、Markdown・JSON・HTML レポートを作ります。</span></li>
</ol>

<p class="inline-note">途中で止まった場合は、同じ Agent 版・件数・出力先に <code>-Resume</code> を付けます。保存済みの応答をむやみに再生成しません。</p>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">04 / PORTAL</div>
<div class="section-content" markdown="1">

## Foundry Portal で見るのは、クラウド評価の詳細

評価を登録した Foundry プロジェクトを開き、**Evaluations** の一覧から、この実行で作成された Evaluation / run を選びます。完了した run を開き、評価指標の集計を確認してから、個別のサンプルを開いて入力、Agent の応答、ツール呼び出し、judge の判定理由を読みます。

<div class="portal-sequence">
  <div><span>01</span><b>プロジェクトを選ぶ</b><p>スクリプトの <code>ProjectEndpoint</code> と同じ Foundry プロジェクトか確認します。</p></div>
  <div><span>02</span><b>評価と run を開く</b><p>一覧から今回の実行を選択し、状態と評価器ごとの結果を確認します。</p></div>
  <div><span>03</span><b>ケース単位で調べる</b><p>失敗したケースの実入力・応答・採点理由を開き、実際に起きたことと judge の解釈を照合します。</p></div>
  <div><span>04</span><b>ローカルレポートと照合</b><p>Foundry 側の評価は 3 指標です。残りの検査と総合ゲートは <code>report.html</code> で確認します。</p></div>
</div>

<aside class="callout callout-info">
  <span class="callout-label">画面に関する注記</span>
  <p>このリポジトリの実行は Evals API で確認しています。Foundry Portal は追加サインインが必要だったため、現在の画面配置・ラベルを実機で目視確認した結果ではありません。Portal の変更により項目名や配置が異なる場合は、Microsoft Learn の現行手順を参照してください。</p>
</aside>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">05 / READ</div>
<div class="section-content" markdown="1">

## 集計の前に、分母と証拠を確かめる

<div class="metric-grid">
  <article><span class="metric-number">3</span><h3>Foundry の judge 指標</h3><p><code>task_adherence</code>、<code>intent_resolution</code>、<code>tool_call_accuracy</code>。合格率の目安は各 80% 以上です。</p></article>
  <article><span class="metric-number">5</span><h3>ローカルの決定的検査</h3><p>ツール使用、出典、日本語、実成果物、攻撃への抵抗を確認します。基準は 100% です。</p></article>
  <article><span class="metric-number">0</span><h3>成功扱いにしない欠落</h3><p>未採点、実行失敗、欠落応答は合格に補完しません。明示的な対象外は合格と分けて記録します。</p></article>
</div>

## 30 件の実証結果と適用範囲

2026 年 11 月 13 日の 30 件では、8 指標すべてが基準を満たしました。指示への適合と依頼の意図はそれぞれ 29 / 30、ツール選択は対象外 4 件を除いて 26 / 26、決定的検査の 5 指標はそれぞれ 30 / 30 でした。実際に生成した Word 5 件、PowerPoint 5 件も取得して検査しています。

<aside class="callout callout-caution">
  <span class="callout-label">結果の読み方</span>
  <p>一部のケースは評価モデルの基準に達していませんが、基準未達を残したままでも合格率の基準を満たしたため、全体のゲートは通過しました。これは固定 30 件を使った 1 回の結果です。未見の入力や、繰り返し実行した場合の安定性までは保証しません。攻撃への検査も固定ケースに限られます。</p>
</aside>

<p class="inline-note">日本語の検査は URL を除いた文字の 30% 以上が日本語かを見る簡易判定で、文章の自然さや専門用語の適切さは評価しません。</p>

<aside class="callout callout-info">
  <span class="callout-label">実行頻度</span>
  <p>通常のプルリクエスト検査はネットワークやモデルを使わない回帰テストです。モデル利用料が発生するフル評価は明示的に実行し、本番トラフィックの自動採点は行いません。GitHub Actions の手動実行ワークフローはありますが、GitHub 上での実行は認証設定を含めて未確認です。</p>
</aside>

| ファイル | 確認する内容 |
|---|---|
| `run.json` / `evaluation.json` | run ID、評価 ID、状態。`completed` は採点処理の完了であり品質合格ではありません。 |
| `output-items.json` | 個別サンプルの judge 結果、理由、元の出力。理由だけで score を作り直さないこと。 |
| `summary.md` / `report.html` | 8 指標の合格数、対象外、しきい値、不合格理由。チームでの改善判断に使います。 |
| `responses/` / `artifacts/` | 完全な実応答と取得した Word / PowerPoint。ケースの根拠を確認できます。 |

<p class="inline-note">評価データは要約・Word・PowerPoint・検索と比較・ページ未選択・対象外依頼・間接プロンプトインジェクションを含みます。ケース ID とカテゴリは <code>eval/datasets/core.jsonl</code> を参照してください。</p>

</div>
</section>

<section class="checkpoint" markdown="1">
<p class="eyebrow">CHECKPOINT</p>

## この章を終える前に

- Foundry Portal の 3 指標とローカルレポートの 5 指標の違いを説明できる。
- `completed`、品質ゲート合格、対象外、未採点を区別できる。
- 点数だけでなく失敗ケースの元応答・ツール出力・判定理由を確認できる。

<div class="source-links">
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/docs/notes/evaluation.md">評価の実行手順と全記録 <span>↗</span></a>
  <a href="https://learn.microsoft.com/en-us/azure/foundry/concepts/evaluation">Microsoft Learn: Foundry の評価 <span>↗</span></a>
  <a href="https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/evaluate-agent">Microsoft Learn: エージェントを評価する <span>↗</span></a>
</div>
</section>

<nav class="lesson-next" aria-label="章の移動">
  <a href="{{ '/architecture/' | relative_url }}"><small>前の章 / 01</small><span>構成を読む</span></a>
  <a class="next-link" href="{{ '/integration/' | relative_url }}"><small>次の章 / 03</small><span>アプリへ組み込む <b aria-hidden="true">→</b></span></a>
</nav>
