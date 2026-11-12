// siteproxy が中継したページに差し込むスクリプト(設計書5.1節、6.1節)。
// 配信時に先頭へ次の定数が付く: AUC_PARENT_ORIGINS、AUC_UPSTREAM_ORIGIN、AUC_PROXY_DATA_API、AUC_DATA_API_PREFIX。
(function () {
  "use strict";

  if (window.__aucBridgeLoaded) {
    return;
  }
  window.__aucBridgeLoaded = true;

  var PROTOCOL_VERSION = 1;
  var DEBOUNCE_MS = 300;
  var MAX_HEADING_LENGTH = 200;
  var MAX_PATH_LENGTH = 2048;
  var DATA_API_PROXY_PREFIX = "/__rc/";

  /* global AUC_PARENT_ORIGINS, AUC_UPSTREAM_ORIGIN, AUC_PROXY_DATA_API, AUC_DATA_API_PREFIX */
  var parentOrigins = typeof AUC_PARENT_ORIGINS !== "undefined" && Array.isArray(AUC_PARENT_ORIGINS)
    ? AUC_PARENT_ORIGINS.filter(function (o) { return typeof o === "string" && o !== "*" && o.length > 0; })
    : [];
  var upstreamOrigin = typeof AUC_UPSTREAM_ORIGIN === "string" ? AUC_UPSTREAM_ORIGIN : "https://azure.microsoft.com";
  var proxyDataApi = typeof AUC_PROXY_DATA_API !== "undefined" && AUC_PROXY_DATA_API === true;
  var dataApiPrefix = typeof AUC_DATA_API_PREFIX === "string" ? AUC_DATA_API_PREFIX : "";

  var isFramed = window.parent && window.parent !== window;

  // ---- 表示中のページの情報を親の画面へ知らせる ----

  function collapse(text) {
    return (text || "").replace(/\s+/g, " ").trim();
  }

  function pathSegments() {
    return location.pathname.split("/").filter(function (s) { return s.length > 0; });
  }

  function getUpdateId() {
    var id = new URLSearchParams(location.search).get("id");
    if (id) {
      return id;
    }
    // クエリがなければ、更新情報のパス(.../updates/<末尾>)の末尾から取る。
    var segments = pathSegments();
    var index = segments.lastIndexOf("updates");
    if (index < 0 || index === segments.length - 1) {
      return null;
    }
    var last;
    try {
      last = decodeURIComponent(segments[segments.length - 1]);
    } catch (e) {
      return null;
    }
    return /^[A-Za-z0-9._-]{1,200}$/.test(last) ? last : null;
  }

  function getLang() {
    var first = pathSegments()[0] || "";
    if (/^[a-z]{2}-[a-z]{2}$/i.test(first)) {
      return first.toLowerCase();
    }
    return (document.documentElement.getAttribute("lang") || "").toLowerCase();
  }

  function getHeading() {
    var h1 = document.querySelector("h1");
    return h1 ? collapse(h1.textContent).slice(0, MAX_HEADING_LENGTH) : "";
  }

  function collectPage() {
    return {
      type: "auc.page",
      version: PROTOCOL_VERSION,
      upstreamUrl: upstreamOrigin + location.pathname + location.search,
      path: location.pathname,
      updateId: getUpdateId(),
      title: collapse(document.title),
      heading: getHeading(),
      lang: getLang()
    };
  }

  var lastSentKey = null;
  var timer = null;

  function send() {
    timer = null;
    var page = collectPage();
    var key = JSON.stringify(page);
    if (key === lastSentKey) {
      return;
    }
    lastSentKey = key;
    page.ts = Date.now();
    // "*" は使わず、許可されたオリジンごとに送る。親のオリジンが違えばブラウザーが捨てる。
    parentOrigins.forEach(function (origin) {
      try {
        window.parent.postMessage(page, origin);
      } catch (e) {
        // 送れなくてもページの表示は続ける。
      }
    });
  }

  function schedule() {
    if (!isFramed || parentOrigins.length === 0) {
      return;
    }
    if (timer !== null) {
      clearTimeout(timer);
    }
    timer = setTimeout(send, DEBOUNCE_MS);
  }

  ["pushState", "replaceState"].forEach(function (name) {
    var original = history[name];
    if (typeof original !== "function") {
      return;
    }
    history[name] = function () {
      var result = original.apply(this, arguments);
      schedule();
      return result;
    };
  });

  window.addEventListener("popstate", schedule);
  window.addEventListener("hashchange", schedule);
  window.addEventListener("load", schedule);

  var lastTitle = document.title;
  if (typeof MutationObserver === "function") {
    new MutationObserver(function () {
      if (document.title !== lastTitle) {
        lastTitle = document.title;
        schedule();
      }
    }).observe(document.head || document.documentElement, { subtree: true, childList: true, characterData: true });
  }

  // ---- 親の画面からのページの移動 ----

  function isSafePath(path) {
    return typeof path === "string"
      && path.length > 0
      && path.length <= MAX_PATH_LENGTH
      && path.charAt(0) === "/"
      && path.charAt(1) !== "/"
      && path.charAt(1) !== "\\"
      && !/[\u0000-\u001f\u007f]/.test(path);
  }

  window.addEventListener("message", function (event) {
    if (parentOrigins.indexOf(event.origin) < 0 || event.source !== window.parent) {
      return;
    }
    var data = event.data;
    if (!data || typeof data !== "object" || data.type !== "auc.navigate" || data.version !== PROTOCOL_VERSION) {
      return;
    }
    if (isSafePath(data.path)) {
      location.assign(data.path);
    }
  });

  // ---- リンクのクリック ----

  document.addEventListener("click", function (event) {
    var target = event.target;
    var anchor = target && typeof target.closest === "function" ? target.closest("a[href]") : null;
    if (!anchor) {
      return;
    }
    var url;
    try {
      url = new URL(anchor.getAttribute("href"), location.href);
    } catch (e) {
      return;
    }
    if (url.protocol !== "https:" && url.protocol !== "http:") {
      return;
    }
    if (url.origin === upstreamOrigin) {
      // 上流のサイトへのリンクは siteproxy の中のパスに読み替える。
      anchor.setAttribute("href", url.pathname + url.search + url.hash);
      return;
    }
    if (url.origin !== location.origin) {
      // それ以外の外部リンクは iframe の中では開かず、新しいタブで開く。
      anchor.setAttribute("target", "_blank");
      anchor.setAttribute("rel", "noopener noreferrer");
    }
  }, true);

  // ---- データ API の呼び出しを siteproxy 経由にする(ProxyDataApi=true のときだけ) ----

  function toProxyUrl(input) {
    var url;
    try {
      url = new URL(String(input), location.href);
    } catch (e) {
      return null;
    }
    var href = url.href;
    if (dataApiPrefix && href.toLowerCase().indexOf(dataApiPrefix.toLowerCase()) === 0) {
      return DATA_API_PROXY_PREFIX + href.slice(dataApiPrefix.length);
    }
    return null;
  }

  if (proxyDataApi && dataApiPrefix) {
    var originalFetch = window.fetch;
    if (typeof originalFetch === "function") {
      window.fetch = function (input, init) {
        if (typeof Request === "function" && input instanceof Request) {
          var rewrittenRequest = toProxyUrl(input.url);
          if (rewrittenRequest !== null) {
            input = new Request(rewrittenRequest, input);
          }
        } else if (typeof input === "string" || input instanceof URL) {
          var rewritten = toProxyUrl(input);
          if (rewritten !== null) {
            input = rewritten;
          }
        }
        return originalFetch.call(window, input, init);
      };
    }

    var originalOpen = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function (method, url) {
      var args = Array.prototype.slice.call(arguments);
      var rewritten = toProxyUrl(url);
      if (rewritten !== null) {
        args[1] = rewritten;
      }
      return originalOpen.apply(this, args);
    };
  }

  schedule();
})();
