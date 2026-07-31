registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");
    context.renderTemplateV1({
        stageClass: "xmf-stage",
        hero: { tag: "section", className: "xmf-hero-copy", html: `<span class="xmf-kicker" data-theme-part="hero-kicker"><i></i><span class="xmf-light-only">DAWN COURT · MOONHEART SOVEREIGN</span><span class="xmf-dark-only">CRIMSON MOON · FOX DOMAIN</span></span>
          <h1 class="xmf-light-only" data-theme-part="hero-title-light">朝月清辉<br><em>照见万心</em></h1>
          <h1 class="xmf-dark-only" data-theme-part="hero-title-dark">赤月临城<br><em>九尾定岁</em></h1>
          <p>${config.subtitle}</p>
          <div class="xmf-phases" data-theme-part="hero-motion" aria-label="岁序狐火月相">
            <i class="xmf-phase-new"></i><i class="xmf-phase-wax"></i><i class="xmf-phase-full"></i><i class="xmf-phase-wane"></i><i class="xmf-phase-eclipse"></i><b></b>
          </div>
          <div class="xmf-oracle-note" data-theme-part="hero-note"><small>岁序心印</small><strong class="xmf-light-only">人形 · 听念入梦</strong><strong class="xmf-dark-only">狐身 · 巡狩孤城</strong></div>` },
        identity: { tag: "div", className: "xmf-identity", html: `<span data-theme-part="identity-emblem"></span><div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div><i data-theme-part="identity-status"></i>` },
        taskLeft: { tag: "aside", className: "xmf-task-card xmf-task-card-left", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>月门 · 谕心</b><small>MOONHEART / ORACLE FORM</small></div>` },
        taskSecondary: { tag: "aside", className: "xmf-task-card xmf-task-card-right xmf-task-card-human", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>人形 · 执扇</b><small>SOVEREIGN / HEARTFIRE</small></div>` },
        taskPrimary: { tag: "aside", className: "xmf-task-card xmf-task-card-right xmf-task-card-fox", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>本体 · 九尾</b><small>TRUE FORM / MOON FOX</small></div>` },
        memory: { tag: "aside", className: "xmf-memory", html: `<small>梦州岁序档案 · X-09</small><p>${config.memory}</p><span data-theme-part="memory-meter">${Array.from({length:7},(_,i)=>`<i style="--n:${i}"></i>`).join("")}</span>` },
        syncPanel: { tag: "div", className: "xmf-heart-covenant", html: `<span class="xmf-covenant-copy" data-theme-part="sync-copy">
          <small>岁序心契 · 心月狐</small>
          <b>双相归心 <strong>玖</strong><em>/玖</em></b>
        </span>
        <span class="xmf-covenant-seal" data-theme-part="sync-core"><i></i><b>心</b><small>月狐</small></span>
        <span class="xmf-covenant-flames" data-theme-part="sync-meter">${Array.from({length:9},(_,i)=>`<i style="--i:${i};--h:${8 + (i % 5) * 3}px"></i>`).join("")}</span>
        <span class="xmf-covenant-state" data-theme-part="sync-state"><small>朝月清辉</small><b><i></i>巡城</b></span>` },
        composerAccessory: { tag: "div", className: "xmf-composer-charm", html: `<svg class="xmf-heart-pendant" viewBox="0 0 96 96" aria-hidden="true">
          <defs>
            <linearGradient id="xmf-pendant-gold" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#84602f"/><stop offset=".4" stop-color="#d8b76d"/><stop offset=".72" stop-color="#fff0b9"/><stop offset="1" stop-color="#a57535"/></linearGradient>
            <radialGradient id="xmf-pendant-jade" cx=".35" cy=".25"><stop stop-color="#ffafb3"/><stop offset=".42" stop-color="#e94251"/><stop offset="1" stop-color="#7b1028"/></radialGradient>
            <linearGradient id="xmf-pendant-silk" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#fff7e6"/><stop offset=".45" stop-color="#d8b76d"/><stop offset=".46" stop-color="#d83a4c"/><stop offset="1" stop-color="#73152a"/></linearGradient>
            <filter id="xmf-pendant-glow"><feGaussianBlur stdDeviation="1.4" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
          </defs>
          <g class="xmf-heart-halo" fill="none" stroke="url(#xmf-pendant-gold)" stroke-linecap="round">
            <circle cx="48" cy="43" r="29" stroke-width="1.2" stroke-dasharray="18 5 3 5"/>
            <circle cx="48" cy="43" r="22" stroke-width=".7" stroke-dasharray="2 5"/>
            <path d="M48 8L52 14L48 20L44 14ZM83 43L77 47L71 43L77 39ZM48 78L44 72L48 66L52 72ZM13 43L19 39L25 43L19 47Z" fill="url(#xmf-pendant-gold)" stroke="none"/>
          </g>
          <g class="xmf-heart-crescent" filter="url(#xmf-pendant-glow)">
            <path d="M63 19C48 20 36 31 36 45C36 55 42 63 51 67C35 67 24 57 24 43C24 28 36 17 51 17C55 17 59 18 63 19Z" fill="url(#xmf-pendant-gold)"/>
            <path d="M32 32Q48 23 64 32M29 54Q48 64 67 53" fill="none" stroke="#fff0c0" stroke-width=".8" opacity=".72"/>
          </g>
          <g class="xmf-heart-jewel" filter="url(#xmf-pendant-glow)">
            <path d="M48 24L54 32L62 34L59 49Q56 61 48 69Q40 61 37 49L34 34L42 32Z" fill="url(#xmf-pendant-jade)" stroke="url(#xmf-pendant-gold)" stroke-width="2"/>
            <path d="M40 37Q48 31 56 37L54 48Q52 56 48 61Q44 56 42 48Z" fill="none" stroke="#ffd8d5" stroke-width="1" opacity=".68"/>
            <circle cx="44" cy="38" r="2.2" fill="#fff4ea" opacity=".86"/>
          </g>
          <g class="xmf-heart-tassels" fill="none" stroke-linecap="round">
            <path d="M36 57Q24 67 18 84M60 57Q73 67 80 84" stroke="url(#xmf-pendant-silk)" stroke-width="2.4"/>
            <path d="M34 59Q29 72 31 89M62 59Q68 72 65 89" stroke="#d83a4c" stroke-width="1.5"/>
            <path d="M15 83L19 92L23 83M76 83L80 92L84 83" stroke="url(#xmf-pendant-gold)" stroke-width="1.3"/>
          </g>
          <g class="xmf-heart-sparks" fill="#fff1bc"><circle cx="17" cy="24" r="1.2"/><circle cx="76" cy="18" r=".9"/><circle cx="84" cy="59" r="1.1"/><path d="M25 15L27 18L25 21L23 18ZM75 69L78 72L75 75L72 72Z"/></g>
        </svg>
        <small>朝月 · 心珰</small>` },
      });

    return context.mountCanonicalTheme({
      namespace: "xmf",
      themeClass: "xmf-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["dawn", "heart", "moon", "fox"],
        projectTone: "phase",
        threadIndex: "thread",
        sections: { "置顶": "听念之庭", "项目": "梦州岁序" },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".xmf-composer-charm");
        positionPanelAboveCards(
          main,
          ".xmf-heart-covenant",
          [".xmf-task-card-human", ".xmf-task-card-fox"],
          320,
          56,
          40,
        );
      },
    });
  },
  async unmount() {}
});
