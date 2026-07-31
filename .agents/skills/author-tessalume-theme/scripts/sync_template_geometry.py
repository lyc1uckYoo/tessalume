#!/usr/bin/env python3
"""Validate runtime-owned Template 1.0 geometry and theme skin isolation."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


REQUIRED_SHARED_SELECTORS = (
    '[data-cts-surface="chat-paper"]',
    '[data-cts-message="assistant"]',
    '[data-cts-message="user"]',
    '[data-theme-role="hero"]',
    '[data-theme-role="identity"]',
    '[data-theme-role="task-left"]',
    '[data-theme-role="task-right"]',
    '[data-theme-role="memory"]',
    '[data-theme-role="sync-panel"]',
    '[data-theme-role="composer-accessory"]',
)

FORBIDDEN_SKIN_PATTERNS = (
    "TESSALUME_TEMPLATE_V1_SURFACE",
    "TESSALUME_TEMPLATE_V1_GEOMETRY",
    "[data-theme-role=",
    "[data-theme-stage]",
    "home-hero-height",
    "height:502px!important",
    "flex:0 0 526px!important",
    "top:calc(100% - 142px)!important",
)


def find_repo_root(theme: Path) -> Path:
    for candidate in (theme, *theme.parents):
        if (candidate / "src" / "CodexThemeStudio.App").is_dir():
            return candidate
    raise ValueError("repository root could not be located")


def skin_path(theme: Path) -> Path:
    manifest = json.loads((theme / "manifest.json").read_text(encoding="utf-8"))
    relative = (manifest.get("entryPoints") or {}).get("css")
    if not relative:
        raise ValueError("manifest entryPoints.css is missing")
    return theme / relative


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("themes", nargs="+", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    failures: list[str] = []
    shared_checked: set[Path] = set()
    for theme_argument in args.themes:
        theme = theme_argument.resolve()
        try:
            repo_root = find_repo_root(theme)
            shared = repo_root / "src" / "CodexThemeStudio.App" / "Compatibility" / "theme-template-v1.css"
            if shared not in shared_checked:
                shared_css = shared.read_text(encoding="utf-8")
                for selector in REQUIRED_SHARED_SELECTORS:
                    if selector not in shared_css:
                        failures.append(f"shared Template 1.0 CSS is missing {selector}")
                if shared_css.count("{") != shared_css.count("}"):
                    failures.append("shared Template 1.0 CSS braces are unbalanced")
                shared_checked.add(shared)

            css = skin_path(theme).read_text(encoding="utf-8")
            for pattern in FORBIDDEN_SKIN_PATTERNS:
                if pattern in css:
                    failures.append(f"{theme.name}: skin contains runtime-owned geometry: {pattern}")
            if re.search(r"main\s*>\s*\*\s*\{[^{}]*position\s*:", css, re.IGNORECASE):
                failures.append(f"{theme.name}: skin repositions direct main children")
        except (OSError, ValueError, json.JSONDecodeError) as exc:
            failures.append(f"{theme.name}: {exc}")

    if failures:
        print("\n".join(f"ERROR {message}" for message in failures), file=sys.stderr)
        return 1
    print(f"PASS shared Template 1.0 geometry: {len(args.themes)} theme(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
