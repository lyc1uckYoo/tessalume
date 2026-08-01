registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");

    context.renderTemplateV1({
      stageClass: "cthy-stage",
      hero: {
        tag: "section",
        className: "cthy-hero-copy",
        html: `<span class="cthy-kicker" data-theme-part="hero-kicker">
          <i></i>
          <span class="cthy-light-only">${config.kickerLight}</span>
          <span class="cthy-dark-only">${config.kickerDark}</span>
        </span>
        <h1 class="cthy-light-only" data-theme-part="hero-title-light">${config.headingLight}<br><em>${config.headingLightAccent}</em></h1>
        <h1 class="cthy-dark-only" data-theme-part="hero-title-dark">${config.headingDark}<br><em>${config.headingDarkAccent}</em></h1>
        <p>${config.subtitle}</p>
        <div class="cthy-gale-crown" data-theme-part="hero-motion" data-cthy-signal="dual-crown-v2" aria-label="${config.motionLabel}">
          <span class="cthy-home-form cthy-home-form-light cthy-light-only" data-cthy-home-fx="wandering-triad-v2">
            <svg viewBox="0 0 300 72" aria-hidden="true">
              <defs><linearGradient id="cthy-home-day-blade" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#2459ad"/><stop offset=".36" stop-color="#8bc8f4"/><stop offset=".54" stop-color="#fff"/><stop offset=".75" stop-color="#d8b866"/><stop offset="1" stop-color="#3d73d2"/></linearGradient><filter id="cthy-home-day-glow" x="-50%" y="-120%" width="200%" height="340%"><feGaussianBlur stdDeviation="1.4" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs>
              <g class="cthy-home-currents" fill="none" stroke-linecap="round"><path d="M6 47C48 14 87 65 132 35S221 15 294 42"/><path d="M4 55C55 30 92 69 145 44s91-24 151-7"/><path d="M28 26C72 8 107 23 139 39 179 59 229 57 280 23"/></g>
              <g class="cthy-home-day-orbits" fill="none"><ellipse cx="150" cy="38" rx="75" ry="20"/><ellipse cx="150" cy="38" rx="108" ry="28" stroke-dasharray="3 9"/><path d="M45 38h24m162 0h24M150 4v10m0 48v7"/></g>
              <g class="cthy-home-triad" fill="url(#cthy-home-day-blade)" stroke="#3264ba" stroke-width=".7" filter="url(#cthy-home-day-glow)"><path style="--i:0" d="m150 2 5 31-5 15-5-15Z"/><path style="--i:1" d="m102 11 25 25 3 14-12-8-23-25Z"/><path style="--i:2" d="m198 11-25 25-3 14 12-8 23-25Z"/></g>
              <g class="cthy-home-day-guard" fill="none" stroke="url(#cthy-home-day-blade)" stroke-linecap="round"><path d="M124 48c10-9 18-10 26-2 8-8 16-7 26 2"/><path d="m132 49 7 9 11-7 11 7 7-9"/></g>
              <g class="cthy-home-motes" fill="#5da4e8" filter="url(#cthy-home-day-glow)"><path style="--i:0" d="M57 12c8-3 13-1 15 5-8 3-13 1-15-5Z"/><path style="--i:1" d="M227 14c3-7 8-9 15-5-3 7-8 9-15 5Z"/><path style="--i:2" d="M82 57c6-5 11-4 15 1-6 5-11 4-15-1Z"/><circle style="--i:3" cx="244" cy="54" r="2"/></g>
            </svg>
          </span>
          <span class="cthy-home-form cthy-home-form-dark cthy-dark-only" data-cthy-home-fx="fleurdelys-tide-array-v2">
            <svg viewBox="0 0 300 72" aria-hidden="true">
              <defs><linearGradient id="cthy-home-night-blade" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#070d29"/><stop offset=".3" stop-color="#315dc9"/><stop offset=".55" stop-color="#b7e9ff"/><stop offset=".75" stop-color="#7060d1"/><stop offset="1" stop-color="#17204f"/></linearGradient><filter id="cthy-home-night-glow" x="-60%" y="-130%" width="220%" height="360%"><feGaussianBlur stdDeviation="1.8" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs>
              <g class="cthy-home-night-tide" fill="none" stroke-linecap="round"><path d="M2 51C42 16 82 65 126 35s94-17 171 7"/><path d="M4 60C54 37 93 68 144 47c48-20 91-25 153-10"/><path d="M24 28c52-28 90-8 123 10 41 22 89 22 136-11"/></g>
              <g class="cthy-home-thorn-halo" fill="none" stroke="url(#cthy-home-night-blade)" filter="url(#cthy-home-night-glow)"><ellipse cx="150" cy="37" rx="78" ry="23"/><path d="M70 37l9-4 4-10 8 8 12-5 4 10M230 37l-9-4-4-10-8 8-12-5-4 10M105 16l7 2 5-8 6 8M195 16l-7 2-5-8-6 8"/><ellipse cx="150" cy="37" rx="112" ry="30" stroke-dasharray="2 8"/></g>
              <g class="cthy-home-night-triad" fill="url(#cthy-home-night-blade)" stroke="#88d2ff" stroke-width=".7" filter="url(#cthy-home-night-glow)"><path style="--i:0" d="m150 0 6 34-6 17-6-17Z"/><path style="--i:1" d="m97 8 28 28 4 17-14-10-25-28Z"/><path style="--i:2" d="m203 8-28 28-4 17 14-10 25-28Z"/></g>
              <g class="cthy-home-night-gems" fill="#85ceff" filter="url(#cthy-home-night-glow)"><path style="--i:0" d="m44 20 3 6 6 3-6 3-3 6-3-6-6-3 6-3Z"/><path style="--i:1" d="m252 17 2 5 5 2-5 2-2 5-2-5-5-2 5-2Z"/><circle style="--i:2" cx="73" cy="57" r="2.4"/><circle style="--i:3" cx="232" cy="57" r="2.4"/></g>
            </svg>
          </span>
        </div>
        <div class="cthy-form-note" data-theme-part="hero-note">
          <small>${config.noteLabel}</small>
          <strong class="cthy-light-only">${config.noteLight}</strong>
          <strong class="cthy-dark-only">${config.noteDark}</strong>
        </div>`,
      },
      identity: {
        tag: "div",
        className: "cthy-identity",
        html: `<span data-theme-part="identity-emblem"><svg viewBox="0 0 34 34" aria-hidden="true"><path d="M4 18C8 8 13 7 17 15C21 7 26 8 30 18M6 19l4-1 2-5 3 4 2-2 2 2 3-4 2 5 4 1M17 7v20M13 25h8"/></svg></span>
        <div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div>
        <i data-theme-part="identity-status"></i>`,
      },
      taskLeft: {
        tag: "aside",
        className: "cthy-task-card cthy-task-left",
        html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>${config.leftCardTitle}</b><small>${config.leftCardMeta}</small></div>`,
      },
      taskSecondary: {
        tag: "aside",
        className: "cthy-task-card cthy-task-right cthy-task-cartethyia",
        html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>${config.secondaryCardTitle}</b><small>${config.secondaryCardMeta}</small></div>`,
      },
      taskPrimary: {
        tag: "aside",
        className: "cthy-task-card cthy-task-right cthy-task-fleurdelys",
        html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>${config.primaryCardTitle}</b><small>${config.primaryCardMeta}</small></div>`,
      },
      memory: {
        tag: "aside",
        className: "cthy-memory",
        html: `<small>${config.memoryLabel}</small><p>${config.memory}</p>
        <span class="cthy-memory-crown" data-theme-part="memory-meter" aria-hidden="true">
          <svg viewBox="0 0 124 32"><path class="cthy-memory-tide" d="M3 23c14-8 24-8 36 0 12 8 24 8 37 0 13-8 27-8 45 0"/><path class="cthy-memory-thorn" d="M19 19c9-13 18-12 25-2 6 8 12 8 18-1 7 10 14 10 20 1 7-10 16-11 24 2"/><path class="cthy-memory-blade" d="m62 3 3 12-3 8-3-8Z"/>${Array.from({ length: 7 }, (_, i) => `<circle cx="${20 + i * 14}" cy="25" r="1.8" style="--i:${i}"/>`).join("")}</svg>
        </span>`,
      },
      syncPanel: {
        tag: "div",
        className: "cthy-triad-panel",
        html: `<span class="cthy-triad-copy" data-theme-part="sync-copy"><small>${config.syncLabel}</small><b>${config.syncTitle} <strong>${config.syncValue}</strong><em>/${config.syncTotal}</em></b></span>
        <span class="cthy-triad-core" data-theme-part="sync-core" data-cthy-sync-fx="dual-crown-tide-v2" aria-hidden="true"><svg viewBox="0 0 48 48"><g class="cthy-sync-halo" fill="none"><circle cx="24" cy="24" r="20"/><circle cx="24" cy="24" r="15" stroke-dasharray="3 4"/><path d="M4 25h7m26 0h7M24 4v7m0 26v7"/></g><g class="cthy-sync-blades"><path style="--i:0" d="m24 5 3 17-3 8-3-8Z"/><path style="--i:1" d="m9 14 12 10 2 7-7-3-11-9Z"/><path style="--i:2" d="m39 14-12 10-2 7 7-3 11-9Z"/></g><circle class="cthy-sync-gem" cx="24" cy="31" r="3"/></svg><b>${config.syncCore}</b><small>${config.syncCoreLabel}</small></span>
        <span class="cthy-triad-meter" data-theme-part="sync-meter">${Array.from({ length: 11 }, (_, i) => `<i style="--i:${i};--h:${9 + ((i * 5) % 16)}px"><b></b></i>`).join("")}</span>
        <span class="cthy-triad-state" data-theme-part="sync-state"><small>${config.syncStateLabel}</small><b><i></i>${config.syncState}</b></span>`,
      },
      composerAccessory: {
        tag: "div",
        className: "cthy-crown-charm",
        html: `<svg class="cthy-charm-light cthy-light-only" viewBox="0 0 120 120" aria-hidden="true">
          <defs><linearGradient id="cthy-weapon-day-blade" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#2868c7"/><stop offset=".36" stop-color="#a8dcff"/><stop offset=".56" stop-color="#ffffff"/><stop offset=".76" stop-color="#78bdf4"/><stop offset="1" stop-color="#315fb4"/></linearGradient><linearGradient id="cthy-weapon-day-gold" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#8e682d"/><stop offset=".5" stop-color="#fff0bb"/><stop offset="1" stop-color="#b98a3e"/></linearGradient><filter id="cthy-weapon-day-glow" x="-70%" y="-70%" width="240%" height="240%"><feGaussianBlur stdDeviation="1.8" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs>
          <g stroke-linecap="round" stroke-linejoin="round"><path class="cthy-weapon-blade" d="M35 89L73 29 91 8 81 34 43 94Z" fill="url(#cthy-weapon-day-blade)" stroke="#315fae" stroke-width="1.2" filter="url(#cthy-weapon-day-glow)"/><path d="M40 88L79 31 86 18M43 91L77 39" fill="none" stroke="#fff" stroke-width="1.15" opacity=".88"/><path d="M17 77c9 1 16 5 21 12 7-6 15-8 23-5-4 6-10 10-18 11-8-1-15-6-26-18Z" fill="none" stroke="url(#cthy-weapon-day-gold)" stroke-width="3"/><path d="M22 82l9-1 5 8M45 92l8-5 5 1" fill="none" stroke="#5d8bd1" stroke-width="1.4"/><path d="M36 92L21 109" fill="none" stroke="url(#cthy-weapon-day-gold)" stroke-width="5"/><path d="M34 95l-10 11" fill="none" stroke="#244f9d" stroke-width="1.7"/><path d="M14 111l7-7 7 7-7 7Z" fill="#2f77cf" stroke="url(#cthy-weapon-day-gold)" stroke-width="2" filter="url(#cthy-weapon-day-glow)"/><path class="cthy-weapon-ribbon" d="M28 96C12 92 8 101 4 111M32 96c-3 13 7 17 15 19" fill="none" stroke="#4b96e7" stroke-width="2"/><path d="M65 42l4-9 3 6 7-3M49 68l5-8 2 6 7-2" fill="none" stroke="url(#cthy-weapon-day-gold)" stroke-width="1.5"/></g>
        </svg>
        <svg class="cthy-charm-dark cthy-dark-only" viewBox="0 0 120 120" aria-hidden="true">
          <defs><linearGradient id="cthy-weapon-night-blade" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#070d28"/><stop offset=".3" stop-color="#2858c8"/><stop offset=".56" stop-color="#b9e8ff"/><stop offset=".72" stop-color="#5c73e4"/><stop offset="1" stop-color="#161a55"/></linearGradient><linearGradient id="cthy-weapon-night-thorn" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#1c2d78"/><stop offset=".5" stop-color="#8dd6ff"/><stop offset="1" stop-color="#7e5bc7"/></linearGradient><filter id="cthy-weapon-night-glow" x="-80%" y="-80%" width="260%" height="260%"><feGaussianBlur stdDeviation="2.2" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs>
          <g stroke-linecap="round" stroke-linejoin="round"><path class="cthy-weapon-blade" d="M33 92L71 32 94 5 83 37 42 98Z" fill="url(#cthy-weapon-night-blade)" stroke="#77bfff" stroke-width="1.2" filter="url(#cthy-weapon-night-glow)"/><path d="M39 91L81 32 89 16M43 94L78 41" fill="none" stroke="#d8f3ff" stroke-width="1.1" opacity=".82"/><path d="M13 74c11 0 18 5 25 17 8-10 17-13 29-10-5 8-13 14-25 17-10-2-19-9-29-24Z" fill="none" stroke="url(#cthy-weapon-night-thorn)" stroke-width="3.4" filter="url(#cthy-weapon-night-glow)"/><path d="M16 76l3 10 8-6 4 10M47 94l7-9 5 7 8-10" fill="none" stroke="#7ebeff" stroke-width="1.5"/><path d="M36 96L20 111" fill="none" stroke="url(#cthy-weapon-night-thorn)" stroke-width="5.4"/><path d="M13 112l7-8 8 8-8 7Z" fill="#192d84" stroke="#8fd8ff" stroke-width="2" filter="url(#cthy-weapon-night-glow)"/><path class="cthy-weapon-ribbon" d="M27 99C9 94 6 104 2 115M32 99c-2 14 9 17 18 18" fill="none" stroke="#617dea" stroke-width="2.2"/><path d="M64 48l5-12 4 7 8-5M51 70l4-10 3 6 8-4M77 29l3-9 4 5 7-4" fill="none" stroke="#86cfff" stroke-width="1.6"/></g>
        </svg>
        <small>${config.accessoryLabel}</small>`,
      },
    });

    return context.mountCanonicalTheme({
      namespace: "cthy",
      themeClass: "cthy-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["gale", "iris", "tide", "crown"],
        projectTone: "phase",
        threadIndex: "thread",
        sections: { "置顶": config.pinnedSection, "项目": config.projectSection },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        positionComposerAccessory(main, ".cthy-crown-charm");
        positionPanelAboveCards(
          main,
          ".cthy-triad-panel",
          [".cthy-task-cartethyia", ".cthy-task-fleurdelys"],
          320,
          56,
          40,
        );
      },
    });
  },
  async unmount() {},
});
