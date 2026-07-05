<script lang="ts">
  import { onMount } from "svelte";
  import { browser } from "wxt/browser";
  import type { PopupStatusResponse } from "@/lib/types";

  let connected = $state(false);
  let appEnabled = $state(false);
  let userEnabled = $state(true);
  let showPopups = $state(true);
  let silentDownload = $state(false);
  let activePort = $state(8597);
  let bypassKey = $state("Delete");
  let loading = $state(true);

  // Expandable panel state
  let showMoreSettings = $state(false);

  const availableBypassKeys = ["Delete", "Shift", "Control", "Alt"];

  onMount(async () => {
    try {
      const res: PopupStatusResponse = await browser.runtime.sendMessage({
        type: "get-status",
      });
      connected = res.connected;
      appEnabled = res.appEnabled;
      userEnabled = res.userEnabled;
      activePort = res.activePort;
      bypassKey = res.bypassKey;
      showPopups = res.showPopups;
      silentDownload = res.silentDownload;
    } catch {
      connected = false;
    } finally {
      loading = false;
    }
  });

  async function toggleAutoCapture(): Promise<void> {
    const next = !userEnabled;
    try {
      const res: PopupStatusResponse = await browser.runtime.sendMessage({
        type: "set-enabled",
        enabled: next,
      });
      userEnabled = res.userEnabled;
    } catch {
      /* ignore */
    }
  }

  async function toggleShowPopups(): Promise<void> {
    const next = !showPopups;
    try {
      const res: PopupStatusResponse = await browser.runtime.sendMessage({
        type: "set-show-popups",
        enabled: next,
      });
      showPopups = res.showPopups;
    } catch {
      /* ignore */
    }
  }

  async function toggleSilentDownload(): Promise<void> {
    const next = !silentDownload;
    try {
      const res: PopupStatusResponse = await browser.runtime.sendMessage({
        type: "set-silent-download",
        enabled: next,
      });
      silentDownload = res.silentDownload;
    } catch {
      /* ignore */
    }
  }

  async function changeBypassKey(e: Event): Promise<void> {
    const target = e.target as HTMLSelectElement;
    const nextKey = target.value;
    try {
      const res: PopupStatusResponse = await browser.runtime.sendMessage({
        type: "set-bypass-key",
        key: nextKey,
      });
      bypassKey = res.bypassKey;
    } catch {
      /* ignore */
    }
  }
</script>

<main class="sdm-popup-container">
  <!-- 1. Header (Quick Settings + Connection Status) -->
  <header class="sdm-header">
    <div class="sdm-logo-wrapper">
      <svg class="sdm-logo-svg" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
        <circle cx="12" cy="12" r="10" stroke="url(#logo-grad)" stroke-width="2.5"/>
        <path d="M12 7V17M12 17L8 13M12 17L16 13" stroke="url(#logo-grad)" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>
        <defs>
          <linearGradient id="logo-grad" x1="0" y1="0" x2="24" y2="24" gradientUnits="userSpaceOnUse">
            <stop stop-color="#818cf8" />
            <stop offset="1" stop-color="#c084fc" />
          </linearGradient>
        </defs>
      </svg>
    </div>
    <div class="sdm-header-text">
      <div class="sdm-header-title">SDM Quick Settings</div>
      <div class="sdm-status-text">
        {#if loading}
          <span class="sdm-statusChecking">Checking...</span>
        {:else if !connected}
          <span class="sdm-statusDisconnected">Not Connected</span>
        {:else if !appEnabled}
          <span class="sdm-statusDisabled">Disabled in SDM</span>
        {:else}
          <span class="sdm-statusConnected">Connected</span>
        {/if}
      </div>
    </div>
  </header>

  <!-- 2. Feature Toggles (Compact List Options) -->
  <div class="sdm-options-list">
    <!-- Auto Capture Links -->
    <button class="sdm-option-row" onclick={toggleAutoCapture} aria-label="Toggle auto capture">
      <span class="sdm-option-label">Auto Capture Links</span>
      <div class="sdm-checkbox-wrapper">
        <input type="checkbox" checked={userEnabled} class="sdm-checkbox" readonly />
      </div>
    </button>

    <!-- Show Popups -->
    <button class="sdm-option-row" onclick={toggleShowPopups} aria-label="Toggle popups">
      <span class="sdm-option-label">Show popups</span>
      <div class="sdm-checkbox-wrapper">
        <input type="checkbox" checked={showPopups} class="sdm-checkbox" readonly />
      </div>
    </button>

    <!-- Add downloads silently -->
    <button class="sdm-option-row" onclick={toggleSilentDownload} aria-label="Toggle silent downloads">
      <span class="sdm-option-label">Add downloads silently</span>
      <div class="sdm-checkbox-wrapper">
        <input type="checkbox" checked={silentDownload} class="sdm-checkbox" readonly />
      </div>
    </button>

    <!-- More settings (Expandable Drawer) -->
    <button class="sdm-option-row" onclick={() => showMoreSettings = !showMoreSettings} aria-label="Toggle advanced settings">
      <span class="sdm-option-label">More settings</span>
      <div class="sdm-icon-wrapper">
        <svg class="sdm-gear-icon" class:sdm-rotate={showMoreSettings} viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
          <path d="M12 15C13.6569 15 15 13.6569 15 12C15 10.3431 13.6569 9 12 9C10.3431 9 9 10.3431 9 12C9 13.6569 10.3431 15 12 15Z" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
          <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
      </div>
    </button>
  </div>

  <!-- 3. Expanded Settings Details -->
  {#if showMoreSettings}
    <div class="sdm-more-drawer">
      <div class="sdm-drawer-item">
        <span>Bypass holding key:</span>
        <select value={bypassKey} onchange={changeBypassKey} class="sdm-select">
          {#each availableBypassKeys as key}
            <option value={key}>{key}</option>
          {/each}
        </select>
      </div>
      {#if connected}
        <div class="sdm-drawer-item sdm-port-info">
          <span>Active Port:</span>
          <span class="sdm-port-val">{activePort}</span>
        </div>
      {/if}
    </div>
  {/if}
</main>

<style>
  .sdm-popup-container {
    width: 250px;
    background: #14161f;
    color: #e2e8f0;
    padding: 0;
    margin: 0;
    box-sizing: border-box;
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
    user-select: none;
    overflow: hidden;
  }

  /* --- Header Styling --- */
  .sdm-header {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 12px 14px;
    background: #191c28;
    border-bottom: 1.5px solid rgba(255, 255, 255, 0.05);
  }

  .sdm-logo-wrapper {
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .sdm-logo-svg {
    width: 28px;
    height: 28px;
  }

  .sdm-header-text {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .sdm-header-title {
    font-size: 13px;
    font-weight: 700;
    color: #f1f5f9;
  }

  .sdm-status-text {
    font-size: 11px;
    font-weight: 600;
  }

  .sdm-statusConnected {
    color: #10b981;
  }

  .sdm-statusDisconnected {
    color: #ef4444;
  }

  .sdm-statusChecking {
    color: #94a3b8;
  }

  .sdm-statusDisabled {
    color: #f59e0b;
  }

  /* --- Options List --- */
  .sdm-options-list {
    display: flex;
    flex-direction: column;
  }

  .sdm-option-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    width: 100%;
    background: transparent;
    border: none;
    padding: 12px 14px;
    color: #cbd5e1;
    cursor: pointer;
    text-align: left;
    transition: background 0.15s ease, color 0.15s ease;
    border-bottom: 1px solid rgba(255, 255, 255, 0.03);
  }

  .sdm-option-row:last-child {
    border-bottom: none;
  }

  .sdm-option-row:hover {
    background: rgba(99, 102, 241, 0.08);
    color: #f1f5f9;
  }

  .sdm-option-label {
    font-size: 12px;
    font-weight: 500;
  }

  /* --- Custom Checkbox Styling --- */
  .sdm-checkbox-wrapper {
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .sdm-checkbox {
    appearance: none;
    -webkit-appearance: none;
    width: 18px;
    height: 18px;
    border-radius: 5px;
    border: 1.5px solid rgba(255, 255, 255, 0.2);
    background: rgba(255, 255, 255, 0.04);
    cursor: pointer;
    position: relative;
    transition: all 0.2s ease;
    display: flex;
    align-items: center;
    justify-content: center;
    margin: 0;
    pointer-events: none; /* Let the row click handle it */
  }

  .sdm-checkbox:checked {
    background: #6366f1;
    border-color: #6366f1;
    box-shadow: 0 0 6px rgba(99, 102, 241, 0.5);
  }

  .sdm-checkbox:checked::after {
    content: "✓";
    color: #ffffff;
    font-size: 11px;
    font-weight: 800;
  }

  /* --- Gear Icon --- */
  .sdm-icon-wrapper {
    display: flex;
    align-items: center;
    justify-content: center;
    color: #94a3b8;
  }

  .sdm-gear-icon {
    width: 16px;
    height: 16px;
    transition: transform 0.25s ease, color 0.15s ease;
  }

  .sdm-option-row:hover .sdm-gear-icon {
    color: #818cf8;
  }

  .sdm-gear-icon.sdm-rotate {
    transform: rotate(45deg);
  }

  /* --- More Settings Drawer --- */
  .sdm-more-drawer {
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 10px 14px 14px 14px;
    background: #11131a;
    border-top: 1.5px solid rgba(255, 255, 255, 0.04);
    animation: slideDown 0.2s ease-out;
  }

  .sdm-drawer-item {
    display: flex;
    justify-content: space-between;
    align-items: center;
    font-size: 11px;
    color: #94a3b8;
  }

  .sdm-select {
    background: #1e2230;
    color: #f1f5f9;
    border: 1px solid rgba(255, 255, 255, 0.1);
    border-radius: 5px;
    font-size: 10px;
    padding: 2px 4px;
    cursor: pointer;
    outline: none;
    transition: border-color 0.15s ease;
  }

  .sdm-select:focus {
    border-color: #6366f1;
  }

  .sdm-port-info {
    border-top: 1px solid rgba(255, 255, 255, 0.03);
    padding-top: 6px;
    margin-top: 4px;
  }

  .sdm-port-val {
    background: rgba(99, 102, 241, 0.15);
    color: #818cf8;
    padding: 1px 4px;
    border-radius: 3px;
    font-family: monospace;
    font-weight: 700;
  }

  /* --- Keyframes --- */
  @keyframes slideDown {
    from {
      opacity: 0;
      transform: translateY(-5px);
    }
    to {
      opacity: 1;
      transform: translateY(0);
    }
  }
</style>
