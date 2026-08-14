registerTheme({
  async mount(context) {
    const { root, config } = context;
    const copy = (key, fallback) => {
      const value = config?.[key];
      return typeof value === "string" && value.trim() ? value : fallback;
    };
    const cardCopy = {
      leftTitle: copy("leftCardTitle", "星枢校准"),
      leftMeta: copy("leftCardMeta", "NEURAL PROFESSOR / 01"),
      secondaryTitle: copy("secondaryCardTitle", "呢绒梦"),
      secondaryMeta: copy("secondaryCardMeta", "WOOLLEN DREAM / 02"),
      primaryTitle: copy("primaryCardTitle", "宙算仪轨"),
      primaryMeta: copy("primaryCardMeta", "COSMOCALC RITE / 03"),
      memoryLabel: copy("memoryLabel", "星枢记忆档案"),
      memoryLightCode: copy("memoryLightCode", "FS-03"),
      memoryDarkCode: copy("memoryDarkCode", "WD-07"),
      memoryLight: copy("memoryLight", "教授离席片刻，三枚星枢仍在晨光中继续推演，将第一颗星的轨迹收进档案。"),
      memoryDark: copy("memoryDark", "透明伞倚在望远镜旁，呢绒围巾留住雪夜余温；未完成的星图等待她回来。"),
    };
    root.setAttribute("aria-hidden", "true");

    context.renderTemplateV1({
      stageClass: "mny-stage",
      stageDecorations: `<div class="mny-observatory-frame" aria-hidden="true"><i></i><i></i><i></i><i></i></div>
        <div class="mny-stage-snow mny-dark-only" aria-hidden="true">${Array.from({ length: 9 }, (_, index) => `<i style="--mny-snow-index:${index}"></i>`).join("")}</div>`,
      hero: {
        tag: "section",
        className: "mny-hero-copy",
        html: `<span class="mny-hero-kicker" data-theme-part="hero-kicker">
            <i aria-hidden="true"><b></b><b></b><b></b></i>
            <span class="mny-light-only">${config.kickerLight}</span>
            <span class="mny-dark-only">${config.kickerDark}</span>
          </span>
          <h1 class="mny-light-only" data-theme-part="hero-title-light">${config.headingLight}<br><em>${config.headingLightAccent}</em></h1>
          <h1 class="mny-dark-only" data-theme-part="hero-title-dark">${config.headingDark}<br><em>${config.headingDarkAccent}</em></h1>
          <p>${config.subtitle}</p>
          <div class="mny-first-star-engine" data-theme-part="hero-motion" data-mny-home-fx="first-star-trajectory-v4" aria-label="${config.motionLabel}">
            <svg class="mny-first-star-map" viewBox="0 0 620 96" aria-hidden="true">
              <defs>
                <linearGradient id="mny-first-star-line" x1="0" y1="0" x2="1" y2="0">
                  <stop offset="0" stop-color="var(--mny-cyan)"></stop>
                  <stop offset=".48" stop-color="var(--mny-violet)"></stop>
                  <stop offset="1" stop-color="var(--mny-gold)"></stop>
                </linearGradient>
              </defs>
              <path class="mny-first-star-guide" d="M34 52C112 12 184 82 274 47S440 16 584 49"></path>
              <path class="mny-first-star-return" d="M34 60C128 88 206 23 308 57S472 83 584 40"></path>
              <path class="mny-first-star-pulse" d="M34 52C112 12 184 82 274 47S440 16 584 49"></path>
              <g class="mny-first-star-origin" transform="translate(20 27)">
                <path d="M0 22L22 0L44 22L22 44Z"></path>
                <path d="M11 22L22 11L33 22L22 33Z"></path>
              </g>
              <g class="mny-first-star-pivot" transform="translate(273 27)">
                <path d="M0 12L12 0L24 12L12 24Z"></path>
                <path d="M22 12L34 0L46 12L34 24Z"></path>
                <path d="M44 12L56 0L68 12L56 24Z"></path>
                <path class="mny-first-star-spine" d="M12 12H56"></path>
              </g>
              <g class="mny-first-star-terminal" transform="translate(548 20)">
                <circle cx="24" cy="24" r="21"></circle>
                <path class="mny-first-star-cross" d="M24 1V47M1 24H47"></path>
                <path class="mny-first-star-core" d="M24 12L36 24L24 36L12 24Z"></path>
              </g>
            </svg>
            <span class="mny-first-star-label mny-first-star-label-origin"><b>星栈</b><small>ORIGIN</small></span>
            <span class="mny-first-star-label mny-first-star-label-pivot"><b>星枢</b><small>CONVERGENCE</small></span>
            <span class="mny-first-star-label mny-first-star-label-terminal"><b>宙算</b><small>TRAJECTORY</small></span>
          </div>
          <div class="mny-hero-note" data-theme-part="hero-note">
            <small>${config.noteLabel}</small>
            <strong class="mny-light-only">${config.noteLight}</strong>
            <strong class="mny-dark-only">${config.noteDark}</strong>
          </div>`,
      },
      identity: {
        tag: "div",
        className: "mny-identity",
        html: `<span class="mny-identity-starstack" data-theme-part="identity-emblem" aria-hidden="true"><i></i><i></i><i></i><b></b></span>
          <div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div>
          <i data-theme-part="identity-status" aria-hidden="true"></i>`,
      },
      taskLeft: {
        tag: "aside",
        className: "mny-task-card mny-task-card-left",
        html: `<i data-theme-part="task-card-art"></i>
          <div data-theme-part="task-card-caption"><b>${cardCopy.leftTitle}</b><small>${cardCopy.leftMeta}</small></div>`,
      },
      taskSecondary: {
        tag: "aside",
        className: "mny-task-card mny-task-card-right mny-task-card-secondary",
        html: `<i data-theme-part="task-card-art"></i>
          <span class="mny-card-instrument mny-card-instrument-snow" aria-hidden="true"><i></i><i></i><b></b></span>
          <div data-theme-part="task-card-caption"><b>${cardCopy.secondaryTitle}</b><small>${cardCopy.secondaryMeta}</small></div>`,
      },
      taskPrimary: {
        tag: "aside",
        className: "mny-task-card mny-task-card-right mny-task-card-primary",
        html: `<i data-theme-part="task-card-art"></i>
          <span class="mny-card-instrument mny-card-instrument-blade" aria-hidden="true"><i></i><b></b><em></em></span>
          <div data-theme-part="task-card-caption"><b>${cardCopy.primaryTitle}</b><small>${cardCopy.primaryMeta}</small></div>`,
      },
      memory: {
        tag: "aside",
        className: "mny-memory",
        html: `<small class="mny-memory-heading">
            <i aria-hidden="true"></i><span>${cardCopy.memoryLabel}</span>
            <b class="mny-light-only">${cardCopy.memoryLightCode}</b>
            <b class="mny-dark-only">${cardCopy.memoryDarkCode}</b>
          </small>
          <p><span class="mny-light-only">${cardCopy.memoryLight}</span><span class="mny-dark-only">${cardCopy.memoryDark}</span></p>
          <span class="mny-memory-observatory" data-theme-part="memory-meter" aria-label="莫宁星枢演算记忆档案">
            <span class="mny-memory-prism" aria-hidden="true"><i></i><i></i><i></i><b></b></span>
            <span class="mny-memory-reading">
              <b class="mny-light-only">FIRST STAR</b><b class="mny-dark-only">WOOLLEN DREAM</b>
              <small>ARCHIVE · 97.8</small>
            </span>
            <span class="mny-memory-phases" aria-hidden="true">${Array.from({ length: 5 }, (_, index) => `<i style="--mny-memory-index:${index}"></i>`).join("")}</span>
          </span>`,
      },
      syncPanel: {
        tag: "div",
        className: "mny-sync-panel",
        html: `<span class="mny-sync-copy" data-theme-part="sync-copy">
            <small>${config.syncLabel}</small>
            <b>${config.syncTitle} <strong>${config.syncValue}</strong><em>/${config.syncTotal}</em></b>
          </span>
          <span class="mny-sync-core" data-theme-part="sync-core" aria-label="改良星栈三节点演算核心">
            <svg viewBox="0 0 36 36" aria-hidden="true">
              <path d="M18 3L23 8L18 13L13 8Z"></path>
              <path d="M18 13L24 19L18 25L12 19Z"></path>
              <path d="M18 25L22 29L18 33L14 29Z"></path>
              <path class="mny-sync-link" d="M18 8V29M8 18H28"></path>
            </svg>
            <b>${config.syncCore}</b><small>${config.syncCoreLabel}</small>
          </span>
          <span class="mny-sync-meter" data-theme-part="sync-meter" aria-label="双路义肢能量收敛">
            ${Array.from({ length: 9 }, (_, index) => `<i style="--mny-index:${index};--mny-level:${10 + (index % 5) * 4}px"></i>`).join("")}
          </span>
          <span class="mny-sync-state" data-theme-part="sync-state"><small>${config.syncStateLabel}</small><b><i></i>${config.syncState}</b></span>`,
      },
      composerAccessory: {
        tag: "div",
        className: "mny-composer-accessory",
        html: `<svg class="mny-celestial-index" data-mny-accessory="starstack-index-v2" viewBox="0 0 96 96" aria-hidden="true">
            <defs>
              <linearGradient id="mny-index-shell" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#252d51"></stop><stop offset=".3" stop-color="#707eae"></stop><stop offset=".58" stop-color="#eef4f8"></stop><stop offset=".82" stop-color="#c4b4e6"></stop><stop offset="1" stop-color="#65558d"></stop></linearGradient>
              <linearGradient id="mny-index-face" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#11172d"></stop><stop offset=".46" stop-color="#34446b"></stop><stop offset=".76" stop-color="#8d9ac5"></stop><stop offset="1" stop-color="#d8e5ee"></stop></linearGradient>
              <linearGradient id="mny-index-current" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#efc77e"></stop><stop offset=".48" stop-color="#fff7d7"></stop><stop offset="1" stop-color="#7edce5"></stop></linearGradient>
              <linearGradient id="mny-index-lens" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#ffffff" stop-opacity=".62"></stop><stop offset=".42" stop-color="#8adce6" stop-opacity=".2"></stop><stop offset="1" stop-color="#aa8ee8" stop-opacity=".5"></stop></linearGradient>
              <filter id="mny-index-glow" x="-90%" y="-90%" width="280%" height="280%"><feGaussianBlur stdDeviation="1.35" result="blur"></feGaussianBlur><feMerge><feMergeNode in="blur"></feMergeNode><feMergeNode in="SourceGraphic"></feMergeNode></feMerge></filter>
            </defs>
            <g class="mny-index-shadow">
              <path d="M36 72L39 54L41 31L56 7L73 10L68 31L72 49L60 68L52 73L39 94L25 88Z"></path>
            </g>
            <g class="mny-index-weapon">
              <path class="mny-index-shell" d="M37 68L40 53L42 29L57 6L74 10L69 31L73 48L60 68L50 74Z"></path>
              <path class="mny-index-face" d="M47 58L49 33L59 16L67 16L64 31L67 46L57 59Z"></path>
              <path class="mny-index-cut" d="M57 11L65 10L61 23L55 33L50 33L51 25Z"></path>
              <path class="mny-index-spine" d="M42 61L45 31L57 10M49 64L60 60L69 45"></path>
              <path class="mny-index-current" d="M53 58L55 35L65 18"></path>
            </g>
            <g class="mny-index-observer">
              <path class="mny-index-lens" d="M62 26L88 35L85 49L64 50L69 39Z"></path>
              <path class="mny-index-lens-grid" d="M67 31L83 37M66 38L85 42M66 45L82 45M73 31L70 47M80 34L77 47"></path>
              <path class="mny-index-scan" d="M67 37L84 41"></path>
            </g>
            <g class="mny-index-starstack" filter="url(#mny-index-glow)">
              <path class="mny-index-rail" d="M39 35L36 58"></path>
              <path d="M39 30L44 35L39 40L34 35Z"></path>
              <path d="M38 41L43 46L38 51L33 46Z"></path>
              <path d="M37 52L42 57L37 62L32 57Z"></path>
            </g>
            <g class="mny-index-yoke">
              <path class="mny-index-guard" d="M26 68L40 63L51 70L68 65L64 77L50 76L38 82Z"></path>
              <path class="mny-index-grip" d="M39 76L50 79L37 95L25 89Z"></path>
              <path class="mny-index-grip-line" d="M32 84L43 87M29 88L39 91"></path>
            </g>
            <g class="mny-index-glyphs" fill="none" stroke-linecap="square">
              <path d="M18 65V56H26M18 78V87H27M77 59H86V51" stroke="#efc77e"></path>
              <path d="M79 24L84 19M83 24L88 19" stroke="#7edce5"></path>
            </g>
          </svg>
          <small>${config.accessoryLabel} · 星栈校准</small>`,
      },
    });

    return context.mountCanonicalTheme({
      namespace: "mny",
      themeClass: "mny-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["lilac", "glass", "star", "amber"],
        projectTone: "observatory",
        threadIndex: "calculation",
        sections: { "置顶": config.pinnedSection, "项目": config.projectSection },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".mny-composer-accessory");
        positionPanelAboveCards(
          main,
          ".mny-sync-panel",
          [".mny-task-card-secondary", ".mny-task-card-primary"],
          320,
          56,
          40,
        );
      },
    });
  },
  async unmount() {},
});
