# GitHub Copilotへの指示(Azure Updates Cowork)

## このリポジトリについて

Azureの更新情報サイトをリバースプロキシで表示し、その横のチャットからMicrosoft FoundryのHosted Agent (C#、Microsoft Agent Framework)を呼んで、表示中の更新の要約、Word、PowerPointを作る学習用サンプルです。WebアプリはAzure Container AppsのExpressで動きます。

- 設計の基準は`docs/design.md`、作業の順序と受け入れ条件は`docs/implementation-plan.md`です。
- 依頼されたフェーズの範囲だけを実装してください。設計と違う実装が必要になったら、勝手に変えずにPRの説明で理由と案を示してください。

## 技術の決まり

- .NET 10 (`net10.0`)、C#の最新言語版、`Nullable`有効、警告はエラー扱い。
- パッケージの版は`Directory.Packages.props`だけに書きます。新しいパッケージを足すときは、`docs/design.md`の4.3節の表も更新してください。
- Web APIはMinimal API。DTOは`record`。JSONは`System.Text.Json`のソース生成(`AucJsonContext`)を使います。
- 画面はビルド工程を持たない静的ファイル(HTML、CSS、ESモジュール)です。フレームワークやCDNを足さないでください。
- 画面の文言とコードコメントは日本語、識別子は英語にします。

## プレビューのAPIの扱い

- Hosted Agentのホスティング、`Azure.AI.AgentServer.*`、`Azure.AI.Projects` 3.xはプレビューで、APIが頻繁に変わります。メソッド名や引数を推測で書かないでください。
- 先に、固定した版のパッケージのXMLドキュメント、公式サンプル、Microsoft Learn (MCPサーバー`microsoft-learn`)で確かめてください。
- 設計書に[要確認]とある箇所は、確かめた結果をPRの説明に「Q番号: 結果」の形で書いてください(設計書14.2節)。

## セキュリティの決まり

- APIキー、接続文字列の鍵、パスワードをコードと設定ファイルに書かないでください。Azureの認証は`TokenCredential` (Azureでは`ManagedIdentityCredential`、ローカルでは`DefaultAzureCredential`)だけを使います。
- エージェントのツールが受け取る引数は信用せず、必ず検証してください。URLを受け取って取得するツールは作らないでください。
- 外部から取得した文字列は`<untrusted_content>`で囲んでモデルに渡します。
- 画面でHTMLを表示するときは、必ず`DOMPurify`を通します。
- ログやスパンに、Cookieの値、トークン、メールアドレスを記録しないでください。利用者はハッシュ化した利用者キーで表します。

## 観測の決まり

- ActivitySourceとメーターの名前は`UpdatesCowork.Web`、`UpdatesCowork.SiteProxy`、`UpdatesCowork.Agent`です。スパン名、属性名、メトリック名は`docs/design.md`の8.3節に従い、`UpdatesCowork.Shared`の`TelemetryNames`の定数を使ってください。
- 新しい外部呼び出しを足したら、スパンが出ることをAspire Dashboardで確かめてください。

## テストの決まり

- テストはxUnit v3。単体テストはネットワークに出ません(`HttpMessageHandler`を差し替えます)。
- モデルを呼ぶ評価のテストには`[Trait("Category", "Eval")]`を付け、通常のCIでは実行しません。
- 各フェーズの受け入れ条件を、PRの説明にチェックリストとして写してください。

## よく使うコマンド

```bash
dotnet build
dotnet test --filter Category!=Eval
docker compose -f tools/otel/compose.yaml up -d          # Aspire Dashboard (http://localhost:18888)
dotnet run --project src/UpdatesCowork.Agent             # http://localhost:8088
dotnet run --project src/UpdatesCowork.SiteProxy         # https://localhost:7102
dotnet run --project src/UpdatesCowork.Web               # https://localhost:7101
azd up
azd deploy agent --preview --no-prompt                   # azure.yaml の事前確認
```
