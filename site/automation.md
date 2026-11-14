---
layout: default
title: "08 回し続ける: 改善の自動化"
description: エージェントの最適化、本番トラフィックの採点、アラートと Insights、品質ゲート、版の固定を組み合わせ、改善と再評価を回し続ける仕組みを設計します。
main_class: lesson
permalink: /automation/
---

<div class="lesson-kicker"><span>08</span><span>AUTOMATE</span><span>—</span><span>回し続ける: 改善の自動化</span></div>

# 本番の失敗を評価に加え、<br>改善と再評価を回し続ける

<p class="lesson-lead">第 7 章までの改善は、担当者が評価を実行し、記録を読み、修正していました。この章では、評価と改善案の作成をどこまで自動化できるか、本番の応答をどう採点するかを、Microsoft Foundry の機能で設計します。本番に影響する判断は担当者が行い、前の版に戻せる状態を保ちます。</p>

<aside class="callout callout-caution">
  <span class="callout-label">この章の位置付け</span>
  <p>この章の機能は、このリポジトリでは有効にしていません。多くはプレビューで、エージェントの最適化は限定プレビューです（許可リストへの登録が必要）。手順と提供状況は、2026 年 11 月 14 日に Microsoft Learn で確かめた内容です。試すときは本番とは別の環境を使い、最新の Learn を確認してください。</p>
</aside>

<nav class="lesson-toc" aria-label="この章の内容">
  <a href="#loop">改善のループ</a><a href="#optimize">リリース前の自動改善</a><a href="#production">本番での採点</a><a href="#detect">検知と分析</a><a href="#release">品質ゲートと版の切り替え</a><a href="#split">自動化と人の判断</a><a href="#practice">演習</a>
</nav>

<section class="lesson-section" markdown="1">
<div class="section-marker">01 / LOOP</div>
<div class="section-content" markdown="1">

## 採点と改善案の判定は自動、反映は人が判断する
{: #loop}

改善のループは、本番の採点、問題の検知、評価データの追加、改善案の作成、品質ゲートと切り替えの 5 つの段階に分かれます。このうち採点から改善案の判定までは自動で進め、原因の確認と本番への反映は担当者が判断します。

<figure class="diagram">
  <a href="{{ '/assets/diagrams/improvement-loop.png' | relative_url }}" title="原寸で開く"><img src="{{ '/assets/diagrams/improvement-loop.png' | relative_url }}" alt="改善ループの設計例。本番の Hosted Agent の応答を Microsoft Foundry が定期的に採点し、問題を分類し、本番のトレースから評価データを作り、改善案を作成する。CI の品質ゲートで評価し、担当者が原因の確認と採用を判断してから、新しい版に切り替える"></a>
  <figcaption><span>設計例</span> 本番の採点から改善、承認、切り替えまでの流れ。このリポジトリでは実装していません。</figcaption>
</figure>

<ol class="numbered-steps">
  <li><b>本番の応答を定期的に採点する</b><span>本番のトレースを一定の間隔で抽出し、評価器で採点します（継続評価）。</span></li>
  <li><b>品質の低下や繰り返す問題を見つける</b><span>スコアや遅延の変化をアラートで知らせ、Insights が問題を分類して原因の候補を示します。</span></li>
  <li><b>評価に加えるケースを選ぶ</b><span>担当者が原因を確かめ、加えるケースを選ぶと、本番のトレースから評価データを作れます。</span></li>
  <li><b>改善案を作る</b><span>エージェントの最適化か、Insights の修正案をもとにしたコードの修正で改善案を作ります。</span></li>
  <li><b>品質ゲートを通った版だけを切り替える</b><span>固定データと本番由来のデータの両方で評価し、基準を満たした版を、本番を固定したまま確かめてから承認後に切り替えます。</span></li>
</ol>

本番で見つかった失敗が次の版の評価シナリオになるため、同じ失敗を繰り返しにくくなります。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">02 / OPTIMIZE</div>
<div class="section-content" markdown="1">

## リリース前に、評価と改善案の作成を自動で繰り返す
{: #optimize}

エージェントの最適化（agent optimizer、限定プレビュー）は、評価データで今の版を採点し、改善案（候補）を作って同じデータで採点し直し、最もスコアの高い候補を推薦します。Hosted Agent にも対応していて、Responses プロトコルが必要です。このリポジトリのエージェントは Responses で公開しています。

<div class="cycle" aria-label="エージェントの最適化の流れ">
  <article><span>01</span><h3>今の版を採点</h3><p>評価データと評価器で、現在の版の応答を採点する。</p></article>
  <article><span>02</span><h3>候補を作る</h3><p>最適化用のモデルが、指示文やスキルの書き直し案を作る。</p></article>
  <article><span>03</span><h3>候補を採点</h3><p>同じ評価データで候補を採点し、今の版と比べる。</p></article>
  <article><span>04</span><h3>最良案を推薦</h3><p>0.0 から 1.0 のスコアで順位を付け、最もよい候補に印を付ける。</p></article>
  <article><span>05</span><h3>確認して配置</h3><p>担当者が差分を確かめ、新しい版として配置する。</p></article>
  <div class="cycle-return">本番で見つかった失敗を評価データに加えて、もう一度実行 <span aria-hidden="true">↻</span></div>
</div>

### 最適化の対象

Hosted Agent では、ベースラインの設定にあるファイルによって、最適化する対象が決まります。

| 最適化の対象 | 有効になる条件 | このリポジトリで対応する箇所 |
|---|---|---|
| 指示文 | `instructions.md` がある | `Instructions/agent-instructions.md` |
| スキル | `skills/` に `SKILL.md` がある | `Skills/` の 4 つのスキル |
| ツールの説明 | `tools.json` がある（関数呼び出しのツールだけ） | 5 つの業務ツールの説明 |
| モデル | 評価の設定の `model_search_space` に候補を書く | 推論に使うモデルのデプロイ |

### 最適化に対応させるために変えること

<ul class="rule-list">
  <li><b>設定の読み込み</b><span>起動時に最適化用の設定（<code>.agent_configs/</code>）を読み込み、指示文、スキル、ツールの説明として使います。Learn の手順は Python の <code>load_config()</code> で、.NET には <code>Azure.AI.AgentServer.Core</code> を使うよう案内しています。.NET の API リファレンスには、プレリリースの <code>Azure.AI.AgentServer.Optimization</code>（<code>OptimizationOptionsLoader</code>）も載っています。</span></li>
  <li><b>このリポジトリの現状</b><span>指示文はアセンブリに埋め込み、ツールの説明は C# の <code>[Description]</code> 属性に書いています。固定している <code>Azure.AI.AgentServer.Core</code> 1.0.0-beta.30 には、最適化の設定を読み込む API が見当たりませんでした。</span></li>
  <li><b>2 つのモデル</b><span>採点に使うモデル（<code>--eval-model</code>）と、改善案を作るモデル（<code>--optimize-model</code>）を指定します。Hosted Agent では、改善案を作るモデルの指定が必須です。</span></li>
  <li><b>テスト用の接続先</b><span>実行中は評価データの件数分だけエージェントが呼ばれ、ツールも実際に動きます。外部 API や書き込みを伴うツールは、テスト用の接続先にします。</span></li>
</ul>

最適化は `azd` の AI エージェント拡張で実行します。次は Learn に載っている手順の形で、`<...>` は自分の環境の値に置き換えます。

```powershell
azd ai agent optimize --eval-model <採点用のデプロイ名> --optimize-model <最適化用のデプロイ名>
azd ai agent optimize status <operation-id> --watch
azd ai agent optimize apply --candidate <candidate-id>
azd deploy
```

`apply` は候補の設定を `.agent_configs/<candidate_id>/` に取り込み、`azure.yaml` のサービスの環境変数に `OPTIMIZATION_CANDIDATE_ID` を加えて、指示文の差分を表示します。候補は下書きの版として作られ、配置するまで稼働中の版には影響しません。`azd ai agent optimize deploy` で直接配置する方法もありますが、手元のファイルが変わらないため、本番には `apply` してから `azd deploy` する手順を使います。評価データと評価器を指定する方法（`--eval` や設定ファイル）は、Learn のページによって書き方が異なります。実行する前に、使う拡張の版に合った手順を確かめてください。

トレースからスキルを改善する機能（プレビュー）もあります。Hosted Agent では候補の昇格はできず、公開されたスキルの版を取り込む形になります。

<aside class="callout callout-info">
  <span class="callout-label">結果の読み方</span>
  <p>スコアの差が 0.03 未満なら誤差として扱います。最適化した指示文は長くなりやすく、トークンが増えることがあります。すべての候補が今の版を下回ったら配置しません。最適化のスコアが上がっても、独立した評価で上がるとは限らないため、第 6 章の品質ゲートでも確かめます。</p>
</aside>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">03 / PRODUCTION</div>
<div class="section-content" markdown="1">

## リリース前と同じ基準で、本番の応答も採点する
{: #production}

本番の応答には正解データがありません。正解を必要としない評価器と、自分で定義する評価器（カスタム評価器、プレビュー）を組み合わせると、リリース前と同じ観点で本番の応答も採点できます。第 6 章の 8 指標は、次のように振り分けられます。

| このリポジトリの指標 | 本番での確かめ方 | 使う機能 |
|---|---|---|
| タスクの遵守、意図の解決、ツール呼び出しの精度 | 本番の応答をそのまま採点。正解データは不要 | 継続評価（組み込みの評価器） |
| 出典、日本語 | コードの検査をカスタム評価器として定義し直して採点 | 継続評価（カスタム評価器） |
| ツールの実行、成果物、攻撃への耐性 | 本番の失敗を評価データに加え、リリース前に確かめる | 品質ゲート、レッドチーミング |

Hosted Agent では、本番のトレースを一定の間隔で採点する、スケジュールされた評価として設定します。画面 08 のモニターにある「スケジュールされた評価」が、ポータルでの入口です。本番のトレースを評価するときは、採点するトレースをランダムに選ぶほか、似た依頼を除いてまれな依頼も残す「インテリジェント サンプリング」でも選べます。

### 本番で採点する前に決めること

<ul class="check-list">
  <li><span>入出力の本文の記録</span><div>評価は <code>invoke_agent</code> スパンの <code>gen_ai.input.messages</code> と <code>gen_ai.output.messages</code> を読みます。空の場合、品質の評価器はスコアを返しません。このリポジトリの本番の自作スパンは、本文を記録しない設定です（第 5 章）。</div></li>
  <li><span>記録の範囲と同意</span><div>どの依頼の本文を残すか、利用者への説明、閲覧できる人、保持期間を決めます。</div></li>
  <li><span>頻度と費用</span><div>採点の間隔、サンプル数、判定に使うモデルの費用を決めます。</div></li>
  <li><span>誤判定と承認</span><div>評価器の誤判定を確かめる人と、基準を変えるときの承認者を決めます。</div></li>
</ul>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">04 / DETECT</div>
<div class="section-content" markdown="1">

## 品質の低下を通知し、問題と原因の候補をまとめる
{: #detect}

採点の結果は、通知と分析につなげて初めて改善に使えます。次の機能はいずれも本番のトレースと評価の結果を使います。

| 機能 | できること | 提供状況 |
|---|---|---|
| アラート | 遅延、トークン使用量、評価スコア、レッドチーミングの結果の変化を通知します。モニターの設定から構成します | <span class="status status-preview">プレビュー</span> |
| Insights | 本番のトレースから繰り返す問題を分類し、代表的なトレース、影響する版、原因の候補、次の対応を示します | <span class="status status-preview">プレビュー</span> |
| 修正案 | コードで作った Hosted Agent では、修正案がソースコードの差分で示されることがあります | <span class="status status-preview">プレビュー</span> |
| 評価データ化 | 選んだトレースから評価データを作ります。件数の上限は 1 から 1,000 で指定でき、既定で非公開の内容を伏せます | <span class="status status-preview">プレビュー</span> |
| レッドチーミング | 攻撃的な入力でエージェントを検査します。モニターの定期スキャンはプレビューです | <span class="status status-ga">GA</span> |

Insights は、幻覚（Hallucinations）、ツール呼び出しの失敗（Tool call failures）、遅延（Latency）などの分類で問題をまとめます。GitHub Copilot などのコーディング エージェントは、Foundry MCP Server の `agent_insights_get` で Insights を読み取り、修正を進められます。Hosted Agent で使うには、Foundry Project Manager のロールが必要です。

トレースから評価データを作るときは、`agent_version` で版を指定します。指定しないと、稼働中のすべての版のスパンが混ざり、古い版の動きまで評価データに入ります。

<aside class="callout callout-caution">
  <span class="callout-label">Insights の扱い</span>
  <p>Insights の分類、原因、修正案は判断の材料で、誤りを含むことがあります。根拠のトレースを開いて確かめてから、評価データの追加や修正に回してください。</p>
</aside>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">05 / RELEASE</div>
<div class="section-content" markdown="1">

## 本番を今の版に固定したまま、新しい版を確かめる
{: #release}

既定では、エージェントのエンドポイントは最新の版に要求を送ります。配置してからテストする手順では、テストの前に本番の要求が新しい版へ移ってしまいます。本番を今の版に固定してから新しい版を作り、その版だけを指定して確かめ、承認後に切り替えます。

<ol class="numbered-steps">
  <li><b>本番を固定する</b><span>今の版を記録し、<code>FixedRatio</code> のルール 1 つで要求の 100% をその版に向けます。版の間で要求を分割することはできません。</span></li>
  <li><b>新しい版を作る</b><span>配置で作られた版を記録し、状態が <code>active</code> になるのを待ちます。</span></li>
  <li><b>その版だけを確かめる</b><span>新しい版を指定したセッション（<code>version_indicator</code>）や、<code>x-agent-version-override</code> を付けた要求で確かめます。</span></li>
  <li><b>承認後に切り替える</b><span>確かめた版にルールを向け直します。本番の確認で問題が出たら、前の版のルールに戻します。</span></li>
</ol>

本番に向ける版の設定は `azd` では変えられないため、REST API か SDK を使います。前の版とイメージは、戻す可能性があるあいだ残します。版やエージェントを削除しても、戻したことにはなりません。

### このリポジトリでの版の指定

評価スクリプトは、要求ごとに `x-agent-version-override` で評価する版を指定しています。次は [run-foundry.ps1](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/eval/run-foundry.ps1) の抜粋です。

```powershell
Invoke-Foundry 'post' "agents/$AgentName/endpoint/protocols/openai/responses?api-version=v1" `
    $responseFile (Join-Path $Out "requests\$id.json") @("x-agent-version-override=$AgentVersion") -RetrySessionCreation
```

Runner は、保存した応答の `agent_reference` の名前と版が、指定した値と一致するかを確かめてから採点に使います。Web の BFF にも、呼び出す版を指定する設定 `Agent:VersionOverride` があります（第 4 章）。

### 評価用の azd 拡張で品質ゲートを作る

このリポジトリの品質ゲートは、`run-foundry.ps1` と Runner の終了コードで作っています（第 6 章）。Foundry の評価用 azd 拡張（`azure.ai.evaluations`、プレビュー）を使う場合は、`evals/azure.eval.yaml` に評価を定義し、合格率が基準を下回ったときに CI を失敗させます。

```powershell
azd ai eval run start --fail-on pass-rate=0.8 --no-prompt
```

`--fail-on` を付けないと、不合格のサンプルがあっても終了コードは 0 になります。また、`run start` は評価する版を固定しないため、変更した版を配置する前に、基準になる実行を記録しておきます。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">06 / SPLIT</div>
<div class="section-content" markdown="1">

## 自動化する範囲と、人が判断する範囲を分ける
{: #split}

改善案の作成と判定までは自動化し、本番に影響する判断は担当者が行う設計をお勧めします。

| 段階 | 自動で行うこと | 人が判断すること | 使う機能 |
|---|---|---|---|
| 本番の採点 | トレースの抽出と採点、スコアの集計 | 採点の範囲と頻度、本文の記録 | 継続評価 <span class="status status-preview">プレビュー</span> |
| 検知と分析 | 低下の通知、問題の分類と原因の候補 | 原因の確認と、対応の要否 | アラート、Insights <span class="status status-preview">プレビュー</span> |
| 評価データ | 本番のトレースからのデータの作成 | 追加するケースと、期待する動き | トレースからの作成 <span class="status status-preview">プレビュー</span> |
| 改善案 | 改善案の作成と、同じデータでの採点 | 採用する案の選択 | 最適化 <span class="status status-limited">限定プレビュー</span> |
| リリース | 品質ゲートの判定と、新しい版の作成 | 本番への切り替えと、前の版への戻し | CI の評価、版の固定 |

本番への反映を承認制にし、前の版に戻せる状態を保ったまま、自動化の範囲を少しずつ広げます。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">07 / PRACTICE</div>
<div class="section-content" markdown="1">

## 演習: このリポジトリに当てはめて設計する
{: #practice}

プレビューの機能を有効にしなくても、次の設計はリポジトリのコードと記録だけで進められます。

<ol class="numbered-steps">
  <li><b>本文の記録方針を書く</b><span>本番で本文を残す範囲、閲覧できる人、保持期間、利用者への説明を 1 ページにまとめます。第 5 章の表で、アプリと基盤のどちらに記録が残るかを確かめます。</span></li>
  <li><b>最適化に対応させる変更を洗い出す</b><span><code>AgentFactory.cs</code> の指示文の読み込み、<code>Skills/</code>、<code>CoworkTools.cs</code> の <code>[Description]</code> を読み、設定から読み込む形にするには何を変えるかを書き出します。</span></li>
  <li><b>本番由来の評価ケースを書く</b><span>第 5 章のトレースの失敗を 1 つ選び、<code>core.jsonl</code> と同じ形式で、期待するツールと結果を書きます。</span></li>
  <li><b>版の切り替え手順を書く</b><span>本番の固定、新しい版の確認、切り替え、戻しを、誰がいつ承認するかを含めて手順にします。</span></li>
</ol>

機能を試すときは、本番とは別の Foundry プロジェクトか評価用のエージェントを使います。エージェントの最適化を使うには、サブスクリプションの許可リストへの登録が必要です。

</div>
</section>

<section class="checkpoint" markdown="1">
<p class="eyebrow">CHECKPOINT</p>

## 次章へ進む前に

- 改善のループのうち、自動で進める段階と人が判断する段階を説明できる。
- 最適化に対応させるために、エージェントのコードで変えることを説明できる。
- 本番の応答を採点する前に決めること（本文の記録、頻度、費用、承認者）を挙げられる。
- 本番を今の版に固定したまま、新しい版を確かめて切り替える手順を説明できる。

<div class="source-links">
  <a href="https://learn.microsoft.com/azure/foundry/agents/concepts/agent-optimizer-overview">What is the agent optimizer? <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/agents/how-to/make-agent-optimizer-ready">Make your agent optimizer-ready <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/observability/how-to/cloud-evaluation-deployed-interactions">Evaluate individual interactions from deployed models and agents <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/observability/how-to/agent-insights">Use Insights in Foundry (preview) <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/observability/how-to/traces-to-dataset">Convert agent traces into evaluation datasets <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/agents/how-to/manage-hosted-agent">Manage hosted agents <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/agents/how-to/set-up-ci-cd-cli">Set up CI/CD for hosted agents with the Azure Developer CLI <span>↗</span></a>
</div>
</section>

<nav class="lesson-next" aria-label="章の移動">
  <a href="{{ '/improvement/' | relative_url }}"><small>前の章 / 07</small><span>直す: 失敗ケースから改善する</span></a>
  <a class="next-link" href="{{ '/adoption/' | relative_url }}"><small>次の章 / 09</small><span>導入する: 自社のエージェントに当てはめる <b aria-hidden="true">→</b></span></a>
</nav>
