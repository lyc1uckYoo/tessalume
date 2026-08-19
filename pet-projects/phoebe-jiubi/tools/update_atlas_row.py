from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


CELL_WIDTH = 192
CELL_HEIGHT = 208
ATLAS_SIZE = (1536, 2288)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Replace one animation row while preserving every other atlas pixel."
    )
    parser.add_argument("--source-atlas", required=True, type=Path)
    parser.add_argument("--frames-dir", required=True, type=Path)
    parser.add_argument("--row", required=True, type=int)
    parser.add_argument("--frame-count", required=True, type=int)
    parser.add_argument("--neutral-from", type=int)
    parser.add_argument("--output", required=True, type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    source = args.source_atlas.resolve()
    output = args.output.resolve()
    if not source.is_file():
        raise FileNotFoundError(source)
    if not 0 <= args.row < 11:
        raise ValueError(f"row out of range: {args.row}")
    if not 1 <= args.frame_count <= 8:
        raise ValueError(f"frame count out of range: {args.frame_count}")

    with Image.open(source) as opened:
        atlas = opened.convert("RGBA")
    if atlas.size != ATLAS_SIZE:
        raise ValueError(f"expected atlas {ATLAS_SIZE}, got {atlas.size}")

    row_top = args.row * CELL_HEIGHT
    atlas.paste(
        Image.new("RGBA", (ATLAS_SIZE[0], CELL_HEIGHT), (0, 0, 0, 0)),
        (0, row_top),
    )
    frames: list[Image.Image] = []
    for index in range(args.frame_count):
        path = args.frames_dir.resolve() / f"{index:02d}.png"
        if not path.is_file():
            raise FileNotFoundError(path)
        with Image.open(path) as opened:
            frame = opened.convert("RGBA")
        if frame.size != (CELL_WIDTH, CELL_HEIGHT):
            raise ValueError(f"unexpected frame size for {path}: {frame.size}")
        frames.append(frame)
        atlas.alpha_composite(frame, (index * CELL_WIDTH, row_top))

    if args.neutral_from is not None:
        if not 0 <= args.neutral_from < len(frames):
            raise ValueError(f"neutral frame out of range: {args.neutral_from}")
        if args.frame_count >= 8:
            raise ValueError("no free cell remains for neutral frame")
        atlas.alpha_composite(frames[args.neutral_from], (args.frame_count * CELL_WIDTH, row_top))

    output.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(output)
    print(
        {
            "ok": True,
            "source": str(source),
            "output": str(output),
            "row": args.row,
            "frames": args.frame_count,
            "neutralFrom": args.neutral_from,
        }
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
