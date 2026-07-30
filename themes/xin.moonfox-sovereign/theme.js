registerTheme({
  async mount(context) {
    const { document, window, root, config } = context;
    const html = document.documentElement;
    let disposed = false;
    let timer = 0;
    const marked = [];

    html.classList.add("xmf-theme");
    root.setAttribute("aria-hidden", "true");
    root.innerHTML = `
      <div class="xmf-stage">
        <section class="xmf-hero-copy">
          <span class="xmf-kicker"><i></i><span class="xmf-light-only">DAWN COURT · MOONHEART SOVEREIGN</span><span class="xmf-dark-only">CRIMSON MOON · FOX DOMAIN</span></span>
          <h1 class="xmf-light-only">朝月清辉<br><em>照见万心</em></h1>
          <h1 class="xmf-dark-only">赤月临城<br><em>九尾定岁</em></h1>
          <p>${config.subtitle}</p>
          <div class="xmf-phases" aria-label="岁序狐火月相">
            <i class="xmf-phase-new"></i><i class="xmf-phase-wax"></i><i class="xmf-phase-full"></i><i class="xmf-phase-wane"></i><i class="xmf-phase-eclipse"></i><b></b>
          </div>
          <div class="xmf-oracle-note"><small>岁序心印</small><strong class="xmf-light-only">人形 · 听念入梦</strong><strong class="xmf-dark-only">狐身 · 巡狩孤城</strong></div>
        </section>
        <div class="xmf-identity"><span></span><div><b>${config.title}</b><small>${config.status}</small></div><i></i></div>
        <aside class="xmf-task-card xmf-task-card-left"><i></i><div><b>月门 · 谕心</b><small>MOONHEART / ORACLE FORM</small></div></aside>
        <aside class="xmf-task-card xmf-task-card-right xmf-task-card-human"><i></i><div><b>人形 · 执扇</b><small>SOVEREIGN / HEARTFIRE</small></div></aside>
        <aside class="xmf-task-card xmf-task-card-right xmf-task-card-fox"><i></i><div><b>本体 · 九尾</b><small>TRUE FORM / MOON FOX</small></div></aside>
        <aside class="xmf-memory"><small>梦州岁序档案 · X-09</small><p>${config.memory}</p><span>${Array.from({length:7},(_,i)=>`<i style="--n:${i}"></i>`).join("")}</span></aside>
        <div class="xmf-domain"><i></i><i></i><i></i><b></b><small>MOONHEART DOMAIN</small></div>
      </div>`;
    root.insertAdjacentHTML("beforeend", `
      <div class="xmf-composer-charm">
        <svg class="xmf-heart-pendant" viewBox="0 0 96 96" aria-hidden="true">
          <defs>
            <linearGradient id="xmf-pendant-gold" x1="0" y1="1" x2="1" y2="0"><stop stop-color="#84602f"/><stop offset=".4" stop-color="#d8b76d"/><stop offset=".72" stop-color="#fff0b9"/><stop offset="1" stop-color="#a57535"/></linearGradient>
            <radialGradient id="xmf-pendant-jade" cx=".35" cy=".25"><stop stop-color="#ffafb3"/><stop offset=".42" stop-color="#e94251"/><stop offset="1" stop-color="#7b1028"/></radialGradient>
            <linearGradient id="xmf-pendant-silk" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#fff7e6"/><stop offset=".45" stop-color="#d8b76d"/><stop offset=".46" stop-color="#d83a4c"/><stop offset="1" stop-color="#73152a"/></linearGradient>
            <filter id="xmf-pendant-glow"><feGaussianBlur stdDeviation="1.4" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
          </defs>
          <g class="xmf-heart-halo" fill="none" stroke="url(#xmf-pendant-gold)" stroke-linecap="round">
            <circle cx="48" cy="43" r="29" stroke-width="1.2" stroke-dasharray="18 5 3 5"/>
            <circle cx="48" cy="43" r="22" stroke-width=".7" stroke-dasharray="2 5"/>
            <path d="M48 8L52 14L48 20L44 14ZM83 43L77 47L71 43L77 39ZM48 78L44 72L48 66L52 72ZM13 43L19 39L25 43L19 47Z" fill="url(#xmf-pendant-gold)" stroke="none"/>
          </g>
          <g class="xmf-heart-crescent" filter="url(#xmf-pendant-glow)">
            <path d="M63 19C48 20 36 31 36 45C36 55 42 63 51 67C35 67 24 57 24 43C24 28 36 17 51 17C55 17 59 18 63 19Z" fill="url(#xmf-pendant-gold)"/>
            <path d="M32 32Q48 23 64 32M29 54Q48 64 67 53" fill="none" stroke="#fff0c0" stroke-width=".8" opacity=".72"/>
          </g>
          <g class="xmf-heart-jewel" filter="url(#xmf-pendant-glow)">
            <path d="M48 24L54 32L62 34L59 49Q56 61 48 69Q40 61 37 49L34 34L42 32Z" fill="url(#xmf-pendant-jade)" stroke="url(#xmf-pendant-gold)" stroke-width="2"/>
            <path d="M40 37Q48 31 56 37L54 48Q52 56 48 61Q44 56 42 48Z" fill="none" stroke="#ffd8d5" stroke-width="1" opacity=".68"/>
            <circle cx="44" cy="38" r="2.2" fill="#fff4ea" opacity=".86"/>
          </g>
          <g class="xmf-heart-tassels" fill="none" stroke-linecap="round">
            <path d="M36 57Q24 67 18 84M60 57Q73 67 80 84" stroke="url(#xmf-pendant-silk)" stroke-width="2.4"/>
            <path d="M34 59Q29 72 31 89M62 59Q68 72 65 89" stroke="#d83a4c" stroke-width="1.5"/>
            <path d="M15 83L19 92L23 83M76 83L80 92L84 83" stroke="url(#xmf-pendant-gold)" stroke-width="1.3"/>
          </g>
          <g class="xmf-heart-sparks" fill="#fff1bc"><circle cx="17" cy="24" r="1.2"/><circle cx="76" cy="18" r=".9"/><circle cx="84" cy="59" r="1.1"/><path d="M25 15L27 18L25 21L23 18ZM75 69L78 72L75 75L72 72Z"/></g>
        </svg>
        <small>朝月 · 心珰</small>
      </div>`);

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
      const stage = root.querySelector(".xmf-stage");
      const charm = root.querySelector(".xmf-composer-charm");
      const composer = document.querySelector(".composer-surface-chrome");
      if (main && stage) {
        const box = main.getBoundingClientRect();
        Object.assign(stage.style, { left:`${Math.round(box.left)}px`, top:`${Math.round(box.top)}px`, width:`${Math.round(box.width)}px`, height:`${Math.round(box.height)}px` });
        if (charm && composer) {
          const composerBox = composer.getBoundingClientRect();
          const charmSize = 76;
          const left = Math.max(box.left + 14, Math.round(composerBox.left - charmSize - 18));
          const top = Math.max(box.top + 56, Math.min(
            Math.round(box.bottom - charmSize - 22),
            Math.round(composerBox.top + (composerBox.height - charmSize) / 2),
          ));
          Object.assign(charm.style, { left:`${left}px`, top:`${top}px`, right:"auto", bottom:"auto" });
        }
      }
      html.classList.toggle("xmf-is-home", Boolean(home));
      html.classList.toggle("xmf-is-task", !home);
      mark(main,"xmf-main"); mark(aside,"xmf-sidebar"); mark(home,"xmf-home");
      mark(document.querySelector(".group\\/application-menu-top-bar"),"xmf-window-bar");

      if (aside) {
        const seals = ["dawn","heart","moon","fox"];
        let thread = 0;
        aside.querySelectorAll('[data-sidebar-project-drop-zone="project-icon"]').forEach((icon,index)=>{
          const heading = icon.parentElement;
          const row = heading?.closest('[data-app-action-sidebar-project-row]');
          mark(heading,"xmf-project-heading");
          if (heading) heading.dataset.xmfIndex = String(index+1).padStart(2,"0");
          if (row) row.dataset.xmfPhase = seals[index%seals.length];
        });
        aside.querySelectorAll('[data-app-action-sidebar-thread-row]').forEach(row=>row.dataset.xmfThread=String(++thread).padStart(2,"0"));
        aside.querySelectorAll("*").forEach(node=>{
          const text = node.children.length===0 ? node.textContent?.trim() : "";
          if (text==="置顶" || text==="项目") {
            mark(node.parentElement,"xmf-section-label");
            node.parentElement.dataset.xmfSection = text==="置顶" ? "听念之庭" : "梦州岁序";
          }
        });
      }

      let message = 0;
      document.querySelectorAll('[class*="_markdownContent_"]').forEach(content=>{
        mark(content,"xmf-markdown");
        const unit = content.closest("[data-content-search-unit-key]");
        const key = unit?.getAttribute("data-content-search-unit-key") || "";
        if (key.endsWith(":assistant")) { mark(unit,"xmf-message-assistant"); unit.dataset.xmfMessage=String(++message).padStart(2,"0"); }
        if (key.endsWith(":user")) { mark(unit,"xmf-message-user"); unit.dataset.xmfMessage=String(++message).padStart(2,"0"); }
      });
      const chatUnit = document.querySelector(".xmf-message-assistant,.xmf-message-user");
      mark(chatUnit?.closest('[class*="thread-content-max-width"]'),"xmf-chat-paper");
      document.querySelectorAll("button").forEach(button=>{
        const label = button.textContent?.trim();
        if (label!=="输出" && label!=="来源") return;
        const section = button.closest("section");
        mark(section,"xmf-output-section"); mark(section?.querySelector("header"),"xmf-output-header"); mark(section?.parentElement?.parentElement,"xmf-output-panel");
      });
      const outputOpen = Array.from(document.querySelectorAll(".xmf-output-panel")).some(panel=>{ const b=panel.getBoundingClientRect(); return b.width>120 && b.height>80; });
      html.classList.toggle("xmf-has-output",outputOpen);
      document.querySelectorAll("[data-xmf-card]").forEach(n=>n.removeAttribute("data-xmf-card"));
      home?.querySelector(".group\\/home-suggestions")?.querySelectorAll("button").forEach((button,index)=>button.dataset.xmfCard=String(index+1).padStart(2,"0"));
      if (!home) {
        const header = document.querySelector('[data-testid="app-shell-header-context-menu-surface"]');
        const title = header?.querySelector("span > span.truncate")?.parentElement;
        mark(header,"xmf-task-header"); mark(title,"xmf-task-title");
      }
    };
    const schedule = () => { if(disposed)return; if(timer)window.clearTimeout(timer); timer=window.setTimeout(ensure,140); };
    const cleanup = () => {
      if(disposed)return true; disposed=true; if(timer)window.clearTimeout(timer);
      for(const [node,name] of marked){try{node?.classList?.remove(name);}catch{}}
      try{document.querySelectorAll("*").forEach(n=>Array.from(n.attributes||[]).forEach(a=>{if(a.name.startsWith("data-xmf-"))n.removeAttribute(a.name);}));}catch{}
      html.classList.remove("xmf-theme","xmf-is-home","xmf-is-task","xmf-has-output"); return true;
    };
    context.observe(document.documentElement,{childList:true,subtree:true},schedule);
    context.on(window,"resize",schedule,{passive:true}); context.interval(ensure,4000);
    window.__XIN_MOONFOX_CLEANUP__=cleanup; context.addCleanup(cleanup); context.addCleanup(()=>{delete window.__XIN_MOONFOX_CLEANUP__;}); ensure();
  },
  async unmount(context){return context.window.__XIN_MOONFOX_CLEANUP__?.()??true;}
});
