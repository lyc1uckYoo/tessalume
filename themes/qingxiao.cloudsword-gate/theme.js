registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");
    context.renderTemplateV1({
        stageClass: "qxo-stage",
        hero: { tag: "section", className: "qxo-hero-copy", html: `<span class="qxo-kicker" data-theme-part="hero-kicker"><i></i><span class="qxo-light-only">FIRST FROST · CLOUD GATE</span><span class="qxo-dark-only">MOON VIGIL · SWORD ARRAY</span></span>
          <h1 class="qxo-light-only" data-theme-part="hero-title-light">云门初霁<br><em>一剑照清宵</em></h1>
          <h1 class="qxo-dark-only" data-theme-part="hero-title-dark">月悬天关<br><em>万剑听霜鸣</em></h1>
          <p>${config.subtitle}</p>
          <div class="qxo-score" data-theme-part="hero-motion" aria-label="云关心剑与月轮万剑">
            <span class="qxo-score-form qxo-score-form-light qxo-light-only" data-qxo-home-fx="cloud-heart-sword-v2"><i></i><i></i><i></i><i></i><i></i><b></b></span>
            <span class="qxo-score-form qxo-score-form-dark qxo-dark-only" data-qxo-home-fx="moon-sword-array-v2"><i></i><i></i><i></i><i></i><i></i><b></b></span>
          </div>
          <div class="qxo-cue" data-theme-part="hero-note"><small>HEARTSWORD</small><strong class="qxo-light-only">心剑出鞘 · 云门开</strong><strong class="qxo-dark-only">万剑归弦 · 月轮定</strong></div>` },
        identity: { tag: "div", className: "qxo-identity", html: `<span data-theme-part="identity-emblem"></span><div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div><i data-theme-part="identity-status"></i>` },
        taskLeft: { tag: "aside", className: "qxo-task-companion qxo-task-companion-left", html: `<i data-theme-part="task-card-art"></i><div class="qxo-task-companion-caption" data-theme-part="task-card-caption"><b>清宵 · 镇玄</b><small>HEARTSWORD / VIGIL</small></div>` },
        taskSecondary: { tag: "aside", className: "qxo-task-companion qxo-task-companion-right", html: `<i data-theme-part="task-card-art"></i><div class="qxo-task-companion-caption" data-theme-part="task-card-caption"><b>清宵 · 御剑</b><small>SWORD ARRAY / COMMAND</small></div><span></span>` },
        taskPrimary: { tag: "aside", className: "qxo-hecate", html: `<div class="qxo-hecate-art" data-theme-part="task-card-art"></div><div data-theme-part="task-card-caption"><b>清宵 · 出鞘</b><small class="qxo-light-only">CLOUD GATE GUARDIAN</small><small class="qxo-dark-only">MOONLIT SWORD VIGIL</small></div><span></span>` },
        memory: { tag: "aside", className: "qxo-memory", html: `<small>JADE DESK / SWORD MEMORY</small><p>${config.memory}</p><span class="qxo-memory-array" data-theme-part="memory-meter"><svg viewBox="0 0 122 38" aria-hidden="true"><g class="qxo-memory-cloud" fill="none" stroke-linecap="round"><path d="M2 29c18-11 28 4 45-2 17-7 26 4 42 3 14-1 20-9 31-13"/><path d="M15 34c17-6 28 3 43-1 20-6 34 5 52-3"/></g><g class="qxo-memory-side-swords"><path d="m22 12 4 11-3 5-4-4-2-10z"/><path d="m39 5 5 13-3 5-5-4-3-12z"/><path d="m83 5-3 12-5 4-3-5 5-13z"/><path d="m100 12-2 10-4 4-3-5 4-11z"/></g><g class="qxo-memory-core"><path d="m61 2 6 23-6 8-6-8z"/><path d="M50 25q11-6 22 0l-4 4q-7-3-14 0Z"/><path d="M61 28v8"/></g><g class="qxo-memory-seal" fill="none"><ellipse cx="61" cy="21" rx="29" ry="13"/><path d="m61 12 8 8-8 8-8-8z"/></g></svg></span>` },
        syncPanel: { tag: "div", className: "qxo-xianxin-sync", html: `<span class="qxo-xianxin-copy" data-theme-part="sync-copy"><small>天地弦心剑 · XIANXIN</small><b>弦凝剑意 <strong>10,000</strong><em> / READY</em></b></span>
        <span class="qxo-xianxin-core" data-theme-part="sync-core">
          <svg viewBox="0 0 108 48" aria-hidden="true">
            <g class="qxo-xianxin-strings" fill="none"><path d="M2 13Q54 2 106 13"/><path d="M2 24Q54 13 106 24"/><path d="M2 35Q54 24 106 35"/></g>
            <g class="qxo-xianxin-blades">
              <path d="m18 10 3 9-3 4-3-4z"/><path d="m35 6 3 12-3 5-3-5z"/><path d="m73 6 3 12-3 5-3-5z"/><path d="m90 10 3 9-3 4-3-4z"/>
            </g>
            <g class="qxo-xianxin-heart"><path d="m54 5 8 20-8 12-8-12z"/><path d="M43 25q11-7 22 0l-5 5q-6-4-12 0z"/><circle cx="54" cy="24" r="17"/><path d="m54 13 10 11-10 11-10-11z"/></g>
          </svg>
          <small>弦化万剑</small>
        </span>
        <span class="qxo-xianxin-meter" data-theme-part="sync-meter">${Array.from({ length: 9 }, (_, i) => `<i style="--i:${i};--h:${8 + (i % 5) * 3}px"></i>`).join("")}</span>
        <span class="qxo-xianxin-state" data-theme-part="sync-state"><small>镇玄司骑</small><b><i></i>荡煞</b></span>` },
        composerAccessory: { tag: "div", className: "qxo-tempo", html: `<svg class="qxo-sword-totem" viewBox="0 0 120 90" aria-hidden="true">
          <defs>
            <linearGradient id="qxo-sword-fill" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#f8ffff"/><stop offset=".32" stop-color="var(--qxo-green)"/><stop offset=".72" stop-color="#5e9ed5"/><stop offset="1" stop-color="#435993"/></linearGradient>
            <filter id="qxo-sword-glow"><feGaussianBlur stdDeviation="1.7" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
            <g id="qxo-flying-sword"><path d="M0 0L4 18L0 25L-4 18Z" fill="url(#qxo-sword-fill)"/><path d="M-6 18H6M0 19V31" stroke="var(--qxo-gold)" stroke-width="2" stroke-linecap="round"/></g>
          </defs>
          <g class="qxo-sword-array" filter="url(#qxo-sword-glow)" opacity=".83">
            <use href="#qxo-flying-sword" transform="translate(18 22) rotate(-48) scale(.72)"/><use href="#qxo-flying-sword" transform="translate(35 8) rotate(-25) scale(.82)"/><use href="#qxo-flying-sword" transform="translate(85 8) rotate(25) scale(.82)"/><use href="#qxo-flying-sword" transform="translate(102 22) rotate(48) scale(.72)"/>
            <use href="#qxo-flying-sword" transform="translate(16 59) rotate(-72) scale(.64)"/><use href="#qxo-flying-sword" transform="translate(104 59) rotate(72) scale(.64)"/>
          </g>
          <g class="qxo-sword-main" filter="url(#qxo-sword-glow)">
            <path d="M60 4L69 49L60 61L51 49Z" fill="url(#qxo-sword-fill)" stroke="#dffcff" stroke-width="1"/>
            <path d="M42 49Q60 42 78 49L72 55Q60 51 48 55Z" fill="var(--qxo-gold)"/>
            <path d="M60 53V78" stroke="#54729b" stroke-width="6" stroke-linecap="round"/><path d="M60 56V76" stroke="var(--qxo-gold)" stroke-width="2" stroke-dasharray="3 3"/>
            <path d="M53 78L60 86L67 78Z" fill="var(--qxo-red)"/>
          </g>
          <g class="qxo-sword-runes" fill="none" stroke="var(--qxo-green)" opacity=".66"><path d="M25 68Q60 82 95 68"/><path d="M32 72Q60 63 88 72" stroke-dasharray="3 5"/></g>
        </svg>
        <small>万剑 · 归弦</small>` },
      });

    return context.mountCanonicalTheme({
      namespace: "qxo",
      themeClass: "qxo-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["cloud", "string", "sword", "seal"],
        projectTone: "phase",
        threadIndex: "thread",
        sections: { "置顶": "云关信标", "项目": "玄方卷宗" },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".qxo-tempo");
        positionPanelAboveCards(
          main,
          ".qxo-xianxin-sync",
          [".qxo-task-companion-right", ".qxo-hecate"],
          320,
          56,
          40,
        );
      },
    });
  },
  async unmount() {}
});
