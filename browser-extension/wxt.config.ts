import { defineConfig } from "wxt";

// See https://wxt.dev/api/config.html
export default defineConfig({
  srcDir: "src",
  modules: ["@wxt-dev/module-svelte"],
  manifest: {
    name: "S Download Manager",
    description: "Download with S Download Manager",
    version: "0.0.1",
    permissions: [
      "downloads", // Intercept and cancel browser-native downloads
      "cookies", // Retrieve cookies for authenticated downloads
      "webRequest", // Monitor network requests for file detection
      "tabs", // Access tab URL/title for referrer context
      "contextMenus", // Right-click "Download with SDM" menu
      "alarms", // Periodic heartbeat sync with the SDM app
      "storage", // Persist user preferences (e.g. monitoring toggle)
    ],
    // Match all HTTP/HTTPS origins for download interception
    host_permissions: ["*://*/*"],
  },
  webExt: {
    binaries: {
      chrome:
        "C:/Program Files/BraveSoftware/Brave-Browser-Beta/Application/brave.exe",
    },
  },
});
