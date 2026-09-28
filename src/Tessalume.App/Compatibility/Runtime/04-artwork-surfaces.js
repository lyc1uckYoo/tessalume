// TESSALUME_RUNTIME_FRAGMENT: native artwork surface discovery and personal card image layers
// TESSALUME_STANDALONE_ENVELOPE_START
(async () => {
// TESSALUME_STANDALONE_ENVELOPE_END
  const artworkRegions = ["hero", "sidebar", "chat", "taskLeft", "memory", "taskRightSecondary", "taskRightPrimary"];
  const cardArtworkSlots = {
    taskLeft: { asset: "task-left", selector: '[data-theme-role="task-left"]' },
    memory: { asset: "memory", selector: '[data-theme-role="memory"]' },
    taskRightSecondary: { asset: "task-right-secondary", selector: '[data-theme-role="task-right"][data-theme-priority="secondary"]' },
    taskRightPrimary: { asset: "task-right-primary", selector: '[data-theme-role="task-right"][data-theme-priority="primary"]' },
  };
  const nativeArtworkAssetKey = (region, mode) => {
    const card = cardArtworkSlots[region];
    if (!card || region === "memory") return `${card?.asset || region}-${mode}`;
    const dark = `${card.asset}-dark`;
    return mode === "dark" && assetAssignments.some(([name]) => name === `--tessalume-asset-${dark}`)
      ? dark : card.asset;
  };
  const isArtworkSurfaceVisible = (element) => {
    if (!element?.isConnected || element.closest('[hidden],[inert],[aria-hidden="true"]')) return false;
    const box = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    const pane = element.closest('[data-app-shell-main-content-layout]');
    if (pane && pane !== element) {
      const clip = pane.getBoundingClientRect();
      if (Math.min(box.right, clip.right) - Math.max(box.left, clip.left) <= 1 ||
          Math.min(box.bottom, clip.bottom) - Math.max(box.top, clip.top) <= 1) return false;
    }
    return box.width > 0 && box.height > 0 && box.right > 0 && box.bottom > 0 &&
      box.left < window.innerWidth && box.top < window.innerHeight &&
      style.display !== "none" && style.visibility !== "hidden" && style.visibility !== "collapse";
  };
  const artworkSurface = (region) => {
    if (cardArtworkSlots[region]) {
      const card = root.querySelector(cardArtworkSlots[region].selector);
      return { element: region === "memory" ? card : card?.querySelector('[data-theme-part="task-card-art"]'), pseudo: null };
    }
    if (region === "sidebar") {
      return { element: document.querySelector('[data-tessalume-surface="sidebar"]'), pseudo: "::after" };
    }
    if (region === "chat") {
      const main = Array.from(document.querySelectorAll('main[data-tessalume-surface="main"]'))
        .find(isArtworkSurfaceVisible);
      return {
        element: (main && Array.from(main.querySelectorAll(".thread-scroll-container"))
          .find(isArtworkSurfaceVisible)) || main,
        pseudo: "::before",
      };
    }
    return {
      element: document.querySelector(
        '[data-tessalume-home-part="banner"]',
      ) || document.querySelector(
        '[data-tessalume-surface="home"]>div:first-child>div:first-child>div:first-child',
      ),
      pseudo: "::before",
    };
  };

  // Modify only the native artwork node. Memory originally paints its image
  // on the text container, so give replacements a separate layer to ensure
  // brightness/opacity/crop never affect the caption or authored instruments.
  const cardArtworkEdits = new Map();
  const splitArtworkBackgroundLayers = (value) => {
    const layers = [];
    let depth = 0;
    let quote = "";
    let start = 0;
    for (let index = 0; index < value.length; index += 1) {
      const token = value[index];
      if (quote) {
        if (token === quote && value[index - 1] !== "\\") quote = "";
      } else if (token === '"' || token === "'") quote = token;
      else if (token === "(") depth += 1;
      else if (token === ")") depth -= 1;
      else if (token === "," && depth === 0) {
        layers.push(value.slice(start, index).trim());
        start = index + 1;
      }
    }
    layers.push(value.slice(start).trim());
    return layers;
  };
  const restoreCardArtworkEdit = (element, entry) => {
    for (const [property, value, priority] of entry.originalStyles) {
      if (value) element.style.setProperty(property, value, priority);
      else element.style.removeProperty(property);
    }
    entry.layer?.remove();
    cardArtworkEdits.delete(element);
  };
  addCleanup(() => {
    for (const [element, entry] of cardArtworkEdits) restoreCardArtworkEdit(element, entry);
  });
  const cardHasAdjustment = (state) => {
    const value = state.adjustment || {};
    return Boolean(value.customImageKey) || enumName(value.compositionMode, "theme") !== "theme" ||
      ["brightness", "contrast", "saturation", "opacity"].some((key) => finite(value[key], 100) !== 100) ||
      ["grayscale", "hueRotation", "blur", "overlayOpacity", "vignette"].some((key) => finite(value[key], 0) !== 0) ||
      enumName(value.blendMode, "normal") !== "normal";
  };
  const synchronizeCardArtworkLayers = () => {
    if (!appearanceCommitted || !root.isConnected) return;
    const mode = document.documentElement.classList.contains("electron-dark") ? "dark" : "light";
    const activeElements = new Set();
    for (const region of Object.keys(cardArtworkSlots)) {
      const state = visualSlotStates.get(`${region}-${mode}`);
      const element = artworkSurface(region).element;
      if (!state || !element || !cardHasAdjustment(state)) continue;
      activeElements.add(element);
      let entry = cardArtworkEdits.get(element);
      if (entry && entry.mode !== mode) {
        restoreCardArtworkEdit(element, entry);
        entry = null;
      }
      if (!entry) {
        const properties = region === "memory" ? ["background-image"] : [
          "background-image", "background-size", "background-position", "background-repeat",
          "filter", "opacity", "mix-blend-mode", "translate", "scale",
        ];
        entry = {
          mode,
          originalStyles: properties.map((property) => [property,
            element.style.getPropertyValue(property), element.style.getPropertyPriority(property)]),
          layer: null,
          foregroundLayers: [],
        };
        if (region === "memory") {
          const originalBackground = getComputedStyle(element).backgroundImage;
          const originalLayers = splitArtworkBackgroundLayers(originalBackground);
          const originalImageIndex = originalLayers.findIndex((layer) => /^url\(/i.test(layer));
          entry.foregroundLayers = originalImageIndex > 0 ? originalLayers.slice(0, originalImageIndex) : [];
          // Retain the original gradients and solid fill, removing only its URL
          // layer. No theme variables, captions, pseudo-elements or DOM are lost.
          element.style.setProperty("background-image", originalBackground.replace(
            /url\((?:[^"')]|"[^"]*"|'[^']*')*\)/g, "linear-gradient(transparent,transparent)"), "important");
          // A private tag avoids inheriting broad theme rules for memory span
          // meters, their pseudo-elements, or div instruments.
          const layer = document.createElement("tessalume-artwork-layer");
          layer.dataset.tessalumePersonalArtwork = "memory";
          layer.setAttribute("aria-hidden", "true");
          for (const [property, value] of Object.entries({
            // Memory's positioned root already owns a stacking context. Negative
            // z-index paints above its background, below both native pseudos and
            // the text/instrument children, including themes using ::before veils.
            position: "absolute", inset: "0", "z-index": "-1", "pointer-events": "none",
            "border-radius": "inherit", display: "block", margin: "0", padding: "0",
            width: "auto", height: "auto", "min-width": "0", "min-height": "0",
            "max-width": "none", "max-height": "none", border: "0", "box-shadow": "none",
          })) layer.style.setProperty(property, value, "important");
          element.prepend(layer);
          entry.layer = layer;
        }
        cardArtworkEdits.set(element, entry);
      }
      const target = entry.layer || element;
      const prefix = `--tessalume-visual-${region}-${mode}`;
      const nativeOverlayCount = entry.foregroundLayers.length;
      for (const [property, value] of Object.entries({
        "background-image": [...entry.foregroundLayers, `var(${state.assetVariable})`].join(","),
        "background-size": layeredValue(nativeOverlayCount, "100% 100%", `var(${prefix}-background-size)`),
        "background-position": layeredValue(nativeOverlayCount, "center", `var(${prefix}-background-position)`),
        "background-repeat": "no-repeat",
        filter: `var(${prefix}-filter)`,
        opacity: `var(${prefix}-opacity)`,
        "mix-blend-mode": `var(${prefix}-blend)`,
        translate: `var(${prefix}-translate)`,
        scale: `${state.baseScale * (state.placement?.geometry?.mirrorX ? -1 : 1)} ${state.baseScale * (state.placement?.geometry?.mirrorY ? -1 : 1)}`,
      })) target.style.setProperty(property, value, "important");
    }
    for (const [element, entry] of cardArtworkEdits) {
      if (!activeElements.has(element)) restoreCardArtworkEdit(element, entry);
    }
  };

// TESSALUME_STANDALONE_ENVELOPE_START
})()
// TESSALUME_STANDALONE_ENVELOPE_END
