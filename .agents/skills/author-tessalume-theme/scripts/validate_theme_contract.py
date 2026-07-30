#!/usr/bin/env python3
"""Validate the canonical Tessalume theme contract."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


REQUIRED_ROLES = (
    "hero",
    "identity",
    "task-left",
    "task-right",
    "memory",
    "composer-accessory",
)

TEMPLATE_V1_PARTS = (
    "hero-copy",
    "hero-kicker",
    "hero-title-light",
    "hero-title-dark",
    "hero-motion",
    "hero-note",
    "identity",
    "identity-emblem",
    "identity-copy",
    "identity-status",
    "task-card-left",
    "task-card-right-secondary",
    "task-card-right-primary",
    "task-card-art",
    "task-card-caption",
    "memory-card",
    "memory-meter",
    "sync-panel",
    "sync-copy",
    "sync-core",
    "sync-meter",
    "sync-state",
    "composer-accessory",
)

GEOMETRY_START = "/* TESSALUME_TEMPLATE_V1_GEOMETRY_START */"
GEOMETRY_END = "/* TESSALUME_TEMPLATE_V1_GEOMETRY_END */"


def geometry_block(css: str) -> tuple[str | None, str]:
    if GEOMETRY_START not in css and GEOMETRY_END not in css:
        return None, ""
    if GEOMETRY_START not in css or GEOMETRY_END not in css:
        raise ValueError("geometry block has only one boundary marker")
    start = css.index(GEOMETRY_START)
    end = css.index(GEOMETRY_END, start) + len(GEOMETRY_END)
    return css[start:end], css[end:]


def canonical_geometry(namespace: str) -> str:
    skill_root = Path(__file__).resolve().parents[1]
    template_css = (
        skill_root / "assets" / "theme-template" / "theme.css"
    ).read_text(encoding="utf-8")
    block, _ = geometry_block(template_css)
    if block is None:
        raise ValueError("canonical Template 1.0 geometry block is missing")
    return block.replace("__NS__", namespace)


def validate_theme(repo_root: Path, theme: Path, expected_author: str | None) -> list[str]:
    errors: list[str] = []
    theme = theme.resolve()
    label = theme.name
    manifest_path = theme / "manifest.json"
    script_path = theme / "theme.js"
    css_path = theme / "theme.css"

    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        return [f"{label}: invalid manifest.json: {exc}"]

    if manifest.get("version") != "1.0":
        errors.append(f'{label}: manifest version must be "1.0"')
    if expected_author and manifest.get("author") != expected_author:
        errors.append(f'{label}: manifest author must be "{expected_author}"')
    if manifest.get("schemaVersion") != 2 or manifest.get("engineVersion") != 2:
        errors.append(f"{label}: schemaVersion and engineVersion must both be 2")
    if manifest.get("type") != "advanced":
        errors.append(f'{label}: manifest type must be "advanced"')

    declared_paths: list[str] = []
    declared_paths.extend((manifest.get("entryPoints") or {}).values())
    declared_paths.extend((manifest.get("previews") or {}).values())
    declared_paths.extend((manifest.get("assets") or {}).values())
    for relative in declared_paths:
        candidate = (theme / relative).resolve()
        try:
            candidate.relative_to(theme)
        except ValueError:
            errors.append(f"{label}: path escapes theme package: {relative}")
            continue
        if not candidate.is_file():
            errors.append(f"{label}: declared file is missing: {relative}")

    try:
        script = script_path.read_text(encoding="utf-8")
        css = css_path.read_text(encoding="utf-8")
    except OSError as exc:
        return errors + [f"{label}: cannot read entry points: {exc}"]

    calls = script.count("context.mountCanonicalTheme(")
    if calls != 1:
        errors.append(f"{label}: theme.js must call context.mountCanonicalTheme exactly once (found {calls})")
    if "data-theme-stage" not in script:
        errors.append(f"{label}: missing data-theme-stage")
    for role in REQUIRED_ROLES:
        if f'data-theme-role="{role}"' not in script:
            errors.append(f"{label}: missing semantic role {role}")
    for forbidden in ("MutationObserver", "context.observe(", "setInterval(", "context.interval("):
        if forbidden in script:
            errors.append(f"{label}: lifecycle code is forbidden in theme.js: {forbidden}")

    match = re.search(r'namespace:\s*"([a-z][a-z0-9]*)"', script)
    if not match:
        errors.append(f"{label}: canonical namespace is missing or invalid")
    else:
        namespace = match.group(1)
        stable_light = f"html.{namespace}-theme.{namespace}-is-task main.{namespace}-main"
        stable_dark = f"html.{namespace}-theme.electron-dark.{namespace}-is-task main.{namespace}-main"
        if stable_light not in css or stable_dark not in css:
            errors.append(f"{label}: light/dark task backgrounds must target the stable themed main")
        if f"--{namespace}-chat-art" not in css:
            errors.append(f"{label}: chat artwork variable is missing")
        if not re.search(
            rf"\.{re.escape(namespace)}-chat-paper::before\s*\{{\s*content\s*:\s*none\s*!important",
            css,
        ):
            errors.append(f"{label}: chat-paper pseudo-element must be disabled")
        if re.search(
            r"main[^{}]*>\s*\*\s*\{[^{}]*position\s*:\s*relative",
            css,
            re.IGNORECASE,
        ):
            errors.append(
                f"{label}: must not reposition every direct main child; "
                "this breaks Codex fixed headers"
            )
        if "z-index:-2" not in css or "z-index:-1" not in css:
            errors.append(
                f"{label}: stable artwork and readability layers must use "
                "negative z-index values"
            )

        template_v1 = bool(
            re.search(r'templateVersion:\s*"1\.0"', script)
        )
        if "templateVersion:" in script and not template_v1:
            errors.append(f'{label}: only templateVersion "1.0" is supported')
        if template_v1:
            if "adaptiveLayout: true" not in script:
                errors.append(f"{label}: Template 1.0 requires adaptiveLayout: true")
            if script.count('data-theme-role="task-right"') != 2:
                errors.append(
                    f"{label}: Template 1.0 requires exactly two task-right cards"
                )
            for priority in ("primary", "secondary"):
                if script.count(f'data-theme-priority="{priority}"') < 1:
                    errors.append(
                        f"{label}: Template 1.0 is missing {priority} priority"
                    )
            sync_tag = re.search(
                r"<[^>]+data-theme-role=\"sync-panel\"[^>]*>",
                script,
            )
            if not sync_tag:
                errors.append(f"{label}: Template 1.0 requires one sync-panel")
            elif 'data-theme-priority="secondary"' not in sync_tag.group(0):
                errors.append(
                    f"{label}: Template 1.0 sync-panel must use secondary priority"
                )
            for part in TEMPLATE_V1_PARTS:
                if f'data-theme-part="{part}"' not in script:
                    errors.append(
                        f"{label}: Template 1.0 is missing structure part {part}"
                    )
            for helper in ("positionComposerAccessory", "positionPanelAboveCards"):
                if helper not in script:
                    errors.append(
                        f"{label}: Template 1.0 must use {helper}"
                    )
            try:
                block, tail = geometry_block(css)
                expected = canonical_geometry(namespace)
                if block != expected:
                    errors.append(
                        f"{label}: frozen geometry differs from Template 1.0"
                    )
                if tail.strip():
                    errors.append(
                        f"{label}: CSS exists after the frozen Template 1.0 geometry"
                    )
            except (OSError, ValueError) as exc:
                errors.append(f"{label}: invalid Template 1.0 geometry: {exc}")

    if css.count("{") != css.count("}"):
        errors.append(f"{label}: CSS braces are unbalanced")

    portable = repo_root / "dist" / "portable-win-x64" / "themes" / theme.name
    if portable.is_dir():
        for name in ("theme.js", "theme.css"):
            source_file = theme / name
            portable_file = portable / name
            if not portable_file.is_file() or source_file.read_bytes() != portable_file.read_bytes():
                errors.append(f"{label}: portable {name} is not synchronized")
        try:
            portable_manifest = json.loads((portable / "manifest.json").read_text(encoding="utf-8"))
            for field in ("id", "version", "author"):
                if portable_manifest.get(field) != manifest.get(field):
                    errors.append(f"{label}: portable manifest {field} is not synchronized")
        except (OSError, json.JSONDecodeError) as exc:
            errors.append(f"{label}: invalid portable manifest.json: {exc}")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("themes", nargs="+", type=Path)
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--author")
    args = parser.parse_args()

    errors: list[str] = []
    for theme in args.themes:
        errors.extend(validate_theme(args.repo_root.resolve(), theme, args.author))
    if errors:
        print("\n".join(f"ERROR {error}" for error in errors), file=sys.stderr)
        return 1
    print(f"PASS canonical contract: {len(args.themes)} theme(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
