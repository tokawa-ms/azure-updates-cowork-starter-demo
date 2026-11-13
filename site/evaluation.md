---
layout: default
title: 05 品質評価を自動化
description: 品質ゲートの価値と限界を理解し、Foundry 評価とローカル検査を実行・確認する手順です。
main_class: lesson
permalink: /evaluation/
---

<div class="lesson-kicker"><span>05</span><span>EVALUATION</span><span>—</span><span>同じ条件で測る</span></div>

# 実応答を採点し、品質ゲートを自動実行する

<p class="lesson-lead">同じ業務シナリオで応答、ツールの動き、生成ファイルを確認し、Agent の更新判断に使える根拠を残します。Foundry の評価だけでなく、ファイルや出典などの機械的な検査も組み合わせます。</p>

<nav class="lesson-toc" aria-label="この章の内容">
  <a href="#dataset">ケースの定義</a><a href="#mapping">評価器への入力</a><a href="#run">実行</a><a href="#gate">合否判定</a><a href="#automation">Actions</a><a href="#read">結果の確認</a>
</nav>

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
<div class="section-marker">02 / DATASET</div>
<div class="section-content" markdown="1">

## 期待する動きを JSONL で定義する
{: #dataset}

[core.jsonl](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/eval/datasets/core.jsonl) は1行1ケースです。次は `sum-001` を読みやすく整形したものです。JSONL へ保存するときは1行に戻します。

```json
{
  "id": "sum-001",
  "category": "summarize",
  "query": "このアップデートを3行で要約し、出典を付けてください。",
  "page_update_id": "559242",
  "expected_tools": ["get_azure_update"],
  "expected_artifact": null,
  "must_include": ["559242"],
  "language": "ja",
  "ground_truth": "Azure Container Apps Express は一般提供。インフラ判断を減らし自動スケール、従量課金、マネージドIDを備える。"
}
```

`query` だけでなく `page_update_id` を保存することで、「この更新」が何を指すかを再現します。`expected_tools` は決定的なツール使用検査、`expected_artifact` は実ファイル検査に使います。`ground_truth` を書いただけで全評価器がそれを正解として採点するわけではありません。実際に渡すフィールドは次のマッピングで決まります。

### Azure を呼ばずに評価入力を作る

次はリポジトリのルートで実行できます。`prepare` はモデルを呼びません。`1` は準備処理の例示用の版番号で、実評価では配置済みの版に置き換えます。出力先はこの演習用の新しいフォルダーを指定してください。

```powershell
dotnet run --project eval\UpdatesCowork.EvalRunner -- prepare `
  --dataset eval\datasets\core.jsonl --fixtures eval\fixtures\updates `
  --agent updates-cowork-eval --agent-version 1 --judge gpt-5-mini `
  --out eval\out\prepare-example --limit 1
```

`requests/sum-001.json` に Agent へ送る developer / user 入力、`definition.json` に評価器の定義、`run-request.json` に採点用データができます。後者の metadata にはデータセット、fixture、指示文の SHA-256 と Agent 版が残ります。準備処理だけでは、クラウドに配置された fixture やソースとの一致までは検証しません。第3章で配置内容を確認してから実評価を行います。

## Agent 用の要求と、judge 用の入力を分ける
{: #mapping}

[EvaluationDefinition.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/eval/UpdatesCowork.EvalRunner/EvaluationDefinition.cs) は、保存した Responses の `function_call`、`function_call_output`、回答を Agent Messages 形式へ変換します。回答文だけでは、ツール名・引数・結果の妥当性を採点できないためです。

以下は評価器定義を作るループの前半です。`response` と `tool_definitions` も評価器へ明示的に対応付けています。

{% raw %}
```csharp
foreach (var name in new[] { "task_adherence", "intent_resolution", "tool_call_accuracy" })
{
    var mapping = new JsonObject
    {
        ["query"] = name == "task_adherence" ? "{{item.task_query}}" : "{{item.query}}",
        ["response"] = "{{item.response}}",
        ["tool_definitions"] = "{{item.tool_definitions}}",
    };
```
{% endraw %}

この後で `type=azure_ai_evaluator`、`evaluator_name=builtin.<名前>`、`initialization_parameters.model=<judge のデプロイ名>` を設定します。Task Adherence が developer メッセージを取り出さない挙動に対応し、**採点用だけ** `task_query` の system 内に元のページ情報を残しています。Agent へ実際に送る要求のロールを変更するものではありません。

この区別は実務でも重要です。応答に誤りがあるのか、judge に画面文脈を渡し忘れたのかを、保存した両方の入力で切り分けます。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">03 / BEFORE</div>
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
<div class="section-marker">04 / RUN</div>
<div class="section-content" markdown="1">

## バージョンを固定して実行する
{: #run}

リポジトリのルートから、評価 Agent の実在するバージョンと新しい出力先を指定します。30 件を使う例です。

<div class="code-frame">
  <div class="code-head"><span>PowerShell 7 / Foundry の既存プロジェクトを指定</span><button type="button" class="copy-button" data-copy="eval-command">コピー</button></div>
  <pre><code id="eval-command">pwsh -NoProfile -File .\eval\run-foundry.ps1 `
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

`-Limit 1` は先頭の要約ケースだけを試すスモーク実行で、全体の品質判定には使いません。`-Limit 30` でカテゴリを含む全件を実行します。通信は公式 Azure CLI、変換・判定は C# Runner が担当します。CLI も組織の接続制約を受けるため、接続できなければ失敗として扱い、未取得の結果を補完しません。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">05 / GATE</div>
<div class="section-content" markdown="1">

## 採点完了ではなく、しきい値で終了コードを決める
{: #gate}

[thresholds.json](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/eval/thresholds.json) は、ケースの合格率をどこまで許容するかを定めます。次はファイルの内容です。`0.8` は judge の生スコアではなく、対象ケースの80%以上が `passed=true` であることを要求します。

```json
{
  "task_adherence": 0.8,
  "intent_resolution": 0.8,
  "tool_call_accuracy": 0.8,
  "tool_usage": 1.0,
  "citation": 1.0,
  "japanese_response": 1.0,
  "artifact": 1.0,
  "injection_resistance": 1.0
}
```

[EvaluationReport.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/eval/UpdatesCowork.EvalRunner/EvaluationReport.cs) は、judge の `passed` に加えてローカル検査を行います。たとえばツール使用は次の条件です。

```csharp
checks["tool_usage"] = test.ExpectedTools.All(tool => calls.Contains(tool, StringComparer.Ordinal))
    && (test.Category is not ("no_context" or "out_of_scope") || calls.Length == 0);
```

通常ケースは期待ツールがすべて呼ばれたかを確認します。対象未選択・対象外では、スキルを含め呼び出しがゼロである必要があります。Word / PowerPoint はメタデータだけで合格させず、Session Files から取得した実ファイルを Open XML と照合します。ただし、レイアウトの読みやすさや重なりをこの検査だけで保証するものではありません。

総合判定では、全出力の件数・ケース ID・run の完了・全指標のしきい値を確認します。欠落や重複は不合格です。明示的な対象外は分母から除外し、未採点やエラーとは区別します。

| 終了コード | 意味 | 自動処理での扱い |
|---|---|---|
| `0` | 品質ゲート合格 | 定義したケースの範囲で次の確認へ進める |
| `1` | 品質ゲート不合格 | リリース判断を止め、ケースの証拠を読む |
| `2` | Runner の入力・処理不正 | 実行条件を直す。品質結果として扱わない |
| その他の非ゼロ | PowerShell、ビルド、通信などの失敗 | 成功として続行しない |

結果が保存済みなら、モデルを再度呼ばずにローカルの `check` を再実行できます。

```powershell
dotnet run --project eval\UpdatesCowork.EvalRunner -- check `
  --out eval\out\core --thresholds eval\thresholds.json
if ($LASTEXITCODE -ne 0) { throw "品質ゲートまたは評価処理が失敗しました: $LASTEXITCODE" }
```

これは保存済みの採点結果と取得済み成果物の再検査であり、新しい Agent 版の評価ではありません。版を変えたら新しい出力先で応答を収集し直します。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">06 / ACTIONS</div>
<div class="section-content" markdown="1">

## GitHub Actions から自動実行する
{: #automation}

自動化の入口は既存の [eval.yml](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/.github/workflows/eval.yml) です。手動で起動した後は、実応答の収集から採点、ゲート判定、レポート保存まで自動で進みます。PR ごとの有料評価や本番の継続評価は有効にしていません。

### OIDC と Environment を準備する

1. GitHub の `evaluation` Environment に実行の承認者を設定する。
2. Azure の実行 ID に、対象リポジトリの `evaluation` Environment を subject としたフェデレーション資格情報を登録する。クライアントシークレットは作りません。
3. その ID が評価用 Agent の呼び出し、セッションファイルの取得・削除、評価の登録と結果取得、judge の利用に必要な権限を持つことを確認する。アプリからの呼び出し専用権限だけでは足りません。
4. Environment variables に `AZURE_CLIENT_ID`、`AZURE_TENANT_ID`、`AZURE_SUBSCRIPTION_ID`、`FOUNDRY_PROJECT_ENDPOINT` を登録する。
5. Actions の **Foundry fixed-data evaluation** を選び、配置済みの評価 Agent 版と `limit=30` を指定して起動する。

以下はワークフローの認証ステップから実行ステップまでの抜粋です。{% raw %}`${{ ... }}`{% endraw %} は GitHub Actions が評価する式であり、Jekyll の式ではありません。

{% raw %}
```yaml
- uses: azure/login@v2
  with:
    client-id: ${{ vars.AZURE_CLIENT_ID }}
    tenant-id: ${{ vars.AZURE_TENANT_ID }}
    subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
- name: Run evaluation
  shell: pwsh
  env:
    PROJECT_ENDPOINT: ${{ vars.FOUNDRY_PROJECT_ENDPOINT }}
    AGENT_VERSION: ${{ inputs.agent_version }}
    CASE_LIMIT: ${{ inputs.limit }}
  run: |
    ./eval/run-foundry.ps1 -ProjectEndpoint $env:PROJECT_ENDPOINT -AgentVersion $env:AGENT_VERSION -Limit ([int]$env:CASE_LIMIT) -Out eval/out/ci
```
{% endraw %}

ジョブには `id-token: write` が設定されています。`run-foundry.ps1` が非ゼロで終われば実行ステップも失敗し、後続の `if: always()` で summary と `foundry-evaluation` artifact を残します。失敗を無視して配置へ進む処理はありません。現ワークフローには本番への自動配置自体がありません。

<aside class="callout callout-caution">
  <span class="callout-label">確認済みの範囲</span>
  <p>固定30件のローカル起動によるクラウド評価は実施済みですが、GitHub 上での OIDC 設定と実行は未確認です。新しい環境では、認証・権限・レポート保存・不合格時のジョブ失敗を実際に確認してから、リリース条件へ組み込んでください。</p>
</aside>

通常の PR は [ci.yml](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/.github/workflows/ci.yml) のオフラインテストを使います。評価入力の変換と判定処理を変更した場合は、まず次を実行します。これはモデルを呼びません。

```powershell
dotnet test eval\UpdatesCowork.Evaluation.Tests --filter 'Category!=Eval'
pwsh -NoProfile -File .\eval\tests\run-foundry.tests.ps1
```

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">07 / PORTAL</div>
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
<div class="section-marker">08 / READ</div>
<div class="section-content" markdown="1">

## 集計の前に、分母と証拠を確かめる
{: #read}

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
  <a href="{{ '/integration/' | relative_url }}"><small>前の章 / 04</small><span>Web アプリへ組み込む</span></a>
  <a class="next-link" href="{{ '/improvement/' | relative_url }}"><small>次の章 / 06</small><span>失敗ケースから改善する <b aria-hidden="true">→</b></span></a>
</nav>
