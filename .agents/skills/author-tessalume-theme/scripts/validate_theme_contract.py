#!/usr/bin/env python3
"""Validate shared Template 1.0 themes and their isolated skin contract."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


BASE_ASSETS = (
    "hero-light", "hero-dark", "sidebar-light", "sidebar-dark", "chat-light", "chat-dark",
    "task-left", "task-right-secondary", "task-right-primary", "memory-light", "memory-dark",
)
OPTIONAL_DARK_TASK_ASSETS = (
    "task-left-dark", "task-right-secondary-dark", "task-right-primary-dark",
)
REQUIRED_SLOTS = (
    "stageClass", "hero", "identity", "taskLeft", "taskSecondary", "taskPrimary",
    "memory", "syncPanel", "composerAccessory",
)
REQUIRED_INNER_PARTS = (
    "hero-kicker", "hero-title-light", "hero-title-dark", "hero-motion", "hero-note",
    "identity-emblem", "identity-copy", "identity-status", "task-card-art",
    "task-card-caption", "memory-meter", "sync-copy", "sync-core", "sync-meter", "sync-state",
)
SECTION_TITLES = (
    "01 Light and dark tokens", "02 App and native planes", "03 Sidebar skin",
    "04 Home skin", "05 Identity skin", "06 Task-card skin", "07 Memory skin",
    "08 Sync-panel skin", "09 Message and output frames", "10 Composer skin",
    "11 Character components", "12 Theme-only media behavior", "13 Character keyframes",
)
ASSET_VARIABLE_RE = re.compile(r"--tessalume-asset-([A-Za-z0-9][A-Za-z0-9._-]*)")
CSS_RULE_RE = re.compile(r"([^{}]+)\{([^{}]*)\}")
KEYFRAME_RE = re.compile(r"@keyframes\s+([A-Za-z_][\w-]*)")
CLASS_NAME_RE = re.compile(r'(?:className|stageClass)\s*:\s*"([^"]+)"')
GEOMETRY_PROPERTY_RE = re.compile(
    r"(?:^|;)\s*(position|inset|left|right|top|bottom|width|height|min-width|max-width|"
    r"min-height|max-height|display|z-index|box-sizing|overflow-x|flex|"
    r"align-items|justify-content|gap|margin(?:-(?:left|right|top|bottom))?|"
    r"padding(?:-(?:left|right|top|bottom))?|pointer-events|white-space)\s*:",
    re.IGNORECASE,
)
LEGACY_TOKENS = (
    "TESSALUME_TEMPLATE_V1_SURFACE", "TESSALUME_TEMPLATE_V1_GEOMETRY",
    "Moonheart Fox sovereign", "Requiem Stage",
)
DRAFT_TOKENS = (
    "data-theme-draft=", "assets/placeholder.svg", "assets/placeholder.png",
    "在这里填写", "亮色主标题", "暗色主标题", "角色挂件",
)


def selector_has_property(
    css: str,
    selector_tokens: tuple[str, ...],
    property_name: str,
) -> bool:
    property_re = re.compile(rf"(?:^|;)\s*{re.escape(property_name)}\s*:", re.IGNORECASE)
    for selector, body in CSS_RULE_RE.findall(css):
        if all(token in selector for token in selector_tokens) and property_re.search(body):
            return True
    return False


def css_entry(theme: Path, manifest: dict) -> Path:
    relative = (manifest.get("entryPoints") or {}).get("css")
    if relative != "skin.css":
        raise ValueError('entryPoints.css must be "skin.css"')
    return theme / relative


def duplicate_selectors(css: str) -> list[str]:
    duplicates: list[str] = []
    contexts: list[str] = []
    rule_depths: list[int] = []
    seen: set[tuple[tuple[str, ...], str]] = set()
    depth = 0
    for raw_line in css.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("/*"):
            continue
        if line.endswith("{"):
            header = line[:-1].strip()
            if header.startswith("@media") or header.startswith("@keyframes"):
                contexts.append(header)
                rule_depths.append(depth)
            elif not re.fullmatch(r"(?:from|to|[\d\s%,.]+)", header):
                key = (tuple(contexts), header)
                if key in seen:
                    duplicates.append(header)
                seen.add(key)
            depth += 1
        depth -= line.count("}")
        while rule_depths and depth <= rule_depths[-1]:
            rule_depths.pop()
            contexts.pop()
    return duplicates


def selector_targets_outer(selector: str, outer_classes: set[str]) -> bool:
    if "[data-theme-role=" in selector or "[data-theme-stage]" in selector:
        return True
    for branch in selector.split(","):
        branch = branch.strip()
        if "::" in branch:
            continue
        for class_name in outer_classes:
            match = re.search(rf"\.{re.escape(class_name)}(?![A-Za-z0-9_-])", branch)
            if match and re.fullmatch(
                r"(?:\[[^\]]+\]|:[A-Za-z-]+(?:\([^)]*\))?)*",
                branch[match.end():],
            ):
                return True
    return False


def validate_theme(
    repo_root: Path,
    theme: Path,
    expected_author: str | None,
    check_portable: bool,
) -> list[str]:
    errors: list[str] = []
    theme = theme.resolve()
    label = theme.name
    try:
        manifest = json.loads((theme / "manifest.json").read_text(encoding="utf-8"))
        script = (theme / "theme.js").read_text(encoding="utf-8")
        skin_path = css_entry(theme, manifest)
        css = skin_path.read_text(encoding="utf-8")
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        return [f"{label}: {exc}"]

    if manifest.get("version") != "1.0":
        errors.append(f'{label}: manifest version must be "1.0"')
    if expected_author and manifest.get("author") != expected_author:
        errors.append(f'{label}: manifest author must be "{expected_author}"')
    if manifest.get("schemaVersion") != 2 or manifest.get("engineVersion") != 2:
        errors.append(f"{label}: schemaVersion and engineVersion must both be 2")
    if manifest.get("type") != "advanced":
        errors.append(f'{label}: manifest type must be "advanced"')
    if manifest.get("template") != {"id": "flagship", "version": "1.0", "style": "shared"}:
        errors.append(f"{label}: shared Template 1.0 declaration is missing or invalid")

    declared_assets = manifest.get("assets") or {}
    missing_keys = set(BASE_ASSETS) - set(declared_assets)
    unknown_keys = set(declared_assets) - set(BASE_ASSETS) - set(OPTIONAL_DARK_TASK_ASSETS)
    if missing_keys:
        errors.append(f"{label}: missing standard assets: {', '.join(sorted(missing_keys))}")
    if unknown_keys:
        errors.append(f"{label}: nonstandard asset keys: {', '.join(sorted(unknown_keys))}")
    for key, relative in declared_assets.items():
        candidate = (theme / relative).resolve()
        try:
            candidate.relative_to(theme)
        except ValueError:
            errors.append(f"{label}: asset escapes package: {relative}")
            continue
        if not candidate.is_file():
            errors.append(f"{label}: missing asset: {relative}")
        if candidate.stem != key:
            errors.append(f"{label}: asset filename must match its slot: {key} -> {relative}")

    for relative in (manifest.get("entryPoints") or {}).values():
        if not (theme / relative).is_file():
            errors.append(f"{label}: missing entry point: {relative}")

    serialized_manifest = json.dumps(manifest, ensure_ascii=False)
    for token in DRAFT_TOKENS:
        if token in serialized_manifest or token in script or token in css:
            errors.append(f"{label}: unresolved starter draft remains: {token}")

    if script.count("context.renderTemplateV1(") != 1:
        errors.append(f"{label}: theme.js must call context.renderTemplateV1 exactly once")
    if script.count("context.mountCanonicalTheme(") != 1:
        errors.append(f"{label}: theme.js must call context.mountCanonicalTheme exactly once")
    for slot in REQUIRED_SLOTS:
        if not re.search(rf"\b{re.escape(slot)}\s*:", script):
            errors.append(f"{label}: missing Template 1.0 slot {slot}")
    for part in REQUIRED_INNER_PARTS:
        if f'data-theme-part="{part}"' not in script:
            errors.append(f"{label}: missing Template 1.0 inner part {part}")
    for forbidden in (
        "root.innerHTML", "insertAdjacentHTML", "data-theme-role=", "data-theme-stage",
        "MutationObserver", "context.observe(", "setInterval(", "context.interval(",
    ):
        if forbidden in script:
            errors.append(f"{label}: forbidden theme-owned structure/lifecycle code: {forbidden}")
    for required in (
        'templateVersion: "1.0"', "adaptiveLayout: true", "preserveRoot: true",
        "positionComposerAccessory", "positionPanelAboveCards",
    ):
        if required not in script:
            errors.append(f"{label}: missing canonical runtime declaration: {required}")
    if not re.search(
        r"positionPanelAboveCards\s*\(\s*main\s*,.*?,.*?,\s*320\s*,\s*56\s*,\s*40\s*,?\s*\)",
        script,
        re.DOTALL,
    ):
        errors.append(f"{label}: sync panel must use canonical 320, 56, 40 positioning")

    namespace_match = re.search(r'namespace:\s*"([a-z][a-z0-9]*)"', script)
    if not namespace_match:
        errors.append(f"{label}: canonical namespace is missing")
    else:
        namespace = namespace_match.group(1)
        asset_refs = set(ASSET_VARIABLE_RE.findall(css))
        undeclared = asset_refs - set(declared_assets)
        unused = set(declared_assets) - asset_refs
        if undeclared:
            errors.append(f"{label}: undeclared asset variables: {', '.join(sorted(undeclared))}")
        if unused:
            errors.append(f"{label}: declared assets are unused: {', '.join(sorted(unused))}")
        stable_light = f"html.{namespace}-theme.{namespace}-is-task main.{namespace}-main"
        stable_dark = f"html.{namespace}-theme.electron-dark.{namespace}-is-task main.{namespace}-main"
        if stable_light not in css or stable_dark not in css:
            errors.append(f"{label}: stable light/dark task artwork selectors are missing")
        if f"--{namespace}-chat-art" not in css:
            errors.append(f"{label}: chat artwork token is missing")
        quality_gate = (manifest.get("config") or {}).get("qualityGate")
        if quality_gate == "flagship-complete-1":
            visual_coverage = {
                "explicit light/dark visibility": (
                    f".{namespace}-dark-only", f".{namespace}-light-only", "display:none",
                ),
                "layered sidebar artwork": ("aside.app-shell-left-panel::after",),
                "task-title row": (f".{namespace}-task-title",),
                "environment panel sections": (f".{namespace}-output-panel>div>section",),
                "environment panel item buttons": ('data-slot="thread-summary-panel-item-button"',),
                "composer footer controls": ("_footer_",),
                "composer model picker": ("_ModelPickerTrigger",),
            }
            compact_css = re.sub(r"\s+", "", css)
            for surface, fragments in visual_coverage.items():
                if not all(fragment.replace(" ", "") in compact_css for fragment in fragments):
                    errors.append(f"{label}: flagship visual coverage missing: {surface}")
            if not selector_has_property(
                css,
                (f".{namespace}-message-assistant", f".{namespace}-markdown"),
                "padding",
            ):
                errors.append(f"{label}: assistant message frame needs deliberate inner padding")
            if not selector_has_property(
                css,
                (f".{namespace}-message-user", "data-user-message-bubble"),
                "padding",
            ):
                errors.append(f"{label}: user message frame needs deliberate inner padding")
        elif quality_gate is not None:
            errors.append(f"{label}: unknown flagship quality gate: {quality_gate}")
        for selector, body in CSS_RULE_RE.findall(css):
            normalized_selector = selector.strip()
            properties = {
                match.group(1).lower()
                for match in re.finditer(r"(?:^|;)\s*([\w-]+)\s*:", body)
            }
            artwork_layer = None
            if "aside.app-shell-left-panel::after" in normalized_selector:
                artwork_layer = "sidebar"
            elif (f".{namespace}-home" in normalized_selector and
                  "div:first-child>div:first-child>div:first-child::before" in normalized_selector):
                artwork_layer = "hero"
            elif (f"-is-task main.{namespace}-main::before" in normalized_selector):
                artwork_layer = "chat"
            if artwork_layer:
                hard_coded_correction = properties & {"filter", "opacity"}
                if hard_coded_correction:
                    errors.append(
                        f"{label}: {artwork_layer} artwork correction belongs to Tessalume settings, "
                        f"not {normalized_selector}: {', '.join(sorted(hard_coded_correction))}"
                    )
            artwork_owners = {
                f"var(--{namespace}-hero)": f".{namespace}-home",
                f"var(--{namespace}-sidebar-art)": "aside.app-shell-left-panel::after",
                f"var(--{namespace}-chat-art)": f"main.{namespace}-main::before",
            }
            for artwork_token, owner_selector in artwork_owners.items():
                if artwork_token in body and owner_selector not in normalized_selector:
                    errors.append(
                        f"{label}: {artwork_token} must be painted only by {owner_selector}"
                    )
            if f"main.{namespace}-main::before" in normalized_selector or f"main.{namespace}-main::after" in normalized_selector:
                shared = properties & {"content", "position", "inset", "z-index", "pointer-events", "isolation"}
                if shared:
                    errors.append(
                        f"{label}: skin owns shared task-canvas structure on {normalized_selector}: "
                        f"{', '.join(sorted(shared))}"
                    )
            owns_transparent_fill = (
                f".{namespace}-chat-paper" in normalized_selector or
                (
                    f".{namespace}-message-assistant" in normalized_selector and
                    f".{namespace}-markdown" in normalized_selector
                ) or
                (
                    f".{namespace}-message-user" in normalized_selector and
                    (f".{namespace}-markdown" in normalized_selector or "data-user-message-bubble" in normalized_selector)
                )
            )
            shared_fill = properties & {
                "background", "background-color", "background-image", "backdrop-filter",
                "-webkit-backdrop-filter",
            }
            if owns_transparent_fill and shared_fill:
                errors.append(
                    f"{label}: skin owns shared transparent fill on {normalized_selector}: "
                    f"{', '.join(sorted(shared_fill))}"
                )

    for token in LEGACY_TOKENS:
        if token in css or token in script:
            errors.append(f"{label}: legacy token remains: {token}")
    if "[data-theme-role=" in css or "[data-theme-stage]" in css:
        errors.append(f"{label}: skin.css contains runtime-owned semantic geometry")
    if css.count("{") != css.count("}"):
        errors.append(f"{label}: CSS braces are unbalanced")

    section_positions = [css.find(f"/* {title} */") for title in SECTION_TITLES]
    if any(position < 0 for position in section_positions):
        missing = [title for title, position in zip(SECTION_TITLES, section_positions) if position < 0]
        errors.append(f"{label}: missing canonical skin sections: {', '.join(missing)}")
    elif section_positions != sorted(section_positions):
        errors.append(f"{label}: canonical skin sections are out of order")

    outer_classes: set[str] = set()
    for value in CLASS_NAME_RE.findall(script):
        outer_classes.update(value.split())
    for selector, body in CSS_RULE_RE.findall(css):
        if selector_targets_outer(selector.strip(), outer_classes):
            match = GEOMETRY_PROPERTY_RE.search(body)
            if match:
                errors.append(f"{label}: skin owns {match.group(1)} on shared outer slot: {selector.strip()}")

    portable = repo_root / "dist" / "portable-win-x64" / "themes" / theme.name
    if check_portable and portable.is_dir():
        for name in ("theme.js", manifest["entryPoints"]["css"]):
            source = theme / name
            target = portable / name
            if not target.is_file():
                errors.append(f"{label}: portable {name} is not synchronized")
            elif source.read_text(encoding="utf-8").replace("\r\n", "\n") != target.read_text(encoding="utf-8").replace("\r\n", "\n"):
                errors.append(f"{label}: portable {name} is not synchronized")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("themes", nargs="+", type=Path)
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--author")
    parser.add_argument("--check-portable", action="store_true")
    args = parser.parse_args()
    errors: list[str] = []
    for theme in args.themes:
        errors.extend(validate_theme(
            args.repo_root.resolve(),
            theme,
            args.author,
            args.check_portable,
        ))
    if errors:
        print("\n".join(f"ERROR {error}" for error in errors), file=sys.stderr)
        return 1
    print(f"PASS canonical contract: {len(args.themes)} theme(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
