registerTheme({
  async mount(context) {
    const { document, window, root, config } = context;
    const html = document.documentElement;
    let disposed = false;
    let timer = 0;
    const marked = [];

    html.classList.add("ae3-theme");
    root.setAttribute("aria-hidden", "true");
    root.innerHTML = `
      <div class="ae3-stage">
        <section class="ae3-hero-copy">
          <span class="ae3-kicker"><i></i><span class="ae3-light-only">STARTORCH · DEPARTURE GATE 07</span><span class="ae3-dark-only">TUNNELER · CORE LINK RA2362-G</span></span>
          <h1 class="ae3-light-only">把告别折成<br><em>新的航标</em></h1>
          <h1 class="ae3-dark-only">越过深空<br><em>回应星海</em></h1>
          <p>${config.subtitle}</p>
          <div class="ae3-route"><i></i><i></i><i></i><i></i><b></b></div>
          <div class="ae3-mode"><small class="ae3-light-only">晨航模式</small><small class="ae3-dark-only">兵装链接</small><strong class="ae3-light-only">纸飞机已进入远航轨道</strong><strong class="ae3-dark-only">隧者核心同步完成</strong></div>
        </section>
        <div class="ae3-identity"><span><i></i></span><div><b>${config.title}</b><small>${config.status}</small></div><em></em></div>
        <aside class="ae3-task-card ae3-task-left"><i></i><div><b>星讯 · 留声</b><small>FAREWELL SIGNAL / 01</small></div></aside>
        <aside class="ae3-task-card ae3-task-right ae3-task-voyage"><i></i><div><b>晨光 · 远航</b><small>VOYAGE ROUTE / 02</small></div></aside>
        <aside class="ae3-task-card ae3-task-right ae3-task-tunneler"><i></i><div><b>隧者 · 同调</b><small>CORE LINK / 03</small></div></aside>
        <aside class="ae3-memory"><small>RA2362-G · 航行记忆</small><p>${config.memory}</p><span class="ae3-heart-resonator"><svg viewBox="0 0 122 32" aria-hidden="true"><defs><linearGradient id="ae3-memory-heart-core" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ffd5e9"/><stop offset=".36" stop-color="#ff75b7"/><stop offset=".72" stop-color="#f33c98"/><stop offset="1" stop-color="#a93689"/></linearGradient><linearGradient id="ae3-memory-heart-frame" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ffffff"/><stop offset=".34" stop-color="#bffaff"/><stop offset=".7" stop-color="#57d9e9"/><stop offset="1" stop-color="#fff2c2"/></linearGradient><radialGradient id="ae3-memory-heart-glow"><stop offset="0" stop-color="#ff8fc4" stop-opacity=".8"/><stop offset="1" stop-color="#ff68ae" stop-opacity="0"/></radialGradient></defs><path class="ae3-heart-trace ae3-heart-trace-left" d="M2 18h22l7-6h13"/><path class="ae3-heart-trace ae3-heart-trace-right" d="M120 18H98l-7-6H78"/><ellipse class="ae3-heart-halo" cx="61" cy="17" rx="25" ry="11"/><path class="ae3-heart-armor" d="M61 30L45 21l-3-9 7-7 8 4 4 5 4-5 8-4 7 7-3 9Z"/><path class="ae3-heart-crown" d="M48 8l-4-6 10 4 7-5 7 5 10-4-4 6-7 2-6 6-6-6Z"/><path class="ae3-heart-core" d="M61 27L50 19l-2-7 5-4 5 2 3 5 3-5 5-2 5 4-2 7Z"/><path class="ae3-heart-facet" d="M50 12l11 15 11-15M53 9l8 6 8-6M50 19h22M61 15v12"/><path class="ae3-heart-shine" d="M55 11l3 2-4 5-2-4Z"/></svg></span></aside>
        <div class="ae3-orbit"><i></i><i></i><i></i><b></b><small>GHOST FREQUENCY</small></div>
        <div class="ae3-link-sync">
          <span class="ae3-sync-copy"><small>EXOSTRIDER LINK · RA2362-G</small><b>同步率 <strong>200</strong><em>/ 200</em></b></span>
          <span class="ae3-sync-core"><i></i><b>4</b><small>/4</small></span>
          <span class="ae3-sync-spectrum">${Array.from({length:20},(_,i)=>`<i style="--i:${i};--h:${6 + (i % 5) * 4}px"></i>`).join("")}</span>
          <span class="ae3-sync-glow"><small>流溢辉光</small><b>600</b></span>
        </div>
        <div class="ae3-weapon-charm">
          <svg class="ae3-everbright-polestar" viewBox="0 0 100 100" aria-hidden="true">
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
          <small>永远的启明星 · 极星长航</small>
        </div>
      </div>`;

    // Both floating instruments live outside the clipped stage. This preserves
    // crisp positioning without entering the composer's shadow/filter context.
    root.appendChild(root.querySelector(".ae3-link-sync"));
    root.appendChild(root.querySelector(".ae3-weapon-charm"));

    // Aemeath's chest mark is a single asymmetric signal-heart, not a generic decorative heart.
    const memoryCard = root.querySelector(".ae3-memory");
    if (memoryCard) memoryCard.querySelector("span")?.replaceWith(document.createElement("span"));
    const heartSlot = root.querySelector(".ae3-memory > span");
    if (heartSlot) heartSlot.className = "ae3-heart-resonator";
    const heartResonator = root.querySelector(".ae3-heart-resonator");
    if (heartResonator) heartResonator.innerHTML = `<svg class="ae3-heart-sigil" viewBox="0 0 122 38" aria-label="Aemeath signal heart" role="img"><defs><linearGradient id="ae3-sigil-shell" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#ffffff"/><stop offset=".28" stop-color="#bff7ff"/><stop offset=".58" stop-color="#ff9dca"/><stop offset="1" stop-color="#b33799"/></linearGradient><linearGradient id="ae3-sigil-wave" x1="0" y1="0" x2="1" y2="0"><stop stop-color="#76e8f3"/><stop offset=".5" stop-color="#fff7ff"/><stop offset="1" stop-color="#ff62ad"/></linearGradient><radialGradient id="ae3-sigil-core"><stop stop-color="#fffaff"/><stop offset=".32" stop-color="#ffb9da"/><stop offset=".72" stop-color="#ff559f"/><stop offset="1" stop-color="#6f235f"/></radialGradient></defs><path class="ae3-sigil-shell" d="M61 34L39 20l-2-9 7-7 10 2 7 7 7-7 10-2 7 7-2 9Z"/><path class="ae3-sigil-core" d="M61 28L48 18l1-6 6-2 6 6 6-6 6 2 1 6Z"/><path class="ae3-sigil-wave" d="M45 18h8l3-5 5 11 4-7h12"/><path class="ae3-sigil-scan" d="M39 10l8-4m28 0 8 4M43 25l8 5m20 0 8-5"/><path class="ae3-sigil-glint" d="M51 12l3 2-4 5-2-4Z"/></svg>`;

    const mark = (node, name) => {
      if (!node || node.classList.contains(name)) return;
      node.classList.add(name);
      marked.push([node, name]);
    };
    const findHome = () => {
      const icon = document.querySelector('[data-testid="home-icon"]');
      return icon?.closest('[role="main"]') || icon?.closest("main") || null;
    };
    const ensure = () => {
      if (disposed) return;
      timer = 0;
      const main = document.querySelector("main.main-surface") || document.querySelector("main");
      const aside = document.querySelector("aside.app-shell-left-panel");
      const home = findHome();
      const stage = root.querySelector(".ae3-stage");
      const linkSync = root.querySelector(".ae3-link-sync");
      const weaponCharm = root.querySelector(".ae3-weapon-charm");
      const voyageCard = root.querySelector(".ae3-task-voyage");
      const tunnelerCard = root.querySelector(".ae3-task-tunneler");
      const composer = document.querySelector(".composer-surface-chrome");
      document.querySelectorAll("button").forEach(button => {
        const label = button.textContent?.trim();
        if (label !== "输出" && label !== "来源") return;
        const section = button.closest("section");
        mark(section, "ae3-output-section");
        mark(section?.querySelector("header"), "ae3-output-header");
        mark(section?.parentElement?.parentElement, "ae3-output-panel");
      });
      const outputOpen = Array.from(document.querySelectorAll(".ae3-output-panel")).some(panel => {
        const panelBox = panel.getBoundingClientRect();
        return panelBox.width > 120 && panelBox.height > 80;
      });
      html.classList.toggle("ae3-has-output", outputOpen);
      if (main && stage) {
        const box = main.getBoundingClientRect();
        Object.assign(stage.style, { left:`${Math.round(box.left)}px`, top:`${Math.round(box.top)}px`, width:`${Math.round(box.width)}px`, height:`${Math.round(box.height)}px` });
        if (weaponCharm && composer) {
          const composerBox = composer.getBoundingClientRect();
          const weaponSize = 76;
          const left = Math.max(box.left + 14, Math.round(composerBox.left - weaponSize - 18));
          const top = Math.max(
            box.top + 56,
            Math.min(
              Math.round(box.bottom - weaponSize - 22),
              Math.round(composerBox.top + (composerBox.height - weaponSize) / 2),
            ),
          );
          Object.assign(weaponCharm.style, { left:`${left}px`, top:`${top}px`, right:"auto", bottom:"auto" });
        }
        if (linkSync && voyageCard && tunnelerCard) {
          const voyageStyle = window.getComputedStyle(voyageCard);
          const tunnelerStyle = window.getComputedStyle(tunnelerCard);
          const voyageWidth = Number.parseFloat(voyageStyle.width);
          const voyageHeight = Number.parseFloat(voyageStyle.height);
          const voyageRight = Number.parseFloat(voyageStyle.right);
          const voyageBottom = Number.parseFloat(voyageStyle.bottom);
          const tunnelerWidth = Number.parseFloat(tunnelerStyle.width);
          const tunnelerHeight = Number.parseFloat(tunnelerStyle.height);
          const tunnelerRight = Number.parseFloat(tunnelerStyle.right);
          const tunnelerBottom = Number.parseFloat(tunnelerStyle.bottom);
          if ([voyageWidth, voyageHeight, voyageRight, voyageBottom, tunnelerWidth, tunnelerHeight, tunnelerRight, tunnelerBottom].every(Number.isFinite)) {
            const syncWidth = 320;
            const syncHeight = 56;
            const cardsLeft = box.right - Math.max(voyageRight + voyageWidth, tunnelerRight + tunnelerWidth);
            const cardsRight = box.right - Math.min(voyageRight, tunnelerRight);
            const cardsTop = Math.min(box.bottom - voyageBottom - voyageHeight, box.bottom - tunnelerBottom - tunnelerHeight);
            const left = Math.max(box.left + 18, Math.round((cardsLeft + cardsRight - syncWidth) / 2));
            const top = Math.max(box.top + 56, Math.round(cardsTop - syncHeight - 14));
            Object.assign(linkSync.style, { left:`${left}px`, top:`${top}px`, right:"auto", bottom:"auto" });
          }
        }
      }
      html.classList.toggle("ae3-is-home", Boolean(home));
      html.classList.toggle("ae3-is-task", !home);
      mark(main, "ae3-main");
      mark(aside, "ae3-sidebar");
      mark(home, "ae3-home");
      mark(document.querySelector(".group\\/application-menu-top-bar"), "ae3-window-bar");

      if (aside) {
        const phases = ["dawn", "signal", "orbit", "core"];
        let thread = 0;
        aside.querySelectorAll('[data-sidebar-project-drop-zone="project-icon"]').forEach((icon,index) => {
          const heading = icon.parentElement;
          const row = heading?.closest('[data-app-action-sidebar-project-row]');
          mark(heading, "ae3-project-heading");
          if (heading) heading.dataset.ae3Index = String(index + 1).padStart(2, "0");
          if (row) row.dataset.ae3Phase = phases[index % phases.length];
        });
        aside.querySelectorAll('[data-app-action-sidebar-thread-row]').forEach(row => row.dataset.ae3Thread = String(++thread).padStart(2, "0"));
        aside.querySelectorAll("*").forEach(node => {
          const text = node.children.length === 0 ? node.textContent?.trim() : "";
          if (text === "置顶" || text === "项目") {
            mark(node.parentElement, "ae3-section-label");
            node.parentElement.dataset.ae3Section = text === "置顶" ? "星讯置顶" : "远航项目";
          }
        });
      }

      let message = 0;
      document.querySelectorAll('[class*="_markdownContent_"]').forEach(content => {
        mark(content, "ae3-markdown");
        const unit = content.closest("[data-content-search-unit-key]");
        const key = unit?.getAttribute("data-content-search-unit-key") || "";
        if (key.endsWith(":assistant")) { mark(unit, "ae3-message-assistant"); unit.dataset.ae3Message = String(++message).padStart(2, "0"); }
        if (key.endsWith(":user")) { mark(unit, "ae3-message-user"); unit.dataset.ae3Message = String(++message).padStart(2, "0"); }
      });
      const chatUnit = document.querySelector(".ae3-message-assistant,.ae3-message-user");
      mark(chatUnit?.closest('[class*="thread-content-max-width"]'), "ae3-chat-paper");

      document.querySelectorAll("[data-ae3-card]").forEach(node => node.removeAttribute("data-ae3-card"));
      home?.querySelector(".group\\/home-suggestions")?.querySelectorAll("button").forEach((button,index) => button.dataset.ae3Card = String(index + 1).padStart(2, "0"));
      if (!home) {
        const header = document.querySelector('[data-testid="app-shell-header-context-menu-surface"]');
        const title = header?.querySelector("span > span.truncate")?.parentElement;
        mark(header, "ae3-task-header");
        mark(title, "ae3-task-title");
      }
    };
    const schedule = () => { if (disposed) return; if (timer) window.clearTimeout(timer); timer = window.setTimeout(ensure, 140); };
    const cleanup = () => {
      if (disposed) return true;
      disposed = true;
      if (timer) window.clearTimeout(timer);
      for (const [node,name] of marked) { try { node?.classList?.remove(name); } catch {} }
      try { document.querySelectorAll("*").forEach(node => Array.from(node.attributes || []).forEach(attr => { if (attr.name.startsWith("data-ae3-")) node.removeAttribute(attr.name); })); } catch {}
      html.classList.remove("ae3-theme", "ae3-is-home", "ae3-is-task", "ae3-has-output");
      return true;
    };
    context.observe(document.documentElement, { childList:true, subtree:true }, schedule);
    context.on(window, "resize", schedule, { passive:true });
    context.interval(ensure, 4000);
    window.__AEMEATH_STAR_VOYAGE_V3_CLEANUP__ = cleanup;
    context.addCleanup(cleanup);
    context.addCleanup(() => { delete window.__AEMEATH_STAR_VOYAGE_V3_CLEANUP__; });
    ensure();
  },
  async unmount(context) { return context.window.__AEMEATH_STAR_VOYAGE_V3_CLEANUP__?.() ?? true; }
});
