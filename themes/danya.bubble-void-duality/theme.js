registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");
    context.renderTemplateV1({
        stageClass: "dny-stage",
        stageDecorations: `<div class="dny-main-frame"><i></i><i></i><i></i><i></i></div>
<div class="dny-orbit-watermark"><i></i><i></i><b></b></div>
<div class="dny-task-index"><small class="dny-light-only">BUBBLE OBSERVATORY</small><small class="dny-dark-only">VOID OBSERVATORY</small><b>FIELD / 07</b><span><i></i><i></i><i></i></span></div>
<div class="dny-mode-seal"><i></i><b></b><span class="dny-light-only">H</span><span class="dny-dark-only">V</span></div>
<div class="dny-bubbles">${Array.from({length:14},(_,i)=>`<i style="--i:${i}"></i>`).join("")}</div>`,
        hero: { tag: "section", className: "dny-hero-copy", html: `<span class="dny-kicker" data-theme-part="hero-kicker"><i></i><span class="dny-light-only">HUMAN ANNOTATION · 泡影记录</span><span class="dny-dark-only">VOID ANNOTATION · 幻灭记录</span></span>
          <h1 class="dny-light-only" data-theme-part="hero-title-light">把今日装进<br><em>不会破碎的泡泡</em></h1>
          <h1 class="dny-dark-only" data-theme-part="hero-title-dark">让群星沉入<br><em>无人知晓的寂静</em></h1>
          <p>${config.subtitle}</p>
          <div class="dny-domain-line" data-theme-part="hero-motion" aria-label="泡影与虚阈相位轨迹"><span class="dny-domain-track"></span><span class="dny-domain-phases dny-domain-phases-light dny-light-only" data-dny-home-fx="bubble-prism-v2"><i></i><i></i><i></i><i></i><i></i><b></b></span><span class="dny-domain-phases dny-domain-phases-dark dny-dark-only" data-dny-home-fx="void-lattice-v2"><i></i><i></i><i></i><i></i><i></i><b></b></span></div>
          <div class="dny-mode-note dny-light-only" data-theme-part="hero-note"><small>DISGUISE FORM</small><b>温柔的学院观测者</b></div>
          <div class="dny-mode-note dny-dark-only" data-theme-part="hero-note"><small>ANNIHILATION FORM</small><b>真正的鸣式共鸣者</b></div>` },
        identity: { tag: "div", className: "dny-identity", html: `<span class="dny-avatar" data-theme-part="identity-emblem"></span><span data-theme-part="identity-copy"><b>${config.title}</b><small class="dny-light-only">${config.status}</small><small class="dny-dark-only">VOID FORM · ANNIHILATION SYNC</small></span><i data-theme-part="identity-status"></i>` },
        taskLeft: { tag: "aside", className: "dny-portrait-card", html: `<div class="dny-portrait-art" data-theme-part="task-card-art"></div><div data-theme-part="task-card-caption"><b class="dny-light-only">达妮娅 · 泡影</b><b class="dny-dark-only">达妮娅 · 虚阈</b><small class="dny-light-only">HUMAN DISGUISE / 01</small><small class="dny-dark-only">TRUE VOID FORM / 01</small></div>` },
        taskSecondary: { tag: "aside", className: "dny-alt-card", html: `<div class="dny-alt-art" data-theme-part="task-card-art"></div><div data-theme-part="task-card-caption"><b class="dny-light-only">伪装 · 观星</b><b class="dny-dark-only">真形 · 湮灭</b><small class="dny-light-only">BUBBLE THEATRE / 02</small><small class="dny-dark-only">ANNIHILATION FIELD / 02</small></div>` },
        taskPrimary: { tag: "aside", className: "dny-observer", html: `<div class="dny-observer-art" data-theme-part="task-card-art"></div><div data-theme-part="task-card-caption"><b class="dny-light-only">达妮娅 · 伪装</b><b class="dny-dark-only">达妮娅 · 真形</b><small class="dny-light-only">BUBBLE DOMAIN ACTIVE</small><small class="dny-dark-only">SILENCE DOMAIN ACTIVE</small></div>` },
        memory: { tag: "aside", className: "dny-memory", html: `<small class="dny-light-only">HUMAN MEMORY</small><small class="dny-dark-only">VOID MEMORY</small><p class="dny-light-only">${config.memory}</p><p class="dny-dark-only">所有被观测的星光，终将在寂静虚阈中熄灭。</p><span class="dny-memory-sigil" data-theme-part="memory-meter"><svg viewBox="0 0 122 32" aria-hidden="true"><defs><radialGradient id="dny-memory-bubble"><stop offset="0" stop-color="#fff" stop-opacity=".82"/><stop offset=".48" stop-color="#f8c7e4" stop-opacity=".36"/><stop offset=".76" stop-color="#8de8f3" stop-opacity=".2"/><stop offset="1" stop-color="#8a7ce2" stop-opacity=".08"/></radialGradient><radialGradient id="dny-memory-void"><stop offset="0" stop-color="#050817"/><stop offset=".48" stop-color="#151142"/><stop offset=".78" stop-color="#6a32c5"/><stop offset="1" stop-color="#b256e7" stop-opacity=".25"/></radialGradient><linearGradient id="dny-memory-orbit" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#f5c570"/><stop offset=".42" stop-color="#ffb8db"/><stop offset=".72" stop-color="#86e6ee"/><stop offset="1" stop-color="#8273dd"/></linearGradient></defs><path class="dny-memory-trace dny-memory-trace-left" d="M2 18h23l7-5h12"/><path class="dny-memory-trace dny-memory-trace-right" d="M120 18H97l-7-5H78"/><g class="dny-memory-orbits"><ellipse cx="61" cy="16" rx="24" ry="9"/><ellipse cx="61" cy="16" rx="11" ry="23" transform="rotate(58 61 16)"/></g><circle class="dny-memory-bubble" cx="61" cy="16" r="12"/><circle class="dny-memory-core" cx="61" cy="16" r="7"/><path class="dny-memory-star" d="M61 5l2.5 7.3L71 16l-7.5 3.7L61 27l-2.5-7.3L51 16l7.5-3.7Z"/><path class="dny-memory-constellation" d="M56 19l3-7 5 3 3-4M59 12l-4-3M64 15l4 4"/><g class="dny-memory-satellites"><circle cx="38" cy="9" r="2.2"/><circle cx="82" cy="24" r="1.8"/><circle cx="88" cy="8" r="1.3"/></g></svg></span>` },
        syncPanel: { tag: "div", className: "dny-sync", html: `<span class="dny-sync-copy" data-theme-part="sync-copy"><small>DUAL FORM · SYNCHRONIZATION</small><b class="dny-light-only">泡影稳定 <strong>97.4</strong><em>%</em></b><b class="dny-dark-only">虚域稳定 <strong>99.8</strong><em>%</em></b></span>
          <span class="dny-sync-core" data-theme-part="sync-core" data-dny-sync-fx="duality-chamber-v2" aria-hidden="true"><i></i><b></b><em></em></span>
          <span class="dny-sync-meter" data-theme-part="sync-meter" aria-hidden="true">${Array.from({length:11},(_,i)=>`<i style="--i:${i};--h:${8 + (i % 4) * 5}px"></i>`).join("")}</span>
          <span class="dny-sync-state" data-theme-part="sync-state"><small>FORM LOCK</small><b><i></i><span class="dny-light-only">拟态</span><span class="dny-dark-only">侵蚀</span></b></span>` },
        composerAccessory: { tag: "div", className: "dny-weapon-charm", html: `<svg class="dny-forged-star dny-forged-star-light dny-light-only" viewBox="0 0 100 100" aria-hidden="true">
            <defs><linearGradient id="dny-prop-metal" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#a85d88"/><stop offset=".34" stop-color="#f1b8d5"/><stop offset=".68" stop-color="#e8fbff"/><stop offset="1" stop-color="#69bfd5"/></linearGradient><radialGradient id="dny-prop-pearl"><stop stop-color="#fff"/><stop offset=".3" stop-color="#ffd8ea"/><stop offset=".64" stop-color="#8ddbea"/><stop offset="1" stop-color="#9979d8" stop-opacity=".45"/></radialGradient><filter id="dny-prop-glow" x="-80%" y="-80%" width="260%" height="260%"><feGaussianBlur stdDeviation="2" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs>
            <g class="dny-prop-body"><path d="M18 84L55 48" stroke="url(#dny-prop-metal)" stroke-width="7" stroke-linecap="round"/><path d="M17 85L54 48" stroke="#fff" stroke-opacity=".72" stroke-width="1.2"/><path d="M12 89l7-14 9 9-14 7Z" fill="#d98eb5" stroke="#fff0f8"/><path d="M45 52l-13-3 7 12-12 5 15 2" fill="none" stroke="#f1a8cd" stroke-width="2"/></g>
            <g class="dny-prop-orbits" fill="none" stroke-linecap="round"><ellipse cx="65" cy="37" rx="25" ry="13" transform="rotate(-24 65 37)" stroke="#72d3e4" stroke-width="1.2" stroke-dasharray="15 5 2 5"/><ellipse cx="65" cy="37" rx="14" ry="25" transform="rotate(42 65 37)" stroke="#ef91bf" stroke-width="1" stroke-dasharray="8 5"/></g>
            <g class="dny-prop-core" filter="url(#dny-prop-glow)"><circle cx="65" cy="37" r="14" fill="url(#dny-prop-pearl)" stroke="#fff" stroke-opacity=".8"/><circle cx="65" cy="37" r="7" fill="none" stroke="#fff" stroke-width="1.2"/><path d="m65 26 2.7 7.8 7.8 2.7-7.8 2.7L65 47l-2.7-7.8-7.8-2.7 7.8-2.7Z" fill="#fff8d9" stroke="#e3ad61" stroke-width=".6"/></g>
            <g class="dny-prop-bubbles" fill="none" stroke="#fff"><circle cx="40" cy="25" r="5"/><circle cx="84" cy="54" r="4"/><circle cx="83" cy="19" r="2.8"/></g>
          </svg>
          <svg class="dny-forged-star dny-forged-star-dark dny-dark-only" viewBox="0 0 100 100" aria-hidden="true">
            <defs><linearGradient id="dny-void-metal" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#071024"/><stop offset=".42" stop-color="#304a91"/><stop offset=".72" stop-color="#9b69eb"/><stop offset="1" stop-color="#e653a4"/></linearGradient><radialGradient id="dny-void-core"><stop stop-color="#010208"/><stop offset=".48" stop-color="#080d25"/><stop offset=".7" stop-color="#4f45bd"/><stop offset=".9" stop-color="#d44fa2"/><stop offset="1" stop-color="#02040d"/></radialGradient><filter id="dny-void-glow" x="-90%" y="-90%" width="280%" height="280%"><feGaussianBlur stdDeviation="2.4" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs>
            <g class="dny-void-cage"><path d="M12 79 23 48 46 20 79 24 91 47 77 72 43 78 27 93Z" fill="#030713" stroke="url(#dny-void-metal)" stroke-width="2"/><path d="m15 78 15-8m-6-21 13 7m9-35 5 13m28-9-11 13m23 10-15 2m1 22-12-9m-22 15-1 13" stroke="#8f76ea" stroke-width="1.2" stroke-dasharray="4 4"/></g>
            <g class="dny-void-rings" fill="none" stroke-linecap="round"><ellipse cx="60" cy="48" rx="29" ry="12" transform="rotate(-19 60 48)" stroke="#718cff" stroke-width="1.2" stroke-dasharray="17 7 2 6"/><ellipse cx="60" cy="48" rx="13" ry="29" transform="rotate(47 60 48)" stroke="#e35aa8" stroke-width="1" stroke-dasharray="5 7"/></g>
            <g class="dny-void-dwarf" filter="url(#dny-void-glow)"><circle cx="60" cy="48" r="16" fill="url(#dny-void-core)"/><circle cx="60" cy="48" r="8" fill="#010208" stroke="#9c7cff"/><path d="M60 34a14 14 0 0 0 0 28c-7-8-7-20 0-28Z" fill="#e35aa8" opacity=".38"/></g>
            <g class="dny-void-shards" fill="#ab82ff"><path d="m29 27 5 4-7 3Z"/><path d="m84 66 7 2-6 5Z"/><path d="m36 82 4-6 3 7Z"/><circle cx="83" cy="31" r="2"/></g>
          </svg>
          <small class="dny-light-only">赝作的矮星 · 泡影布景</small><small class="dny-dark-only">赝作的矮星 · 消解终幕</small>` },
      });
    return context.mountCanonicalTheme({
      namespace: "dny",
      themeClass: "dny-theme",
      templateVersion: "1.0",
      adaptiveLayout: true,
      preserveRoot: true,
      sidebar: {
        palette: ["rose", "bubble", "orbit", "void"],
        projectTone: "tone",
        threadTone: "thread-tone",
        threadIndex: "thread-index",
        inheritProjectTone: true,
        expandLabel: "展开显示",
        sections: { "置顶": "PINNED FIELDS", "项目": "ACTIVE DOMAINS" },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".dny-weapon-charm");
        positionPanelAboveCards(
          main,
          ".dny-sync",
          [".dny-alt-card", ".dny-observer"],
          320,
          56,
          40,
        );
      },
    });
  },
  async unmount() {}
});
