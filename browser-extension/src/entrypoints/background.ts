import { Connector } from "@/lib/connector";
import { RequestWatcher } from "@/lib/request-watcher";
import type {
  AppSyncConfig,
  DownloadRequest,
  ExtensionState,
  PopupMessage,
  PopupStatusResponse,
  DownloadableMedia,
} from "@/lib/types";
import {
  DEFAULT_PORT,
  DEFAULT_BYPASS_KEY,
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
 * 2. Intercept browser-native downloads (Hybrid: Chromium/Firefox)
 * 3. Monitor webRequest traffic via RequestWatcher for matching files/media
 * 4. Context menu "Download with SDM" actions
 * 5. Communicate with popup and content scripts for UI state
 */
export default defineBackground({
  type: "module",

  main() {
    // --- State ---

    const state: ExtensionState = {
      appConnected: false,
      appEnabled: false,
      userEnabled: true,
      activePort: DEFAULT_PORT,
      bypassKey: DEFAULT_BYPASS_KEY,
      isBypassActive: false,
      showPopups: true,
      silentDownload: false,
    };

    // Matching config from the app (rebuilt on each sync)
    let fileExts = new Set<string>();
    let blockedHosts = new Set<string>();

    let currentVideoList: readonly DownloadableMedia[] = [];

    // --- Media List Broadcast ---

    function broadcastMediaList(): void {
      browser.tabs.query({}).then((tabs: any[]) => {
        for (const tab of tabs) {
          if (tab.id) {
            const tabMedia = currentVideoList.filter(
              (m) => m.tabId === String(tab.id)
            );
            browser.tabs.sendMessage(tab.id, {
              type: "media-list-updated",
              media: tabMedia,
            }).catch(() => {
              // Ignore error for tabs without loaded content scripts
            });
          }
        }
      });
    }

    // --- Connector ---

    const connector = new Connector(
      (config: AppSyncConfig) => {
        state.appConnected = true;
        state.appEnabled = config.enabled;
        state.activePort = connector.getActivePort();

        // Rebuild Set-based lookups from the app config
        fileExts = new Set(config.fileExts.map((e) => e.toUpperCase()));
        blockedHosts = new Set(config.blockedHosts);

        // Push config to RequestWatcher
        requestWatcher.updateConfig({
          fileExts: new Set(config.fileExts),
          blockedHosts: new Set(config.blockedHosts),
          mediaExts: new Set(config.requestFileExts),
        });

        // Update video list and broadcast to content scripts
        if (config.videoList) {
          currentVideoList = config.videoList;
          broadcastMediaList();
        }

        updateIcon();
      },
      () => {
        state.appConnected = false;
        updateIcon();
      },
    );

    // ─── Request Watcher ──────────────────────────────────────────────────

    const requestWatcher = new RequestWatcher((data: any) => {
      if (!isMonitoringActive()) return;

      const mimeType = extractMimeType(data.responseHeaders) ?? undefined;
      // Determine if request is a media stream (HLS or video/audio content type)
      const isMedia =
        (mimeType && (mimeType.startsWith("audio/") || mimeType.startsWith("video/"))) ||
        (data.url && (data.url.includes(".m3u8") || data.url.includes(".ts")));

      const req: DownloadRequest = {
        url: data.url,
        fileName: data.fileName ?? undefined,
        cookies: data.cookies ?? undefined,
        requestHeaders: data.requestHeaders,
        responseHeaders: data.responseHeaders,
        referrer: data.tabUrl ?? undefined,
        fileSize: extractFileSize(data.responseHeaders) ?? undefined,
        mimeType: mimeType,
        tabUrl: data.tabUrl ?? undefined,
        silentDownload: state.silentDownload,
      };

      if (isMedia) {
        connector.sendMedia(req);
      } else {
        connector.sendDownload(req);
      }
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

        if (lastDotIdx !== -1) {
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
          .catch((err: any) => console.error("Icon update failed:", err));
      } catch (err) {
        /* fail silently to not break background script */
      }
    }

    // --- Download Interception (Hybrid: Chrome & Firefox Support) ---

    const isChromium = typeof (browser.downloads as any).onDeterminingFilename !== "undefined";

    if (isChromium) {
      // Chromium clean interception
      (browser.downloads as any).onDeterminingFilename.addListener(
        (
          item: Browser.downloads.DownloadItem,
          suggest: (suggestion?: Browser.downloads.FilenameSuggestion) => void,
        ) => {
          if (!isMonitoringActive() || state.isBypassActive) return;

          const url = item.finalUrl || item.url;
          if (!shouldIntercept(url, item.filename)) return;

          // Cancel the browser's native download immediately
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
    } else if (browser.downloads?.onCreated) {
      // Firefox fallback interception
      browser.downloads.onCreated.addListener(async (item: any) => {
        if (!isMonitoringActive() || state.isBypassActive) return;

        // Skip extension-initiated downloads to prevent loops
        if (item.byExtensionId) return;

        const url = item.finalUrl || item.url;
        if (!shouldIntercept(url, item.filename)) return;

        // Cancel and erase native download
        await browser.downloads.cancel(item.id);
        await browser.downloads.erase({ id: item.id });
        try {
          // Erase temporary file from Firefox if created
          await (browser.downloads as any).removeFile(item.id);
        } catch {
          // Safe to ignore if file wasn't created yet
        }

        // Route to SDM app instead
        triggerDownload(
          url,
          item.filename,
          item.referrer ?? null,
          item.fileSize,
          item.mime ?? null,
        );
      });
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
        .then((cookies: any[]) => {
          return cookies?.length
            ? cookies.map((c: any) => `${c.name}=${c.value}`).join("; ")
            : null;
        })
        .catch(() => undefined) // Fallback silently if no permission
        .then((cookieStr: string | null | undefined) => {
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
            silentDownload: state.silentDownload,
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

    browser.contextMenus.onClicked.addListener((info: any) => {
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

    browser.alarms.onAlarm.addListener((alarm: any) => {
      if (alarm.name === HEARTBEAT_ALARM) {
        connector.sync();
      }
    });

    // --- Popup & Content Script Message Handler ---
    const getStatusResponse = () => ({
      connected: state.appConnected,
      appEnabled: state.appEnabled,
      userEnabled: state.userEnabled,
      activePort: state.activePort,
      bypassKey: state.bypassKey,
      showPopups: state.showPopups,
      silentDownload: state.silentDownload,
    });

    browser.runtime.onMessage.addListener(
      (message: any, sender: any, sendResponse: (res?: any) => void) => {
        // Handle Popup Messages
        if (message.type === "get-status") {
          sendResponse(getStatusResponse());
        } else if (message.type === "set-enabled") {
          state.userEnabled = message.enabled;
          browser.storage.local.set({
            [STORAGE_KEYS.USER_ENABLED]: message.enabled,
          });
          updateIcon();
          sendResponse(getStatusResponse());
        } else if (message.type === "set-bypass-key") {
          state.bypassKey = message.key;
          browser.storage.local.set({
            [STORAGE_KEYS.BYPASS_KEY]: message.key,
          });
          sendResponse(getStatusResponse());
        } else if (message.type === "set-show-popups") {
          state.showPopups = message.enabled;
          browser.storage.local.set({
            [STORAGE_KEYS.SHOW_POPUPS]: message.enabled,
          });
          // Broadcast to tabs so content script can hide or show the widget
          browser.tabs.query({}).then((tabs: any[]) => {
            for (const tab of tabs) {
              if (tab.id) {
                browser.tabs.sendMessage(tab.id, {
                  type: "show-popups-changed",
                  enabled: message.enabled,
                }).catch(() => {});
              }
            }
          });
          sendResponse(getStatusResponse());
        } else if (message.type === "set-silent-download") {
          state.silentDownload = message.enabled;
          browser.storage.local.set({
            [STORAGE_KEYS.SILENT_DOWNLOAD]: message.enabled,
          });
          sendResponse(getStatusResponse());
        } else if (message.type === "set-bypass-state") {
          state.isBypassActive = message.active;
          sendResponse({ success: true });
        }
        
        // Handle Content Script Messages
        else if (message.type === "get-config") {
          const tabId = sender.tab?.id;
          const tabMedia = tabId
            ? currentVideoList.filter((m) => m.tabId === String(tabId))
            : [];
          sendResponse({
            userEnabled: state.userEnabled,
            appEnabled: state.appEnabled,
            connected: state.appConnected,
            bypassKey: state.bypassKey,
            showPopups: state.showPopups,
            media: tabMedia,
          });
        } else if (message.type === "download-links") {
          if (message.links && message.links.length > 0) {
            for (const link of message.links) {
              triggerDownload(link, null, sender.tab?.url ?? null, null, null);
            }
          }
          sendResponse({ success: true });
        } else if (message.type === "media-click") {
          if (message.mediaId) {
            fetch(`http://127.0.0.1:${connector.getActivePort()}/vid`, {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({
                Vid: message.mediaId,
                silentDownload: state.silentDownload,
              }),
            }).catch((err) => console.error("Failed to trigger video download:", err));
          }
          sendResponse({ success: true });
        }
        return true;
      },
    );

    // --- Sync Tab Titles to SDM Desktop App ---
    browser.tabs.onUpdated.addListener((tabId: number, changeInfo: any, tab: any) => {
      if (changeInfo.title && tab.url && state.appConnected) {
        fetch(`http://127.0.0.1:${connector.getActivePort()}/tab-update`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ TabUrl: tab.url, TabTitle: changeInfo.title }),
        }).catch(() => {});
      }
    });

    // --- Startup ---

    // Restore persisted user preference
    browser.storage.local.get(STORAGE_KEYS.USER_ENABLED).then((result: any) => {
      const stored = result[STORAGE_KEYS.USER_ENABLED];
      if (typeof stored === "boolean") state.userEnabled = stored;
    });

    // Restore persisted bypass key preference
    browser.storage.local.get(STORAGE_KEYS.BYPASS_KEY).then((result: any) => {
      const stored = result[STORAGE_KEYS.BYPASS_KEY];
      if (typeof stored === "string") state.bypassKey = stored;
    });

    // Restore persisted show popups preference
    browser.storage.local.get(STORAGE_KEYS.SHOW_POPUPS).then((result: any) => {
      const stored = result[STORAGE_KEYS.SHOW_POPUPS];
      if (typeof stored === "boolean") state.showPopups = stored;
    });

    // Restore persisted silent download preference
    browser.storage.local.get(STORAGE_KEYS.SILENT_DOWNLOAD).then((result: any) => {
      const stored = result[STORAGE_KEYS.SILENT_DOWNLOAD];
      if (typeof stored === "boolean") state.silentDownload = stored;
    });

    requestWatcher.register();
    connector.sync();
  },
});

