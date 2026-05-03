<script lang="ts">
  import { onMount } from "svelte";
  import type { PopupStatusResponse } from "@/lib/types";

  let connected = $state(false);
  let appEnabled = $state(false);
  let userEnabled = $state(true);
  let loading = $state(true);
  let isActive = $derived(connected && appEnabled && userEnabled);

  onMount(async () => {
    try {
      const res: PopupStatusResponse = await browser.runtime.sendMessage({
        type: "get-status",
      });
      connected = res.connected;
      appEnabled = res.appEnabled;
      userEnabled = res.userEnabled;
    } catch {
      connected = false;
    } finally {
      loading = false;
    }
  });

  async function toggleMonitoring(): Promise<void> {
    const next = !userEnabled;
    try {
      const res: PopupStatusResponse = await browser.runtime.sendMessage({
        type: "set-enabled",
        enabled: next,
      });
      connected = res.connected;
      appEnabled = res.appEnabled;
      userEnabled = res.userEnabled;
    } catch {
      /* ignore */
    }
  }
</script>

<main>
  <header>
    <span class="logo">⬇</span>
    <h1>SDM Extension</h1>
  </header>
  {#if loading}
    <div class="status-card">
      <p class="dim">Checking connection…</p>
    </div>
  {:else if !connected}
    <div class="status-card error">
      <div class="dot red"></div>
      <div>
        <p class="label">Disconnected</p>
        <p class="dim">Unable to connect. Make sure SDM is running.</p>
      </div>
    </div>
  {:else if !appEnabled}
    <div class="status-card warning">
      <div class="dot amber"></div>
      <div>
        <p class="label">Monitoring Disabled</p>
        <p class="dim">Browser monitoring is disabled in SDM settings.</p>
      </div>
    </div>
  {:else}
    <div class="status-card ok">
      <div class="dot green"></div>
      <div>
        <p class="label">{isActive ? "Active" : "Paused"}</p>
        <p class="dim">
          {isActive ? "Monitoring downloads" : "Monitoring paused by user"}
        </p>
      </div>
    </div>
  {/if}
  {#if connected}
    <div class="toggle-row">
      <span>Browser Monitoring</span>
      <button
        class="toggle"
        class:on={userEnabled}
        onclick={toggleMonitoring}
        aria-label="Toggle monitoring"
      >
        <span class="knob"></span>
      </button>
    </div>
  {/if}
</main>

<style>
  main {
    width: 280px;
    padding: 4px;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  header {
    display: flex;
    align-items: center;
    gap: 8px;
    padding-bottom: 5px;
    border-bottom: 1px solid #2a2d4a;
  }
  .logo {
    font-size: 22px;
  }
  h1 {
    font-size: 15px;
    font-weight: 600;
  }
  .status-card {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 10px;
    border-radius: 10px;
    background: #242640;
  }
  .label {
    font-weight: 600;
    font-size: 18px;
  }
  .dim {
    color: #9a9cb8;
    font-size: 13px;
    margin-top: 12px;
  }
  .dot {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    flex-shrink: 0;
    margin-left: 5px;
  }
  .green {
    background: #4ade80;
    box-shadow: 0 0 6px #4ade8066;
  }
  .red {
    background: #f87171;
    box-shadow: 0 0 6px #f8717166;
  }
  .amber {
    background: #fbbf24;
    box-shadow: 0 0 6px #fbbf2466;
  }
  .toggle-row {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 10px 14px;
    background: #242640;
    border-radius: 10px;
    font-size: 13px;
  }
  .toggle {
    width: 42px;
    height: 24px;
    border-radius: 12px;
    border: none;
    background: #4a4d6a;
    cursor: pointer;
    position: relative;
    transition: background 200ms;
  }
  .toggle.on {
    background: #5b7fff;
  }
  .knob {
    position: absolute;
    top: 3px;
    left: 3px;
    width: 18px;
    height: 18px;
    border-radius: 50%;
    background: white;
    transition: transform 200ms;
  }
  .toggle.on .knob {
    transform: translateX(18px);
  }
</style>
