registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");
    context.renderTemplateV1({
        stageClass: "sui-stage",
        stageDecorations: `<div class="sui-banner-fx" aria-hidden="true">
          <svg viewBox="0 0 560 210">
            <g class="sui-banner-mountains" fill="none" stroke-linecap="round" stroke-linejoin="round">
              <path d="M8 155C54 141 71 99 105 118c22 12 31 43 64 23 33-20 43-71 80-45 24 17 30 56 70 50"/>
              <path d="M36 161c40-7 54-31 78-25 23 6 37 34 72 18 31-14 48-39 78-27 19 7 28 25 51 26"/>
              <path d="M77 132l24-28 17 23 31-51 35 65 29-42 31 35"/>
            </g>
            <g class="sui-banner-water" fill="none" stroke-linecap="round">
              <path d="M0 169c78-30 135 28 221 2 83-25 128-3 193 4 53 6 88-8 146-33"/>
              <path d="M13 184c72-20 132 19 210 1 74-17 128 7 194 9 57 2 92-16 132-34"/>
              <path d="M45 195c68-12 118 10 183-2 89-17 132 11 212 8"/>
            </g>
            <g class="sui-banner-fan">
              <path class="sui-banner-fan-silk" d="M405 160 350 99Q404 54 493 85L405 160Z"/>
              <g class="sui-banner-fan-ribs" fill="none" stroke-linecap="round">
                <path d="m405 160-55-61"/><path d="m405 160-27-81"/><path d="m405 160 7-91"/><path d="m405 160 42-79"/><path d="m405 160 69-61"/><path d="m405 160 88-75"/>
                <path d="M350 99q54-45 143-14"/>
              </g>
              <circle cx="405" cy="160" r="6"/>
            </g>
            <g class="sui-banner-dew">
              <circle cx="79" cy="157" r="4"/><circle cx="254" cy="151" r="3"/><circle cx="342" cy="172" r="3.5"/><circle cx="505" cy="150" r="2.5"/>
            </g>
          </svg>
          <small><b>栖霞饮露</b><i></i>山河水境 · SHANHE FLOW</small>
        </div>`,
        hero: { tag: "section", className: "sui-hero-copy", html: `<span class="sui-kicker" data-theme-part="hero-kicker"><i></i><span class="sui-light-only">朝晖开卷 · ZHAOMING ARCHIVE</span><span class="sui-dark-only">月映山河 · SHANHE NIGHT</span></span>
          <h1 class="sui-light-only" data-theme-part="hero-title-light">扇开千里<br><em>朝光落山河</em></h1>
          <h1 class="sui-dark-only" data-theme-part="hero-title-dark">月沉黛岭<br><em>水境照云天</em></h1>
          <p>${config.subtitle}</p>
          <div class="sui-river" data-theme-part="hero-motion"><i></i><i></i><i></i><b></b></div>
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
        syncPanel: { tag: "div", className: "sui-shanhe-sync", html: `<span class="sui-sync-copy" data-theme-part="sync-copy"><small>栖霞饮露 · QIXIA</small><b>山河水境 <strong>千里</strong><em> / FLOW</em></b></span>
        <span class="sui-sync-core" data-theme-part="sync-core">
          <svg class="sui-sync-scroll" viewBox="0 0 128 56">
            <g class="sui-sync-mountains" fill="none" stroke-linecap="round" stroke-linejoin="round"><path d="M2 37 21 23l12 10 17-23 22 29 17-16 19 14"/><path d="M1 43c31-12 48 9 76 0 24-8 39 5 50-1"/></g>
            <g class="sui-sync-water" fill="none" stroke-linecap="round"><path d="M2 48c36-10 62 8 99 0 11-3 19-3 27-2"/><path d="M7 52c31-6 54 5 87 0 13-2 23-2 34-2"/></g>
            <g class="sui-sync-fan"><path d="m83 43-21-23q20-16 50-7Z"/><path d="m83 43-21-23m21 23-9-32m9 32 4-34m-4 34 17-30m-17 30 29-30" fill="none"/></g>
            <circle class="sui-sync-dew" cx="54" cy="40" r="3"/>
          </svg>
        </span>
        <span class="sui-mist" data-theme-part="sync-meter">${Array.from({ length: 7 }, (_, i) => `<i style="--n:${i}"></i>`).join("")}</span>
        <span class="sui-sync-state" data-theme-part="sync-state"><small>昭明商会</small><b><i></i>入画</b></span>` },
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
