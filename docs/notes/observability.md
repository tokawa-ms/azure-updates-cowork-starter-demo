# MCPとトレースを見る

対象は、このサンプルを動かす開発者です。更新の取得から資料作成までを、ローカルのAspire DashboardとFoundryに接続したApplication Insightsで追跡します。確認日: 2026-11-12。

## 実際に呼んでいるMCP

Agentの業務5機能は`search_azure_updates`、`get_azure_update`、`update_plan`、`create_word_report`、`create_powerpoint_deck`です。同一コンテナーのloopback・動的ポートでStreamable HTTP MCPを起動し、MCPクライアントが発見したツールをモデルへ渡します。MCPらしいスパン名だけを付けた直接呼び出しではありません。

MCPは外部公開しません。起動時の秘密値、接続元、Origin、要求サイズ、セッションメタデータを検証します。成果物の承認はMCPクライアント側で行い、セッションIDはモデルの引数ではなく要求メタデータで運びます。`load_skill`と`read_skill_resource`はMAF内部のツールで、MCPの5機能とは区別します。

## ローカルで見る

1. [README](../../README.md)の手順でAspire Dashboard、Agent、Webを起動します。
2. 左で更新を選び、右のチャットからWordの作成を依頼します。
3. `http://localhost:18888` のTracesで該当のAgent実行を開きます。`execute_tool create_word_report`を展開します。

実際のWord生成で、次の親子関係を確認しました。

```text
invoke_agent
  execute_tool create_word_report
    HTTP POST
      POST /mcp/
        tools/call create_word_report
          mcp.tool.execute
            office.word.build
            artifact.save
```

ローカル実証のtrace IDは`<trace ID>`。45スパン、深さ6、約30秒、生成Wordは36,383バイトでした。これはAgentを直接呼んだ記録です。Dashboardの記憶領域は永続化していないため、再起動後に同じtraceが残る保証はありません。

SDK 2.4.0のActivitySourceは`Experimental.ModelContextProtocol`です。`ModelContextProtocol`だけを購読してもSDKのスパンは出ません。SDKの`tools/call`、自作`mcp.tool.execute`、HTTPのスパンはそれぞれ異なる階層です。

## FoundryとApplication Insightsで見る

FoundryプロジェクトのAgentsで`updates-cowork`または`updates-cowork-eval`を選び、Tracesを開きます。時間範囲を実行時刻へ合わせ、`invoke_agent`から業務ツール、HTTP/MCP、Officeまで展開します。接続先App Insightsの読み取り権限が必要です。

クラウドでは、WebからWord生成までが同じ`operation_Id`になることを確認しました。実証IDは`<trace ID>`。MCP SDKの`tools/call`と自作スパンもAzureへ到着しています。

本番v10のPowerPoint生成は`<trace ID>`で78スパンを確認しました。最初の`tools/call create_powerpoint_deck`と`mcp.tool.execute`は枚数の入力不整合でエラーになり、1回の修正後は成功しています。成功した呼び出しに`office.pptx.build`と`artifact.save`がつながり、ダウンロードした実ファイルは指定どおり合計3枚でした。入力エラーと回復を隠さず、同じトレースで追えます。

確認用のブラウザーではFoundryへの追加サインインが必要だったため、ポータル画面の目視は未完了です。Azureの実証はApp Insightsの取得データによるもので、ポータルの全表示が確認済みという意味ではありません。

Logsで特定のtraceの構造だけを見る例:

```kusto
union requests, dependencies
| where timestamp > ago(1d)
| where operation_Id == "<trace ID>"
| project timestamp, cloud_RoleName, name, id, operation_ParentId, duration, success
| order by timestamp asc
```

MCPが届いているかを確認する例:

```kusto
union requests, dependencies
| where timestamp > ago(2h)
| where cloud_RoleName == "UpdatesCowork.Agent"
| where name startswith "tools/call" or name == "mcp.tool.execute"
| summarize calls=count(), failures=countif(success == false), p95_ms=percentile(duration, 95) by name
```

自作Agentの`cloud_RoleName`は`UpdatesCowork.Agent`です。`updates-cowork`で絞ると該当しません。本番と評価は、同じ`operation_Id`に属する`invoke_agent updates-cowork` / `invoke_agent updates-cowork-eval`で区別します。

Q4はW3Cのトレース連携を実データで確認済みです。Q5の`x-client-trace-id`代替ヘッダー単独での伝播は未確認であり、Q4の成功をその証拠にはしません。応答IDと会話IDによる関連付けも維持しています。

## 本文の記録範囲と注意

| 発生元 | 設定・実測 |
|---|---|
| 本番の自作Agent | `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=false`。調査した144スパンの入出力本文は0件 |
| 本番Web | 調査した19スパンの入出力本文は0件 |
| 本番Foundry `responsesapi` | 調査した26スパン中20件に入出力本文あり。最大入力長33,278、最大出力長2,761 |
| 評価の自作Agent | 固定データ用だけ`true`。調査した1,426スパン中168件に本文あり |
| 評価Foundry `responsesapi` | 調査した262スパン中230件に本文あり |

上記は2時間の観測範囲で、本文値を取得せず件数と長さだけを集計した結果です。永続的な総件数ではありません。**アプリ側の`false`は、Foundry基盤側で本文を記録しない保証にはなりません。機密情報を入力しないでください。** 共有Foundryの設定は変更していません。基盤側の保存方針、保持期間、閲覧権限の変更は別途管理者と確認する必要があります。

自作の属性にはCookie、トークン、メールアドレスを入れず、利用者キーはハッシュ値です。トレース本文を含むJSON、録画、スクリーンショットを公開リポジトリへ追加しないでください。固定データ評価を実行できることと、本番の継続評価を有効にしてよいことは別の判断です。
