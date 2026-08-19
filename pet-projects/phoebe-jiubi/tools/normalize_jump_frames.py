from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

from PIL import Image


CELL_WIDTH = 192
CELL_HEIGHT = 208
FRAME_COUNT = 5
BOTTOMS = (203, 196, 188, 196, 203)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract the Phoebe welcome-jump strip at one shared drawing scale."
    )
    parser.add_argument("strip", type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--json-out", required=True, type=Path)
    parser.add_argument("--max-pose-height", type=int, default=185)
    parser.add_argument("--max-pose-width", type=int, default=182)
    parser.add_argument("--chroma-threshold", type=float, default=96.0)
    return parser.parse_args()


def remove_chroma(image: Image.Image, threshold: float) -> Image.Image:
    rgba = image.convert("RGBA")
    pixels = rgba.load()
    for y in range(rgba.height):
        for x in range(rgba.width):
            red, green, blue, alpha = pixels[x, y]
            distance = math.sqrt(red * red + (green - 255) ** 2 + blue * blue)
            if distance <= threshold:
                pixels[x, y] = (0, 0, 0, 0)
            elif alpha:
                pixels[x, y] = (red, green, blue, 255)
    return rgba


def find_pose_bboxes(image: Image.Image) -> list[tuple[int, int, int, int]]:
    alpha = image.getchannel("A")
    width, height = image.size
    data = alpha.tobytes()
    visited = bytearray(width * height)
    components: list[tuple[int, int, int, int, int]] = []

    for start, alpha_value in enumerate(data):
        if alpha_value <= 16 or visited[start]:
            continue
        visited[start] = 1
        stack = [start]
        pixel_count = 0
        min_x = width
        min_y = height
        max_x = 0
        max_y = 0
        while stack:
            index = stack.pop()
            y, x = divmod(index, width)
            pixel_count += 1
            min_x = min(min_x, x)
            min_y = min(min_y, y)
            max_x = max(max_x, x)
            max_y = max(max_y, y)
            for neighbor in (index - 1, index + 1, index - width, index + width):
                if neighbor < 0 or neighbor >= len(data) or visited[neighbor]:
                    continue
                neighbor_y, neighbor_x = divmod(neighbor, width)
                if abs(neighbor_x - x) + abs(neighbor_y - y) != 1:
                    continue
                if data[neighbor] <= 16:
                    continue
                visited[neighbor] = 1
                stack.append(neighbor)
        if pixel_count >= 200:
            components.append((pixel_count, min_x, min_y, max_x + 1, max_y + 1))

    selected = sorted(components, reverse=True)[:FRAME_COUNT]
    if len(selected) != FRAME_COUNT:
        raise ValueError(f"expected {FRAME_COUNT} connected poses, found {len(selected)}")
    return [(left, top, right, bottom) for _, left, top, right, bottom in sorted(selected, key=lambda item: item[1])]


def main() -> int:
    args = parse_args()
    strip_path = args.strip.resolve()
    output_dir = args.output_dir.resolve()
    report_path = args.json_out.resolve()
    if not strip_path.is_file():
        raise FileNotFoundError(strip_path)

    with Image.open(strip_path) as opened:
        strip = remove_chroma(opened, args.chroma_threshold)

    crops: list[Image.Image] = []
    source_bboxes = find_pose_bboxes(strip)
    for bbox in source_bboxes:
        crops.append(strip.crop(bbox))

    max_source_width = max(crop.width for crop in crops)
    max_source_height = max(crop.height for crop in crops)
    shared_scale = min(
        args.max_pose_width / max_source_width,
        args.max_pose_height / max_source_height,
    )

    output_dir.mkdir(parents=True, exist_ok=True)
    frames: list[dict[str, object]] = []
    for index, (crop, bottom) in enumerate(zip(crops, BOTTOMS)):
        width = max(1, round(crop.width * shared_scale))
        height = max(1, round(crop.height * shared_scale))
        resized = crop.resize((width, height), Image.Resampling.LANCZOS)
        frame = Image.new("RGBA", (CELL_WIDTH, CELL_HEIGHT), (0, 0, 0, 0))
        x = (CELL_WIDTH - width) // 2
        y = bottom - height
        if x < 5 or y < 5 or x + width > CELL_WIDTH - 5 or bottom > CELL_HEIGHT - 5:
            raise ValueError(
                f"jump frame {index} would clip or violate padding: "
                f"x={x}, y={y}, width={width}, height={height}, bottom={bottom}"
            )
        frame.alpha_composite(resized, (x, y))
        output_path = output_dir / f"{index:02d}.png"
        frame.save(output_path)
        frames.append(
            {
                "index": index,
                "sourceBbox": list(source_bboxes[index]),
                "outputBbox": [x, y, x + width, bottom],
                "bottom": bottom,
            }
        )

    report = {
        "ok": True,
        "strip": str(strip_path),
        "outputDir": str(output_dir),
        "sharedScale": round(shared_scale, 6),
        "maxPoseWidth": args.max_pose_width,
        "maxPoseHeight": args.max_pose_height,
        "bottoms": list(BOTTOMS),
        "frames": frames,
        "invariant": "one shared scale for all five frames; vertical motion comes from placement and pose",
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    manifest_path = output_dir.parent / "frames-manifest.json"
    manifest = {
        "ok": True,
        "chroma_key": {
            "hex": "#00FF00",
            "rgb": [0, 255, 0],
            "threshold": args.chroma_threshold,
        },
        "rows": [
            {
                "state": "jumping",
                "frames": [str(output_dir / f"{index:02d}.png") for index in range(FRAME_COUNT)],
                "method": "components",
                "registration": "shared-scale welcome jump",
            }
        ],
    }
    manifest_path.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
