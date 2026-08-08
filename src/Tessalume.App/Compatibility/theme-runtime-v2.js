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
  let syncDisplayPreferences = () => {};

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
    syncDisplayPreferences();
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
// TESSALUME_RUNTIME_FRAGMENT: semantic page recognition, route state, and stable surface discovery
// TESSALUME_STANDALONE_ENVELOPE_START
(async () => {
// TESSALUME_STANDALONE_ENVELOPE_END
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
    const surfaced = [];
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
      root.setAttribute("data-tessalume-template-version", templateVersion);
    };
// TESSALUME_STANDALONE_ENVELOPE_START
  };
})()
// TESSALUME_STANDALONE_ENVELOPE_END
    const mark = (node, className) => {
      if (!node || node.classList.contains(className)) return node;
      node.classList.add(className);
      marked.push([node, className]);
      return node;
    };
    const markSurface = (node, surface) => {
      if (!node) return node;
      const previous = node.getAttribute("data-tessalume-surface");
      if (previous === surface) return node;
      node.setAttribute("data-tessalume-surface", surface);
      surfaced.push([node, previous]);
      return node;
    };
    const markMessage = (node, role) => {
      if (!node) return node;
      const previous = node.getAttribute("data-tessalume-message");
      if (previous === role) return node;
      node.setAttribute("data-tessalume-message", role);
      surfaced.push([node, previous, "data-tessalume-message"]);
      return node;
    };
    const setData = (node, name, value) => {
      if (!node) return;
      node.setAttribute(dataName(name), String(value));
    };
    const findHome = () => {
      const icon = queryFirst(document, "homeIcon", ['[data-testid="home-icon"]']);
      return closestFirst(icon, "homeAncestor", ['[role="main"]', "main"]);
    };
    const findMain = () => queryFirst(document, "main", ["main.main-surface", "main"]);
    const findComposerSurface = () => {
      const legacySurface = queryFirst(
        document,
        "composerLegacySurface",
        [".composer-surface-chrome"],
      );
      const editor = queryFirst(
        document,
        "composerEditor",
        ['[data-codex-composer="true"]'],
      );
      const surface = legacySurface ||
        closestFirst(editor, "composerRootAncestor", ['[class*="ComposerLayoutRoot"]']) ||
        closestFirst(editor, "composerBodyAncestor", ['[class*="ComposerLayoutBody"]'])?.parentElement ||
        null;
      if (!surface) return null;

      // Codex renamed both the composer root and footer CSS-module classes in
      // mid-2026. Keep the stable Tessalume aliases at the compatibility layer
      // so every existing theme can continue styling the native composer.
      mark(surface, "composer-surface-chrome");
      const footer = queryFirst(
        surface,
        "composerFooter",
        ['[class*="ComposerLayoutFooter"]', '[class*="_footer_"]'],
      );
      if (footer && !footer.matches('[class*="_footer_"]')) mark(footer, "_footer_");
      return surface;
    };
    const findSettingsSurface = (main = findMain()) => {
      if (!main) return null;
      return queryAll(
        main,
        "settingsSurface",
        [".main-surface.flex.h-full.min-h-0.flex-col"],
      ).find((surface) => queryFirst(
        surface,
        "settingsScrollChild",
        [":scope > .scrollbar-stable.flex-1.overflow-y-auto.p-panel"],
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
      html.classList.toggle("tessalume-is-home", isHome);
      html.classList.toggle("tessalume-is-task", !isHome);
      html.classList.toggle("tessalume-is-settings", Boolean(settingsSurface));
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
// TESSALUME_RUNTIME_FRAGMENT: effective motion, text-scale, and density preferences
// TESSALUME_STANDALONE_ENVELOPE_START
(async () => {
  const root = document.createElement("div");
  const html = document.documentElement;
  let themeDisposed = false;
  let syncDisplayPreferences = () => {};
  const addCleanup = () => {};
  const mountCanonicalTheme = () => {
// TESSALUME_STANDALONE_ENVELOPE_END
    const MotionReductionFactor = .55;
    const TextScaleFactors = Object.freeze({ small:.9, standard:1, large:1.16 });
    const textStyles = new Map();
    const densityStyles = new Map();
    const animationRates = new Map();
    const animationFrames = new Map();
    let preferenceFrame = 0;

    const removeLegacyReducedMotionRule = () => {
      const sheet = style.sheet;
      if (!sheet) return;
      try {
        for (let index = sheet.cssRules.length - 1; index >= 0; index -= 1) {
          const rule = sheet.cssRules[index];
          if (rule instanceof CSSStyleRule &&
              rule.selectorText.includes('data-tessalume-motion="reduced"') &&
              rule.style.getPropertyValue("animation-duration").trim() === ".8s") {
            sheet.deleteRule(index);
          }
        }
      } catch { }
    };
    removeLegacyReducedMotionRule();

    const ensureStyleRecord = (registry, node) => {
      let record = registry.get(node);
      if (!record) {
        record = { inline:new Map(), metrics:Object.create(null) };
        registry.set(node, record);
      }
      return record;
    };
    const rememberInlineStyle = (record, node, property) => {
      if (record.inline.has(property)) return;
      record.inline.set(property, {
        value:node.style.getPropertyValue(property),
        priority:node.style.getPropertyPriority(property),
      });
    };
    const setManagedStyle = (registry, node, property, value) => {
      const record = ensureStyleRecord(registry, node);
      rememberInlineStyle(record, node, property);
      node.style.setProperty(property, value, "important");
      return record;
    };
    const restoreManagedStyles = (registry) => {
      for (const [node, record] of registry) {
        if (!node?.style) continue;
        for (const [property, previous] of record.inline) {
          if (previous.value) {
            node.style.setProperty(property, previous.value, previous.priority);
          } else {
            node.style.removeProperty(property);
          }
        }
      }
      registry.clear();
    };
    const withNeutralPreference = (datasetName, neutralValue, callback) => {
      const previous = html.dataset[datasetName];
      html.dataset[datasetName] = neutralValue;
      try { return callback(); }
      finally {
        if (previous == null) delete html.dataset[datasetName];
        else html.dataset[datasetName] = previous;
      }
    };
    const finitePixels = (value, fallback = 0) => {
      const number = Number.parseFloat(value);
      return Number.isFinite(number) ? number : fallback;
    };
    const pixels = (value) => `${Math.round(value * 100) / 100}px`;

    const cloneKeyframes = (effect) => effect.getKeyframes().map((frame) => {
      const copy = { ...frame };
      delete copy.computedOffset;
      return copy;
    });
    const softenTransform = (reference, value) => {
      if (!reference || !value || reference === "none" || value === "none") return value;
      try {
        const origin = new DOMMatrixReadOnly(reference);
        const target = new DOMMatrixReadOnly(value);
        const names = [
          "m11", "m12", "m13", "m14", "m21", "m22", "m23", "m24",
          "m31", "m32", "m33", "m34", "m41", "m42", "m43", "m44",
        ];
        const values = names.map((name) =>
          origin[name] + ((target[name] - origin[name]) * MotionReductionFactor));
        return `matrix3d(${values.join(",")})`;
      } catch {
        return value;
      }
    };
    const softenKeyframes = (frames) => {
      const reference = frames.find((frame) => frame.transform && frame.transform !== "none")?.transform;
      if (!reference) return frames;
      return frames.map((frame) => frame.transform ? {
        ...frame,
        transform:softenTransform(reference, frame.transform),
      } : { ...frame });
    };

    const collectTextTargets = () => {
      const targets = new Set();
      const selector = [
        "p", "li", "td", "th", "h1", "h2", "h3", "h4", "h5", "h6",
        "blockquote", "figcaption", "label", "button", "a", "input", "textarea",
        "code", "pre", "span", "[role='button']", "[role='textbox']",
        "[contenteditable='true']", "[data-user-message-bubble='true']",
        "[data-app-action-sidebar-project-row]", "[data-app-action-sidebar-thread-row]",
      ].join(",");
      document.querySelectorAll(
        '[data-tessalume-surface="main"],[data-tessalume-surface="sidebar"]',
      ).forEach((surface) => {
        if (root.contains(surface)) return;
        if (surface.matches(selector)) targets.add(surface);
        surface.querySelectorAll(selector).forEach((node) => targets.add(node));
      });
      return Array.from(targets).filter((node) =>
        node.isConnected &&
        !root.contains(node) &&
        !node.closest("svg") &&
        node.getAttribute("aria-hidden") !== "true" &&
        (node.matches("input,textarea,[contenteditable='true']") || /\S/.test(node.textContent || "")));
    };
    const applyTextScale = () => {
      const mode = html.dataset.tessalumeTextScale || "standard";
      const factor = TextScaleFactors[mode] || 1;
      if (factor === 1) {
        restoreManagedStyles(textStyles);
        return;
      }

      const targets = collectTextTargets();
      const measurements = withNeutralPreference("tessalumeTextScale", "standard", () =>
        targets.map((node) => {
          const record = ensureStyleRecord(textStyles, node);
          if (record.metrics.fontSize == null) {
            const computed = getComputedStyle(node);
            record.metrics.fontSize = finitePixels(computed.fontSize);
            const lineHeight = finitePixels(computed.lineHeight, Number.NaN);
            record.metrics.lineHeight = Number.isFinite(lineHeight) ? lineHeight : null;
          }
          return [node, record];
        }));

      for (const [node, record] of measurements) {
        if (!(record.metrics.fontSize > 0)) continue;
        setManagedStyle(textStyles, node, "font-size", pixels(record.metrics.fontSize * factor));
        if (record.metrics.lineHeight > 0) {
          setManagedStyle(textStyles, node, "line-height", pixels(record.metrics.lineHeight * factor));
        }
      }
    };

    const measureDensityTarget = (node, kind) => {
      const record = ensureStyleRecord(densityStyles, node);
      if (record.metrics.kind) return record;
      const computed = getComputedStyle(node);
      record.metrics.kind = kind;
      record.metrics.paddingTop = finitePixels(computed.paddingTop);
      record.metrics.paddingBottom = finitePixels(computed.paddingBottom);
      record.metrics.marginTop = finitePixels(computed.marginTop);
      record.metrics.marginBottom = finitePixels(computed.marginBottom);
      record.metrics.height = finitePixels(computed.height);
      record.metrics.minHeight = Math.max(
        finitePixels(computed.minHeight),
        record.metrics.height,
      );
      return record;
    };
    const applyDensity = () => {
      const mode = html.dataset.tessalumeDensity || "comfortable";
      if (mode === "comfortable") {
        restoreManagedStyles(densityStyles);
        return;
      }

      const messages = Array.from(document.querySelectorAll("[data-tessalume-message]"))
        .filter((node) => node.isConnected && !root.contains(node));
      const sidebarRows = Array.from(document.querySelectorAll(
        '[data-tessalume-surface="sidebar"] :is([data-app-action-sidebar-project-row],[data-app-action-sidebar-thread-row])',
      )).filter((node) => node.isConnected && !root.contains(node));
      const measured = withNeutralPreference("tessalumeDensity", "comfortable", () => [
        ...messages.map((node) => [node, measureDensityTarget(node, "message")]),
        ...sidebarRows.map((node) => [node, measureDensityTarget(node, "sidebar-row")]),
      ]);

      for (const [node, record] of measured) {
        const metric = record.metrics;
        if (metric.kind === "message") {
          const paddingDelta = mode === "compact" ? -4 : 10;
          const margin = mode === "compact" ? -4 : 10;
          setManagedStyle(densityStyles, node, "padding-top", pixels(Math.max(0, metric.paddingTop + paddingDelta)));
          setManagedStyle(densityStyles, node, "padding-bottom", pixels(Math.max(0, metric.paddingBottom + paddingDelta)));
          setManagedStyle(densityStyles, node, "margin-top", pixels(margin));
          setManagedStyle(densityStyles, node, "margin-bottom", pixels(margin));
        } else {
          const heightDelta = mode === "compact" ? -8 : 10;
          const paddingDelta = mode === "compact" ? -3 : 5;
          const targetHeight = Math.max(30, metric.height + heightDelta);
          setManagedStyle(densityStyles, node, "height", pixels(targetHeight));
          setManagedStyle(densityStyles, node, "min-height", pixels(Math.max(targetHeight, metric.minHeight + heightDelta)));
          setManagedStyle(densityStyles, node, "padding-top", pixels(Math.max(1, metric.paddingTop + paddingDelta)));
          setManagedStyle(densityStyles, node, "padding-bottom", pixels(Math.max(1, metric.paddingBottom + paddingDelta)));
        }
      }
    };

    const applyMotionIntensity = () => {
      const reduced = html.dataset.tessalumeMotion === "reduced";
      for (const animation of root.getAnimations({ subtree:true })) {
        if (!animationRates.has(animation)) {
          const baseRate = Number.isFinite(animation.playbackRate) && animation.playbackRate !== 0
            ? animation.playbackRate
            : 1;
          animationRates.set(animation, baseRate);
        }
        const baseRate = animationRates.get(animation);
        const targetRate = reduced ? baseRate * MotionReductionFactor : baseRate;
        try {
          const effect = animation.effect;
          if (effect && typeof effect.getKeyframes === "function" && typeof effect.setKeyframes === "function") {
            if (!animationFrames.has(animation)) animationFrames.set(animation, cloneKeyframes(effect));
            const frames = animationFrames.get(animation);
            effect.setKeyframes(reduced ? softenKeyframes(frames) : frames);
          }
        } catch { }
        try {
          if (typeof animation.updatePlaybackRate === "function") {
            animation.updatePlaybackRate(targetRate);
          } else {
            animation.playbackRate = targetRate;
          }
        } catch {
          // A detached CSS animation must not block text or density updates.
        }
      }
    };
    const applyDisplayPreferences = () => {
      preferenceFrame = 0;
      if (themeDisposed) return;
      applyMotionIntensity();
      applyTextScale();
      applyDensity();
    };
    const scheduleDisplayPreferences = () => {
      if (themeDisposed || preferenceFrame) return;
      preferenceFrame = requestAnimationFrame(applyDisplayPreferences);
    };

    syncDisplayPreferences = scheduleDisplayPreferences;
    const preferenceObserver = new MutationObserver(scheduleDisplayPreferences);
    preferenceObserver.observe(document.documentElement, { childList:true, subtree:true });
    addCleanup(() => {
      preferenceObserver.disconnect();
      if (preferenceFrame) cancelAnimationFrame(preferenceFrame);
      preferenceFrame = 0;
      restoreManagedStyles(textStyles);
      restoreManagedStyles(densityStyles);
      for (const [animation, baseRate] of animationRates) {
        try {
          if (typeof animation.updatePlaybackRate === "function") animation.updatePlaybackRate(baseRate);
          else animation.playbackRate = baseRate;
        } catch { }
      }
      animationRates.clear();
      animationFrames.clear();
      syncDisplayPreferences = () => {};
    });
    scheduleDisplayPreferences();
// TESSALUME_STANDALONE_ENVELOPE_START
  };
})()
// TESSALUME_STANDALONE_ENVELOPE_END
// TESSALUME_RUNTIME_FRAGMENT: responsive geometry, collision handling, and layout observers
    const positionComposerAccessory = (main, selector, size = 76, gap = 18) => {
      const accessory = root.querySelector(selector);
      const composer = findComposerSurface();
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
      setAdaptiveAttribute(node, "data-tessalume-auto-hidden", hidden);
      if (hidden && reason) {
        setAdaptiveAttribute(node, "data-tessalume-auto-hidden-reason", reason);
      } else {
        node.removeAttribute("data-tessalume-auto-hidden-reason");
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
        setAdaptiveAttribute(root, "data-tessalume-task-layout", "inactive");
        return;
      }

      const workspace = queryFirst(document, "workspace", [".thread-scroll-container"]) || main;
      const composer = findComposerSurface();

      const workspaceBox = workspace?.getBoundingClientRect();
      const composerBox = composer?.getBoundingClientRect();
      const validWorkspace = workspaceBox && workspaceBox.width > 0 && workspaceBox.height > 0;
      const validComposer = composerBox && composerBox.width > 0 && composerBox.height > 0;
      if (!main || !stage || !validWorkspace || !validComposer) {
        managed.forEach((node) => setAutoHidden(node, true, "content-unavailable"));
        setAdaptiveAttribute(root, "data-tessalume-task-layout", "minimal");
        setAdaptiveAttribute(root, "data-tessalume-left-rail", "none");
        setAdaptiveAttribute(root, "data-tessalume-right-rail", "none");
        setAdaptiveAttribute(root, "data-tessalume-composer-accessory", "hidden");
        return;
      }

      const leftGutter = Math.max(0, composerBox.left - workspaceBox.left);
      const rightGutter = Math.max(0, workspaceBox.right - composerBox.right);
      const workspaceHeight = workspaceBox.height;
      const reviewOpen = html.classList.contains("tessalume-code-review-open");

      const previousLeft = root.getAttribute("data-tessalume-left-rail");
      const previousRight = root.getAttribute("data-tessalume-right-rail");
      const previousAccessory = root.getAttribute("data-tessalume-composer-accessory");

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
      setAdaptiveAttribute(root, "data-tessalume-task-layout", taskLayout);
      setAdaptiveAttribute(root, "data-tessalume-left-rail", leftFits ? "full" : "none");
      setAdaptiveAttribute(root, "data-tessalume-right-rail", rightRail);
      setAdaptiveAttribute(root, "data-tessalume-composer-accessory", accessoryFits ? "visible" : "hidden");
      setAdaptiveAttribute(root, "data-tessalume-workspace-width", Math.round(workspaceBox.width));
      setAdaptiveAttribute(root, "data-tessalume-workspace-height", Math.round(workspaceHeight));
      setAdaptiveAttribute(root, "data-tessalume-left-gutter", Math.round(leftGutter));
      setAdaptiveAttribute(root, "data-tessalume-right-gutter", Math.round(rightGutter));
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
      const aside = queryFirst(document, "sidebar", ["aside.app-shell-left-panel"]);
      const home = syncRouteState();
      const stage = findStage();
      const workspace = adaptiveLayout
        ? queryFirst(document, "workspace", [".thread-scroll-container"]) || main
        : null;
      const composer = findComposerSurface();

      syncLayoutObservers(adaptiveLayout
        ? [main, workspace, composer]
        : [main]);
      syncStageGeometry(main, stage);
      if (decorate) decorateSharedSurfaces(main, aside, home);
      spec.onEnsure?.({ ...api, main, aside, home, stage });
      syncAdaptiveVisibility(main, stage, home);

      return liveLayoutSignature(adaptiveLayout ? [main, workspace, composer] : [main]);
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
// TESSALUME_RUNTIME_FRAGMENT: sidebar, message, task, output, and shared surface decoration
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
      markSurface(content, "markdown");
      const unit = closestFirst(content, "messageUnitAncestor", ["[data-content-search-unit-key]"]);
      const key = unit?.getAttribute("data-content-search-unit-key") || "";
      const unitId = key.split(":").at(-1) || "";
      const label = unit?.querySelector("h4.sr-only")?.textContent?.trim() || "";
      const isUserMessage = Boolean(queryFirst(
        unit,
        "userMessageBubble",
        ['[data-user-message-bubble="true"]'],
      )) ||
        /^(?:you|你)\s*(?:said|说)/i.test(label);
      const isAssistantMessage = !isUserMessage &&
        (/^(?:chatgpt|assistant|助手)\s*(?:said|说)/i.test(label) || /^msg_/i.test(unitId));
      if (isAssistantMessage || key.endsWith(":assistant")) {
        mark(unit, roleClass("message-assistant"));
        markMessage(unit, "assistant");
      }
      if (isUserMessage || key.endsWith(":user")) {
        mark(unit, roleClass("message-user"));
        markMessage(unit, "user");
      }
      if (isAssistantMessage || isUserMessage ||
        key.endsWith(":assistant") || key.endsWith(":user")) {
        const paper = closestFirst(unit, "chatPaperAncestor", ['[class*="thread-content-max-width"]']);
        mark(paper, roleClass("chat-paper"));
        markSurface(paper, "chat-paper");
        return unit;
      }
      return null;
    };
    const decorateTaskHeaders = () => {
      queryAll(
        document,
        "taskHeader",
        ['[data-testid="app-shell-header-context-menu-surface"]'],
      ).forEach((header) => {
        if (!header.isConnected) return;
        const titleButton = header.querySelector("span > button.truncate");
        const title = titleButton?.parentElement?.parentElement ||
          header.querySelector("span > span.truncate")?.parentElement;
        const secondaryTitle = header.querySelector("span > span.truncate")?.parentElement;
        mark(header, roleClass("task-header"));
        markSurface(header, "task-header");
        for (const candidate of new Set([title, secondaryTitle])) {
          mark(candidate, roleClass("task-title"));
          markSurface(candidate, "task-title");
        }
      });
    };
    const decorateOutputPanels = () => {
      const sections = new Set();
      const collectSection = (node) => {
        const section = node?.closest?.("section");
        if (section?.isConnected) sections.add(section);
      };

      // The environment panel can open on a branch/status view that does not
      // render the old "Output" or "Sources" labels. Its item slot remains
      // stable across those views and after React replaces the panel subtree.
      queryAll(
        document,
        "outputPanelItem",
        ['[data-slot="thread-summary-panel-item-button"]'],
      ).forEach(collectSection);

      // Keep compatibility with older Codex builds that predate the item slot.
      const legacyLabels = new Set(["\u8f93\u51fa", "\u6765\u6e90", "Output", "Sources"]);
      document.querySelectorAll("button").forEach((button) => {
        if (legacyLabels.has(button.textContent?.trim() || "")) collectSection(button);
      });

      sections.forEach((section) => {
        const panel = section.parentElement?.parentElement;
        mark(section, roleClass("output-section"));
        mark(section.querySelector("header"), roleClass("output-header"));
        mark(panel, roleClass("output-panel"));
        markSurface(section, "output-section");
        markSurface(section.querySelector("header"), "output-header");
        markSurface(panel, "output-panel");
      });
    };
    const decorateTaskCriticalSurfaces = (mutations) => {
      if (!mutations?.length || !html.classList.contains(roleClass("is-task"))) return;
      const markdownSelector = selectorList(
        "markdownContent",
        ['[class*="_MarkdownRoot_"]', '[class*="_markdownContent_"]'],
      ).join(",");
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
      // Mark the environment panel immediately as well. Deferring this work to
      // the debounced repair can starve while live counters keep mutating.
      decorateOutputPanels();
    };
    const decorateSharedSurfaces = (main, aside, home) => {
      mark(main, roleClass("main"));
      mark(aside, roleClass("sidebar"));
      mark(home, roleClass("home"));
      const windowBar = queryFirst(
        document,
        "windowBar",
        [".group\\/application-menu-top-bar"],
      );
      mark(windowBar, roleClass("window-bar"));
      markSurface(main, "main");
      markSurface(aside, "sidebar");
      markSurface(home, "home");
      markSurface(windowBar, "window-bar");
      markSurface(findComposerSurface(), "composer");

      let messageIndex = 0;
      queryAll(
        document,
        "markdownContent",
        ['[class*="_MarkdownRoot_"]', '[class*="_markdownContent_"]'],
      ).forEach((content) => {
        const unit = decorateMarkdownSurface(content);
        if (unit) {
          setData(unit, "message", String(++messageIndex).padStart(2, "0"));
        }
      });

      decorateOutputPanels();
      const outputOpen = Array.from(document.querySelectorAll(`.${roleClass("output-panel")}`)).some((panel) => {
        const box = panel.getBoundingClientRect();
        return box.width > 120 && box.height > 80;
      });
      html.classList.toggle(roleClass("has-output"), outputOpen);
      html.classList.toggle("tessalume-has-output", outputOpen);

      document.querySelectorAll(`[${dataName("card")}]`).forEach((node) => node.removeAttribute(dataName("card")));
      queryFirst(home, "homeSuggestions", [".group\\/home-suggestions"])
        ?.querySelectorAll("button").forEach((button, index) => {
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
      const main = queryFirst(document, "main", ["main.main-surface", "main"]);
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
