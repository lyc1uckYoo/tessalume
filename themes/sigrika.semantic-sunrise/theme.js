registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");

    const semanticTabs = Array.from({ length: 6 }, (_, index) => `
      <i class="sgk-semantic-tab" style="--tab:${index}">
        <span></span><b>${String(index + 1).padStart(2, "0")}</b>
      </i>`).join("");

    context.renderTemplateV1({
      stageClass: "sgk-stage",
      hero: {
        tag: "section",
        className: "sgk-hero-copy",
        html: `<span class="sgk-hero-kicker" data-theme-part="hero-kicker">
            <i></i>
            <span class="sgk-light-only">STARTORCH ACADEMY · ROYA SEMANTICS</span>
            <span class="sgk-dark-only">NIGHT ARCHIVE · ANSWER PROTOCOL</span>
          </span>
          <h1 class="sgk-light-only" data-theme-part="hero-title-light">回应期望<br><em>译作昭日</em></h1>
          <h1 class="sgk-dark-only" data-theme-part="hero-title-dark">循问入夜<br><em>以句作答</em></h1>
          <p>${config.subtitle}</p>
          <div class="sgk-semantic-engine" data-theme-part="hero-motion" aria-label="期望、答问与句点组成的罗伊语义译链">
            <svg class="sgk-semantic-script" viewBox="0 0 620 96" aria-hidden="true">
              <defs>
                <linearGradient id="sgk-script-light" x1="0" y1="0" x2="1" y2="0">
                  <stop stop-color="#f19a39"/><stop offset=".48" stop-color="#73dcd2"/><stop offset="1" stop-color="#8e62dc"/>
                </linearGradient>
                <filter id="sgk-script-glow"><feGaussianBlur stdDeviation="2.2" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
              </defs>
              <path class="sgk-script-guide" d="M30 54C94 9 154 88 222 45S348 20 402 53S496 81 582 35"/>
              <path class="sgk-script-pulse" d="M30 54C94 9 154 88 222 45S348 20 402 53S496 81 582 35"/>
              <g class="sgk-script-shard sgk-script-expect" transform="translate(27 31)">
                <path d="M0 24L19 0L42 8L34 38L11 47Z"/><path d="M10 25L20 11L31 15L27 31L16 36Z"/>
              </g>
              <g class="sgk-script-shard sgk-script-answer" transform="translate(392 29)">
                <path d="M0 8L23 0L43 22L30 46L7 38Z"/><path d="M11 13L23 10L33 23L27 35L15 31Z"/>
              </g>
              <g class="sgk-script-period" transform="translate(564 18)" filter="url(#sgk-script-glow)">
                <path d="M20 0L24 14L38 19L25 25L21 40L15 27L0 22L14 16Z"/><path d="M20 12L28 21L20 30L12 21Z"/>
              </g>
            </svg>
            <span class="sgk-semantic-label sgk-label-expect"><b>期望</b><small>EXPECTATION</small></span>
            <span class="sgk-semantic-label sgk-label-answer"><b>答问</b><small>RESPONSE</small></span>
            <span class="sgk-semantic-label sgk-label-period"><b>句点</b><small>SEMANTIC SUN</small></span>
            <span class="sgk-soliskin-trace" aria-hidden="true">
              <svg viewBox="0 0 64 52"><path d="M23 9L17 0L13 17C4 23 2 36 9 44C16 52 32 53 42 46C49 41 51 31 47 23L55 8L43 14C37 9 30 7 23 9Z"/><path d="M21 27C25 20 35 20 39 27C35 34 25 34 21 27Z"/><path d="M28 23L32 27L28 31L24 27Z"/></svg>
            </span>
          </div>
          <div class="sgk-hero-note" data-theme-part="hero-note">
            <small>ROYA · SEMANTIC ENTRY</small>
            <strong class="sgk-light-only">期望已接收 · 日灵正在译注</strong>
            <strong class="sgk-dark-only">疑问已定位 · 答案等待显化</strong>
          </div>`
      },
      identity: {
        tag: "div",
        className: "sgk-identity",
        html: `<span class="sgk-identity-emblem" data-theme-part="identity-emblem">
            <svg viewBox="0 0 44 44" aria-hidden="true"><path class="sgk-emblem-wing" d="M4 22L13 7L20 17L14 28ZM40 22L31 7L24 17L30 28Z"/><path class="sgk-emblem-face" d="M15 18L22 12L29 18L27 30L22 35L17 30Z"/><path class="sgk-emblem-point" d="M22 18L26 23L22 29L18 23Z"/></svg>
          </span>
          <div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div>
          <i data-theme-part="identity-status"></i>`
      },
      taskLeft: {
        tag: "aside",
        className: "sgk-task-card sgk-task-card-left",
        html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>初译 · 倾听</b><small>SEMANTIC ENTRY / 01</small></div>`
      },
      taskSecondary: {
        tag: "aside",
        className: "sgk-task-card sgk-task-card-right sgk-task-card-secondary",
        html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>答问 · 夜林</b><small>QUESTION FIELD / 02</small></div>`
      },
      taskPrimary: {
        tag: "aside",
        className: "sgk-task-card sgk-task-card-right sgk-task-card-primary",
        html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>期望 · 昭日</b><small>EXPECTATION / 03</small></div>`
      },
      memory: {
        tag: "aside",
        className: "sgk-memory",
        html: `<small>星炬语义档案 · SGK-06</small><p>${config.memory}</p>
          <span class="sgk-memory-instrument" data-theme-part="memory-meter" aria-label="罗伊译注进度">
            <svg viewBox="0 0 116 32" aria-hidden="true">
              <path class="sgk-memory-page" d="M6 6Q30 1 54 10V28Q30 19 6 24ZM110 6Q86 1 62 10V28Q86 19 110 24Z"/>
              <path class="sgk-memory-spine" d="M58 8V29"/>
              <path class="sgk-memory-phrase" d="M15 11L30 8L43 12M15 17L27 15L46 19M101 11L86 8L73 12M101 17L89 15L70 19"/>
              <path class="sgk-memory-period" d="M58 0L63 6L58 12L53 6Z"/>
            </svg>
            <i style="--note:0"></i><i style="--note:1"></i><i style="--note:2"></i><i style="--note:3"></i><i style="--note:4"></i><i style="--note:5"></i>
          </span>`
      },
      syncPanel: {
        tag: "div",
        className: "sgk-roya-console",
        html: `<span class="sgk-sync-copy" data-theme-part="sync-copy"><small>罗伊语义祝福 · 日灵协译</small><b>译注完成 <strong>6</strong><em>/6</em></b></span>
          <span class="sgk-sync-core" data-theme-part="sync-core">
            <svg viewBox="0 0 54 54" aria-hidden="true"><path class="sgk-core-ear" d="M9 18L15 3L21 17M45 18L39 3L33 17"/><path class="sgk-core-body" d="M14 19L27 11L40 19L37 38L27 47L17 38Z"/><path class="sgk-core-glyph" d="M20 28L27 20L34 28L27 37Z"/></svg>
            <b>句</b><small>昭日</small>
          </span>
          <span class="sgk-sync-meter" data-theme-part="sync-meter">${semanticTabs}</span>
          <span class="sgk-sync-state" data-theme-part="sync-state"><small>语义状态</small><b><i></i>可显化</b></span>`
      },
      composerAccessory: {
        tag: "div",
        className: "sgk-roya-gauntlet",
        html: `<svg viewBox="0 0 104 104" aria-hidden="true">
            <defs>
              <linearGradient id="sgk-gauntlet-metal" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#5a397e"/><stop offset=".45" stop-color="#d7c9f0"/><stop offset=".7" stop-color="#fff9e8"/><stop offset="1" stop-color="#8e61c7"/></linearGradient>
              <linearGradient id="sgk-gauntlet-rune" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#63d9d0"/><stop offset=".55" stop-color="#ffd37a"/><stop offset="1" stop-color="#f08b36"/></linearGradient>
              <filter id="sgk-gauntlet-glow"><feGaussianBlur stdDeviation="1.6" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
            </defs>
            <g class="sgk-gauntlet-bracer"><path d="M31 40L50 25L72 37L68 72L45 84L25 67Z" fill="url(#sgk-gauntlet-metal)"/><path d="M35 44L50 33L63 41L59 64L45 73L33 63Z"/></g>
            <g class="sgk-gauntlet-blades" fill="url(#sgk-gauntlet-rune)" filter="url(#sgk-gauntlet-glow)"><path d="M22 58L5 48L12 73L29 79Z"/><path d="M69 40L94 22L84 51L69 59Z"/><path d="M63 72L91 82L68 92L52 82Z"/></g>
            <g class="sgk-gauntlet-script" fill="none" stroke="url(#sgk-gauntlet-rune)" stroke-linecap="round"><path d="M13 50Q43 4 88 25"/><path d="M20 78Q52 98 88 82"/><path d="M19 63L32 56L43 65L55 49L71 54"/></g>
            <path class="sgk-gauntlet-period" d="M49 42L57 51L49 61L40 51Z" fill="url(#sgk-gauntlet-rune)" filter="url(#sgk-gauntlet-glow)"/>
          </svg><small>罗伊 · 译注臂铠</small>`
      }
    });

    return context.mountCanonicalTheme({
      namespace: "sgk",
      themeClass: "sgk-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["expectation", "answer", "soliskin", "period"],
        projectTone: "semantic",
        threadIndex: "entry",
        sections: { "置顶": "昭日译注", "项目": "星炬语义档案" }
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".sgk-roya-gauntlet");
        positionPanelAboveCards(
          main,
          ".sgk-roya-console",
          [".sgk-task-card-secondary", ".sgk-task-card-primary"],
          320,
          56,
          40
        );
      }
    });
  },
  async unmount() {}
});
