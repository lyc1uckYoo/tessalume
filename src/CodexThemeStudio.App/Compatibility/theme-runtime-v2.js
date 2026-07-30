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
html.cts-theme-active #cts-theme-root [data-cts-auto-hidden] {
  transition: opacity .18s ease, visibility 0s linear 0s !important;
}
html.cts-theme-active #cts-theme-root [data-cts-auto-hidden="true"] {
  opacity: 0 !important;
  visibility: hidden !important;
  pointer-events: none !important;
  animation-play-state: paused !important;
  transition: opacity .14s ease, visibility 0s linear .14s !important;
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

  let context;
  const mountCanonicalTheme = (spec) => {
    if (!spec || typeof spec !== "object") {
      throw new TypeError("mountCanonicalTheme expects a theme specification");
    }
    const namespace = String(spec.namespace || "");
    const themeClass = String(spec.themeClass || "");
    const templateVersion = spec.templateVersion == null
      ? ""
      : String(spec.templateVersion);
    if (!/^[a-z][a-z0-9]*$/.test(namespace) || !themeClass) {
      throw new TypeError("Canonical themes require a lowercase namespace and themeClass");
    }
    if (templateVersion && templateVersion !== "1.0") {
      throw new TypeError(`Unsupported canonical theme template: ${templateVersion}`);
    }

    const html = document.documentElement;
    const marked = [];
    let themeDisposed = false;
    let ensureTimer = 0;
    let layoutResizeObserver = null;
    let layoutObserved = new Set();
    let layoutFrame = 0;
    let layoutTrackingStartedAt = 0;
    let layoutLastSignature = "";
    let layoutStableFrames = 0;
    const adaptiveLayout = spec.adaptiveLayout === true ||
      (spec.adaptiveLayout && typeof spec.adaptiveLayout === "object");

    const roleClass = (role) => `${namespace}-${role}`;
    const dataName = (name) => `data-${namespace}-${name}`;
    const validateTemplateStructure = () => {
      if (templateVersion !== "1.0") return;
      const stage = root.querySelectorAll("[data-theme-stage]");
      const role = (name) =>
        Array.from(root.querySelectorAll(`[data-theme-role="${name}"]`));
      const exactlyOne = [
        "hero",
        "identity",
        "task-left",
        "memory",
        "sync-panel",
        "composer-accessory",
      ];
      if (stage.length !== 1 || stage[0].parentElement !== root) {
        throw new TypeError("Template 1.0 requires one root-level data-theme-stage");
      }
      for (const name of exactlyOne) {
        const nodes = role(name);
        if (nodes.length !== 1) {
          throw new TypeError(`Template 1.0 requires exactly one ${name} role`);
        }
      }
      const rightCards = role("task-right");
      const primary = rightCards.filter(
        (node) => node.getAttribute("data-theme-priority") === "primary",
      );
      const secondary = rightCards.filter(
        (node) => node.getAttribute("data-theme-priority") === "secondary",
      );
      if (rightCards.length !== 2 || primary.length !== 1 || secondary.length !== 1) {
        throw new TypeError(
          "Template 1.0 requires one primary and one secondary task-right card",
        );
      }
      const stageNode = stage[0];
      for (const name of ["hero", "identity", "task-left", "task-right", "memory"]) {
        if (role(name).some((node) => !stageNode.contains(node))) {
          throw new TypeError(`Template 1.0 requires ${name} inside data-theme-stage`);
        }
      }
      for (const name of ["sync-panel", "composer-accessory"]) {
        if (role(name)[0].parentElement !== root) {
          throw new TypeError(`Template 1.0 requires root-level ${name}`);
        }
      }
      if (role("sync-panel")[0].getAttribute("data-theme-priority") !== "secondary") {
        throw new TypeError(
          "Template 1.0 sync-panel must hide with the secondary task card",
        );
      }
      root.setAttribute("data-cts-template-version", templateVersion);
    };
    const mark = (node, className) => {
      if (!node || node.classList.contains(className)) return node;
      node.classList.add(className);
      marked.push([node, className]);
      return node;
    };
    const setData = (node, name, value) => {
      if (!node) return;
      node.setAttribute(dataName(name), String(value));
    };
    const findHome = () => {
      const icon = document.querySelector('[data-testid="home-icon"]');
      return icon?.closest('[role="main"]') || icon?.closest("main") || null;
    };
    const findMain = () =>
      document.querySelector("main.main-surface") || document.querySelector("main");
    const findSettingsSurface = (main = findMain()) => {
      if (!main) return null;
      return Array.from(main.querySelectorAll(
        ".main-surface.flex.h-full.min-h-0.flex-col",
      )).find((surface) => surface.querySelector(
        ":scope > .scrollbar-stable.flex-1.overflow-y-auto.p-panel",
      )) || null;
    };
    const syncRouteState = () => {
      const home = findHome();
      const isHome = Boolean(home);
      const settingsSurface = findSettingsSurface();
      mark(settingsSurface, roleClass("settings-surface"));
      html.classList.toggle(roleClass("is-home"), isHome);
      html.classList.toggle(roleClass("is-task"), !isHome);
      html.classList.toggle(roleClass("is-settings"), Boolean(settingsSurface));
      return home;
    };
    const findStage = () =>
      root.querySelector("[data-theme-stage]") || root.querySelector(`.${roleClass("stage")}`);
    const syncStageGeometry = (main, stage) => {
      if (!main || !stage) return "";
      const box = main.getBoundingClientRect();
      if (!(box.width > 0 && box.height > 0)) return "";
      const pixels = (value) => `${Math.round(value * 100) / 100}px`;
      const geometry = {
        left: pixels(box.left),
        top: pixels(box.top),
        width: pixels(box.width),
        height: pixels(box.height),
      };
      for (const [property, value] of Object.entries(geometry)) {
        if (stage.style[property] !== value) stage.style[property] = value;
      }
      return [box.left, box.top, box.width, box.height]
        .map((value) => Math.round(value * 4) / 4)
        .join(":");
    };
    const positionComposerAccessory = (main, selector, size = 76, gap = 18) => {
      const accessory = root.querySelector(selector);
      const composer = document.querySelector(".composer-surface-chrome");
      if (!main || !accessory || !composer) return;
      const mainBox = main.getBoundingClientRect();
      const composerBox = composer.getBoundingClientRect();
      const left = Math.max(mainBox.left + 14, Math.round(composerBox.left - size - gap));
      const top = Math.max(
        mainBox.top + 56,
        Math.min(
          Math.round(mainBox.bottom - size - 22),
          Math.round(composerBox.top + (composerBox.height - size) / 2),
        ),
      );
      Object.assign(accessory.style, { left:`${left}px`, top:`${top}px`, right:"auto", bottom:"auto" });
    };
    const positionPanelAboveCards = (main, panelSelector, cardSelectors, width, height, gap = 14) => {
      const panel = root.querySelector(panelSelector);
      const cards = cardSelectors.map((selector) => root.querySelector(selector));
      if (!main || !panel || cards.some((card) => !card)) return;
      const cardMetrics = cards.map((card) => {
        const style = window.getComputedStyle(card);
        return {
          width: Number.parseFloat(style.width),
          height: Number.parseFloat(style.height),
          right: Number.parseFloat(style.right),
          bottom: Number.parseFloat(style.bottom),
        };
      });
      if (!cardMetrics.flatMap((item) => Object.values(item)).every(Number.isFinite)) return;
      const mainBox = main.getBoundingClientRect();
      const cardsLeft = mainBox.right - Math.max(...cardMetrics.map((item) => item.right + item.width));
      const cardsRight = mainBox.right - Math.min(...cardMetrics.map((item) => item.right));
      const cardsTop = Math.min(...cardMetrics.map((item) => mainBox.bottom - item.bottom - item.height));
      const left = Math.max(mainBox.left + 18, Math.round((cardsLeft + cardsRight - width) / 2));
      const top = Math.max(mainBox.top + 56, Math.round(cardsTop - height - gap));
      Object.assign(panel.style, { left:`${left}px`, top:`${top}px`, right:"auto", bottom:"auto" });
    };
    const setAdaptiveAttribute = (node, name, value) => {
      if (!node) return;
      const next = String(value);
      if (node.getAttribute(name) !== next) node.setAttribute(name, next);
    };
    const setAutoHidden = (node, hidden, reason = "") => {
      if (!node) return;
      setAdaptiveAttribute(node, "data-cts-auto-hidden", hidden);
      if (hidden && reason) {
        setAdaptiveAttribute(node, "data-cts-auto-hidden-reason", reason);
      } else {
        node.removeAttribute("data-cts-auto-hidden-reason");
      }
    };
    const syncLayoutObservers = (nodes) => {
      if (typeof ResizeObserver !== "function") return;
      if (!layoutResizeObserver) {
        layoutResizeObserver = new ResizeObserver(() => {
          syncLiveLayout();
          startLayoutTracking();
          schedule();
        });
      }
      const next = new Set(nodes.filter(Boolean));
      for (const node of layoutObserved) {
        if (!next.has(node)) layoutResizeObserver.unobserve(node);
      }
      for (const node of next) {
        if (!layoutObserved.has(node)) layoutResizeObserver.observe(node);
      }
      layoutObserved = next;
    };
    const syncAdaptiveVisibility = (main, stage, home) => {
      if (!adaptiveLayout) return;

      const leftRoles = Array.from(root.querySelectorAll(
        '[data-theme-role="task-left"],[data-theme-role="memory"]',
      ));
      const rightRoles = Array.from(root.querySelectorAll(
        '[data-theme-role="task-right"],[data-theme-role="sync-panel"],[data-theme-role="domain"]',
      ));
      const accessories = Array.from(root.querySelectorAll(
        '[data-theme-role="composer-accessory"]',
      ));
      const managed = [...leftRoles, ...rightRoles, ...accessories];

      if (home) {
        managed.forEach((node) => setAutoHidden(node, false));
        setAdaptiveAttribute(root, "data-cts-task-layout", "inactive");
        return;
      }

      const workspace = document.querySelector(".thread-scroll-container") || main;
      const composer = document.querySelector(".composer-surface-chrome");

      const workspaceBox = workspace?.getBoundingClientRect();
      const composerBox = composer?.getBoundingClientRect();
      const validWorkspace = workspaceBox && workspaceBox.width > 0 && workspaceBox.height > 0;
      const validComposer = composerBox && composerBox.width > 0 && composerBox.height > 0;
      if (!main || !stage || !validWorkspace || !validComposer) {
        managed.forEach((node) => setAutoHidden(node, true, "content-unavailable"));
        setAdaptiveAttribute(root, "data-cts-task-layout", "minimal");
        setAdaptiveAttribute(root, "data-cts-left-rail", "none");
        setAdaptiveAttribute(root, "data-cts-right-rail", "none");
        setAdaptiveAttribute(root, "data-cts-composer-accessory", "hidden");
        return;
      }

      const leftGutter = Math.max(0, composerBox.left - workspaceBox.left);
      const rightGutter = Math.max(0, workspaceBox.right - composerBox.right);
      const workspaceHeight = workspaceBox.height;
      const reviewOpen = html.classList.contains("cts-code-review-open");

      const previousLeft = root.getAttribute("data-cts-left-rail");
      const previousRight = root.getAttribute("data-cts-right-rail");
      const previousAccessory = root.getAttribute("data-cts-composer-accessory");

      const leftFits = !reviewOpen &&
        leftGutter >= (previousLeft === "full" ? 164 : 180) &&
        workspaceHeight >= (previousLeft === "full" ? 680 : 720);

      let rightRail = "none";
      if (!reviewOpen) {
        const fullFits =
          rightGutter >= (previousRight === "full" ? 398 : 422) &&
          workspaceHeight >= (previousRight === "full" ? 680 : 720);
        const singleFits =
          rightGutter >= (previousRight === "single" || previousRight === "full" ? 166 : 182) &&
          workspaceHeight >= (previousRight === "single" || previousRight === "full" ? 590 : 620);
        rightRail = fullFits ? "full" : singleFits ? "single" : "none";
      }

      const accessoryFits = !reviewOpen &&
        leftGutter >= (previousAccessory === "visible" ? 96 : 108) &&
        workspaceHeight >= 560;

      leftRoles.forEach((node) => setAutoHidden(node, !leftFits, "left-rail"));
      rightRoles.forEach((node) => {
        const secondary = node.getAttribute("data-theme-priority") === "secondary";
        const hidden = rightRail === "none" || (rightRail === "single" && secondary);
        setAutoHidden(node, hidden, "right-rail");
      });
      accessories.forEach((node) => setAutoHidden(node, !accessoryFits, "composer-rail"));

      const taskLayout = leftFits && rightRail === "full"
        ? "full"
        : leftFits || rightRail === "single" || accessoryFits
          ? "reduced"
          : "minimal";
      setAdaptiveAttribute(root, "data-cts-task-layout", taskLayout);
      setAdaptiveAttribute(root, "data-cts-left-rail", leftFits ? "full" : "none");
      setAdaptiveAttribute(root, "data-cts-right-rail", rightRail);
      setAdaptiveAttribute(root, "data-cts-composer-accessory", accessoryFits ? "visible" : "hidden");
      setAdaptiveAttribute(root, "data-cts-workspace-width", Math.round(workspaceBox.width));
      setAdaptiveAttribute(root, "data-cts-workspace-height", Math.round(workspaceHeight));
      setAdaptiveAttribute(root, "data-cts-left-gutter", Math.round(leftGutter));
      setAdaptiveAttribute(root, "data-cts-right-gutter", Math.round(rightGutter));
    };
    const liveLayoutSignature = (nodes) => nodes.filter(Boolean).map((node) => {
      const box = node.getBoundingClientRect();
      return [box.left, box.top, box.width, box.height]
        .map((value) => Math.round(value * 4) / 4)
        .join(":");
    }).join("|");
    const syncLiveLayout = (decorate = false) => {
      if (themeDisposed) return "";
      const main = findMain();
      const aside = document.querySelector("aside.app-shell-left-panel");
      const home = syncRouteState();
      const stage = findStage();
      const workspace = adaptiveLayout
        ? document.querySelector(".thread-scroll-container") || main
        : null;
      const composer = adaptiveLayout
        ? document.querySelector(".composer-surface-chrome")
        : null;

      syncLayoutObservers(adaptiveLayout
        ? [main, workspace, composer]
        : [main]);
      syncStageGeometry(main, stage);
      if (decorate) decorateSharedSurfaces(main, aside, home);
      spec.onEnsure?.({ ...api, main, aside, home, stage });
      syncAdaptiveVisibility(main, stage, home);

      return liveLayoutSignature([main, workspace, composer]);
    };
    const startLayoutTracking = () => {
      if (themeDisposed || layoutFrame) return;
      layoutTrackingStartedAt = window.performance.now();
      layoutLastSignature = "";
      layoutStableFrames = 0;
      const track = (timestamp) => {
        layoutFrame = 0;
        if (themeDisposed) return;
        const signature = syncLiveLayout();
        if (signature && signature === layoutLastSignature) {
          layoutStableFrames += 1;
        } else {
          layoutLastSignature = signature;
          layoutStableFrames = 0;
        }
        if (layoutStableFrames >= 2 || timestamp - layoutTrackingStartedAt >= 480) {
          schedule();
          return;
        }
        layoutFrame = window.requestAnimationFrame(track);
      };
      layoutFrame = window.requestAnimationFrame(track);
    };
    const decorateSidebar = (aside) => {
      const sidebar = spec.sidebar;
      if (!aside || !sidebar) return;
      const palette = sidebar.palette || [];
      const projectTone = sidebar.projectTone || "phase";
      const threadIndex = sidebar.threadIndex || "thread";
      const threadTone = sidebar.threadTone || "";
      const projectRows = '[data-app-action-sidebar-project-row]';
      const threadRows = '[data-app-action-sidebar-thread-row]';

      aside.querySelectorAll('[data-sidebar-project-drop-zone="project-icon"]').forEach((icon, index) => {
        const heading = icon.parentElement;
        const row = heading?.closest(projectRows);
        mark(heading, roleClass("project-heading"));
        setData(heading, "index", String(index + 1).padStart(2, "0"));
        if (row && palette.length) setData(row, projectTone, palette[index % palette.length]);
      });

      if (sidebar.inheritProjectTone && threadTone) {
        let tone = palette[0] || "";
        let index = 0;
        aside.querySelectorAll(`${projectRows},${threadRows}`).forEach((row) => {
          if (row.hasAttribute("data-app-action-sidebar-project-row")) {
            tone = row.getAttribute(dataName(projectTone)) || tone;
          } else {
            setData(row, threadTone, tone);
            setData(row, threadIndex, String(++index).padStart(2, "0"));
          }
        });
      } else {
        let index = 0;
        aside.querySelectorAll(threadRows).forEach((row) => {
          setData(row, threadIndex, String(++index).padStart(2, "0"));
        });
      }

      const sections = sidebar.sections || {};
      aside.querySelectorAll("*").forEach((node) => {
        if (node.children.length !== 0) return;
        const text = node.textContent?.trim() || "";
        if (sidebar.expandLabel && text === sidebar.expandLabel) {
          mark(node, roleClass("expand-label"));
          mark(node.parentElement, roleClass("expand-row"));
        }
        if (!Object.prototype.hasOwnProperty.call(sections, text)) return;
        mark(node.parentElement, roleClass("section-label"));
        setData(node.parentElement, "section", sections[text]);
      });
    };
    const decorateMarkdownSurface = (content) => {
      if (!content?.isConnected) return null;
      mark(content, roleClass("markdown"));
      const unit = content.closest("[data-content-search-unit-key]");
      const key = unit?.getAttribute("data-content-search-unit-key") || "";
      if (key.endsWith(":assistant")) mark(unit, roleClass("message-assistant"));
      if (key.endsWith(":user")) mark(unit, roleClass("message-user"));
      if (key.endsWith(":assistant") || key.endsWith(":user")) {
        mark(unit.closest('[class*="thread-content-max-width"]'), roleClass("chat-paper"));
        return unit;
      }
      return null;
    };
    const decorateTaskHeaders = () => {
      document.querySelectorAll('[data-testid="app-shell-header-context-menu-surface"]').forEach((header) => {
        if (!header.isConnected) return;
        const title = header.querySelector("span > span.truncate")?.parentElement;
        mark(header, roleClass("task-header"));
        mark(title, roleClass("task-title"));
      });
    };
    const decorateTaskCriticalSurfaces = (mutations) => {
      if (!mutations?.length || !html.classList.contains(roleClass("is-task"))) return;
      const markdownSelector = '[class*="_markdownContent_"]';
      const contents = new Set();
      const collectMarkdown = (node, includeDescendants = false) => {
        const element = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
        if (!element?.isConnected) return;
        const closest = element.matches(markdownSelector)
          ? element
          : element.closest(markdownSelector);
        if (closest?.isConnected) contents.add(closest);
        if (includeDescendants) {
          element.querySelectorAll(markdownSelector).forEach((content) => {
            if (content.isConnected) contents.add(content);
          });
        }
      };

      for (const mutation of mutations) {
        collectMarkdown(mutation.target);
        mutation.addedNodes?.forEach((node) => {
          collectMarkdown(node, node.nodeType === Node.ELEMENT_NODE);
        });
      }

      contents.forEach(decorateMarkdownSurface);
      // Codex commonly reuses the header node and replaces its class/text during
      // task navigation, so the tiny header query is safer than relying only on
      // added nodes. This path performs no layout reads or whole-page scans.
      decorateTaskHeaders();
    };
    const decorateSharedSurfaces = (main, aside, home) => {
      mark(main, roleClass("main"));
      mark(aside, roleClass("sidebar"));
      mark(home, roleClass("home"));
      mark(document.querySelector(".group\\/application-menu-top-bar"), roleClass("window-bar"));

      let messageIndex = 0;
      document.querySelectorAll('[class*="_markdownContent_"]').forEach((content) => {
        const unit = decorateMarkdownSurface(content);
        if (unit) {
          setData(unit, "message", String(++messageIndex).padStart(2, "0"));
        }
      });

      document.querySelectorAll("button").forEach((button) => {
        const label = button.textContent?.trim();
        if (label !== "输出" && label !== "来源") return;
        const section = button.closest("section");
        mark(section, roleClass("output-section"));
        mark(section?.querySelector("header"), roleClass("output-header"));
        mark(section?.parentElement?.parentElement, roleClass("output-panel"));
      });
      const outputOpen = Array.from(document.querySelectorAll(`.${roleClass("output-panel")}`)).some((panel) => {
        const box = panel.getBoundingClientRect();
        return box.width > 120 && box.height > 80;
      });
      html.classList.toggle(roleClass("has-output"), outputOpen);

      document.querySelectorAll(`[${dataName("card")}]`).forEach((node) => node.removeAttribute(dataName("card")));
      home?.querySelector(".group\\/home-suggestions")?.querySelectorAll("button").forEach((button, index) => {
        setData(button, "card", String(index + 1).padStart(2, "0"));
      });
      if (!home) decorateTaskHeaders();
      decorateSidebar(aside);
    };

    const api = Object.freeze({
      namespace,
      themeClass,
      templateVersion,
      html,
      root,
      document,
      window,
      config,
      roleClass,
      dataName,
      mark,
      setData,
      syncRouteState,
      positionComposerAccessory,
      positionPanelAboveCards,
    });
    const ensure = () => {
      if (themeDisposed) return;
      ensureTimer = 0;
      syncLiveLayout(true);
    };
    const schedule = (routeStateIsCurrent = false) => {
      if (themeDisposed) return;
      if (!routeStateIsCurrent) syncRouteState();
      if (ensureTimer) window.clearTimeout(ensureTimer);
      ensureTimer = window.setTimeout(ensure, spec.debounceMilliseconds ?? 96);
    };
    const onDocumentMutations = (mutations) => {
      if (themeDisposed) return;
      syncRouteState();
      decorateTaskCriticalSurfaces(mutations);
      schedule(true);
    };
    const cleanup = () => {
      if (themeDisposed) return true;
      themeDisposed = true;
      if (ensureTimer) window.clearTimeout(ensureTimer);
      if (layoutFrame) window.cancelAnimationFrame(layoutFrame);
      layoutFrame = 0;
      layoutResizeObserver?.disconnect();
      layoutResizeObserver = null;
      layoutObserved = new Set();
      spec.onCleanup?.(api);
      for (const [node, className] of marked) {
        try { node?.classList?.remove(className); } catch { }
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
      );
      root.removeAttribute("data-cts-template-version");
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
