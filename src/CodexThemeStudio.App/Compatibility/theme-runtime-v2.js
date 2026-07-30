(async () => {
  const RUNTIME_KEY = "__CODEX_THEME_STUDIO_RUNTIME__";
  const themeId = __CTS_THEME_ID_JSON__;
  const cssText = __CTS_CSS_JSON__;
  const scriptText = __CTS_SCRIPT_JSON__;
  const stagedAssetDataUrls = window.__CODEX_THEME_STUDIO_STAGED_ASSETS__;
  delete window.__CODEX_THEME_STUDIO_STAGED_ASSETS__;
  const assetDataUrls = stagedAssetDataUrls || __CTS_ASSETS_JSON__;
  const config = __CTS_CONFIG_JSON__;
  const allowPetOverlay = __CTS_ALLOW_PET_OVERLAY__;
  const fingerprint = __CTS_FINGERPRINT_JSON__;
  const isPetOverlay = new URLSearchParams(location.search).get("initialRoute") === "/avatar-overlay";

  if (window[RUNTIME_KEY]?.dispose) {
    await window[RUNTIME_KEY].dispose();
  } else if (window.__CODEX_DREAM_SKIN_STATE__?.cleanup) {
    window.__CODEX_DREAM_SKIN_STATE__.cleanup();
  }

  if (isPetOverlay && !allowPetOverlay) {
    // This target was intentionally handled. Keep the marker so the desktop
    // watcher does not resend the (potentially very large) asset payload on
    // every health check just because this compact window has no theme UI.
    window.__CODEX_THEME_STUDIO_THEME_ID__ = themeId;
    return { installed: false, skipped: "pet-overlay" };
  }

  const safeThemeId = themeId.replace(/[^a-z0-9_-]/gi, "-");
  const style = document.createElement("style");
  style.id = "cts-theme-style";
  style.dataset.themeId = themeId;
  // Codex updates can change the home page's generated DOM shape. Several
  // early Studio themes styled that old shape directly, which can push or clip
  // the new-task composer. Keep the composer visible without changing the
  // themed home hero geometry.
  const compatibilityFixes = `
html.cts-theme-active .sticky.bottom-0:has(.composer-surface-chrome) [class*="from-token-main-surface-primary"] {
  background: transparent !important;
}
html.cts-theme-active :is(main,[role="main"]):has(.composer-surface-chrome) {
  overflow-x: hidden !important;
}
html.cts-theme-active :is(main,[role="main"]):has(.composer-surface-chrome) .sticky.bottom-0 {
  display: block !important;
  visibility: visible !important;
  opacity: 1 !important;
  z-index: 80 !important;
  pointer-events: auto !important;
}
html.cts-theme-active .composer-surface-chrome {
  display: flex !important;
  visibility: visible !important;
  opacity: 1 !important;
  min-height: 64px !important;
  position: relative !important;
  z-index: 81 !important;
  pointer-events: auto !important;
}
html.cts-theme-active .composer-surface-chrome :is(textarea,input,[contenteditable="true"]) {
  display: revert !important;
  visibility: visible !important;
  opacity: 1 !important;
  pointer-events: auto !important;
}
html.cts-theme-active [role="main"]:has(.composer-surface-chrome) :has(> .group\\/title),
html.cts-theme-active [role="main"]:has(.composer-surface-chrome) .group\\/title,
html.cts-theme-active [role="main"]:has(.composer-surface-chrome) :has(> .group\\/home-suggestions),
html.cts-theme-active [role="main"]:has(.composer-surface-chrome) .group\\/home-suggestions {
  display: none !important;
}
html.cts-theme-active #cts-theme-root [data-cts-side-panel-overlay="true"] {
  transition: opacity .18s ease, visibility .18s ease, transform .18s ease !important;
}
html.cts-theme-active.cts-code-review-open #cts-theme-root [data-cts-side-panel-overlay="true"] {
  opacity: 0 !important;
  visibility: hidden !important;
  pointer-events: none !important;
  transform: translateX(24px) scale(.96) !important;
  transition: opacity .18s ease, visibility .18s ease, transform .18s ease !important;
}`;
  style.textContent = `${cssText}\n${compatibilityFixes}`;
  (document.head || document.documentElement).appendChild(style);

  const root = document.createElement("div");
  root.id = "cts-theme-root";
  root.dataset.themeId = themeId;
  document.body?.appendChild(root);

  const managedCleanups = [];
  const assetVariables = [];
  const assetObjectUrls = [];
  let definition = null;
  let disposed = false;

  const addCleanup = (cleanup) => {
    if (typeof cleanup !== "function") throw new TypeError("cleanup must be a function");
    managedCleanups.push(cleanup);
    return cleanup;
  };

  const assetDataUrl = (name) => {
    const value = assetDataUrls[name];
    if (!value) throw new Error(`Theme asset not found: ${name}`);
    return value;
  };

  const createAssetObjectUrl = (dataUrl) => {
    const comma = dataUrl.indexOf(",");
    if (comma < 0 || !dataUrl.slice(0, comma).endsWith(";base64")) {
      throw new Error("Theme asset is not a base64 data URL");
    }

    const mimeType = dataUrl.slice(5, comma).split(";", 1)[0] || "application/octet-stream";
    const binary = atob(dataUrl.slice(comma + 1));
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index += 1) {
      bytes[index] = binary.charCodeAt(index);
    }

    const objectUrl = URL.createObjectURL(new Blob([bytes], { type: mimeType }));
    assetObjectUrls.push(objectUrl);
    return objectUrl;
  };

  for (const [name, dataUrl] of Object.entries(assetDataUrls)) {
    const variable = `--cts-asset-${name.replace(/[^a-z0-9_-]/gi, "-")}`;
    const objectUrl = createAssetObjectUrl(dataUrl);
    document.documentElement.style.setProperty(variable, `url("${objectUrl}")`);
    assetVariables.push(variable);
  }

  document.documentElement.classList.add("cts-theme-active", `cts-theme-${safeThemeId}`);

  const context = Object.freeze({
    id: themeId,
    fingerprint,
    root,
    document,
    window,
    cssText,
    config: Object.freeze(config),
    get mode() {
      return document.documentElement.classList.contains("electron-dark") ? "dark" : "light";
    },
    assets: Object.freeze({ ...assetDataUrls }),
    assetDataUrl,
    addCleanup,
    on(target, eventName, listener, options) {
      target.addEventListener(eventName, listener, options);
      addCleanup(() => target.removeEventListener(eventName, listener, options));
      return listener;
    },
    observe(target, options, callback) {
      const observer = new MutationObserver(callback);
      observer.observe(target, options);
      addCleanup(() => observer.disconnect());
      return observer;
    },
    interval(callback, milliseconds) {
      const handle = setInterval(callback, milliseconds);
      addCleanup(() => clearInterval(handle));
      return handle;
    },
    timeout(callback, milliseconds) {
      const handle = setTimeout(callback, milliseconds);
      addCleanup(() => clearTimeout(handle));
      return handle;
    },
  });

  const dispose = async () => {
    if (disposed) return true;
    disposed = true;
    try {
      if (definition?.unmount) await definition.unmount(context);
    } finally {
      for (const cleanup of managedCleanups.reverse()) {
        try { await cleanup(); } catch { }
      }
      style.remove();
      root.remove();
      document.documentElement.classList.remove("cts-theme-active", `cts-theme-${safeThemeId}`);
      for (const variable of assetVariables) document.documentElement.style.removeProperty(variable);
      for (const objectUrl of assetObjectUrls) URL.revokeObjectURL(objectUrl);
      if (window.__CODEX_THEME_STUDIO_THEME_ID__ === themeId) {
        delete window.__CODEX_THEME_STUDIO_THEME_ID__;
      }
      if (window[RUNTIME_KEY]?.fingerprint === fingerprint) delete window[RUNTIME_KEY];
    }
    return true;
  };

  window[RUNTIME_KEY] = { themeId, fingerprint, dispose, context };

  try {
    // Theme packages can add floating task cards and companion instruments.
    // Keep those decorations out of Codex's code-review diff. Ordinary Files,
    // Browser, and Terminal sidebars must not change the cards.
    let reviewFrame = 0;
    const syncCodeReview = () => {
      reviewFrame = 0;
      const main = document.querySelector("main.main-surface") || document.querySelector("main");
      const mainBox = main?.getBoundingClientRect();
      const reviewLabel = /review|diff|changes|审阅|差异|更改/i;
      const rightEdge = mainBox ? mainBox.left + mainBox.width * .6 : window.innerWidth * .6;
      const reviewTabs = Array.from(document.querySelectorAll('[role="tab"]')).filter((tab) => {
        const box = tab.getBoundingClientRect();
        return box.width > 0 && box.height > 0 && box.left >= rightEdge;
      });
      const rightDiffControls = Array.from(document.querySelectorAll("button[aria-label]")).some((button) => {
        const box = button.getBoundingClientRect();
        return box.width > 0 && box.height > 0 && box.left >= rightEdge && reviewLabel.test(button.getAttribute("aria-label") || "");
      });
      const codeReviewOpen = reviewTabs.some((tab) => reviewLabel.test(tab.textContent || "")) ||
        (reviewTabs.length > 0 && rightDiffControls);

      document.documentElement.classList.toggle("cts-code-review-open", codeReviewOpen);
      if (!mainBox) return;

      const stage = root.firstElementChild;
      const candidates = new Set([
        ...Array.from(stage?.children || []),
        ...Array.from(root.children),
      ]);
      candidates.delete(stage);

      const rightThreshold = mainBox.left + mainBox.width * .54;
      const lowerThreshold = mainBox.top + mainBox.height * .24;
      for (const candidate of candidates) {
        const box = candidate.getBoundingClientRect();
        const isRightTaskOverlay =
          box.width >= 48 &&
          box.height >= 28 &&
          box.right >= rightThreshold &&
          box.top >= lowerThreshold;
        const nextValue = String(isRightTaskOverlay);
        if (candidate.getAttribute("data-cts-side-panel-overlay") !== nextValue) {
          candidate.setAttribute("data-cts-side-panel-overlay", nextValue);
        }
      }
    };
    const scheduleCodeReviewSync = () => {
      if (reviewFrame || disposed) return;
      reviewFrame = window.requestAnimationFrame(syncCodeReview);
    };
    syncCodeReview();
    context.observe(document.documentElement, { childList: true, subtree: true }, scheduleCodeReviewSync);
    context.on(window, "resize", scheduleCodeReviewSync, { passive: true });
    context.interval(syncCodeReview, 1000);
    context.addCleanup(() => {
      if (reviewFrame) window.cancelAnimationFrame(reviewFrame);
      document.documentElement.classList.remove("cts-code-review-open");
      root.querySelectorAll("[data-cts-side-panel-overlay]").forEach((node) => {
        node.removeAttribute("data-cts-side-panel-overlay");
      });
    });

    const repairCodexHomeDom = () => {
      for (const banners of document.querySelectorAll(".home-banners:empty")) {
        const wrapper = banners.parentElement;
        const home = wrapper?.parentElement;
        if (!wrapper || wrapper.dataset.ctsRemovedHomeBanners === "true") continue;
        if (home?.getAttribute("role") !== "main") continue;
        if (wrapper !== home.firstElementChild) continue;
        wrapper.dataset.ctsRemovedHomeBanners = "true";
        wrapper.remove();
      }
    };
    repairCodexHomeDom();
    context.observe(document.documentElement, { childList: true, subtree: true }, repairCodexHomeDom);

    if (scriptText) {
      const registerTheme = (candidate) => {
        if (!candidate || typeof candidate !== "object") {
          throw new TypeError("registerTheme expects a theme lifecycle object");
        }
        definition = candidate;
      };
      eval(scriptText);
      if (!definition || typeof definition.mount !== "function") {
        throw new Error("Advanced theme script must call registerTheme({ mount, unmount? })");
      }
      await definition.mount(context);
    }

    window.__CODEX_THEME_STUDIO_THEME_ID__ = themeId;
    return { installed: true, themeId, fingerprint, advanced: Boolean(scriptText) };
  } catch (error) {
    await dispose();
    throw error;
  }
})()
