// 画面の部品の描画(設計書5.2節「画面」)。DOM は textContent で組み立て、HTML を入れるのは
// marked の出力を DOMPurify で無害化したものだけにする。
import { marked } from "../lib/marked/lib/marked.esm.js";
import DOMPurify from "../lib/dompurify/dist/purify.es.mjs";

/** 学習モードで表示するツールの引数と結果の上限(文字数)。 */
export const MAX_PREVIEW_LENGTH = 2048;

const SANITIZE_OPTIONS = Object.freeze({
  USE_PROFILES: { html: true },
  // CSP で inline の style は効かないため、属性ごと落とす。フォーム部品も応答には要らない。
  FORBID_TAGS: ["style", "form", "input", "button", "textarea", "select", "option"],
  FORBID_ATTR: ["style"],
});

marked.setOptions({ gfm: true, breaks: false });

const TOOL_TEXTS = Object.freeze({
  search_azure_updates: ["更新情報を検索しています", "更新情報を検索しました", "更新情報を検索できませんでした"],
  get_azure_update: ["更新情報を取得しています", "更新情報を取得しました", "更新情報を取得できませんでした"],
  create_word_report: ["Word の報告書を作成しています", "Word の報告書を作成しました", "Word の報告書を作成できませんでした"],
  create_powerpoint_deck: ["PowerPoint の資料を作成しています", "PowerPoint の資料を作成しました", "PowerPoint の資料を作成できませんでした"],
  load_skill: ["手順書を読み込んでいます", "手順書を読み込みました", "手順書を読み込めませんでした"],
});

const TOOL_NOUNS = Object.freeze({
  search_azure_updates: "更新情報の検索",
  get_azure_update: "更新情報の取得",
  create_word_report: "Word の報告書の作成",
  create_powerpoint_deck: "PowerPoint の資料の作成",
});

const PLAN_STATUS_LABELS = Object.freeze({
  pending: "未着手",
  in_progress: "実行中",
  completed: "完了",
});

const ARTIFACT_KIND_LABELS = Object.freeze({
  docx: "Word 文書",
  pptx: "PowerPoint 資料",
});

/** 要素を作る。text は textContent に入れる。 */
export function el(tag, className, text) {
  const element = document.createElement(tag);
  if (className) {
    element.className = className;
  }
  if (text !== undefined && text !== null) {
    element.textContent = String(text);
  }
  return element;
}

/** Markdown を HTML にし、無害化してから表示する。リンクは新しいタブで開く。 */
export function renderMarkdown(container, markdown) {
  const html = marked.parse(markdown ?? "", { async: false });
  container.innerHTML = DOMPurify.sanitize(html, SANITIZE_OPTIONS);
  for (const anchor of container.querySelectorAll("a[href]")) {
    anchor.setAttribute("target", "_blank");
    anchor.setAttribute("rel", "noopener noreferrer");
  }
}

/** 外部記事は本文に必要な要素だけを残し、外部リソースを自動取得しない。 */
export function renderArticleHtml(container, html, sourceUrl) {
  container.innerHTML = DOMPurify.sanitize(html ?? "", {
    ALLOWED_TAGS: ["p", "br", "strong", "b", "em", "i", "u", "s", "h2", "h3", "h4", "ul", "ol", "li", "a", "blockquote", "pre", "code", "table", "thead", "tbody", "tr", "th", "td", "hr"],
    ALLOWED_ATTR: ["href", "title", "colspan", "rowspan"],
  });
  for (const anchor of container.querySelectorAll("a[href]")) {
    try {
      const url = new URL(anchor.getAttribute("href"), sourceUrl);
      if (url.protocol !== "https:" || url.username || url.password) {
        anchor.removeAttribute("href");
        continue;
      }
      anchor.href = url.href;
      anchor.target = "_blank";
      anchor.rel = "noopener noreferrer";
    } catch {
      anchor.removeAttribute("href");
    }
  }
}

/** 進行状況のチェックリストを描く。 */
export function renderPlan(listElement, steps) {
  const items = (Array.isArray(steps) ? steps : []).map((step) => {
    const status = Object.hasOwn(PLAN_STATUS_LABELS, step?.status) ? step.status : "pending";
    const item = el("li", "plan-step plan-" + status.replace("_", "-"));
    const marker = el("span", "plan-marker");
    marker.setAttribute("aria-hidden", "true");
    item.append(marker, el("span", "plan-title", step?.title ?? ""), el("span", "visually-hidden", `(${PLAN_STATUS_LABELS[status]})`));
    return item;
  });
  listElement.replaceChildren(...items);
}

/** 画面のツールの行に出さないツール(進行状況はチェックリストで見せる)。 */
export function isQuietTool(name) {
  return name === "update_plan";
}

function toolTexts(name) {
  return TOOL_TEXTS[name] ?? [`ツール ${name} を実行しています`, `ツール ${name} を実行しました`, `ツール ${name} を実行できませんでした`];
}

/** ツールの動きの行(実行中)を作る。 */
export function createToolLine(name) {
  const line = el("li", "tool-line tool-running");
  line.append(el("span", "tool-marker"), el("span", "tool-text", toolTexts(name)[0]));
  line.firstChild.setAttribute("aria-hidden", "true");
  return line;
}

/** ツールの動きの行を、終わった状態にする。 */
export function completeToolLine(line, name, ok) {
  const [, done, failed] = toolTexts(name);
  line.classList.remove("tool-running");
  line.classList.add(ok ? "tool-ok" : "tool-failed");
  line.querySelector(".tool-text").textContent = ok ? done : failed;
}

/** バイト数を読みやすい形にする。 */
export function formatBytes(bytes) {
  const value = Number(bytes);
  if (!Number.isFinite(value) || value < 0) {
    return "";
  }
  if (value < 1024) {
    return `${value} B`;
  }
  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KB`;
  }
  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

/** 成果物カードを作る。ダウンロード先は BFF の /api/artifacts/ だけを許す。 */
export function createArtifactCard(artifact) {
  const card = el("div", "card artifact-card");
  card.setAttribute("role", "group");
  card.setAttribute("aria-label", "成果物");

  const info = el("div", "artifact-info");
  info.append(
    el("span", "artifact-kind artifact-kind-" + (Object.hasOwn(ARTIFACT_KIND_LABELS, artifact.kind) ? artifact.kind : "other"),
      ARTIFACT_KIND_LABELS[artifact.kind] ?? "ファイル"),
    el("span", "artifact-name", artifact.fileName ?? ""),
    el("span", "artifact-size", formatBytes(artifact.sizeBytes)));
  card.append(info);

  const url = typeof artifact.downloadUrl === "string" && /^\/api\/artifacts\/art_[0-9A-Za-z]+$/.test(artifact.downloadUrl)
    ? artifact.downloadUrl
    : null;
  if (url) {
    const link = el("a", "primary-button download-button", "ダウンロード");
    link.href = url;
    link.setAttribute("download", artifact.fileName ?? "");
    link.setAttribute("aria-label", `${artifact.fileName ?? "成果物"} をダウンロード`);
    card.append(link);
  }
  return card;
}

/** 承認カードを作る。onDecide(approve) は Promise を返す。 */
export function createApprovalCard(request, onDecide) {
  const card = el("div", "card approval-card");
  card.setAttribute("role", "group");
  card.setAttribute("aria-label", "承認の確認");
  const noun = TOOL_NOUNS[request.toolName] ?? `ツール ${request.toolName} の実行`;
  card.append(el("p", "approval-title", `${noun}を始めてよいですか。`));
  if (request.argumentsPreview) {
    const details = el("details", "approval-details");
    details.append(el("summary", null, "引数を見る"), el("pre", "json-preview", prettyJson(request.argumentsPreview)));
    card.append(details);
  }

  const actions = el("div", "card-actions");
  const approve = el("button", "primary-button", "作成する");
  const reject = el("button", "secondary-button", "やめる");
  approve.type = "button";
  reject.type = "button";
  const status = el("p", "approval-status");
  status.setAttribute("role", "status");
  const decide = async (value) => {
    approve.disabled = true;
    reject.disabled = true;
    status.textContent = value ? "承認しました。" : "取りやめました。";
    try {
      await onDecide(value);
    } catch {
      status.textContent = "送れませんでした。";
    }
  };
  approve.addEventListener("click", () => decide(true));
  reject.addEventListener("click", () => decide(false));
  actions.append(approve, reject);
  card.append(actions, status);
  return card;
}

/** 評価ボタンを作る。onSubmit(rating, comment) は Promise を返す。 */
export function createFeedback(onSubmit) {
  const box = el("div", "feedback");
  const buttons = el("div", "feedback-buttons");
  buttons.setAttribute("role", "group");
  buttons.setAttribute("aria-label", "この応答の評価");
  const good = el("button", "feedback-button", "役に立った");
  const bad = el("button", "feedback-button", "役に立たなかった");
  for (const [button, rating] of [[good, "good"], [bad, "bad"]]) {
    button.type = "button";
    button.dataset.rating = rating;
    button.setAttribute("aria-pressed", "false");
  }
  buttons.append(good, bad);

  const form = el("form", "feedback-form");
  form.hidden = true;
  const commentId = "feedback-comment-" + Math.random().toString(36).slice(2);
  const label = el("label", "visually-hidden", "コメント(任意)");
  label.htmlFor = commentId;
  const comment = el("textarea", "feedback-comment");
  comment.id = commentId;
  comment.rows = 2;
  comment.maxLength = 500;
  comment.placeholder = "コメント(任意、500文字まで)";
  const submit = el("button", "secondary-button", "評価を送る");
  submit.type = "submit";
  const status = el("p", "feedback-status");
  status.setAttribute("role", "status");
  form.append(label, comment, submit);

  let selected = null;
  buttons.addEventListener("click", (event) => {
    const button = event.target.closest("button[data-rating]");
    if (!button) {
      return;
    }
    selected = button.dataset.rating;
    for (const other of buttons.querySelectorAll("button")) {
      other.setAttribute("aria-pressed", String(other === button));
    }
    form.hidden = false;
    comment.focus();
  });
  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    if (!selected) {
      return;
    }
    submit.disabled = true;
    try {
      await onSubmit(selected, comment.value.trim());
      box.replaceChildren(el("p", "feedback-status", "評価を送りました。ありがとうございます。"));
    } catch {
      submit.disabled = false;
      status.textContent = "評価を送れませんでした。";
    }
  });

  box.append(buttons, form, status);
  return box;
}

/** JSON の文字列を整形し、上限で切る。JSON でなければそのまま切る。 */
export function prettyJson(text) {
  let value = typeof text === "string" ? text : JSON.stringify(text);
  try {
    value = JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    // JSON でなければ受け取った文字列を出す。
  }
  return truncate(value ?? "");
}

export function truncate(text) {
  return text.length > MAX_PREVIEW_LENGTH ? text.slice(0, MAX_PREVIEW_LENGTH) + "…(省略)" : text;
}

/** トレース ID で絞り込む KQL (Application Insights)。 */
export function buildKql(traceId) {
  return [
    "union requests, dependencies, traces, exceptions, customEvents",
    `| where operation_Id == "${traceId}"`,
    "| project timestamp, itemType, name, message, duration, success, resultCode, customDimensions",
    "| order by timestamp asc",
  ].join("\n");
}

/** 学習モードのツール呼び出しの項目を作る。 */
export function createLearningTool(callId, name) {
  const item = el("li", "learning-tool");
  item.dataset.callId = callId;
  const header = el("div", "learning-tool-header");
  header.append(el("span", "learning-tool-name", name), el("span", "learning-tool-status", "実行中"));
  const args = el("pre", "json-preview learning-tool-args");
  args.hidden = true;
  const result = el("pre", "json-preview learning-tool-result");
  result.hidden = true;
  item.append(header, args, result);
  return item;
}

export function setLearningToolArguments(item, argumentsText) {
  const args = item.querySelector(".learning-tool-args");
  args.textContent = "引数:\n" + prettyJson(argumentsText);
  args.hidden = false;
}

export function setLearningToolResult(item, ok, summary) {
  item.querySelector(".learning-tool-status").textContent = ok ? "成功" : "失敗";
  if (summary) {
    const result = item.querySelector(".learning-tool-result");
    result.textContent = "結果:\n" + prettyJson(summary);
    result.hidden = false;
  }
}
