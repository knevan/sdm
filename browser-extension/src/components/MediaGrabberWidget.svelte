<script lang="ts">
  import { onMount } from "svelte";
  import { browser } from "wxt/browser";

  // --- States (Svelte 5 Runes) ---
  let connected = $state(false);
  let appEnabled = $state(false);
  let userEnabled = $state(true);
  let bypassKey = $state("Delete");
  let showPopups = $state(true);

  // Detected media items
  let mediaList = $state<any[]>([]);

  // Selection grabber state
  let selectedLinks = $state<string[]>([]);
  let selectionPosition = $state<{ x: number; y: number } | null>(null);

  // Widget visibility state
  let listVisible = $state(false);
  let dismissed = $state(false);
  let widgetPosition = $state<{ top: string; left: string } | null>(null);

  // --- Methods ---

  function extractLinks(text: string): string[] {
    const urlRegex = /(https?:\/\/[^\s$.?#].[^\s]*)/gi;
    const matches = text.match(urlRegex) || [];
    return Array.from(new Set(matches)).filter((u) => {
      try {
        new URL(u);
        return true;
      } catch {
        return false;
      }
    });
  }

  function toggleList(e: MouseEvent): void {
    const target = e.target as HTMLElement;
    if (target.classList.contains("sdm-close")) return;
    listVisible = !listVisible;
  }

  function dismissGrabber(e: MouseEvent): void {
    e.stopPropagation();
    dismissed = true;
  }

  function downloadMedia(mediaId: string): void {
    browser.runtime.sendMessage({ type: "media-click", mediaId });
    listVisible = false;
  }

  function downloadSelectedLinks(): void {
    if (selectedLinks.length > 0) {
      browser.runtime.sendMessage({ type: "download-links", links: selectedLinks });
      selectedLinks = [];
      selectionPosition = null;
    }
  }

  function updateWidgetPosition(): void {
    if (dismissed) return;

    // Find the largest active video element on the page
    const videos = Array.from(document.querySelectorAll("video"));
    if (videos.length === 0) {
      widgetPosition = null;
      return;
    }

    const largestVideo = videos.sort((a, b) => {
      const rectA = a.getBoundingClientRect();
      const rectB = b.getBoundingClientRect();
      return rectB.width * rectB.height - rectA.width * rectA.height;
    })[0];

    if (largestVideo) {
      const rect = largestVideo.getBoundingClientRect();
      widgetPosition = {
        top: `${Math.max(rect.top + window.scrollY + 10, 10)}px`,
        left: `${Math.max(rect.left + window.scrollX + 10, 10)}px`,
      };
    } else {
      widgetPosition = null;
    }
  }

  // --- Lifecycle ---

  onMount(() => {
    // 1. Initial config and media list fetch
    browser.runtime.sendMessage({ type: "get-config" }).then((res: any) => {
      if (res) {
        userEnabled = res.userEnabled;
        appEnabled = res.appEnabled;
        connected = res.connected;
        bypassKey = res.bypassKey;
        showPopups = res.showPopups !== false;
        mediaList = res.media || [];
      }
    });

    // 2. Listen for media list and config updates from background
    const messageListener = (message: any) => {
      if (message.type === "media-list-updated") {
        mediaList = message.media || [];
      } else if (message.type === "show-popups-changed") {
        showPopups = message.enabled;
      }
    };
    browser.runtime.onMessage.addListener(messageListener);

    // 3. Text selection listeners
    const handleMouseUp = () => {
      setTimeout(() => {
        const selection = window.getSelection();
        if (!selection || selection.type !== "Range" || selection.isCollapsed) {
          selectedLinks = [];
          selectionPosition = null;
          return;
        }

        const text = selection.toString();
        const links = extractLinks(text);
        if (links.length === 0) {
          selectedLinks = [];
          selectionPosition = null;
          return;
        }

        try {
          const range = selection.getRangeAt(0);
          const rects = range.getClientRects();
          if (rects.length > 0) {
            const lastRect = rects[rects.length - 1];
            selectionPosition = {
              x: lastRect.right + window.scrollX,
              y: lastRect.bottom + window.scrollY + 12,
            };
            selectedLinks = links;
          }
        } catch {
          selectedLinks = [];
          selectionPosition = null;
        }
      }, 50);
    };

    const handleSelectionChange = () => {
      const selection = window.getSelection();
      if (!selection || selection.type !== "Range") {
        selectedLinks = [];
        selectionPosition = null;
      }
    };

    document.addEventListener("mouseup", handleMouseUp);
    document.addEventListener("selectionchange", handleSelectionChange);

    // 4. Update widget positioning periodically
    const interval = setInterval(updateWidgetPosition, 1500);

    return () => {
      browser.runtime.onMessage.removeListener(messageListener);
      document.removeEventListener("mouseup", handleMouseUp);
      document.removeEventListener("selectionchange", handleSelectionChange);
      clearInterval(interval);
    };
  });
</script>

<div class="sdm-shadow-wrapper">
  <!-- 1. Selection Link Grabber Popup -->
  {#if showPopups && selectionPosition && selectedLinks.length > 0}
    <div
      class="sdm-selection-popup"
      style="top: {selectionPosition.y}px; left: {selectionPosition.x}px;"
    >
      <button onclick={downloadSelectedLinks} class="sdm-btn">
        <span class="sdm-btn-icon">⬇</span>
        Download Tautan Terpilih ({selectedLinks.length})
      </button>
    </div>
  {/if}

  <!-- 2. Floating Video/Media Grabber -->
  {#if !dismissed && mediaList.length > 0 && connected && appEnabled && userEnabled && showPopups}
    <div
      class="sdm-media-grabber"
      class:sdm-fixed={!widgetPosition}
      style={widgetPosition ? `top: ${widgetPosition.top}; left: ${widgetPosition.left};` : ""}
    >
      <div class="sdm-header" onclick={toggleList} role="button" tabindex="0" onkeydown={(e) => e.key === 'Enter' && toggleList(e as any)}>
        <div class="sdm-logo-section">
          <svg class="sdm-arrow-icon" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path d="M12 5V19M12 19L5 12M12 19L19 12" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
          <span class="sdm-header-title">SDM Grabber</span>
        </div>
        <div class="sdm-badge-section">
          <span class="sdm-badge">{mediaList.length}</span>
          <button class="sdm-close" onclick={dismissGrabber} aria-label="Dismiss media grabber">×</button>
        </div>
      </div>

      {#if listVisible}
        <div class="sdm-list">
          {#each mediaList as item}
            <button class="sdm-item" onclick={() => downloadMedia(item.id)}>
              <div class="sdm-item-text">{item.text}</div>
              {#if item.info}
                <div class="sdm-item-info">{item.info}</div>
              {/if}
            </button>
          {/each}
        </div>
      {/if}
    </div>
  {/if}
</div>

<style>
  .sdm-shadow-wrapper * {
    box-sizing: border-box;
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  }

  /* --- Common Button Styles --- */
  .sdm-btn {
    display: flex;
    align-items: center;
    gap: 8px;
    background: linear-gradient(135deg, #4f46e5 0%, #3730a3 100%);
    border: 1px solid rgba(255, 255, 255, 0.2);
    border-radius: 12px;
    padding: 8px 14px;
    color: #ffffff;
    font-size: 13px;
    font-weight: 600;
    cursor: pointer;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
    transition: all 0.2s ease;
  }
  .sdm-btn:hover {
    transform: translateY(-1px);
    box-shadow: 0 6px 16px rgba(79, 70, 229, 0.4);
    background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%);
  }

  /* --- Selection Link Grabber --- */
  .sdm-selection-popup {
    position: absolute;
    z-index: 2147483647;
    animation: fadeIn 0.15s ease-out;
  }

  /* --- Media Grabber Widget --- */
  .sdm-media-grabber {
    position: absolute;
    z-index: 2147483646;
    width: 250px;
    background: rgba(26, 27, 38, 0.85);
    backdrop-filter: blur(12px);
    -webkit-backdrop-filter: blur(12px);
    border: 1px solid rgba(255, 255, 255, 0.15);
    border-radius: 16px;
    box-shadow: 0 10px 30px rgba(0, 0, 0, 0.35);
    overflow: hidden;
    animation: fadeIn 0.2s ease-out;
    transition: top 0.3s ease, left 0.3s ease;
  }

  .sdm-media-grabber.sdm-fixed {
    position: fixed;
    top: 16px;
    left: 16px;
  }

  .sdm-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 10px 14px;
    background: linear-gradient(180deg, rgba(255, 255, 255, 0.08) 0%, rgba(255, 255, 255, 0) 100%);
    cursor: pointer;
    user-select: none;
    border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  }

  .sdm-logo-section {
    display: flex;
    align-items: center;
    gap: 8px;
    color: #e2e8f0;
  }

  .sdm-arrow-icon {
    width: 14px;
    height: 14px;
    color: #6366f1;
  }

  .sdm-header-title {
    font-size: 13px;
    font-weight: 700;
    letter-spacing: 0.3px;
  }

  .sdm-badge-section {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .sdm-badge {
    background: #6366f1;
    color: #ffffff;
    font-size: 11px;
    font-weight: 700;
    padding: 2px 7px;
    border-radius: 20px;
    box-shadow: 0 0 8px rgba(99, 102, 241, 0.5);
  }

  .sdm-close {
    background: transparent;
    border: none;
    color: #94a3b8;
    font-size: 16px;
    font-weight: 300;
    cursor: pointer;
    padding: 0 4px;
    line-height: 1;
    transition: color 0.15s ease;
  }
  .sdm-close:hover {
    color: #f1f5f9;
  }

  /* --- Scrollable Dropdown List --- */
  .sdm-list {
    max-height: 250px;
    overflow-y: auto;
    background: rgba(20, 21, 30, 0.6);
  }

  .sdm-item {
    display: block;
    width: 100%;
    text-align: left;
    background: transparent;
    border: none;
    border-bottom: 1px solid rgba(255, 255, 255, 0.05);
    padding: 10px 14px;
    color: #e2e8f0;
    cursor: pointer;
    transition: background 0.15s ease;
  }
  .sdm-item:last-child {
    border-bottom: none;
  }
  .sdm-item:hover {
    background: rgba(99, 102, 241, 0.15);
  }

  .sdm-item-text {
    font-size: 12px;
    font-weight: 600;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    color: #ffffff;
  }

  .sdm-item-info {
    font-size: 10px;
    color: #94a3b8;
    margin-top: 4px;
  }

  /* --- Animations --- */
  @keyframes fadeIn {
    from {
      opacity: 0;
      transform: scale(0.95);
    }
    to {
      opacity: 1;
      transform: scale(1);
    }
  }
</style>
