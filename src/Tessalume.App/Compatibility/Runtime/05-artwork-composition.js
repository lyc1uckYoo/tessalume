// TESSALUME_RUNTIME_FRAGMENT: absolute artwork placement, projection, and authored motion
// TESSALUME_STANDALONE_ENVELOPE_START
(async () => {
// TESSALUME_STANDALONE_ENVELOPE_END
  const artworkSurface = (region) => {
    if (region === "sidebar") {
      return { element: document.querySelector('[data-tessalume-surface="sidebar"]'), pseudo: "::after" };
    }
    if (region === "chat") {
      return { element: document.querySelector('main[data-tessalume-surface="main"]'), pseudo: "::before" };
    }
    return {
      element: document.querySelector(
        '[data-tessalume-surface="home"]>div:first-child>div:first-child>div:first-child',
      ),
      pseudo: "::before",
    };
  };

  const enumName = (value, fallback) => {
    const candidate = String(value || "").trim().toLowerCase();
    return candidate || fallback;
  };
  const finite = (value, fallback) => Number.isFinite(Number(value)) ? Number(value) : fallback;
  const lengthCss = (length) => {
    const unit = enumName(length?.unit, "auto");
    const value = finite(length?.value, unit === "percent" ? 100 : 1);
    if (unit === "percent") return `${value}%`;
    if (unit === "pixels") return `${value}px`;
    return "auto";
  };
  const positionCss = (position, horizontal) => {
    const kind = enumName(position?.kind, "center");
    const value = finite(position?.value, kind === "percent" ? 50 : 0);
    if (kind === "start") return horizontal ? "left" : "top";
    if (kind === "end") return horizontal ? "right" : "bottom";
    if (kind === "percent") return `${value}%`;
    if (kind === "pixels") return `${value}px`;
    return "center";
  };
  const placementCss = (placement, region = "") => {
    const storedSizeMode = enumName(placement?.sizeMode, "cover");
    const sizeMode = (region === "hero" || region === "chat") &&
      storedSizeMode === "explicit"
      ? "cover"
      : storedSizeMode;
    const size = sizeMode === "contain" || sizeMode === "cover"
      ? sizeMode
      : region === "sidebar"
        ? `${lengthCss(placement?.width)} auto`
        : `${lengthCss(placement?.width)} ${lengthCss(placement?.height)}`;
    return {
      size,
      position: `${positionCss(placement?.positionX, true)} ${positionCss(placement?.positionY, false)}`,
    };
  };
  const resolveLengthPixels = (length, targetLength) => {
    const unit = enumName(length?.unit, "auto");
    const value = finite(length?.value, 0);
    if (unit === "percent") return targetLength * value / 100;
    if (unit === "pixels") return value;
    return null;
  };
  const resolvePositionPixels = (position, targetLength, renderedLength, asOrigin = false) => {
    const kind = enumName(position?.kind, "center");
    const value = finite(position?.value, kind === "percent" ? 50 : 0);
    const basis = asOrigin ? targetLength : targetLength - renderedLength;
    if (kind === "start") return 0;
    if (kind === "end") return basis;
    if (kind === "percent") return basis * value / 100;
    if (kind === "pixels") return value;
    return basis / 2;
  };
  const readImageDimensions = (url) => {
    if (!url) return Promise.resolve(null);
    if (visualImageDimensions.has(url)) return visualImageDimensions.get(url);
    const promise = new Promise((resolve) => {
      const image = new Image();
      image.onload = () => resolve({ width: image.naturalWidth, height: image.naturalHeight });
      image.onerror = () => resolve(null);
      image.src = url;
    });
    visualImageDimensions.set(url, promise);
    return promise;
  };
  const layeredValue = (overlayCount, overlayValue, imageValue) =>
    [...Array(overlayCount).fill(overlayValue), imageValue].join(",");
  const rebuildSlotLayers = (state, surfaceWidth) => {
    const adjustment = state.adjustment || {};
    const clampPercent = (value, fallback = 0) => {
      const number = Number(value);
      return Number.isFinite(number) ? Math.min(100, Math.max(0, number)) : fallback;
    };
    const color = (value) => /^#[0-9a-f]{6}$/i.test(String(value || ""))
      ? String(value).toUpperCase()
      : "#000000";
    const colorAlpha = (hex, opacity) => {
      const value = Number.parseInt(hex.slice(1), 16);
      return `rgba(${(value >> 16) & 255},${(value >> 8) & 255},${value & 255},${opacity})`;
    };
    const overlayColor = color(adjustment.overlayColor);
    const imageLayers = [];
    const maskLayers = [];
    const vignette = clampPercent(adjustment.vignette) / 100;
    if (vignette > 0) {
      imageLayers.push(`radial-gradient(circle at center,transparent 45%,rgba(0,0,0,${Math.min(.78, vignette * .78)}) 100%)`);
    }
    let gradientVeil = adjustment.gradientVeil || {};
    let readabilityVeil = adjustment.readabilityVeil || {};
    for (const variant of adjustment.responsiveVariants || []) {
      const minimum = Number(variant?.minWidth);
      const maximum = Number(variant?.maxWidth);
      if ((Number.isFinite(minimum) && surfaceWidth < minimum) ||
          (Number.isFinite(maximum) && surfaceWidth > maximum)) continue;
      if (variant?.gradientVeil) gradientVeil = variant.gradientVeil;
      if (variant?.readabilityVeil) readabilityVeil = variant.readabilityVeil;
    }
    const legacyGradientStrength = clampPercent(adjustment.gradientStrength) / 100;
    if (gradientVeil?.enabled === true) {
      const strength = clampPercent(gradientVeil.strength, 100) / 100;
      const configured = Array.isArray(gradientVeil.layers)
        ? gradientVeil.layers.slice(0, 8)
        : [];
      for (const layer of configured) {
        const stops = Array.isArray(layer?.stops) ? layer.stops.slice(0, 16) : [];
        if (!stops.length) continue;
        const stopCss = stops.map((stop) =>
          `${colorAlpha(color(stop?.color), Math.min(1, clampPercent(stop?.opacity) / 100 * strength))} ${clampPercent(stop?.position)}%`,
        ).join(",");
        maskLayers.push(`linear-gradient(${finite(layer?.directionDeg, 90)}deg,${stopCss})`);
      }
      if (!configured.length && strength > 0) {
        maskLayers.push(`linear-gradient(90deg,${colorAlpha(overlayColor, Math.min(.82, strength * .82))},transparent 72%)`);
      }
    }
    if (legacyGradientStrength > 0) {
      maskLayers.push(`linear-gradient(90deg,${colorAlpha(overlayColor, Math.min(.82, legacyGradientStrength * .82))},transparent 72%)`);
    }
    if (readabilityVeil?.enabled === true) {
      maskLayers.push(
        `linear-gradient(${finite(readabilityVeil.directionDeg, 90)}deg,` +
        `${colorAlpha(color(readabilityVeil.color), clampPercent(readabilityVeil.opacity) / 100)} ` +
        `${clampPercent(readabilityVeil.rangeStart)}%,transparent ` +
        `${clampPercent(readabilityVeil.rangeEnd, 100)}%)`,
      );
    }
    const overlayOpacity = clampPercent(adjustment.overlayOpacity) / 100;
    let overlayLayer = null;
    if (overlayOpacity > 0) {
      const value = colorAlpha(overlayColor, Math.min(.86, overlayOpacity * .86));
      overlayLayer = `linear-gradient(${value},${value})`;
    }
    if (state.region === "chat") {
      visualSettingsTarget.style.setProperty(
        state.maskVariable,
        maskLayers.length ? maskLayers.join(",") : "none",
      );
      visualSettingVariables.add(state.maskVariable);
    } else {
      imageLayers.push(...maskLayers);
    }
    if (overlayLayer) imageLayers.push(overlayLayer);
    imageLayers.push(`url("${state.imageUrl}")`);
    state.overlayCount = imageLayers.length - 1;
    visualSettingsTarget.style.setProperty(state.assetVariable, imageLayers.join(","));
    visualSettingVariables.add(state.assetVariable);
  };
  const setPlacementVariables = (state, size, position) => {
    const html = visualSettingsTarget;
    const prefix = `--tessalume-visual-${state.region}-${state.mode}`;
    const sizeVariable = `${prefix}-background-size`;
    const positionVariable = `${prefix}-background-position`;
    const repeatVariable = `${prefix}-background-repeat`;
    const geometryVariable = `${prefix}-geometry-transform`;
    html.style.setProperty(sizeVariable, layeredValue(state.overlayCount, "100% 100%", size));
    html.style.setProperty(positionVariable, layeredValue(state.overlayCount, "center", position));
    html.style.setProperty(repeatVariable, layeredValue(state.overlayCount, "no-repeat", "no-repeat"));
    const geometry = state.placement?.geometry || {};
    const mirrorX = geometry.mirrorX === true;
    const mirrorY = geometry.mirrorY === true;
    html.style.setProperty(
      geometryVariable,
      mirrorX || mirrorY ? `scale(${mirrorX ? -1 : 1},${mirrorY ? -1 : 1})` : "none",
    );
    for (const variable of [sizeVariable, positionVariable, repeatVariable, geometryVariable]) {
      visualSettingVariables.add(variable);
    }
  };
  const resolveFoldedPlacement = async (state, target) => {
    const placement = state.placement || {};
    const geometry = placement.geometry || {};
    const scale = Math.min(10, Math.max(.1, finite(geometry.scale, 1)));
    const raw = placementCss(placement, state.region);
    const storedSizeMode = enumName(placement.sizeMode, "cover");
    const sizeMode = (state.region === "hero" || state.region === "chat") &&
      storedSizeMode === "explicit"
      ? "cover"
      : storedSizeMode;
    const fixedWidthSidebar = state.region === "sidebar" && sizeMode === "explicit";
    if (Math.abs(scale - 1) < .000001 && !fixedWidthSidebar) return raw;
    const dimensions = await readImageDimensions(state.imageUrl);
    if (!dimensions || !target?.width || !target?.height) return raw;
    let width;
    let height;
    if (sizeMode === "cover" || sizeMode === "contain") {
      const factor = (sizeMode === "cover" ? Math.max : Math.min)(
        target.width / dimensions.width,
        target.height / dimensions.height,
      );
      width = dimensions.width * factor;
      height = dimensions.height * factor;
    } else {
      width = resolveLengthPixels(placement.width, target.width);
      height = resolveLengthPixels(placement.height, target.height);
      if (width == null && height == null) {
        width = dimensions.width;
        height = dimensions.height;
      } else if (width == null) {
        width = height * dimensions.width / dimensions.height;
      } else if (height == null) {
        height = width * dimensions.height / dimensions.width;
      }
      if (fixedWidthSidebar) {
        width = resolveLengthPixels(placement.width, target.width) ?? dimensions.width;
        height = width * dimensions.height / dimensions.width;
      }
    }
    const left = resolvePositionPixels(placement.positionX, target.width, width);
    const storedHeightUnit = enumName(placement.height?.unit, "auto");
    const storedHeightPercent = finite(placement.height?.value, 0);
    const encodedReferenceHeight = fixedWidthSidebar &&
      storedHeightUnit === "percent" && storedHeightPercent > 0
      ? height * 100 / storedHeightPercent
      : null;
    const verticalBasis = encodedReferenceHeight ?? target.height;
    const top = resolvePositionPixels(placement.positionY, verticalBasis, height);
    const originX = resolvePositionPixels(geometry.originX, target.width, 0, true);
    const originY = resolvePositionPixels(geometry.originY, verticalBasis, 0, true);
    let renderedTop = originY + ((top - originY) * scale);
    if (fixedWidthSidebar && encodedReferenceHeight == null) {
      const widthChanged = !Number.isFinite(state.sidebarReferenceWidth) ||
        Math.abs(state.sidebarReferenceWidth - target.width) > .5;
      if (widthChanged || !Number.isFinite(state.sidebarReferenceTop) ||
          target.height >= finite(state.sidebarReferenceHeight, 0)) {
        state.sidebarReferenceWidth = target.width;
        state.sidebarReferenceHeight = target.height;
        state.sidebarReferenceTop = renderedTop;
      } else {
        renderedTop = state.sidebarReferenceTop;
      }
    }
    return {
      size: `${width * scale}px ${height * scale}px`,
      position: `${originX + ((left - originX) * scale)}px ${renderedTop}px`,
    };
  };
  const synchronizeVisualPlacements = async (revision = visualPlacementRevision) => {
    for (const state of visualSlotStates.values()) {
      if (revision !== visualPlacementRevision || disposed) return false;
      const surface = artworkSurface(state.region).element;
      if (surface && visualSurfaceResizeObserver) visualSurfaceResizeObserver.observe(surface);
      const box = surface?.getBoundingClientRect();
      rebuildSlotLayers(state, box?.width || window.innerWidth);
      const placement = await resolveFoldedPlacement(state, box);
      if (revision !== visualPlacementRevision || disposed) return false;
      setPlacementVariables(state, placement.size, placement.position);
    }
    return true;
  };

  const motionSelector = (region, mode) => {
    const color = mode === "dark" ? ".electron-dark" : ":not(.electron-dark)";
    if (region === "sidebar") {
      return `html.tessalume-theme-active${color} [data-tessalume-surface="sidebar"]::after`;
    }
    if (region === "chat") {
      return `html.tessalume-theme-active${color}.tessalume-is-task ` +
        `main[data-tessalume-surface="main"]::before`;
    }
    return `html.tessalume-theme-active${color}.tessalume-is-home ` +
      `[data-tessalume-surface="home"]>div:first-child>div:first-child>div:first-child::before`;
  };
  const motionLength = (value) => {
    const match = String(value || "").trim().toLowerCase().match(
      /^(-?(?:\d+(?:\.\d+)?|\.\d+))(px|%)$/,
    );
    if (!match) return { value: 0, unit: "px" };
    return { value: finite(match[1], 0), unit: match[2] };
  };
  const combinedMotionLength = (basePixels, deltaToken, factor) => {
    const delta = motionLength(deltaToken);
    const value = delta.value * factor;
    if (delta.unit === "px") return `${basePixels + value}px`;
    if (Math.abs(basePixels) < .000001) return `${value}%`;
    const operator = value < 0 ? "-" : "+";
    return `calc(${basePixels}px ${operator} ${Math.abs(value)}%)`;
  };
  const buildMotionKeyframes = (state, factor, name, properties) => {
    const frames = Array.isArray(state.adjustment?.motion?.keyframes)
      ? state.adjustment.motion.keyframes.slice(0, 16)
      : [];
    const body = frames.map((frame) => {
      const at = Math.min(100, Math.max(0, finite(frame?.at, 0)));
      const scaleDelta = Math.min(1, Math.max(-.9, finite(frame?.scaleDelta, 0)));
      const opacityDelta = Math.min(100, Math.max(-100, finite(frame?.opacityDelta, 0)));
      const scale = Math.max(.01, state.baseScale * (1 + scaleDelta * factor));
      const opacity = Math.min(1, Math.max(0, state.baseOpacity + opacityDelta * factor / 100));
      return `${at}%{${properties.x}:${combinedMotionLength(
        state.baseOffsetX,
        frame?.translateX,
        factor,
      )};${properties.y}:${combinedMotionLength(
        state.baseOffsetY,
        frame?.translateY,
        factor,
      )};${properties.scale}:${scale};${properties.opacity}:${opacity}}`;
    }).join("");
    return `@keyframes ${name}{${body}}`;
  };
  const rebuildVisualMotionStyle = () => {
    const rules = [];
    for (const state of visualSlotStates.values()) {
      const motion = state.adjustment?.motion;
      if (enumName(motion?.mode, "none") !== "loop" ||
          !Array.isArray(motion?.keyframes) || motion.keyframes.length < 2) continue;
      const slot = `${state.region}-${state.mode}`;
      const prefix = `--tessalume-artwork-motion-${slot}`;
      const properties = {
        x: `${prefix}-x`,
        y: `${prefix}-y`,
        scale: `${prefix}-scale`,
        opacity: `${prefix}-opacity`,
      };
      const fullName = `tessalume-artwork-${slot}-full`;
      const reducedName = `tessalume-artwork-${slot}-reduced`;
      const selector = motionSelector(state.region, state.mode);
      const reducedSelector = selector.replace(
        /^html/,
        'html[data-tessalume-motion="reduced"]',
      );
      const offSelector = selector.replace(/^html/, 'html[data-tessalume-motion="off"]');
      const duration = Math.min(300000, Math.max(100, finite(motion.durationMs, 1000)));
      const easing = ["linear", "ease", "ease-in", "ease-out", "ease-in-out"]
        .includes(enumName(motion.easing, "ease-in-out"))
        ? enumName(motion.easing, "ease-in-out")
        : "ease-in-out";
      const direction = ["normal", "reverse", "alternate", "alternate-reverse"]
        .includes(enumName(motion.direction, "alternate"))
        ? enumName(motion.direction, "alternate")
        : "alternate";
      rules.push(
        `@property ${properties.x}{syntax:"<length-percentage>";inherits:false;initial-value:${state.baseOffsetX}px}`,
        `@property ${properties.y}{syntax:"<length-percentage>";inherits:false;initial-value:${state.baseOffsetY}px}`,
        `@property ${properties.scale}{syntax:"<number>";inherits:false;initial-value:${state.baseScale}}`,
        `@property ${properties.opacity}{syntax:"<number>";inherits:false;initial-value:${state.baseOpacity}}`,
        buildMotionKeyframes(state, 1, fullName, properties),
        buildMotionKeyframes(state, .35, reducedName, properties),
        `${selector}{translate:var(${properties.x}) var(${properties.y})!important;` +
          `scale:var(${properties.scale})!important;opacity:var(${properties.opacity})!important;` +
          `animation-name:${fullName}!important;animation-duration:${duration}ms!important;` +
          `animation-timing-function:${easing}!important;animation-direction:${direction}!important;` +
          "animation-iteration-count:infinite!important;animation-fill-mode:both!important;" +
          "animation-play-state:running!important}",
        `${reducedSelector}{animation-name:${reducedName}!important}`,
        `${offSelector}{animation:none!important;translate:${state.baseOffsetX}px ${state.baseOffsetY}px!important;` +
          `scale:${state.baseScale}!important;opacity:${state.baseOpacity}!important}`,
        `@media(prefers-reduced-motion:reduce){${selector}{animation:none!important;` +
          `translate:${state.baseOffsetX}px ${state.baseOffsetY}px!important;` +
          `scale:${state.baseScale}!important;opacity:${state.baseOpacity}!important}}`,
      );
    }
    visualMotionStyle.textContent = rules.join("\n");
  };

// TESSALUME_STANDALONE_ENVELOPE_START
})()
// TESSALUME_STANDALONE_ENVELOPE_END
