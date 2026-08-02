registerTheme({
  async mount(context) {
    const { root, config } = context;
    root.setAttribute("aria-hidden", "true");
    root.setAttribute("data-iuno-signal", "moon-palace-v3");

    const ephemerisHtml = `<span class="iun-ephemeris-axis"></span>
      <span class="iun-ephemeris-phases">${Array.from({ length: 7 }, (_, i) => `<i style="--i:${i}"><b></b></i>`).join("")}</span>
      <span class="iun-ephemeris-disc"><i></i><b>127</b><em>SEPTIMONT</em></span>
      <span class="iun-ephemeris-needle"><i></i></span>
      <span class="iun-ephemeris-dust">${Array.from({ length: 9 }, (_, i) => `<i style="--i:${i}"></i>`).join("")}</span>`;

    context.renderTemplateV1({
      stageClass: "iun-stage",
      hero: {
        tag: "section",
        className: "iun-hero-copy",
        html: `<span class="iun-hero-kicker" data-theme-part="hero-kicker"><i></i><span class="iun-light-only">${config.kickerLight}</span><span class="iun-dark-only">${config.kickerDark}</span></span>
          <h1 class="iun-light-only" data-theme-part="hero-title-light">${config.headingLight}<br><em>${config.headingLightAccent}</em></h1>
          <h1 class="iun-dark-only" data-theme-part="hero-title-dark">${config.headingDark}<br><em>${config.headingDarkAccent}</em></h1>
          <p>${config.subtitle}</p>
          <div class="iun-lunar-ephemeris" data-theme-part="hero-motion" data-iuno-signal="moon-palace-v3" data-iuno-home-fx="lunar-ephemeris-v3" aria-label="${config.motionLabel}">${ephemerisHtml}</div>
          <div class="iun-hero-note" data-theme-part="hero-note"><small>${config.noteLabel}</small><strong class="iun-light-only">${config.noteLight}</strong><strong class="iun-dark-only">${config.noteDark}</strong></div>`,
      },
      identity: {
        tag: "div",
        className: "iun-identity",
        html: `<span data-theme-part="identity-emblem"><svg viewBox="0 0 36 36" aria-hidden="true"><path d="M24 5C12 4 6 12 7 20c1 8 9 13 18 10"/><path d="M24 8c-8 0-13 6-13 12 1 5 6 9 12 8"/><circle cx="12" cy="11" r="2"/><circle cx="8" cy="18" r="2"/><circle cx="12" cy="27" r="2"/></svg></span><div data-theme-part="identity-copy"><b>${config.title}</b><small>${config.status}</small></div><i data-theme-part="identity-status"></i>`,
      },
      taskLeft: { tag: "aside", className: "iun-task-card iun-task-left", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>${config.leftCardTitle}</b><small>${config.leftCardMeta}</small></div>` },
      taskSecondary: { tag: "aside", className: "iun-task-card iun-task-right iun-task-secondary", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>${config.secondaryCardTitle}</b><small>${config.secondaryCardMeta}</small></div>` },
      taskPrimary: { tag: "aside", className: "iun-task-card iun-task-right iun-task-primary", html: `<i data-theme-part="task-card-art"></i><div data-theme-part="task-card-caption"><b>${config.primaryCardTitle}</b><small>${config.primaryCardMeta}</small></div>` },
      memory: {
        tag: "aside",
        className: "iun-memory",
        html: `<small>${config.memoryLabel}</small><p>${config.memory}</p><span data-theme-part="memory-meter" aria-hidden="true">${Array.from({ length: 7 }, (_, i) => `<i style="--n:${i}"></i>`).join("")}</span>`,
      },
      syncPanel: {
        tag: "div",
        className: "iun-tetragon-sync",
        html: `<span class="iun-sync-copy" data-theme-part="sync-copy"><small>${config.syncLabel}</small><b>${config.syncTitle} <strong>${config.syncValue}</strong><em>/${config.syncTotal}</em></b></span>
          <span class="iun-sync-core" data-theme-part="sync-core" data-iuno-sync-fx="septimont-moon-seal-v4" aria-hidden="true"><i></i><b>${config.syncCore}</b><small>${config.syncCoreLabel}</small></span>
          <span class="iun-sync-spectrum" data-theme-part="sync-meter">${Array.from({ length: 9 }, (_, i) => `<i style="--i:${i};--h:${7 + (i % 5) * 4}px"></i>`).join("")}</span>
          <span class="iun-sync-state" data-theme-part="sync-state"><small>${config.syncStateLabel}</small><b><i></i>${config.syncState}</b></span>`,
      },
      composerAccessory: {
        tag: "div",
        className: "iun-moongazer-armlet",
        html: `<svg class="iun-moongazer-sigil" viewBox="0 0 120 120" data-iuno-weapon="annotations-of-all-things-v1" aria-hidden="true">
          <defs>
            <linearGradient id="iun-moongazer-silver" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#6079a7"/><stop offset=".28" stop-color="#c8ddf6"/><stop offset=".55" stop-color="#fff"/><stop offset=".78" stop-color="#adcaf3"/><stop offset="1" stop-color="#526ba5"/></linearGradient>
            <linearGradient id="iun-moongazer-gold" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#836631"/><stop offset=".44" stop-color="#d5bd87"/><stop offset=".7" stop-color="#fff0bd"/><stop offset="1" stop-color="#9e7538"/></linearGradient>
            <radialGradient id="iun-moongazer-core" cx=".35" cy=".28"><stop stop-color="#fff"/><stop offset=".2" stop-color="#d9efff"/><stop offset=".52" stop-color="#6d9eff"/><stop offset=".78" stop-color="#315ec7"/><stop offset="1" stop-color="#101d61"/></radialGradient>
            <linearGradient id="iun-moongazer-cuff" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#18285c"/><stop offset=".42" stop-color="#4e78d6"/><stop offset=".7" stop-color="#bed8ff"/><stop offset="1" stop-color="#202d6c"/></linearGradient>
            <filter id="iun-moongazer-glow" x="-80%" y="-80%" width="260%" height="260%"><feGaussianBlur stdDeviation="1.8" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
          </defs>
          <g class="iun-moongazer-orbits" fill="none" stroke-linecap="round">
            <circle cx="60" cy="53" r="43" stroke="url(#iun-moongazer-silver)" stroke-width="1.3" stroke-dasharray="54 9 5 11"/>
            <ellipse cx="60" cy="53" rx="34" ry="43" transform="rotate(24 60 53)" stroke="url(#iun-moongazer-gold)" stroke-width="1" stroke-dasharray="20 8 3 8"/>
            <path d="M13 54h12m70 0h12M60 5v12" stroke="#dcecff" stroke-width="1.2"/>
          </g>
          <g class="iun-moongazer-frame" filter="url(#iun-moongazer-glow)">
            <path d="M18 61C19 36 34 17 54 12 42 27 40 43 47 58c-7 10-20 12-29 3Z" fill="url(#iun-moongazer-silver)" stroke="url(#iun-moongazer-gold)" stroke-width="1.2"/>
            <path d="M102 61C101 36 86 17 66 12c12 15 14 31 7 46 7 10 20 12 29 3Z" fill="url(#iun-moongazer-silver)" stroke="url(#iun-moongazer-gold)" stroke-width="1.2"/>
            <path d="M25 58c8 2 14 0 18-6M95 58c-8 2-14 0-18-6" fill="none" stroke="#fff" stroke-opacity=".76" stroke-width="1.4"/>
            <path d="M41 62 34 99l13 12 13-34 13 34 13-12-7-37-19 11Z" fill="url(#iun-moongazer-cuff)" stroke="url(#iun-moongazer-silver)" stroke-width="2"/>
            <path d="M42 72 60 86 78 72M40 91l13 8m27-8-13 8" fill="none" stroke="url(#iun-moongazer-gold)" stroke-width="1.7"/>
            <path d="M35 73c-7 3-12 8-15 14 7-2 12-1 17 2M85 73c7 3 12 8 15 14-7-2-12-1-17 2" fill="none" stroke="url(#iun-moongazer-gold)" stroke-width="2" stroke-linecap="round"/>
          </g>
          <g class="iun-moongazer-core" filter="url(#iun-moongazer-glow)">
            <circle cx="60" cy="56" r="24" fill="#09163c" fill-opacity=".9" stroke="url(#iun-moongazer-silver)" stroke-width="2.4"/>
            <circle cx="60" cy="56" r="19" fill="none" stroke="url(#iun-moongazer-gold)" stroke-width="1.3" stroke-dasharray="5 3"/>
            <path d="M59 38c-10 2-16 10-16 19 0 11 9 19 20 19 7 0 12-3 16-8-5 3-10 3-14 1-9-4-12-15-8-23 1-3 3-6 6-8Z" fill="url(#iun-moongazer-silver)"/>
            <path d="m60 45 9 11-9 12-9-12Z" fill="url(#iun-moongazer-core)" stroke="#edf8ff" stroke-width="1.4"/>
            <path d="m60 49 5 7-5 7-5-7Z" fill="none" stroke="#fff1bd" stroke-width="1"/>
          </g>
          <g class="iun-moongazer-laurel" fill="url(#iun-moongazer-gold)">
            <ellipse cx="37" cy="79" rx="2.3" ry="5" transform="rotate(-48 37 79)"/><ellipse cx="31" cy="84" rx="2.1" ry="4.6" transform="rotate(-58 31 84)"/><ellipse cx="83" cy="79" rx="2.3" ry="5" transform="rotate(48 83 79)"/><ellipse cx="89" cy="84" rx="2.1" ry="4.6" transform="rotate(58 89 84)"/>
          </g>
          <g class="iun-moongazer-motes" fill="#dcecff">${[[17,36],[103,36],[21,77],[99,77],[60,8]].map(([cx,cy],i)=>`<circle style="--i:${i}" cx="${cx}" cy="${cy}" r="${i===4?2.1:1.4}"/>`).join("")}</g>
        </svg><small>${config.accessoryLabel}</small>`,
      },
    });

    return context.mountCanonicalTheme({
      namespace: "iun",
      themeClass: "iun-theme",
      templateVersion: "1.0",
      preserveRoot: true,
      adaptiveLayout: true,
      sidebar: {
        palette: ["moonring", "moonbow", "tetragon", "fullmoon"],
        projectTone: "phase",
        threadIndex: "thread",
        sections: { "置顶": config.pinnedSection, "项目": config.projectSection },
      },
      onEnsure({ main, positionComposerAccessory, positionPanelAboveCards }) {
        root.setAttribute("data-iuno-signal", "moon-palace-v3");
        positionComposerAccessory(main, ".iun-moongazer-armlet");
        positionPanelAboveCards(main, ".iun-tetragon-sync", [".iun-task-secondary", ".iun-task-primary"], 320, 56, 40);
      },
    });
  },
  async unmount() {},
});
