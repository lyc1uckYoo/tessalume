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


def payload():
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
        "CSS_JSON": json.dumps(""), "HAS_SCRIPT": "true", "SCRIPT_BODY": script,
        "ASSETS_JSON": "{}", "CONFIG_JSON": "{}", "ALLOW_PET_OVERLAY": "false",
        "FINGERPRINT_JSON": json.dumps("qa-visible-pane-20260928"),
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
<div class="thread-scroll-container"><div class="composer-carrier"><div class="ComposerLayoutRoot"><div data-codex-composer="true" contenteditable="true"></div></div></div></div></div>
<aside id="viewer" data-app-shell-focus-area="right-panel"><div role="tabpanel">Image preview</div></aside></main></body></html>"""

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
    cdp.evaluate(f"document.documentElement.setAttribute('data-theme',{'\"dark\"' if dark else '\"light\"'})")
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
    results = []
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
    print("PASS 30 runtime surface scenarios")


if __name__ == "__main__":
    main()
