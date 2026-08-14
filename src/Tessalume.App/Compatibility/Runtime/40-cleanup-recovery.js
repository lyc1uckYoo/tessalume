// TESSALUME_RUNTIME_FRAGMENT: cleanup, rollback, code-review isolation, and theme lifecycle recovery
// TESSALUME_STANDALONE_ENVELOPE_START
(async () => {
  const mountCanonicalTheme = (spec) => {
// TESSALUME_STANDALONE_ENVELOPE_END
    const cleanup = () => {
      if (themeDisposed) return true;
      themeDisposed = true;
      if (ensureTimer) window.clearTimeout(ensureTimer);
      if (layoutFrame) window.cancelAnimationFrame(layoutFrame);
      layoutFrame = 0;
      layoutResizeObserver?.disconnect();
      layoutResizeObserver = null;
      layoutObserved = new Set();
      if (taskTitleWidthManaged) {
        if (taskTitleWidthPrevious) {
          html.style.setProperty("--tessalume-task-title-primary-width", taskTitleWidthPrevious);
        } else {
          html.style.removeProperty("--tessalume-task-title-primary-width");
        }
        taskTitleWidthManaged = false;
      }
      spec.onCleanup?.(api);
      for (const [node, className] of marked) {
        try { node?.classList?.remove(className); } catch { }
      }
      for (const [node, previous, attribute = "data-tessalume-surface"] of surfaced) {
        try {
          if (previous == null) node?.removeAttribute?.(attribute);
          else node?.setAttribute?.(attribute, previous);
        } catch { }
      }
      try {
        document.querySelectorAll("*").forEach((node) => {
          for (const attribute of Array.from(node.attributes || [])) {
            if (attribute.name.startsWith(`data-${namespace}-`)) node.removeAttribute(attribute.name);
          }
        });
      } catch { }
      html.classList.remove(
        themeClass,
        roleClass("is-home"),
        roleClass("is-task"),
        roleClass("is-settings"),
        roleClass("has-output"),
        "tessalume-is-home",
        "tessalume-is-task",
        "tessalume-is-settings",
        "tessalume-has-output",
      );
      root.removeAttribute("data-tessalume-template-version");
      return true;
    };

    html.classList.add(themeClass);
    root.setAttribute("aria-hidden", "true");
    if (!spec.preserveRoot) {
      root.innerHTML = typeof spec.render === "function" ? spec.render(api) : String(spec.render || "");
    }
    spec.onMount?.(api);
    validateTemplateStructure();
    context.observe(document.documentElement, { childList:true, subtree:true }, onDocumentMutations);
    context.on(window, "resize", () => {
      syncLiveLayout();
      startLayoutTracking();
      schedule();
    }, { passive:true });
    context.interval(ensure, 4000);
    context.addCleanup(cleanup);
    ensure();
    return cleanup;
  };

  context = Object.freeze({
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
    renderTemplateV1,
    mountCanonicalTheme,
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
      document.documentElement.classList.remove("tessalume-theme-active", `tessalume-theme-${safeThemeId}`);
      for (const variable of assetVariables) document.documentElement.style.removeProperty(variable);
      for (const variable of visualSettingVariables) document.documentElement.style.removeProperty(variable);
      delete document.documentElement.dataset.tessalumeReadability;
      delete document.documentElement.dataset.tessalumeMotion;
      delete document.documentElement.dataset.tessalumeTextScale;
      delete document.documentElement.dataset.tessalumeDensity;
      for (const objectUrl of assetObjectUrls) URL.revokeObjectURL(objectUrl);
      if (window.__TESSALUME_THEME_ID__ === themeId) {
        delete window.__TESSALUME_THEME_ID__;
      }
      if (window[RUNTIME_KEY]?.fingerprint === fingerprint) delete window[RUNTIME_KEY];
    }
    return true;
  };

  window[RUNTIME_KEY] = {
    themeId,
    fingerprint,
    compatibilityProfileVersion: compatibilityProfile.profileVersion,
    dispose,
    context,
    setVisualSettings,
  };

  try {
    // Theme packages can add floating task cards and companion instruments.
    // Keep those decorations out of Codex's code-review diff. Ordinary Files,
    // Browser, and Terminal sidebars must not change the cards.
    let reviewFrame = 0;
    const syncCodeReview = () => {
      reviewFrame = 0;
      const main = queryFirst(
        document,
        "main",
        [
          'main[data-app-shell-main-surface="default"]',
          'main[class*="MainContentSurface"]',
          "main.main-surface",
          "main",
        ],
      );
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

      document.documentElement.classList.toggle("tessalume-code-review-open", codeReviewOpen);
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
        if (candidate.getAttribute("data-tessalume-side-panel-overlay") !== nextValue) {
          candidate.setAttribute("data-tessalume-side-panel-overlay", nextValue);
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
      document.documentElement.classList.remove("tessalume-code-review-open");
      root.querySelectorAll("[data-tessalume-side-panel-overlay]").forEach((node) => {
        node.removeAttribute("data-tessalume-side-panel-overlay");
      });
    });

    const repairCodexHomeDom = () => {
      for (const banners of document.querySelectorAll(".home-banners:empty")) {
        const wrapper = banners.parentElement;
        const home = wrapper?.parentElement;
        if (!wrapper || wrapper.dataset.tessalumeRemovedHomeBanners === "true") continue;
        if (home?.getAttribute("role") !== "main") continue;
        if (wrapper !== home.firstElementChild) continue;
        wrapper.dataset.tessalumeRemovedHomeBanners = "true";
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
      try {
        eval(scriptText);
      } catch (error) {
        throw new Error(`TESSALUME_THEME_SCRIPT: ${error?.message || String(error)}`);
      }
      if (!definition || typeof definition.mount !== "function") {
        throw new Error("TESSALUME_THEME_SCRIPT: Advanced theme script must call registerTheme({ mount, unmount? })");
      }
      try {
        await definition.mount(context);
      } catch (error) {
        throw new Error(`TESSALUME_THEME_SCRIPT: ${error?.message || String(error)}`);
      }
    }

    window.__TESSALUME_THEME_ID__ = themeId;
    return { installed: true, themeId, fingerprint, advanced: Boolean(scriptText) };
  } catch (error) {
    await dispose();
    throw error;
  }
})()
