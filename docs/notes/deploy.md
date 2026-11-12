# デプロイの調査記録

確認日: 2026-11-12。azd 1.37.3、azure.ai.agents 拡張1.0.0、Bicep 0.49.2。**専用ビューアーとMCPを既存Azure環境へ配置済み。本番Agentはversion 10、固定データ評価用Agentはversion 6。** 旧siteproxyとStorageは削除せず、現在の利用経路から外している。

Web: <https://<web-app>.<environment>.<region>.azurecontainerapps.io/>

以下の初回障害は解消済みの履歴であり、現在アプリが停止しているという意味ではない。

## 既存Foundryの再利用

開発環境は既存プロジェクト、既存のエージェント用／評価用モデル、既存Application Insightsを参照した。アカウント、プロジェクト、モデルデプロイは新設していない。環境固有の値はコミット対象外の `.azure` に置く。

別の開発環境を設定するときの例:

```powershell
azd env new <環境名>
azd env set AZURE_LOCATION <リージョン>
azd env set AZURE_SUBSCRIPTION_ID <サブスクリプションID>
azd env set EXISTING_AI_PROJECT_RESOURCE_ID <既存プロジェクトのARMリソースID>
azd env set EXISTING_APPINSIGHTS_RESOURCE_ID <接続済みApplication-InsightsのARMリソースID>
azd env set AGENT_DEPLOYMENT_NAME <既存のモデルデプロイ名>
azd env set JUDGE_DEPLOYMENT_NAME <既存の評価用モデルデプロイ名>
# 既に必要なロールを持っている開発者に対する重複割り当てを避ける場合だけ指定
azd env set ASSIGN_DEVELOPER_FOUNDRY_ROLES false
azd provision --preview
```

`EXISTING_AI_PROJECT_RESOURCE_ID` が空の場合は新規Foundry作成の分岐になる。実行前にpreviewを読み、新設対象に共有Foundryやモデルが含まれないことを確認する。アプリの展開は成功したが、RAIポリシーIDの一時的な直接指定が必要なため、`azd up` の再現性は未確認。

## Q番号ごとの結果

| 項目 | 結果 |
|---|---|
| Q6 | `azure.yaml` はA案。ai-projectサービスとルートBicepを使う。本番v10と評価v6を配置。環境変数は`env`。RAIポリシーIDを一時的に直接指定する回避策は引き続き必要 |
| Q7 | Express環境を作成し、UAMIによるACR pullとWeb／siteproxyのデプロイに成功。両revisionのコンテナーはready、再起動0 |
| Q8 | `{project}/agents/{name}?api-version=v1`の`instance_identity.principal_id`を取得。本番と評価用の両IDへpostdeployでロールを付与。再デプロイ時の再実行も成功 |
| Q9 | 既存アカウント上のRAIポリシーと既存App Insights参照を利用。Agent versionの`rai_config`に対象ポリシーの完全なARMリソースIDが設定されたことを確認 |
| Q4 | Web、Agent、MCP、Office生成が同じ`operation_Id`に関連付くことをAzureで確認。[観測記録](observability.md)参照 |
| Q5 | 代替の`x-client-*`ヘッダー単独での伝播は未確認。Q4の実証と区別する |
| Q10、Q12 | Foundryの3組み込み評価器を登録し、30件の実応答を`jsonl/file_content`で採点済み。初期化は`model`、ツール呼び出しはflatなAgent Messages形式。[評価記録](evaluation.md)参照 |
| Q11 | 継続評価は未有効化。固定データ評価とは別の発展課題 |
| Q14 | WebのFoundry Agent Consumerだけで、自身のSession Filesを取得・削除できることを確認 |

## 調査結果と残る制約

### Container Apps ExpressのACR取得

WebとsiteproxyのイメージはACRでビルドできた。Dockerfileの `FROM --platform=$BUILDPLATFORM` をACRが解釈できず失敗したため、プラットフォーム指定を取り除いた。

初回の既存アプリ更新では次のエラーが発生した。

```text
BuildFailed: Authentication failed when pulling container image ...
Provide 'registryCredentials' (username and token) or 'managedIdentityClientId' ...
```

ユーザー割り当てIDと、そのIDへのACRスコープのAcrPullを確認した。初回の失敗後はARM応答の `configuration.registries` がnullだった。apiVersionを2026-07-01にそろえ、Bicepから明示的にイメージとregistry設定を再投入した後は、GETでserverとidentityが返るようになったが、その時点ではprovisioningStateはFailedのままだった。

同じExpress環境、ACR、UAMI、イメージを使った一時アプリでUAMIによるpullに成功した。調査後に一時アプリを削除し、Web／siteproxyも再デプロイしてコンテナーがreadyであることを確認した。ACR管理者パスワードを有効にしたり、ExpressをStandardへ変更したりする回避策は採っていない。

### Hosted AgentのRAIポリシー

`raiPolicyName` の環境変数参照では、version作成でHTTP 400 `invalid_payload` が返る。

```text
RAI policy name must be a fully qualified Microsoft.CognitiveServices accounts/raiPolicies ARM resource ID.
```

azd環境の値が完全なARMリソースIDであることを確認した。隔離したAgentで、IDの直接指定は成功し、同じ値の `${AZURE_AI_RAI_POLICY_ID}` 参照は失敗した。この差から、現在のHosted Agent経路では `raiPolicyName` の環境変数が展開されないことを確認した。

RAIポリシーを外さず、デプロイ中だけローカルの `azure.yaml` に環境値を直接指定してHosted Agentを作成した。Agentはactiveで、`rai_config.rai_policy_name` に同じIDが設定された。デプロイ後は `azure.yaml` を `${AZURE_AI_RAI_POLICY_ID}` に戻しており、環境固有IDはソースに残していない。再デプロイでは拡張の修正、または同様の一時展開手順が必要。

初回にWebをAgentより先にデプロイした際、共通postdeployフックはAgentが存在せず失敗した。現在のフックは本番と評価用の両Agentを対象にするため、初回は両方を配置してからロール付与を完了させる。片方だけの先行配置でフックが失敗した場合、Agent配置まで済んだことと、アプリ全体の準備完了を混同しない。

RAIを一時展開した状態での再デプロイは、次のサービスを指定する。基盤の変更がなければ`provision`は繰り返さない。

```powershell
azd deploy agent --environment auc-dev --no-prompt
azd deploy agent-eval --environment auc-dev --no-prompt
# Webの変更がある場合
azd deploy web --environment auc-dev --no-prompt
```

上記の実行後、失敗した場合も含めて`azure.yaml`のRAI指定を環境変数参照へ戻す。環境固有のARM IDをコミットしない。

## 未完了の受け入れ条件

- [x] AzureのWEB_URLでアプリが開き、Hosted AgentのSession Files経由で成果物をダウンロードできる(2026-11-11確認)。
- [x] RAI適用と再デプロイ時のpostdeploy再実行を確認する。
- [ ] IP制限と、Foundry Agent Consumerを外したときの403を実環境で確認する。共有環境の権限は今回取り外していない。
- [ ] デプロイ済みHosted Agentの停止／再開と、共有リソースを残す終了手順を確認する。
- [x] P8の30件の実応答、Foundryの採点、Office検査、レポート、品質ゲートを実装する。失敗を含む結果を保持する。
- [ ] 継続評価は別途導入する。今回有効化していない。
- [ ] P9の承認・Entra IDの実機確認と7つのラボを完成させる。
- [ ] 公開先を決め、Deploy to Azureからアプリまで完成する導線を検証する。
- [x] 評価ロジックを含む通常のビルド・単体テストを成功させる(354件)。
- [ ] GitHubのOIDCを設定して`ci.yml`／`eval.yml`を実行する。`deploy.yml`は未実装。

Gitのremoteは未設定で、リポジトリの作成・公開・pushは行っていない。デモ用Azure基盤とデプロイ済みアプリは保持しており、ACR、Storage、ログなどの費用が発生し得る。

## 成果物保存方式の変更

Blobダウンロードを必要としない構成として、実装ではAgentのHosted Session Filesを成果物の保存先にし、Webの`id-web`がFoundry Agent Consumer権限で同じIDのセッションファイルを取得・削除する。Webの台帳とData Protectionキーはプロセスメモリーのみとし、ACAのwebレプリカは1つに固定する。

新しい会話の開始時にAgentセッションを削除し、ページ終了時にもbest effortで削除する。Hosted Agentのセッションとファイルは削除要求が届かなければ最大30日残る。

2026-11-11に`auc-dev`へ反映した。`azd provision`の事前確認では削除はなく、ACA環境とACRは既存の値をBicepに明記して差分をなくした。その後、Agent、web、siteproxyをデプロイした。Webの環境変数からTable、Blob、Data Protectionの設定が消え、レプリカは1つになった。

クラウドで初めて試したとき、Wordの作成ツールが`internal_error`を返した。原因は次の2つだった。

1. ツールの実行時に`FoundryAgentRequestContext.Current.SessionId`が空になることがあった。プロセス単位の`FoundryEnvironment.SessionId`で補うようにした。
2. AgentのDockerfileが`USER $APP_UID`で動いていたため、基盤が用意する`$HOME` (`/home/session`)に書けず、`UnauthorizedAccessException`になった。公式のC#サンプル(file-tools)と同じく、Agentのイメージだけ既定のユーザーで動かすようにした。webとsiteproxyは非rootのままとした。

当時はツールのロガーが別のサービスプロバイダーにあり、Application Insightsにログが出なかった。現在は共通のツールDIとロガーを使うMCP構成へ移行した。例外時の`error.type`とエラー状態も維持し、例外本文や機微な引数は記録しない。

修正後のクラウドでのスモークテストでは、次のことを確認した。

- `/healthz`と画面が200を返す。
- 要約、Word (36,038バイト)、PowerPoint (37,809バイト)が作られ、ダウンロードしたサイズが一致する。
- 別の利用者からは同じ成果物が404になる。
- 新しい会話を始めると204が返り、その後は元の成果物が404になる。
- `id-web`はFoundry Agent Consumerだけで、自分が作ったセッションのファイルの取得と削除ができる(Foundry Userは付けていない)。

既存のStorage Accountとデータは削除せず、新しい`main.bicep`からStorageモジュールを呼ばない。アプリは使わなくなったが、既存Storageの費用は続く可能性がある。

Session Files API仕様: [Manage hosted agent sessions](https://learn.microsoft.com/azure/foundry/agents/how-to/manage-hosted-sessions)。

## 専用ビューアー・MCPへ移行後の確認

本番v10でブラウザーから要約、Word、PowerPointを順に実行し、同じHosted Sessionの会話を継続できた。Wordは36,570バイト、PowerPointは33,784バイトで、表示されたサイズと取得したファイルのサイズが一致した。「3枚の資料」の指定に対し、実PPTXも表紙・本文・出典の合計3枚だった。別のブラウザーセッションでは両ファイルが404、新しい会話への切り替えは204、その後の旧ファイル取得は404だった。

v8/v9では合計3枚の指定を本文3枚と取り違えたため、v10でMCPの必須引数`totalSlides`と本文枚数の整合検証を追加した。最新の実呼び出しでは不整合を保存前に拒否し、Agentが1回の修正で正しい3枚を作った。説明だけで防げたとは扱わない。

表示時にiframeはなく、ブラウザーの要求先はWebの同一オリジンだけだった。一覧と指定記事の表示まで、v8では5.3秒、最新v10確認時は1.3秒(それぞれ1回の計測)だった。キャッシュ状態も異なり、恒常的な性能や版間の改善率を示すものではない。サイト全体のHTML・JavaScript・CSSは中継せず、最新データAPIへの初回要求やAgentの起動には別に時間がかかる。

確認中にはFoundryのResponses APIが間欠的にHTTP 500を返し、アプリが`agent_error`を表示する試行もあった。Webからの下流呼び出しが500であることをApp Insightsで確認しており、認証や入力検証を緩めて回避していない。後の試行では全操作が成功したが、サービスの一時障害が解消したという保証ではない。評価スクリプトだけが明示的な`session_creation_failed`を限定再試行し、Webは結果不明の生成を自動で再送しない。

MCP SDKと自作スパンがクラウドに届き、Aspire DashboardではWord生成までの実ツリーを確認した。ただし自動操作用のブラウザーでFoundryポータルを開くと追加のサインインが必要だった。ポータル画面そのものの目視確認は未完了であり、クラウド上の実証はApp Insightsの実データとFoundry Evals APIによる。

**本番の自作Agentは本文記録を無効にしているが、Foundryの`responsesapi`側では本文記録が確認された。** 共有Foundryの設定は変更していない。機密情報を入力しないこと。件数・長さだけを調べた実測と注意点は[観測記録](observability.md)に記載した。
