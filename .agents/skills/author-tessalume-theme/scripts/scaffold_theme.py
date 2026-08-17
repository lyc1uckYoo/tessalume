#!/usr/bin/env python3
"""Create a new theme from the repository-owned canonical template."""

from __future__ import annotations

import argparse
import re
import shutil
from pathlib import Path


TOKENS = {
    "__SCHEMA__": "schema",
    "__THEME_ID__": "theme_id",
    "__THEME_NAME__": "theme_name",
    "__AUTHOR__": "author",
    "__NS__": "namespace",
}
TEXT_EXTENSIONS = {".css", ".js", ".json", ".md", ".svg", ".txt", ".yaml", ".yml"}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--directory", required=True)
    parser.add_argument("--id", dest="theme_id", required=True)
    parser.add_argument("--name", dest="theme_name", required=True)
    parser.add_argument("--author", required=True)
    parser.add_argument("--namespace", required=True)
    args = parser.parse_args()
    args.schema = "../../schemas/theme-manifest-v2.schema.json"

    if not re.fullmatch(r"[a-z0-9][a-z0-9._-]*", args.directory):
        parser.error("--directory must be one safe theme directory name")
    if not re.fullmatch(r"[a-z0-9][a-z0-9._-]*", args.theme_id):
        parser.error("--id must contain only lowercase letters, numbers, dots, dashes or underscores")
    if not args.namespace.isalnum() or not args.namespace[0].isalpha() or not args.namespace.islower():
        parser.error("--namespace must be lowercase letters/numbers and start with a letter")
    skill_root = Path(__file__).resolve().parents[1]
    template = skill_root / "assets" / "theme-template"
    destination = args.repo_root.resolve() / "themes" / args.directory
    if destination.exists():
        parser.error(f"destination already exists: {destination}")

    shutil.copytree(template, destination)
    values = vars(args)
    for path in destination.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in TEXT_EXTENSIONS:
            continue
        text = path.read_text(encoding="utf-8")
        for token, key in TOKENS.items():
            text = text.replace(token, values[key])
        path.write_text(text, encoding="utf-8", newline="\n")

    print(f"Created {destination}")
    print("Template version: 1.0")
    print("Complete character research and the eleven-slot art matrix before accepting assets.")
    print("Replace every placeholder, data-theme-draft marker, sample component and keyframe.")
    print("Complete all six same-named original-asset recommendations in artwork-defaults.json.")
    print("Keep crop/effects/veils/image motion out of CSS; bump defaultsVersion when recommendations change.")
    print("Never read or write user personalization state while authoring a theme.")
    print("Complete the flagship visual-coverage checklist; keep skin.css free of shared geometry.")
    if (args.repo_root.resolve() / "TESSALUME_CREATOR_WORKSPACE.md").is_file():
        print("Run geometry and contract checks, then import this theme folder in Tessalume.")
    else:
        print("Run geometry and contract checks, then the repository-root 一键构建EXE.ps1.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
