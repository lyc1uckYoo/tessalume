#!/usr/bin/env python3
"""Regenerate the examples/ root package from the canonical Template 1.0 asset."""

from __future__ import annotations

import argparse
import shutil
from pathlib import Path


TOKENS = {
    "__SCHEMA__": "../schemas/theme-manifest-v2.schema.json",
    "__THEME_ID__": "example.template-v1",
    "__THEME_NAME__": "旗舰主题模板 1.0",
    "__AUTHOR__": "GitHub username",
    "__NS__": "example",
}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    args = parser.parse_args()

    repo_root = args.repo_root.resolve()
    skill_root = Path(__file__).resolve().parents[1]
    template_root = skill_root / "assets" / "theme-template"
    example_root = repo_root / "examples"
    example_root.mkdir(parents=True, exist_ok=True)

    for name in ("manifest.json", "theme.js", "theme.css"):
        text = (template_root / name).read_text(encoding="utf-8")
        for token, value in TOKENS.items():
            text = text.replace(token, value)
        (example_root / name).write_text(text, encoding="utf-8", newline="\n")

    source_assets = template_root / "assets"
    destination_assets = example_root / "assets"
    if destination_assets.exists():
        shutil.rmtree(destination_assets)
    shutil.copytree(source_assets, destination_assets)
    print(f"Synced Template 1.0 example: {example_root}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
