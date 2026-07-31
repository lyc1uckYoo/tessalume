#!/usr/bin/env python3
"""Detect accidental visual-identity loss while migrating an existing theme."""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path


KEYFRAME_RE = re.compile(r"@(?:-webkit-)?keyframes\s+([A-Za-z_][\w-]*)")
ASSET_RE = re.compile(r"--cts-asset-([A-Za-z0-9][A-Za-z0-9._-]*)")
CLASS_ATTRIBUTE_RE = re.compile(r'class\s*=\s*"([^"]+)"')
CLASS_TOKEN_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_-]*")


def git_text(repo_root: Path, revision: str, relative_path: Path) -> str:
    result = subprocess.run(
        ["git", "show", f"{revision}:{relative_path.as_posix()}"],
        cwd=repo_root,
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=False,
    )
    if result.returncode:
        detail = result.stderr.strip() or "path is absent from the baseline"
        raise ValueError(f"{revision}:{relative_path.as_posix()}: {detail}")
    return result.stdout


def keyframes(css: str) -> set[str]:
    return set(KEYFRAME_RE.findall(css))


def asset_variables(css: str) -> set[str]:
    return set(ASSET_RE.findall(css))


def markup_classes(script: str) -> set[str]:
    classes: set[str] = set()
    for value in CLASS_ATTRIBUTE_RE.findall(script):
        classes.update(CLASS_TOKEN_RE.findall(value))
    return classes


def format_names(names: set[str], limit: int = 14) -> str:
    ordered = sorted(names)
    if len(ordered) <= limit:
        return ", ".join(ordered)
    return ", ".join(ordered[:limit]) + f" … (+{len(ordered) - limit})"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("theme", type=Path)
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--baseline-ref", default="HEAD")
    parser.add_argument("--minimum-class-retention", type=float, default=0.70)
    parser.add_argument("--allow-removed-keyframe", action="append", default=[])
    parser.add_argument("--allow-removed-asset", action="append", default=[])
    parser.add_argument("--allow-removed-class", action="append", default=[])
    args = parser.parse_args()

    if not 0 <= args.minimum_class_retention <= 1:
        parser.error("--minimum-class-retention must be between 0 and 1")

    repo_root = args.repo_root.resolve()
    theme = args.theme.resolve()
    try:
        relative_theme = theme.relative_to(repo_root)
    except ValueError:
        parser.error("theme must be inside --repo-root")

    try:
        baseline_css = git_text(
            repo_root, args.baseline_ref, relative_theme / "theme.css"
        )
        baseline_script = git_text(
            repo_root, args.baseline_ref, relative_theme / "theme.js"
        )
        current_css = (theme / "theme.css").read_text(encoding="utf-8")
        current_script = (theme / "theme.js").read_text(encoding="utf-8")
    except (OSError, ValueError) as exc:
        print(f"ERROR {theme.name}: {exc}", file=sys.stderr)
        return 1

    baseline_keyframes = keyframes(baseline_css)
    current_keyframes = keyframes(current_css)
    removed_keyframes = (
        baseline_keyframes - current_keyframes - set(args.allow_removed_keyframe)
    )

    baseline_assets = asset_variables(baseline_css)
    current_assets = asset_variables(current_css)
    removed_assets = baseline_assets - current_assets - set(args.allow_removed_asset)

    baseline_classes = markup_classes(baseline_script) - set(args.allow_removed_class)
    current_classes = markup_classes(current_script)
    retained_classes = baseline_classes & current_classes
    class_ratio = (
        len(retained_classes) / len(baseline_classes) if baseline_classes else 1.0
    )
    removed_classes = baseline_classes - current_classes

    errors: list[str] = []
    if removed_keyframes:
        errors.append(
            "baseline keyframes disappeared: " + format_names(removed_keyframes)
        )
    if removed_assets:
        errors.append(
            "baseline asset variables disappeared: " + format_names(removed_assets)
        )
    if len(baseline_classes) >= 5 and class_ratio < args.minimum_class_retention:
        errors.append(
            "baseline markup-class retention is "
            f"{class_ratio:.0%} ({len(retained_classes)}/{len(baseline_classes)}); "
            "removed: "
            + format_names(removed_classes)
        )

    if errors:
        for error in errors:
            print(f"ERROR {theme.name}: {error}", file=sys.stderr)
        print(
            "If a loss is intentional and user-authorized, pass the matching "
            "--allow-removed-* option.",
            file=sys.stderr,
        )
        return 1

    print(
        f"PASS migration preservation: {theme.name}; "
        f"keyframes {len(baseline_keyframes)}/{len(current_keyframes)}, "
        f"asset variables {len(baseline_assets)}/{len(current_assets)}, "
        f"markup classes retained {len(retained_classes)}/{len(baseline_classes)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
