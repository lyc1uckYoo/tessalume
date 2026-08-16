// TESSALUME_RUNTIME_FRAGMENT: resolved artwork settings and incremental image updates
// TESSALUME_STANDALONE_ENVELOPE_START
(async () => {
// TESSALUME_STANDALONE_ENVELOPE_END
  const setVisualSettings = async (settings = {}, imageDataUrls = Object.create(null)) => {
    const html = visualSettingsTarget;
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
    const nextSlotImageKeys = new Map();
    for (const mode of ["light", "dark"]) {
      for (const region of ["hero", "sidebar", "chat"]) {
        const key = settings?.[mode]?.[region]?.customImageKey;
        if (typeof key === "string" && key) nextSlotImageKeys.set(`${region}-${mode}`, key);
      }
    }

    // Decode every newly referenced image before changing a slot. A malformed or
    // interrupted delta therefore leaves the previously rendered image set intact.
    const preparedImageUrls = new Map();
    try {
      for (const key of new Set(nextSlotImageKeys.values())) {
        if (customImageObjectUrls.has(key)) continue;
        const dataUrl = imageDataUrls?.[key];
        if (typeof dataUrl !== "string" || !dataUrl) {
          throw new Error(`Custom artwork payload missing for fingerprint: ${key}`);
        }
        const objectUrl = createObjectUrl(dataUrl);
        preparedImageUrls.set(key, objectUrl);
        if (!(await readImageDimensions(objectUrl))) {
          throw new Error(`Custom artwork could not be decoded: ${key}`);
        }
      }
    } catch (error) {
      for (const objectUrl of preparedImageUrls.values()) {
        URL.revokeObjectURL(objectUrl);
        visualImageDimensions.delete(objectUrl);
      }
      preparedImageUrls.clear();
      throw error;
    }
    let preparedImagesCommitted = false;
    try {
    visualSlotStates.clear();
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
          [
            "normal", "multiply", "screen", "overlay", "darken", "lighten",
            "color-dodge", "color-burn", "hard-light", "soft-light", "difference",
            "exclusion", "hue", "saturation", "color", "luminosity", "plus-lighter",
          ],
        );
        const compositionMode = readChoice(
          adjustment.compositionMode,
          "theme",
          ["theme", "legacy", "custom"],
        );
        const filterVariable = `--tessalume-visual-${region}-${mode}-filter`;
        const opacityVariable = `--tessalume-visual-${region}-${mode}-opacity`;
        const translateVariable = `--tessalume-visual-${region}-${mode}-translate`;
        const scaleVariable = `--tessalume-visual-${region}-${mode}-scale`;
        const blendVariable = `--tessalume-visual-${region}-${mode}-blend`;
        const assetVariable = `--tessalume-asset-${region}-${mode}`;
        const themeAssetKey = typeof adjustment.themeAssetKey === "string" && adjustment.themeAssetKey
          ? adjustment.themeAssetKey.replace(/[^a-z0-9_-]/gi, "-")
          : `${region}-${mode}`;
        const sourceAssetVariable = `--tessalume-asset-${themeAssetKey}`;
        const originalAssetUrl = assetAssignments.find(
          ([name]) => name === sourceAssetVariable,
        )?.[1] || null;
        const customImageKey = nextSlotImageKeys.get(`${region}-${mode}`);
        const imageUrl = customImageKey
          ? customImageObjectUrls.get(customImageKey) || preparedImageUrls.get(customImageKey)
          : originalAssetUrl;
        if (imageUrl) {
          const layers = [];
          if (vignette > 0) {
            layers.push(`radial-gradient(circle at center,transparent 45%,rgba(0,0,0,${Math.min(.78, vignette * .78)}) 100%)`);
          }
          let gradientVeil = adjustment.gradientVeil || {};
          let readabilityVeil = adjustment.readabilityVeil || {};
          const surfaceWidth = artworkSurface(region).element?.getBoundingClientRect().width ||
            window.innerWidth;
          for (const variant of adjustment.responsiveVariants || []) {
            const minimum = Number(variant?.minWidth);
            const maximum = Number(variant?.maxWidth);
            if ((Number.isFinite(minimum) && surfaceWidth < minimum) ||
                (Number.isFinite(maximum) && surfaceWidth > maximum)) continue;
            if (variant?.gradientVeil) gradientVeil = variant.gradientVeil;
            if (variant?.readabilityVeil) readabilityVeil = variant.readabilityVeil;
          }
          if (gradientVeil?.enabled === true) {
            const strength = readPercent(gradientVeil.strength, 100, 0, 100) / 100;
            const configuredLayers = Array.isArray(gradientVeil.layers)
              ? gradientVeil.layers.slice(0, 8)
              : [];
            if (configuredLayers.length) {
              for (const layer of configuredLayers) {
                const stops = Array.isArray(layer?.stops) ? layer.stops.slice(0, 16) : [];
                if (!stops.length) continue;
                const stopCss = stops.map((stop) => {
                  const color = readColor(stop?.color);
                  const stopOpacity = readPercent(stop?.opacity, 0, 0, 100) / 100;
                  const position = readPercent(stop?.position, 0, 0, 100);
                  return `${rgba(color, Math.min(1, stopOpacity * strength))} ${position}%`;
                }).join(",");
                layers.push(`linear-gradient(${finite(layer?.directionDeg, 90)}deg,${stopCss})`);
              }
            } else if (strength > 0) {
              // Deterministic schema-five compatibility for the former single veil.
              layers.push(`linear-gradient(90deg,${rgba(overlayColor, Math.min(.82, strength * .82))},transparent 72%)`);
            }
          }
          if (gradientStrength > 0) {
            layers.push(`linear-gradient(90deg,${rgba(overlayColor, Math.min(.82, gradientStrength * .82))},transparent 72%)`);
          }
          if (readabilityVeil?.enabled === true) {
            const color = readColor(readabilityVeil.color);
            const veilOpacity = readPercent(readabilityVeil.opacity, 0, 0, 100) / 100;
            const direction = finite(readabilityVeil.directionDeg, 90);
            const start = readPercent(readabilityVeil.rangeStart, 0, 0, 100);
            const end = readPercent(readabilityVeil.rangeEnd, 100, 0, 100);
            layers.push(
              `linear-gradient(${direction}deg,${rgba(color, veilOpacity)} ${start}%,transparent ${end}%)`,
            );
          }
          if (overlayOpacity > 0) {
            layers.push(`linear-gradient(${rgba(overlayColor, Math.min(.86, overlayOpacity * .86))},${rgba(overlayColor, Math.min(.86, overlayOpacity * .86))})`);
          }
          layers.push(`url("${imageUrl}")`);
          html.style.setProperty(assetVariable, layers.join(","));
          visualSettingVariables.add(assetVariable);
          const state = {
            region,
            mode,
            placement: adjustment.placement || {
              sizeMode: "Cover",
              positionX: { kind: "Center" },
              positionY: { kind: "Center" },
              geometry: { scale: 1 },
            },
            imageUrl,
            overlayCount: layers.length - 1,
            adjustment,
            assetVariable,
            baseOffsetX: compositionMode === "legacy" ? offsetX : 0,
            baseOffsetY: compositionMode === "legacy" ? offsetY : 0,
            baseScale: compositionMode === "legacy" ? zoom : 1,
            baseOpacity: opacity,
          };
          visualSlotStates.set(`${region}-${mode}`, state);
          const rawPlacement = placementCss(state.placement, state.region);
          setPlacementVariables(state, rawPlacement.size, rawPlacement.position);
        } else {
          // An absent key explicitly means "use the theme original". Removing the
          // prior variable also handles packages that do not define this slot.
          html.style.removeProperty(assetVariable);
        }
        html.style.setProperty(
          filterVariable,
          `brightness(${brightness}) contrast(${contrast}) saturate(${saturation}) grayscale(${grayscale}) hue-rotate(${hueRotation}deg) blur(${blur}px)`,
        );
        html.style.setProperty(opacityVariable, String(opacity));
        html.style.setProperty(
          translateVariable,
          compositionMode === "legacy" ? `${offsetX}px ${offsetY}px` : "0px 0px",
        );
        html.style.setProperty(scaleVariable, compositionMode === "legacy" ? String(zoom) : "1");
        html.style.setProperty(blendVariable, blendMode);
        visualSettingVariables.add(filterVariable);
        visualSettingVariables.add(opacityVariable);
        visualSettingVariables.add(translateVariable);
        visualSettingVariables.add(scaleVariable);
        visualSettingVariables.add(blendVariable);
        if (adjustment.readabilityProtection === true ||
            adjustment.readabilityVeil?.enabled === true) readability.push(`${region}-${mode}`);
      }
    }
    html.dataset.tessalumeReadability = readability.join(" ");
    html.dataset.tessalumeVisualPlacement = [
      "hero-light", "hero-dark", "sidebar-light", "sidebar-dark", "chat-light", "chat-dark",
    ].join(" ");
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
    rebuildVisualMotionStyle();
    if (appearanceCommitted) syncDisplayPreferences();
    for (const [key, objectUrl] of preparedImageUrls) customImageObjectUrls.set(key, objectUrl);
    customSlotImageKeys.clear();
    for (const [slot, key] of nextSlotImageKeys) customSlotImageKeys.set(slot, key);
    const activeImageKeys = new Set(customSlotImageKeys.values());
    for (const [key, objectUrl] of customImageObjectUrls) {
      if (activeImageKeys.has(key)) continue;
      URL.revokeObjectURL(objectUrl);
      visualImageDimensions.delete(objectUrl);
      customImageObjectUrls.delete(key);
    }
    preparedImagesCommitted = true;
    visualPlacementRevision += 1;
    await synchronizeVisualPlacements(visualPlacementRevision);
    return true;
    } finally {
      if (!preparedImagesCommitted) {
        for (const objectUrl of preparedImageUrls.values()) {
          URL.revokeObjectURL(objectUrl);
          visualImageDimensions.delete(objectUrl);
        }
      }
    }
  };

// TESSALUME_STANDALONE_ENVELOPE_START
})()
// TESSALUME_STANDALONE_ENVELOPE_END
