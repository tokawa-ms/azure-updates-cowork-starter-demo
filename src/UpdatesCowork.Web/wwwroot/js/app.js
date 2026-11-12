// 専用ビューアーの選択とチャットを、同一オリジンのページ状態でつなぐ。
import { getJson, postJson, postSse, HttpError } from "./sse.js";
import { createViewer } from "./viewer.js";
import {
  buildKql,
  completeToolLine,
  createApprovalCard,
  createArtifactCard,
  createFeedback,
  createLearningTool,
  createToolLine,
  el,
  isQuietTool,
  renderMarkdown,
  renderPlan,
  setLearningToolArguments,
  setLearningToolResult,
} from "./render.js";

const UPSTREAM_ORIGIN = "https://azure.microsoft.com";
const MAX_MESSAGE_LENGTH = 2000;
const MAX_TITLE_LENGTH = 300;
const MAX_URL_LENGTH = 2048;
const LEARNING_STORAGE_KEY = "auc.learningMode";
const THREAD_ID_PATTERN = /^th_[0-9A-HJKMNP-TV-Z]{26}$/;
const UPDATE_ID_PATTERN = /^[A-Za-z0-9-]{1,200}$/;
const LANG_PATTERN = /^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8}){0,3}$/;

// すぐ使うボタンの定型文。BFF は action をテレメトリにだけ使う(6.2節)。
const QUICK_PROMPTS = Object.freeze({
  summarize: "このアップデートを要約して",
  word: "このアップデートを Word にまとめて",
  pptx: "このアップデートを PowerPoint の資料にまとめて",
  related: "このアップデートに関連する更新を探して",
});

const ERROR_MESSAGES = Object.freeze({
  timeout: "時間内に応答が終わりませんでした。依頼を短くして、もう一度お試しください。",
  agent_forbidden: "エージェントを呼び出す権限がありません。",
  agent_not_found: "エージェントが見つかりません。",
  rate_limited: "依頼が多すぎます。しばらく待ってからもう一度お試しください。",
  agent_error: "エージェントでエラーが起きました。もう一度お試しください。",
  response_failed: "応答の作成に失敗しました。もう一度お試しください。",
  invalid_request: "依頼の内容が正しくありません。",
});

const ui = {
  pageTitle: document.getElementById("page-title"),
  pageId: document.getElementById("page-id"),
  quickButtons: Array.from(document.querySelectorAll(".quick-button")),
  plan: document.getElementById("plan"),
  planSteps: document.getElementById("plan-steps"),
  messages: document.getElementById("messages"),
  emptyState: document.getElementById("empty-state"),
  form: document.getElementById("chat-form"),
  input: document.getElementById("chat-input"),
  sendButton: document.getElementById("send-button"),
  stopButton: document.getElementById("stop-button"),
  newThreadButton: document.getElementById("new-thread"),
  learningToggleLabel: document.getElementById("learning-toggle-label"),
  learningToggle: document.getElementById("learning-toggle"),
  learningPanel: document.getElementById("learning-panel"),
  learnTraceId: document.getElementById("learn-trace-id"),
  learnResponseId: document.getElementById("learn-response-id"),
  learnConversationId: document.getElementById("learn-conversation-id"),
  learnTokens: document.getElementById("learn-tokens"),
  learnKql: document.getElementById("learn-kql"),
  learnTools: document.getElementById("learn-tools"),
  copyKqlButton: document.getElementById("copy-kql"),
  copyKqlStatus: document.getElementById("copy-kql-status"),
  userStatus: document.getElementById("user-status"),
  signinLink: document.getElementById("signin-link"),
  signoutLink: document.getElementById("signout-link"),
};

const state = {
  config: null,
  threadId: null,
  page: null,
  busy: false,
  canChat: true,
  controller: null,
  traceId: null,
};
const viewer = createViewer(data => {
  state.page = data ? toPageContext(data) : null;
  showCurrentPage();
  updateControls();
});

// ---- 起動 ----

async function start() {
  bindEvents();
  try {
    state.config = await getJson("/api/config");
  } catch {
    ui.emptyState.textContent = "画面の設定を読み込めませんでした。ページを再読み込みしてください。";
    state.canChat = false;
    updateControls();
    return;
  }
  setupLearningMode();
  await setupAuth();
  updateControls();
  await viewer.start();
}

function setupLearningMode() {
  const available = state.config.features?.learningMode === true;
  ui.learningToggleLabel.hidden = !available;
  let enabled = false;
  if (available) {
    try {
      enabled = window.localStorage.getItem(LEARNING_STORAGE_KEY) === "on";
    } catch {
      enabled = false;
    }
  }
  ui.learningToggle.checked = enabled;
  ui.learningPanel.hidden = !enabled;
}

async function setupAuth() {
  if (state.config.authMode !== "EntraId") {
    return;
  }
  let me = null;
  try {
    me = await getJson("/api/me");
  } catch {
    me = null;
  }
  if (me?.signedIn) {
    ui.userStatus.textContent = me.displayName ? `${me.displayName} でサインイン中` : "サインイン中";
    ui.userStatus.hidden = false;
    ui.signoutLink.hidden = false;
  } else {
    ui.signinLink.hidden = false;
    state.canChat = false;
    ui.emptyState.textContent = "チャットを使うにはサインインしてください。";
  }
}

// ---- 選択中の更新情報 ----

function cleanText(value, max) {
  if (typeof value !== "string") {
    return null;
  }
  const text = value.replace(/[\u0000-\u001f\u007f\u202a-\u202e\u2066-\u2069]+/g, " ").replace(/\s+/g, " ").trim();
  return text.length > 0 ? text.slice(0, max) : null;
}

/** 受け取ったページ情報を、BFF の検証(PageContextValidator)を通る形にそろえる。 */
function toPageContext(data) {
  const upstreamUrl = typeof data.upstreamUrl === "string"
    && data.upstreamUrl.startsWith(UPSTREAM_ORIGIN + "/")
    && data.upstreamUrl.length <= MAX_URL_LENGTH
    && !/[\s\u0000-\u001f\u007f]/.test(data.upstreamUrl)
    ? data.upstreamUrl
    : null;
  if (!upstreamUrl) {
    return null;
  }
  return {
    upstreamUrl,
    updateId: typeof data.updateId === "string" && UPDATE_ID_PATTERN.test(data.updateId) ? data.updateId : null,
    title: cleanText(data.heading, MAX_TITLE_LENGTH) ?? cleanText(data.title, MAX_TITLE_LENGTH),
    lang: typeof data.lang === "string" && LANG_PATTERN.test(data.lang) ? data.lang.toLowerCase() : null,
  };
}

function showCurrentPage() {
  const page = state.page;
  if (!page) {
    ui.pageTitle.textContent = "ページ未選択";
    ui.pageId.hidden = true;
    return;
  }
  ui.pageTitle.textContent = page.title ?? page.upstreamUrl;
  ui.pageId.textContent = page.updateId ? `ID: ${page.updateId}` : "更新情報の ID なし";
  ui.pageId.hidden = false;
}

function onMessagesClick(event) {
  const anchor = event.target.closest?.("a[href]");
  if (!anchor || !ui.messages.contains(anchor) || anchor.hasAttribute("download")) {
    return;
  }
  let url;
  try {
    url = new URL(anchor.getAttribute("href"), window.location.href);
  } catch {
    return;
  }
  const id = url.searchParams.get("id");
  if (!event.ctrlKey && !event.metaKey && !event.shiftKey && event.button === 0
      && url.origin === UPSTREAM_ORIGIN && /\/updates\/?$/.test(url.pathname)
      && UPDATE_ID_PATTERN.test(id ?? "")) {
    event.preventDefault();
    void viewer.select(id);
  }
}

// ---- 送信 ----

function bindEvents() {
  window.addEventListener("pagehide", (event) => {
    if (event.persisted || !state.threadId) {
      return;
    }
    const threadId = state.threadId;
    state.threadId = null;
    void fetch("/api/threads/end", {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-AUC-Request": "1" },
      body: JSON.stringify({ threadId }),
      credentials: "same-origin",
      keepalive: true,
    }).catch(() => console.warn("会話を終了できませんでした。"));
  });
  ui.messages.addEventListener("click", onMessagesClick);
  ui.form.addEventListener("submit", (event) => {
    event.preventDefault();
    sendMessage(ui.input.value, null);
  });
  ui.input.addEventListener("keydown", (event) => {
    if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      sendMessage(ui.input.value, null);
    }
  });
  for (const button of ui.quickButtons) {
    button.addEventListener("click", () => {
      const action = button.dataset.action;
      if (Object.hasOwn(QUICK_PROMPTS, action)) {
        sendMessage(QUICK_PROMPTS[action], action);
      }
    });
  }
  ui.stopButton.addEventListener("click", () => state.controller?.abort());
  ui.newThreadButton.addEventListener("click", startNewThread);
  ui.learningToggle.addEventListener("change", () => {
    ui.learningPanel.hidden = !ui.learningToggle.checked;
    try {
      window.localStorage.setItem(LEARNING_STORAGE_KEY, ui.learningToggle.checked ? "on" : "off");
    } catch {
      // 保存できなくても切り替えは効く。
    }
  });
  ui.copyKqlButton.addEventListener("click", copyKql);
}

function updateControls() {
  const disabled = state.busy || !state.canChat;
  for (const button of ui.quickButtons) {
    button.disabled = disabled || !state.page?.updateId;
  }
  ui.sendButton.disabled = disabled;
  ui.input.disabled = !state.canChat;
  ui.newThreadButton.disabled = disabled;
  ui.stopButton.hidden = !state.busy;
}

async function sendMessage(rawMessage, action) {
  if (state.busy || !state.canChat) {
    return;
  }
  const message = (rawMessage ?? "").trim();
  if (message.length === 0) {
    ui.input.focus();
    return;
  }
  if (message.length > MAX_MESSAGE_LENGTH) {
    appendSystemNote(`依頼は${MAX_MESSAGE_LENGTH}文字までにしてください。`, true);
    return;
  }
  if (!action) {
    ui.input.value = "";
  }
  appendUserMessage(message, action);
  await runTurn("/api/chat", {
    threadId: state.threadId,
    message,
    action,
    pageContext: state.page,
  });
}

async function startNewThread() {
  if (state.busy) {
    return;
  }
  ui.newThreadButton.disabled = true;
  try {
    if (state.threadId) {
      await postJson("/api/threads/end", { threadId: state.threadId });
      state.threadId = null;
    }
    const result = await postJson("/api/threads/new", {});
    state.threadId = typeof result?.threadId === "string" && THREAD_ID_PATTERN.test(result.threadId) ? result.threadId : null;
  } catch {
    appendSystemNote("会話を終了または開始できませんでした。もう一度お試しください。", true);
    ui.newThreadButton.disabled = false;
    return;
  }
  ui.messages.replaceChildren(ui.emptyState);
  ui.emptyState.hidden = false;
  ui.plan.hidden = true;
  ui.planSteps.replaceChildren();
  resetLearning();
  updateControls();
  ui.input.focus();
}

function appendUserMessage(text, action) {
  ui.emptyState.hidden = true;
  const item = el("article", "message message-user");
  item.setAttribute("aria-label", "あなた");
  item.append(el("p", "message-text", text));
  if (state.page?.title && action) {
    item.append(el("p", "message-context", `対象: ${state.page.title}`));
  }
  ui.messages.append(item);
  item.scrollIntoView({ block: "end" });
}

function appendSystemNote(text, isError) {
  ui.emptyState.hidden = true;
  const note = el("p", isError ? "system-note system-error" : "system-note", text);
  if (isError) {
    note.setAttribute("role", "alert");
  }
  ui.messages.append(note);
}

function createAssistantTurn() {
  ui.emptyState.hidden = true;
  const root = el("article", "message message-assistant streaming");
  root.setAttribute("aria-label", "エージェント");
  root.setAttribute("aria-busy", "true");
  const tools = el("ul", "tool-lines");
  tools.setAttribute("aria-label", "ツールの動き");
  const text = el("div", "markdown");
  const cards = el("div", "cards");
  const footer = el("div", "message-footer");
  root.append(tools, text, cards, footer);
  ui.messages.append(root);
  return {
    root,
    tools,
    text,
    cards,
    footer,
    markdown: "",
    renderPending: false,
    finished: false,
    threadId: state.threadId,
    responseId: null,
    toolLines: new Map(),
    learningItems: new Map(),
  };
}

async function runTurn(url, body) {
  if (state.busy) {
    throw new Error("busy");
  }
  const turn = createAssistantTurn();
  const controller = new AbortController();
  state.busy = true;
  state.controller = controller;
  updateControls();
  resetLearning();
  try {
    await postSse(url, body, (name, data) => handleEvent(turn, name, data), controller.signal);
    if (!turn.finished) {
      showTurnError(turn, "応答が途中で切れました。もう一度お試しください。");
    }
  } catch (error) {
    if (error?.name === "AbortError") {
      showTurnNote(turn, "止めました。");
    } else if (error instanceof HttpError) {
      handleHttpError(turn, error);
    } else {
      showTurnError(turn, "サーバーに接続できませんでした。");
    }
  } finally {
    renderText(turn);
    turn.root.classList.remove("streaming");
    turn.root.setAttribute("aria-busy", "false");
    state.busy = false;
    state.controller = null;
    updateControls();
  }
}

function handleHttpError(turn, error) {
  switch (error.status) {
    case 429: {
      const wait = error.retryAfterSeconds ? `${error.retryAfterSeconds}秒ほど` : "しばらく";
      showTurnError(turn, `依頼が多すぎます。${wait}待ってからもう一度お試しください。`);
      return;
    }
    case 404:
      if (error.code === "thread_not_found") {
        state.threadId = null;
        showTurnError(turn, "会話が見つかりませんでした。新しい会話として、もう一度送ってください。");
        return;
      }
      showTurnError(turn, "見つかりませんでした。");
      return;
    case 401:
      showTurnError(turn, "サインインしてください。");
      return;
    default:
      showTurnError(turn, error.message || ERROR_MESSAGES[error.code] || `エラーが起きました(${error.status})。`);
  }
}

function showTurnError(turn, text) {
  const note = el("p", "turn-error", text);
  note.setAttribute("role", "alert");
  turn.footer.append(note);
}

function showTurnNote(turn, text) {
  turn.footer.append(el("p", "turn-note", text));
}

function scheduleRender(turn) {
  if (turn.renderPending) {
    return;
  }
  turn.renderPending = true;
  window.requestAnimationFrame(() => renderText(turn));
}

function renderText(turn) {
  turn.renderPending = false;
  if (turn.markdown.length > 0) {
    renderMarkdown(turn.text, turn.markdown);
  }
}

/** SSE のイベント(6.3節)を画面に反映する。 */
function handleEvent(turn, name, data) {
  switch (name) {
    case "meta":
      if (typeof data.threadId === "string" && THREAD_ID_PATTERN.test(data.threadId)) {
        state.threadId = data.threadId;
        turn.threadId = data.threadId;
      }
      turn.responseId = data.responseId ?? turn.responseId;
      setLearningIds(data.traceId, data.responseId, data.conversationId);
      break;
    case "text.delta":
      if (typeof data.text === "string") {
        turn.markdown += data.text;
        scheduleRender(turn);
      }
      break;
    case "tool.started": {
      if (!isQuietTool(data.name)) {
        const line = createToolLine(data.name);
        turn.toolLines.set(data.callId, line);
        turn.tools.append(line);
      }
      const item = createLearningTool(data.callId, data.name);
      turn.learningItems.set(data.callId, item);
      ui.learnTools.append(item);
      break;
    }
    case "tool.arguments": {
      const item = turn.learningItems.get(data.callId);
      if (item) {
        setLearningToolArguments(item, data.arguments);
      }
      break;
    }
    case "plan.updated":
      renderPlan(ui.planSteps, data.steps);
      ui.plan.hidden = !Array.isArray(data.steps) || data.steps.length === 0;
      break;
    case "tool.completed": {
      const line = turn.toolLines.get(data.callId);
      if (line) {
        completeToolLine(line, data.name, data.ok === true);
      }
      const item = turn.learningItems.get(data.callId);
      if (item) {
        setLearningToolResult(item, data.ok === true, data.summary);
      }
      break;
    }
    case "artifact.created":
      turn.cards.append(createArtifactCard(data));
      break;
    case "approval.requested":
      if (state.config?.features?.approvals === true) {
        turn.cards.append(createApprovalCard(data, (approve) => runTurn("/api/chat/approval", {
          threadId: turn.threadId,
          approvalRequestId: data.approvalRequestId,
          approve,
        })));
      } else {
        showTurnNote(turn, "承認の要求を受け取りましたが、承認機能が無効のため表示できません。");
      }
      break;
    case "usage":
      ui.learnTokens.textContent = `入力 ${data.inputTokens} / 出力 ${data.outputTokens} / 合計 ${data.totalTokens}`;
      break;
    case "done":
      turn.finished = true;
      turn.responseId = data.responseId ?? turn.responseId;
      if (data.responseId) {
        ui.learnResponseId.textContent = data.responseId;
      }
      if (data.status === "incomplete") {
        showTurnNote(turn, data.reason ? `応答が途中で終わりました(${data.reason})。` : "応答が途中で終わりました。");
      }
      addFeedback(turn);
      break;
    case "error":
      turn.finished = true;
      if (data.traceId) {
        setLearningIds(data.traceId, null, null);
      }
      showTurnError(turn, data.message || ERROR_MESSAGES[data.code] || "エラーが起きました。");
      break;
    default:
      break;
  }
}

function addFeedback(turn) {
  const responseId = turn.responseId;
  const threadId = turn.threadId;
  if (!responseId || !threadId) {
    return;
  }
  turn.footer.append(createFeedback((rating, comment) => postJson("/api/feedback", {
    threadId,
    responseId,
    rating,
    comment: comment.length > 0 ? comment : null,
  })));
}

// ---- 学習モード ----

function resetLearning() {
  state.traceId = null;
  ui.learnTraceId.textContent = "-";
  ui.learnResponseId.textContent = "-";
  ui.learnConversationId.textContent = "-";
  ui.learnTokens.textContent = "-";
  ui.learnKql.textContent = "";
  ui.learnKql.hidden = true;
  ui.learnTools.replaceChildren();
  ui.copyKqlButton.disabled = true;
  ui.copyKqlStatus.textContent = "";
}

function setLearningIds(traceId, responseId, conversationId) {
  if (typeof traceId === "string" && /^[0-9a-f]{32}$/.test(traceId)) {
    state.traceId = traceId;
    ui.learnTraceId.textContent = traceId;
    ui.learnKql.textContent = buildKql(traceId);
    ui.learnKql.hidden = false;
    ui.copyKqlButton.disabled = false;
  }
  if (responseId) {
    ui.learnResponseId.textContent = responseId;
  }
  if (conversationId) {
    ui.learnConversationId.textContent = conversationId;
  }
}

async function copyKql() {
  if (!state.traceId) {
    return;
  }
  try {
    await navigator.clipboard.writeText(buildKql(state.traceId));
    ui.copyKqlStatus.textContent = "コピーしました。";
  } catch {
    // クリップボードが使えないときは、表示した KQL を選択して手でコピーしてもらう。
    const range = document.createRange();
    range.selectNodeContents(ui.learnKql);
    const selection = window.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(range);
    ui.copyKqlStatus.textContent = "コピーできませんでした。選択した KQL を Ctrl+C でコピーしてください。";
  }
}

start();
