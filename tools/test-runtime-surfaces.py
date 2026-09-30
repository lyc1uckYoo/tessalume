"""Exercise the assembled runtime in isolated Chromium, never in the user's chat.

Usage: python tools/test-runtime-surfaces.py --output artifacts/qa/split-view
Requires websocket-client and a local Chrome/Edge installation.
"""
import argparse
import json
import pathlib
import re
import socket
import subprocess
import tempfile
import time
import urllib.request

import websocket

ROOT = pathlib.Path(__file__).resolve().parents[1]
COMPAT = ROOT / "src/Tessalume.App/Compatibility"


def payload(extra_css=""):
    manifest = json.loads((COMPAT / "Runtime/runtime-bundle.json").read_text("utf-8"))
    fragments = []
    for name in manifest["fragments"]:
        source = (COMPAT / "Runtime" / name).read_text("utf-8")
        fragments.append(re.sub(
            r"(?ms)^[ \t]*// TESSALUME_STANDALONE_ENVELOPE_START\s*\n.*?^[ \t]*// TESSALUME_STANDALONE_ENVELOPE_END\s*(?:\n)?",
            "", source).rstrip())
    script = """registerTheme({ mount(context) {
      context.root.innerHTML = `<div data-theme-stage>
        <div data-theme-role="hero"></div><div data-theme-role="identity">Identity</div>
        <div data-theme-role="task-left"></div><div data-theme-role="memory"></div>
        <div data-theme-role="task-right" data-theme-priority="secondary"></div>
        <div data-theme-role="task-right" data-theme-priority="primary"></div></div>
        <div data-theme-role="sync-panel" data-theme-priority="secondary"></div>
        <div data-theme-role="composer-accessory"></div>`;
      return context.mountCanonicalTheme({ namespace:'qa', themeClass:'qa-theme',
        templateVersion:'1.0', preserveRoot:true, adaptiveLayout:true,
        onEnsure({main,positionComposerAccessory,positionPanelAboveCards}) {
          positionComposerAccessory(main, '[data-theme-role="composer-accessory"]');
          positionPanelAboveCards(main, '[data-theme-role="sync-panel"]',
            ['[data-theme-role="task-right"][data-theme-priority="secondary"]',
             '[data-theme-role="task-right"][data-theme-priority="primary"]'], 320, 56, 40);
        }});
    }});"""
    values = {
        "THEME_ID_JSON": json.dumps("qa.surface-isolation"),
        "TEMPLATE_CSS_JSON": json.dumps((COMPAT / "theme-template-v1.css").read_text("utf-8")),
        "CSS_JSON": json.dumps(extra_css), "HAS_SCRIPT": "true", "SCRIPT_BODY": script,
        "ASSETS_JSON": "{}", "CONFIG_JSON": "{}", "ALLOW_PET_OVERLAY": "false",
        "FINGERPRINT_JSON": json.dumps("qa-home-footer-20260930"),
        "COMPATIBILITY_PROFILE_JSON": (COMPAT / "compatibility-profile-v3.json").read_text("utf-8"),
    }
    source = "\n".join(fragments)
    for key, value in values.items():
        source = source.replace(f"__TESSALUME_PAYLOAD_{key}__", value)
    assert "__TESSALUME_PAYLOAD_" not in source
    return source


HTML = """<!doctype html><html><head><style>
*{box-sizing:border-box}body{margin:0}header{position:fixed;left:190px;top:0;width:calc(100% - 190px);height:52px;display:flex}
header>div{height:52px;width:100%;display:flex;align-items:center}.title{height:32px;display:flex;flex:1;min-width:0}
main{position:relative;margin-left:190px;top:52px;width:calc(100% - 190px);height:calc(100vh - 52px)}
#pane{position:absolute;inset:0;width:100%;overflow:hidden}.thread-scroll-container{position:absolute;inset:0;overflow:auto}
.composer-carrier{position:absolute;bottom:16px;left:35%;width:28%;height:90px}.ComposerLayoutRoot{position:relative;width:100%;height:90px}
[data-codex-composer]{height:60px}#viewer{position:absolute;right:0;top:0;height:100%;width:0;overflow:hidden}
</style></head><body><header data-testid="app-shell-header-context-menu-surface"><div data-app-shell-titlebar-content="true" class="title"><span><button class="truncate">A task title</button></span></div></header>
<main data-app-shell-main-surface="default"><div id="pane" data-app-shell-main-content-layout="thread-edge-scroll" data-app-shell-workspace-layout="split" data-app-shell-focus-area="main">
<div class="thread-scroll-container"><div class="composer-carrier"><div class="ComposerLayoutRoot" data-composer-surface-variant="default"><div data-codex-composer="true" contenteditable="true"></div></div></div></div></div>
<aside id="viewer" data-app-shell-focus-area="right-panel"><div role="tabpanel">Image preview</div></aside></main></body></html>"""

# The titlebar is now a fixed sibling of main, painted over its top 52px.
# Its old z-index of 30 can conceal the stage's centered identity at z-index 9.
TITLEBAR_HTML = HTML.replace(
    '<header data-testid="app-shell-header-context-menu-surface">',
    '<header data-app-shell-titlebar="true"><div data-testid="app-shell-header-context-menu-surface">',
).replace('</header>', '</div></header>').replace(
    '</style>', 'header{top:52px;z-index:30;background:rgb(15,20,25)}header>div{width:100%;height:52px}</style>',
)

TITLEBAR_PROBE = """(() => {
 const root=document.querySelector('#tessalume-theme-root'),stage=root.querySelector('[data-theme-stage]');
 const identity=root.querySelector('[data-theme-role="identity"]'),header=document.querySelector('header'),button=header.querySelector('button');
 const h=header.getBoundingClientRect(),i=identity.getBoundingClientRect(),b=button.getBoundingClientRect();
 const buttonHit=document.elementFromPoint(b.left+b.width/2,b.top+b.height/2);
 const previousStyle=identity.getAttribute('style');
 let identityHit;
 try {
   // Decorations normally let pointer events pass through. Enable only this
   // identity for the stacking probe, then restore the original inline style.
   identity.style.setProperty('pointer-events','auto','important');
   const hit=document.elementFromPoint(i.left+i.width/2,i.top+i.height/2);
   identityHit=hit===identity||identity.contains(hit);
 } finally {
   if(previousStyle===null)identity.removeAttribute('style');else identity.setAttribute('style',previousStyle);
 }
 return {kind:root.dataset.tessalumePageKind,headerOutsideMain:!document.querySelector('main').contains(header),
   headerZ:getComputedStyle(header).zIndex,stageZ:getComputedStyle(stage).zIndex,
   headerMarked:!!header.querySelector('[data-tessalume-surface="task-header"]'),
   identityShown:getComputedStyle(identity).display!=='none'&&i.width>0&&i.height>0,identityHit,
   headerIdentityOverlap:Math.min(h.bottom,i.bottom)>Math.max(h.top,i.top)&&Math.min(h.right,i.right)>Math.max(h.left,i.left),
   buttonHit:buttonHit===button||button.contains(buttonHit),
   pointerEventsRestored:(identity.getAttribute('style')||'')===(previousStyle||'')};
})()"""

# The September 30 home icon is decorative, with aria-hidden on the icon itself.
# Preserve the native wrapper depth: discovery also marks these banner layers.
HOME_HTML = """<!doctype html><html><head><style>
*{box-sizing:border-box}body{margin:0}main{position:relative;margin-left:190px;top:52px;width:calc(100% - 190px);height:calc(100vh - 52px)}
[role=main]{position:relative;width:100%;height:100%}.home-layout{height:100%;display:flex;flex-direction:column}
.contents{display:contents}.hero-region{display:flex;align-items:center;justify-content:center;flex:1}
.banner{width:100%}.banner-inner{width:80%;margin:auto}.native-title{height:112px}
.icon-wrapper{display:flex;flex-direction:column;align-items:center}[data-testid=home-icon]{width:56px;height:56px}
.home-composer{position:absolute;bottom:16px;left:30%;width:40%;height:90px}.ComposerLayoutRoot{height:90px}
[data-codex-composer]{height:60px}
</style></head><body><main data-app-shell-main-surface="default"><div id="home" role="main">
<div id="home-layout" class="home-layout"><div class="contents"><div class="hero-region">
<div class="banner" aria-hidden="false"><div class="banner-inner"><div class="native-title"><div class="icon-wrapper">
<div data-testid="home-icon" aria-hidden="true"></div><h1>What would you like to build?</h1>
</div></div></div></div></div></div></div>
<div class="home-composer z-20 pb-4"><div class="ComposerLayoutRoot"><div data-codex-composer="true" contenteditable="true"></div></div></div>
</div></main></body></html>"""

HOME_PROBE = """(() => {
 const root=document.querySelector('#tessalume-theme-root'),home=document.querySelector('#home');
 return {kind:root.dataset.tessalumePageKind,home:document.documentElement.classList.contains('tessalume-is-home'),
   task:document.documentElement.classList.contains('tessalume-is-task'),
   homeMarked:home.getAttribute('data-tessalume-surface')==='home',
   parts:[...home.querySelectorAll('[data-tessalume-home-part]')].map(n=>n.dataset.tessalumeHomePart),
   heroDisplay:getComputedStyle(root.querySelector('[data-theme-role="hero"]')).display,
   chromeCount:document.querySelectorAll('.composer-surface-chrome').length,
   liveComposerMarked:!!home.querySelector('.composer-surface-chrome'),
   iconAriaHidden:home.querySelector('[data-testid="home-icon"]').getAttribute('aria-hidden')};
})()"""

# New footer paint is a solid background sibling, not the old gradient overlay.
# The editor and actionable/footer-external siblings must keep their own fill.
FOOTER_HTML = """<!doctype html><html><head><style>
*{box-sizing:border-box}body{margin:0}main{position:relative;margin-left:190px;top:52px;width:calc(100% - 190px);height:calc(100vh - 52px)}
[data-app-shell-main-content-layout],.thread-scroll-container{position:absolute;inset:0}.absolute{position:absolute}.bottom-0{bottom:0}.inset-x-0{left:0;right:0}
.pointer-events-none{pointer-events:none}.bg-surface{background:rgb(24,24,24)}
#footer{position:absolute;bottom:0;width:100%;height:130px}#native-floor{height:115px}
.composer-carrier{position:absolute;bottom:0;left:25%;width:50%;height:100px}
.ComposerLayoutRoot{height:100px;background:rgb(43,53,63)}[data-codex-composer]{height:70px}
#footer-action{position:absolute;right:0;bottom:0;width:90px;height:50px;background:rgb(54,64,74)}
#unrelated-floor{height:120px;top:0;bottom:auto;background:rgb(65,75,85)}
</style></head><body><main data-app-shell-main-surface="default"><div data-app-shell-main-content-layout="thread-edge-scroll">
<div class="thread-scroll-container"><div id="unrelated-floor" aria-hidden="true" class="pointer-events-none absolute inset-x-0 bottom-0 bg-surface"></div>
<div id="footer" data-thread-scroll-footer="true"><div id="native-floor" aria-hidden="true" class="pointer-events-none absolute inset-x-0 bottom-0 mt-8 bg-surface"></div>
<div class="composer-carrier"><div id="editor-surface" class="ComposerLayoutRoot"><div data-codex-composer="true" contenteditable="true"></div></div></div>
<button id="footer-action" class="absolute bottom-0 bg-surface">Action</button>
</div></div></div></main></body></html>"""

FOOTER_PROBE = """(() => {
 const paint=id=>{let n=document.getElementById(id),s=getComputedStyle(n);return {background:s.backgroundColor,image:s.backgroundImage,marked:n.classList.contains('tessalume-composer-native-fade')}};
 return {kind:document.querySelector('#tessalume-theme-root')?.dataset.tessalumePageKind,
   floor:paint('native-floor'),editor:paint('editor-surface'),action:paint('footer-action'),unrelated:paint('unrelated-floor')};
})()"""

# These are native root attributes, retained when React replaces its className.
# Deliberately different native/theme paints make a one-frame fallback visible.
SUBMIT_HTML = FOOTER_HTML.replace(
    '.ComposerLayoutRoot{', '._ComposerLayoutRoot_gcdh7_2{',
).replace(
    'class="ComposerLayoutRoot"', 'class="_ComposerLayoutRoot_gcdh7_2" data-composer-surface-variant="default"',
).replace(
    '</style>', '#editor-surface{background-color:rgb(48,48,48);background-image:none}[data-codex-composer]{width:100%}'
    '#fade-carrier{position:sticky;bottom:0;width:100%;height:0}'
    '#native-fade{height:150px;background-image:linear-gradient(to top,rgb(24,24,24),transparent)}</style>',
).replace(
    '<div id="footer"',
    '<div id="fade-carrier" class="sticky bottom-0"><div id="native-fade" class="pointer-events-none absolute inset-x-0 bottom-0 bg-gradient-to-t from-surface"></div></div><div id="footer"',
)
SUBMIT_CSS = """html.qa-theme .composer-surface-chrome {
 background-color:rgb(9,38,50)!important;
 background-image:linear-gradient(135deg,rgb(11,52,63),rgb(6,23,33))!important;
}"""

SUBMIT_PROBE = """(() => {
 const editor=document.querySelector('#editor-surface'),floor=document.querySelector('#native-floor');
 const e=getComputedStyle(editor),f=getComputedStyle(floor),a=getComputedStyle(document.querySelector('#footer-action')),g=getComputedStyle(document.querySelector('#native-fade'));
 return {kind:document.querySelector('#tessalume-theme-root')?.dataset.tessalumePageKind,
   editor:{background:e.backgroundColor,image:e.backgroundImage,marked:editor.classList.contains('composer-surface-chrome'),variant:editor.dataset.composerSurfaceVariant},
   floor:{background:f.backgroundColor,image:f.backgroundImage,marked:floor.classList.contains('tessalume-composer-native-fade')},
   fade:{background:g.backgroundColor,image:g.backgroundImage},
   action:{background:a.backgroundColor,image:a.backgroundImage}};
})()"""

CARDS_PROBE = """(() => {
 const root=document.querySelector('#tessalume-theme-root'),composer=document.querySelector('#editor-surface');
 const visible=node=>{for(let n=node;n;n=n.parentElement){const s=getComputedStyle(n);if(s.display==='none'||s.visibility==='hidden'||Number(s.opacity)===0)return false;}const r=node.getBoundingClientRect();return r.width>0&&r.height>0};
 return {kind:root.dataset.tessalumePageKind,layout:root.dataset.tessalumeTaskLayout,
   composerVisible:visible(composer),nativeRoot:composer.dataset.composerSurfaceVariant,composerInert:composer.inert,
   editorCount:composer.querySelectorAll('[data-codex-composer="true"]').length,
   cards:[...root.querySelectorAll('[data-theme-role="task-left"],[data-theme-role="memory"],[data-theme-role="task-right"]')].map(n=>({
     role:n.dataset.themeRole,priority:n.dataset.themePriority||'',hidden:n.dataset.tessalumeAutoHidden,
     reason:n.dataset.tessalumeAutoHiddenReason||'',visible:visible(n)}))};
})()"""

PROBE = """(() => {
 const rect=n=>{let b=n.getBoundingClientRect();return {left:b.left,top:b.top,right:b.right,bottom:b.bottom,width:b.width,height:b.height}};
 const shown=n=>{if(!n)return false;for(let x=n;x;x=x.parentElement){let s=getComputedStyle(x);if(s.display==='none'||s.visibility==='hidden'||Number(s.opacity)===0)return false;}return rect(n).width>0};
 const root=document.querySelector('#tessalume-theme-root'),stage=root.querySelector('[data-theme-stage]');
 return {task:document.documentElement.classList.contains('tessalume-is-task'),kind:root.dataset.tessalumePageKind,
   stage:rect(stage),main:rect(document.querySelector('main')),pane:rect(document.querySelector('#pane')),viewer:rect(document.querySelector('#viewer')),
   stageShown:shown(stage),identityShown:shown(root.querySelector('[data-theme-role="identity"]')),
   headerMarked:!!document.querySelector('[data-tessalume-surface="task-header"],[data-tessalume-surface="task-title"]'),
   header:rect(document.querySelector('header')),title:rect(document.querySelector('.title')),
   chromeCount:document.querySelectorAll('.composer-surface-chrome').length,
   decorations:[...root.querySelectorAll('[data-theme-role]')].filter(shown).map(n=>({role:n.dataset.themeRole,...rect(n)}))};
})()"""


class Cdp:
    def __init__(self, url):
        self.connection = websocket.create_connection(url, suppress_origin=True, timeout=15)
        self.serial = 0

    def call(self, method, params=None):
        self.serial += 1
        self.connection.send(json.dumps(dict(id=self.serial, method=method, params=params or {})))
        while True:
            reply = json.loads(self.connection.recv())
            if reply.get("id") != self.serial:
                continue
            if "error" in reply:
                raise RuntimeError(reply["error"])
            return reply["result"]

    def evaluate(self, expression):
        result = self.call("Runtime.evaluate", dict(expression=expression, returnByValue=True, awaitPromise=True))
        if "exceptionDetails" in result:
            raise RuntimeError(result["exceptionDetails"])
        return result.get("result", {}).get("value")

    def settle(self):
        self.evaluate("new Promise(r=>setTimeout(()=>requestAnimationFrame(()=>requestAnimationFrame(r)),350))")


def run_case(cdp, source, width, dark):
    cdp.call("Emulation.setDeviceMetricsOverride", dict(width=width, height=960, deviceScaleFactor=1, mobile=False))
    cdp.call("Page.navigate", {"url": "about:blank"})
    cdp.call("Page.setDocumentContent", {"frameId": cdp.call("Page.getFrameTree")["frameTree"]["frame"]["id"], "html": HTML})
    cdp.evaluate(f"document.documentElement.classList.toggle('electron-dark',{str(dark).lower()})")
    cdp.evaluate(source)
    cdp.settle()
    chat = cdp.evaluate(PROBE)
    assert chat["task"] and chat["kind"] == "task" and chat["chromeCount"] == 1, chat
    assert abs(chat["stage"]["width"] - chat["pane"]["width"]) < 1, chat
    assert chat["headerMarked"], chat
    cdp.evaluate("""document.querySelector('header').setAttribute('data-app-shell-tab-row','true');
      document.querySelector('header').className='native-react-rerender';
      document.querySelector('.title').innerHTML='<div role="tablist"><button role="tab">Chat</button><button role="tab">Image.png</button></div>';
      document.querySelector('#pane').style.width='65%'; document.querySelector('#viewer').style.width='35%';""")
    cdp.settle()
    split = cdp.evaluate(PROBE)
    assert split["task"] and not split["headerMarked"] and not split["identityShown"], split
    assert abs(split["stage"]["width"] - split["pane"]["width"]) < 1, split
    for item in split["decorations"]:
        if item["role"] != "hero":
            assert item["right"] <= split["viewer"]["left"] + 1, item
    cdp.evaluate("""document.querySelector('#pane').setAttribute('aria-hidden','true');
      document.querySelector('#pane').setAttribute('data-app-shell-workspace-layout','full');
      document.querySelector('#pane').style.width='0';
      document.querySelector('.thread-scroll-container').style.cssText='width:792px;left:-792px';
      document.querySelector('#viewer').style.width='100%';""")
    cdp.settle()
    file_only = cdp.evaluate(PROBE)
    assert not file_only["task"] and file_only["kind"] == "other", file_only
    assert not file_only["stageShown"] and not file_only["identityShown"] and not file_only["decorations"], file_only
    assert not file_only["headerMarked"] and file_only["chromeCount"] == 0, file_only
    assert file_only["header"] == split["header"] and file_only["title"] == split["title"], file_only
    # Codex removes aria-hidden before the pane's opening animation has width.
    cdp.evaluate("document.querySelector('#pane').removeAttribute('aria-hidden')")
    cdp.settle()
    transition = cdp.evaluate(PROBE)
    assert not transition["task"] and not transition["decorations"], transition
    cdp.evaluate("document.querySelector('#pane').setAttribute('aria-hidden','true')")
    # Keep the hidden old thread first in DOM; discovery must choose the new one.
    cdp.evaluate("""const stale=document.querySelector('#pane');const live=stale.cloneNode(true);
      stale.id='stale';live.id='pane';live.removeAttribute('aria-hidden');live.style.width='65%';
      live.setAttribute('data-app-shell-workspace-layout','split');live.querySelector('.thread-scroll-container').style.cssText='';
      stale.after(live);document.querySelector('#viewer').style.width='35%';""")
    cdp.settle()
    restored = cdp.evaluate(PROBE)
    assert restored["task"] and restored["chromeCount"] == 1 and not restored["headerMarked"], restored
    assert abs(restored["stage"]["width"] - restored["pane"]["width"]) < 1, restored
    return {"width": width, "dark": dark, "chat": chat, "split": split, "file": file_only, "transition": transition, "restored": restored}


def load_fixture(cdp, html, width, dark):
    cdp.call("Emulation.setDeviceMetricsOverride", dict(width=width, height=960, deviceScaleFactor=1, mobile=False))
    cdp.call("Page.navigate", {"url": "about:blank"})
    cdp.call("Page.setDocumentContent", {"frameId": cdp.call("Page.getFrameTree")["frameTree"]["frame"]["id"], "html": html})
    cdp.evaluate(f"document.documentElement.classList.toggle('electron-dark',{str(dark).lower()})")


def run_home_case(cdp, source, width, dark):
    load_fixture(cdp, HOME_HTML, width, dark)
    cdp.evaluate(source)
    cdp.settle()
    states = {}

    def check_visible(name):
        state = cdp.evaluate(HOME_PROBE)
        assert state["home"] and state["kind"] == "home" and not state["task"], state
        assert state["homeMarked"] and state["heroDisplay"] != "none", state
        assert state["chromeCount"] == 1 and state["liveComposerMarked"], state
        assert {"layout", "hero-region", "banner", "banner-inner", "native-title", "composer-carrier"}.issubset(state["parts"]), state
        states[name] = state

    check_visible("decorative_icon")
    assert states["decorative_icon"]["iconAriaHidden"] == "true"
    cdp.evaluate("document.querySelector('[data-testid=home-icon]').removeAttribute('aria-hidden')")
    cdp.settle()
    check_visible("legacy_icon")
    cdp.evaluate("document.querySelector('[data-testid=home-icon]').setAttribute('aria-hidden','true')")

    for attribute in ["aria-hidden", "hidden", "inert"]:
        # The surrounding role=main keeps its size: recognition must check the
        # icon's ancestors, not only the dimensions of the containing page.
        cdp.evaluate(f"document.querySelector('#home-layout').setAttribute({json.dumps(attribute)},'true')")
        cdp.settle()
        state = cdp.evaluate(HOME_PROBE)
        assert not state["home"] and state["kind"] == "other" and not state["task"], state
        assert state["heroDisplay"] == "none" and state["chromeCount"] == 0, state
        states[f"ancestor_{attribute}"] = state
        cdp.evaluate(f"document.querySelector('#home-layout').removeAttribute({json.dumps(attribute)})")
        cdp.settle()
        check_visible(f"restored_{attribute}")

    # React retains the previous view; a hidden first match must not mask the
    # visible home below it, and only the live editor may have the theme alias.
    cdp.evaluate("""const stale=document.querySelector('#home'),live=stale.cloneNode(true);
      stale.id='stale-home';stale.setAttribute('aria-hidden','true');stale.style.display='none';
      stale.after(live);""")
    cdp.settle()
    check_visible("retained_hidden_home")
    return {"width": width, "dark": dark, "states": states}


def run_footer_case(cdp, source, width, dark):
    load_fixture(cdp, FOOTER_HTML, width, dark)
    before = cdp.evaluate(FOOTER_PROBE)
    assert before["floor"]["background"] == "rgb(24, 24, 24)", before
    cdp.evaluate(source)
    cdp.settle()
    after = cdp.evaluate(FOOTER_PROBE)
    assert after["kind"] == "task", after
    assert after["floor"]["marked"] and after["floor"]["background"] == "rgba(0, 0, 0, 0)" and after["floor"]["image"] == "none", after
    for name in ["editor", "action", "unrelated"]:
        assert after[name] == before[name], (name, before[name], after[name])
    return {"width": width, "dark": dark, "before": before, "solid_footer": after}


def run_titlebar_case(cdp, source, width, dark):
    load_fixture(cdp, TITLEBAR_HTML, width, dark)
    cdp.evaluate(source)
    cdp.settle()
    states = {}

    def check(name, task_title):
        state = cdp.evaluate(TITLEBAR_PROBE)
        assert state["headerOutsideMain"] and state["buttonHit"] and state["pointerEventsRestored"], state
        if task_title:
            assert state["kind"] == "task" and state["headerMarked"], state
            assert state["headerIdentityOverlap"] and state["identityShown"] and state["identityHit"], state
            assert int(state["headerZ"]) < int(state["stageZ"]), state
        else:
            assert not state["headerMarked"] and not state["identityShown"], state
            assert state["headerZ"] == "30", state
        states[name] = state

    check("task_title", True)
    cdp.evaluate("""document.querySelector('header').setAttribute('data-app-shell-tab-row','true');
      document.querySelector('.title').innerHTML='<div role="tablist"><button role="tab">Chat</button><button role="tab">Image.png</button></div>';
      document.querySelector('#pane').style.width='65%';document.querySelector('#viewer').style.width='35%';""")
    cdp.settle()
    check("tab_strip", False)
    cdp.evaluate("""document.querySelector('header').removeAttribute('data-app-shell-tab-row');
      document.querySelector('.title').innerHTML='<span><button class="truncate">Image.png</button></span>';
      document.querySelector('#pane').setAttribute('aria-hidden','true');document.querySelector('#pane').style.width='0';
      document.querySelector('#viewer').style.width='100%';""")
    cdp.settle()
    check("file_only", False)
    assert states["file_only"]["kind"] == "other", states["file_only"]
    cdp.evaluate("""document.querySelector('#pane').removeAttribute('aria-hidden');
      document.querySelector('#pane').style.width='100%';document.querySelector('#viewer').style.width='0';""")
    cdp.settle()
    check("restored_task_title", True)
    return {"width": width, "dark": dark, "states": states}


def run_submit_case(cdp, source, width, dark):
    load_fixture(cdp, SUBMIT_HTML, width, dark)
    native = cdp.evaluate(SUBMIT_PROBE)
    assert native["editor"]["background"] == "rgb(48, 48, 48)" and native["editor"]["image"] == "none", native
    cdp.evaluate(source)
    cdp.settle()
    before = cdp.evaluate(SUBMIT_PROBE)
    assert before["editor"]["background"] == "rgb(9, 38, 50)" and "linear-gradient" in before["editor"]["image"], before
    expected = {name: {key: before[name][key] for key in ["background", "image"]} for name in ["editor", "floor", "fade", "action"]}
    assert expected["floor"] == {"background": "rgba(0, 0, 0, 0)", "image": "none"}, before
    assert "linear-gradient" in native["fade"]["image"] and expected["fade"]["image"] == "none", (native, before)
    states = {}
    mutations = {
        # Do not combine these first two mutations: childList repair can hide
        # the otherwise-unobserved React className reset of the same editor.
        "class_reset": "document.querySelector('#editor-surface').className='_ComposerLayoutRoot_gcdh7_2 gap-2';",
        "footer_replaced": """const floor=document.createElement('div');floor.id='native-floor';
          floor.setAttribute('aria-hidden','true');floor.className='pointer-events-none absolute inset-x-0 bottom-0 mt-8 bg-surface';
          document.querySelector('#native-floor').replaceWith(floor);""",
        "fade_replaced": """const old=document.querySelector('#native-fade'),next=old.cloneNode(true);
          next.className='pointer-events-none absolute inset-x-0 bottom-0 bg-gradient-to-t from-surface';old.replaceWith(next);""",
        "submit_rerender": """const old=document.querySelector('#editor-surface'),next=old.cloneNode(true);
          next.className='_ComposerLayoutRoot_gcdh7_2 gap-2';next.removeAttribute('data-tessalume-surface');
          old.replaceWith(next);document.querySelector('#footer').setAttribute('data-submit-state','pending');""",
    }
    for name, mutation in mutations.items():
        state = cdp.evaluate("""new Promise(resolve=>{const start=performance.now();
          """ + mutation + """
          const immediate=""" + SUBMIT_PROBE + """;
          requestAnimationFrame(()=>resolve({immediate,firstFrame:""" + SUBMIT_PROBE + """,elapsedMs:performance.now()-start}));
        })""")
        states[name] = state
        # Synchronous paint catches the gap even if another observer happens to
        # restore an alias before this machine's next animation frame.
        for phase in ["immediate", "firstFrame"]:
            for surface in ["editor", "floor", "fade", "action"]:
                paint = {key: state[phase][surface][key] for key in ["background", "image"]}
                assert paint == expected[surface], (name, phase, surface, expected[surface], state)
        cdp.settle()
    return {"width": width, "dark": dark, "native": native, "before": before, "states": states}


def run_pending_cards_case(cdp, source, width, dark):
    load_fixture(cdp, SUBMIT_HTML, width, dark)
    cdp.evaluate(source)
    cdp.settle()
    before = cdp.evaluate(CARDS_PROBE)
    assert before["kind"] == "task" and before["composerVisible"] and len(before["cards"]) == 4, before
    assert all(card["reason"] != "content-unavailable" for card in before["cards"]), before
    if width == 1920:
        assert all(card["visible"] and card["hidden"] == "false" for card in before["cards"]), before

    def frames_after(mutation):
        # Watch beyond the debounced adaptive pass: a first-frame-only check
        # misses a card disappearing 96-140ms into the native pending state.
        return cdp.evaluate("""new Promise(resolve=>{const start=performance.now(),frames=[];
          """ + mutation + """
          const capture=()=>{frames.push({elapsedMs:performance.now()-start,state:""" + CARDS_PROBE + """});
            if(performance.now()-start>=320)resolve(frames);else requestAnimationFrame(capture)};
          requestAnimationFrame(capture);
        })""")

    pending = frames_after("""const composer=document.querySelector('#editor-surface');
      composer.className='_ComposerLayoutRoot_gcdh7_2 gap-2';composer.removeAttribute('data-tessalume-surface');
      composer.inert=true;
      composer.innerHTML='<div data-pending-composer-preview="true">Sending...</div>';""")
    restored = frames_after("""const composer=document.querySelector('#editor-surface');
      composer.className='_ComposerLayoutRoot_gcdh7_2';composer.inert=false;
      composer.innerHTML='<div data-codex-composer="true" contenteditable="true"></div>';""")
    for name, frames in [("pending", pending), ("restored", restored)]:
        assert len(frames) >= 2, (name, frames)
        for frame in frames:
            state = frame["state"]
            assert state["kind"] == "task" and state["composerVisible"] and state["nativeRoot"] == "default", (name, frame)
            assert state["composerInert"] == (name == "pending"), (name, frame)
            assert state["editorCount"] == (0 if name == "pending" else 1), (name, frame)
            assert state["cards"] == before["cards"] and state["layout"] == before["layout"], (name, before, frame)

    blocked = {}
    for name, selector, attribute, expected_kind in [
        ("root_hidden", "#editor-surface", "hidden", "task"),
        ("root_aria_hidden", "#editor-surface", "aria-hidden", "task"),
        ("ancestor_inert", "[data-app-shell-main-content-layout]", "inert", "other"),
    ]:
        # Self-inert is the only exception. Explicitly hidden roots and inert
        # ancestor panes must still hide the decoration instead of being cached.
        cdp.evaluate(f"document.querySelector('#editor-surface').inert=true;document.querySelector({json.dumps(selector)}).setAttribute({json.dumps(attribute)},'true')")
        cdp.settle()
        hidden = cdp.evaluate(CARDS_PROBE)
        assert hidden["kind"] == expected_kind, (name, hidden)
        assert all(card["hidden"] == "true" and not card["visible"] for card in hidden["cards"]), (name, hidden)
        cdp.evaluate(f"document.querySelector({json.dumps(selector)}).removeAttribute({json.dumps(attribute)});document.querySelector('#editor-surface').inert=false")
        cdp.settle()
        shown = cdp.evaluate(CARDS_PROBE)
        assert shown["kind"] == "task" and shown["cards"] == before["cards"] and shown["layout"] == before["layout"], (name, shown)
        blocked[name] = {"hidden": hidden, "restored": shown}
    return {"width": width, "dark": dark, "before": before, "pending": pending, "restored": restored, "blocked": blocked}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--browser", type=pathlib.Path)
    args = parser.parse_args()
    browser = args.browser or next(p for p in [
        pathlib.Path("C:/Program Files/Google/Chrome/Application/chrome.exe"),
        pathlib.Path("C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"),
    ] if p.is_file())
    args.output.mkdir(parents=True, exist_ok=True)
    with socket.socket() as endpoint:
        endpoint.bind(("127.0.0.1", 0))
        port = endpoint.getsockname()[1]
    source = payload()
    submit_source = payload(SUBMIT_CSS)
    results = []
    home_results = []
    footer_results = []
    titlebar_results = []
    submit_results = []
    pending_cards_results = []
    with tempfile.TemporaryDirectory(prefix="runtime-surface-", dir=args.output.resolve()) as profile:
        process = subprocess.Popen([str(browser), "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
            f"--remote-debugging-port={port}", f"--user-data-dir={profile}", "about:blank"],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW)
        cdp = None
        try:
            deadline = time.monotonic() + 15
            while True:
                try:
                    targets = json.load(urllib.request.urlopen(f"http://127.0.0.1:{port}/json/list", timeout=1))
                    break
                except OSError:
                    if time.monotonic() > deadline: raise
                    time.sleep(.1)
            cdp = Cdp(next(t["webSocketDebuggerUrl"] for t in targets if t["type"] == "page"))
            for width in [1920, 1440, 1080]:
                for dark in [False, True]:
                    results.append(run_case(cdp, source, width, dark))
                    print(f"PASS {width}px {'dark' if dark else 'light'}: chat, split, file-only, retained hidden chat")
                    home_results.append(run_home_case(cdp, source, width, dark))
                    print(f"PASS {width}px {'dark' if dark else 'light'}: decorative home icon, hidden ancestors, retained hidden home")
                    footer_results.append(run_footer_case(cdp, source, width, dark))
                    print(f"PASS {width}px {'dark' if dark else 'light'}: solid footer transparent, editor and unrelated fills preserved")
                    titlebar_results.append(run_titlebar_case(cdp, source, width, dark))
                    print(f"PASS {width}px {'dark' if dark else 'light'}: external titlebar, visible identity, clickable controls, native tab/file stacking")
                    submit_results.append(run_submit_case(cdp, submit_source, width, dark))
                    print(f"PASS {width}px {'dark' if dark else 'light'}: submit class reset, footer/fade replacement, composer remount keep theme on first frame")
                    pending_cards_results.append(run_pending_cards_case(cdp, submit_source, width, dark))
                    print(f"PASS {width}px {'dark' if dark else 'light'}: pending self-inert cards stable; hidden roots/inert ancestors excluded; restoration correct")
        finally:
            if cdp:
                try: cdp.call("Browser.close")
                except Exception: pass
                cdp.connection.close()
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.terminate()
                process.wait(timeout=5)
    (args.output / "runtime-surfaces.json").write_text(json.dumps(results, indent=2), "utf-8")
    (args.output / "runtime-home.json").write_text(json.dumps(home_results, indent=2), "utf-8")
    (args.output / "runtime-footer.json").write_text(json.dumps(footer_results, indent=2), "utf-8")
    (args.output / "runtime-titlebar.json").write_text(json.dumps(titlebar_results, indent=2), "utf-8")
    (args.output / "runtime-submit.json").write_text(json.dumps(submit_results, indent=2), "utf-8")
    (args.output / "runtime-pending-cards.json").write_text(json.dumps(pending_cards_results, indent=2), "utf-8")
    print(f"PASS {len(results) * 5} existing split-view scenarios, {sum(len(r['states']) for r in home_results)} home scenarios, {len(footer_results)} solid-footer scenarios, {sum(len(r['states']) for r in titlebar_results)} titlebar scenarios, {sum(len(r['states']) for r in submit_results)} submit first-frame scenarios, {len(pending_cards_results) * 2 + sum(len(r['blocked']) * 2 for r in pending_cards_results)} pending/hidden/restored card scenarios")


if __name__ == "__main__":
    main()
