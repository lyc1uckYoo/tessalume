// TESSALUME_RUNTIME_FRAGMENT: payload bootstrap, visual settings, assets, and Template 1.0 rendering
(async () => {
  const RUNTIME_KEY = "__TESSALUME_RUNTIME__";
  const themeId = __TESSALUME_PAYLOAD_THEME_ID_JSON__;
  const templateCssText = __TESSALUME_PAYLOAD_TEMPLATE_CSS_JSON__;
  const cssText = __TESSALUME_PAYLOAD_CSS_JSON__;
  const scriptText = __TESSALUME_PAYLOAD_SCRIPT_JSON__;
  const stagedAssetDataUrls = window.__TESSALUME_STAGED_ASSETS__;
  const stagedVisualSettings = window.__TESSALUME_STAGED_VISUAL_SETTINGS__;
  delete window.__TESSALUME_STAGED_ASSETS__;
  delete window.__TESSALUME_STAGED_VISUAL_SETTINGS__;
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

  const disposeCompatibleRuntime = async () => {
    if (window[RUNTIME_KEY]?.dispose) {
      await window[RUNTIME_KEY].dispose();
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
        await candidate.dispose();
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

  const root = document.createElement("div");
  root.id = "tessalume-theme-root";
  root.dataset.themeId = themeId;

  const managedCleanups = [];
  const assetVariables = [];
  const assetAssignments = [];
  const assetObjectUrls = [];
  const customAssetObjectUrls = new Map();
  const visualSettingVariables = new Set();
  let definition = null;
  let disposed = false;

  const addCleanup = (cleanup) => {
    if (typeof cleanup !== "function") throw new TypeError("cleanup must be a function");
    managedCleanups.push(cleanup);
    return cleanup;
  };

  const setVisualSettings = (settings = {}) => {
    const html = document.documentElement;
    const readPercent = (value, fallback, minimum, maximum) => {
      const number = Number(value);
      return Number.isFinite(number) ? Math.min(maximum, Math.max(minimum, number)) : fallback;
    };
    const readChoice = (value, fallback, choices) => {
      const candidate = String(value || "").trim().toLowerCase();
      return choices.includes(candidate) ? candidate : fallback;
    };
    const readColor = (value) => /^#[0-9a-f]{6}$/i.test(String(value || ""))
      ? String(value).toUpperCase()
      : "#000000";
    const rgba = (hex, opacity) => {
      const value = Number.parseInt(hex.slice(1), 16);
      return `rgba(${(value >> 16) & 255},${(value >> 8) & 255},${value & 255},${opacity})`;
    };
    const readability = [];
    for (const mode of ["light", "dark"]) {
      for (const region of ["hero", "sidebar", "chat"]) {
        const adjustment = settings?.[mode]?.[region] || {};
        const brightness = readPercent(adjustment.brightness, 100, 20, 180) / 100;
        const contrast = readPercent(adjustment.contrast, 100, 20, 180) / 100;
        const saturation = readPercent(adjustment.saturation, 100, 0, 200) / 100;
        const opacity = readPercent(adjustment.opacity, 100, 0, 100) / 100;
        const zoom = readPercent(adjustment.zoom, 100, 70, 200) / 100;
        const offsetX = readPercent(adjustment.offsetX, 0, -200, 200);
        const offsetY = readPercent(adjustment.offsetY, 0, -200, 200);
        const grayscale = readPercent(adjustment.grayscale, 0, 0, 100) / 100;
        const hueRotation = readPercent(adjustment.hueRotation, 0, -180, 180);
        const blur = readPercent(adjustment.blur, 0, 0, 20);
        const overlayColor = readColor(adjustment.overlayColor);
        const overlayOpacity = readPercent(adjustment.overlayOpacity, 0, 0, 100) / 100;
        const gradientStrength = readPercent(adjustment.gradientStrength, 0, 0, 100) / 100;
        const vignette = readPercent(adjustment.vignette, 0, 0, 100) / 100;
        const blendMode = readChoice(
          adjustment.blendMode,
          "normal",
          ["normal", "multiply", "screen", "overlay", "soft-light", "luminosity"],
        );
        const filterVariable = `--tessalume-visual-${region}-${mode}-filter`;
        const opacityVariable = `--tessalume-visual-${region}-${mode}-opacity`;
        const translateVariable = `--tessalume-visual-${region}-${mode}-translate`;
        const scaleVariable = `--tessalume-visual-${region}-${mode}-scale`;
        const blendVariable = `--tessalume-visual-${region}-${mode}-blend`;
        const assetVariable = `--tessalume-asset-${region}-${mode}`;
        const originalAssetUrl = assetAssignments.find(([name]) => name === assetVariable)?.[1] || null;
        const previousCustomUrl = customAssetObjectUrls.get(`${region}-${mode}`);
        if (previousCustomUrl) {
          URL.revokeObjectURL(previousCustomUrl);
          customAssetObjectUrls.delete(`${region}-${mode}`);
        }
        let imageUrl = originalAssetUrl;
        if (typeof adjustment.customImageDataUrl === "string" && adjustment.customImageDataUrl) {
          imageUrl = createAssetObjectUrl(adjustment.customImageDataUrl);
          customAssetObjectUrls.set(`${region}-${mode}`, imageUrl);
        }
        if (imageUrl) {
          const layers = [];
          if (vignette > 0) {
            layers.push(`radial-gradient(circle at center,transparent 45%,rgba(0,0,0,${Math.min(.78, vignette * .78)}) 100%)`);
          }
          if (gradientStrength > 0) {
            layers.push(`linear-gradient(90deg,${rgba(overlayColor, Math.min(.82, gradientStrength * .82))},transparent 72%)`);
          }
          if (overlayOpacity > 0) {
            layers.push(`linear-gradient(${rgba(overlayColor, Math.min(.86, overlayOpacity * .86))},${rgba(overlayColor, Math.min(.86, overlayOpacity * .86))})`);
          }
          layers.push(`url("${imageUrl}")`);
          html.style.setProperty(assetVariable, layers.join(","));
          visualSettingVariables.add(assetVariable);
        }
        html.style.setProperty(
          filterVariable,
          `brightness(${brightness}) contrast(${contrast}) saturate(${saturation}) grayscale(${grayscale}) hue-rotate(${hueRotation}deg) blur(${blur}px)`,
        );
        html.style.setProperty(opacityVariable, String(opacity));
        html.style.setProperty(translateVariable, `${offsetX}px ${offsetY}px`);
        html.style.setProperty(scaleVariable, String(zoom));
        html.style.setProperty(blendVariable, blendMode);
        visualSettingVariables.add(filterVariable);
        visualSettingVariables.add(opacityVariable);
        visualSettingVariables.add(translateVariable);
        visualSettingVariables.add(scaleVariable);
        visualSettingVariables.add(blendVariable);
        if (adjustment.readabilityProtection === true) readability.push(`${region}-${mode}`);
      }
    }
    html.dataset.tessalumeReadability = readability.join(" ");
    const display = settings?.display || {};
    html.dataset.tessalumeMotion = readChoice(
      display.motionIntensity,
      "full",
      ["full", "reduced", "off"],
    );
    html.dataset.tessalumeTextScale = readChoice(
      display.textScale,
      "standard",
      ["small", "standard", "large"],
    );
    html.dataset.tessalumeDensity = readChoice(
      display.density,
      "comfortable",
      ["compact", "comfortable", "spacious"],
    );
    return true;
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

  try {
    for (const [name, dataUrl] of Object.entries(assetDataUrls)) {
      const variable = `--tessalume-asset-${name.replace(/[^a-z0-9_-]/gi, "-")}`;
      assetAssignments.push([variable, createAssetObjectUrl(dataUrl)]);
    }
  } catch (error) {
    for (const objectUrl of assetObjectUrls) URL.revokeObjectURL(objectUrl);
    throw error;
  }

  try {
    if (!(await disposeCompatibleRuntime()) && window.__CODEX_DREAM_SKIN_STATE__?.cleanup) {
      window.__CODEX_DREAM_SKIN_STATE__.cleanup();
    }
  } catch (error) {
    for (const objectUrl of assetObjectUrls) URL.revokeObjectURL(objectUrl);
    throw error;
  }

  (document.head || document.documentElement).appendChild(style);
  document.body?.appendChild(root);
  for (const [variable, objectUrl] of assetAssignments) {
    document.documentElement.style.setProperty(variable, `url("${objectUrl}")`);
    assetVariables.push(variable);
  }

  setVisualSettings(stagedVisualSettings || {});

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
