import App from "../components/MediaGrabberWidget.svelte";
import { mount, unmount } from "svelte";
import { browser } from "wxt/browser";

/**
 * Content script entrypoint for SDM.
 *
 * Responsibilities:
 * 1. Creates an isolated Shadow DOM container at the end of the body
 * 2. Mounts the Svelte 5 MediaGrabberWidget inside it
 * 3. Tracks and propagates the bypass key holding state to background
 * 4. Safely cleans up resources on extension reload/remove
 */
export default defineContentScript({
  matches: ["*://*/*"],
  cssInjectionMode: "ui",

  async main(ctx) {
    let activeBypassKey = "Delete";

    // Retrieve active bypass key preference
    browser.runtime.sendMessage({ type: "get-config" }).then((res: any) => {
      if (res && res.bypassKey) {
        activeBypassKey = res.bypassKey;
      }
    });

    // Handle key state updates
    const handleKeyState = (e: KeyboardEvent, pressed: boolean) => {
      const isMatch =
        e.key === activeBypassKey ||
        (activeBypassKey === "Shift" && e.key === "Shift") ||
        (activeBypassKey === "Control" && e.key === "Control") ||
        (activeBypassKey === "Alt" && e.key === "Alt");

      if (isMatch) {
        browser.runtime
          .sendMessage({
            type: "set-bypass-state",
            active: pressed,
          })
          .catch(() => {});
      }
    };

    const handleKeyDown = (e: KeyboardEvent) => handleKeyState(e, true);
    const handleKeyUp = (e: KeyboardEvent) => handleKeyState(e, false);
    const handleBlur = () => {
      browser.runtime
        .sendMessage({
          type: "set-bypass-state",
          active: false,
        })
        .catch(() => {});
    };

    window.addEventListener("keydown", handleKeyDown, { passive: true });
    window.addEventListener("keyup", handleKeyUp, { passive: true });
    window.addEventListener("blur", handleBlur, { passive: true });

    ctx.onInvalidated(() => {
      window.removeEventListener("keydown", handleKeyDown);
      window.removeEventListener("keyup", handleKeyUp);
      window.removeEventListener("blur", handleBlur);
    });

    const ui = await createShadowRootUi(ctx, {
      name: "sdm-media-grabber-root",
      position: "inline",
      anchor: "body",
      onMount: (container) => {
        // Mount the Svelte 5 widget inside the Shadow DOM container
        const app = mount(App, { target: container });
        return app;
      },
      onRemove: (app) => {
        // Safely unmount Svelte app when removed
        if (app) {
          unmount(app);
        }
      },
    });

    // Mount the Shadow DOM UI to the page
    ui.mount();
  },
});
