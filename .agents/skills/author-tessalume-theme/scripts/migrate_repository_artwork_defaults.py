#!/usr/bin/env python3
"""Materialize the audited v1 artwork defaults for the built-in themes.

This is intentionally a repository migration, not a general CSS parser.  The
values below are the reviewed effective values from the legacy skin cascade;
keeping the matrix executable makes the 12 x 6 extraction repeatable.
"""

from __future__ import annotations

import argparse
import copy
import json
import re
from pathlib import Path


def gradient(direction: float, stops: list[tuple[float, str, float]]) -> dict:
    return {
        "directionDeg": direction,
        "start": stops[0][0],
        "end": stops[-1][0],
        "stops": [
            {"position": position, "color": color, "opacity": opacity}
            for position, color, opacity in stops
        ],
    }


def effects(*layers: dict, overlay: tuple[str, float] = ("#000000", 0)) -> dict:
    return {
        "brightness": 100,
        "contrast": 100,
        "saturation": 100,
        "opacity": 100,
        "grayscale": 0,
        "hueRotate": 0,
        "blur": 0,
        "blendMode": "normal",
        "overlay": {"color": overlay[0], "opacity": overlay[1]},
        "gradientVeil": {
            "enabled": bool(layers),
            "strength": 100 if layers else 0,
            "layers": list(layers),
        },
        "vignette": 0,
        "readabilityVeil": {
            "enabled": False,
            "color": "#000000",
            "opacity": 0,
            "directionDeg": 90,
            "rangeStart": 0,
            "rangeEnd": 100,
        },
    }


def motion_none() -> dict:
    return {"mode": "none"}


def loop_motion(
    duration_ms: int,
    translate_x: str,
    translate_y: str,
    scale_delta: float,
) -> dict:
    return {
        "mode": "loop",
        "durationMs": duration_ms,
        "easing": "ease-in-out",
        "direction": "alternate",
        "keyframes": [
            {
                "at": 0,
                "translateX": "0px",
                "translateY": "0px",
                "scaleDelta": 0,
                "opacityDelta": 0,
            },
            {
                "at": 100,
                "translateX": translate_x,
                "translateY": translate_y,
                "scaleDelta": scale_delta,
                "opacityDelta": 0,
            },
        ],
    }


def slot(
    asset: str,
    size: tuple[str, str],
    position: tuple[str, str],
    *,
    scale: float = 1,
    origin: tuple[str, str] = ("50%", "50%"),
    layers: tuple[dict, ...] = (),
    overlay: tuple[str, float] = ("#000000", 0),
    motion: dict | None = None,
) -> dict:
    result = {
        "asset": asset,
        "placement": {
            "size": {"width": size[0], "height": size[1]},
            "position": {"x": position[0], "y": position[1]},
            "scale": scale,
            "origin": {"x": origin[0], "y": origin[1]},
            "mirrorX": False,
            "mirrorY": False,
        },
        "effects": effects(*layers, overlay=overlay),
    }
    if motion is not None:
        result["motion"] = motion
    return result


def pair(light: dict, dark: dict) -> dict:
    return {"light": light, "dark": dark}


COVER = ("cover", "auto")
CONTAIN = ("contain", "auto")
CENTER = ("center", "center")


THEMES: dict[str, dict] = {
    "aemeath-star-voyage": {
        "hero": pair(
            slot("hero-light", COVER, ("center", "46%"), scale=1.012, origin=("73%", "50%"), motion=loop_motion(17000, "-8px", "-3px", 0.030632), layers=(
                gradient(90, [(0, "#FFFAFD", 58), (0, "#FFFAFD", 30), (0, "#FFFAFD", 8), (100, "#FFFAFD", 0)]),
            )),
            slot("hero-dark", COVER, ("center", "46%"), scale=1.012, origin=("73%", "50%"), motion=loop_motion(17000, "-8px", "-3px", 0.030632), layers=(
                gradient(90, [(0, "#050916", 72), (0, "#050916", 42), (0, "#050916", 12), (0, "#050916", 0)]),
            )),
        ),
        "sidebar": pair(
            slot("sidebar-light", ("228%", "auto"), ("62%", "-108px")),
            slot("sidebar-dark", ("225%", "auto"), ("82%", "-150px")),
        ),
        "chat": pair(
            slot("chat-light", COVER, CENTER, layers=(
                gradient(90, [(0, "#FFFAFD", 82), (46, "#FFFAFD", 57), (76, "#FFFAFD", 24), (100, "#FFFAFD", 10)]),
            )),
            slot("chat-dark", COVER, CENTER),
        ),
    },
    "cartethyia.gale-tide-crown": {
        "hero": pair(
            slot("hero-light", COVER, CENTER, scale=1.004, motion=loop_motion(19000, "-4px", "-2px", 0.007968), layers=(
                gradient(90, [(0, "#FFFFFF", 83), (39, "#FFFFFF", 34), (61, "#FFFFFF", 0)]),
            )),
            slot("hero-dark", COVER, CENTER, scale=1.004, motion=loop_motion(19000, "-4px", "-2px", 0.007968), layers=(
                gradient(90, [(0, "#040917", 88), (40, "#040917", 37), (62, "#040917", 0)]),
            )),
        ),
        "sidebar": pair(
            slot("sidebar-light", ("312%", "auto"), ("40%", "-148px")),
            slot("sidebar-dark", ("355%", "auto"), ("52%", "-200px")),
        ),
        "chat": pair(
            slot("chat-light", COVER, CENTER, layers=(
                gradient(90, [(0, "#FAFCFF", 78), (48, "#FAFCFF", 48), (78, "#FAFCFF", 18), (100, "#FAFCFF", 8)]),
            )),
            slot("chat-dark", COVER, CENTER),
        ),
    },
    "danya.bubble-void-duality": {
        "hero": pair(
            slot("hero-light", COVER, CENTER, scale=1.012, origin=("70%", "50%"), motion=loop_motion(17000, "-6px", "-3px", 0.019763), layers=(
                gradient(90, [(0, "#F6FAFC", 69), (30, "#F6FAFC", 28), (51, "#F6FAFC", 0)]),
            )),
            slot("hero-dark", COVER, CENTER, scale=1.025, origin=("70%", "50%"), motion=loop_motion(12000, "-9px", "-4px", 0.029268), layers=(
                gradient(90, [(0, "#070A18", 60), (31, "#070A18", 25), (51, "#070A18", 0)]),
            )),
        ),
        "sidebar": pair(
            slot("sidebar-light", ("222%", "auto"), ("76%", "-140px")),
            slot("sidebar-dark", ("260%", "auto"), ("86%", "-90px")),
        ),
        "chat": pair(
            slot("chat-light", COVER, CENTER, layers=(
                gradient(90, [(0, "#F7FDFF", 84), (46, "#F7FDFF", 60), (76, "#F7FDFF", 28), (100, "#F7FDFF", 12)]),
            )),
            slot("chat-dark", COVER, CENTER),
        ),
    },
    "hiyuki.crimson-snow": {
        "hero": pair(
            slot("hero-light", COVER, CENTER, scale=1.012, origin=("73%", "50%"), motion=loop_motion(17000, "-8px", "-3px", 0.030632), layers=(
                gradient(90, [(0, "#F9FCFE", 94), (39, "#F9FCFE", 56), (62, "#F9FCFE", 8), (76, "#F9FCFE", 0)]),
            )),
            slot("hero-dark", COVER, CENTER, scale=1.012, origin=("73%", "50%"), motion=loop_motion(17000, "-8px", "-3px", 0.030632), layers=(
                gradient(90, [(0, "#050811", 93), (40, "#050811", 58), (64, "#050811", 10), (78, "#050811", 0)]),
            )),
        ),
        "sidebar": pair(
            slot("sidebar-light", ("258%", "auto"), ("52%", "-28px")),
            slot("sidebar-dark", ("258%", "auto"), ("44%", "-10px")),
        ),
        "chat": pair(
            slot("chat-light", COVER, CENTER, layers=(
                gradient(90, [(0, "#FFFDFE", 86), (46, "#FFFDFE", 62), (76, "#FFFDFE", 30), (100, "#FFFDFE", 12)]),
            )),
            slot("chat-dark", COVER, CENTER),
        ),
    },
    "iuno.moonbow-defiance": {
        "hero": pair(
            slot("hero-light", COVER, CENTER, scale=1.002, motion=loop_motion(22000, "-4px", "-2px", 0.009980)),
            slot("hero-dark", COVER, CENTER, scale=1.002, motion=loop_motion(22000, "-4px", "-2px", 0.009980), overlay=("#35495E", 24)),
        ),
        "sidebar": pair(
            slot("sidebar-light", ("auto", "122%"), ("56%", "60%")),
            slot("sidebar-dark", ("auto", "107%"), ("51%", "48%")),
        ),
        "chat": pair(
            slot("chat-light", COVER, CENTER, layers=(
                gradient(90, [(0, "#FAFCFF", 78), (48, "#FAFCFF", 48), (78, "#FAFCFF", 18), (100, "#FAFCFF", 8)]),
            )),
            slot("chat-dark", COVER, CENTER),
        ),
    },
    "mornye.first-star-observatory": {
        "hero": pair(
            slot("hero-light", COVER, ("center", "46%"), scale=1.008, origin=("73%", "50%"), motion=loop_motion(24000, "-1px", "-0.5px", 0.003968), layers=(
                gradient(90, [(0, "#FBFCFF", 74), (38, "#F8FAFF", 34), (64, "#F8FAFF", 0)]),
            )),
            slot("hero-dark", COVER, ("center", "46%"), scale=1.008, origin=("73%", "50%"), motion=loop_motion(24000, "-1px", "-0.5px", 0.003968), layers=(
                gradient(90, [(0, "#080B18", 76), (39, "#11101C", 35), (66, "#11101C", 0)]),
            )),
        ),
        "sidebar": pair(
            slot("sidebar-light", ("206%", "auto"), ("58%", "-28px")),
            slot("sidebar-dark", ("220%", "auto"), ("66%", "-20px")),
        ),
        "chat": pair(
            slot("chat-light", COVER, CENTER, layers=(
                gradient(90, [(0, "#F9FBFF", 68), (42, "#F9FBFF", 43), (69, "#F9FBFF", 8), (100, "#F9FBFF", 20)]),
            )),
            slot("chat-dark", COVER, CENTER),
        ),
    },
    "qingxiao.cloudsword-gate": {
        "hero": pair(
            slot("hero-light", COVER, ("58%", "42%"), scale=1.08, origin=("58%", "42%"), motion=motion_none(), layers=(
                gradient(90, [(0, "#F2F9F8", 82), (31, "#EEF7F7", 43), (49, "#ECF6F8", 8), (64, "#ECF6F8", 0)]),
            )),
            slot("hero-dark", COVER, ("58%", "40%"), scale=1.08, origin=("58%", "40%"), motion=motion_none(), layers=(
                gradient(90, [(0, "#040D16", 78), (32, "#05111D", 39), (50, "#05111D", 7), (65, "#05111D", 0)]),
            )),
        ),
        "sidebar": pair(
            slot("sidebar-light", ("258%", "auto"), ("45%", "-186px")),
            slot("sidebar-dark", ("188%", "auto"), ("40%", "-68px")),
        ),
        "chat": pair(
            slot("chat-light", COVER, CENTER, layers=(
                gradient(90, [(0, "#F8FDFF", 84), (46, "#F8FDFF", 60), (76, "#F8FDFF", 27), (100, "#F8FDFF", 11)]),
            )),
            slot("chat-dark", COVER, CENTER),
        ),
    },
    "shorekeeper.tethys-reverie": {
        "hero": pair(
            slot("hero-light", COVER, ("center", "top"), scale=1.003, origin=("78%", "28%"), motion=loop_motion(17000, "-5px", "-2px", 0.014955), layers=(
                gradient(90, [(0, "#FBFEFF", 77), (38, "#FBFEFF", 25), (60, "#FBFEFF", 0)]),
            )),
            slot("hero-dark", COVER, ("center", "top"), scale=1.003, origin=("78%", "28%"), motion=loop_motion(17000, "-5px", "-2px", 0.014955), layers=(
                gradient(90, [(0, "#050C1B", 76), (39, "#050C1B", 26), (61, "#050C1B", 0)]),
            )),
        ),
        "sidebar": pair(
            slot("sidebar-light", ("282%", "auto"), ("86%", "-212px")),
            slot("sidebar-dark", ("282%", "auto"), ("75%", "-152px")),
        ),
        "chat": pair(
            slot("chat-light", COVER, CENTER, layers=(
                gradient(90, [(0, "#F8FEFF", 84), (46, "#F8FEFF", 60), (76, "#F8FEFF", 28), (100, "#F8FEFF", 11)]),
            )),
            slot("chat-dark", COVER, CENTER),
        ),
    },
    "sigrika.semantic-sunrise": {
        "hero": pair(
            slot("hero-light", COVER, CENTER, scale=1.004, origin=("73%", "50%"), motion=loop_motion(18000, "-3px", "-2px", 0.007968), layers=(
                gradient(90, [(0, "#FFFAF1", 82), (39, "#FFFAF1", 32), (61, "#FFFAF1", 0)]),
            )),
            slot("hero-dark", COVER, CENTER, scale=1.004, origin=("73%", "50%"), motion=loop_motion(18000, "-3px", "-2px", 0.007968), layers=(
                gradient(90, [(0, "#070814", 80), (40, "#070814", 30), (62, "#070814", 0)]),
            )),
        ),
        "sidebar": pair(
            slot("sidebar-light", ("254%", "auto"), ("61%", "-148px")),
            slot("sidebar-dark", ("254%", "auto"), ("63%", "-166px")),
        ),
        "chat": pair(
            slot("chat-light", COVER, CENTER, layers=(
                gradient(90, [(0, "#FFFBF4", 78), (30, "#FFFBF4", 53), (47, "#FFFBF4", 12), (58, "#FFFBF4", 10), (77, "#FFFBF4", 48), (100, "#FFFBF4", 67)]),
            )),
            slot("chat-dark", COVER, CENTER, layers=(
                gradient(90, [(0, "#060711", 55), (31, "#060711", 24), (47, "#060711", 4), (58, "#060711", 3), (78, "#060711", 24), (100, "#060711", 46)]),
            )),
        ),
    },
    "suisui.inkscape-dawn": {
        "hero": pair(
            slot("hero-light", COVER, ("58%", "42%"), scale=1.08, origin=("58%", "42%"), motion=motion_none(), layers=(
                gradient(90, [(0, "#FFFDF5", 76), (38, "#FFFDF5", 22), (58, "#FFFDF5", 0)]),
            )),
            slot("hero-dark", COVER, ("58%", "40%"), scale=1.08, origin=("58%", "40%"), motion=motion_none(), layers=(
                gradient(90, [(0, "#071219", 74), (39, "#071219", 24), (59, "#071219", 0)]),
            )),
        ),
        "sidebar": pair(
            slot("sidebar-light", ("230%", "auto"), ("80%", "-168px")),
            slot("sidebar-dark", ("232%", "auto"), ("80%", "-188px")),
        ),
        "chat": pair(
            slot("chat-light", COVER, CENTER, layers=(
                gradient(90, [(0, "#FFFEF8", 82), (46, "#FFFEF8", 57), (76, "#FFFEF8", 24), (100, "#FFFEF8", 10)]),
            )),
            slot("chat-dark", COVER, CENTER),
        ),
    },
    "xin.moonfox-sovereign": {
        "hero": pair(
            slot("hero-light", COVER, CENTER, scale=1, origin=("73%", "50%"), motion=loop_motion(18000, "-3px", "-2px", 0.008000), layers=(
                gradient(90, [(0, "#FFFAF3", 77), (38, "#FFFAF3", 25), (59, "#FFFAF3", 0)]),
            )),
            slot("hero-dark", COVER, CENTER, scale=1, origin=("73%", "50%"), motion=loop_motion(18000, "-3px", "-2px", 0.008000), layers=(
                gradient(90, [(0, "#060B1B", 76), (39, "#060B1B", 26), (60, "#060B1B", 0)]),
            )),
        ),
        "sidebar": pair(
            slot("sidebar-light", ("214%", "auto"), ("86%", "-78px")),
            slot("sidebar-dark", ("225%", "auto"), ("92%", "-150px")),
        ),
        "chat": pair(
            slot("chat-light", COVER, CENTER, layers=(
                gradient(90, [(0, "#FFFAF3", 84), (46, "#FFFAF3", 59), (76, "#FFFAF3", 27), (100, "#FFFAF3", 11)]),
            )),
            slot("chat-dark", COVER, CENTER),
        ),
    },
    "yangyang.xuanling-echo": {
        "hero": pair(
            slot("hero-light", COVER, ("center", "20%"), scale=1.012, origin=("73%", "42%"), motion=loop_motion(18000, "-7px", "-3px", 0.025692), layers=(
                gradient(90, [(0, "#FBFEFF", 82), (34, "#FBFEFF", 45), (58, "#FBFEFF", 6), (73, "#FBFEFF", 0)]),
            )),
            slot("hero-dark", COVER, CENTER, scale=1.012, origin=("73%", "50%"), motion=loop_motion(18000, "-7px", "-3px", 0.025692), layers=(
                gradient(90, [(0, "#060B1B", 76), (39, "#060B1B", 26), (60, "#060B1B", 0)]),
            )),
        ),
        "sidebar": pair(
            slot("sidebar-light", ("212%", "auto"), ("80%", "-72px")),
            slot("sidebar-dark", ("220%", "auto"), ("84%", "-124px")),
        ),
        "chat": pair(
            slot("chat-light", CONTAIN, CENTER, layers=(
                gradient(90, [(0, "#F6FCFF", 84), (46, "#F6FCFF", 59), (76, "#F6FCFF", 27), (100, "#F6FCFF", 11)]),
            )),
            slot("chat-dark", CONTAIN, CENTER),
        ),
    },
}


CSS_RULE_RE = re.compile(r"((?:\s*/\*.*?\*/\s*)*)([^{}]+)\{([^{}]*)\}", re.DOTALL)
ARTWORK_KEYFRAMES = {
    "ae3": ("ae3-hero-breathe",),
    "cthy": ("cthy-hero-drift",),
    "dny": ("dny-hero-breathe", "dny-hero-light-drift", "dny-hero-dark-collapse"),
    "hy3": ("hy3-hero-breathe",),
    "iun": ("iun-hero-drift",),
    "mny": ("mny-observatory-drift",),
    "qxo": ("qxo-breathe",),
    "sk3": ("sk3-hero-breathe",),
    "sgk": ("sgk-hero-breathe",),
    "xmf": ("xmf-orbit-breathe",),
    "xyl": ("xyl-orbit-breathe",),
}


def split_selector_branches(selector: str) -> list[str]:
    branches: list[str] = []
    start = 0
    depth = 0
    for index, character in enumerate(selector):
        if character == "(":
            depth += 1
        elif character == ")":
            depth = max(0, depth - 1)
        elif character == "," and depth == 0:
            branches.append(selector[start:index].strip())
            start = index + 1
    branches.append(selector[start:].strip())
    return [branch for branch in branches if branch]


def artwork_branch_kind(branch: str, namespace: str) -> str | None:
    hero_token = f".{namespace}-home>div:first-child>div:first-child>div:first-child::before"
    if hero_token in branch:
        return "hero"
    if "aside.app-shell-left-panel::after" in branch:
        return "sidebar"
    if f"-is-task main.{namespace}-main::before" in branch:
        return "chat"
    if f"-is-task main.{namespace}-main::after" in branch:
        return "chat-veil"
    return None


def remove_declarations(body: str, properties: tuple[str, ...]) -> str:
    for property_name in sorted(properties, key=len, reverse=True):
        body = re.sub(
            rf"(?m)^\s*{re.escape(property_name)}\s*:[^;{{}}]*;\s*",
            "",
            body,
            flags=re.IGNORECASE,
        )
    return body


def remove_keyframe(css: str, name: str) -> str:
    match = re.search(rf"@keyframes\s+{re.escape(name)}\s*\{{", css)
    if not match:
        return css
    opening = css.find("{", match.start())
    depth = 0
    for index in range(opening, len(css)):
        if css[index] == "{":
            depth += 1
        elif css[index] == "}":
            depth -= 1
            if depth == 0:
                end = index + 1
                while end < len(css) and css[end] in " \t\r\n":
                    end += 1
                return css[:match.start()] + css[end:]
    raise ValueError(f"unbalanced keyframes block: {name}")


def clean_legacy_artwork_css(css: str, namespace: str) -> str:
    css = re.sub(
        rf"(?m)^[ \t]*--{re.escape(namespace)}-(?:hero|sidebar-art|chat-art)\s*:[^;]+;[ \t]*\r?\n",
        "",
        css,
    )
    css = re.sub(
        r"(?m)^[ \t]*--tessalume-v1-light-chat-mask\s*:[^;]+;[ \t]*\r?\n",
        "",
        css,
    )
    image_properties = (
        "background", "background-image", "background-size", "background-position",
        "background-position-x", "background-position-y", "background-blend-mode",
        "filter", "opacity", "transform", "transform-origin", "translate", "scale",
        "animation", "animation-name",
    )

    def replace_rule(match: re.Match[str]) -> str:
        prefix, selector, body = match.groups()
        branches = split_selector_branches(selector)
        kinds = [artwork_branch_kind(branch, namespace) for branch in branches]
        if not any(kinds):
            return match.group(0)
        target_indices = [index for index, kind in enumerate(kinds) if kind]
        if len(target_indices) != len(branches):
            retained = [branch for branch, kind in zip(branches, kinds) if not kind]
            leading = selector[: len(selector) - len(selector.lstrip())]
            indent_match = re.search(r"(?m)^([ \t]*)\S", selector)
            indent = indent_match.group(1) if indent_match else ""
            return prefix + leading + (",\n" + indent).join(retained) + " {" + body + "}"
        properties = ("background", "background-image") if all(
            kind == "chat-veil" for kind in kinds
        ) else image_properties
        cleaned = remove_declarations(body, properties)
        if not cleaned.strip():
            return prefix
        return prefix + selector.strip() + " {" + cleaned + "}"

    css = CSS_RULE_RE.sub(replace_rule, css)
    css = re.sub(
        r"}(?=(?:html\.|\.[A-Za-z_-]|#[A-Za-z_-]|@media|@keyframes|/\*))",
        "}\n\n",
        css,
    )
    css = re.sub(r"(@media[^{}]+\{)(?=\S)", r"\1\n  ", css)
    css = re.sub(r"(?m)^--", "  --", css)
    for keyframe in ARTWORK_KEYFRAMES.get(namespace, ()):
        css = remove_keyframe(css, keyframe)
    return css


def write_theme(repo_root: Path, directory_name: str, slots: dict, clean_css: bool) -> None:
    theme_dir = repo_root / "themes" / directory_name
    manifest_path = theme_dir / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    theme_id = manifest.get("id")
    if not isinstance(theme_id, str) or not theme_id:
        raise ValueError(f"{directory_name}: manifest id is missing")
    entry_points = manifest.setdefault("entryPoints", {})
    entry_points["artworkDefaults"] = "artwork-defaults.json"
    manifest_path.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )

    document = {
        "$schema": "../../schemas/theme-artwork-defaults-v1.schema.json",
        "schemaVersion": 1,
        "themeId": theme_id,
        "defaultsVersion": "1.0.0",
        "slots": copy.deepcopy(slots),
    }
    (theme_dir / "artwork-defaults.json").write_text(
        json.dumps(document, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    if clean_css:
        script = (theme_dir / "theme.js").read_text(encoding="utf-8")
        namespace_match = re.search(r'namespace:\s*"([a-z][a-z0-9]*)"', script)
        if not namespace_match:
            raise ValueError(f"{directory_name}: canonical namespace is missing")
        css_path = theme_dir / manifest["entryPoints"]["css"]
        css = css_path.read_text(encoding="utf-8")
        cleaned = clean_legacy_artwork_css(css, namespace_match.group(1))
        css_path.write_text(cleaned, encoding="utf-8", newline="\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--write", action="store_true")
    parser.add_argument("--clean-css", action="store_true")
    args = parser.parse_args()
    repo_root = args.repo_root.resolve()
    missing = sorted(
        path.name for path in (repo_root / "themes").iterdir()
        if path.is_dir() and path.name not in THEMES
    )
    if missing:
        raise ValueError(f"unaudited built-in themes: {', '.join(missing)}")
    if not args.write:
        print(json.dumps(THEMES, ensure_ascii=False, indent=2))
        return 0
    for theme_id, slots in THEMES.items():
        write_theme(repo_root, theme_id, slots, args.clean_css)
    print(f"Wrote audited artwork defaults for {len(THEMES)} themes ({len(THEMES) * 6} slots).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
