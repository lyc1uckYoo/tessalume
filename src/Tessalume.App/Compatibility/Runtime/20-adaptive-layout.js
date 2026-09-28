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
    const syncTaskTitleWidth = () => {
      if (templateVersion !== "1.0") return;
      const primary = Array.from(document.querySelectorAll(
        '[data-tessalume-surface="task-title"]',
      )).find((node) => node.matches('[data-app-shell-titlebar-content="true"]') ||
        node.querySelector("button.truncate"));
      if (!primary?.isConnected) return;

      const primaryBox = primary.getBoundingClientRect();
      if (!(primaryBox.width > 0 && primaryBox.height > 0)) return;

      const header = primary.closest('[data-tessalume-surface="task-header"]');
      const identity = root.querySelector('[data-theme-role="identity"]');
      const secondary = Array.from(header?.querySelectorAll(
        '[data-tessalume-surface="task-title"]',
      ) || []).find((node) => node !== primary &&
        !node.matches('[data-app-shell-titlebar-content="true"]') &&
        !node.querySelector("button.truncate"));
      const boundaries = [identity, secondary]
        .map((node) => node?.getBoundingClientRect())
        .filter((box) => box && box.width > 0 && box.height > 0 && box.left > primaryBox.left + 120)
        .map((box) => box.left);
      const boundary = boundaries.length
        ? Math.min(...boundaries)
        : window.innerWidth - 24;
      const available = Math.max(120, Math.floor(boundary - primaryBox.left - 14));
      const variable = "--tessalume-task-title-primary-width";
      if (!taskTitleWidthManaged) {
        taskTitleWidthManaged = true;
        taskTitleWidthPrevious = html.style.getPropertyValue(variable);
      }
      html.style.setProperty(variable, `${available}px`);
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
      const summaryPanel = Array.from(document.querySelectorAll(
        '[data-summary-panel-variant="summary"] [data-tessalume-surface="output-panel"]',
      )).find((node) => node.getBoundingClientRect().width > 0);
      const summaryBox = summaryPanel?.getBoundingClientRect();
      const stageBox = stage.getBoundingClientRect();
      const navigation = document.querySelector('[data-thread-user-message-navigation-rail-list="true"]');
      const navigationBox = navigation?.getBoundingClientRect();
      const leftInset = navigationBox?.width > 0 && navigationBox.height > 0
        ? Math.max(4, Math.ceil(navigationBox.right - stageBox.left + 8))
        : 4;
      const insetValue = `${leftInset}px`;
      if (root.style.getPropertyValue("--tessalume-left-rail-inset") !== insetValue) {
        root.style.setProperty("--tessalume-left-rail-inset", insetValue);
      }
      const summaryOverlapsRightRail = !!summaryBox && rightRoles.some((node) => {
        const style = window.getComputedStyle(node);
        const width = Number.parseFloat(style.width);
        const height = Number.parseFloat(style.height);
        const right = Number.parseFloat(style.right);
        const bottom = Number.parseFloat(style.bottom);
        if (![width, height, right, bottom].every(Number.isFinite)) return false;
        const anchor = style.position === "fixed"
          ? { right: window.innerWidth, bottom: window.innerHeight }
          : stageBox;
        const railLeft = anchor.right - right - width;
        const railTop = anchor.bottom - bottom - height;
        return summaryBox.left < anchor.right - right &&
          summaryBox.right > railLeft &&
          summaryBox.top < railTop + height &&
          summaryBox.bottom > railTop - 10;
      });

      const previousLeft = root.getAttribute("data-tessalume-left-rail");
      const previousRight = root.getAttribute("data-tessalume-right-rail");
      const previousAccessory = root.getAttribute("data-tessalume-composer-accessory");

      const leftFits = !reviewOpen &&
        leftGutter >= Math.max(previousLeft === "full" ? 164 : 180, leftInset + 146 + 14) &&
        workspaceHeight >= (previousLeft === "full" ? 680 : 720);

      let rightRail = "none";
      if (!reviewOpen && !summaryOverlapsRightRail) {
        // Cards retain the summary-open footprint in both summary states.
        const fullThreshold = 352;
        const singleThreshold = 184;
        const fullFits =
          rightGutter >= (previousRight === "full" ? fullThreshold - 16 : fullThreshold) &&
          workspaceHeight >= (previousRight === "full" ? 680 : 720);
        const singleFits =
          rightGutter >= (previousRight === "single" || previousRight === "full"
            ? singleThreshold - 16 : singleThreshold) &&
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
        setAutoHidden(node, hidden, summaryOverlapsRightRail ? "summary-panel" : "right-rail");
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
      const summaryPanel = document.querySelector(
        '[data-summary-panel-variant="summary"] [data-tessalume-surface="output-panel"]',
      );
      const messageNavigation = document.querySelector('[data-thread-user-message-navigation-rail-list="true"]');

      syncLayoutObservers(adaptiveLayout
        ? [main, workspace, composer, summaryPanel, messageNavigation]
        : [main]);
      syncStageGeometry(main, stage);
      if (decorate) decorateSharedSurfaces(main, aside, home);
      syncTaskTitleWidth();
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
