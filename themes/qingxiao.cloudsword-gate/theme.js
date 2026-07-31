registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");
    root.innerHTML = `
      <div class="qxo-stage" data-theme-stage>
        <section class="qxo-hero-copy" data-theme-role="hero" data-theme-part="hero-copy">
          <span class="qxo-kicker" data-theme-part="hero-kicker"><i></i><span class="qxo-light-only">FIRST FROST · CLOUD GATE</span><span class="qxo-dark-only">MOON VIGIL · SWORD ARRAY</span></span>
          <h1 class="qxo-light-only" data-theme-part="hero-title-light">云门初霁<br><em>一剑照清宵</em></h1>
          <h1 class="qxo-dark-only" data-theme-part="hero-title-dark">月悬天关<br><em>万剑听霜鸣</em></h1>
          <p>${config.subtitle}</p>
          <div class="qxo-score" data-theme-part="hero-motion"><i></i><i></i><i></i><i></i><i></i><b></b></div>
          <div class="qxo-cue" data-theme-part="hero-note"><small>HEARTSWORD</small><strong class="qxo-light-only">心剑出鞘 · 云门开</strong><strong class="qxo-dark-only">万剑归弦 · 月轮定</strong></div>
        </section>
        <div class="qxo-banner-fx" aria-hidden="true">
          <svg viewBox="0 0 560 220">
            <defs>
              <linearGradient id="qxo-banner-blade" x1="0" y1="0" x2="1" y2="1">
                <stop stop-color="#f8ffff"/><stop offset=".36" stop-color="var(--qxo-red-bright)"/><stop offset=".72" stop-color="var(--qxo-violet)"/><stop offset="1" stop-color="var(--qxo-gold)"/>
              </linearGradient>
              <filter id="qxo-banner-glow"><feGaussianBlur stdDeviation="1.8" result="blur"/><feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
            </defs>
            <g class="qxo-banner-cloud-ring" fill="none">
              <ellipse cx="376" cy="113" rx="128" ry="64"/>
              <ellipse cx="376" cy="113" rx="99" ry="48"/>
              <path d="M242 112c31-34 69-51 115-53M396 61c42 7 73 25 107 55M250 139c35 26 72 35 113 31M405 169c38-7 69-25 94-49"/>
            </g>
            <g class="qxo-banner-wind" fill="none" stroke-linecap="round">
              <path d="M14 169c68-31 125 24 202-2 73-25 121-1 179 8 61 10 105-4 151-35"/>
              <path d="M36 187c72-22 129 18 205-1 69-18 118 5 184 8 55 3 91-14 121-32"/>
              <path d="M79 202c59-12 105 8 168-3 82-15 134 13 222 2"/>
            </g>
            <g class="qxo-banner-flying-swords" fill="url(#qxo-banner-blade)" filter="url(#qxo-banner-glow)">
              <path d="m265 73 8 27-5 9-8-6-4-27z"/><path d="m295 44 11 30-4 10-9-7-7-29z"/>
              <path d="m337 29 7 34-6 9-8-9-1-34z"/><path d="m415 29-1 34-8 9-6-9 7-34z"/>
              <path d="m457 44-7 29-9 7-4-10 11-30z"/><path d="m487 73-4 27-8 6-5-9 8-27z"/>
            </g>
            <g class="qxo-banner-heart-sword" filter="url(#qxo-banner-glow)">
              <path class="qxo-banner-core-blade" d="M376 19 389 128 376 149 363 128Z" fill="url(#qxo-banner-blade)"/>
              <path class="qxo-banner-core-line" d="M376 30v106M367 121l9 15 9-15" fill="none"/>
              <path class="qxo-banner-guard" d="M343 129q33-16 66 0l-9 11q-24-10-48 0Z"/>
              <path class="qxo-banner-grip" d="M376 136v44m-6-34h12m-12 10h12m-12 10h12"/>
              <path class="qxo-banner-pommel" d="m366 181 10 13 10-13-10-8Z"/>
            </g>
            <g class="qxo-banner-heart-seal" fill="none">
              <path d="M376 85 397 106 376 127 355 106Z"/>
              <circle cx="376" cy="106" r="8"/><path d="M376 94v24M364 106h24"/>
            </g>
          </svg>
          <small><b>天地弦心剑</b><i></i>万剑归弦 · HEARTSWORD ARRAY</small>
        </div>
        <div class="qxo-identity" data-theme-role="identity" data-theme-part="identity"><span data-theme-part="identity-emblem"></span><div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div><i data-theme-part="identity-status"></i></div>
        <aside class="qxo-task-companion qxo-task-companion-left" data-theme-role="task-left" data-theme-part="task-card-left"><i data-theme-part="task-card-art"></i><div class="qxo-task-companion-caption" data-theme-part="task-card-caption"><b>清宵 · 镇玄</b><small>HEARTSWORD / VIGIL</small></div></aside>
        <aside class="qxo-task-companion qxo-task-companion-right" data-theme-role="task-right" data-theme-priority="secondary" data-theme-part="task-card-right-secondary"><i data-theme-part="task-card-art"></i><div class="qxo-task-companion-caption" data-theme-part="task-card-caption"><b>清宵 · 御剑</b><small>SWORD ARRAY / COMMAND</small></div><span></span></aside>
        <aside class="qxo-hecate" data-theme-role="task-right" data-theme-priority="primary" data-theme-part="task-card-right-primary"><div class="qxo-hecate-art" data-theme-part="task-card-art"></div><div data-theme-part="task-card-caption"><b>清宵 · 出鞘</b><small class="qxo-light-only">CLOUD GATE GUARDIAN</small><small class="qxo-dark-only">MOONLIT SWORD VIGIL</small></div><span></span></aside>
        <aside class="qxo-memory" data-theme-role="memory" data-theme-part="memory-card"><small>JADE DESK / SWORD MEMORY</small><p>${config.memory}</p><span class="qxo-memory-array" data-theme-part="memory-meter"><svg viewBox="0 0 122 38" aria-hidden="true"><g class="qxo-memory-cloud" fill="none" stroke-linecap="round"><path d="M2 29c18-11 28 4 45-2 17-7 26 4 42 3 14-1 20-9 31-13"/><path d="M15 34c17-6 28 3 43-1 20-6 34 5 52-3"/></g><g class="qxo-memory-side-swords"><path d="m22 12 4 11-3 5-4-4-2-10z"/><path d="m39 5 5 13-3 5-5-4-3-12z"/><path d="m83 5-3 12-5 4-3-5 5-13z"/><path d="m100 12-2 10-4 4-3-5 4-11z"/></g><g class="qxo-memory-core"><path d="m61 2 6 23-6 8-6-8z"/><path d="M50 25q11-6 22 0l-4 4q-7-3-14 0Z"/><path d="M61 28v8"/></g><g class="qxo-memory-seal" fill="none"><ellipse cx="61" cy="21" rx="29" ry="13"/><path d="m61 12 8 8-8 8-8-8z"/></g></svg></span></aside>
      </div>`;

    root.insertAdjacentHTML("beforeend", `
      <div class="qxo-xianxin-sync" data-theme-role="sync-panel" data-theme-priority="secondary" data-theme-part="sync-panel">
        <span class="qxo-xianxin-copy" data-theme-part="sync-copy"><small>天地弦心剑 · XIANXIN</small><b>弦凝剑意 <strong>10,000</strong><em> / READY</em></b></span>
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
        <span class="qxo-xianxin-state" data-theme-part="sync-state"><small>镇玄司骑</small><b><i></i>荡煞</b></span>
      </div>
      <div class="qxo-tempo" data-theme-role="composer-accessory" data-theme-part="composer-accessory">
        <svg class="qxo-sword-totem" viewBox="0 0 120 90" aria-hidden="true">
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
        <small>万剑 · 归弦</small>
      </div>`);

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
