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
