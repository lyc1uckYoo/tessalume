registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");
    // 模板 1.0 固定结构①：首页主视觉、顶部身份牌与任务页五个基础组件。
    // 可替换爱弥斯专属文案、图片与内部动效，但不得改变 data-theme-role / data-theme-part。
    context.renderTemplateV1({
        stageClass: "ae3-stage",
        stageDecorations: `<div class="ae3-orbit"><i></i><i></i><i></i><b></b><small>GHOST FREQUENCY</small></div>`,
        hero: { tag: "section", className: "ae3-hero-copy", html: `<span class="ae3-kicker" data-theme-part="hero-kicker"><i></i><span class="ae3-light-only">STARTORCH · DEPARTURE GATE 07</span><span class="ae3-dark-only">TUNNELER · CORE LINK RA2362-G</span></span>
          <h1 class="ae3-light-only" data-theme-part="hero-title-light">把告别折成<br><em>新的航标</em></h1>
          <h1 class="ae3-dark-only" data-theme-part="hero-title-dark">越过深空<br><em>回应星海</em></h1>
          <p>${config.subtitle}</p>
          <div class="ae3-route" data-theme-part="hero-motion" aria-label="启航信标与隧者核心链路"><span class="ae3-route-track"></span><span class="ae3-route-form ae3-route-form-light ae3-light-only" data-ae3-home-fx="departure-gate-v2"><i></i><i></i><i></i><i></i><b></b></span><span class="ae3-route-form ae3-route-form-dark ae3-dark-only" data-ae3-home-fx="tunneler-core-v2"><i></i><i></i><i></i><i></i><b></b></span></div>
          <div class="ae3-mode" data-theme-part="hero-note"><small class="ae3-light-only">晨航模式</small><small class="ae3-dark-only">兵装链接</small><strong class="ae3-light-only">纸飞机已进入远航轨道</strong><strong class="ae3-dark-only">隧者核心同步完成</strong></div>` },
        identity: { tag: "div", className: "ae3-identity", html: `<span data-theme-part="identity-emblem"><i></i></span><div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div><em data-theme-part="identity-status"></em>` },
        taskLeft: { tag: "aside", className: "ae3-task-card ae3-task-left", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b class="ae3-light-only">星讯 · 留声</b><b class="ae3-dark-only">幽频 · 回声</b><small class="ae3-light-only">FAREWELL SIGNAL / 01</small><small class="ae3-dark-only">GHOST ECHO / 01</small></div>` },
        taskSecondary: { tag: "aside", className: "ae3-task-card ae3-task-right ae3-task-voyage", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b class="ae3-light-only">晨光 · 远航</b><b class="ae3-dark-only">深空 · 越界</b><small class="ae3-light-only">VOYAGE ROUTE / 02</small><small class="ae3-dark-only">VOID PASSAGE / 02</small></div>` },
        taskPrimary: { tag: "aside", className: "ae3-task-card ae3-task-right ae3-task-tunneler", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b class="ae3-light-only">启明星 · 引航</b><b class="ae3-dark-only">隧者 · 同调</b><small class="ae3-light-only">POLESTAR GUIDANCE / 03</small><small class="ae3-dark-only">CORE LINK / 03</small></div>` },
        memory: { tag: "aside", className: "ae3-memory", html: `<small class="ae3-light-only">STARTORCH · 毕业航标</small><small class="ae3-dark-only">RA2362-G · 核心回声</small><p class="ae3-light-only">${config.memory}</p><p class="ae3-dark-only">封存在心印黑匣子里的告别，正沿着幽频重新抵达星海。</p><span class="ae3-heart-resonator" data-theme-part="memory-meter"><svg class="ae3-heart-sigil" viewBox="0 0 122 38" aria-label="爱弥斯信号心印" role="img"><defs><linearGradient id="ae3-sigil-shell" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#ffffff"/><stop offset=".28" stop-color="#bff7ff"/><stop offset=".58" stop-color="#ff9dca"/><stop offset="1" stop-color="#b33799"/></linearGradient><linearGradient id="ae3-sigil-wave" x1="0" y1="0" x2="1" y2="0"><stop stop-color="#76e8f3"/><stop offset=".5" stop-color="#fff7ff"/><stop offset="1" stop-color="#ff62ad"/></linearGradient><radialGradient id="ae3-sigil-core"><stop stop-color="#fffaff"/><stop offset=".32" stop-color="#ffb9da"/><stop offset=".72" stop-color="#ff559f"/><stop offset="1" stop-color="#6f235f"/></radialGradient></defs><path class="ae3-sigil-shell" d="M61 34L39 20l-2-9 7-7 10 2 7 7 7-7 10-2 7 7-2 9Z"/><path class="ae3-sigil-core" d="M61 28L48 18l1-6 6-2 6 6 6-6 6 2 1 6Z"/><path class="ae3-sigil-wave" d="M45 18h8l3-5 5 11 4-7h12"/><path class="ae3-sigil-scan" d="M39 10l8-4m28 0 8 4M43 25l8 5m20 0 8-5"/><path class="ae3-sigil-glint" d="M51 12l3 2-4 5-2-4Z"/></svg></span>` },
        syncPanel: { tag: "div", className: "ae3-link-sync", html: `<span class="ae3-sync-copy" data-theme-part="sync-copy"><small>EXOSTRIDER LINK · RA2362-G</small><b>同步率 <strong>200</strong><em>/ 200</em></b></span>
          <span class="ae3-sync-core" data-theme-part="sync-core"><i></i><b>4</b><small>/4</small></span>
          <span class="ae3-sync-spectrum" data-theme-part="sync-meter">${Array.from({length:20},(_,i)=>`<i style="--i:${i};--h:${6 + (i % 5) * 4}px"></i>`).join("")}</span>
          <span class="ae3-sync-glow" data-theme-part="sync-state"><small>流溢辉光</small><b>600</b></span>` },
        composerAccessory: { tag: "div", className: "ae3-weapon-charm", html: `<svg class="ae3-everbright-polestar ae3-everbright-polestar-light ae3-light-only" viewBox="0 0 100 100" aria-hidden="true">
            <defs>
              <linearGradient id="ae3-polestar-white" x1="0" y1="1" x2="1" y2="0">
                <stop offset="0" stop-color="#334866"/><stop offset=".34" stop-color="#9cbdd0"/>
                <stop offset=".62" stop-color="#f8fdff"/><stop offset="1" stop-color="#c9e9f5"/>
              </linearGradient>
              <linearGradient id="ae3-polestar-edge" x1="0" y1="1" x2="1" y2="0">
                <stop offset="0" stop-color="#5974e9"/><stop offset=".42" stop-color="#4ce1f4"/>
                <stop offset=".74" stop-color="#d6ffff"/><stop offset="1" stop-color="#ff91cb"/>
              </linearGradient>
              <radialGradient id="ae3-polestar-core">
                <stop offset="0" stop-color="#fff"/><stop offset=".25" stop-color="#9ff5ff"/>
                <stop offset=".58" stop-color="#5ccfe9"/><stop offset=".8" stop-color="#ae6ee6"/><stop offset="1" stop-color="#15213d"/>
              </radialGradient>
              <filter id="ae3-polestar-glow" x="-90%" y="-90%" width="280%" height="280%">
                <feGaussianBlur stdDeviation="2.1" result="blur"/><feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge>
              </filter>
            </defs>
            <g class="ae3-polestar-halo" fill="none" stroke-linecap="round">
              <ellipse cx="54" cy="49" rx="31" ry="14" transform="rotate(-43 54 49)" stroke="#67d9ed" stroke-width="1.1" stroke-dasharray="15 6 3 5"/>
              <ellipse cx="54" cy="49" rx="19" ry="29" transform="rotate(24 54 49)" stroke="#d883bb" stroke-width=".9" stroke-dasharray="8 5"/>
            </g>
            <g class="ae3-polestar-wings">
              <path d="M29 67L12 62L22 52L38 53Z" fill="url(#ae3-polestar-white)" stroke="#dff8ff" stroke-width="1"/>
              <path d="M36 48L22 35L37 31L48 41Z" fill="url(#ae3-polestar-white)" stroke="#dff8ff" stroke-width="1"/>
              <path d="M64 36L68 19L81 24L75 42Z" fill="url(#ae3-polestar-white)" stroke="#dff8ff" stroke-width="1"/>
              <path d="M70 48L89 43L91 57L73 61Z" fill="url(#ae3-polestar-white)" stroke="#dff8ff" stroke-width="1"/>
            </g>
            <g class="ae3-polestar-blade">
              <path d="M18 83L32 61L67 23L87 9L77 29L42 70L28 91Z" fill="url(#ae3-polestar-white)" stroke="#eefcff" stroke-width="1.2"/>
              <path d="M26 82L39 64L73 25L82 17L74 33L41 73L32 87Z" fill="#112440"/>
              <path class="ae3-polestar-edge" d="M31 81L42 66L77 23" fill="none" stroke="url(#ae3-polestar-edge)" stroke-width="3.1" filter="url(#ae3-polestar-glow)"/>
              <path d="M18 83L28 91L22 97L10 90Z" fill="#263b59" stroke="#bdeafb" stroke-width="1"/>
              <path d="M21 80L32 91" stroke="#fbffff" stroke-width="2"/>
            </g>
            <g class="ae3-polestar-node" filter="url(#ae3-polestar-glow)">
              <path d="M36 54L45 43L57 45L62 56L53 66L41 64Z" fill="#172543" stroke="#d9faff" stroke-width="1.2"/>
              <circle cx="49" cy="55" r="7" fill="url(#ae3-polestar-core)"/>
              <path d="M49 47v16M41 55h16" stroke="#fff" stroke-width=".8" opacity=".72"/>
            </g>
            <g class="ae3-polestar-ghost" fill="#e8fbff" filter="url(#ae3-polestar-glow)">
              <path d="M87 17l1.5 3.2l3.2 1.5l-3.2 1.5l-1.5 3.2l-1.5-3.2l-3.2-1.5l3.2-1.5Z"/>
              <circle cx="18" cy="42" r="1.4"/><circle cx="77" cy="70" r="1.2"/>
              <path d="M58 15l1 2.2l2.2 1l-2.2 1l-1 2.2l-1-2.2l-2.2-1l2.2-1Z"/>
            </g>
          </svg>
          <svg class="ae3-everbright-polestar ae3-everbright-polestar-dark ae3-dark-only" viewBox="0 0 100 100" aria-hidden="true">
            <defs>
              <linearGradient id="ae3-tunneler-shell" x1="0" y1="1" x2="1" y2="0">
                <stop offset="0" stop-color="#070d21"/><stop offset=".32" stop-color="#172b59"/>
                <stop offset=".62" stop-color="#546fba"/><stop offset=".82" stop-color="#d8f9ff"/><stop offset="1" stop-color="#ff9ccb"/>
              </linearGradient>
              <linearGradient id="ae3-tunneler-current" x1="0" y1="1" x2="1" y2="0">
                <stop offset="0" stop-color="#8174ff"/><stop offset=".36" stop-color="#ff65ad"/>
                <stop offset=".68" stop-color="#f4faff"/><stop offset="1" stop-color="#56eff7"/>
              </linearGradient>
              <radialGradient id="ae3-tunneler-heart">
                <stop offset="0" stop-color="#fff"/><stop offset=".22" stop-color="#ffcee4"/>
                <stop offset=".54" stop-color="#ff5fac"/><stop offset=".76" stop-color="#6eeaf2"/><stop offset="1" stop-color="#101933"/>
              </radialGradient>
              <filter id="ae3-tunneler-glow" x="-100%" y="-100%" width="300%" height="300%">
                <feGaussianBlur stdDeviation="2.35" result="blur"/><feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge>
              </filter>
            </defs>
            <g class="ae3-tunneler-cage" fill="none" stroke-linecap="round">
              <path d="M12 77C19 43 43 17 79 12M23 89C49 87 77 66 91 34" stroke="#5de8f2" stroke-width="1.1" stroke-dasharray="13 6 2 5"/>
              <path d="M17 31C38 19 70 24 87 51M36 91C58 74 68 44 60 17" stroke="#f064ad" stroke-width=".9" stroke-dasharray="7 6"/>
            </g>
            <g class="ae3-tunneler-blade">
              <path d="M12 87L29 64L58 36L86 10L75 38L48 62L26 94Z" fill="url(#ae3-tunneler-shell)" stroke="#bff8ff" stroke-width="1.15"/>
              <path d="M27 80L38 61L63 38L89 17L73 46L50 67L34 88Z" fill="#090f25" stroke="#536ca7" stroke-width=".8"/>
              <path d="M45 57L65 31L91 11L78 42L58 62Z" fill="url(#ae3-tunneler-shell)" stroke="#f3c5e1" stroke-width=".9"/>
              <path d="M18 82L27 94L20 99L7 91Z" fill="#111c3b" stroke="#8debf3" stroke-width="1"/>
              <path class="ae3-tunneler-current" d="M24 84L41 61L76 28M48 56L83 19" fill="none" stroke="url(#ae3-tunneler-current)" stroke-width="2.7" filter="url(#ae3-tunneler-glow)"/>
            </g>
            <g class="ae3-tunneler-guard">
              <path d="M28 67L14 60L23 50L40 55L36 40L48 31L57 46L69 35L80 43L66 58L76 70L63 78L51 65L42 80Z" fill="#111a38" stroke="url(#ae3-tunneler-current)" stroke-width="1.1"/>
              <path d="M43 50C47 45 54 48 54 53C55 48 63 47 66 52C70 60 57 69 55 71C52 68 39 59 43 50Z" fill="url(#ae3-tunneler-heart)" stroke="#edfbff" stroke-width=".75" filter="url(#ae3-tunneler-glow)"/>
              <path d="M55 48v20M45 58h21" stroke="#fff" stroke-width=".65" opacity=".56"/>
            </g>
            <g class="ae3-tunneler-data" filter="url(#ae3-tunneler-glow)">
              <path d="M84 14h7v3h-4v4h-6v-3h3Z" fill="#72eef6"/>
              <path d="M77 72h8v3h-3v5h-5Z" fill="#ff75b7"/>
              <path d="M20 39h5v2h-2v4h-5v-2h2Z" fill="#77e9f3"/>
              <path d="M71 20l1.5 3.2l3.2 1.5l-3.2 1.5l-1.5 3.2l-1.5-3.2l-3.2-1.5l3.2-1.5Z" fill="#fff5fb"/>
            </g>
          </svg>
          <small class="ae3-light-only">永远的启明星 · 极星长航</small><small class="ae3-dark-only">永远的启明星 · 幽频解放</small>` },
      });

    return context.mountCanonicalTheme({
      namespace: "ae3",
      themeClass: "ae3-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["dawn", "signal", "orbit", "core"],
        projectTone: "phase",
        threadIndex: "thread",
        sections: { "置顶": "星讯置顶", "项目": "远航项目" },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".ae3-weapon-charm");
        positionPanelAboveCards(
          main,
          ".ae3-link-sync",
          [".ae3-task-voyage", ".ae3-task-tunneler"],
          320,
          56,
          40,
        );
      },
    });
  },
  async unmount() {}
});
