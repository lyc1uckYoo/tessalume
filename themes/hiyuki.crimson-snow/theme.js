registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");
    root.innerHTML = `
      <div class="hy3-stage" data-theme-stage>
        <section class="hy3-hero-copy" data-theme-role="hero" data-theme-part="hero-copy">
          <span class="hy3-kicker" data-theme-part="hero-kicker"><i></i><span class="hy3-light-only">PRESENT SELF · SNOWBOUND VOW</span><span class="hy3-dark-only">FORECLAIMED · BLADE LIBERATION</span></span>
          <h1 class="hy3-light-only" data-theme-part="hero-title-light">守住此刻<br><em>让绯樱落在雪前</em></h1>
          <h1 class="hy3-dark-only" data-theme-part="hero-title-dark">预见未来<br><em>以归刃斩断寒夜</em></h1>
          <p>${config.subtitle}</p>
          <div class="hy3-atlas" data-theme-part="hero-motion" aria-label="常世与预求未来分岔"><i></i><i></i><i></i><i></i><b></b></div>
          <div class="hy3-mode" data-theme-part="hero-note"><small class="hy3-light-only">常世身</small><small class="hy3-dark-only">预求身</small><strong class="hy3-light-only">心念与霜刃已归入刀鞘</strong><strong class="hy3-dark-only">居合与预见正冻结未至之敌</strong></div>
        </section>
        <div class="hy3-identity" data-theme-role="identity" data-theme-part="identity"><span data-theme-part="identity-emblem"><i></i><b></b></span><div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div><em data-theme-part="identity-status"></em></div>
        <aside class="hy3-task-card hy3-task-left" data-theme-role="task-left" data-theme-part="task-card-left"><i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>愿望 · 结绳</b><small>VOW RECORD / 01</small></div></aside>
        <aside class="hy3-task-card hy3-task-right hy3-task-present" data-theme-role="task-right" data-theme-priority="secondary" data-theme-part="task-card-right-secondary"><i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>常世 · 居合</b><small>PRESENT SELF / 02</small></div></aside>
        <aside class="hy3-task-card hy3-task-right hy3-task-foreclaimed" data-theme-role="task-right" data-theme-priority="primary" data-theme-part="task-card-right-primary"><i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>预求 · 归刃</b><small>FORECLAIMED / 03</small></div></aside>
        <aside class="hy3-memory" data-theme-role="memory" data-theme-part="memory-card"><small>SPECIAL RESPONSE · 愿望档案</small><p>${config.memory}</p><span class="hy3-memory-sigil" data-theme-part="memory-meter" aria-hidden="true"><svg viewBox="0 0 122 32" fill="none" xmlns="http://www.w3.org/2000/svg"><path class="hy3-memory-vow" d="M8 20c10-15 23-15 32-3 8 11 18 11 27-2 10-15 24-13 32 1 6 10 12 10 18 2"/><path class="hy3-memory-orbit" d="M46 16c4-8 10-12 15-12s11 4 15 12c-4 8-10 12-15 12s-11-4-15-12Z"/><path class="hy3-memory-frost" d="m61 7 2.7 6.3L70 16l-6.3 2.7L61 25l-2.7-6.3L52 16l6.3-2.7L61 7Z"/><path class="hy3-memory-echo" d="M22 26h18M82 26h18"/></svg></span></aside>
      </div>`;

    root.insertAdjacentHTML("beforeend", `
      <div class="hy3-syntax hy3-foreclaim-array" data-theme-role="sync-panel" data-theme-priority="secondary" data-theme-part="sync-panel">
        <span class="hy3-syntax-copy" data-theme-part="sync-copy"><small>预求我身 · 万世霜天</small><b>寒意 <strong>III</strong><em>/III</em></b></span>
        <span class="hy3-syntax-core" data-theme-part="sync-core" aria-hidden="true">
          <i></i><i></i><i></i>
          <svg viewBox="0 0 36 36" fill="none"><path class="hy3-bell-cord" d="M18 2C12 6 14 10 18 12C22 10 24 6 18 2Z"/><path class="hy3-bell-shell" d="M10 23C12 20 12.5 17 12.5 14C12.5 10.5 14.7 8 18 8C21.3 8 23.5 10.5 23.5 14C23.5 17 24 20 26 23C22.8 25.2 13.2 25.2 10 23Z"/><path class="hy3-bell-rim" d="M8 24C13 27 23 27 28 24"/><circle class="hy3-bell-clapper" cx="18" cy="28" r="2"/></svg>
        </span>
        <span class="hy3-syntax-meter" data-theme-part="sync-meter" aria-hidden="true"><i style="--i:0"><b></b></i><i style="--i:1"><b></b></i><i style="--i:2"><b></b></i><em></em></span>
        <span class="hy3-syntax-state" data-theme-part="sync-state"><small>预求身</small><b><i></i>归刃</b></span>
      </div>
      <div class="hy3-weapon-charm" data-theme-role="composer-accessory" data-theme-part="composer-accessory">
        <svg class="hy3-frostburn" viewBox="0 0 100 100" aria-hidden="true">
          <defs>
            <linearGradient id="hy3-frostburn-steel" x1="0" y1="1" x2="1" y2="0"><stop offset="0" stop-color="#17223d"/><stop offset=".35" stop-color="#5f7198"/><stop offset=".62" stop-color="#edfaff"/><stop offset="1" stop-color="#9fdff0"/></linearGradient>
            <linearGradient id="hy3-frostburn-crimson" x1="0" y1="1" x2="1" y2="0"><stop offset="0" stop-color="#8d1734"/><stop offset=".52" stop-color="#ff536e"/><stop offset="1" stop-color="#ffd8df"/></linearGradient>
            <filter id="hy3-frostburn-glow" x="-80%" y="-80%" width="260%" height="260%"><feGaussianBlur stdDeviation="2" result="blur"/><feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
          </defs>
          <g class="hy3-frostburn-ring" fill="none" stroke-linecap="round"><circle cx="58" cy="48" r="21" stroke="#f44f70" stroke-width="2" stroke-dasharray="25 8 4 7"/><circle cx="58" cy="48" r="16" stroke="#a8ebf5" stroke-width=".9" stroke-dasharray="5 5"/><path d="M32 61C42 32 68 19 88 31C70 27 52 39 45 57" stroke="#ff9eb0" stroke-width="1.2" opacity=".7"/></g>
          <g class="hy3-frostburn-echo" opacity=".62"><path d="M18 79L67 17L73 13L70 22L29 84Z" fill="#81508e" stroke="#d7c8ff" stroke-width="1"/><path d="M20 80L27 86L17 91L11 86Z" fill="#342747" stroke="#c8a9dc" stroke-width="1"/></g>
          <g class="hy3-frostburn-blade"><path d="M23 75L72 18L81 12L77 23L34 82Z" fill="url(#hy3-frostburn-steel)" stroke="#eefcff" stroke-width="1.1"/><path d="M29 75L72 23L77 19L72 29L36 79Z" fill="url(#hy3-frostburn-crimson)" opacity=".82"/><path d="M24 74L34 82L29 88L17 81Z" fill="#1c2842" stroke="#f79aac" stroke-width="1.2"/><path d="M18 80L30 88L25 94L12 86Z" fill="#303a59" stroke="#b8e9f5" stroke-width="1"/><path d="M21 75L32 84" stroke="#fff0f3" stroke-width="2.2"/><circle cx="29" cy="80" r="3.4" fill="#f84e6c" stroke="#dffaff" stroke-width="1.2"/><path class="hy3-frostburn-edge" d="M35 74L76 20" fill="none" stroke="#e9fcff" stroke-width="1.5" filter="url(#hy3-frostburn-glow)"/></g>
          <g class="hy3-frostburn-snow" fill="#dcfaff" filter="url(#hy3-frostburn-glow)"><path d="M79 30l1.5 3l3 1.5l-3 1.5l-1.5 3l-1.5-3l-3-1.5l3-1.5Z"/><path d="M48 22l1 2l2 1l-2 1l-1 2l-1-2l-2-1l2-1Z"/><circle cx="84" cy="56" r="1.4"/><circle cx="45" cy="66" r="1.2"/></g>
        </svg>
        <small>灼霜 · 预见归刃</small>
      </div>`);

    return context.mountCanonicalTheme({
      namespace: "hy3",
      themeClass: "hy3-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["vow", "present", "frost", "foreclaimed"],
        projectTone: "phase",
        threadIndex: "thread",
        sections: { "置顶": "守愿书签", "项目": "预见档案" },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".hy3-weapon-charm");
        positionPanelAboveCards(
          main,
          ".hy3-syntax",
          [".hy3-task-present", ".hy3-task-foreclaimed"],
          320,
          56,
          40,
        );
      },
    });
  },
  async unmount() {},
});
