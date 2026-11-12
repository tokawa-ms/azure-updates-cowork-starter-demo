# Azureデプロイ事前検証

日本語 (プライマリ) | [English](preflight-report-en.md)

初回実施日: 2026-11-11。更新: 2026-11-13。状態: **共有基盤とWebの更新は成功。本番Agent v11・評価用v12も現在のソースと一致し、activeを確認済み。固定データ評価の結果は評価記録を参照**。

## 概要

| 項目 | 結果 |
|---|---|
| 種別 | azdプロジェクト、resourceGroupスコープ |
| テンプレート | `infra/main.bicep` と参照モジュール |
| パラメーター | `infra/main.parameters.json`、ローカルazd環境 |
| Bicep構文 | 最終変更後のbuild成功、警告なし |
| 初回preview | 成功。既存Foundry／モデルの新規作成なし |
| 初回provision | 成功 |
| 初回アプリ展開 | ACR／RAIの停止を解決後にHosted Agent、Web、siteproxyの展開成功 |
| Session Files移行後preview | 成功。現在値を明記した再検証後に適用済み。Storage削除なし |
| 専用ビューアー構成preview / provision | 成功。旧proxyとStorageはSkip、削除なし。Webは1レプリカを維持 |
| 11月13日の一式更新と再確認 | 基盤デプロイはSucceeded。ACRでビルドしたWebがHealthy / Running。現在のBicep、配置済みイメージ、実エンドポイントを照合 |
| 11月13日の旧リソース削除後preview | 成功。旧proxy・専用ID・Storageの再作成なし。現行Webと両Agentは稼働継続 |

## 実行したツール

- `az bicep build --file infra\main.bicep --outfile <セッション領域のARMファイル>`: 終了コード0。
- `azd provision --preview`: 終了コード0、初回previewは42秒。
- `azd provision --no-prompt`: 初回成功。実イメージを使った再provisionはACR取得で失敗。
- `azd deploy agent --preview --no-prompt`: 成功。実デプロイの成功を保証するものではなかった。
- `azd provision --preview --environment auc-dev`: Session Files移行後のpreview成功、終了コード0。変更は適用していない。
- Azure CLIでContainer Apps環境、ACR、Web、siteproxyの現在値を読み取り、preview差分を照合した。

azd 1.37.3、azure.ai.agents 1.0.0、Bicep 0.49.2。azdのpreview経路を使ったため、az CLIのProvider／ProviderNoRbac切り替えは実施していない。

## 初回what-if

azdの要約はContainer Apps 2個、Express環境、ACR、Log Analytics、Storageの6件をCreateと報告した。既存Foundry／モデルのCreateは表示されなかった。この6件はazdが表示した主要リソース数であり、子リソース・ロール割り当てを含むARM全体の件数ではない。削除は要求していない。

## Session Files移行後what-if

`auc-dev` のプレビューは成功し、既存Storage Accountは `Skip`、リソースのCreate／Deleteは表示されなかった。確認された変更:

| リソース | プレビュー差分 | 評価 |
|---|---|---|
| `ca-web-auc-dev` | ACAのregistry／container設定、replica数 `0..3` → `1..1`、`workloadProfileName` の省略 | 1レプリカ固定はインメモリ台帳とData Protectionキーの設計どおり。アプリ設定／イメージ更新を含む |
| `ca-proxy-auc-dev` | ACAのregistry／container設定、`workloadProfileName` の省略 | 新しい構成でのアプリ設定／イメージ更新を含む |
| `cae-auc-dev` | Log Analytics `customerId` の式表現、peer設定・`publicNetworkAccess`・workload profileの省略 | 現在値は公開ネットワーク `Enabled`、mTLSとpeer traffic encryptionは無効、Consumption profile。意図した値ではあるが、テンプレートからの省略を含む |
| `<container-registry>` | `dataEndpointEnabled` と`encryption`の省略 | Azure CLIで取得した現在値はどちらもnull |
| `log-auc-dev`、Storage Account | Skip | 変更なし。Storage削除なし |

ACA環境の`customerId`は現在値と同じLog Analytics workspaceを参照する式に変わる。previewはリソース削除を示していないが、複数リソースでプロパティ省略が報告されたため、この時点では適用を保留した。

## 現在値を明記した後の再what-ifと適用

ACA環境の`publicNetworkAccess`、peer設定、Consumption workload profile、Container Appの`workloadProfileName`、ACRの`dataEndpointEnabled`と`encryption`を、Azureの現在値に合わせてBicepに明記した。`enableFips`はBicepの型定義にないため(BCP037)指定していない。Bicepのビルドは警告なしで成功した。

再プレビューで残った差分は、Container Appのregistry／container設定、webのレプリカ数 `0..3` → `1..1`、ACA環境の`customerId`の式表現だけだった。ACR、Log Analytics、StorageはSkipで、削除はなかった。

2026-11-11に`azd provision --environment auc-dev`を適用し(1分47秒)、Agent、web、siteproxyをデプロイした。クラウドでのスモークテストで、成果物の作成、ダウンロード、利用者の分離、セッション削除を確認した。詳細は [docs/notes/deploy.md](docs/notes/deploy.md) を参照。

## 問題と推奨事項

初回展開ではExpressのACR取得とHosted AgentのRAIポリシー指定で停止したが、その後解決してアプリ展開に成功した。経緯と回避策は [docs/notes/deploy.md](docs/notes/deploy.md) を参照。

既存アプリの初回展開とSession Files移行は、どちらも適用済み。認証を鍵に置き換えたり、ガードレールを外したりはしていない。既存Foundry参照を省略した新規作成分岐は未検証。

## 専用ビューアーと評価用エージェントの事前検証

2026-11-12に次を実行した。azdは1.37.3。

- `az bicep build --file infra\main.bicep --outfile <セッション領域>\viewer-infra.json`: 警告なし、終了コード0。
- `azd provision --preview --environment auc-dev --no-prompt`: 成功、26秒。
- `azd deploy agent-eval --preview --environment auc-dev --no-prompt`: 成功。`updates-cowork-eval`を新規作成する計画を確認。
- `azd provision --environment auc-dev --no-prompt`: 成功、1分38秒。

| リソース | what-if | 確認内容 |
|---|---|---|
| `ca-web-auc-dev` | Modify | registryとcontainer設定。旧proxy向け環境変数を削除し、1レプリカ固定を維持 |
| `cae-auc-dev` | Modify | Log Analytics `customerId`の同一workspace参照への式表現の差分 |
| `ca-proxy-auc-dev` | Skip | `enableLegacyProxy=false`により新構成の対象外。既存リソースは削除しない |
| ACR、Log Analytics、Storage | Skip | 削除なし |
| 既存Foundryとモデル | 新規作成なし | 既存プロジェクトを継続利用 |

provisionには「全対象スコープのroleAssignments/write権限を事前検証できない」という警告があったが、実適用は成功した。新しいHosted AgentのIDに対するpostdeployのロール付与も成功した。評価専用エージェントは固定データのみを使い、自作の本番エージェントの本文記録は無効にする。ただしFoundry基盤の`responsesapi`には本番本文が記録される実測があり、アプリ側設定だけでは停止できない。共有基盤の設定変更は行っていない。

最終のアプリ修正ではBicepを変更せず、`agent`と`agent-eval`だけを再デプロイした。公開Webで要約、Word/PPTXの作成・ダウンロード、利用者の分離、会話終了後の取得不可を確認済み。詳細は[デプロイ記録](docs/notes/deploy.md)、[MCPと観測](docs/notes/observability.md)、[評価記録](docs/notes/evaluation.md)を参照。

## 2026-11-13の一式更新後の再確認

Azure CLI 2.92.0、azd 1.37.3、Bicep 0.49.2で以下を実行した。今回の再確認では追加のprovisionは行っていない。

- `az bicep build --file infra\main.bicep --outfile .artifacts\azure-rollout-20261113\main.compiled.json`: 終了コード0。
- `azd provision --preview --environment auc-dev --no-prompt`: 終了コード0、23秒。WebとContainer Apps環境の2件がModify、旧proxy・ACR・Log Analytics・Storageの4件がSkip。Create／Deleteの表示なし。
- `az deployment group show -g <app-resource-group> -n <infrastructure-deployment>`: Succeeded、2026-11-13 06:55:52 JST。既存Foundry／App Insights／モデルを参照し、`enableLegacyProxy=false`。
- `az deployment group export -g <app-resource-group> -n <infrastructure-deployment>`: 配置済みテンプレートを取得し、現在のBicepのコンパイル結果と照合。metadataと型名の大文字・小文字を除いて差分0。リソース名・IDは公開用のプレースホルダー。

previewに残る差分は、Webの既存イメージを参照するregistry／container設定と、同じLog Analytics workspaceの`customerId`参照式である。Webの実revisionはHealthy / Runningで、1レプリカ・トラフィック100%。本番v11・評価用v12はactive。既存モデル`gpt-5.4-mini`と`gpt-5-mini`もSucceededであり、モデルの版や容量を更新する作業は行っていない。

実行結果と稼働イメージのdigest、ソースチェックサムの一致は[デプロイ記録](docs/notes/deploy.md#azure側の実行結果と配置内容の照合)に記載した。取得したJSONとログは`.artifacts/azure-rollout-20261113/`へ保存した。この時点では旧proxyとStorageを保全していた。既知のRAI変数展開制約と共有Foundry側の本文記録の制約は変わっていない。

## 旧リソース削除後のpreview

2026-11-13、利用者の明示承認後に旧proxy、専用Managed IdentityとAcrPull割り当て、旧Storageを削除した。Storage専用のEvent Grid system topicもアカウント削除に伴い消滅した。現行Web・両Agentが旧リソースを参照していないことは削除前に確認済み。

削除後の`azd provision --preview --environment auc-dev --no-prompt`は23秒、終了コード0。WebとContainer Apps環境は上記と同じ2件のModify、ACRとLog Analyticsは2件のSkipで、Create／Deleteの表示はなかった。旧proxy・専用ID・Storageを再作成する計画もなかった。previewを確認しただけで、追加のprovisionは適用していない。

現行WebはHealthy / Running、`/healthz`は`ok`、更新一覧は20件。本番v11・評価用v12はactiveのままである。削除前後の一覧と実行記録は`.artifacts/legacy-cleanup-20261113/`、手順と削除対象は[デプロイ記録](docs/notes/deploy.md#旧proxyとstorageの削除2026-11-13)を参照。
