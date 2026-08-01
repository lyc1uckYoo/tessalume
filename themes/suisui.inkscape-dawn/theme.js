registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");
    context.renderTemplateV1({
        stageClass: "sui-stage",
        hero: { tag: "section", className: "sui-hero-copy", html: `<span class="sui-kicker" data-theme-part="hero-kicker"><i></i><span class="sui-light-only">朝晖开卷 · ZHAOMING ARCHIVE</span><span class="sui-dark-only">月映山河 · SHANHE NIGHT</span></span>
          <h1 class="sui-light-only" data-theme-part="hero-title-light">扇开千里<br><em>朝光落山河</em></h1>
          <h1 class="sui-dark-only" data-theme-part="hero-title-dark">月沉黛岭<br><em>水境照云天</em></h1>
          <p>${config.subtitle}</p>
          <div class="sui-river" data-theme-part="hero-motion" aria-label="朝明开扇卷与月映重明境">
            <span class="sui-river-form sui-river-form-light sui-light-only" data-sui-home-fx="dawn-fan-scroll-v2"><i></i><i></i><i></i><i></i><i></i><b></b><em></em></span>
            <span class="sui-river-form sui-river-form-dark sui-dark-only" data-sui-home-fx="moonlit-chongming-v2"><i></i><i></i><i></i><i></i><i></i><b></b><em></em></span>
          </div>
          <div class="sui-verse" data-theme-part="hero-note"><small>山河水境</small><strong class="sui-light-only">霞铺万顷欲流金</strong><strong class="sui-dark-only">一桁秋山共我吟</strong></div>` },
        identity: { tag: "div", className: "sui-identity", html: `<span data-theme-part="identity-emblem"></span>
          <div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div>
          <i data-theme-part="identity-status"></i>` },
        taskLeft: { tag: "aside", className: "sui-task-card sui-task-card-left", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>仙游碧落</b><small>FLYING IMMORTAL / 01</small></div>` },
        taskSecondary: { tag: "aside", className: "sui-task-card sui-task-card-right sui-task-card-human", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>穗穗 · 水扇</b><small>SHANHE REALM / SUISUI</small></div>` },
        taskPrimary: { tag: "aside", className: "sui-task-card sui-task-card-right sui-task-card-bird", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>重明 · 朝晖</b><small>DOUBLE SIGHT / CHONGMING</small></div>` },
        memory: { tag: "aside", className: "sui-memory", html: `<small>昭明案牍 · WATER FAN MEMORY</small>
          <p>${config.memory}</p>
          <span class="sui-memory-realm" data-theme-part="memory-meter">
            <svg viewBox="0 0 122 34" aria-hidden="true">
              <g class="sui-memory-mountains" fill="none" stroke-linecap="round" stroke-linejoin="round"><path d="M2 24 20 12l10 9L44 5l18 20 14-13 18 14"/><path d="M17 26c17-7 27 5 43 0 15-5 25 2 38 1 9-1 14-5 22-9"/></g>
              <g class="sui-memory-fan"><path d="m92 27-13-15q13-11 34-3Z"/><path d="m92 27-13-15m13 15-5-20m5 20 4-22m-4 22 13-20m-13 20 21-18" fill="none"/></g>
              <circle class="sui-memory-dew" cx="8" cy="25" r="2.4"/>
            </svg>
          </span>` },
        syncPanel: { tag: "div", className: "sui-shanhe-sync", html: `<span class="sui-sync-copy" data-theme-part="sync-copy"><small>栖霞 · SHANHE SCROLL</small><b>山河入画 <strong>千里</strong><em> / FLOW</em></b></span>
          <span class="sui-sync-core" data-theme-part="sync-core" data-sui-sync-fx="shanhe-fan-v2" aria-hidden="true"><i></i><i></i><i></i><b></b><em></em></span>
          <span class="sui-mist" data-theme-part="sync-meter" aria-hidden="true">${Array.from({length:9},(_,i)=>`<i style="--i:${i};--h:${7 + (i % 5) * 4}px"></i>`).join("")}</span>
          <span class="sui-sync-state" data-theme-part="sync-state"><small>ZHAOMING</small><b><i></i>入画</b></span>` },
        composerAccessory: { tag: "div", className: "sui-seal", html: `<svg class="sui-dew-fan" viewBox="0 0 100 100" aria-hidden="true">
          <defs>
            <linearGradient id="sui-fan-jade" x1="0" y1="1" x2="1" y2="0"><stop stop-color="var(--sui-blue)"/><stop offset=".42" stop-color="var(--sui-jade)"/><stop offset=".78" stop-color="var(--sui-jade-bright)"/><stop offset="1" stop-color="#f7fff1"/></linearGradient>
            <linearGradient id="sui-fan-gold" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#9d7330"/><stop offset=".55" stop-color="var(--sui-gold)"/><stop offset="1" stop-color="#fff0af"/></linearGradient>
            <radialGradient id="sui-dew-drop" cx=".35" cy=".25"><stop stop-color="#fff"/><stop offset=".28" stop-color="var(--sui-jade-bright)"/><stop offset="1" stop-color="var(--sui-blue)"/></radialGradient>
            <filter id="sui-fan-glow"><feGaussianBlur stdDeviation="1.25" result="blur"/><feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
          </defs>
          <g class="sui-fan-water" fill="none" stroke-linecap="round"><path d="M8 72C24 63 34 68 48 75C62 82 74 82 92 70" stroke="var(--sui-jade)" stroke-width="1.2"/><path d="M15 80C31 73 41 77 54 82C67 87 78 84 88 78" stroke="var(--sui-gold)" stroke-width=".9"/></g>
          <g class="sui-fan-leaves" filter="url(#sui-fan-glow)">
            <path class="sui-fan-leaf" style="--i:0" d="M49 72L13 57Q18 38 31 25Z" fill="url(#sui-fan-jade)" opacity=".78"/>
            <path class="sui-fan-leaf" style="--i:1" d="M49 72L31 25Q39 17 49 15Z" fill="url(#sui-fan-jade)" opacity=".86"/>
            <path class="sui-fan-leaf" style="--i:2" d="M49 72V15Q60 16 68 22Z" fill="url(#sui-fan-jade)" opacity=".94"/>
            <path class="sui-fan-leaf" style="--i:3" d="M49 72L68 22Q78 29 83 41Z" fill="url(#sui-fan-jade)" opacity=".86"/>
            <path class="sui-fan-leaf" style="--i:4" d="M49 72L83 41Q88 50 87 59Z" fill="url(#sui-fan-jade)" opacity=".78"/>
            <path class="sui-fan-paint" d="M21 47C34 36 48 33 62 37C72 40 78 46 83 52M29 55C43 46 59 46 75 54" fill="none" stroke="#f7fff1" stroke-width="1.4" stroke-linecap="round" opacity=".72"/>
          </g>
          <g class="sui-fan-ribs" fill="none" stroke="url(#sui-fan-gold)" stroke-width="1.7" stroke-linecap="round"><path d="M49 72L13 57"/><path d="M49 72L31 25"/><path d="M49 72V15"/><path d="M49 72L68 22"/><path d="M49 72L83 41"/><path d="M49 72L87 59"/><path d="M13 57Q18 38 31 25Q39 17 49 15Q60 16 68 22Q78 29 83 41Q88 50 87 59"/></g>
          <g class="sui-fan-handle" filter="url(#sui-fan-glow)"><circle cx="49" cy="72" r="5" fill="#f9f5dc" stroke="var(--sui-gold)" stroke-width="2"/><path d="M49 77L45 91M49 77L55 90" stroke="url(#sui-fan-gold)" stroke-width="2" stroke-linecap="round"/><path d="M45 91Q50 87 55 90" fill="none" stroke="var(--sui-red)" stroke-width="2"/></g>
          <g class="sui-fan-dew" fill="url(#sui-dew-drop)" filter="url(#sui-fan-glow)"><path d="M24 30C24 30 19 37 19 40A5 5 0 0 0 29 40C29 37 24 30 24 30Z"/><path d="M75 31C75 31 71 37 71 40A4 4 0 0 0 79 40C79 37 75 31 75 31Z"/><circle cx="64" cy="13" r="2.1"/></g>
        </svg>
        <small>栖霞 · 饮露</small>` },
      });

    return context.mountCanonicalTheme({
      namespace: "sui",
      themeClass: "sui-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["jade", "gold", "blue", "red"],
        projectTone: "ledger",
        threadIndex: "thread",
        sections: { "置顶": "珍藏卷", "项目": "商会簿" },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".sui-seal");
        positionPanelAboveCards(
          main,
          ".sui-shanhe-sync",
          [".sui-task-card-human", ".sui-task-card-bird"],
          320,
          56,
          40,
        );
      },
    });
  },
  async unmount() {}
});
