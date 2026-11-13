**日本語** | [English](implementation-plan-en.md)

# Azure Updates Cowork実装計画

- 版: 1.1 (2026-11-12、専用ビューアー・MCP・固定データ評価へ更新)
- 前提: 設計は`docs/design.md`、Copilotの決まりは`.github/copilot-instructions.md`
- 進め方: 1つのフェーズを1つのブランチとPRで実装する。PRの説明には、受け入れ条件のチェックリストと、確かめた[要確認]事項(設計書14.2節の番号)の結果を書く。
- BFF: Backend for Frontend。画面からの要求を受け、エージェントなどのサーバー側サービスを呼び出すWeb API。

| フェーズ | 内容 | 主に使う節 | Azureが必要か |
|---|---|---|---|
| P0 | リポジトリの土台 | 4章 | 不要 |
| P1 | 旧siteproxy(比較用に保全) | 5.1 | 不要 |
| P2 | 専用ビューアーとBFF(偽のエージェント) | 5.2、6.1〜6.3 | 不要 |
| P3 | エージェントをローカルで動かす(取得、スキル、進行状況) | 5.3、5.5、付録A、B | モデルだけ必要(`azd provision`済みのプロジェクト) |
| P4 | 成果物(Word、PowerPoint、保存、Prompt Shields) | 5.3、5.4、6.5 | 一部 |
| P5 | webとエージェントをつなぐ(ローカル) | 5.2、6.3、6.4、6.6 | モデルだけ必要 |
| P6 | Azureへのデプロイ | 7、10、11章 | 必要 |
| P7 | 観測 | 8章、付録D | 必要 |
| P8 | 評価 | 9章、付録C | 必要 |
| P9 | 仕上げ(承認、サインイン、学習ガイド) | 5.2、5.3、7.3、12章 | 必要 |

P3より前にモデルを使いたい場合は、既存Foundryとモデルを参照する。新規作成が必要ならP6の対象だけを事前確認して配置する。現構成にStorageは不要。

ユーザー承認済みの再構築ではP2・P3/P4・P7・P8を変更する。左ペインはサイトのiframeではなくAPIから描画し、業務5機能を同一コンテナー内の非公開MCPへ移す。評価は本番とは別のAgentで行う。継続評価、7ラボ、公開リポジトリと自動デプロイは今回の完了条件に含めない。以下の未チェック項目は全体計画の残件であり、現行の確認結果は`docs/notes/`を参照する。

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

旧構成の比較・復旧用。以下は旧フェーズの受け入れ条件であり、現在の起動・デプロイの前提ではない。既存のAzureリソースも削除せず保全する。

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

作るもの: `UpdatesCowork.Web`の静的な画面(`wwwroot`)、`viewer.js`、`/api/updates`と`/api/updates/{id}`、`/api/config`、`/api/chat` (SSE)、`/api/threads/new`と`end`、`/api/feedback`、`InMemoryChatRegistry`、匿名の利用者識別、`FakeAgentGateway`、`ChatEventMapper`、セキュリティヘッダー、レート制限。

### Copilotへの依頼例

> `docs/design.md`の5.2節と6.1〜6.3節に従って、webの画面とBFFを実装してください。エージェントの代わりに、録画したSSEを再生する`FakeAgentGateway`を使います。録画は`tests/recordings/fake-word.sse`に、6.3節の全イベントが出るように手で書いてください。

### 受け入れ条件

- [x] 左に更新の検索・一覧・本文、右にチャットが表示される。iframeや外部ページの資産読み込みは使わない。
- [x] 詳細取得に成功した更新だけを「表示中」のページ文脈として使う。記事本文をブラウザーからモデルへ渡さない。
- [ ] 送信すると、進行状況、ツールの動き、成果物カード、応答の文字が順に表示される。
- [x] 記事本文をDOMPurifyで処理し、スクリプト、iframe、フォーム、外部画像を表示しない。
- [ ] `X-AUC-Request`ヘッダーのないPOSTは400。
- [ ] 応答のMarkdownに含まれる`<script>`が実行されない。
- [ ] 単体テスト: `ChatEventMapper`の対応表(6.3節)の全行、`PageContextValidator`。

## P3 エージェントをローカルで動かす

作るもの: `UpdatesCowork.AzureUpdates` (HTTP版と固定データ版)、`eval/fixtures/updates/` (実際のAPIから5件以上と一覧を保存)、`UpdatesCowork.Agent`のProgram.cs、AgentFactory、指示文(付録A)、スキル(`summarize-update`、`compare-updates`)、業務MCPサーバー／クライアント、`CurrentTimeContextProvider`、`ToolCallMiddleware`、`tools/http/agent-local.http`。更新の取得、検索、進行状況とP4の成果物生成をMCPの5ツールとして提供する。

### Copilotへの依頼例

> `docs/design.md`の5.3節と5.5節に従って、エージェントをローカルで動くところまで実装してください。成果物のツールはまだ作りません。14.2節のQ1とQ2を、固定したパッケージのXMLドキュメントで確かめてからProgram.csとAgentFactoryを書き、どちらの形にしたかをPRに書いてください。

### 受け入れ条件

- [ ] `dotnet run --project src/UpdatesCowork.Agent`で8088番が立ち上がり、`/readiness`が200を返す。
- [ ] `agent-local.http`で「559242を要約して」と送ると、`get_azure_update`が呼ばれ、IDとURLを含む要約が返る。
- [ ] 「関連する更新を探して」で`search_azure_updates`が呼ばれる。
- [ ] `AZURE_UPDATES_FIXTURES_DIR`を設定すると、ネットワークなしで同じ流れが動く。
- [ ] 選んだモデルで関数ツールが実際に呼ばれることを確かめた(14.1節)。
- [ ] 単体テスト: データAPIの問い合わせ文字列、HTMLからテキストへの変換、引数の検証、ツール呼び出しの上限。
- [x] 業務ツールを実際のStreamable HTTP MCP経由で呼ぶ。発見した5ツールの名前と入力スキーマをモデルへ渡す。
- [x] MCPをloopbackの動的ポートだけで待ち受け、起動時の秘密値、Origin、要求サイズ、セッションメタデータを検証する。公開の`/mcp`は作らない。
- [x] ツール失敗、承認、ファイル保存先のセッション分離をMCP移行後も維持する。

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

作るもの: `infra/`、`azure.yaml`、`infra/hooks/postdeploy.*`、WebとAgentのDockerfile、`InMemoryChatRegistry`、`SessionFilesArtifactReader`、`tools/env/load-env.*`、`tools/http/agent-remote.http`。本番と評価用に同じAgentソースを配置する。旧proxyのDockerfileは保全する。自動デプロイの`deploy.yml`は後続課題。

### Copilotへの依頼例

> `docs/design.md`の7章、10章、11章に従って、`azd up`で全体を作れるようにしてください。14.2節のQ6〜Q9を、`azd deploy agent --preview --no-prompt`、`az deployment group what-if`、`az rest`で確かめ、azure.yamlをA案とB案のどちらにしたかを`docs/notes/deploy.md`に書いてください。

### 受け入れ条件

- [x] 既存Foundryを参照した環境で`WEB_URL`から専用ビューアーを使える。
- [ ] 空のサブスクリプションで`azd up`が成功する。現行拡張のRAI変数展開には回避手順が必要なため、初回再現性は未確認。
- [ ] `allowedIpRanges`に自分のIPを入れると、ほかのIPからは403になる。
- [ ] Azure上で「Wordにする」が成功し、Hosted Agentのセッションファイルから画面へダウンロードできる。
- [ ] `id-web`が自分で作成したAgentセッションのファイル取得と削除を行える。Foundry Userなど広いロールを追加しない。
- [ ] 新しい会話で旧セッションの削除を行い、ブラウザー終了時にも削除を試みる。要求が届かない場合はFoundryの最大30日保持になることを確認する。
- [ ] webのレプリカが1つで、プロセス再起動後にメモリー台帳とData Protectionキーが失われることを確認する。
- [ ] 移行時に既存Storage Accountとデータを自動削除せず、新しいデプロイとアプリから参照しない。移行後に整理する場合は、参照がないことと運用者の明示承認を確認して別途削除する。
- [ ] `id-web`からFoundry Agent Consumerを外すと403になり、画面に`agent_forbidden`が出る(確かめたら戻す)。
- [ ] `azd ai agent show`でRAIポリシーが付いていることを確かめた。
- [ ] どこにも鍵や接続文字列の鍵がない(`git grep`とポータルで確認)。

## P7 観測

作るもの: WebとAgentのOpenTelemetry、MCP SDKの`Experimental.ModelContextProtocol`、設計書8.3節のスパンとメトリック、`tools/otel/compose.yaml` (Aspire Dashboard)、利用者の評価の記録、KQL、検索アラートのBicep (任意)、`docs/notes/observability.md`。

### Copilotへの依頼例

> `docs/design.md`の8章に従って観測を実装してください。ローカルでは`tools/otel/compose.yaml`のAspire Dashboardに送り、AzureではApplication Insightsに送ります。14.2節のQ4とQ5を確かめ、つながらない場合は8.4節の2番目と3番目の手段を実装してください。

### 受け入れ条件

- [x] Aspire Dashboardで実際の`invoke_agent` → `execute_tool` → HTTP/MCP → `mcp.tool.execute` → Office生成・保存のツリーが見える。
- [x] AzureのApplication Insightsで、WebからAgent、MCP、Officeまで同じ`operation_Id`に関連付く。
- [ ] Azureで、FoundryポータルのTracesタブに会話が表示され、ツール呼び出しの順序が見える。
- [ ] 付録DのKQLがすべて結果を返す(列名を実データに合わせて直したものを`docs/kql/`に置く)。
- [ ] 「役に立たなかった」を押した応答を、KQLでエージェント側のトレースまでたどれる。
- [ ] 同じスパンが二重に出ていない。
- [x] 自作Agentの本番本文記録は無効にし、評価用だけ有効にする。Foundry基盤側には本番本文が記録される実測結果と、その制約を明記する。

## P8 評価

作るもの: `eval/datasets/core.jsonl` (30件)、`injection`用の固定データ、オフラインの`Evaluation.Tests`、`EvalRunner` (`prepare` / `attach` / `collect` / `check`)、`eval/run-foundry.ps1`、`eval/thresholds.json`、手動実行の`eval.yml`。継続評価ルールは作成しない。

### Copilotへの依頼例

> `docs/design.md`の9章に従い、固定データ専用Hosted Agentの実応答を記録し、Foundryで採点してください。Q10とQ12を少数ケースで確かめてから30件へ広げます。実ファイルを検査し、未採点や未取得を合格に補完しないでください。通常CIではモデルを呼びません。

### 受け入れ条件

- [x] 通常のxUnitで準備・応答変換・判定・Office検査をネットワークなしで検証する。
- [x] 固定データ専用Agentの版を指定し、30件の実応答を記録してFoundryの実行IDとレポートURLを得る。
- [x] JSON・Markdown・HTMLに各指標の合格数、対象数、対象外数と不合格理由を出す。
- [x] 基準未達は終了コード1、未採点・実行失敗・ファイル未取得は成功扱いしない。基準を変えて結果を隠さない。
- [x] `injection`の4件で、埋め込んだ合言葉への追従を検査する。
- [x] 全結果と成果物の保存後、このrunの評価用セッションだけを削除し、`-Resume`で再利用・後片付けできることをクラウドで確認する(v2・v4・v6各30件とpilotの3件)。
- [ ] OIDCを設定したGitHub上で`eval.yml`の品質ゲートを確認する(公開先・認証設定後)。

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
- 本番本文の保存方針・費用・権限を合意した後の継続評価(Q11)。固定データ評価の完了とは分けて扱う。
