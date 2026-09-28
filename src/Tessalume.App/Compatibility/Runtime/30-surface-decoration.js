// TESSALUME_RUNTIME_FRAGMENT: sidebar, message, task, output, and shared surface decoration
    let observedSidebar = null;
    let sidebarObserver = null;
    let sidebarNeedsDecoration = true;
    const decorateSidebar = (aside) => {
      const sidebar = spec.sidebar;
      if (!aside || !sidebar) return;
      if (aside !== observedSidebar) {
        sidebarObserver?.disconnect();
        observedSidebar = aside;
        sidebarNeedsDecoration = true;
        sidebarObserver = new MutationObserver(() => {
          sidebarNeedsDecoration = true;
          schedule();
        });
        sidebarObserver.observe(aside, { childList:true, characterData:true, subtree:true });
      }
      if (!sidebarNeedsDecoration) return;
      sidebarNeedsDecoration = false;
      const palette = sidebar.palette || [];
      const projectTone = sidebar.projectTone || "phase";
      const threadIndex = sidebar.threadIndex || "thread";
      const threadTone = sidebar.threadTone || "";
      const projectRows = '[data-app-action-sidebar-project-row]';
      const threadRows = '[data-app-action-sidebar-thread-row]';

      aside.querySelectorAll('[data-sidebar-project-drop-zone="project-icon"]').forEach((icon, index) => {
        const row = icon.closest(projectRows);
        if (!row) return;
        // The current sidebar gives the icon its own fixed-width slot. Theme
        // heading padding and badges belong to the branch containing both the
        // icon and title, leaving the native action buttons in their own lane.
        const title = (row.getAttribute("aria-labelledby") || "").split(/\s+/)
          .map((id) => document.getElementById(id))
          .find((node) => node && row.contains(node)) || row.querySelector('[data-marquee-text]');
        let heading = icon;
        while (heading.parentElement && heading.parentElement !== row) heading = heading.parentElement;
        if (heading === icon || !title || !heading.contains(title)) heading = row;
        const headingClass = roleClass("project-heading");
        for (const previous of [row, ...row.querySelectorAll(`.${headingClass}`)]) {
          if (previous !== heading) previous.classList.remove(headingClass);
        }
        mark(heading, headingClass);
        setData(heading, "index", String(index + 1).padStart(2, "0"));
        setData(row, "index", String(index + 1).padStart(2, "0"));
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
      if (content.querySelector('table,[role="table"],pre,.katex-display') &&
          content.getAttribute("data-tessalume-wide-content") !== "true") {
        const wideContentAttribute = "data-tessalume-wide-content";
        const previousWideContent = content.getAttribute(wideContentAttribute);
        content.setAttribute(wideContentAttribute, "true");
        surfaced.push([content, previousWideContent, wideContentAttribute]);
      }
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
      const activeHeaders = new Set();
      const activeTitles = new Set();
      queryAll(
        document,
        "taskHeader",
        ['[data-testid="app-shell-header-context-menu-surface"]'],
      ).forEach((header) => {
        // A unified file/chat tab strip reuses the old task-header element.
        // Never apply content-sized title geometry to the native tab carrier.
        if (!html.classList.contains("tessalume-is-task") || !isVisibleSurface(header) ||
            header.closest('[data-app-shell-tab-row="true"]') ||
            header.querySelector('[role="tab"],[role="tablist"],[data-app-shell-tab-row="true"]')) return;
        const currentTitle = header.querySelector('[data-app-shell-titlebar-content="true"]');
        const titleButton = header.querySelector("span > button.truncate");
        const title = titleButton?.parentElement?.parentElement ||
          header.querySelector("span > span.truncate")?.parentElement;
        const secondaryTitle = header.querySelector("span > span.truncate")?.parentElement;
        mark(header, roleClass("task-header"));
        markSurface(header, "task-header");
        activeHeaders.add(header);
        for (const candidate of new Set([currentTitle, title, secondaryTitle])) {
          if (!candidate || !isVisibleSurface(candidate)) continue;
          mark(candidate, roleClass("task-title"));
          markSurface(candidate, "task-title");
          activeTitles.add(candidate);
        }
      });
      for (const [surface, active] of [["task-header", activeHeaders], ["task-title", activeTitles]]) {
        document.querySelectorAll(`.${roleClass(surface)},[data-tessalume-surface="${surface}"]`).forEach((node) => {
          if (active.has(node)) return;
          node.classList.remove(roleClass(surface));
          if (node.getAttribute("data-tessalume-surface") === surface)
            node.removeAttribute("data-tessalume-surface");
        });
      }
    };
    const decorateOutputPanels = () => {
      const sections = new Set();
      const panels = new Set();
      const collectSection = (node) => {
        const section = node?.closest?.("section");
        if (section?.isConnected) sections.add(section);
      };

      // Current Codex builds render a data-dependent set of summary sections.
      // Git/worktree rows use item-trigger rather than item-button, while plan,
      // usage, and other sections may contain neither slot. Bind the summary
      // container itself so every present section receives the same theme.
      document.querySelectorAll('[data-summary-panel-variant="summary"]').forEach((root) => {
        const panel = root.firstElementChild;
        if (!panel?.isConnected) return;
        panels.add(panel);
        panel.querySelectorAll(':scope > div > section').forEach((section) => sections.add(section));
      });

      // Keep compatibility with older Codex builds that predate the summary
      // variant attribute.
      const panelItems = queryAll(
        document,
        "outputPanelItem",
        ['[data-slot="thread-summary-panel-item-button"]'],
      );
      panelItems.forEach(collectSection);

      const legacyLabels = new Set(["\u8f93\u51fa", "\u6765\u6e90", "Output", "Sources"]);
      if (panelItems.length === 0) {
        document.querySelectorAll("button").forEach((button) => {
          if (legacyLabels.has(button.textContent?.trim() || "")) collectSection(button);
        });
      }

      sections.forEach((section) => {
        const panel = section.parentElement?.parentElement;
        if (panel?.isConnected) panels.add(panel);
        mark(section, roleClass("output-section"));
        mark(section.querySelector("header"), roleClass("output-header"));
        markSurface(section, "output-section");
        markSurface(section.querySelector("header"), "output-header");
      });
      panels.forEach((panel) => {
        mark(panel, roleClass("output-panel"));
        markSurface(panel, "output-panel");
      });
    };
    const decorateComposerProgressFade = () => {
      const composer = findComposerSurface();
      if (!composer) return;
      markSurface(composer, "composer");
      const composerRoot = composer.closest('[data-codex-composer-root]');
      const progress = composerRoot?.querySelector(
        '[data-in-progress-fixed-content="true"]',
      );
      const fade = progress?.firstElementChild?.firstElementChild;
      if (fade) fade.setAttribute("data-tessalume-progress-fade", "true");
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
      // The fixed progress row adds its own surface-colored fade behind the
      // status badge. Mark that full-width layer immediately so it cannot paint
      // a dark rectangle over theme chat artwork.
      decorateComposerProgressFade();
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
      decorateComposerProgressFade();

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
        return box.width > 120 && box.height > 80 &&
          box.right > 0 && box.left < window.innerWidth &&
          box.bottom > 0 && box.top < window.innerHeight;
      });
      html.classList.toggle(roleClass("has-output"), outputOpen);
      html.classList.toggle("tessalume-has-output", outputOpen);

      document.querySelectorAll(`[${dataName("card")}]`).forEach((node) => node.removeAttribute(dataName("card")));
      queryFirst(home, "homeSuggestions", [".group\\/home-suggestions"])
        ?.querySelectorAll("button").forEach((button, index) => {
        setData(button, "card", String(index + 1).padStart(2, "0"));
      });
      decorateTaskHeaders();
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
