import type { Browser } from "wxt/browser";

/**
 * Download request payload sent from extension to App
 * */
export interface DownloadRequest {
  readonly url: string;
  readonly fileName?: string;
  readonly cookies?: string;
  readonly requestHeaders: Readonly<Record<string, readonly string[]>>;
  readonly responseHeaders: Readonly<Record<string, readonly string[]>>;
  readonly referrer?: string;
  readonly fileSize?: number;
  readonly mimeType?: string;
  readonly tabUrl?: string;
  readonly tabId?: string;
  readonly silentDownload?: boolean;
}

/**
 * Downloadable media structure synced from desktop app.
 */
export interface DownloadableMedia {
  readonly id: string;
  readonly text: string;
  readonly info?: string;
  readonly tabId?: string;
}

/**
 * Configuration payload returned by the SDM app on every /sync or POST response.
 * Used to keep the extension in sync with the app's current settings.
 */
export interface AppSyncConfig {
  readonly enabled: boolean;
  readonly fileExts: readonly string[];
  readonly blockedHosts: readonly string[];
  readonly requestFileExts: readonly string[];
  readonly videoList?: readonly DownloadableMedia[];
}

export interface ExtensionState {
  appConnected: boolean;
  appEnabled: boolean;
  userEnabled: boolean;
  activePort: number;
  bypassKey: string;
  isBypassActive?: boolean;
  showPopups: boolean;
  silentDownload: boolean;
}

/**
 * Captured request headers from webRequest.onSendHeaders.
 * Stored temporarily until the corresponding response arrives.
 */
export interface CapturedRequest {
  readonly requestId: string;
  readonly url: string;
  readonly method: string;
  readonly tabId: number;
  readonly requestHeaders: Browser.webRequest.HttpHeader[] | undefined;
  readonly timeStamp: number;
}

/** Matching config consumed by RequestWatcher */
export interface RequestWatcherConfig {
  readonly fileExts: ReadonlySet<string>;
  readonly blockedHosts: ReadonlySet<string>;
  readonly mediaExts: ReadonlySet<string>;
}

export type PopupMessage =
  | { readonly type: "get-status" }
  | {
      readonly type: "set-enabled";
      readonly enabled: boolean;
    }
  | {
      readonly type: "set-bypass-key";
      readonly key: string;
    }
  | {
      readonly type: "set-show-popups";
      readonly enabled: boolean;
    }
  | {
      readonly type: "set-silent-download";
      readonly enabled: boolean;
    };

export interface PopupStatusResponse {
  readonly connected: boolean;
  readonly appEnabled: boolean;
  readonly userEnabled: boolean;
  readonly activePort: number;
  readonly bypassKey: string;
  readonly showPopups: boolean;
  readonly silentDownload: boolean;
}

export type ContentMessage =
  | { readonly type: "media-list-updated"; readonly media: readonly DownloadableMedia[] }
  | { readonly type: "get-config" }
  | { readonly type: "download-links"; readonly links: readonly string[] }
  | { readonly type: "media-click"; readonly mediaId: string };

