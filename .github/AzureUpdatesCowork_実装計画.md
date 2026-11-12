**日本語** | [English](AzureUpdatesCowork_実装計画-en.md)

# Azure Updates Cowork実装計画

- 版: 1.0 (2026-11-11)
- 前提: 設計は`docs/design.md`、Copilotの決まりは`.github/copilot-instructions.md`
- 進め方: 1つのフェーズを1つのブランチとPRで実装する。PRの説明には、受け入れ条件のチェックリストと、確かめた[要確認]事項(設計書14.2節の番号)の結果を書く。

| フェーズ | 内容 | 主に使う節 | Azureが必要か |
|---|---|---|---|
| P0 | リポジトリの土台 | 4章 | 不要 |
| P1 | siteproxy | 5.1、6.1 | 不要 |
| P2 | webの画面とBFFの骨組み(偽のエージェント) | 5.2、6.2、6.3 | 不要 |
| P3 | エージェントをローカルで動かす(取得、スキル、進行状況) | 5.3、5.5、付録A、B | モデルだけ必要(`azd provision`済みのプロジェクト) |
| P4 | 成果物(Word、PowerPoint、保存、Prompt Shields) | 5.3、5.4、6.5 | 一部 |
| P5 | webとエージェントをつなぐ(ローカル) | 5.2、6.3、6.4、6.6 | モデルだけ必要 |
| P6 | Azureへのデプロイ | 7、10、11章 | 必要 |
| P7 | 観測 | 8章、付録D | 必要 |
| P8 | 評価 | 9章、付録C | 必要 |
| P9 | 仕上げ(承認、サインイン、学習ガイド) | 5.2、5.3、7.3、12章 | 必要 |

P3より前にモデルを使いたい場合は、P6のうちFoundryアカウント、プロジェクト、モデルデプロイ、ストレージだけを先に`azd provision`してよい。

---

## P0 リポジトリの土台

作るもの: `global.json`、`Directory.Build.props`、`Directory.Packages.props` (設計書4.3節の版)、`UpdatesCowork.slnx`、`src/`と`tests/`と`eval/`のプロジェクト(中身は空でよい)、`.gitignore` (`.azure/`、`.artifacts/`、`eval/.results/`、`eval/out/`を含む)、`.github/workflows/ci.yml`、`.vscode/mcp.json` (付録E)、`README.md` (概要と、設計書へのリンクだけ)。

### Copilotへの依頼例

> `docs/design.md`の4章に従って、ソリューションとプロジェクトの骨組みを作ってください。パッケージの版は4.3節の表のとおりに`Directory.Packages.props`で固定し、「実装時の最新安定版」と書かれたものは`dotnet package search`で確かめた版を入れてください。`ci.yml`はビルドと単体テストだけを行います。

### 受け入れ条件

- [ ] `dotnet build`と`dotnet test --filter Category!=Eval`が成功する。
- [ ] フォルダー構成が設計書4.1節と一致する。
- [ ] すべてのプロジェクトが`net10.0`で、パッケージの版が`Directory.Packages.props`だけに書かれている。
- [ ] `ci.yml`がPRで成功する。

## P1 siteproxy

作るもの: 事前調査の記録`docs/notes/site-survey.md`、`UpdatesCowork.SiteProxy` (ルート、変換、レート制限、`/healthz`、`/robots.txt`、`/__auc/bridge.js`)、`wwwroot/__auc/bridge.js`、単体テスト。

### Copilotへの依頼例

> `docs/design.md`の5.1節に従ってsiteproxyを実装してください。最初に「P1の事前調査」の4項目を実行して`docs/notes/site-survey.md`に結果を書き、その結果から任意の規則(`ProxyDataApi`など)を有効にするかを決めてください。HTMLの書き換え、ヘッダーの書き換え、`Set-Cookie`の書き換えには単体テストを付けてください。

### 受け入れ条件

- [ ] `https://localhost:7102/ja-jp/updates/`で更新情報サイトが表示され、詳細ページにも移動できる。
- [ ] 応答に`X-Frame-Options`がなく、`Content-Security-Policy: frame-ancestors 'self' https://localhost:7101`が付いている。
- [ ] HTMLの`</head>`の直前に`bridge.js`のタグが入っている。
- [ ] GETとHEAD以外は405。上流以外のホストへは中継しない。
- [ ] 単体テスト: 絶対URLの置き換え、タグの差し込み、`Location`と`Set-Cookie`の書き換え、5 MBを超えるHTMLを書き換えないこと。

## P2 webの画面とBFFの骨組み

作るもの: `UpdatesCowork.Web`の静的な画面(`wwwroot`)、`/api/config`、`/api/chat` (SSE)、`/api/threads/new`、`/api/feedback`、`InMemoryChatRegistry`、匿名の利用者識別、`FakeAgentGateway` (固定のイベント列を返す)、`ChatEventMapper`、セキュリティヘッダー、レート制限。

### Copilotへの依頼例

> `docs/design.md`の5.2節と6.1〜6.3節に従って、webの画面とBFFを実装してください。エージェントの代わりに、録画したSSEを再生する`FakeAgentGateway`を使います。録画は`tests/recordings/fake-word.sse`に、6.3節の全イベントが出るように手で書いてください。

### 受け入れ条件

- [ ] 左にsiteproxyのiframe、右にチャットが表示される。iframe内でページを移動すると「表示中」の表示が変わる。
- [ ] 送信すると、進行状況、ツールの動き、成果物カード、応答の文字が順に表示される。
- [ ] 別のオリジンからの`postMessage`を無視する(テスト用のHTMLで確かめる)。
- [ ] `X-AUC-Request`ヘッダーのないPOSTは400。
- [ ] 応答のMarkdownに含まれる`<script>`が実行されない。
- [ ] 単体テスト: `ChatEventMapper`の対応表(6.3節)の全行、`PageContextValidator`。

## P3 エージェントをローカルで動かす

作るもの: `UpdatesCowork.AzureUpdates` (HTTP版と固定データ版)、`eval/fixtures/updates/` (実際のAPIから5件以上と一覧を保存)、`tools/fixtures/refresh-fixtures.ps1`、`UpdatesCowork.Agent`のProgram.cs、AgentFactory、指示文(付録A)、スキル(`summarize-update`、`compare-updates`)、ツール(`search_azure_updates`、`get_azure_update`、`update_plan`)、`CurrentTimeContextProvider`、`ToolCallMiddleware`、`tools/http/agent-local.http`。

### Copilotへの依頼例

> `docs/design.md`の5.3節と5.5節に従って、エージェントをローカルで動くところまで実装してください。成果物のツールはまだ作りません。14.2節のQ1とQ2を、固定したパッケージのXMLドキュメントで確かめてからProgram.csとAgentFactoryを書き、どちらの形にしたかをPRに書いてください。

### 受け入れ条件

- [ ] `dotnet run --project src/UpdatesCowork.Agent`で8088番が立ち上がり、`/readiness`が200を返す。
- [ ] `agent-local.http`で「559242を要約して」と送ると、`get_azure_update`が呼ばれ、IDとURLを含む要約が返る。
- [ ] 「関連する更新を探して」で`search_azure_updates`が呼ばれる。
- [ ] `AZURE_UPDATES_FIXTURES_DIR`を設定すると、ネットワークなしで同じ流れが動く。
- [ ] 選んだモデルで関数ツールが実際に呼ばれることを確かめた(14.1節)。
- [ ] 単体テスト: データAPIの問い合わせ文字列、HTMLからテキストへの変換、引数の検証、ツール呼び出しの上限。

## P4 成果物

作るもの: `UpdatesCowork.Office` (テンプレートを埋め込み、`WordReportBuilder`、`SlideDeckBuilder`、検証)、ツール`create_word_report`と`create_powerpoint_deck`、`LocalArtifactStore`、`SessionArtifactStore`、`PromptShieldClient`、スキル`word-report` (`references/outline.md`を含む)と`slide-deck`。

### Copilotへの依頼例

> `docs/design.md`の5.4節と6.5節に従って、WordとPowerPointの生成を実装してください。テンプレートは`src/UpdatesCowork.Office/Templates/`にあるものを使い、作り直さないでください。生成したWordとPowerPointは`OpenXmlValidator`で検証し、エラーがあれば保存しないでください。

### 受け入れ条件

- [ ] 生成したファイルがWordとPowerPoint (デスクトップ版かWeb版)でエラーなく開ける。PowerPointは16:9で、ノートが表示される。
- [ ] 日本語がYu Gothic (または代わりのフォント)で表示される。
- [ ] 単体テスト: 設計書5.4節「テスト」の全項目。
- [ ] Prompt Shieldsの3つのモード(`Off`、`Annotate`、`Block`)の動きを、HTTPを差し替えたテストで確かめた。

## P5 webとエージェントをつなぐ(ローカル)

作るもの: `ResponsesAgentGateway`、`BearerTokenHandler`、`LocalArtifactReader`、成果物の台帳登録とダウンロード、`tests/recordings/`に実際のSSEを保存する仕組み(`Agent:RecordTo`設定)、`tools/http/web.http`。

### Copilotへの依頼例

> `docs/design.md`の5.2節のIAgentGatewayと6.4節に従って、BFFからローカルのエージェント(`http://localhost:8088`)を呼べるようにしてください。14.2節のQ3を確かめ、会話の続け方(`Agent:ConversationMode`)の既定値を決めてください。実際のSSEを3種類(要約、Word、PowerPoint)録画し、`ChatEventMapper`のテストに使ってください。

### 受け入れ条件

- [ ] 画面から「要約」「Wordにする」「PowerPointにする」を実行でき、成果物をダウンロードできる。
- [ ] 2回目の依頼で前の会話の内容を踏まえた答えが返る。
- [ ] 他人の成果物ID (台帳にないもの)では404になる。
- [ ] ブラウザーを閉じるとエージェントへの要求も取り消される(ログで確かめる)。
- [ ] 200秒を超えると`error` (`timeout`)が返る(設定値を短くして確かめる)。

## P6 Azureへのデプロイ

作るもの: `infra/` (設計書10.1節のリソース、10.2節のパラメーターと出力)、`azure.yaml` (10.3節)、`infra/hooks/postdeploy.*`、3つのDockerfile、`.github/workflows/deploy.yml`、`InMemoryChatRegistry`、`SessionFilesArtifactReader`、`tools/env/load-env.*`、`tools/http/agent-remote.http`。

### Copilotへの依頼例

> `docs/design.md`の7章、10章、11章に従って、`azd up`で全体を作れるようにしてください。14.2節のQ6〜Q9を、`azd deploy agent --preview --no-prompt`、`az deployment group what-if`、`az rest`で確かめ、azure.yamlをA案とB案のどちらにしたかを`docs/notes/deploy.md`に書いてください。

### 受け入れ条件

- [ ] 空のサブスクリプションで`azd up`が成功し、`WEB_URL`で画面が開く。
- [ ] `allowedIpRanges`に自分のIPを入れると、ほかのIPからは403になる。
- [ ] Azure上で「Wordにする」が成功し、Hosted Agentのセッションファイルから画面へダウンロードできる。
- [ ] `id-web`が自分で作成したAgentセッションのファイル取得と削除を行える。Foundry Userなど広いロールを追加しない。
- [ ] 新しい会話で旧セッションの削除を行い、ブラウザー終了時にも削除を試みる。要求が届かない場合はFoundryの最大30日保持になることを確認する。
- [ ] webのレプリカが1つで、プロセス再起動後にメモリー台帳とData Protectionキーが失われることを確認する。
- [ ] 既存Storage Accountとデータが削除されず、新しいデプロイとアプリから参照されない。
- [ ] `id-web`からFoundry Agent Consumerを外すと403になり、画面に`agent_forbidden`が出る(確かめたら戻す)。
- [ ] `azd ai agent show`でRAIポリシーが付いていることを確かめた。
- [ ] どこにも鍵や接続文字列の鍵がない(`git grep`とポータルで確認)。

## P7 観測

作るもの: 3つのアプリのOpenTelemetryの設定、設計書8.3節のスパンとメトリック、`tools/otel/compose.yaml` (Aspire Dashboard)、利用者の評価の記録、`docs/kql/*.kql` (付録D)、検索アラートのBicep (任意)、`docs/notes/observability.md` (Q4、Q5の結果)。

### Copilotへの依頼例

> `docs/design.md`の8章に従って観測を実装してください。ローカルでは`tools/otel/compose.yaml`のAspire Dashboardに送り、AzureではApplication Insightsに送ります。14.2節のQ4とQ5を確かめ、つながらない場合は8.4節の2番目と3番目の手段を実装してください。

### 受け入れ条件

- [ ] ローカルで、1回の依頼について`chat.turn` → HTTP → `invoke_agent` → `execute_tool` → 自作スパンの並びがAspire Dashboardで見える。
- [ ] Azureで、FoundryポータルのTracesタブに会話が表示され、ツール呼び出しの順序が見える。
- [ ] 付録DのKQLがすべて結果を返す(列名を実データに合わせて直したものを`docs/kql/`に置く)。
- [ ] 「役に立たなかった」を押した応答を、KQLでエージェント側のトレースまでたどれる。
- [ ] 同じスパンが二重に出ていない。

## P8 評価

作るもの: `eval/datasets/core.jsonl` (設計書9.2節の30件)、`injection`用の固定データ、`eval/UpdatesCowork.Evaluation.Tests` (9.3節の評価器)、`eval/UpdatesCowork.EvalRunner` (`cloud`と`continuous enable/disable`)、`eval/thresholds.json`、`.github/workflows/eval.yml`。

### Copilotへの依頼例

> `docs/design.md`の9章に従って評価を実装してください。まずローカル評価で30件を回してレポートを作り、次に`EvalRunner cloud`でデプロイ済みのエージェントを評価してください。14.2節のQ10〜Q12は、1件だけの評価を流して確かめてから全件に広げてください。

### 受け入れ条件

- [ ] `dotnet test eval/UpdatesCowork.Evaluation.Tests --filter Category=Eval`のあと、`aieval report`のHTMLが作られる。
- [ ] `EvalRunner cloud`の結果がFoundryポータルのEvaluationsに表示され、`eval/out/summary.md`に評価器ごとの合格率が出る。
- [ ] しきい値を一時的に1.0に上げると`eval.yml`が失敗する(確かめたら戻す)。
- [ ] 継続評価を有効にし、Monitorタブに結果が出るまでの時間を`docs/notes/evaluation.md`に記録した。
- [ ] `injection`の4件で、埋め込んだ指示に従っていない。

## P9 仕上げ

作るもの: 承認機能(`REQUIRE_APPROVAL_FOR_ARTIFACTS`、`/api/chat/approval`、承認カード)、任意のEntra IDサインイン(`Auth:Mode=EntraId`)、学習ガイド`docs/labs/lab1.md`〜`lab7.md` (設計書12章)、READMEのクイックスタート。

### 受け入れ条件

- [ ] 承認機能を有効にすると、Wordを作る前に承認カードが出て、「やめる」を押すとファイルが作られない。
- [ ] Entra IDモードで、サインインしないとチャットが使えない。
- [ ] 7つのラボがあり、どれも「ゴール」「触るコード」「確かめること」「発展」の4節を持つ。
- [ ] READMEの手順だけで、新しい人が`azd up`からラボ1までを進められる。

## 発展(P10以降、任意)

- 利用者ごとのセッション分離(設計書7.3節、Q15)。
- `x-agent-version-override`で2つの版を同じデータセットで比べる。
- バックグラウンド応答(`background: true`)とポーリングで長い処理に対応する。
- Pythonでのレッドチーミング(設計書9.7節)。
