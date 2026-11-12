import { getJson, HttpError } from "./sse.js";
import { el, renderArticleHtml } from "./render.js";

const ID_PATTERN = /^[A-Za-z0-9-]{1,200}$/;
const STATUS_LABELS = Object.freeze({
  Launched: "一般提供",
  "In preview": "プレビュー",
  "In development": "開発中",
});
const dateFormat = new Intl.DateTimeFormat("ja-JP", { year: "numeric", month: "2-digit", day: "2-digit" });

function formatDate(value) {
  const date = new Date(value);
  return value && Number.isFinite(date.getTime()) ? dateFormat.format(date) : "日付なし";
}

export function createViewer(onSelection) {
  const form = document.getElementById("updates-search");
  const list = document.getElementById("updates-list");
  const count = document.getElementById("updates-count");
  const article = document.getElementById("update-article");
  const retry = document.getElementById("updates-retry");
  let searchController;
  let detailController;
  let selectedId = null;
  let pendingId = null;

  function markSelected() {
    for (const button of list.querySelectorAll("button[data-update-id]")) {
      button.setAttribute("aria-pressed", String(button.dataset.updateId === selectedId));
    }
  }

  async function select(id, updateHistory = true) {
    detailController?.abort();
    const controller = new AbortController();
    detailController = controller;
    selectedId = null;
    pendingId = id;
    onSelection(null);
    markSelected();
    if (!ID_PATTERN.test(id ?? "")) {
      article.replaceChildren(el("p", "viewer-error", "更新 ID が正しくありません。一覧から選び直してください。"));
      return;
    }
    article.setAttribute("aria-busy", "true");
    article.replaceChildren(el("p", "article-placeholder", "本文を読み込んでいます…"));
    try {
      const detail = await getJson("/api/updates/" + encodeURIComponent(id), controller.signal);
      if (controller.signal.aborted) return;
      const update = detail.update;
      if (update?.id !== id) throw new Error("記事 ID が一致しません。");
      selectedId = id;
      pendingId = null;
      renderDetail(detail);
      markSelected();
      onSelection({ updateId: id, title: update.title, upstreamUrl: update.url, lang: update.locale });
      if (updateHistory) {
        const url = new URL(window.location.href);
        url.searchParams.set("id", id);
        if (url.href !== window.location.href) window.history.pushState(null, "", url);
      }
    } catch (error) {
      if (controller.signal.aborted) return;
      const button = el("button", "secondary-button", "本文を再読み込み");
      button.type = "button";
      button.addEventListener("click", () => void select(id, updateHistory));
      article.replaceChildren(el("p", "viewer-error",
        error instanceof HttpError && error.message ? error.message : "本文を読み込めませんでした。もう一度お試しください。"), button);
    } finally {
      if (detailController === controller) article.removeAttribute("aria-busy");
    }
  }

  function renderDetail(detail) {
    const update = detail.update;
    const meta = el("div", "article-meta");
    meta.append(el("span", "status-badge", STATUS_LABELS[update.status] ?? (detail.tags.includes("Retirements") ? "廃止・退役" : "更新情報")),
      el("span", "article-date", `${formatDate(update.modified)} 更新`));
    const heading = el("h2", "article-title", update.title);
    const products = el("p", "article-products", update.products.join(" / "));
    const link = el("a", "source-link", `公式の記事を開く ↗  ·  ID ${update.id}`);
    link.href = update.url;
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    const body = el("div", "article-content");
    renderArticleHtml(body, detail.html, update.url);
    if (!body.textContent.trim()) body.append(el("p", null, "本文がありません。公式の記事をご確認ください。"));
    const note = el("p", "article-footnote", "本文は Microsoft の更新情報データを表示しています。チャットの回答では公式の出典を確認してください。");
    article.replaceChildren(meta, heading, products, link, body, note);
    article.scrollTop = 0;
  }

  async function search(initial = false) {
    searchController?.abort();
    const controller = new AbortController();
    searchController = controller;
    retry.hidden = true;
    count.textContent = "更新情報を読み込んでいます…";
    list.setAttribute("aria-busy", "true");
    const parameters = new URLSearchParams();
    for (const key of ["keyword", "product", "status"]) {
      const value = document.getElementById("updates-" + key).value.trim();
      if (value) parameters.set(key, value);
    }
    try {
      const result = await getJson("/api/updates?" + parameters, controller.signal);
      if (controller.signal.aborted) return;
      list.replaceChildren(...result.items.map(update => {
        const item = el("li");
        const button = el("button", "update-card");
        button.type = "button";
        button.dataset.updateId = update.id;
        button.append(el("span", "update-card-meta", `${formatDate(update.modified)} · ${STATUS_LABELS[update.status] ?? "更新情報"}`),
          el("span", "update-card-title", update.title),
          el("span", "update-card-product", update.products.join(" / ")));
        button.addEventListener("click", () => void select(update.id));
        item.append(button);
        return item;
      }));
      markSelected();
      count.textContent = result.items.length
        ? `${result.items.length} 件を表示 · 新しい順に最大 ${result.scanned} 件を検索、表示は ${result.limit} 件まで`
        : "条件に一致する更新はありません。キーワードや製品の条件を変えてください。";
      if (initial && !selectedId && !pendingId) {
        const requestedId = new URL(window.location.href).searchParams.get("id");
        if (requestedId || result.items[0]) await select(requestedId ?? result.items[0].id, false);
      }
    } catch (error) {
      if (controller.signal.aborted) return;
      list.replaceChildren();
      retry.hidden = false;
      count.textContent = error instanceof HttpError && error.message
        ? error.message : "一覧を読み込めませんでした。再読み込みしてください。";
    } finally {
      if (searchController === controller) list.removeAttribute("aria-busy");
    }
  }

  form.addEventListener("submit", event => { event.preventDefault(); void search(); });
  retry.addEventListener("click", () => void search(!selectedId && !pendingId));
  window.addEventListener("popstate", () => {
    const id = new URL(window.location.href).searchParams.get("id") ?? list.querySelector("button")?.dataset.updateId;
    if (id) void select(id, false);
  });
  return {
    start: () => {
      const id = new URL(window.location.href).searchParams.get("id");
      return id ? Promise.all([search(), select(id, false)]) : search(true);
    },
    select,
  };
}
