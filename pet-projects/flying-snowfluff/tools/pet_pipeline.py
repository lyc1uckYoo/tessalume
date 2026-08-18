#!/usr/bin/env python3
"""Validate, extract, and preview the Flying Snowfluff Codex pet atlas."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "pet.json"
SHEET = ROOT / "spritesheet.webp"
COLS = 8
ROWS = 11
CELL_WIDTH = 192
CELL_HEIGHT = 208
SHEET_SIZE = (COLS * CELL_WIDTH, ROWS * CELL_HEIGHT)
EDGE_MARGIN = 2
EDGE_ALPHA_LIMIT = 24
ROW_NAMES = (
    "idle",
    "move-right",
    "move-left",
    "wave-touch",
    "jump",
    "blocked",
    "needs-input",
    "running",
    "ready",
    "gaze-upper",
    "gaze-lower",
)
EXPECTED_USED_COLUMNS = (
    (0, 1, 2, 3, 4, 5, 6),
    (0, 1, 2, 3, 4, 5, 6, 7),
    (0, 1, 2, 3, 4, 5, 6, 7),
    (0, 1, 2, 3),
    (0, 1, 2, 3, 4),
    (0, 1, 2, 3, 4, 5, 6, 7),
    (0, 1, 2, 3, 4, 5),
    (0, 1, 2, 3, 4, 5),
    (0, 1, 2, 3, 4, 5),
    (0, 1, 2, 3, 4, 5, 6, 7),
    (0, 1, 2, 3, 4, 5, 6, 7),
)
ANIMATION_COLUMNS = (
    (0, 1, 2, 3, 4, 5),
    (0, 1, 2, 3, 4, 5, 6, 7),
    (0, 1, 2, 3, 4, 5, 6, 7),
    (0, 1, 2, 3),
    (0, 1, 2, 3, 4),
    (0, 1, 2, 3, 4, 5, 6, 7),
    (0, 1, 2, 3, 4, 5),
    (0, 1, 2, 3, 4, 5),
    (0, 1, 2, 3, 4, 5),
)
FRAME_DURATIONS_MS = (
    (280, 110, 110, 140, 140, 320),
    (120, 120, 120, 120, 120, 120, 120, 220),
    (120, 120, 120, 120, 120, 120, 120, 220),
    (140, 140, 140, 280),
    (140, 140, 140, 140, 280),
    (140, 140, 140, 140, 140, 140, 140, 240),
    (150, 150, 150, 150, 150, 260),
    (120, 120, 120, 120, 120, 220),
    (150, 150, 150, 150, 150, 280),
)


def load_sheet(sheet_path: Path = SHEET) -> Image.Image:
    return Image.open(sheet_path).convert("RGBA")


def frame_at(sheet: Image.Image, row: int, col: int) -> Image.Image:
    left = col * CELL_WIDTH
    top = row * CELL_HEIGHT
    return sheet.crop((left, top, left + CELL_WIDTH, top + CELL_HEIGHT))


def alpha_centroid(frame: Image.Image) -> tuple[float, float] | None:
    alpha = frame.getchannel("A")
    pixels = alpha.tobytes()
    weight = sum(pixels)
    if not weight:
        return None
    x_weight = 0
    y_weight = 0
    for index, value in enumerate(pixels):
        if value:
            x = index % CELL_WIDTH
            y = index // CELL_WIDTH
            x_weight += x * value
            y_weight += y * value
    return (x_weight / weight, y_weight / weight)


def frame_digest(frame: Image.Image) -> str:
    return hashlib.sha256(frame.tobytes()).hexdigest()


def edge_alpha_count(frame: Image.Image, margin: int = EDGE_MARGIN) -> int:
    """Count non-transparent pixels inside the outer cell margin once."""
    alpha = frame.getchannel("A")
    pixels = alpha.load()
    return sum(
        1
        for y in range(CELL_HEIGHT)
        for x in range(CELL_WIDTH)
        if (x < margin or x >= CELL_WIDTH - margin or y < margin or y >= CELL_HEIGHT - margin)
        and pixels[x, y]
    )


def validate(
    sheet_path: Path,
    report_path: Path | None,
    baseline_path: Path | None = None,
    unchanged_rows: tuple[int, ...] = (),
) -> int:
    errors: list[str] = []
    warnings: list[str] = []

    try:
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"ERROR: cannot read pet.json: {exc}")
        return 1

    if manifest.get("id") != "flying-snowfluff":
        errors.append("pet.json id must be flying-snowfluff")
    if manifest.get("spriteVersionNumber") != 2:
        errors.append("spriteVersionNumber must remain 2 for the desktop V2 atlas protocol")
    if manifest.get("spritesheetPath") != SHEET.name:
        errors.append("spritesheetPath does not point to spritesheet.webp")

    try:
        source = Image.open(sheet_path)
        source_mode = source.mode
        source_size = source.size
        sheet = source.convert("RGBA")
    except OSError as exc:
        print(f"ERROR: cannot read spritesheet: {exc}")
        return 1

    if source_size != SHEET_SIZE:
        errors.append(f"spritesheet must be {SHEET_SIZE[0]}x{SHEET_SIZE[1]}, got {source_size}")
    if "A" not in source.getbands():
        errors.append(f"spritesheet must contain alpha, source mode is {source_mode}")

    rgba_bytes = sheet.tobytes()
    transparent_pixels = 0
    transparent_rgb_residue = 0
    for index in range(0, len(rgba_bytes), 4):
        if rgba_bytes[index + 3] != 0:
            continue
        transparent_pixels += 1
        if rgba_bytes[index] or rgba_bytes[index + 1] or rgba_bytes[index + 2]:
            transparent_rgb_residue += 1
    if transparent_rgb_residue:
        errors.append(
            f"spritesheet has {transparent_rgb_residue} fully transparent pixels "
            "with non-zero RGB residue"
        )

    all_hashes: dict[str, list[str]] = {}
    row_reports: list[dict[str, Any]] = []
    for row, name in enumerate(ROW_NAMES):
        occupied = 0
        centroids: list[tuple[float, float]] = []
        row_hashes: list[str] = []
        edge_contacts: list[int] = []
        outer_edge_alpha: list[int] = []
        occupied_columns: list[int] = []
        for col in range(COLS):
            frame = frame_at(sheet, row, col)
            bbox = frame.getchannel("A").getbbox()
            digest = frame_digest(frame)
            if bbox is None:
                continue
            occupied += 1
            occupied_columns.append(col)
            row_hashes.append(digest)
            all_hashes.setdefault(digest, []).append(f"{name}[{col}]")
            centroid = alpha_centroid(frame)
            if centroid is not None:
                centroids.append(centroid)
            left, top, right, bottom = bbox
            if left < 6 or top < 6 or right > CELL_WIDTH - 6 or bottom > CELL_HEIGHT - 6:
                edge_contacts.append(col)
            outer_edge_alpha.append(edge_alpha_count(frame))

        unique = len(set(row_hashes))
        x_span = max((point[0] for point in centroids), default=0) - min(
            (point[0] for point in centroids), default=0
        )
        y_span = max((point[1] for point in centroids), default=0) - min(
            (point[1] for point in centroids), default=0
        )
        expected_columns = set(EXPECTED_USED_COLUMNS[row])
        actual_columns = set(occupied_columns)
        missing_columns = sorted(expected_columns - actual_columns)
        unexpected_columns = sorted(actual_columns - expected_columns)
        if missing_columns:
            errors.append(f"{name}: required cells are empty: {missing_columns}")
        if unexpected_columns:
            errors.append(
                f"{name}: protocol-unused cells must be transparent: {unexpected_columns}"
            )
        if name.startswith("gaze-") and unique < COLS:
            warnings.append(f"{name}: direction samples are not all unique ({unique}/{COLS})")
        outer_edge_violations = [
            occupied_columns[index]
            for index, count in enumerate(outer_edge_alpha)
            if count > EDGE_ALPHA_LIMIT
        ]
        if outer_edge_violations:
            warnings.append(
                f"{name}: outer {EDGE_MARGIN}px alpha exceeds {EDGE_ALPHA_LIMIT} pixels "
                f"in cells {outer_edge_violations}"
            )
        row_reports.append(
            {
                "row": row,
                "name": name,
                "occupied": occupied,
                "expected_occupied": len(expected_columns),
                "occupied_columns": occupied_columns,
                "unique_frames": unique,
                "centroid_span": {"x": round(x_span, 2), "y": round(y_span, 2)},
                "near_edge_cells": edge_contacts,
                "outer_edge_alpha_counts": outer_edge_alpha,
                "outer_edge_alpha_limit": EDGE_ALPHA_LIMIT,
                "outer_edge_violations": outer_edge_violations,
            }
        )

    duplicate_groups = [locations for locations in all_hashes.values() if len(locations) > 1]
    report = {
        "manifest": manifest,
        "spritesheet": {
            "path": str(sheet_path),
            "source_mode": source_mode,
            "size": list(source_size),
            "cell_size": [CELL_WIDTH, CELL_HEIGHT],
            "transparent_pixels": transparent_pixels,
            "transparent_rgb_residue": transparent_rgb_residue,
        },
        "rows": row_reports,
        "duplicate_groups": duplicate_groups,
        "errors": errors,
        "warnings": warnings,
    }

    if baseline_path is not None:
        baseline_comparison: dict[str, Any] = {
            "path": str(baseline_path),
            "unchanged_rows": list(unchanged_rows),
            "matching_rows": [],
        }
        try:
            baseline = Image.open(baseline_path).convert("RGBA")
            if baseline.size != sheet.size:
                errors.append(
                    f"baseline size {baseline.size} does not match candidate size {sheet.size}"
                )
            else:
                for row in unchanged_rows:
                    if row < 0 or row >= ROWS:
                        errors.append(f"unchanged row index is out of range: {row}")
                        continue
                    top = row * CELL_HEIGHT
                    box = (0, top, SHEET_SIZE[0], top + CELL_HEIGHT)
                    if baseline.crop(box).tobytes() != sheet.crop(box).tobytes():
                        errors.append(f"row {row} changed but was declared unchanged")
                    else:
                        baseline_comparison["matching_rows"].append(row)
        except OSError as exc:
            errors.append(f"cannot read baseline spritesheet: {exc}")
        report["baseline_comparison"] = baseline_comparison

    if report_path:
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    for row in row_reports:
        print(
            f"{row['row']:02d} {row['name']:<12} "
            f"occupied={row['occupied']}/{COLS} unique={row['unique_frames']}/{COLS} "
            f"centroid-span=({row['centroid_span']['x']}, {row['centroid_span']['y']})"
        )
    for message in warnings:
        print(f"WARN: {message}")
    for message in errors:
        print(f"ERROR: {message}")
    print(f"RESULT: {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


def extract(sheet_path: Path, out_dir: Path) -> int:
    sheet = load_sheet(sheet_path)
    for row, name in enumerate(ROW_NAMES):
        row_dir = out_dir / f"{row:02d}-{name}"
        row_dir.mkdir(parents=True, exist_ok=True)
        for col in range(COLS):
            frame_at(sheet, row, col).save(row_dir / f"{col:02d}.png")
    print(f"Extracted {ROWS * COLS} cells to {out_dir}")
    return 0


def preview(sheet_path: Path, out_dir: Path, scale: int, duration: int | None) -> int:
    sheet = load_sheet(sheet_path)
    out_dir.mkdir(parents=True, exist_ok=True)
    for row, name in enumerate(ROW_NAMES[:9]):
        frames = [frame_at(sheet, row, col) for col in ANIMATION_COLUMNS[row]]
        durations = (
            [duration] * len(frames)
            if duration is not None
            else list(FRAME_DURATIONS_MS[row])
        )
        if scale != 1:
            frames = [
                frame.resize((CELL_WIDTH * scale, CELL_HEIGHT * scale), Image.Resampling.LANCZOS)
                for frame in frames
            ]
        frames[0].save(
            out_dir / f"{row:02d}-{name}.gif",
            save_all=True,
            append_images=frames[1:],
            duration=durations,
            loop=0,
            disposal=2,
            transparency=0,
        )
    print(f"Generated state previews in {out_dir}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate_parser = subparsers.add_parser("validate", help="validate manifest and atlas")
    validate_parser.add_argument("--sheet", type=Path, default=SHEET)
    validate_parser.add_argument("--report", type=Path)
    validate_parser.add_argument("--baseline", type=Path)
    validate_parser.add_argument(
        "--unchanged-rows",
        default="",
        help="comma-separated row indexes that must match --baseline",
    )

    extract_parser = subparsers.add_parser("extract", help="extract all 88 atlas cells")
    extract_parser.add_argument("--sheet", type=Path, default=SHEET)
    extract_parser.add_argument("--out", type=Path, default=ROOT / "build" / "frames")

    preview_parser = subparsers.add_parser("preview", help="build animated previews for state rows")
    preview_parser.add_argument("--sheet", type=Path, default=SHEET)
    preview_parser.add_argument("--out", type=Path, default=ROOT / "build" / "previews")
    preview_parser.add_argument("--scale", type=int, default=2)
    preview_parser.add_argument(
        "--duration",
        type=int,
        help="optional uniform frame duration; defaults to protocol timings",
    )

    args = parser.parse_args()
    if args.command == "validate":
        try:
            unchanged_rows = tuple(
                int(value.strip())
                for value in args.unchanged_rows.split(",")
                if value.strip()
            )
        except ValueError:
            parser.error("--unchanged-rows must be a comma-separated list of integers")
        return validate(args.sheet, args.report, args.baseline, unchanged_rows)
    if args.command == "extract":
        return extract(args.sheet, args.out)
    return preview(args.sheet, args.out, args.scale, args.duration)


if __name__ == "__main__":
    raise SystemExit(main())
