import { browser } from "wxt/browser";
import {
  DEFAULT_PORT,
  IPC_ENDPOINTS,
  IPC_TIMEOUT_MS,
  PORT_SCAN_RANGE,
  STORAGE_KEYS,
} from "./config";
import { AppSyncConfig, DownloadRequest } from "./types";

/**
 * HTTP-based IPC connector to the SDM desktop app.
 *
 * Improvements over XDM Connector:
 * - Dynamic port scanning and auto-discovery
 * - AbortController-based timeout (no hanging fetch)
 * - Every POST response piggybacks config (eliminates extra sync round-trip)
 * - Explicit connect/disconnect state tracking
 */
export class Connector {
  private connected = false;
  private activePort = DEFAULT_PORT;

  constructor(
    private readonly onSync: (config: AppSyncConfig) => void,
    private readonly onDisconnect: () => void,
  ) {
    // Restore persisted active port
    browser.storage.local.get(STORAGE_KEYS.ACTIVE_PORT).then((res) => {
      const stored = res[STORAGE_KEYS.ACTIVE_PORT];
      if (typeof stored === "number") {
        this.activePort = stored;
      }
    });
  }

  isConnected(): boolean {
    return this.connected;
  }

  getActivePort(): number {
    return this.activePort;
  }

  /** Heartbeat */
  async sync(): Promise<void> {
    try {
      const url = `http://127.0.0.1:${this.activePort}${IPC_ENDPOINTS.SYNC}`;
      const res = await this.timedFetch(url);
      if (!res.ok) throw new Error(`HTTP Error ${res.status}`);

      this.connected = true;
      const config: AppSyncConfig = await res.json();
      this.onSync(config);
    } catch {
      // Current port failed, try scanning to discover active port
      const foundPort = await this.scanPorts();
      if (foundPort !== null) {
        this.activePort = foundPort;
        // Try sync again on the newly found port
        try {
          const url = `http://127.0.0.1:${this.activePort}${IPC_ENDPOINTS.SYNC}`;
          const res = await this.timedFetch(url);
          if (res.ok) {
            this.connected = true;
            const config: AppSyncConfig = await res.json();
            this.onSync(config);
            return;
          }
        } catch {
          // ignore fallback fail
        }
      }
      this.disconnect();
    }
  }

  /** Send download to SDM app. Response piggybacks updated config. */
  async sendDownload(request: DownloadRequest): Promise<void> {
    try {
      const url = `http://127.0.0.1:${this.activePort}${IPC_ENDPOINTS.DOWNLOAD}`;
      const res = await this.timedFetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(request),
      });
      this.connected = true;
      const config: AppSyncConfig = await res.json();
      this.onSync(config);
    } catch {
      this.disconnect();
    }
  }

  /** Send media URL to SDM app. Response piggybacks updated config. */
  async sendMedia(request: DownloadRequest): Promise<void> {
    try {
      const url = `http://127.0.0.1:${this.activePort}${IPC_ENDPOINTS.MEDIA}`;
      const res = await this.timedFetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(request),
      });
      this.connected = true;
      const config: AppSyncConfig = await res.json();
      this.onSync(config);
    } catch {
      this.disconnect();
    }
  }

  /** Scans ports in configured range to auto-discover active SDM instance */
  private async scanPorts(): Promise<number | null> {
    const ports: number[] = [];
    for (let p = PORT_SCAN_RANGE.min; p <= PORT_SCAN_RANGE.max; p++) {
      ports.push(p);
    }

    const promises = ports.map(async (port) => {
      try {
        const url = `http://127.0.0.1:${port}${IPC_ENDPOINTS.SYNC}`;
        // Use a short 500ms timeout for scanning
        const res = await this.timedFetch(url, {}, 500);
        if (res.ok) {
          const config = await res.json();
          if (config && typeof config.enabled !== "undefined") {
            return port;
          }
        }
      } catch {
        // failed or timed out, ignore
      }
      return null;
    });

    const results = await Promise.all(promises);
    const foundPort = results.find((p) => p !== null);
    if (typeof foundPort === "number") {
      await browser.storage.local.set({
        [STORAGE_KEYS.ACTIVE_PORT]: foundPort,
      });
      return foundPort;
    }
    return null;
  }

  private timedFetch(
    url: string,
    init?: RequestInit,
    timeoutMs = IPC_TIMEOUT_MS,
  ): Promise<Response> {
    const ctrl = new AbortController();
    const tid = setTimeout(() => ctrl.abort(), timeoutMs);
    return fetch(url, { ...init, signal: ctrl.signal }).finally(() =>
      clearTimeout(tid),
    );
  }

  private disconnect(): void {
    if (this.connected) {
      this.connected = false;
      this.onDisconnect();
    }
  }
}
