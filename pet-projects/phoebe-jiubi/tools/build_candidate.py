from __future__ import annotations

import argparse
import json
import shutil
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw


CELL_W = 192
CELL_H = 208
ATLAS_W = 1536
ATLAS_H = 2288
PREVIEW_SCALE = 3
SHOWCASE_SCALE = 2
BACKGROUND = (18, 22, 39, 255)
LABEL = (194, 211, 255, 255)


@dataclass(frozen=True)
class PreviewSpec:
    row: int
    frames: int
    filename: str
    durations: tuple[int, ...]
    columns: tuple[int, ...] | None = None


PREVIEWS = (
    PreviewSpec(
        0,
        9,
        "00-idle.gif",
        (1500, 1500, 60, 60, 70, 50, 50, 1650, 1660),
        (0, 0, 1, 2, 3, 4, 1, 5, 5),
    ),
    PreviewSpec(1, 8, "01-move-right.gif", (90, 85, 85, 90, 85, 85, 90, 110)),
    PreviewSpec(2, 8, "02-move-left.gif", (90, 85, 85, 90, 85, 85, 90, 110)),
    PreviewSpec(3, 4, "03-wave-touch.gif", (110, 80, 90, 130)),
    PreviewSpec(4, 5, "04-jump.gif", (120, 85, 115, 85, 140)),
    PreviewSpec(5, 8, "05-blocked.gif", (110, 90, 90, 110, 150, 140, 100, 120)),
    PreviewSpec(6, 6, "06-needs-input.gif", (130, 110, 120, 130, 120, 150)),
    PreviewSpec(7, 6, "07-running.gif", (100, 90, 100, 120, 90, 110)),
    PreviewSpec(8, 6, "08-ready.gif", (120, 100, 110, 140, 100, 150)),
)


def parse_args() -> argparse.Namespace:
    project = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(
        description="Build the Tessalume live-preview candidate from an approved Codex v2 atlas."
    )
    parser.add_argument(
        "--sheet",
        type=Path,
        default=project / "build" / "hatch-run" / "final" / "spritesheet-extended.webp",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=project / "build" / "final-motion-candidate",
    )
    parser.add_argument(
        "--runtime-sheet",
        type=Path,
        default=(
            project
            / "build"
            / "hatch-run"
            / "runtime-experiment"
            / "spritesheet-animated.png"
        ),
        help="Actual PNG/APNG spritesheet loaded by Codex and Tessalume runtime preview.",
    )
    parser.add_argument(
        "--jump-only",
        action="store_true",
        help="Preserve every existing preview revision except jump and showcase.",
    )
    parser.add_argument(
        "--idle-only",
        action="store_true",
        help="Preserve every existing preview revision except idle and showcase.",
    )
    return parser.parse_args()


def cell(sheet: Image.Image, row: int, column: int) -> Image.Image:
    left = column * CELL_W
    top = row * CELL_H
    return sheet.crop((left, top, left + CELL_W, top + CELL_H)).convert("RGBA")


def preview_tile(sprite: Image.Image, extra_height: int = 0) -> Image.Image:
    tile = Image.new("RGBA", (CELL_W, CELL_H + extra_height), BACKGROUND)
    tile.alpha_composite(sprite, (0, 0))
    return tile


def save_gif(frames: list[Image.Image], path: Path, durations: list[int] | int) -> None:
    if not frames:
        raise ValueError(f"No frames for {path}")
    rgb_frames = [frame.convert("RGB") for frame in frames]
    width, height = rgb_frames[0].size
    palette_source = Image.new("RGB", (width, height * len(rgb_frames)))
    for index, frame in enumerate(rgb_frames):
        if frame.size != (width, height):
            raise ValueError(f"Mismatched GIF frame size for {path}: {frame.size}")
        palette_source.paste(frame, (0, index * height))
    shared_palette = palette_source.quantize(
        colors=256,
        method=Image.Quantize.MEDIANCUT,
        dither=Image.Dither.NONE,
    )
    indexed_frames = [
        frame.quantize(palette=shared_palette, dither=Image.Dither.NONE)
        for frame in rgb_frames
    ]
    indexed_frames[0].save(
        path,
        save_all=True,
        append_images=indexed_frames[1:],
        duration=durations,
        loop=0,
        disposal=2,
        optimize=False,
    )


def build_action_preview(
    sheet: Image.Image,
    output: Path,
    spec: PreviewSpec,
) -> dict[str, object]:
    preview_dir = output / "previews"
    preview_dir.mkdir(parents=True, exist_ok=True)
    target_size = (CELL_W * PREVIEW_SCALE, CELL_H * PREVIEW_SCALE)
    columns = spec.columns or tuple(range(spec.frames))
    if len(columns) != spec.frames or len(spec.durations) != spec.frames:
        raise ValueError(f"Preview frame contract mismatch for {spec.filename}")
    source_frames = [cell(sheet, spec.row, column) for column in columns]
    frames = [
        preview_tile(frame).resize(target_size, Image.Resampling.LANCZOS)
        for frame in source_frames
    ]
    if spec.columns is not None:
        # GIF encoders merge byte-identical hold frames and can create a delay
        # above Tessalume's 2000 ms per-frame ceiling. A one-pixel corner marker
        # keeps the timeline intact without changing the visible pet artwork.
        for index, frame in enumerate(frames):
            marker = BACKGROUND if index % 2 == 0 else (0, 0, 0, 255)
            frame.putpixel((frame.width - 1, frame.height - 1), marker)
    path = preview_dir / spec.filename
    save_gif(frames, path, list(spec.durations))
    total_ms = sum(spec.durations)
    return {
        "file": f"previews/{spec.filename}",
        "frames": spec.frames,
        "durationsMs": list(spec.durations),
        "loopMs": total_ms,
        "averageFps": round(spec.frames * 1000 / total_ms, 2),
        "width": target_size[0],
        "height": target_size[1],
    }


def build_action_previews(sheet: Image.Image, output: Path) -> list[dict[str, object]]:
    return [build_action_preview(sheet, output, spec) for spec in PREVIEWS]


def update_timing_report_entry(output: Path, entry: dict[str, object]) -> None:
    path = output / "timing-report.json"
    if not path.is_file():
        return
    report = json.loads(path.read_text(encoding="utf-8"))
    previews = report.get("previews", [])
    report["previews"] = [
        entry if item.get("file") == entry["file"] else item for item in previews
    ]
    path.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


def build_direction_preview(sheet: Image.Image, output: Path) -> dict[str, object]:
    direction_frames = [cell(sheet, 9 + index // 8, index % 8) for index in range(16)]
    rendered: list[Image.Image] = []
    for index, sprite in enumerate(direction_frames):
        tile = preview_tile(sprite, extra_height=20)
        draw = ImageDraw.Draw(tile)
        degree = index * 22.5
        draw.text((6, CELL_H + 3), f"{degree:05.1f} deg", fill=LABEL)
        rendered.append(
            tile.resize(
                (CELL_W * PREVIEW_SCALE, (CELL_H + 20) * PREVIEW_SCALE),
                Image.Resampling.LANCZOS,
            )
        )
    path = output / "previews" / "10-gaze-clockwise.gif"
    save_gif(rendered, path, 100)
    return {
        "file": "previews/10-gaze-clockwise.gif",
        "frames": 16,
        "durationsMs": [100] * 16,
        "loopMs": 1600,
        "averageFps": 10.0,
        "width": CELL_W * PREVIEW_SCALE,
        "height": (CELL_H + 20) * PREVIEW_SCALE,
    }


def build_showcase(sheet: Image.Image, output: Path) -> dict[str, object]:
    animation_counts = [6, 8, 8, 4, 5, 8, 6, 6, 6]
    frames: list[Image.Image] = []
    for phase in range(8):
        grid = Image.new("RGBA", (CELL_W * 3, CELL_H * 3), BACKGROUND)
        for row, count in enumerate(animation_counts):
            sprite = cell(sheet, row, phase % count)
            x = (row % 3) * CELL_W
            y = (row // 3) * CELL_H
            grid.alpha_composite(sprite, (x, y))
        frames.append(
            grid.resize(
                (CELL_W * 3 * SHOWCASE_SCALE, CELL_H * 3 * SHOWCASE_SCALE),
                Image.Resampling.LANCZOS,
            )
        )
    path = output / "showcase.gif"
    save_gif(frames, path, 110)
    return {
        "file": "showcase.gif",
        "frames": 8,
        "durationsMs": [110] * 8,
        "loopMs": 880,
        "averageFps": 9.09,
        "width": CELL_W * 3 * SHOWCASE_SCALE,
        "height": CELL_H * 3 * SHOWCASE_SCALE,
    }


def replace_output(staging: Path, output: Path) -> None:
    backup = output.with_name(f"{output.name}.previous")
    if backup.exists():
        shutil.rmtree(backup)
    if output.exists():
        output.rename(backup)
    staging.rename(output)
    if backup.exists():
        shutil.rmtree(backup)


def main() -> int:
    args = parse_args()
    if args.jump_only and args.idle_only:
        raise ValueError("choose only one partial action update")
    partial_row = 4 if args.jump_only else 0 if args.idle_only else None
    sheet_path = args.sheet.resolve()
    runtime_sheet_path = args.runtime_sheet.resolve()
    output = args.output_dir.resolve()
    if not sheet_path.is_file():
        raise FileNotFoundError(sheet_path)
    if not runtime_sheet_path.is_file():
        raise FileNotFoundError(runtime_sheet_path)
    sheet = Image.open(sheet_path).convert("RGBA")
    if sheet.size != (ATLAS_W, ATLAS_H):
        raise ValueError(f"Expected {ATLAS_W}x{ATLAS_H}, got {sheet.size[0]}x{sheet.size[1]}")
    with Image.open(runtime_sheet_path) as runtime_sheet:
        if runtime_sheet.size != (ATLAS_W, ATLAS_H):
            raise ValueError(
                f"Expected runtime {ATLAS_W}x{ATLAS_H}, "
                f"got {runtime_sheet.size[0]}x{runtime_sheet.size[1]}"
            )

    staging = output.with_name(f"{output.name}.staging")
    if staging.exists():
        shutil.rmtree(staging)
    if partial_row is not None:
        if not output.is_dir():
            raise FileNotFoundError(f"partial update requires an existing candidate: {output}")
        shutil.copytree(output, staging, copy_function=shutil.copy2)
    else:
        staging.mkdir(parents=True)
    try:
        if partial_row is not None:
            shutil.copy2(sheet_path, staging / "spritesheet.webp")
            shutil.copy2(runtime_sheet_path, staging / "spritesheet.png")
            action_spec = next(spec for spec in PREVIEWS if spec.row == partial_row)
            timing_entry = build_action_preview(sheet, staging, action_spec)
            build_showcase(sheet, staging)
            update_timing_report_entry(staging, timing_entry)
        else:
            sheet.save(
                staging / "spritesheet.webp",
                format="WEBP",
                lossless=True,
                method=6,
                exact=True,
            )
            shutil.copy2(runtime_sheet_path, staging / "spritesheet.png")
            timing = build_action_previews(sheet, staging)
            timing.append(build_direction_preview(sheet, staging))
            timing.append(build_showcase(sheet, staging))
            (staging / "timing-report.json").write_text(
                json.dumps({"ok": True, "previews": timing}, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
            )

        validation = sheet_path.with_name("validation-extended.json")
        if validation.is_file():
            shutil.copy2(validation, staging / "validation-report.json")
        replace_output(staging, output)
    finally:
        if staging.exists():
            shutil.rmtree(staging)

    print(
        json.dumps(
            {
                "ok": True,
                "output": str(output),
                "jumpOnly": args.jump_only,
                "idleOnly": args.idle_only,
            },
            ensure_ascii=False,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
