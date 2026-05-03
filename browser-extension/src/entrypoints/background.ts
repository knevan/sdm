import { Connector } from "@/lib/connector";
import { RequestWatcher } from "@/lib/request-watcher";
import type {
  AppSyncConfig,
  DownloadRequest,
  ExtensionState,
  PopupMessage,
  PopupStatusResponse,
} from "@/lib/types";
import {
  HEARTBEAT_ALARM,
  HEARTBEAT_INTERVAL_MINUTES,
  MENU_IDS,
  STORAGE_KEYS,
  SUPPORTED_PROTOCOLS,
} from "@/lib/config";

/**
 * Background service worker for SDM browser integration.
 *
 * Responsibilities:
 * 1. Periodic heartbeat sync with the SDM app via Connector
 * 2. Intercept browser-native downloads (onDeterminingFilename)
 * 3. Monitor webRequest traffic via RequestWatcher for matching files
 * 4. Context menu "Download with SDM" actions
 * 5. Respond to popup status/toggle messages
 */
export default defineBackground({
  type: "module",

  main() {
    // --- State ---

    const state: ExtensionState = {
      appConnected: false,
      appEnabled: false,
      userEnabled: true,
    };

    // Matching config from the app (rebuilt on each sync)
    let fileExts = new Set<string>();
    let blockedHosts = new Set<string>();

    // --- Connector ---

    const connector = new Connector(
      (config: AppSyncConfig) => {
        state.appConnected = true;
        state.appEnabled = config.enabled;

        // Rebuild Set-based lookups from the app config
        fileExts = new Set(config.fileExts.map((e) => e.toUpperCase()));
        blockedHosts = new Set(config.blockedHosts);

        // Push config to RequestWatcher
        requestWatcher.updateConfig({
          fileExts: new Set(config.fileExts),
          blockedHosts: new Set(config.blockedHosts),
          mediaExts: new Set(config.requestFileExts),
        });

        updateIcon();
      },
      () => {
        state.appConnected = false;
        updateIcon();
      },
    );

    // ─── Request Watcher ──────────────────────────────────────────────────

    const requestWatcher = new RequestWatcher((data) => {
      if (!isMonitoringActive()) return;

      const req: DownloadRequest = {
        url: data.url,
        fileName: data.fileName ?? undefined,
        cookies: data.cookies ?? undefined,
        requestHeaders: data.requestHeaders,
        responseHeaders: data.responseHeaders,
        referrer: data.tabUrl ?? undefined,
        fileSize: extractFileSize(data.responseHeaders) ?? undefined,
        mimeType: extractMimeType(data.responseHeaders) ?? undefined,
        tabUrl: data.tabUrl ?? undefined,
      };
      connector.sendDownload(req);
    });

    // --- Helpers ---

    function isMonitoringActive(): boolean {
      return state.appConnected && state.appEnabled && state.userEnabled;
    }

    function isSupportedUrl(url: string | undefined): boolean {
      if (!url) return false;
      try {
        return SUPPORTED_PROTOCOLS.has(new URL(url).protocol);
      } catch {
        return false;
      }
    }

    /** Check if a URL's file extension matches the app-configured list */
    function shouldIntercept(url: string, filename?: string): boolean {
      if (!isSupportedUrl(url)) return false;

      try {
        const parsed = new URL(url);

        if (blockedHosts.has(parsed.hostname)) return false;
        for (const bh of blockedHosts) {
          if (parsed.hostname.includes(bh)) return false;
        }

        // Check file extension from filename or URL path
        const pathToCheck = (filename ?? parsed.pathname).toUpperCase();
        const lastDotIdx = pathToCheck.lastIndexOf(".");

        if (lastDotIdx === -1) {
          const ext = pathToCheck.slice(lastDotIdx + 1);
          return fileExts.has(ext);
        }
      } catch {
        // Malformed URL — skip
      }
      return false;
    }

    function extractFileSize(
      headers: Record<string, readonly string[]>,
    ): number | null {
      const cl = headers["Content-Length"] ?? headers["content-length"];
      if (cl?.[0]) {
        const n = parseInt(cl[0], 10);
        return Number.isFinite(n) && n > 0 ? n : null;
      }
      return null;
    }

    function extractMimeType(
      headers: Record<string, readonly string[]>,
    ): string | null {
      const ct = headers["Content-Type"] ?? headers["content-type"];
      return ct?.[0] ?? null;
    }

    function updateIcon(): void {
      const suffix = "";

      try {
        browser.action
          .setIcon({
            path: {
              "16": `/icon/16${suffix}.png`,
              "32": `/icon/32${suffix}.png`,
              "48": `/icon/48${suffix}.png`,
              "128": `/icon/128${suffix}.png`,
            },
          })
          .catch((err) => console.error("Icon update failed:", err));
      } catch (err) {
        /* fail silently to not break background script */
      }
    }

    // --- Download Interception ---
    // Intercept browser-native downloads via the downloads API.
    // onDeterminingFilename fires after the browser resolves the filename,
    // giving us the final URL, filename, MIME type, and file size.
    if (browser.downloads?.onDeterminingFilename) {
      browser.downloads.onDeterminingFilename.addListener(
        (
          item: Browser.downloads.DownloadItem,
          suggest: (suggestion?: Browser.downloads.FilenameSuggestion) => void,
        ) => {
          if (!isMonitoringActive()) return;

          const url = item.finalUrl || item.url;
          if (!shouldIntercept(url, item.filename)) return;

          // Cancel the browser's native download
          browser.downloads.cancel(item.id).then(() => {
            browser.downloads.erase({ id: item.id });
          });

          // Route to SDM app instead
          triggerDownload(
            url,
            item.filename,
            item.referrer ?? null,
            item.fileSize,
            item.mime ?? null,
          );
        },
      );
    }

    // --- Download Trigger ---

    function triggerDownload(
      url: string,
      file: string | null,
      referrer: string | null,
      size: number | null,
      mime: string | null,
    ): void {
      // Retrieve cookies for the target URL for authenticated downloads
      browser.cookies
        .getAll({ url })
        .then((cookies) => {
          return cookies?.length
            ? cookies.map((c) => `${c.name}=${c.value}`).join("; ")
            : null;
        })
        .catch(() => undefined) // Fallback silently if no permission
        .then((cookieStr) => {
          const reqHeaders: Record<string, string[]> = {
            "User-Agent": [navigator.userAgent],
          };

          if (referrer) reqHeaders["Referer"] = [referrer];

          const resHeaders: Record<string, string[]> = {};
          if (size && size > 0) resHeaders["Content-Length"] = [String(size)];
          if (mime) resHeaders["Content-Type"] = [mime];

          const request: DownloadRequest = {
            url,
            fileName: file ?? undefined,
            cookies: cookieStr ?? undefined,
            requestHeaders: reqHeaders,
            responseHeaders: resHeaders,
            referrer: referrer ?? undefined,
            fileSize: size ?? undefined,
            mimeType: mime ?? undefined,
            tabUrl: referrer ?? undefined,
          };

          connector.sendDownload(request);
        });
    }

    // --- Context Menu ---
    browser.runtime.onInstalled.addListener(() => {
      browser.contextMenus.removeAll().then(() => {
        browser.contextMenus.create({
          id: MENU_IDS.DOWNLOAD_LINK,
          title: "Download with SDM",
          contexts: ["link", "video", "audio"],
        });

        browser.contextMenus.create({
          id: MENU_IDS.DOWNLOAD_IMAGE,
          title: "Download Image with SDM",
          contexts: ["image"],
        });
      });
    });

    browser.contextMenus.onClicked.addListener((info) => {
      if (!connector.isConnected()) return;

      let url: string | undefined;
      const menuId = info.menuItemId;

      if (menuId === MENU_IDS.DOWNLOAD_IMAGE) {
        url = info.srcUrl ?? info.linkUrl ?? info.pageUrl;
      } else if (menuId === MENU_IDS.DOWNLOAD_LINK) {
        url = info.linkUrl ?? info.srcUrl ?? info.pageUrl;
      }

      if (url && isSupportedUrl(url)) {
        triggerDownload(url, null, info.pageUrl ?? null, null, null);
      }
    });

    // --- Alarm Heartbeat ---
    browser.alarms.create(HEARTBEAT_ALARM, {
      periodInMinutes: HEARTBEAT_INTERVAL_MINUTES,
      when: Date.now() + 1_000,
    });

    browser.alarms.onAlarm.addListener((alarm) => {
      if (alarm.name === HEARTBEAT_ALARM) {
        connector.sync();
      }
    });

    // --- Popup Message Handler ---
    browser.runtime.onMessage.addListener(
      (
        message: PopupMessage,
        _sender: Browser.runtime.MessageSender,
        sendResponse: (response: PopupStatusResponse) => void,
      ) => {
        if (message.type === "get-status") {
          sendResponse({
            connected: state.appConnected,
            appEnabled: state.appEnabled,
            userEnabled: state.userEnabled,
          });
        } else if (message.type === "set-enabled") {
          state.userEnabled = message.enabled;
          // Persist user preference
          browser.storage.local.set({
            [STORAGE_KEYS.USER_ENABLED]: message.enabled,
          });
          updateIcon();
          sendResponse({
            connected: state.appConnected,
            appEnabled: state.appEnabled,
            userEnabled: state.userEnabled,
          });
        }
        return true;
      },
    );

    // --- Startup ---

    // Restore persisted user preference
    browser.storage.local.get(STORAGE_KEYS.USER_ENABLED).then((result) => {
      const stored = result[STORAGE_KEYS.USER_ENABLED];
      if (typeof stored === "boolean") state.userEnabled = stored;
    });

    requestWatcher.register();
    connector.sync();
  },
});
