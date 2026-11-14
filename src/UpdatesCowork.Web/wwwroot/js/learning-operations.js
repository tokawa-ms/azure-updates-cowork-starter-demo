export const OPERATION_TYPES = Object.freeze([
  { id: "model", label: "モデル", badge: "モデル" },
  { id: "code", label: "コード実行", badge: "C# コード" },
  { id: "mcp", label: "MCP 呼び出し", badge: "MCP → C#" },
  { id: "other", label: "その他・未確認", badge: "その他" },
]);

export const EXECUTION_LANES = Object.freeze([
  { id: "browser", label: "ブラウザー", description: "画面・受信イベント" },
  { id: "web", label: "Container Apps", description: "Web / BFF" },
  { id: "agent", label: "Foundry Hosted Agent", description: "C#・内部 MCP" },
  { id: "models", label: "Foundry Models", description: "モデルの呼び出し先" },
  { id: "content-safety", label: "Azure AI Content Safety", description: "安全性検査の呼び出し先", optional: true },
  { id: "unknown", label: "未確認", description: "実行場所未確認", optional: true },
]);

const MCP_ROUTE = [
  { kind: "code", label: "Hosted Agent" },
  { kind: "mcp", label: "内部 MCP（同一コンテナー）" },
  { kind: "code", label: "C# ツール実装" },
];

const UPDATE_RESOURCES = [
  { name: "Azure Updates の更新情報", access: "読み取り（API または固定データ）" },
];
const OFFICE_RESOURCES = [
  { name: "Office テンプレート", access: "読み取り" },
  { name: "Word / PowerPoint 文書", access: "C# / Open XML による生成・検証" },
  { name: "成果物の保存領域", access: "書き込み（Session Files またはローカル）" },
];
const AGENT_LOCATION = Object.freeze({
  id: "agent",
  label: "Agent コンテナー内",
  description: "Azure では Foundry Hosted Agent のコンテナー内、ローカルでは Agent プロセス内です。内部 MCP サーバーと C# ツールも同じプロセスで動きます。",
});
const WEB_LOCATION = Object.freeze({
  id: "web",
  label: "Web サーバー",
  description: "Web アプリのサーバー側です。Azure では Azure Container Apps、ローカルでは Web プロセスで動きます。Agent とは別の実行環境です。",
});
const WEB_SPAN_OPERATIONS = Object.freeze({
  "web.request.prepare": {
    title: "要求の受付・検証", resource: "Web API / 会話台帳",
    action: "利用者キー・入力・会話の検証と準備",
    note: "Web API のハンドラー内の実測です。認証ミドルウェアや静的ファイルの配信時間は含みません。SSE 開始前に拒否した要求の計測は監視側に残り、学習グラフには送信しません。",
  },
  "chat.turn": {
    title: "Web 応答の処理", resource: "Web API / 応答ストリーム",
    action: "Agent の実行・受信イベントの処理・会話台帳の更新",
    note: "Web 側の応答処理全体の経過時間です。Agent の応答待ちを含み、Web の CPU 使用時間ではありません。",
  },
  "agent.conversation.create": {
    title: "Agent の会話の作成", resource: "Hosted Agent の会話 API",
    action: "会話の作成要求・応答の受信",
    note: "Web 側の API クライアントの実測です。既存の会話を再利用した場合、この処理はありません。",
  },
  "web.agent.invoke": {
    title: "Hosted Agent の呼び出し", resource: "Hosted Agent / Responses API",
    action: "要求の送信・Agent 応答ストリームの受信",
    note: "Web 側の呼び出しから受信終了までの経過時間です。通信・Agent の応答待ち・中継処理を含み、Agent 内部の実行時間とは区別します。",
  },
  "web.sse.relay": {
    title: "SSE の変換・中継", resource: "ブラウザー向け SSE",
    action: "Agent イベントの検証・変換と送信キューへの投入",
    note: "最初の Agent イベント受信から受信終了までを計測します。次のイベントの待機や成果物登録も含み、変換だけの CPU 時間やブラウザーへの到達時刻ではありません。",
  },
  "web.artifact.register": {
    title: "成果物台帳への保存", resource: "Web の成果物台帳",
    action: "成果物の所有者・保存場所・ダウンロード情報を登録",
    note: "文書の生成・ファイル保存とは別の Web 側の台帳登録です。文書やモデルをもう一度生成する処理ではありません。",
  },
});
const SKILL_OPERATION = Object.freeze({
  kind: "code",
  location: AGENT_LOCATION,
  resource: "同梱スキル（Markdown）",
  action: "手順書・参照資料の読み取り",
  route: [{ kind: "code", label: "Agent Skills SDK" }, { kind: "code", label: "コンテナー内の Skills" }],
  resources: [{ name: "Skills 内の Markdown・参照資料", access: "読み取り" }],
  note: "SDK 内のコード処理です。スキルを読む操作自体はモデル呼び出しでも MCP 呼び出しでもありません。",
});
const UNKNOWN_TOOL = Object.freeze({
  kind: "other",
  location: { id: "unknown", label: "未確認", description: "実行場所は未確認です。ツール名からコンテナーや外部サービスを推定しません。" },
  resource: "対象リソース未確認",
  action: "ツールの実行（経路未確認）",
  route: [],
  resources: [],
  note: "このツールの実装経路は登録されていません。名前から MCP やモデル呼び出しとは推定しません。引数・結果を確認してください。",
});

// 種別と経路は確認済みの実装構成。内部の各 API 呼び出しを計測した記録ではない。
const TOOL_OPERATIONS = Object.freeze({
  load_skill: SKILL_OPERATION,
  read_skill_resource: SKILL_OPERATION,
  search_azure_updates: {
    kind: "mcp",
    location: AGENT_LOCATION,
    resource: "Azure Updates の更新情報",
    action: "検索条件を検証し、更新情報を検索",
    route: [...MCP_ROUTE, { kind: "other", label: "Azure Updates / 固定データ" }],
    resources: UPDATE_RESOURCES,
    note: "MCP は呼び出し経路で、検索処理は C# です。取得先は設定により API または固定データになります。",
  },
  get_azure_update: {
    kind: "mcp",
    location: AGENT_LOCATION,
    resource: "Azure Updates の更新情報",
    argumentTarget: "update",
    action: "指定 ID の本文・提供状況を読み取り",
    route: [...MCP_ROUTE, { kind: "other", label: "Azure Updates / 固定データ" }],
    resources: [...UPDATE_RESOURCES, { name: "Azure AI Content Safety", access: "本文の安全性検査（設定に依存）" }],
    note: "C# が本文を取得し、安全性を検査します。API / 固定データの選択と Content Safety の個別呼び出しは、この SSE では計測していません。",
  },
  update_plan: {
    kind: "mcp",
    location: AGENT_LOCATION,
    resource: "会話の実行計画",
    action: "手順と状態を検証して返却",
    route: [...MCP_ROUTE, { kind: "other", label: "Web の進行状況表示" }],
    resources: [{ name: "実行計画の JSON", access: "検証・返却（データベースへの保存なし）" }],
    note: "このツールも内部 MCP 経由です。C# が手順を検証し、返された計画を Web が表示します。",
  },
  create_word_report: {
    kind: "mcp",
    location: AGENT_LOCATION,
    resource: "Word 文書・成果物保存領域",
    argumentTarget: "word",
    action: "Word 文書を生成・検証して保存",
    route: [...MCP_ROUTE, { kind: "code", label: "Open XML → 成果物保存領域" }],
    resources: OFFICE_RESOURCES,
    note: "渡された構成を C# / Open XML で文書化します。このツール自体はモデルや Code Interpreter を呼びません。保存先は設定に依存します。",
  },
  create_powerpoint_deck: {
    kind: "mcp",
    location: AGENT_LOCATION,
    resource: "PowerPoint 文書・成果物保存領域",
    argumentTarget: "powerpoint",
    action: "スライドを生成・検証して保存",
    route: [...MCP_ROUTE, { kind: "code", label: "Open XML → 成果物保存領域" }],
    resources: OFFICE_RESOURCES,
    note: "渡された構成を C# / Open XML でスライドにします。このツール自体はモデルや Code Interpreter を呼びません。保存先は設定に依存します。",
  },
});

const EVENT_OPERATIONS = Object.freeze({
  request: {
    kind: "other",
    location: {
      id: "browser",
      label: "ブラウザー → Web → Agent",
      description: "ブラウザーから Web API（Azure では Container Apps）を経由して Hosted Agent に依頼を送ります。ローカルでは Web と Agent の各プロセスに送ります。",
    },
    resource: "Web API / Hosted Agent",
    action: "依頼・承認結果の送信",
    route: [{ kind: "other", label: "ブラウザー" }, { kind: "code", label: "Web API（BFF）" }, { kind: "code", label: "Hosted Agent" }],
    note: "要求の開始を示します。モデル呼び出しの開始を計測したイベントではありません。",
  },
  plan: {
    kind: "other",
    location: {
      id: "browser",
      label: "Web → ブラウザー",
      description: "Web サーバーで受信イベントを変換し、利用者のブラウザーで進行状況を描画します。計画の検証を行う update_plan ツールとは別の表示処理です。",
    },
    resource: "Web の進行状況表示",
    action: "受信した実行計画を画面に反映",
    route: [{ kind: "code", label: "Web のイベント変換" }, { kind: "other", label: "ブラウザーの進行状況" }],
  },
  approval: {
    kind: "other",
    location: {
      id: "browser",
      label: "Agent → Web → ブラウザー",
      description: "Hosted Agent の承認要求を Web 経由でブラウザーに表示します。利用者の選択はブラウザーで受け付け、Web から Agent へ送ります。",
    },
    resource: "利用者の承認操作",
    action: "ツール実行の許可・却下を確認",
    route: [{ kind: "code", label: "Hosted Agent" }, { kind: "other", label: "Web / 利用者" }],
    note: "承認の要求であり、承認対象のツールを実行した記録ではありません。",
  },
  artifact: {
    kind: "code",
    location: WEB_LOCATION,
    resource: "Web の成果物台帳",
    action: "成果物の登録・ダウンロード先の通知",
    route: [{ kind: "code", label: "Agent の成果物" }, { kind: "code", label: "Web の台帳" }, { kind: "other", label: "ダウンロードカード" }],
    note: "生成ツールが返した成果物を Web に登録したイベントです。ここで文書をもう一度生成したわけではありません。",
  },
  answer: {
    kind: "model",
    location: {
      id: "browser",
      label: "ブラウザー（モデル出力の受信）",
      description: "Foundry のモデルが生成した回答をブラウザーに表示します。このノードは出力の受信だけを観測しており、モデルサービス内の実行や実ホスト・リージョンは未計測です。",
    },
    resource: "Foundry のモデル（出力のみ観測）",
    action: "生成された回答テキストを受信",
    route: [{ kind: "model", label: "モデル" }, { kind: "code", label: "Hosted Agent / Web" }, { kind: "other", label: "チャット欄" }],
    note: "回答出力の受信を観測しています。モデル呼び出しの開始・終了・回数・デプロイ名はこのイベントでは確認できません。",
  },
  "answer-received": {
    kind: "other",
    location: { id: "browser", label: "Web → ブラウザー", description: "Hosted Agent の回答テキストを Web 経由で受信し、ブラウザーのチャット欄に描画します。" },
    resource: "チャット欄",
    action: "回答テキストの受信・描画",
    route: [{ kind: "code", label: "Hosted Agent / Web" }, { kind: "other", label: "ブラウザー" }],
    note: "回答テキストの受信は、モデル呼び出しとは別のイベントです。実際の呼び出し回数・時間は「実測」のモデルノードで確認できます。",
  },
  end: {
    kind: "other",
    location: {
      id: "browser",
      label: "Web / ブラウザー",
      description: "Web からの終了・エラーイベント、またはブラウザー側の受信停止を画面に反映します。サーバー側の各処理の停止を保証するものではありません。",
    },
    resource: "Web の応答ストリーム",
    action: "応答の終了状態を画面に反映",
    route: [{ kind: "code", label: "Web のイベント処理" }, { kind: "other", label: "ブラウザー" }],
  },
});

export function operationForTool(name) {
  return Object.hasOwn(TOOL_OPERATIONS, name) ? TOOL_OPERATIONS[name] : UNKNOWN_TOOL;
}

export function operationForEvent(name) {
  return Object.hasOwn(EVENT_OPERATIONS, name) ? EVENT_OPERATIONS[name] : UNKNOWN_TOOL;
}

export function operationForSpan(span) {
  if (span.source === "web") {
    return Object.hasOwn(WEB_SPAN_OPERATIONS, span.name)
      ? { kind: "code", location: WEB_LOCATION, route: [], resources: [], ...WEB_SPAN_OPERATIONS[span.name] }
      : { ...UNKNOWN_TOOL, title: "未登録の Web 処理" };
  }
  const tool = operationForTool(span.toolName);
  const base = { kind: "code", location: AGENT_LOCATION, route: [], resources: [] };
  switch (span.name) {
    case "agent.turn":
      return { ...base, kind: "other", title: "エージェントの実行", resource: "Hosted Agent", action: "1 回の依頼の実行" };
    case "model.call":
      return {
        ...base, kind: "model", title: "モデル呼び出し", resource: span.model ?? "モデル名未受信",
        location: { id: "models", label: "Agent → Foundry モデル", description: "Agent 内の IChatClient から Foundry のモデルデプロイを呼び出します。呼び出し先の Foundry Models 列に配置しますが、計測点は Agent 側で、実ホスト・リージョンは未計測です。" },
        action: "モデルへの要求・応答の受信",
        note: "1 ノードは実際のモデルクライアント呼び出し 1 回です。所要時間には通信・応答の受信を含み、モデルの推論時間だけを示すものではありません。",
      };
    case "tool.execute":
      return { ...tool, kind: "code", location: AGENT_LOCATION, title: "ツールの実行", route: [], note: "Agent Framework のツール実行を計測しています。MCP 通信・内部処理は別の実測ノードです。" };
    case "mcp.call":
      return { ...base, kind: "mcp", title: "MCP 呼び出し", resource: span.toolName ?? "内部 MCP",
        action: "同一コンテナー内の MCP サーバーへの要求・応答",
        note: "MCP クライアント側の計測です。ツール実装は子処理の「MCP サーバーの実行」に表示します。" };
    case "mcp.tool.execute":
      return { ...tool, ...base, title: "MCP サーバーの実行", resource: tool.resource,
        action: tool.action, note: "内部 MCP サーバーで C# ツールを実行した計測です。MCP 自体が LLM を呼ぶという意味ではありません。" };
    case "azure_updates.get":
    case "azure_updates.search":
      return { ...base, title: span.name.endsWith(".get") ? "更新情報の取得" : "更新情報の検索",
        resource: "Azure Updates / キャッシュ / 固定データ", action: "更新情報クライアントによる読み取り",
        resources: UPDATE_RESOURCES,
        note: "API・キャッシュ・固定データの読み取りを含むクライアント処理です。個別 HTTP 通信の時間とは区別します。" };
    case "content_safety.shield_prompt":
      return { ...base, title: "本文の安全性検査", resource: "Azure AI Content Safety", action: "Prompt Shields による外部本文の検査",
        location: { id: "content-safety", label: "Agent → Content Safety", description: "Agent 側の検査クライアントを計測します。呼び出し先の Azure AI Content Safety 列に配置しますが、検査サービス内の実行時間ではありません。モデル呼び出しの計測とは別です。" } };
    case "office.word.build":
    case "office.pptx.build":
      return { ...base, title: span.name === "office.word.build" ? "Word の生成" : "PowerPoint の生成",
        resource: "C# / Open XML", action: "文書の生成・検証",
        note: "C# コードの実行です。この生成処理自体では LLM や Code Interpreter を呼びません。" };
    case "artifact.save":
      return { ...base, title: "成果物の保存", resource: "Session Files / ローカル保存領域", action: "文書ファイルの書き込み",
        note: "保存処理の実測です。Web の成果物台帳への登録とは別の処理です。保存先は Agent の設定に依存します。" };
    default:
      return { ...UNKNOWN_TOOL, title: "未登録の実測処理" };
  }
}

export function operationTarget(operation, argumentsText, artifact) {
  if (artifact?.fileName) {
    return { label: `成果物: ${String(artifact.fileName).slice(0, 200)}` };
  }
  if (!operation.argumentTarget) {
    return { label: operation.resource };
  }
  const fallback = { label: operation.resource, note: "対象の詳細は引数の受信後に表示します。" };
  if (typeof argumentsText !== "string" || argumentsText.length === 0) {
    return fallback;
  }
  let args;
  try {
    args = JSON.parse(argumentsText);
  } catch (error) {
    if (!(error instanceof SyntaxError)) {
      throw error;
    }
    return { label: operation.resource, note: "引数が省略されているか JSON 形式でないため、対象の詳細は特定できません。" };
  }
  if (operation.argumentTarget === "update" && typeof args?.id === "string" && /^[A-Za-z0-9-]{1,200}$/.test(args.id)) {
    return { label: `Azure Updates · ID ${args.id}` };
  }
  const fileName = operation.argumentTarget === "word" ? args?.report?.fileName : args?.deck?.fileName;
  if (operation.argumentTarget !== "update" && typeof fileName === "string" && fileName.trim()) {
    return { label: `${operation.argumentTarget === "word" ? "Word" : "PowerPoint"} · ${fileName.slice(0, 100)}` };
  }
  return { label: operation.resource, note: "引数から対象を特定できません。引数・結果の内容を確認してください。" };
}
