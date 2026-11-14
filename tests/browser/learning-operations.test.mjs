import assert from "node:assert/strict";
import test from "node:test";
import { EXECUTION_LANES, operationForEvent, operationForTool, operationForSpan, operationTarget } from "../../src/UpdatesCowork.Web/wwwroot/js/learning-operations.js";

test("業務5ツールはMCP経由でC#を実行する", () => {
  for (const name of ["search_azure_updates", "get_azure_update", "update_plan", "create_word_report", "create_powerpoint_deck"]) {
    const operation = operationForTool(name);
    assert.equal(operation.kind, "mcp");
    assert.deepEqual(operation.route.slice(0, 3).map(step => step.kind), ["code", "mcp", "code"]);
    assert.ok(operation.resources.length > 0);
  }
  assert.match(operationForTool("get_azure_update").note, /計測していません/);
  assert.match(operationForTool("create_word_report").note, /モデルや Code Interpreter を呼びません/);
});

test("SDKのスキル読み込みと未知ツールをMCPと誤分類しない", () => {
  for (const name of ["load_skill", "read_skill_resource"]) {
    const operation = operationForTool(name);
    assert.equal(operation.kind, "code");
    assert.ok(operation.route.every(step => step.kind === "code"));
  }
  for (const name of ["unknown_tool", "mcp_unknown", "__proto__", "constructor", "", undefined]) {
    assert.equal(operationForTool(name).kind, "other");
    assert.match(operationForTool(name).note, /推定しません/);
  }
});

test("モデル出力と成果物通知を別の実行や呼び出し回数として扱わない", () => {
  assert.equal(operationForEvent("answer").kind, "model");
  assert.match(operationForEvent("answer").note, /呼び出しの開始・終了・回数/);
  assert.equal(operationForEvent("artifact").kind, "code");
  assert.match(operationForEvent("artifact").note, /もう一度生成したわけではありません/);
  assert.equal(operationForEvent("approval").kind, "other");
  assert.match(operationForEvent("approval").note, /実行した記録ではありません/);
  assert.equal(operationForEvent("constructor").kind, "other");
});

test("Agent内の処理とWeb側の登録・ブラウザー表示・モデルサービスを区別する", () => {
  for (const name of ["load_skill", "read_skill_resource", "search_azure_updates", "get_azure_update", "update_plan", "create_word_report", "create_powerpoint_deck"]) {
    const location = operationForTool(name).location;
    assert.match(location.label, /Agent コンテナー/);
    assert.match(location.description, /同じプロセス/);
    assert.match(location.description, /ローカル/);
  }
  assert.match(operationForEvent("artifact").location.description, /Container Apps/);
  assert.match(operationForEvent("artifact").location.description, /Agent とは別/);
  assert.match(operationForEvent("answer").location.description, /実ホスト・リージョンは未計測/);
  assert.match(operationForEvent("plan").location.description, /ブラウザーで進行状況を描画/);
  assert.match(operationForEvent("approval").location.description, /利用者の選択はブラウザー/);
  assert.match(operationForEvent("end").location.description, /ブラウザー側の受信停止/);
  assert.equal(operationForTool("mcp_unknown").location.label, "未確認");
});

test("受信した引数から更新IDと文書名を示し、不完全な引数は未確認とする", () => {
  const update = operationForTool("get_azure_update");
  assert.equal(operationTarget(update, '{"id":"578818"}').label, "Azure Updates · ID 578818");
  assert.match(operationTarget(update).note, /受信後/);
  assert.match(operationTarget(update, '{"id":').note, /省略/);
  assert.match(operationTarget(update, '{"id":"https://example.com"}').note, /特定できません/);
  assert.match(operationTarget(update, "null").note, /特定できません/);
  assert.equal(operationTarget(operationForTool("create_word_report"), '{"report":{"fileName":"報告書"}}').label, "Word · 報告書");
  assert.equal(operationTarget(operationForTool("create_powerpoint_deck"), '{"deck":{"fileName":"更新資料"}}').label, "PowerPoint · 更新資料");
  assert.equal(operationTarget(operationForEvent("artifact"), undefined, { fileName: "report.docx" }).label, "成果物: report.docx");
});

test("実測したモデル、MCP通信、サーバー実装、文書生成を分ける", () => {
  const model = operationForSpan({ name: "model.call", model: "test-model" });
  assert.equal(model.kind, "model");
  assert.equal(model.resource, "test-model");
  assert.match(model.note, /通信・応答の受信/);
  assert.match(model.location.description, /Agent 側/);
  assert.equal(operationForSpan({ name: "model.call" }).resource, "モデル名未受信");
  assert.equal(operationForSpan({ name: "mcp.call" }).kind, "mcp");
  const server = operationForSpan({ name: "mcp.tool.execute", toolName: "get_azure_update" });
  assert.equal(server.kind, "code");
  assert.match(server.resource, /Azure Updates/);
  for (const name of ["tool.execute", "office.word.build", "office.pptx.build", "artifact.save", "azure_updates.get", "azure_updates.search"]) {
    assert.equal(operationForSpan({ name, toolName: "create_word_report" }).kind, "code");
  }
  assert.match(operationForSpan({ name: "office.word.build" }).note, /LLM.*呼びません/);
  assert.equal(operationForEvent("answer-received").kind, "other");
  assert.match(operationForEvent("answer-received").note, /モデル呼び出しとは別/);
  assert.equal(operationForSpan({ name: "unknown" }).kind, "other");
});

test("処理種別と独立した場所別レーンで内部MCPとC#を同じAgent列に揃える", () => {
  for (const name of ["agent.turn", "tool.execute", "mcp.call", "mcp.tool.execute", "azure_updates.get",
    "azure_updates.search", "office.word.build", "office.pptx.build", "artifact.save"]) {
    assert.equal(operationForSpan({ name }).location.id, "agent", name);
  }
  for (const name of ["load_skill", "read_skill_resource", "get_azure_update", "search_azure_updates",
    "update_plan", "create_word_report", "create_powerpoint_deck"]) {
    assert.equal(operationForTool(name).location.id, "agent", name);
  }
  assert.equal(operationForSpan({ name: "model.call" }).location.id, "models");
  assert.equal(operationForSpan({ name: "content_safety.shield_prompt" }).location.id, "content-safety");
  assert.equal(operationForEvent("artifact").location.id, "web");
  for (const name of ["request", "plan", "approval", "answer", "answer-received", "end"]) {
    assert.equal(operationForEvent(name).location.id, "browser", name);
  }
  assert.equal(operationForTool("mcp_unknown").location.id, "unknown");
  assert.equal(operationForSpan({ name: "unregistered" }).location.id, "unknown");
  assert.deepEqual(EXECUTION_LANES.filter(lane => !lane.optional).map(lane => lane.id),
    ["browser", "web", "agent", "models"]);
});

test("Webの実測はContainer Apps列に置き、Agent内部の処理と混同しない", () => {
  for (const name of ["web.request.prepare", "chat.turn", "agent.conversation.create", "web.agent.invoke",
    "web.sse.relay", "web.artifact.register"]) {
    const operation = operationForSpan({ name, source: "web" });
    assert.equal(operation.location.id, "web");
    assert.match(operation.location.description, /Container Apps/);
    assert.ok(operation.title);
    assert.equal(operationForSpan({ name }).location.id, "unknown");
  }
  assert.match(operationForSpan({ name: "web.sse.relay", source: "web" }).note, /待機/);
  assert.match(operationForSpan({ name: "web.agent.invoke", source: "web" }).note, /Web 側/);
  assert.equal(operationForSpan({ name: "model.call", source: "web" }).location.id, "unknown");
});
