export const DEFAULT_PORT = 8597;
export const PORT_SCAN_RANGE = { min: 8590, max: 8600 } as const;

export const IPC_ENDPOINTS = {
  SYNC: "/sync",
  DOWNLOAD: "/download",
  MEDIA: "/media",
} as const;

export const HEARTBEAT_ALARM = "sdm-heartbeat" as const;

/** MV3 minimum alarm interval is 0.5 minutes (30 seconds) */
export const HEARTBEAT_INTERVAL_MINUTES = 0.5;

/** Localhost-only fetch timeout */
export const IPC_TIMEOUT_MS = 2_000;

/** Max age for stale correlation map entries before pruning */
export const REQUEST_STALE_MS = 60_000;

export const SUPPORTED_PROTOCOLS = new Set(["http:", "https:"]);

export const MENU_IDS = {
  DOWNLOAD_LINK: "sdm-download-link",
  DOWNLOAD_IMAGE: "sdm-download-image",
} as const;

export const STORAGE_KEYS = {
  USER_ENABLED: "sdm_user_enabled",
  ACTIVE_PORT: "sdm_active_port",
  BYPASS_KEY: "sdm_bypass_key",
  SHOW_POPUPS: "sdm_show_popups",
  SILENT_DOWNLOAD: "sdm_silent_download",
} as const;

export const DEFAULT_BYPASS_KEY = "Delete";

