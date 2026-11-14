import { createArtifactCard, el, prettyJson, renderPlan, toolNoun } from "./render.js";
import { EXECUTION_LANES, OPERATION_TYPES, operationForEvent, operationForTool, operationForSpan, operationTarget } from "./learning-operations.js";

const STATUS_LABELS = Object.freeze({
  running: "実行中",
  completed: "完了",
  failed: "失敗",
  waiting: "承認待ち",
  incomplete: "中断",
  stopped: "受信停止",
  unknown: "完了未確認",
  unavailable: "承認機能が無効",
  cancelled: "処理の取消",
});

/** 実際に受信したイベントだけを、依存関係を推測せず受信順に並べる。 */
export function createLearningFlow(container) {
  const summary = el("p", "flow-summary", "まだ実行はありません。");
  summary.setAttribute("role", "status");
  const layout = el("div", "flow-layout");
  const measurements = el("p", "flow-measurements");
  measurements.setAttribute("role", "status");
  const stage = el("div", "flow-stage");
  const lanes = el("div", "flow-lanes");
  lanes.setAttribute("role", "list");
  lanes.setAttribute("aria-label", "実行場所（列）");
  const legend = el("ul", "flow-legend");
  legend.setAttribute("aria-label", "処理の種類（色の凡例）");
  for (const type of OPERATION_TYPES) {
    legend.append(el("li", "flow-kind-" + type.id, type.badge));
  }
  const list = el("ol", "flow-list");
  list.setAttribute("aria-label", "エージェントの実行フロー（受信順）");
  const detail = el("section", "flow-detail");
  detail.setAttribute("aria-label", "選択した処理の詳細");
  stage.append(lanes, list);
  layout.append(stage, detail);
  container.append(legend, summary, measurements, layout);

  const nodes = new Map();
  const spans = new Map();
  let activeLanes = [];
  let traceStatus = null;
  let webTraceStatus = null;
  let droppedEvents = 0;
  let webDroppedEvents = 0;
  let selected = null;
  let pinned = false;
  let ended = false;

  function showDetail(node) {
    const operation = node.operation;
    const type = OPERATION_TYPES.find(type => type.id === operation.kind);
    const heading = el("h3", "flow-detail-title", node.title);
    const status = el("p", "flow-detail-status", STATUS_LABELS[node.status]);
    const category = el("p", "flow-detail-kind flow-kind-" + operation.kind, type.badge);
    const facts = el("dl", "flow-operation-facts");
    for (const [label, value] of [["実行場所", operation.location.description], ["対象", node.target.label], ["操作", operation.action]]) {
      const row = el("div");
      row.append(el("dt", null, label), el("dd", null, value));
      facts.append(row);
    }
    const content = [category, heading, status, facts];
    if (node.span) {
      const span = node.span;
      const source = span.source === "web" ? "Web" : "Agent";
      const timing = el("dl", "flow-operation-facts flow-measured-facts");
      const fields = [
        ["計測", `${source} 側の実測（実行場所の説明は構成情報）`],
        ["開始（UTC）", span.startedAt],
        ["終了（UTC）", span.endedAt ?? "未受信"],
        ["所要時間", duration(span.durationMs)],
        ["モデル", span.model],
        ["入力トークン", span.name === "model.call" ? span.inputTokens ?? "未受信" : undefined],
        ["出力トークン", span.name === "model.call" ? span.outputTokens ?? "未受信" : undefined],
        ["キャッシュ", typeof span.cacheHit === "boolean" ? span.cacheHit ? "ヒット" : "ミス" : undefined],
        [`${source} Trace ID`, span.traceId], ["Span ID", span.spanId], ["ツール呼び出し ID", span.callId],
      ];
      for (const [label, value] of fields) {
        if (value !== undefined) {
          const row = el("div");
          row.append(el("dt", null, label), el("dd", null, String(value)));
          timing.append(row);
        }
      }
      content.push(timing, el("h4", "flow-detail-label", "実測した親子関係"));
      if (span.parentSpanId) {
        content.push(relatedSpan(span.parentSpanId, "親", span));
      } else {
        content.push(el("p", "flow-observation-note", "計測対象の親処理はありません。"));
      }
      for (const child of spans.values()) {
        if (child.span.parentSpanId === span.spanId && spanKey({ ...child.span, spanId: span.spanId }) === spanKey(span)) {
          content.push(relatedSpan(child.span.spanId, "子", span));
        }
      }
      content.push(el("p", "flow-observation-note", "親子関係は計測コンテキストに基づきます。図の矢印は受信順であり、依存関係ではありません。親子の時間は重なるため合算しません。"));
    } else {
      content.push(el("p", "flow-observation-note", "受信イベントによる表示です。このノード自体の開始時刻・所要時間は未計測です。"));
    }
    if (node.target.note) {
      content.push(el("p", "flow-observation-note", node.target.note));
    }
    if (operation.route.length > 0) {
      const route = el("ol", "flow-route");
      route.setAttribute("aria-label", "実装経路（構成に基づく説明）");
      for (const step of operation.route) {
        route.append(el("li", "flow-kind-" + step.kind, step.label));
      }
      content.push(el("h4", "flow-detail-label", "実装経路（個別の呼び出し計測ではありません）"), route);
    }
    if (operation.resources?.length) {
      const resources = el("ul", "flow-resources");
      for (const resource of operation.resources) {
        const item = el("li");
        item.append(el("strong", null, resource.name), el("span", null, resource.access));
        resources.append(item);
      }
      content.push(el("h4", "flow-detail-label", "この処理が扱うリソース"), resources);
    }
    if (operation.note) {
      content.push(el("p", "flow-observation-note", operation.note));
    }
    if (node.name) {
      content.push(el("p", "flow-tool-name", node.name));
    }
    if (node.message) {
      content.push(el("p", null, node.message));
    }
    for (const [label, value] of [["引数", node.arguments], ["結果", node.result]]) {
      if (value !== undefined) {
        content.push(el("h4", "flow-detail-label", label), el("pre", "json-preview", prettyJson(value)));
      }
    }
    if (node.steps) {
      const steps = el("ol", "flow-plan");
      renderPlan(steps, node.steps);
      content.push(steps);
    }
    if (node.artifact) {
      content.push(createArtifactCard(node.artifact));
    }
    detail.replaceChildren(...content);
  }

  function duration(value) {
    if (typeof value !== "number" || !Number.isFinite(value) || value < 0) return "未受信";
    return value < 1000 ? `${value.toFixed(1)} ms` : `${(value / 1000).toFixed(2)} 秒`;
  }

  function spanKey(span) {
    return `${span.source ?? "agent"}:${span.traceId}:${span.spanId}`;
  }

  function relatedSpan(id, relation, span) {
    const node = spans.get(spanKey({ ...span, spanId: id }));
    if (!node) return el("p", "flow-observation-note", `${relation}: ${id}（処理情報未受信）`);
    const button = el("button", "flow-related", `${relation}: ${node.title}${node.name ? ` · ${node.name}` : ""}`);
    button.type = "button";
    button.addEventListener("click", () => select(node, true));
    return button;
  }

  function updateMeasurements() {
    const agentSpans = Array.from(spans.values()).filter(node => node.span.source !== "web");
    const webSpans = spans.size - agentSpans.length;
    const models = agentSpans.filter(node => node.span.name === "model.call").length;
    const label = (status, dropped) => ({
      started: ended ? "収集終了は未確認" : "収集中",
      complete: "収集完了",
      limited: `一部省略（${dropped} イベント）`,
      invalid: "形式不正のため収集表示を中断",
    })[status] ?? "収集状態未確認";
    measurements.textContent = traceStatus
      ? `受信済みの実測: モデル ${models} 回 / ${agentSpans.length} 処理 · ${label(traceStatus, droppedEvents)}`
      : "実測トレースは未受信です。モデル呼び出し数は確認できません。";
    if (webTraceStatus) measurements.textContent += ` / Web ${webSpans} 処理 · ${label(webTraceStatus, webDroppedEvents)}`;
    const warning = status => status && (status !== "complete" && ended || status === "limited" || status === "invalid");
    measurements.classList.toggle("flow-trace-warning", Boolean(warning(traceStatus) || warning(webTraceStatus)));
  }

  function select(node, byUser = false) {
    selected?.button.setAttribute("aria-pressed", "false");
    selected = node;
    pinned ||= byUser;
    node.button.setAttribute("aria-pressed", "true");
    showDetail(node);
    detail.scrollTop = 0;
  }

  function drawConnections() {
    const ordered = Array.from(nodes.values());
    const used = new Set(ordered.map(node => node.operation.location.id));
    const visible = EXECUTION_LANES.filter(lane => !lane.optional || used.has(lane.id));
    if (visible.length !== activeLanes.length || visible.some((lane, index) => lane !== activeLanes[index])) {
      activeLanes = visible;
      stage.style.setProperty("--flow-lane-count", String(activeLanes.length));
      lanes.replaceChildren(...activeLanes.map(lane => {
        const header = el("div", "flow-lane");
        header.dataset.lane = lane.id;
        header.setAttribute("role", "listitem");
        header.append(el("strong", "flow-lane-label", lane.label), el("span", "flow-lane-description", lane.description));
        return header;
      }));
    }
    for (const [index, node] of ordered.entries()) {
      const column = activeLanes.findIndex(lane => lane.id === node.operation.location.id);
      node.button.style.gridColumn = String(column + 1);
      node.button.dataset.lane = node.operation.location.id;
      node.connector.setAttribute("viewBox", `0 0 ${activeLanes.length * 100} 28`);
      const next = ordered[index + 1];
      node.connector.toggleAttribute("hidden", !next);
      if (next) {
        const from = column * 100 + 50;
        const to = activeLanes.findIndex(lane => lane.id === next.operation.location.id) * 100 + 50;
        node.line.setAttribute("d", `M ${from} 0 V 13 H ${to} V 24`);
        node.arrow.setAttribute("points", `${to - 2},19 ${to},25 ${to + 2},19`);
      }
    }
  }

  function update(node, values) {
    Object.assign(node, values);
    node.target = operationTarget(node.operation, node.arguments, node.artifact);
    node.item.className = "flow-item flow-kind-" + node.operation.kind;
    node.button.dataset.kind = node.operation.kind;
    node.button.className = "flow-node flow-" + node.status;
    node.label.textContent = node.title;
    node.badge.textContent = STATUS_LABELS[node.status];
    node.category.textContent = OPERATION_TYPES.find(type => type.id === node.operation.kind).badge;
    node.resource.textContent = node.target.label;
    node.location.textContent = "実行場所: " + node.operation.location.label;
    node.measured.textContent = node.span ? "実測 · " + (typeof node.span.durationMs === "number"
      ? duration(node.span.durationMs) : node.status === "running" ? "計測中" : "所要時間未受信") : "受信イベント";
    node.button.dataset.spanId = node.span?.spanId ?? "";
    node.button.dataset.source = node.span?.source ?? (node.span ? "agent" : "");
    node.button.dataset.operation = node.span?.name ?? "";
    node.parentLabel.hidden = !node.span?.parentSpanId;
    if (node.span?.parentSpanId) {
      node.parentLabel.textContent = "親: " + (spans.get(spanKey({ ...node.span, spanId: node.span.parentSpanId }))?.title ?? "処理情報未受信");
    }
    drawConnections();
    if (selected === node) {
      showDetail(node);
    }
    return node;
  }

  function add(key, title, status, values = {}) {
    if (nodes.has(key)) {
      return update(nodes.get(key), { title, status, ...values });
    }
    const item = el("li", "flow-item");
    const button = el("button");
    button.type = "button";
    button.setAttribute("aria-pressed", "false");
    const number = el("span", "flow-number", String(nodes.size + 1).padStart(2, "0"));
    number.setAttribute("aria-hidden", "true");
    const label = el("span", "flow-node-label");
    const badge = el("span", "flow-badge");
    const category = el("span", "flow-type");
    const meta = el("span", "flow-node-meta");
    meta.append(category, badge);
    const resource = el("span", "flow-node-target");
    const location = el("span", "flow-node-location");
    const measured = el("span", "flow-node-measured");
    const parentLabel = el("span", "flow-node-parent");
    const body = el("span", "flow-node-content");
    body.append(meta, label, measured, location, resource, parentLabel);
    button.append(number, body);
    const connector = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    connector.setAttribute("class", "flow-connector");
    connector.setAttribute("viewBox", "0 0 400 28");
    connector.setAttribute("preserveAspectRatio", "none");
    connector.setAttribute("aria-hidden", "true");
    connector.setAttribute("focusable", "false");
    const line = document.createElementNS(connector.namespaceURI, "path");
    line.setAttribute("vector-effect", "non-scaling-stroke");
    const arrow = document.createElementNS(connector.namespaceURI, "polyline");
    arrow.setAttribute("vector-effect", "non-scaling-stroke");
    connector.append(line, arrow);
    const node = { key, item, button, label, badge, category, resource, location, measured, parentLabel, connector, line, arrow,
      title, status, operation: operationForEvent(key), ...values };
    button.addEventListener("click", () => select(node, true));
    item.append(button, connector);
    list.append(item);
    nodes.set(key, node);
    update(node, {});
    if (!pinned) {
      select(node);
    }
    return node;
  }

  function reset() {
    nodes.clear();
    spans.clear();
    traceStatus = null;
    webTraceStatus = null;
    droppedEvents = 0;
    webDroppedEvents = 0;
    measurements.classList.remove("flow-trace-warning");
    selected = null;
    pinned = false;
    ended = false;
    list.replaceChildren();
    detail.replaceChildren(el("p", "flow-empty", "依頼を送信すると、実行フローをここに表示します。"));
    summary.textContent = "まだ実行はありません。";
    updateMeasurements();
    drawConnections();
    stage.scrollTop = 0;
    stage.scrollLeft = 0;
  }

  function start(approval) {
    reset();
    summary.textContent = "実行中";
    const title = approval === true ? "承認して再開" : approval === false ? "却下して再開" : "依頼の開始";
    add("request", title, "completed", { message: "画面からエージェントへの要求を開始しました。" });
  }

  function finish(status, message) {
    if (ended) {
      return;
    }
    ended = true;
    for (const node of nodes.values()) {
      if (node.status === "running") {
        update(node, { status: node.key === "answer" ? status : "unknown" });
      } else if (node.status === "waiting" && status !== "waiting") {
        update(node, { status: "unknown" });
      }
    }
    summary.textContent = STATUS_LABELS[status];
    updateMeasurements();
    add("end", status === "waiting" ? "承認を待っています" : "応答の終了", status, { message });
  }

  function handle(name, data, approvalsEnabled) {
    switch (name) {
      case "trace.status":
        if (data.source === "web") {
          webTraceStatus = data.status;
          webDroppedEvents = data.droppedEvents;
        } else {
          traceStatus = data.status;
          droppedEvents = data.droppedEvents;
        }
        updateMeasurements();
        break;
      case "trace.span": {
        const isWeb = data.source === "web";
        if (isWeb) webTraceStatus ??= "started";
        else traceStatus ??= "started";
        const operation = operationForSpan(data);
        const key = !isWeb && data.name === "tool.execute" && data.callId ? "tool:" + data.callId : "span:" + spanKey(data);
        const title = data.name === "tool.execute" && data.toolName ? toolNoun(data.toolName) : operation.title;
        const node = add(key, title, data.status, { span: data, name: data.toolName, operation });
        spans.set(spanKey(data), node);
        for (const measured of spans.values()) update(measured, {});
        const answer = nodes.get("answer");
        if (answer) update(answer, { title: "回答テキストの受信", operation: operationForEvent("answer-received") });
        updateMeasurements();
        break;
      }
      case "tool.started":
        if (!nodes.get("tool:" + data.callId)?.span) {
          add("tool:" + data.callId, toolNoun(data.name), "running", { name: data.name, operation: operationForTool(data.name) });
        }
        break;
      case "tool.arguments": {
        // 録画や上流の応答によっては、開始イベントなしで引数が届く。
        const node = nodes.get("tool:" + data.callId)
          ?? add("tool:" + data.callId, toolNoun(data.name), "unknown", { name: data.name, operation: operationForTool(data.name) });
        update(node, { arguments: data.arguments });
        break;
      }
      case "tool.completed": {
        const measured = nodes.get("tool:" + data.callId);
        if (measured?.span) update(measured, { result: data.summary });
        else add("tool:" + data.callId, toolNoun(data.name), data.ok === true ? "completed" : "failed",
          { name: data.name, result: data.summary, operation: operationForTool(data.name) });
        break;
      }
      case "plan.updated":
        add("plan", "実行計画の受信", "completed", { steps: Array.isArray(data.steps) ? data.steps : [] });
        break;
      case "approval.requested":
        add("approval:" + data.approvalRequestId, "承認の確認", approvalsEnabled ? "waiting" : "unavailable", {
          operation: operationForEvent("approval"),
          name: data.toolName,
          arguments: data.argumentsPreview,
          message: approvalsEnabled
            ? "チャット欄の承認カードで「作成する」または「やめる」を選んでください。"
            : "承認機能が無効です。この画面から承認して再開することはできません。",
        });
        break;
      case "artifact.created":
        add("artifact:" + data.artifactId, "成果物の登録", "completed", { artifact: data, operation: operationForEvent("artifact") });
        break;
      case "text.delta":
        if (!nodes.has("answer") && typeof data.text === "string" && data.text.length > 0) {
          add("answer", traceStatus ? "回答テキストの受信" : "回答の生成", "running", {
            operation: operationForEvent(traceStatus ? "answer-received" : "answer"),
            message: "回答の本文はチャット欄で確認できます。",
          });
        }
        break;
      case "done": {
        const awaiting = Array.from(nodes.values()).some(node => node.status === "waiting");
        const unavailable = Array.from(nodes.values()).some(node => node.status === "unavailable");
        finish(awaiting ? "waiting" : unavailable ? "unavailable" : data.status === "incomplete" ? "incomplete" : "completed",
          data.reason);
        break;
      }
      case "error":
        finish("failed", data.message || "エージェントでエラーが起きました。");
        break;
      default:
        break;
    }
  }

  reset();
  return { reset, start, handle, finish };
}
