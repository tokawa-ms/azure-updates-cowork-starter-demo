# ローカル統合と観測の記録

日本語 (プライマリ) | [English](local-integration-en.md)

確認日: 2026-11-11。実モデルは既存デプロイ `gpt-5.4-mini`。更新情報はリポジトリの固定データを使った。Azureに新たなモデルは作成していない。

これはMCP移行前の記録です。現在のMCP構成とAzureでのQ4の確認結果は[観測記録](observability.md)を参照してください。以下の計装構成・未確認事項は、記録当時のものです。

## 確認したAPI

- Q1: 固定した Foundry.Hosting パッケージに `builder.AddFoundryResponses(agent)` と `app.MapFoundryResponses()` が存在する。XMLドキュメント、ビルド、`/readiness` の200応答で確認した。
- Q2: `AIProjectClient.AsAIAgent(ChatClientAgentOptions, ... loggerFactory, services)` が存在し、現在時刻コンテキスト、ローカルスキル、関数ツールを組み合わせて動いた。
- Q3: ローカルの会話APIと `previous_response_id` を確認した。BFFの開発設定は `PreviousResponseId` を採用し、要約後に同じ会話でWord、PowerPointを依頼できた。
- Q13: Prompt Shields は `2024-09-01` を使用する。HTTP差し替えで Off／Annotate／Block、検査失敗、不正応答、長文の分割を確認した。実サービスでの攻撃検出精度を検証した結果ではない。

使用パッケージは `Directory.Packages.props` が正本。ローカルの `DefaultAzureCredential` は `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` でCLIの資格情報に限定した。`dev` では別のキャッシュ済みIDが選ばれ403になったため、権限追加ではなく資格情報選択で解決した。

## 会話と資料

更新ID `559242` の要約で `load_skill` と `get_azure_update` が実際に呼ばれた。同じCookie・会話でWordとPowerPointを作成し、BFFからダウンロードできた。別Cookieで同じ成果物IDを要求すると404になった。

生成したWordはデスクトップ版Wordで開き、PDFにして1ページを目視確認した。PowerPointはデスクトップ版で開き、16:9・4枚をPNGにして全枚を確認した。日本語の欠落、はみ出し、重なりは見られなかった。PowerPointのテキスト高さも確認した。検証ファイルと画像はセッション用領域に保存し、ソースリポジトリには含めていない。

Local成果物の `yyyy/MM/dd/art_ID/file` 形式をBFFが受け付けるよう修正した。パストラバーサルと末尾改行を許可せず、所有者台帳との照合を維持している。

## 入力・ログの境界

ページのタイトルを含む外部文字列はHTMLエンコードし、`<untrusted_content>` 内に置く。エラー応答本文や通信例外の本文はログに出さず、状態コード・例外型だけを記録する。ツール上限は1ターン12回で、キャンセルは伝播する。

SSEの録画そのものはUTF-8で正常だった。PowerShellで応答の `.Content` を読むとcharsetの解釈で文字化けしたため、検証では生バイトをUTF-8で明示的に復号した。大量のSSEやCLIデバッグ出力は端末へ流さずファイルへ保存する。

## トレース

Aspire Dashboardで自分のBFF応答のtraceIdだけを開き、次の同一トレース内の親子関係を確認した。

```text
POST /api/chat
  chat.turn
    HTTP POST
      POST /responses
        invoke_agent updates-cowork
          chat gpt-5.4-mini
          execute_tool load_skill
          execute_tool get_azure_update
            azure_updates.get
          execute_tool read_skill_resource
          execute_tool create_word_report
            artifact.save
```

ホストがagent/chat/tool計測を提供するため、AgentFactoryに追加の `UseOpenTelemetry` ラッパーは重ねていない。各アプリには識別可能な既定のservice.nameを設定した。共有のDashboardには他アプリのデータも届き得るので、全ログを出力せず自分のtraceIdで絞る。

Q4・Q5のAzure Hosted Agentでの検証、Foundryポータル、KQL、フィードバックとの結合は未完了。ローカルでの成功をAzure側の伝播確認の代わりにしていない。
