---
layout: default
title: "08 回し続ける: 継続的改善のループ"
description: Microsoft Foundry の継続的改善の 4 つのステップ（確立、特定、最適化、検証して繰り返す）に沿って、利用者の評価の記録、低い評価のアラート、版の統計的な比較、品質ゲート、版の固定を組み合わせ、改善と再評価を回し続ける仕組みを作ります。
main_class: lesson
permalink: /automation/
---

<div class="lesson-kicker"><span>08</span><span>CONTINUOUS IMPROVEMENT</span><span>—</span><span>回し続ける: 継続的改善のループ</span></div>

# 本番の声を評価につなぎ、<br>確立、特定、最適化、検証を繰り返す

<p class="lesson-lead">第 7 章では、評価の不合格を手がかりに、担当者が原因を調べて直しました。リリースした後も品質を上げ続けるには、本番で起きた失敗を評価に取り込み、直した版が本当によくなったかを確かめ、その版を次の基準にする流れが必要です。Microsoft は Ignite 2026 で、この流れを「確立」「特定」「最適化」「検証して繰り返す」の 4 つのステップとして示し、Microsoft Foundry の機能をそろえました。この章では、4 つのステップごとに、このリポジトリで実装した部分と、まだ使っていない Foundry の機能を、実コードと実行記録で確かめます。</p>

<aside class="callout callout-caution">
  <span class="callout-label">この章の位置付け</span>
  <p>利用者の評価をトレースに付ける処理、低い評価の割合のアラート、版の比較（<code>compare</code>）、CI での基準との比較は、このリポジトリに実装しています。Insights、エージェントの最適化、継続評価などの Foundry の機能は有効にしていません。多くはプレビューで、エージェントの最適化は限定プレビューです（許可リストへの登録が必要）。提供状況は、2026 年 11 月 19 日に Microsoft の公式ブログと Microsoft Learn で確かめた内容です。試すときは本番とは別の環境を使い、最新の情報を確認してください。</p>
</aside>

<nav class="lesson-toc" aria-label="この章の内容">
  <a href="#loop">4 つのステップ</a><a href="#status">機能と対応</a><a href="#establish">確立</a><a href="#identify">特定</a><a href="#optimize">最適化</a><a href="#validate">検証して繰り返す</a><a href="#production">本番での採点</a><a href="#split">自動化と人の判断</a><a href="#practice">演習</a>
</nav>

<section class="lesson-section" markdown="1">
<div class="section-marker">01 / LOOP</div>
<div class="section-content" markdown="1">

## 4 つのステップを、前の周の結果を土台に繰り返す
{: #loop}

Microsoft Foundry の継続的改善は、次の 4 つのステップを繰り返す進め方です。1 周ごとに、評価データと比較の基準が、前の周の結果で更新されます。

<div class="principle-grid principle-grid-4">
  <article><span>STEP 1 / ESTABLISH</span><h3>確立する</h3><p>よい応答の基準を決め、評価と評価データにする。専門家や利用者の判断をトレースに残し、評価データに取り込む。</p><p class="stage-result">このリポジトリ: 固定 30 件と 8 指標、利用者の評価の注釈</p></article>
  <article><span>STEP 2 / IDENTIFY</span><h3>特定する</h3><p>本番のトレースから、繰り返し起きている問題と、その原因の候補を見つける。</p><p class="stage-result">このリポジトリ: トレース、KQL、3 つのアラート</p></article>
  <article><span>STEP 3 / OPTIMIZE</span><h3>最適化する</h3><p>指示文、スキル、ツール、モデルを直す案を作り、同じ評価で比べる。反映するかは人が決める。</p><p class="stage-result">このリポジトリ: 原因の層を手で直す（第 7 章）</p></article>
  <article><span>STEP 4 / VALIDATE</span><h3>検証して繰り返す</h3><p>差が偶然の範囲かを統計的に確かめ、品質ゲートを通った版を出す。出した版が次の基準になる。</p><p class="stage-result">このリポジトリ: compare、CI の基準比較、版の固定</p></article>
</div>

公式ブログは、4 つ目のステップを「出した版が新しい基準になる」と説明しています。改善は 1 回の修正で終わりません。次の周では、その版を基準にして本番の失敗を集め直し、評価データに加え、また比べます。

第 5 章から第 7 章で扱った内容も、この 4 つのステップに当てはまります。評価の基準とデータ（第 6 章）が確立、トレースと KQL（第 5 章）が特定、原因の層を直す手順（第 7 章）が最適化、同じ条件での再評価と版の固定が検証に当たります。この章では、それらをつなぐために足りなかった部分を補います。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">02 / STATUS</div>
<div class="section-content" markdown="1">

## Foundry の機能と、このリポジトリの対応を確かめる
{: #status}

4 つのステップで使う Foundry の機能と提供状況、このリポジトリの対応です。「代わりの実装」は、Foundry の機能を使わずに、同じ目的をリポジトリのコードや手順で満たしている項目です。

| ステップ | Foundry の機能 | 提供状況 | このリポジトリ |
|---|---|---|---|
| 確立 | 評価（組み込みの評価器） | <span class="status status-ga">GA</span> 一部の評価器はプレビュー | 使用。固定 30 件を 8 指標で採点（第 6 章） |
| 確立 | ルーブリック評価器 | <span class="status status-preview">プレビュー</span> | 未使用 |
| 確立 | トレースの注釈（開発者、利用者） | <span class="status status-preview">プレビュー</span> | 実装。利用者の評価を注釈と同じ形で記録（[確立](#establish)） |
| 確立 | 注釈からの評価データ、ゴールデン データセット | <span class="status status-preview">プレビュー</span> | 代わりの手順。低い評価から <code>core.jsonl</code> に足し、SHA-256 で版を固定 |
| 特定 | トレース | <span class="status status-ga">GA</span> | 使用（第 5 章） |
| 特定 | Insights | <span class="status status-ga">GA</span> | 未使用 |
| 特定 | モニターのアラート | <span class="status status-preview">プレビュー</span> | 代わりの実装。Azure Monitor のログ検索アラート 3 つ（[特定](#identify)） |
| 最適化 | エージェントの最適化 | <span class="status status-limited">限定プレビュー</span> | 未使用。原因の層を手で直す（第 7 章） |
| 最適化 | Insights からの最適化、スキルの最適化 | <span class="status status-preview">プレビュー</span> | 未使用 |
| 検証 | GitHub Actions、Azure DevOps の評価（統計的な比較を含む） | <span class="status status-preview">プレビュー</span> | 代わりの実装。<code>compare</code> と CI の基準比較（[検証](#validate)） |
| 検証 | 版の固定と切り替え（Hosted Agent） | <span class="status status-ga">GA</span> | 使用。評価では要求ごとに版を指定 |
| 検証 | 継続評価、スケジュールされた評価、定期的なレッドチーミング | <span class="status status-preview">プレビュー</span> | 未使用（[本番での採点](#production)） |

<p class="inline-note">提供状況は、Azure Blog の「Continuous improvement comes to Microsoft Foundry」（2026 年 11 月 17 日）、Microsoft Foundry Blog の「What's new in Foundry Agent Service at Ignite 2026」、Learn の「Microsoft Foundry portal general availability overview」と各機能のページで確かめました。Insights は、両方のブログで GA と案内されています。</p>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">03 / ESTABLISH</div>
<div class="section-content" markdown="1">

## 確立: 利用者の評価を、評価した応答のトレースに付ける
{: #establish}

Foundry のトレースの注釈（プレビュー）は、トレースに人の判断を残す機能です。開発者がポータルで付ける注釈（出どころ <code>builder</code>）と、アプリが利用者の評価を送る注釈（出どころ <code>end_user</code>）があり、どちらも追記され、前の注釈を上書きしません。Learn は、利用者の評価を、評価した応答と同じトレースの中で記録するよう求めています。

このリポジトリのチャットには、「役に立った」「役に立たなかった」のボタンとコメント欄があります。評価は応答とは別の HTTP 要求（<code>POST /api/feedback</code>）で届くため、そのまま記録すると、評価の要求の新しいトレースに入ってしまいます。そこで Web は、次の順で記録します。

<ol class="numbered-steps">
  <li><b>応答のトレースを残す</b><span><code>chat.turn</code> が終わるたびに、応答 ID と、そのスパンのトレース ID とスパン ID を、スレッドの台帳に残します（直近 20 件）。</span></li>
  <li><b>元のトレースの中で記録する</b><span>評価が届いたら、そのスパンを親にした <code>feedback.record</code> を開き、従来のログ <code>auc.feedback</code> と、注釈の形のログ <code>gen_ai.evaluation.result</code> を出します。</span></li>
  <li><b>見つからなければ注釈を出さない</b><span>古い応答やプロセスの再起動後で台帳に見つからないときは、<code>auc.feedback</code> だけを記録します。別のトレースに注釈を付けると、Foundry では別の応答の評価に見えるためです。</span></li>
</ol>

次は [FeedbackAnnotation.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/Telemetry/FeedbackAnnotation.cs) の抜粋です。残しておいたトレース ID とスパン ID から親を作り、<code>feedback.record</code> を開きます。

```csharp
var parent = new ActivityContext(
    ActivityTraceId.CreateFromString(trace.TraceId),
    ActivitySpanId.CreateFromString(trace.SpanId),
    trace.Recorded ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None,
    isRemote: true);

// 計測が無効でもログの相関だけは取れるように、発行元が無視したときは手で Activity を作る。
var activity = WebTelemetry.Source.StartActivity(TelemetryNames.Spans.FeedbackRecord, ActivityKind.Internal, parent)
    ?? new Activity(TelemetryNames.Spans.FeedbackRecord).SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags).Start();
```

呼び出し側の [FeedbackEndpoints.cs](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/src/UpdatesCowork.Web/Endpoints/FeedbackEndpoints.cs) では、台帳に応答のトレースがあるときだけ、注釈を出し、元のトレースの中で記録します。

```csharp
void Record()
{
    logger.Log(LogLevel.Information, new EventId(1001, "Feedback"), state, null, static (_, _) => TelemetryNames.FeedbackLogMessage);
    if (trace is not null)
    {
        FeedbackAnnotation.Log(logger, request.Rating!, comment, request.ResponseId, thread.ConversationId);
    }
}

if (trace is not null) FeedbackAnnotation.RecordInTrace(trace, Record);
else Record();
```

注釈のログには、Learn の「Log end user feedback」と同じ属性を付けます。値は、次のローカルの確認で「役に立たなかった」を送ったときのものです。

| 属性 | 値 | 意味 |
|---|---|---|
| `microsoft.custom_event.name` | `gen_ai.evaluation.result` | Azure Monitor のエクスポーターが、このログを Application Insights の `customEvents` に送る目印 |
| `gen_ai.evaluation.name` | `task_completion` | 評価の名前。Foundry の既定の採点テンプレートに合わせた |
| `gen_ai.evaluation.score.value` / `gen_ai.evaluation.score.label` | `0` / `fail` | 「役に立った」は `1.0` / `pass` |
| `gen_ai.response.id` | `resp_fake_word_0001` | 評価した応答 |
| `microsoft.gen_ai.human_evaluation.source` | `end_user` | 利用者による評価 |
| `microsoft.gen_ai.evaluation.actor.type` | `human` | 人による評価 |
| `gen_ai.evaluation.explanation` | `出典のURLがない` | 整えたコメント（ある場合だけ） |
| `internal_properties` | しきい値 1.0、範囲 0.0 から 1.0、増える向きが望ましい、種類 `boolean` | 採点の範囲と向き（JSON 文字列） |

`Web:Features:FeedbackAnnotations` を `false` にすると、注釈と元のトレースへの結び付けを止め、従来の `auc.feedback` だけに戻ります。利用者キーと Cookie の値は、注釈に入れません。

### ローカルで確かめる

2026 年 11 月 19 日に、偽の Agent と Aspire Dashboard で確かめた手順と結果です。

```powershell
docker compose -f tools/otel/compose.yaml up -d
dotnet run --project src/UpdatesCowork.Web --launch-profile fake-agent
```

チャットを 1 回送り、その応答に「役に立たなかった」とコメント「出典のURLがない」を送りました。次は、チャットの SSE の `meta` イベントで受け取ったトレース ID です。

```text
event: meta
data: {"threadId":"th_01M7SMWGBD8T29K5FCZH9YY9J5","responseId":"resp_fake_word_0001","traceId":"1dfad41fb91aab6af968a56ad29280fa",...}
```

評価を送った後、Dashboard でこのトレース ID を開くと、`POST /api/chat` のトレースの最後に `feedback.record` があり、`auc.feedback` と `gen_ai.evaluation.result` の 2 件のログが付いていました。コンソールのログでは、2 件とも `ParentId` が `chat.turn` のスパン ID `6a1f8decd0db8b38` でした。ローカルの既定（前の応答 ID で会話をつなぐ方式）では会話 ID がないため、`gen_ai.conversation.id` は入りません。

<aside class="callout callout-caution">
  <span class="callout-label">Azure で確かめていないこと</span>
  <p>Application Insights の <code>customEvents</code> に届くか、Foundry ポータルのトレースに注釈として表示されるかは、まだ確かめていません（設計書の Q16）。Learn の属性の一覧にあるエージェントの名前と版（<code>gen_ai.agent.*</code>）も、このリポジトリの注釈には入れていません。表示されないときは、元のトレースとスパンの ID、送り先の Application Insights が同じか、Entra ID の認証で送る ID に Monitoring Metrics Publisher のロールがあるかを確かめます。</p>
</aside>

コメントは利用者が書いた文章です。このリポジトリは制御文字を除いて 500 文字までに整えますが、個人情報を取り除く処理はありません。Azure Monitor のドキュメントも、評価の説明に個人情報を入れないよう求めています。本番では、画面での案内や、記録の前に伏せる処理を検討してください。

### 低い評価を、評価データに足す

Foundry では、注釈を付けたトレースから評価データを作り（プレビュー）、テスト ケースごとにルーブリックを生成できます。チームが合意した評価データには `golden` タグを付けてゴールデン データセット（プレビュー）とし、新しい版が上回るべき基準にします。Learn は、このタグは判断の記録で、中身を検証するものではないこと、データセットの版を記録し、版どうしは同じデータセットの版で比べることを求めています。

このリポジトリでは、これらの機能を使わず、次の手順で `eval/datasets/core.jsonl` に足します（設計書 9.2 節）。`core.jsonl` は Git で管理し、評価の実行記録に SHA-256 を残しているため、ゴールデン データセットの版の記録と同じ役割を果たします。

<ol class="numbered-steps">
  <li><b>候補を集める</b><span>次の KQL で、低い評価の注釈と応答 ID を集め、<code>operation_Id</code> でトレースを開いて原因（ツールの選び方、出典、指示違反など）を確かめます。</span></li>
  <li><b>依頼を書き直す</b><span>利用者の文章や個人情報をそのまま使わず、同じ失敗を再現する依頼に書き直して、区分と <code>ground_truth</code> を決めます。必要なら固定データも足します。</span></li>
  <li><b>再現を確かめてから足す</b><span>基準の版で評価し、失敗が再現することを確かめてから足します。データセットの SHA-256 が変わるため、以後の比較は新しいデータセットで基準を取り直します。</span></li>
</ol>

```text
customEvents
| where timestamp > ago(7d)
| where name == "gen_ai.evaluation.result"
| where tostring(customDimensions["gen_ai.evaluation.score.label"]) == "fail"
| project timestamp, operation_Id, responseId = tostring(customDimensions["gen_ai.response.id"]), source = tostring(customDimensions["microsoft.gen_ai.human_evaluation.source"]), comment = tostring(customDimensions["gen_ai.evaluation.explanation"])
| order by timestamp desc
```

この KQL は、Azure での到着を確かめていません（Q16）。注釈が届かないときは、`traces` の `auc.feedback`（`customDimensions["auc.feedback.rating"] == "bad"`）で同じ候補を集められます。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">04 / IDENTIFY</div>
<div class="section-content" markdown="1">

## 特定: 低い評価の増加を知らせ、繰り返す問題をまとめる
{: #identify}

本番の問題は、1 件ずつトレースを読むだけでは傾向が分かりません。このリポジトリでは、Azure Monitor のログ検索アラートを Bicep で 3 つ作ります。通知先のメール アドレスをパラメーター（`alertEmail`）で指定したときだけ作ります。

| アラート | 条件 | 評価の間隔 | 重大度 |
|---|---|---|---|
| ツール呼び出しの失敗率 | 15 分間で `execute_tool` が 5 件以上あり、失敗率が 20% を超えた | 5 分ごと | 2 |
| 応答時間 | 15 分間で `chat.turn` の 95 パーセンタイルが 120 秒を超えた | 5 分ごと | 3 |
| 低い評価の割合 | 24 時間で利用者の評価が 10 件以上あり、「役に立たなかった」が 30% を超えた | 1 時間ごと | 3 |

次は、低い評価の割合のアラートのクエリです（[alerts.bicep](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/infra/modules/alerts.bicep)）。注釈ではなく従来の `auc.feedback` を数えるため、注釈を止めても動きます。

```text
traces
| where message has "auc.feedback"
| extend rating = tostring(customDimensions["auc.feedback.rating"])
| summarize total = count(), bad = countif(rating == "bad")
| extend badRate = iff(total == 0, 0.0, todouble(bad) / total)
| where total >= 10 and badRate > 0.3
```

件数の下限（10 件）は、評価が少ない時間帯に 1、2 件の低い評価で通知が出ないようにするためです。通知が来たら、前の節の KQL で応答 ID を集め、評価データに足す候補を探します。評価スコアの低下を知らせるアラートは、Foundry のモニターの設定（プレビュー）で構成できます。

### Insights で、繰り返す問題と原因の候補をまとめる

Insights（GA）は、本番のトレースから、繰り返し起きている失敗、幻覚、トークンの急増などをまとめ、代表的なトレース、影響する版、原因の候補、次の対応を示します。コードで作った Hosted Agent では、修正案がソース コードの差分で示されることがあります。GitHub Copilot などのコーディング エージェントからは、Foundry MCP Server の `agent_insights_get` で読み取れます。読み取りだけで、スキャンの開始、状態の変更、エージェントの変更はしません。

このリポジトリでは Insights を有効にしていません。使うときの前提は次のとおりです。

<ul class="check-list">
  <li><span>判定に使うモデル</span><div>GPT-5 以降の GPT モデルのデプロイが必要です。Insights の対応リージョンには japaneast と japanwest が含まれます。</div></li>
  <li><span>権限</span><div>Hosted Agent では、操作する人に Foundry Project Manager のロールが必要です。トレースを読む ID には Monitoring Reader が必要で、本文を保護している場合は Privileged Monitoring Data Reader が必要です。</div></li>
  <li><span>トレースの中身</span><div>Insights はトレースに残っている証拠だけを分析します。このリポジトリの本番は入出力の本文を記録しないため（第 5 章）、応答の内容に関する問題は見つけにくくなります。</div></li>
  <li><span>版の識別</span><div>エージェントと版の識別子が、テレメトリの中で安定している必要があります。</div></li>
</ul>

<aside class="callout callout-caution">
  <span class="callout-label">Insights の扱い</span>
  <p>Insights の分類、原因、修正案は判断の材料で、誤りを含むことがあります。根拠のトレースを開いて確かめてから、評価データの追加や修正に回してください。原因がツールの接続先やモデルのエンドポイントなど、エージェントの外にあるときは、指示文を直さず、その担当に回します。Insights を解決済みにする前に、新しい本番の記録か評価で確かめます。</p>
</aside>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">05 / OPTIMIZE</div>
<div class="section-content" markdown="1">

## 最適化: 改善案を作り、同じ評価で比べる
{: #optimize}

このリポジトリでは、第 7 章のとおり、担当者が不合格の記録から原因の層を特定して直しています。Foundry のエージェントの最適化（agent optimizer、限定プレビュー）を使うと、改善案の作成と採点を自動で繰り返せます。評価データで今の版を採点し、改善案（候補）を作って同じデータで採点し直し、最もスコアの高い候補を推薦します。公式ブログは、どの変更を出すかはチームが決め、適用するまで本番は変わらないと説明しています。Hosted Agent にも対応していて、Responses プロトコルが必要です。このリポジトリのエージェントは Responses で公開しています。

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

Insights で確かめた問題を選んで最適化に送るポータルの手順（プレビュー）は、Microsoft Copilot のハーネスを使わないプロンプト エージェントだけが対象です。Hosted Agent では、Foundry Toolkit か `azd` の手順を使います。トレースからスキルを改善する機能（プレビュー）もありますが、Hosted Agent では候補の昇格はできず、公開されたスキルの版を取り込む形になります。

<aside class="callout callout-info">
  <span class="callout-label">結果の読み方</span>
  <p>Learn は、スコアの差が 0.03 未満なら誤差、0.03 から 0.10 は中程度、0.10 から 0.20 は大きな改善と説明しています。最適化した指示文は長くなりやすく、トークンが増えることがあります。すべての候補が今の版を下回ったら配置しません。最適化のスコアが上がっても、独立した評価で上がるとは限らないため、次の節の比較と第 6 章の品質ゲートでも確かめます。</p>
</aside>

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">06 / VALIDATE</div>
<div class="section-content" markdown="1">

## 検証して繰り返す: 差が偶然の範囲かを確かめ、出した版を次の基準にする
{: #validate}

合格率が上がっても、その差が本当の改善か、評価の揺れかは、合格率だけでは分かりません。30 件の評価では 1 件の違いが 3.3 ポイントに当たり、judge の揺れでも数件が入れ替わります。公式ブログは、比較に統計的な検定を付け、差が本物か揺れかを示すと説明しています。Foundry ポータルの評価結果の比較は t 検定で、基準にした実行との差を、有意かどうかで色分けします。GitHub Actions と Azure DevOps の評価（プレビュー）は、信頼区間と、版どうしの統計的な比較を出します。

このリポジトリの品質ゲートは、保存した応答をクラウドの評価器とコードの検査で採点し、ケースごとの合否で判定しています（第 6 章）。同じケースを 2 つの版で採点した合否を比べるため、対になった 2 値のデータに使う正確 McNemar 検定で比べる `compare` コマンドを、評価の Runner に加えました。ネットワークを使わず、2 つの実行フォルダーの `summary.json` と `definition.json` だけを読みます。

```powershell
dotnet run --project eval\UpdatesCowork.EvalRunner -- compare `
  --baseline eval\out\core-v7 --candidate eval\out\core-v12 --out $env:TEMP\auc-compare-core-v7-core-v12
```

<ul class="rule-list">
  <li><b>比べられる条件</b><span>データセットと固定データの SHA-256、judge、応答の出どころ、採点入力の形式、ケース ID の集合がすべて一致しないと「比べられない」とし、終了コード 2 を返します。</span></li>
  <li><b>対にして数える</b><span>両方の版で対象になったケースだけを対にし、不合格から合格を改善、合格から不合格を悪化として数えます。</span></li>
  <li><b>検定と区間</b><span>改善と悪化の件数から、正確 McNemar 検定（両側、有意水準 0.05）の p 値を出します。合格率には Wilson の 95% 区間を付けます。</span></li>
  <li><b>終了コード</b><span>有意な悪化が 1 つでもあれば 1、それ以外は 0 です。結果は <code>--out</code>（省略時は候補の実行フォルダー）の <code>comparison.json</code> と <code>comparison.md</code> に書きます。</span></li>
</ul>

### 例: v7 と v12 を比べる

2026 年 11 月 19 日に、ローカルの実行記録で実行した結果です。終了コードは 0 でした。

```text
前提: データセット、固定データ、judge、採点入力の形式が一致しています。

| 指標 | 基準版 | 候補版 | 差 | 改善 / 悪化 | p 値 | 判定 |
|---|---:|---:|---:|---:|---:|---|
| task_adherence | 24/30 (80%、95%区間 63%〜90%) | 29/30 (97%、95%区間 83%〜99%) | +17 pt | 6 / 1 | 0.125 | 偶然の範囲 |
| intent_resolution | 28/30 (93%、95%区間 79%〜98%) | 29/30 (97%、95%区間 83%〜99%) | +3 pt | 1 / 0 | 1.000 | 偶然の範囲 |
| tool_call_accuracy | 26/27 (96%、95%区間 82%〜99%) | 26/26 (100%、95%区間 87%〜100%) | +4 pt | 0 / 0 | 1.000 | 変化なし |
| tool_usage | 29/30 (97%、95%区間 83%〜99%) | 30/30 (100%、95%区間 89%〜100%) | +3 pt | 1 / 0 | 1.000 | 偶然の範囲 |
| japanese_response | 28/30 (93%、95%区間 79%〜98%) | 30/30 (100%、95%区間 89%〜100%) | +7 pt | 2 / 0 | 0.500 | 偶然の範囲 |

- task_adherence: 改善 no-context-002, pptx-002, sum-002, sum-006, word-001, word-003、悪化 related-002
```

`citation`、`artifact`、`injection_resistance` の 3 行は、両方とも 30/30 で「変化なし」のため省いています。指示遵守（`task_adherence`）は 17 ポイント上がりましたが、改善 6 件と悪化 1 件では p 値が 0.125 で、30 件の評価 1 回だけでは偶然の範囲を出ません。v12 が品質ゲートを通ったこと（基準 80% に対して 29/30）と、v7 より確かによくなったことは、別の主張です。後者を示すには、ケースを増やすか、同じ版を繰り返し評価して揺れの幅を測ります。`tool_call_accuracy` の 4 ポイントの差は、v7 で不合格だった 1 件が v12 で対象外になったことによるもので、対になるケースの入れ替わりはありません。

### 条件の違う組は比べない

第 7 章の v6 と v12 の組は、終了コード 2 になりました。

```text
前提: 一致していない条件があります。この比較で版の良し悪しを判断しないでください。
- task_query_format が異なります (基準版: なし、候補版: system_with_developer_context_v1)。

| task_adherence | 20/30 (67%、95%区間 49%〜81%) | 29/30 (97%、95%区間 83%〜99%) | +30 pt | 9 / 0 | 0.004 | 改善 |
```

検定だけを見ると有意な改善ですが、v7 以降は指示遵守の採点入力を、指示文とページ情報をまとめた system メッセージに変えています。改善 9 件には採点方法の変更の影響が含まれるため、版の改善とは言えません。データセットと judge が同じでも、比べられない組があります。

### CI で、前回の評価と比べる

評価のワークフロー [eval.yml](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/.github/workflows/eval.yml) に、入力 `baseline_run_id` を加えました。以前の評価ワークフローの実行 ID を入れると、その実行の成果物 `foundry-evaluation` を取得して `compare` を実行し、結果をジョブの概要に追記します。有意な悪化（終了コード 1）と比べられない組（2）では、ジョブを失敗にします。

{% raw %}
```yaml
- name: Compare with baseline
  if: ${{ !cancelled() && inputs.baseline_run_id != '' }}
  shell: pwsh
  env:
    GH_TOKEN: ${{ github.token }}
    BASELINE_RUN_ID: ${{ inputs.baseline_run_id }}
  run: |
    if ($env:BASELINE_RUN_ID -notmatch '^\d+$') { throw 'baseline_run_id は数字の実行 ID にしてください。' }
    if (-not (Test-Path eval/out/ci/summary.json)) { throw '今回の summary.json がないため比べられません。' }
    gh run download $env:BASELINE_RUN_ID --repo $env:GITHUB_REPOSITORY --name foundry-evaluation --dir eval/out/baseline
    if ($LASTEXITCODE -ne 0) { throw '基準の評価結果を取得できませんでした。' }
    dotnet run --project eval/UpdatesCowork.EvalRunner -- compare --baseline eval/out/baseline --candidate eval/out/ci
    $code = $LASTEXITCODE
    if (Test-Path eval/out/ci/comparison.md) { Get-Content eval/out/ci/comparison.md -Raw >> $env:GITHUB_STEP_SUMMARY }
    exit $code
```
{% endraw %}

成果物を取得するため、ワークフローの権限に `actions: read` を加えています。品質ゲートを通った版を本番に出したら、その評価の実行 ID を次の `baseline_run_id` にします。これが「出した版が次の基準になる」に当たります。評価データに失敗ケースを足したときは、SHA-256 が変わって比べられなくなるため、基準の版を新しいデータセットで評価し直し、その実行 ID を基準にします。

<aside class="callout callout-caution">
  <span class="callout-label">確認済みの範囲</span>
  <p><code>compare</code> は、単体テストと、上のローカルの実行記録で確かめています。GitHub 上での評価ワークフローの実行は、OIDC の設定を含めて未確認です（第 6 章）。新しい環境では、基準の取得と、悪化したときにジョブが失敗することを実際に確かめてから、リリースの条件に組み込んでください。</p>
</aside>

### 本番を今の版に固定したまま、新しい版を確かめる

既定では、エージェントのエンドポイントは最新の版に要求を送ります。配置してからテストする手順では、テストの前に本番の要求が新しい版へ移ってしまいます。本番を今の版に固定してから新しい版を作り、その版だけを指定して確かめ、承認後に切り替えます。

<ol class="numbered-steps">
  <li><b>本番を固定する</b><span>今の版を記録し、<code>FixedRatio</code> のルール 1 つで要求の 100% をその版に向けます。版の間で要求を分割することはできません。</span></li>
  <li><b>新しい版を作る</b><span>配置で作られた版を記録し、状態が <code>active</code> になるのを待ちます。</span></li>
  <li><b>その版だけを確かめる</b><span>新しい版を指定したセッション（<code>version_indicator</code>）や、<code>x-agent-version-override</code> を付けた要求で確かめます。</span></li>
  <li><b>承認後に切り替える</b><span>確かめた版にルールを向け直します。本番の確認で問題が出たら、前の版のルールに戻します。</span></li>
</ol>

本番に向ける版の設定は `azd` では変えられないため、REST API か SDK を使います。前の版とイメージは、戻す可能性があるあいだ残します。版やエージェントを削除しても、戻したことにはなりません。

評価スクリプトは、要求ごとに `x-agent-version-override` で評価する版を指定しています。次は [run-foundry.ps1](https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/eval/run-foundry.ps1) の抜粋です。

```powershell
Invoke-Foundry 'post' "agents/$AgentName/endpoint/protocols/openai/responses?api-version=v1" `
    $responseFile (Join-Path $Out "requests\$id.json") @("x-agent-version-override=$AgentVersion") -RetrySessionCreation
```

Runner は、保存した応答の `agent_reference` の名前と版が、指定した値と一致するかを確かめてから採点に使います。Web の BFF にも、呼び出す版を指定する設定 `Agent:VersionOverride` があります（第 4 章）。

### 評価用の azd 拡張で品質ゲートを作る

Foundry の評価用 azd 拡張（`azure.ai.evaluations`、プレビュー）を使う場合は、`evals/azure.eval.yaml` に評価を定義し、合格率が基準を下回ったときに CI を失敗させます。

```powershell
azd ai eval run start --fail-on pass-rate=0.8 --no-prompt
```

`--fail-on` を付けないと、不合格のサンプルがあっても終了コードは 0 になります。また、`run start` は評価する版を固定しないため、変更した版を配置する前に、基準になる実行を記録しておきます。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">07 / PRODUCTION</div>
<div class="section-content" markdown="1">

## 本番の応答も、リリース前と同じ基準で採点する
{: #production}

公式ブログは、検証のステップに、本番の通信を抜き出して採点する継続評価、ゴールデン データセットを定期的に評価し直すスケジュールされた評価、定期的なレッドチーミング、スコアが下がったときのアラートを含めています。このリポジトリでは、いずれも有効にしていません。

本番の応答には正解データがありません。正解を必要としない評価器と、自分で定義する評価器（カスタム評価器、プレビュー）を組み合わせると、リリース前と同じ観点で本番の応答も採点できます。第 6 章の 8 指標は、次のように振り分けられます。

| このリポジトリの指標 | 本番での確かめ方 | 使う機能 |
|---|---|---|
| タスクの遵守、意図の解決、ツール呼び出しの精度 | 本番の応答をそのまま採点。正解データは不要 | 継続評価（組み込みの評価器） |
| 出典、日本語 | コードの検査をカスタム評価器として定義し直して採点 | 継続評価（カスタム評価器） |
| ツールの実行、成果物、攻撃への耐性 | 本番の失敗を評価データに加え、リリース前に確かめる | 品質ゲート、レッドチーミング |

Hosted Agent では、本番のトレースを一定の間隔で採点する、スケジュールされた評価として設定します。第 5 章の画面 08 のモニターにある「スケジュールされた評価」が、ポータルでの入口です。本番のトレースを評価するときは、採点するトレースをランダムに選ぶほか、似た依頼を除いてまれな依頼も残す「インテリジェント サンプリング」でも選べます。

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
<div class="section-marker">08 / SPLIT</div>
<div class="section-content" markdown="1">

## 自動化する範囲と、人が判断する範囲を分ける
{: #split}

次の図は、Foundry の機能をすべて使って 4 つのステップを自動化した場合の設計例です。定期的な採点と品質ゲートが検証、問題の分類が特定、評価データ化が確立、改善案の作成が最適化に当たります。採点から改善案の判定までは自動で進め、原因の確認と本番への反映は担当者が判断します。

<figure class="diagram">
  <a href="{{ '/assets/diagrams/improvement-loop.png' | relative_url }}" title="原寸で開く"><img src="{{ '/assets/diagrams/improvement-loop.png' | relative_url }}" alt="改善ループの設計例。本番の Hosted Agent の応答を Microsoft Foundry が定期的に採点し、問題を分類し、本番のトレースから評価データを作り、改善案を作成する。CI の品質ゲートで評価し、担当者が原因の確認と採用を判断してから、新しい版に切り替える"></a>
  <figcaption><span>設計例</span> Foundry の機能を使った場合の流れ。このリポジトリで実装しているのは、利用者の評価の記録、アラート、品質ゲートと版の比較です。</figcaption>
</figure>

| ステップ | 自動で行うこと | 人が判断すること | 使う機能 |
|---|---|---|---|
| 確立 | 利用者の評価の記録、注釈からの評価データの作成 | 評価データに足すケースと期待する動き、ゴールデン データセットにするか | トレースの注釈、評価データ <span class="status status-preview">プレビュー</span> |
| 特定 | 低下の通知、問題の分類と原因の候補 | 原因の確認と、対応の要否 | アラート <span class="status status-preview">プレビュー</span>、Insights <span class="status status-ga">GA</span> |
| 最適化 | 改善案の作成と、同じデータでの採点 | 採用する案の選択 | 最適化 <span class="status status-limited">限定プレビュー</span> |
| 検証して繰り返す | 品質ゲートの判定、基準との統計的な比較、本番の継続評価 | 本番への切り替えと前の版への戻し、次の基準にする版 | CI の評価、版の固定、継続評価 <span class="status status-preview">プレビュー</span> |

本番への反映を承認制にし、前の版に戻せる状態を保ったまま、自動化の範囲を少しずつ広げます。

</div>
</section>

<section class="lesson-section" markdown="1">
<div class="section-marker">09 / PRACTICE</div>
<div class="section-content" markdown="1">

## 演習: このリポジトリで 1 周を回す
{: #practice}

プレビューの機能を有効にしなくても、次の演習はリポジトリのコードと記録で進められます。

<ol class="numbered-steps">
  <li><b>評価をトレースに付ける</b><span>偽の Agent と Aspire Dashboard でチャットを送り、低い評価とコメントを付けます。同じトレース ID の中に <code>feedback.record</code> と <code>gen_ai.evaluation.result</code> があることを確かめます。</span></li>
  <li><b>評価ケースに書き直す</b><span>付けたコメントから、<code>core.jsonl</code> と同じ形式で、依頼、期待するツール、<code>ground_truth</code> を書きます。利用者の文章はそのまま使いません。</span></li>
  <li><b>2 回の評価を比べる</b><span>自分の環境で同じ版を 2 回評価し、<code>compare</code> で比べます。差が偶然の範囲に収まるか、揺れの大きさを確かめます。Azure を使わずに判定の動きを確かめるときは、<code>dotnet test eval\UpdatesCowork.Evaluation.Tests --filter 'Category!=Eval'</code> を実行します。</span></li>
  <li><b>最適化に対応させる変更を洗い出す</b><span><code>AgentFactory.cs</code> の指示文の読み込み、<code>Skills/</code>、<code>CoworkTools.cs</code> の <code>[Description]</code> を読み、設定から読み込む形にするには何を変えるかを書き出します。</span></li>
  <li><b>本文の記録方針と、版の切り替え手順を書く</b><span>本番で本文を残す範囲、閲覧できる人、保持期間と、本番の固定、新しい版の確認、切り替え、戻しを誰がいつ承認するかを 1 ページにまとめます。</span></li>
</ol>

機能を試すときは、本番とは別の Foundry プロジェクトか評価用のエージェントを使います。エージェントの最適化を使うには、サブスクリプションの許可リストへの登録が必要です。

</div>
</section>

<section class="checkpoint" markdown="1">
<p class="eyebrow">CHECKPOINT</p>

## 次章へ進む前に

- 継続的改善の 4 つのステップと、それぞれでこのリポジトリが使っている仕組みを説明できる。
- 利用者の評価を、評価した応答のトレースに付ける理由と方法を説明できる。
- 合格率の差が偶然の範囲かを確かめる方法と、比べてはいけない組の条件を説明できる。
- 本番を今の版に固定したまま新しい版を確かめて切り替え、その版を次の基準にする手順を説明できる。
- 改善のループのうち、自動で進める段階と人が判断する段階を分けられる。

<div class="source-links">
  <a href="https://azure.microsoft.com/en-us/blog/continuous-improvement-comes-to-microsoft-foundry/">Azure Blog: Continuous improvement comes to Microsoft Foundry <span>↗</span></a>
  <a href="https://devblogs.microsoft.com/foundry/whats-new-in-foundry-agent-service-at-ignite-2026/">Microsoft Foundry Blog: What's new in Foundry Agent Service at Ignite 2026 <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/observability/how-to/trace-annotations">Annotate traces with human feedback <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/observability/how-to/log-end-user-feedback">Log end user feedback <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/observability/how-to/human-in-the-loop-golden-datasets">Curate golden datasets <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/observability/how-to/agent-insights">Find recurring issues with insights <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/agents/concepts/agent-optimizer-overview">What is the agent optimizer? <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/agents/how-to/optimize-agents-from-insights">Fix insights with agent optimizer <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/how-to/evaluation-github-action">How to run an evaluation in GitHub Action <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/observability/how-to/continuous-scheduled-evaluation">Set up continuous and scheduled evaluation <span>↗</span></a>
  <a href="https://learn.microsoft.com/azure/foundry/agents/how-to/manage-hosted-agent">Manage hosted agents <span>↗</span></a>
  <a href="https://github.com/tokawa-ms/azure-updates-cowork-starter-demo/blob/main/docs/notes/evaluation.md">評価の実行記録（版を比べる） <span>↗</span></a>
</div>
</section>

<nav class="lesson-next" aria-label="章の移動">
  <a href="{{ '/improvement/' | relative_url }}"><small>前の章 / 07</small><span>直す: 失敗ケースから改善する</span></a>
  <a class="next-link" href="{{ '/adoption/' | relative_url }}"><small>次の章 / 09</small><span>導入する: 自社のエージェントに当てはめる <b aria-hidden="true">→</b></span></a>
</nav>
