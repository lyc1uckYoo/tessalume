// TESSALUME_RUNTIME_FRAGMENT: payload bootstrap, assets, and Template 1.0 rendering
(async () => {
  const RUNTIME_KEY = "__TESSALUME_RUNTIME__";
  const themeId = __TESSALUME_PAYLOAD_THEME_ID_JSON__;
  const templateCssText = __TESSALUME_PAYLOAD_TEMPLATE_CSS_JSON__;
  const cssText = __TESSALUME_PAYLOAD_CSS_JSON__;
  const hasThemeScript = __TESSALUME_PAYLOAD_HAS_SCRIPT__;
  const stagedAssetDataUrls = window.__TESSALUME_STAGED_ASSETS__;
  const stagedVisualSettings = window.__TESSALUME_STAGED_VISUAL_SETTINGS__;
  const stagedVisualImages = window.__TESSALUME_STAGED_VISUAL_IMAGES__;
  delete window.__TESSALUME_STAGED_ASSETS__;
  delete window.__TESSALUME_STAGED_VISUAL_SETTINGS__;
  delete window.__TESSALUME_STAGED_VISUAL_IMAGES__;
  const assetDataUrls = stagedAssetDataUrls || __TESSALUME_PAYLOAD_ASSETS_JSON__;
  const config = __TESSALUME_PAYLOAD_CONFIG_JSON__;
  const allowPetOverlay = __TESSALUME_PAYLOAD_ALLOW_PET_OVERLAY__;
  const fingerprint = __TESSALUME_PAYLOAD_FINGERPRINT_JSON__;
  const compatibilityProfile = __TESSALUME_PAYLOAD_COMPATIBILITY_PROFILE_JSON__;
  const compatibilitySelectors = compatibilityProfile?.selectors || {};
  const isPetOverlay = new URLSearchParams(location.search).get("initialRoute") === "/avatar-overlay";

  const selectorList = (name, fallback = []) => {
    const configured = compatibilitySelectors[name];
    return Array.isArray(configured) && configured.length
      ? configured.filter((selector) => typeof selector === "string" && selector.trim())
      : fallback;
  };
  const queryFirst = (scope, name, fallback = []) => {
    if (!scope?.querySelector) return null;
    for (const selector of selectorList(name, fallback)) {
      try {
        const match = scope.querySelector(selector);
        if (match) return match;
      } catch { }
    }
    return null;
  };
  const queryAll = (scope, name, fallback = []) => {
    if (!scope?.querySelectorAll) return [];
    const matches = new Set();
    for (const selector of selectorList(name, fallback)) {
      try {
        scope.querySelectorAll(selector).forEach((node) => matches.add(node));
      } catch { }
    }
    return Array.from(matches);
  };
  const closestFirst = (node, name, fallback = []) => {
    if (!node?.closest) return null;
    for (const selector of selectorList(name, fallback)) {
      try {
        const match = node.closest(selector);
        if (match) return match;
      } catch { }
    }
    return null;
  };

  const disposeCompatibleRuntime = async (preserveSharedAppearance = false) => {
    if (window[RUNTIME_KEY]?.dispose) {
      await window[RUNTIME_KEY].dispose({ preserveSharedAppearance });
      return true;
    }
    for (const key of Object.getOwnPropertyNames(window)) {
      if (key === RUNTIME_KEY) continue;
      const descriptor = Object.getOwnPropertyDescriptor(window, key);
      const candidate = descriptor && "value" in descriptor ? descriptor.value : null;
      if (
        candidate &&
        typeof candidate === "object" &&
        typeof candidate.dispose === "function" &&
        typeof candidate.themeId === "string" &&
        typeof candidate.fingerprint === "string" &&
        candidate.context &&
        typeof candidate.context.mountCanonicalTheme === "function"
      ) {
        await candidate.dispose({ preserveSharedAppearance });
        return true;
      }
    }
    return false;
  };

  if (isPetOverlay && !allowPetOverlay) {
    if (!(await disposeCompatibleRuntime()) && window.__CODEX_DREAM_SKIN_STATE__?.cleanup) {
      window.__CODEX_DREAM_SKIN_STATE__.cleanup();
    }
    // This target was intentionally handled. Keep the marker so the desktop
    // watcher does not resend the (potentially very large) asset payload on
    // every health check just because this compact window has no theme UI.
    window.__TESSALUME_THEME_ID__ = themeId;
    return { installed: false, skipped: "pet-overlay" };
  }

  const safeThemeId = themeId.replace(/[^a-z0-9_-]/gi, "-");
  const style = document.createElement("style");
  style.id = "tessalume-theme-style";
  style.dataset.themeId = themeId;
  style.textContent = `${templateCssText}\n${cssText}`;
  const visualMotionStyle = document.createElement("style");
  visualMotionStyle.id = "tessalume-artwork-motion-style";
  visualMotionStyle.dataset.themeId = themeId;

  const root = document.createElement("div");
  root.id = "tessalume-theme-root";
  root.dataset.themeId = themeId;

  const managedCleanups = [];
  const assetVariables = [];
  const assetAssignments = [];
  const assetObjectUrls = [];
  const customImageObjectUrls = new Map();
  const customSlotImageKeys = new Map();
  const visualSettingVariables = new Set();
  const visualSlotStates = new Map();
  const visualImageDimensions = new Map();
  let visualPlacementRevision = 0;
  let visualSurfaceResizeObserver = null;
  let definition = null;
  let disposed = false;
  let syncDisplayPreferences = () => {};

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

  const createObjectUrl = (dataUrl) => {
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

    return URL.createObjectURL(new Blob([bytes], { type: mimeType }));
  };

  const createAssetObjectUrl = (dataUrl) => {
    const objectUrl = createObjectUrl(dataUrl);
    assetObjectUrls.push(objectUrl);
    return objectUrl;
  };

  const preloadAssetObjectUrl = async (dataUrl, objectUrl) => {
    if (!dataUrl.startsWith("data:image/")) return;
    const image = new Image();
    image.decoding = "async";
    image.src = objectUrl;
    await image.decode();
  };

  try {
    const pendingAssetDecodes = [];
    for (const [name, dataUrl] of Object.entries(assetDataUrls)) {
      const variable = `--tessalume-asset-${name.replace(/[^a-z0-9_-]/gi, "-")}`;
      const objectUrl = createAssetObjectUrl(dataUrl);
      assetAssignments.push([variable, objectUrl]);
      pendingAssetDecodes.push(preloadAssetObjectUrl(dataUrl, objectUrl));
    }
    await Promise.all(pendingAssetDecodes);
  } catch (error) {
    for (const objectUrl of assetObjectUrls) URL.revokeObjectURL(objectUrl);
    throw error;
  }

  (document.head || document.documentElement).appendChild(style);
  (document.head || document.documentElement).appendChild(visualMotionStyle);
  addCleanup(() => visualMotionStyle.remove());
  document.body?.appendChild(root);
  for (const [variable, objectUrl] of assetAssignments) {
    document.documentElement.style.setProperty(variable, `url("${objectUrl}")`);
    assetVariables.push(variable);
  }

  document.documentElement.classList.add("tessalume-theme-active", `tessalume-theme-${safeThemeId}`);

  const renderTemplateV1 = (spec) => {
    if (!spec || typeof spec !== "object") {
      throw new TypeError("renderTemplateV1 expects a slot specification");
    }
    const createSlot = (slotName, role, part, priority, defaultTag) => {
      const slot = spec[slotName];
      if (!slot || typeof slot !== "object") {
        throw new TypeError(`Template 1.0 requires the ${slotName} slot`);
      }
      const tagName = String(slot.tag || defaultTag);
      if (!/^[a-z][a-z0-9-]*$/i.test(tagName)) {
        throw new TypeError(`Invalid Template 1.0 slot tag: ${tagName}`);
      }
      const element = document.createElement(tagName);
      if (slot.className) element.className = String(slot.className);
      element.setAttribute("data-theme-role", role);
      element.setAttribute("data-theme-part", part);
      if (priority) element.setAttribute("data-theme-priority", priority);
      for (const [name, value] of Object.entries(slot.attributes || {})) {
        if (/^(?:class|data-theme-(?:role|part|priority))$/i.test(name)) continue;
        element.setAttribute(name, String(value));
      }
      element.innerHTML = String(slot.html || "");
      return element;
    };
    const appendDecorations = (parent, html) => {
      if (!html) return;
      const template = document.createElement("template");
      template.innerHTML = String(html);
      parent.append(...template.content.childNodes);
    };

    const stage = document.createElement("div");
    stage.className = String(spec.stageClass || "");
    stage.setAttribute("data-theme-stage", "");
    const hero = createSlot("hero", "hero", "hero-copy", null, "section");
    const identity = createSlot("identity", "identity", "identity", null, "div");
    const taskLeft = createSlot("taskLeft", "task-left", "task-card-left", null, "aside");
    const taskSecondary = createSlot(
      "taskSecondary", "task-right", "task-card-right-secondary", "secondary", "aside");
    const taskPrimary = createSlot(
      "taskPrimary", "task-right", "task-card-right-primary", "primary", "aside");
    const memory = createSlot("memory", "memory", "memory-card", null, "aside");
    const syncPanel = createSlot("syncPanel", "sync-panel", "sync-panel", "secondary", "section");
    const composerAccessory = createSlot(
      "composerAccessory", "composer-accessory", "composer-accessory", null, "div");
    stage.append(hero, identity, taskLeft, taskSecondary, taskPrimary, memory);
    appendDecorations(stage, spec.stageDecorations);
    root.replaceChildren(stage);
    appendDecorations(root, spec.rootDecorations);
    root.append(syncPanel, composerAccessory);
    return Object.freeze({
      stage,
      hero,
      identity,
      taskLeft,
      taskSecondary,
      taskPrimary,
      memory,
      syncPanel,
      composerAccessory,
    });
  };

  let context;
// TESSALUME_STANDALONE_ENVELOPE_START
})()
// TESSALUME_STANDALONE_ENVELOPE_END
