registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");

    // 模板 1.0 固定结构①：首页主视觉。
    // 可以替换文案、符号和内部动效节点，但不要改变 data-theme-role / data-theme-part。
    context.renderTemplateV1({
        stageClass: "example-stage",
        hero: { tag: "section", className: "example-hero-copy", html: `<span class="example-hero-kicker" data-theme-part="hero-kicker">
            <i></i>
            <span class="example-light-only">${config.kickerLight}</span>
            <span class="example-dark-only">${config.kickerDark}</span>
          </span>
          <h1 class="example-light-only" data-theme-part="hero-title-light">${config.headingLight}<br><em>${config.headingLightAccent}</em></h1>
          <h1 class="example-dark-only" data-theme-part="hero-title-dark">${config.headingDark}<br><em>${config.headingDarkAccent}</em></h1>
          <p>${config.subtitle}</p>
          <div class="example-hero-motion" data-theme-part="hero-motion" data-theme-draft="replace-home-motion" aria-label="${config.motionLabel}">
            ${Array.from({length:5},(_,i)=>`<i style="--i:${i}"></i>`).join("")}<b></b>
          </div>
          <div class="example-hero-note" data-theme-part="hero-note">
            <small>${config.noteLabel}</small>
            <strong class="example-light-only">${config.noteLight}</strong>
            <strong class="example-dark-only">${config.noteDark}</strong>
          </div>` },
        identity: { tag: "div", className: "example-identity", html: `<span data-theme-part="identity-emblem"></span>
          <div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div>
          <i data-theme-part="identity-status"></i>` },
        taskLeft: { tag: "aside", className: "example-task-card example-task-card-left", html: `<i data-theme-part="task-card-art"></i>
          <div data-theme-part="task-card-caption"><b>${config.leftCardTitle}</b><small>${config.leftCardMeta}</small></div>` },
        taskSecondary: { tag: "aside", className: "example-task-card example-task-card-right example-task-card-secondary", html: `<i data-theme-part="task-card-art"></i>
          <div data-theme-part="task-card-caption"><b>${config.secondaryCardTitle}</b><small>${config.secondaryCardMeta}</small></div>` },
        taskPrimary: { tag: "aside", className: "example-task-card example-task-card-right example-task-card-primary", html: `<i data-theme-part="task-card-art"></i>
          <div data-theme-part="task-card-caption"><b>${config.primaryCardTitle}</b><small>${config.primaryCardMeta}</small></div>` },
        memory: { tag: "aside", className: "example-memory", html: `<small>${config.memoryLabel}</small>
          <p>${config.memory}</p>
          <span data-theme-part="memory-meter" data-theme-draft="replace-memory-instrument">${Array.from({length:7},(_,i)=>`<i style="--i:${i}"></i>`).join("")}</span>` },
        syncPanel: { tag: "div", className: "example-sync-panel", html: `<span class="example-sync-copy" data-theme-part="sync-copy">
          <small>${config.syncLabel}</small>
          <b>${config.syncTitle} <strong>${config.syncValue}</strong><em>/${config.syncTotal}</em></b>
        </span>
        <span class="example-sync-core" data-theme-part="sync-core" data-theme-draft="replace-sync-instrument"><i></i><b>${config.syncCore}</b><small>${config.syncCoreLabel}</small></span>
        <span class="example-sync-meter" data-theme-part="sync-meter">${Array.from({length:9},(_,i)=>`<i style="--i:${i};--h:${8 + (i % 5) * 3}px"></i>`).join("")}</span>
        <span class="example-sync-state" data-theme-part="sync-state"><small>${config.syncStateLabel}</small><b><i></i>${config.syncState}</b></span>` },
        composerAccessory: { tag: "div", className: "example-composer-accessory", html: `<i data-theme-draft="replace-character-accessory"></i><b>${config.accessorySymbol}</b><small>${config.accessoryLabel}</small>` },
      });

    return context.mountCanonicalTheme({
      namespace: "example",
      themeClass: "example-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["one", "two", "three", "four"],
        projectTone: "phase",
        threadIndex: "thread",
        sections: { "置顶": config.pinnedSection, "项目": config.projectSection },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".example-composer-accessory");
        positionPanelAboveCards(
          main,
          ".example-sync-panel",
          [".example-task-card-secondary", ".example-task-card-primary"],
          320,
          56,
          40,
        );
      },
    });
  },
  async unmount() {}
});
