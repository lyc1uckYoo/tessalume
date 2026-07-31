registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");

    // 模板 1.0 固定结构①：首页主视觉。
    // 可以替换文案、符号和内部动效节点，但不要改变 data-theme-role / data-theme-part。
    root.innerHTML = `
      <div class="dny-stage" data-theme-stage>
        <section class="dny-hero-copy" data-theme-role="hero" data-theme-part="hero-copy">
          <span class="dny-hero-kicker" data-theme-part="hero-kicker">
            <i></i>
            <span class="dny-light-only">${config.kickerLight}</span>
            <span class="dny-dark-only">${config.kickerDark}</span>
          </span>
          <h1 class="dny-light-only" data-theme-part="hero-title-light">${config.headingLight}<br><em>${config.headingLightAccent}</em></h1>
          <h1 class="dny-dark-only" data-theme-part="hero-title-dark">${config.headingDark}<br><em>${config.headingDarkAccent}</em></h1>
          <p>${config.subtitle}</p>
          <div class="dny-hero-motion" data-theme-part="hero-motion" aria-label="${config.motionLabel}">
            ${Array.from({length:5},(_,i)=>`<i style="--i:${i}"></i>`).join("")}<b></b>
          </div>
          <div class="dny-hero-note" data-theme-part="hero-note">
            <small>${config.noteLabel}</small>
            <strong class="dny-light-only">${config.noteLight}</strong>
            <strong class="dny-dark-only">${config.noteDark}</strong>
          </div>
        </section>

        <!-- 模板 1.0 固定结构②：顶部身份牌。 -->
        <div class="dny-identity" data-theme-role="identity" data-theme-part="identity">
          <span data-theme-part="identity-emblem"></span>
          <div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div>
          <i data-theme-part="identity-status"></i>
        </div>

        <!-- 模板 1.0 固定结构③：左侧主卡、右侧双卡和左侧记忆卡。 -->
        <aside class="dny-task-card dny-task-card-left" data-theme-role="task-left" data-theme-part="task-card-left">
          <i data-theme-part="task-card-art"></i>
          <div data-theme-part="task-card-caption"><b>${config.leftCardTitle}</b><small>${config.leftCardMeta}</small></div>
        </aside>
        <aside class="dny-task-card dny-task-card-right dny-task-card-secondary" data-theme-role="task-right" data-theme-priority="secondary" data-theme-part="task-card-right-secondary">
          <i data-theme-part="task-card-art"></i>
          <div data-theme-part="task-card-caption"><b>${config.secondaryCardTitle}</b><small>${config.secondaryCardMeta}</small></div>
        </aside>
        <aside class="dny-task-card dny-task-card-right dny-task-card-primary" data-theme-role="task-right" data-theme-priority="primary" data-theme-part="task-card-right-primary">
          <i data-theme-part="task-card-art"></i>
          <div data-theme-part="task-card-caption"><b>${config.primaryCardTitle}</b><small>${config.primaryCardMeta}</small></div>
        </aside>
        <aside class="dny-memory" data-theme-role="memory" data-theme-part="memory-card">
          <small>${config.memoryLabel}</small>
          <p>${config.memory}</p>
          <span data-theme-part="memory-meter">${Array.from({length:7},(_,i)=>`<i style="--i:${i}"></i>`).join("")}</span>
        </aside>
      </div>`;

    // 模板 1.0 固定结构④：同步面板和输入框挂件必须直属主题根节点。
    // 这样公共运行时可以稳定定位，并在空间不足时与对应卡片同步隐藏。
    root.insertAdjacentHTML("beforeend", `
      <div class="dny-sync-panel" data-theme-role="sync-panel" data-theme-priority="secondary" data-theme-part="sync-panel">
        <span class="dny-sync-copy" data-theme-part="sync-copy">
          <small>${config.syncLabel}</small>
          <b>${config.syncTitle} <strong>${config.syncValue}</strong><em>/${config.syncTotal}</em></b>
        </span>
        <span class="dny-sync-core" data-theme-part="sync-core"><i></i><b>${config.syncCore}</b><small>${config.syncCoreLabel}</small></span>
        <span class="dny-sync-meter" data-theme-part="sync-meter">${Array.from({length:9},(_,i)=>`<i style="--i:${i};--h:${8 + (i % 5) * 3}px"></i>`).join("")}</span>
        <span class="dny-sync-state" data-theme-part="sync-state"><small>${config.syncStateLabel}</small><b><i></i>${config.syncState}</b></span>
      </div>
      <div class="dny-composer-accessory" data-theme-role="composer-accessory" data-theme-part="composer-accessory">
        <i></i><b>${config.accessorySymbol}</b><small>${config.accessoryLabel}</small>
      </div>`);

    return context.mountCanonicalTheme({
      namespace: "dny",
      themeClass: "dny-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["bubble", "pearl", "orbit", "void"],
        projectTone: "domain",
        threadIndex: "trace",
        sections: { "置顶": config.pinnedSection, "项目": config.projectSection },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".dny-composer-accessory");
        positionPanelAboveCards(
          main,
          ".dny-sync-panel",
          [".dny-task-card-secondary", ".dny-task-card-primary"],
          320,
          56,
          40,
        );
      },
    });
  },
  async unmount() {}
});
