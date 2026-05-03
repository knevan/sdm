import { APP_BASE_URL, IPC_ENDPOINTS, IPC_TIMEOUT_MS } from "./config";
import { AppSyncConfig, DownloadRequest } from "./types";

/**
 * HTTP-based IPC connector to the SDM desktop app.
 *
 * Improvements over XDM Connector:
 * - AbortController-based timeout (no hanging fetch)
 * - Every POST response piggybacks config (eliminates extra sync round-trip)
 * - Explicit connect/disconnect state tracking
 */
export class Connector {
  private connected = false;

  constructor(
    private readonly onSync: (config: AppSyncConfig) => void,
    private readonly onDisconnect: () => void,
  ) {}

  isConnected(): boolean {
    return this.connected;
  }

  /** Heartbeat */
  async sync(): Promise<void> {
    try {
      const res = await this.timedFetch(APP_BASE_URL + IPC_ENDPOINTS.SYNC);
      if (!res.ok) throw new Error(`HTTP Error ${res.status}`);

      this.connected = true;
      const config: AppSyncConfig = await res.json();
      this.onSync(config);
    } catch {
      this.onDisconnect();
    }
  }

  /** Send download to SDM app. Response piggybacks updated config. */
  async sendDownload(request: DownloadRequest): Promise<void> {
    try {
      const res = await this.timedFetch(APP_BASE_URL + IPC_ENDPOINTS.DOWNLOAD, {
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

  private timedFetch(url: string, init?: RequestInit): Promise<Response> {
    const ctrl = new AbortController();
    const tid = setTimeout(() => ctrl.abort(), IPC_TIMEOUT_MS);
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
