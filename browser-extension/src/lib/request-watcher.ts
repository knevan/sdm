import { browser, Browser } from "wxt/browser";
import { REQUEST_STALE_MS, SUPPORTED_PROTOCOLS } from "./config";
import { CapturedRequest, RequestWatcherConfig } from "./types";

/**
 * Monitors webRequest API to detect downloadable files by extension,
 * MIME type, and Content-Disposition header.
 */
export type RequestMatchCallback = (data: {
  url: string;
  requestHeaders: Record<string, string[]>;
  responseHeaders: Record<string, string[]>;
  cookies: string | null;
  tabUrl: string | null;
  fileName: string | null;
}) => void;

export class RequestWatcher {
  private readonly reqMap = new Map<string, CapturedRequest>();
  private fileExts = new Set<string>();
  private blockedHosts = new Set<string>();
  private mediaExts = new Set<string>();
  private pruneTimer: ReturnType<typeof setInterval> | null = null;

  constructor(private readonly onMatch: RequestMatchCallback) {}

  updateConfig(cfg: RequestWatcherConfig): void {
    this.fileExts = new Set(
      Array.from(cfg.fileExts).map((e) => e.toUpperCase()),
    );
    this.blockedHosts = new Set(cfg.blockedHosts);
    this.mediaExts = new Set(
      Array.from(cfg.mediaExts).map((e) => e.toUpperCase()),
    );
  }

  register(): void {
    const filter: Browser.webRequest.RequestFilter = {
      urls: ["http://*/*", "https://*/*"],
    };

    browser.webRequest.onSendHeaders.addListener(
      this.handleSendHeaders,
      filter,
      ["requestHeaders", "extraHeaders"],
    );
    browser.webRequest.onHeadersReceived.addListener(
      this.handleHeadersReceived,
      filter,
      ["responseHeaders", "extraHeaders"],
    );
    browser.webRequest.onErrorOccurred.addListener(this.handleError, filter);

    this.pruneTimer = setInterval(() => this.pruneStale(), REQUEST_STALE_MS);
  }

  unregister(): void {
    browser.webRequest.onSendHeaders.removeListener(this.handleSendHeaders);
    browser.webRequest.onHeadersReceived.removeListener(
      this.handleHeadersReceived,
    );
    browser.webRequest.onErrorOccurred.removeListener(this.handleError);
    if (this.pruneTimer) clearInterval(this.pruneTimer);
    this.reqMap.clear();
  }

  private readonly handleSendHeaders = (
    d: Browser.webRequest.OnSendHeadersDetails,
  ): void => {
    if (d.method !== "GET") return;
    this.reqMap.set(d.requestId, {
      requestId: d.requestId,
      url: d.url,
      method: d.method,
      tabId: d.tabId,
      requestHeaders: d.requestHeaders,
      timeStamp: d.timeStamp,
    });
  };

  private readonly handleHeadersReceived = (
    d: Browser.webRequest.OnHeadersReceivedDetails,
  ): Browser.webRequest.BlockingResponse | undefined => {
    const req = this.reqMap.get(d.requestId);
    if (!req) return;

    this.reqMap.delete(d.requestId);
    if (!this.isMatch(d)) return;

    if (req.tabId !== -1) {
      browser.tabs.get(req.tabId).then(
        (tab) => this.emitMatch(req, d, tab.url ?? null, tab.title ?? null),
        () => this.emitMatch(req, d, null, null),
      );
    } else {
      this.emitMatch(req, d, null, null);
    }

    return undefined;
  };

  private readonly handleError = (
    d: Browser.webRequest.OnErrorOccurredDetails,
  ): void => {
    this.reqMap.delete(d.requestId);
  };

  private isMatch(res: Browser.webRequest.OnHeadersReceivedDetails): boolean {
    let parsed: URL;
    try {
      parsed = new URL(res.url);
    } catch {
      return false;
    }

    const protocol = parsed.protocol;
    if (
      !SUPPORTED_PROTOCOLS.has(protocol) &&
      !SUPPORTED_PROTOCOLS.has(protocol.replace(":", ""))
    ) {
      return false;
    }

    if (this.isBlockedHost(parsed.hostname)) return false;

    const upperPath = parsed.pathname.toUpperCase();
    const lastDotIdx = upperPath.lastIndexOf(".");

    if (lastDotIdx !== -1) {
      const ext = upperPath.substring(lastDotIdx + 1);
      if (this.mediaExts.has(ext) || this.fileExts.has(ext)) {
        return true;
      }
    }

    // Check Content-Type for media MIME types
    const headers = res.responseHeaders ?? [];
    const ctHeader = headers.find(
      (h: any) => h.name.toUpperCase() === "CONTENT-TYPE",
    );
    if (ctHeader?.value) {
      const ct = ctHeader.value.toLowerCase();
      if (ct.startsWith("audio/") || ct.startsWith("video/")) return true;
    }

    // Check Content-Disposition for filename with known extension
    const cdHeader = headers.find(
      (h: any) => h.name.toUpperCase() === "CONTENT-DISPOSITION",
    );
    if (cdHeader?.value) {
      const cdUpper = cdHeader.value.toUpperCase();
      const match = cdUpper.match(/FILENAME[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
      if (match) {
        const filename = match[1].replace(/['"]/g, "");
        const fDotIdx = filename.lastIndexOf(".");
        if (fDotIdx !== -1) {
          const ext = filename.substring(fDotIdx + 1);
          if (this.fileExts.has(ext) || this.mediaExts.has(ext)) return true;
        }
      }
    }
    return false;
  }

  private isBlockedHost(hostname: string): boolean {
    if (this.blockedHosts.has(hostname)) return true;

    for (const blocked of this.blockedHosts) {
      if (hostname.includes(blocked)) return true;
    }
    return false;
  }

  private emitMatch(
    req: CapturedRequest,
    res: Browser.webRequest.OnHeadersReceivedDetails,
    tabUrl: string | null,
    fileName: string | null,
  ): void {
    const reqHeaders: Record<string, string[]> = {};
    const resHeaders: Record<string, string[]> = {};
    let cookies: string | null = null;

    // Collect request headers and extract cookies
    for (const h of req.requestHeaders ?? []) {
      if (!h.name || !h.value) continue;
      const lower = h.name.toLowerCase();
      if (lower === "cookie") {
        cookies = h.value;
        continue;
      }
      (reqHeaders[h.name] ??= []).push(h.value);
    }

    // Collect response headers
    for (const h of res.responseHeaders ?? []) {
      if (!h.name || !h.value) continue;
      (resHeaders[h.name] ??= []).push(h.value);
    }

    this.onMatch({
      url: res.url,
      requestHeaders: reqHeaders,
      responseHeaders: resHeaders,
      cookies,
      tabUrl,
      fileName,
    });
  }

  /** Remove correlation entries older than the stale threshold */
  private pruneStale(): void {
    const cutoff = Date.now() - REQUEST_STALE_MS;
    for (const [id, entry] of this.reqMap) {
      if (entry.timeStamp < cutoff) this.reqMap.delete(id);
    }
  }
}
