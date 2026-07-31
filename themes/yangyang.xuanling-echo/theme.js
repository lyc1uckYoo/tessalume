registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");
    root.innerHTML = `
      <div class="xyl-stage" data-theme-stage>
        <section class="xyl-hero-copy" data-theme-role="hero" data-theme-part="hero-copy">
          <span class="xyl-kicker" data-theme-part="hero-kicker"><i></i><span class="xyl-light-only">XUANFANG DAWN · AZURE PLUME</span><span class="xyl-dark-only">SILENT NIGHT · HAVOC PLUME</span></span>
          <h1 class="xyl-light-only" data-theme-part="hero-title-light">清风骀荡<br><em>苍翎响远音</em></h1>
          <h1 class="xyl-dark-only" data-theme-part="hero-title-dark">裁羽寂万音<br><em>此剑为守护</em></h1>
          <p>${config.subtitle}</p>
          <div class="xyl-phases" data-theme-part="hero-motion" aria-label="苍翎六音风轨"><i style="--xyl-feather:0"></i><i style="--xyl-feather:1"></i><i style="--xyl-feather:2"></i><i style="--xyl-feather:3"></i><i style="--xyl-feather:4"></i><i style="--xyl-feather:5"></i><b></b></div>
          <div class="xyl-oracle-note" data-theme-part="hero-note"><small>苍翎六音</small><strong class="xyl-light-only">苍剑式 · 听风而行</strong><strong class="xyl-dark-only">羽剑式 · 万籁俱寂</strong></div>
        </section>
        <div class="xyl-identity" data-theme-role="identity" data-theme-part="identity"><span data-theme-part="identity-emblem"></span><div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div><i data-theme-part="identity-status"></i></div>
        <aside class="xyl-task-card xyl-task-card-left" data-theme-role="task-left" data-theme-part="task-card-left"><i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>苍翎 · 执剑</b><small>AZURE FORM / GUARDIAN</small></div></aside>
        <aside class="xyl-task-card xyl-task-card-right xyl-task-card-human" data-theme-role="task-right" data-theme-priority="secondary" data-theme-part="task-card-right-secondary"><i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>秧秧 · 羽剑</b><small>HEAVY STRIKE / HAVOC</small></div></aside>
        <aside class="xyl-task-card xyl-task-card-right xyl-task-card-fox" data-theme-role="task-right" data-theme-priority="primary" data-theme-part="task-card-right-primary"><i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>玄翎 · 归风</b><small>PLUME ECHO / XUANLING</small></div></aside>
        <aside class="xyl-memory" data-theme-role="memory" data-theme-part="memory-card"><small>祀声档案 · RA2710-G</small><p>${config.memory}</p><span data-theme-part="memory-meter">${Array.from({ length: 6 }, (_, i) => `<i style="--n:${i}"></i>`).join("")}</span></aside>
      </div>`;

    root.insertAdjacentHTML("beforeend", `
      <div class="xyl-domain xyl-plume-resonance" data-theme-role="sync-panel" data-theme-priority="secondary" data-theme-part="sync-panel">
        <span class="xyl-resonance-copy" data-theme-part="sync-copy"><small>湮灭共鸣 · 玄翎六音</small><b>裁羽式 <strong>VI</strong><em>/VI</em></b></span>
        <span class="xyl-resonance-core" data-theme-part="sync-core"><i></i><i></i><i></i><b>翎</b><small>湮灭</small></span>
        <span class="xyl-resonance-meter" data-theme-part="sync-meter">${Array.from({ length: 6 }, (_, i) => `<i style="--i:${i};--h:${12 + ((i * 7) % 17)}px"></i>`).join("")}</span>
        <span class="xyl-resonance-state" data-theme-part="sync-state"><small>迅刀</small><b><i></i>归风</b></span>
      </div>
      <div class="xyl-composer-charm" data-theme-role="composer-accessory" data-theme-part="composer-accessory">
        <svg class="xyl-plume-blade" viewBox="0 0 100 100" aria-hidden="true">
          <defs>
            <linearGradient id="xyl-plume-steel" x1="0" y1="1" x2="1" y2="0"><stop stop-color="var(--xyl-indigo)"/><stop offset=".42" stop-color="var(--xyl-blue)"/><stop offset=".76" stop-color="var(--xyl-cyan)"/><stop offset="1" stop-color="#f5ffff"/></linearGradient>
            <linearGradient id="xyl-plume-gold" x1="0" y1="1" x2="1" y2="0"><stop stop-color="var(--xyl-blue)"/><stop offset=".6" stop-color="var(--xyl-gold)"/><stop offset="1" stop-color="#fff4bd"/></linearGradient>
            <filter id="xyl-plume-glow"><feGaussianBlur stdDeviation="1.35" result="blur"/><feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
          </defs>
          <g class="xyl-plume-orbit" fill="none" stroke-linecap="round">
            <path d="M18 61C9 39 26 15 51 13C70 11 87 23 91 40" stroke="var(--xyl-cyan)" stroke-width="1.2"/>
            <path d="M84 66C76 83 55 91 37 84" stroke="var(--xyl-gold)" stroke-width="1"/>
            <path d="M13 69C25 75 31 78 40 87" stroke="var(--xyl-blue)" stroke-width="1" stroke-dasharray="3 5"/>
          </g>
          <g class="xyl-plume-forge" filter="url(#xyl-plume-glow)">
            <path class="xyl-plume-spine" d="M31 72L75 27" fill="none" stroke="url(#xyl-plume-steel)" stroke-width="3.2" stroke-linecap="round"/>
            <path class="xyl-plume-segment" style="--i:0" d="M40 64C31 61 25 64 20 70C29 67 35 71 39 76Z" fill="url(#xyl-plume-steel)"/>
            <path class="xyl-plume-segment" style="--i:1" d="M46 58C37 54 31 56 26 61C35 59 40 63 44 69Z" fill="url(#xyl-plume-steel)"/>
            <path class="xyl-plume-segment" style="--i:2" d="M52 52C44 47 38 49 33 53C42 52 47 56 50 63Z" fill="url(#xyl-plume-steel)"/>
            <path class="xyl-plume-segment" style="--i:3" d="M57 47C63 38 69 35 76 36C68 40 65 46 64 53Z" fill="url(#xyl-plume-steel)"/>
            <path class="xyl-plume-segment" style="--i:4" d="M63 41C68 32 74 29 81 30C73 34 71 40 70 47Z" fill="url(#xyl-plume-steel)"/>
            <path class="xyl-plume-segment" style="--i:5" d="M69 35C73 27 79 23 86 24C79 28 77 34 76 40Z" fill="url(#xyl-plume-steel)"/>
            <path class="xyl-plume-tip" d="M73 29L86 18L78 32Z" fill="url(#xyl-plume-gold)"/>
            <path class="xyl-plume-guard" d="M24 69L37 82M27 76L34 69" fill="none" stroke="url(#xyl-plume-gold)" stroke-width="3" stroke-linecap="round"/>
            <path d="M25 78L18 85" fill="none" stroke="var(--xyl-indigo)" stroke-width="4" stroke-linecap="round"/>
            <circle cx="17" cy="86" r="3.2" fill="none" stroke="var(--xyl-gold)" stroke-width="1.5"/>
          </g>
          <g class="xyl-plume-motes" fill="var(--xyl-cyan)"><circle cx="17" cy="49" r="1.7"/><circle cx="80" cy="48" r="1.3"/><circle cx="68" cy="78" r="1.5"/><circle cx="91" cy="58" r="1"/></g>
        </svg>
        <small>苍剑 ⇄ 羽剑</small>
      </div>`);

    return context.mountCanonicalTheme({
      namespace: "xyl",
      themeClass: "xyl-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["azure", "plume", "wind", "echo"],
        projectTone: "phase",
        threadIndex: "thread",
        sections: { "置顶": "随风之信", "项目": "玄方声轨" },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".xyl-composer-charm");
        positionPanelAboveCards(
          main,
          ".xyl-plume-resonance",
          [".xyl-task-card-human", ".xyl-task-card-fox"],
          320,
          56,
          40,
        );
      },
    });
  },
  async unmount() {},
});
