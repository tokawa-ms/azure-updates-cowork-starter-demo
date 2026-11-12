// BFF への要求と、POST で受け取る SSE の読み取り(設計書5.2節、6.3節)。
// EventSource は GET しか使えないため、fetch と ReadableStream で読む。

const REQUEST_HEADERS = Object.freeze({
  "Content-Type": "application/json",
  "X-AUC-Request": "1",
});

/** BFF が 2xx 以外を返したときの例外。 */
export class HttpError extends Error {
  constructor(status, code, message, retryAfterSeconds) {
    super(message);
    this.name = "HttpError";
    this.status = status;
    this.code = code;
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

async function toHttpError(response) {
  let code = "http_" + response.status;
  let message = "";
  try {
    const body = await response.json();
    if (body && typeof body === "object") {
      if (typeof body.code === "string") {
        code = body.code;
      }
      if (typeof body.message === "string") {
        message = body.message;
      }
    }
  } catch {
    // 本文が JSON でなければ、状態コードだけで扱う。
  }
  const retryAfter = Number.parseInt(response.headers.get("Retry-After") ?? "", 10);
  return new HttpError(response.status, code, message, Number.isFinite(retryAfter) ? retryAfter : null);
}

/** JSON を GET する。 */
export async function getJson(url, signal) {
  const response = await fetch(url, {
    method: "GET",
    headers: { Accept: "application/json" },
    credentials: "same-origin",
    cache: "no-store",
    signal,
  });
  if (!response.ok) {
    throw await toHttpError(response);
  }
  return response.json();
}

/** JSON を POST する。本文がない応答(204)では null を返す。 */
export async function postJson(url, body, signal) {
  const response = await fetch(url, {
    method: "POST",
    headers: { ...REQUEST_HEADERS, Accept: "application/json" },
    body: JSON.stringify(body ?? {}),
    credentials: "same-origin",
    signal,
  });
  if (!response.ok) {
    throw await toHttpError(response);
  }
  if (response.status === 204) {
    return null;
  }
  const contentType = response.headers.get("Content-Type") ?? "";
  return contentType.includes("json") ? response.json() : null;
}

/**
 * POST して SSE を読む。イベントごとに onEvent(name, data) を呼ぶ。ping は無視する。
 * signal を中止すると読み取りをやめ、BFF 側もエージェントへの要求を取り消す。
 */
export async function postSse(url, body, onEvent, signal) {
  const response = await fetch(url, {
    method: "POST",
    headers: { ...REQUEST_HEADERS, Accept: "text/event-stream" },
    body: JSON.stringify(body),
    credentials: "same-origin",
    cache: "no-store",
    signal,
  });
  if (!response.ok) {
    throw await toHttpError(response);
  }
  const contentType = response.headers.get("Content-Type") ?? "";
  if (!contentType.startsWith("text/event-stream") || !response.body) {
    throw new HttpError(response.status, "invalid_response", "", null);
  }

  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = "";
  let eventName = "";
  let dataLines = [];

  const dispatch = () => {
    if (dataLines.length === 0) {
      eventName = "";
      return;
    }
    const name = eventName || "message";
    const raw = dataLines.join("\n");
    eventName = "";
    dataLines = [];
    if (name === "ping") {
      return;
    }
    let data;
    try {
      data = JSON.parse(raw);
    } catch {
      return;
    }
    onEvent(name, data);
  };

  const handleLine = (line) => {
    if (line === "") {
      dispatch();
      return;
    }
    if (line.startsWith(":")) {
      return;
    }
    const colon = line.indexOf(":");
    const field = colon < 0 ? line : line.slice(0, colon);
    let value = colon < 0 ? "" : line.slice(colon + 1);
    if (value.startsWith(" ")) {
      value = value.slice(1);
    }
    if (field === "event") {
      eventName = value;
    } else if (field === "data") {
      dataLines.push(value);
    }
  };

  try {
    for (;;) {
      const { value, done } = await reader.read();
      if (done) {
        break;
      }
      buffer += value;
      let newline;
      while ((newline = buffer.indexOf("\n")) >= 0) {
        let line = buffer.slice(0, newline);
        buffer = buffer.slice(newline + 1);
        if (line.endsWith("\r")) {
          line = line.slice(0, -1);
        }
        handleLine(line);
      }
    }
    // 最後の空行がないまま終わったイベントも渡す。
    if (buffer.length > 0) {
      handleLine(buffer.endsWith("\r") ? buffer.slice(0, -1) : buffer);
    }
    dispatch();
  } finally {
    reader.releaseLock();
  }
}
