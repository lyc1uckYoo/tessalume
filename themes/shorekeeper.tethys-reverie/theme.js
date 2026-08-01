registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");
    context.renderTemplateV1({
        stageClass: "sk3-stage",
        hero: { tag: "section", className: "sk3-hero-copy", html: `<span class="sk3-kicker" data-theme-part="hero-kicker"><i></i><span class="sk3-light-only">BLACK SHORES · COASTLINE 01</span><span class="sk3-dark-only">TETHYS · PROBABILITY SEA</span></span>
          <h1 class="sk3-light-only" data-theme-part="hero-title-light">守望静海<br><em>让文明靠岸</em></h1>
          <h1 class="sk3-dark-only" data-theme-part="hero-title-dark">溯回星潮<br><em>聆听万千回响</em></h1>
          <p>${config.subtitle}</p>
          <div class="sk3-tide" data-theme-part="hero-motion" aria-label="镜海蝶航与泰缇斯概率潮核">
            <span class="sk3-tide-form sk3-tide-form-light sk3-light-only" data-sk3-home-fx="shoreline-butterfly-v2"><i></i><i></i><i></i><i></i><i></i><b></b><em></em></span>
            <span class="sk3-tide-form sk3-tide-form-dark sk3-dark-only" data-sk3-home-fx="tethys-probability-v2"><i></i><i></i><i></i><i></i><i></i><b></b><em></em></span>
          </div>
          <div class="sk3-mode" data-theme-part="hero-note"><small class="sk3-light-only">海岸守望</small><small class="sk3-dark-only">泰缇斯演算</small><strong class="sk3-light-only">镜海潮汐已抵达观测岸线</strong><strong class="sk3-dark-only">概率之海同步完成</strong></div>` },
        identity: { tag: "div", className: "sk3-identity", html: `<span data-theme-part="identity-emblem"><i></i><b></b></span><div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div><em data-theme-part="identity-status"></em>` },
        taskLeft: { tag: "aside", className: "sk3-task-card sk3-task-left", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>文明 · 留声</b><small>ARCHIVE ECHO / 01</small></div>` },
        taskSecondary: { tag: "aside", className: "sk3-task-card sk3-task-right sk3-task-butterfly", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>镜海 · 蝶渡</b><small>RESONANCE TIDE / 02</small></div>` },
        taskPrimary: { tag: "aside", className: "sk3-task-card sk3-task-right sk3-task-tethys", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>泰缇斯 · 溯算</b><small>CORE COMPUTE / 03</small></div>` },
        memory: { tag: "aside", className: "sk3-memory", html: `<small>TETHYS · 潮汐记忆</small><p>${config.memory}</p><span data-theme-part="memory-meter">${Array.from({length:7},(_,i)=>`<i style="--n:${i}"></i>`).join("")}</span>` },
        syncPanel: { tag: "div", className: "sk3-link-sync", html: `<span class="sk3-sync-copy" data-theme-part="sync-copy"><small>TETHYS · PROBABILITY SEA</small><b>潮汐同步 <strong>97</strong><em>/ 100</em></b></span>
          <span class="sk3-orbit" data-theme-part="sync-core"><i></i><i></i><i></i><b></b><small>SONATA OF SHORES</small></span>
          <span class="sk3-sync-spectrum" data-theme-part="sync-meter">${Array.from({length:18},(_,i)=>`<i style="--i:${i};--h:${6 + (i % 5) * 4}px"></i>`).join("")}</span>
          <span class="sk3-sync-state" data-theme-part="sync-state"><small>BLACK SHORES</small><b>共鸣</b></span>` },
        composerAccessory: { tag: "div", className: "sk3-weapon-charm", html: `<svg class="sk3-stellar-symphony" viewBox="0 0 100 100" aria-hidden="true">
            <defs>
              <linearGradient id="sk3-wing-silver" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#6d91ba"/><stop offset=".42" stop-color="#d8f3ff"/><stop offset=".72" stop-color="#fff"/><stop offset="1" stop-color="var(--sk3-violet)"/></linearGradient>
              <linearGradient id="sk3-wing-blue" x1="0" y1="1" x2="1" y2="0"><stop stop-color="var(--sk3-blue)"/><stop offset=".48" stop-color="var(--sk3-cyan)"/><stop offset="1" stop-color="#eaffff"/></linearGradient>
              <radialGradient id="sk3-star-core"><stop stop-color="#fff"/><stop offset=".2" stop-color="#dffcff"/><stop offset=".55" stop-color="var(--sk3-cyan)"/><stop offset="1" stop-color="var(--sk3-violet)"/></radialGradient>
              <filter id="sk3-star-glow" x="-100%" y="-100%" width="300%" height="300%"><feGaussianBlur stdDeviation="2.1" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
            </defs>
            <g class="sk3-symphony-orbit sk3-symphony-orbit-outer" fill="none" stroke="var(--sk3-cyan)" stroke-width=".8" stroke-linecap="round" stroke-dasharray="5 8">
              <ellipse cx="50" cy="47" rx="40" ry="17" transform="rotate(-13 50 47)"/><circle cx="13" cy="55" r="1.7" fill="var(--sk3-cyan)"/><circle cx="87" cy="39" r="1.3" fill="#fff"/>
            </g>
            <g class="sk3-symphony-orbit sk3-symphony-orbit-inner" fill="none" stroke="var(--sk3-violet)" stroke-width=".7" stroke-dasharray="3 7">
              <ellipse cx="50" cy="47" rx="29" ry="34" transform="rotate(29 50 47)"/><circle cx="68" cy="20" r="1.5" fill="var(--sk3-violet)"/>
            </g>
            <g class="sk3-symphony-wing sk3-symphony-wing-left" fill="url(#sk3-wing-silver)" stroke="var(--sk3-cyan)" stroke-width=".65" stroke-linejoin="round">
              <path d="M46 47C36 39 29 25 25 11C35 17 42 26 47 39Z"/>
              <path d="M44 51C31 49 19 41 11 30C25 31 37 36 47 44Z"/>
              <path d="M45 55C32 60 22 70 17 83C31 79 41 70 48 58Z"/>
              <path d="M39 46L24 38L35 50L22 61L42 54Z" fill="url(#sk3-wing-blue)"/>
            </g>
            <g class="sk3-symphony-wing sk3-symphony-wing-right" fill="url(#sk3-wing-silver)" stroke="var(--sk3-cyan)" stroke-width=".65" stroke-linejoin="round">
              <path d="M54 47C64 39 71 25 75 11C65 17 58 26 53 39Z"/>
              <path d="M56 51C69 49 81 41 89 30C75 31 63 36 53 44Z"/>
              <path d="M55 55C68 60 78 70 83 83C69 79 59 70 52 58Z"/>
              <path d="M61 46L76 38L65 50L78 61L58 54Z" fill="url(#sk3-wing-blue)"/>
            </g>
            <g class="sk3-symphony-spine" fill="none" stroke="url(#sk3-wing-silver)" stroke-linecap="round">
              <path d="M50 18V78" stroke-width="4.4"/><path d="M50 13V84" stroke-width="1.2"/><path d="M45 79L50 90L55 79" stroke-width="1.4"/>
            </g>
            <g class="sk3-symphony-core" filter="url(#sk3-star-glow)">
              <path d="M50 35L54 43L63 47L54 51L50 60L46 51L37 47L46 43Z" fill="url(#sk3-star-core)"/>
              <circle cx="50" cy="47" r="4.2" fill="#fff"/><circle cx="50" cy="47" r="9.5" fill="none" stroke="var(--sk3-cyan)" stroke-width=".7"/>
            </g>
            <g class="sk3-symphony-wave" fill="none" stroke="var(--sk3-cyan)" stroke-width="1" stroke-linecap="round">
              <path d="M18 70C24 64 28 76 34 70S44 64 50 70S60 76 66 70S76 64 82 70"/>
            </g>
            <g class="sk3-symphony-motes" fill="var(--sk3-cyan)">
              <path d="M16 20C12 17 10 20 13 23C10 26 13 29 17 25C21 29 24 26 21 23C24 20 21 17 16 20Z"/>
              <path d="M84 72C81 69 78 72 81 75C78 78 81 81 84 77C88 81 91 78 88 75C91 72 88 69 84 72Z"/>
              <circle cx="24" cy="14" r="1.3"/><circle cx="79" cy="17" r="1.1"/><circle cx="14" cy="65" r="1"/>
            </g>
          </svg>
          <small>星序 · 协响</small>` },
      });

    return context.mountCanonicalTheme({
      namespace: "sk3",
      themeClass: "sk3-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["coast", "butterfly", "echo", "tethys"],
        projectTone: "phase",
        threadIndex: "thread",
        sections: { "置顶": "海岸信标", "项目": "文明档案" },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".sk3-weapon-charm");
        positionPanelAboveCards(
          main,
          ".sk3-link-sync",
          [".sk3-task-butterfly", ".sk3-task-tethys"],
          320,
          56,
          40,
        );
      },
    });
  },
  async unmount() {}
});
