#!/usr/bin/env python3
"""Append, refresh, or check the frozen Template 1.0 geometry block."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


START = "/* TESSALUME_TEMPLATE_V1_GEOMETRY_START */"
END = "/* TESSALUME_TEMPLATE_V1_GEOMETRY_END */"


def template_block(skill_root: Path) -> str:
    css = (skill_root / "assets" / "theme-template" / "theme.css").read_text(
        encoding="utf-8"
    )
    start = css.index(START)
    end = css.index(END, start) + len(END)
    return css[start:end]


def namespace_for(theme: Path) -> str:
    script = (theme / "theme.js").read_text(encoding="utf-8")
    match = re.search(r'namespace:\s*"([a-z][a-z0-9]*)"', script)
    if not match:
        raise ValueError(f"{theme.name}: canonical namespace is missing")
    return match.group(1)


def current_block(css: str) -> tuple[str | None, str]:
    if START not in css and END not in css:
        return None, ""
    if START not in css or END not in css:
        raise ValueError("geometry block has only one boundary marker")
    start = css.index(START)
    end = css.index(END, start) + len(END)
    return css[start:end], css[end:]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("themes", nargs="+", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    skill_root = Path(__file__).resolve().parents[1]
    canonical = template_block(skill_root)
    failures: list[str] = []
    for theme in args.themes:
        theme = theme.resolve()
        css_path = theme / "theme.css"
        try:
            namespace = namespace_for(theme)
            css = css_path.read_text(encoding="utf-8")
            block, tail = current_block(css)
            expected = canonical.replace("__NS__", namespace)
            if args.check:
                if block != expected:
                    failures.append(f"{theme.name}: frozen geometry differs from Template 1.0")
                if tail.strip():
                    failures.append(f"{theme.name}: CSS exists after the frozen geometry block")
                continue
            if tail.strip():
                raise ValueError(
                    "move theme-specific CSS before the frozen geometry block before syncing"
                )
            if block is None:
                updated = css.rstrip() + "\n\n" + expected + "\n"
            else:
                start = css.index(START)
                updated = css[:start].rstrip() + "\n\n" + expected + "\n"
            css_path.write_text(updated, encoding="utf-8", newline="\n")
            print(f"Synced Template 1.0 geometry: {theme}")
        except (OSError, ValueError) as exc:
            failures.append(f"{theme.name}: {exc}")

    if failures:
        print("\n".join(f"ERROR {message}" for message in failures), file=sys.stderr)
        return 1
    if args.check:
        print(f"PASS Template 1.0 geometry: {len(args.themes)} theme(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
