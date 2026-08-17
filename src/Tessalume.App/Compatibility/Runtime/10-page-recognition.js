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
    let taskTitleWidthManaged = false;
    let taskTitleWidthPrevious = "";
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
    const findMain = () => queryFirst(
      document,
      "main",
      [
        'main[data-app-shell-main-surface="default"]',
        'main[class*="MainContentSurface"]',
        "main.main-surface",
        "main",
      ],
    );
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
      let stickyContainer = surface.parentElement;
      while (stickyContainer && stickyContainer !== document.body) {
        const stickyStyle = getComputedStyle(stickyContainer);
        const stickyBottom = parseFloat(stickyStyle.bottom);
        if (stickyStyle.position === "sticky" &&
            Number.isFinite(stickyBottom) &&
            Math.abs(stickyBottom) < .5) {
          break;
        }
        stickyContainer = stickyContainer.parentElement;
      }
      if (stickyContainer && stickyContainer !== document.body) {
        const stickyBox = stickyContainer.getBoundingClientRect();
        const nativeFade = Array.from(stickyContainer.querySelectorAll("*"))
          .find((node) => {
            const style = getComputedStyle(node);
            if (style.pointerEvents !== "none" || style.position !== "absolute") return false;
            const className = typeof node.className === "string" ? node.className : "";
            if (!style.backgroundImage.includes("linear-gradient") &&
                !className.includes("bg-gradient")) return false;
            const box = node.getBoundingClientRect();
            return box.width >= stickyBox.width * .8 &&
              box.height >= surface.getBoundingClientRect().height &&
              Math.abs(box.bottom - stickyBox.bottom) < 2;
          });
        mark(nativeFade, "tessalume-composer-native-fade");
      }
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
      const configuredSurface = queryAll(
        main,
        "settingsSurface",
        [".main-surface.flex.h-full.min-h-0.flex-col"],
      ).find((surface) => queryFirst(
        surface,
        "settingsScrollChild",
        [":scope > .scrollbar-stable.flex-1.overflow-y-auto.p-panel"],
      ));
      if (configuredSurface) return configuredSurface;

      // Codex 26.810 removed the legacy main-surface token from the settings
      // carrier but retained its dedicated panel scroll viewport. Resolve the
      // carrier from that stable child instead of binding route recognition to
      // utility classes on the replaceable outer element.
      const settingsScrollChild = queryFirst(
        main,
        "settingsScrollChild",
        [".scrollbar-stable.flex-1.overflow-y-auto.p-panel"],
      );
      return settingsScrollChild?.parentElement || null;
    };
    const syncRouteState = () => {
      const home = findHome();
      const isHome = Boolean(home);
      const settingsSurface = findSettingsSurface();
      mark(settingsSurface, roleClass("settings-surface"));
      markSurface(settingsSurface, "settings");
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
