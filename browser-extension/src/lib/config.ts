export const APP_BASE_URL = "http://127.0.0.1:8597" as const;

export const IPC_ENDPOINTS = {
  SYNC: "/sync",
  DOWNLOAD: "/download",
} as const;

export const HEARTBEAT_ALARM = "sdm-heartbeat" as const;

/** MV3 minimum alarm interval is 0.5 minutes (30 seconds) */
export const HEARTBEAT_INTERVAL_MINUTES = 0.5;

/** Localhost-only fetch timeout */
export const IPC_TIMEOUT_MS = 3_000;

/** Max age for stale correlation map entries before pruning */
export const REQUEST_STALE_MS = 60_000;

export const SUPPORTED_PROTOCOLS = new Set(["http:", "https:"]);

export const MENU_IDS = {
  DOWNLOAD_LINK: "sdm-download-link",
  DOWNLOAD_IMAGE: "sdm-download-image",
} as const;

export const STORAGE_KEYS = {
  USER_ENABLED: "sdm_user_enabled",
} as const;
