#!/usr/bin/env python3
"""Split a transparent ImageGen pose sheet into padded RGBA key-pose cutouts."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

from PIL import Image


def clear_transparent_rgb(image: Image.Image) -> Image.Image:
    rgba = image.convert("RGBA")
    pixels = bytearray(rgba.tobytes())
    for index in range(0, len(pixels), 4):
        if pixels[index + 3] == 0:
            pixels[index] = 0
            pixels[index + 1] = 0
            pixels[index + 2] = 0
    return Image.frombytes("RGBA", rgba.size, bytes(pixels))


def connected_groups(
    sheet: Image.Image,
    cols: int,
    rows: int,
    minimum_component_pixels: int = 4,
) -> list[Image.Image]:
    """Assign disconnected alpha components to their nearest requested grid cell."""

    width, height = sheet.size
    alpha = sheet.getchannel("A").tobytes()
    seen = bytearray(width * height)
    members_by_group: list[list[int]] = [[] for _ in range(cols * rows)]
    centers = [
        ((col + 0.5) * width / cols, (row + 0.5) * height / rows)
        for row in range(rows)
        for col in range(cols)
    ]

    for start in range(width * height):
        if not alpha[start] or seen[start]:
            continue
        stack = [start]
        seen[start] = 1
        members: list[int] = []
        x_total = 0
        y_total = 0
        while stack:
            index = stack.pop()
            members.append(index)
            x = index % width
            y = index // width
            x_total += x
            y_total += y
            if x and alpha[index - 1] and not seen[index - 1]:
                seen[index - 1] = 1
                stack.append(index - 1)
            if x + 1 < width and alpha[index + 1] and not seen[index + 1]:
                seen[index + 1] = 1
                stack.append(index + 1)
            if y and alpha[index - width] and not seen[index - width]:
                seen[index - width] = 1
                stack.append(index - width)
            if y + 1 < height and alpha[index + width] and not seen[index + width]:
                seen[index + width] = 1
                stack.append(index + width)
        if len(members) < minimum_component_pixels:
            continue
        centroid = (x_total / len(members), y_total / len(members))
        group = min(
            range(len(centers)),
            key=lambda value: math.dist(centroid, centers[value]),
        )
        members_by_group[group].extend(members)

    groups: list[Image.Image] = []
    for group_index, members in enumerate(members_by_group):
        if not members:
            raise RuntimeError(f"pose group {group_index} contains no foreground pixels")
        mask_bytes = bytearray(width * height)
        for index in members:
            mask_bytes[index] = 255
        mask = Image.frombytes("L", (width, height), bytes(mask_bytes))
        isolated = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
        isolated.paste(sheet, (0, 0), mask)
        groups.append(clear_transparent_rgb(isolated))
    return groups


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--cols", type=int, required=True)
    parser.add_argument("--rows", type=int, required=True)
    parser.add_argument("--names", required=True, help="comma-separated output stems")
    parser.add_argument("--padding", type=int, default=8)
    parser.add_argument(
        "--components",
        action="store_true",
        help="split by connected alpha components assigned to nearest grid centers",
    )
    args = parser.parse_args()

    names = [name.strip() for name in args.names.split(",") if name.strip()]
    expected = args.cols * args.rows
    if len(names) != expected:
        parser.error(f"--names must contain exactly {expected} values")

    sheet = clear_transparent_rgb(Image.open(args.input))
    # Connected-component assignment uses proportional grid centres and does
    # not require exact divisibility. ImageGen commonly returns odd dimensions
    # even for visually regular grids, so only strict cell cropping needs it.
    if not args.components and (sheet.width % args.cols or sheet.height % args.rows):
        parser.error("input dimensions must divide evenly by --cols and --rows")
    cell_w = sheet.width // args.cols
    cell_h = sheet.height // args.rows
    args.out.mkdir(parents=True, exist_ok=True)

    groups = (
        connected_groups(sheet, args.cols, args.rows)
        if args.components
        else [
            sheet.crop((
                col * cell_w,
                row * cell_h,
                (col + 1) * cell_w,
                (row + 1) * cell_h,
            ))
            for row in range(args.rows)
            for col in range(args.cols)
        ]
    )

    report: list[dict[str, object]] = []
    for index, name in enumerate(names):
        region = groups[index]
        alpha = region.getchannel("A")
        bbox = alpha.getbbox()
        if bbox is None:
            raise RuntimeError(f"pose {name!r} is empty")
        left, top, right, bottom = bbox
        region_w, region_h = region.size
        touches = {
            "left": left == 0,
            "top": top == 0,
            "right": right == region_w,
            "bottom": bottom == region_h,
        }
        if any(touches.values()):
            raise RuntimeError(f"pose {name!r} touches its sheet cell boundary: {touches}")
        box = (
            max(0, left - args.padding),
            max(0, top - args.padding),
            min(region_w, right + args.padding),
            min(region_h, bottom + args.padding),
        )
        pose = clear_transparent_rgb(region.crop(box))
        path = args.out / f"{index:02d}-{name}.png"
        pose.save(path)
        report.append(
            {
                "index": index,
                "name": name,
                "path": path.name,
                "region_bbox": list(bbox),
                "crop_box": list(box),
                "size": list(pose.size),
                "boundary_touches": touches,
            }
        )
    (args.out / "split-report.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Wrote {len(report)} key poses to {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
