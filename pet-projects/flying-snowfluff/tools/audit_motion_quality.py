#!/usr/bin/env python3
"""Audit motion continuity for the original, baseline, and candidate pet atlases."""

from __future__ import annotations

import argparse
import csv
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
CELL_W = 192
CELL_H = 208
STATE_NAMES = (
    "idle",
    "move-right",
    "move-left",
    "wave-touch",
    "jump",
    "blocked",
    "needs-input",
    "running",
    "ready",
    "gaze-360",
)
FRAME_COUNTS = (6, 8, 8, 4, 5, 8, 6, 6, 6, 16)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--original",
        type=Path,
        default=ROOT / "references" / "canonical" / "canonical-spritesheet.webp",
    )
    parser.add_argument(
        "--baseline",
        type=Path,
        default=ROOT / "spritesheet.webp",
    )
    parser.add_argument(
        "--candidate",
        type=Path,
        default=ROOT / "build" / "final-motion-candidate" / "spritesheet.webp",
    )
    parser.add_argument("--original-label", default="original-pixel")
    parser.add_argument("--baseline-label", default="installed-current")
    parser.add_argument("--candidate-label", default="candidate")
    parser.add_argument("--out", type=Path, default=ROOT / "build" / "motion-audit")
    return parser.parse_args()


def font(size: int) -> ImageFont.ImageFont:
    for path in (
        Path("C:/Windows/Fonts/msyh.ttc"),
        Path("C:/Windows/Fonts/segoeui.ttf"),
        Path("C:/Windows/Fonts/arial.ttf"),
    ):
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def frame_at(sheet: Image.Image, row: int, col: int) -> Image.Image:
    left = col * CELL_W
    top = row * CELL_H
    return sheet.crop((left, top, left + CELL_W, top + CELL_H)).convert("RGBA")


def frames_for_state(sheet: Image.Image, row: int) -> list[Image.Image]:
    count = FRAME_COUNTS[row]
    if row < 9:
        return [frame_at(sheet, row, col) for col in range(count)]
    return [frame_at(sheet, 9 + index // 8, index % 8) for index in range(count)]


def rgba_array(frame: Image.Image) -> np.ndarray:
    return np.asarray(frame.convert("RGBA"), dtype=np.float32)


def alpha_mask(array: np.ndarray) -> np.ndarray:
    return array[:, :, 3] > 20


def weighted_centroid(mask: np.ndarray, weights: np.ndarray) -> tuple[float, float]:
    ys, xs = np.nonzero(mask)
    if not len(xs):
        raise ValueError("cannot measure an empty mask")
    values = weights[ys, xs].astype(np.float64)
    total = float(values.sum())
    return float((xs * values).sum() / total), float((ys * values).sum() / total)


def alpha_centroid(array: np.ndarray) -> tuple[float, float]:
    alpha = array[:, :, 3]
    return weighted_centroid(alpha > 20, alpha)


def head_mask(array: np.ndarray) -> np.ndarray:
    """Find the stable pink/skin head mass without using a learned detector."""

    red = array[:, :, 0]
    green = array[:, :, 1]
    blue = array[:, :, 2]
    alpha = array[:, :, 3]
    yy, xx = np.indices(alpha.shape)
    warm = (
        (alpha > 64)
        & (red > 145)
        & (blue > 80)
        & (red > green * 1.025)
        & (red > blue * 0.94)
        & (xx >= 25)
        & (xx <= 167)
        & (yy >= 18)
        & (yy <= 150)
    )
    return warm


def bbox(mask: np.ndarray) -> tuple[int, int, int, int]:
    ys, xs = np.nonzero(mask)
    if not len(xs):
        raise ValueError("cannot measure an empty mask")
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def coefficient_of_variation(values: list[float]) -> float:
    average = float(np.mean(values))
    return float(np.std(values) / average) if average else 0.0


def circular_distances(points: list[tuple[float, float]]) -> list[float]:
    return [
        math.dist(points[index], points[(index + 1) % len(points)])
        for index in range(len(points))
    ]


def circular_accelerations(points: list[tuple[float, float]]) -> list[float]:
    vectors = [
        (
            points[(index + 1) % len(points)][0] - points[index][0],
            points[(index + 1) % len(points)][1] - points[index][1],
        )
        for index in range(len(points))
    ]
    return [
        math.dist(vectors[index], vectors[(index + 1) % len(vectors)])
        for index in range(len(vectors))
    ]


def silhouette_ious(masks: list[np.ndarray]) -> list[float]:
    values: list[float] = []
    for index, current in enumerate(masks):
        following = masks[(index + 1) % len(masks)]
        union = np.logical_or(current, following).sum()
        intersection = np.logical_and(current, following).sum()
        values.append(float(intersection / union) if union else 1.0)
    return values


def summarize_frames(frames: list[Image.Image]) -> dict[str, object]:
    arrays = [rgba_array(frame) for frame in frames]
    masks = [alpha_mask(array) for array in arrays]
    alpha_points = [alpha_centroid(array) for array in arrays]
    head_masks = [head_mask(array) for array in arrays]
    head_points = [
        weighted_centroid(mask, array[:, :, 3])
        for mask, array in zip(head_masks, arrays)
    ]
    boxes = [bbox(mask) for mask in masks]
    widths = [float(right - left) for left, _, right, _ in boxes]
    heights = [float(bottom - top) for _, top, _, bottom in boxes]
    ious = silhouette_ious(masks)
    alpha_steps = circular_distances(alpha_points)
    alpha_acceleration = circular_accelerations(alpha_points)
    head_steps = circular_distances(head_points)
    head_acceleration = circular_accelerations(head_points)
    return {
        "frame_count": len(frames),
        "silhouette_iou": [round(value, 5) for value in ious],
        "silhouette_iou_average": round(float(np.mean(ious)), 5),
        "silhouette_iou_minimum": round(float(np.min(ious)), 5),
        "motion_energy": round(1.0 - float(np.mean(ious)), 5),
        "alpha_centroids": [[round(x, 3), round(y, 3)] for x, y in alpha_points],
        "alpha_step": [round(value, 3) for value in alpha_steps],
        "alpha_step_average": round(float(np.mean(alpha_steps)), 3),
        "alpha_step_maximum": round(float(np.max(alpha_steps)), 3),
        "alpha_acceleration_average": round(float(np.mean(alpha_acceleration)), 3),
        "alpha_acceleration_maximum": round(float(np.max(alpha_acceleration)), 3),
        "loop_alpha_step": round(alpha_steps[-1], 3),
        "loop_silhouette_iou": round(ious[-1], 5),
        "head_centroids": [[round(x, 3), round(y, 3)] for x, y in head_points],
        "head_step": [round(value, 3) for value in head_steps],
        "head_step_average": round(float(np.mean(head_steps)), 3),
        "head_step_maximum": round(float(np.max(head_steps)), 3),
        "head_acceleration_average": round(float(np.mean(head_acceleration)), 3),
        "head_acceleration_maximum": round(float(np.max(head_acceleration)), 3),
        "loop_head_step": round(head_steps[-1], 3),
        "bbox_width_cv": round(coefficient_of_variation(widths), 5),
        "bbox_height_cv": round(coefficient_of_variation(heights), 5),
        "alpha_bboxes": [list(box) for box in boxes],
    }


def checker(size: tuple[int, int], block: int) -> Image.Image:
    image = Image.new("RGBA", size, (238, 241, 247, 255))
    draw = ImageDraw.Draw(image)
    for y in range(0, size[1], block):
        for x in range(0, size[0], block):
            if (x // block + y // block) % 2:
                draw.rectangle(
                    (x, y, x + block - 1, y + block - 1),
                    fill=(215, 221, 233, 255),
                )
    return image


def annotated_frame(
    frame: Image.Image,
    scale: int,
    alpha_point: tuple[float, float],
    head_point: tuple[float, float],
) -> Image.Image:
    tile = checker((CELL_W * scale, CELL_H * scale), 12 * scale)
    tile.alpha_composite(
        frame.resize(tile.size, Image.Resampling.NEAREST if scale > 1 else Image.Resampling.LANCZOS)
    )
    draw = ImageDraw.Draw(tile)
    ax, ay = round(alpha_point[0] * scale), round(alpha_point[1] * scale)
    hx, hy = round(head_point[0] * scale), round(head_point[1] * scale)
    draw.line((ax - 5, ay, ax + 5, ay), fill=(255, 70, 105, 255), width=2)
    draw.line((ax, ay - 5, ax, ay + 5), fill=(255, 70, 105, 255), width=2)
    draw.ellipse((hx - 4, hy - 4, hx + 4, hy + 4), outline=(28, 199, 226, 255), width=2)
    return tile


def render_state_board(
    state: str,
    row: int,
    sheets: dict[str, Image.Image],
    report: dict[str, dict[str, dict[str, object]]],
    out: Path,
) -> None:
    scale = 2
    count = FRAME_COUNTS[row]
    header = 48
    label_width = 150
    width = label_width + count * CELL_W * scale
    height = len(sheets) * (CELL_H * scale + header)
    board = Image.new("RGBA", (width, height), (9, 13, 27, 255))
    label_font = font(18)
    small_font = font(13)
    draw = ImageDraw.Draw(board)
    for version_index, (version, sheet) in enumerate(sheets.items()):
        top = version_index * (CELL_H * scale + header)
        metrics = report[version][state]
        draw.text((10, top + 5), version, fill=(215, 252, 255, 255), font=label_font)
        draw.text(
            (10, top + 27),
            f"IoU {metrics['silhouette_iou_average']:.3f}  "
            f"head {metrics['head_step_average']:.1f}px  "
            f"loop {metrics['loop_alpha_step']:.1f}px",
            fill=(184, 194, 218, 255),
            font=small_font,
        )
        alpha_points = [tuple(value) for value in metrics["alpha_centroids"]]
        head_points = [tuple(value) for value in metrics["head_centroids"]]
        for col in range(count):
            frame = frames_for_state(sheet, row)[col]
            tile = annotated_frame(frame, scale, alpha_points[col], head_points[col])
            x = label_width + col * CELL_W * scale
            board.alpha_composite(tile, (x, top + header))
            draw.text((x + 6, top + header + 4), str(col), fill=(35, 46, 70, 255), font=small_font)
    board.convert("RGB").save(out / f"{row:02d}-{state}-triptych.jpg", quality=94)


def render_trace_board(
    report: dict[str, dict[str, dict[str, object]]],
    out: Path,
) -> None:
    versions = tuple(report)
    panel_w = 300
    panel_h = 205
    left_label = 150
    board = Image.new(
        "RGBA",
        (left_label + panel_w * len(versions), panel_h * len(STATE_NAMES) + 44),
        (9, 13, 27, 255),
    )
    draw = ImageDraw.Draw(board)
    label_font = font(17)
    small_font = font(12)
    colors = ((255, 99, 135, 255), (92, 231, 244, 255))
    for version_index, version in enumerate(versions):
        draw.text(
            (left_label + version_index * panel_w + 10, 10),
            version,
            fill=(215, 252, 255, 255),
            font=label_font,
        )
    for row, state in enumerate(STATE_NAMES):
        top = 44 + row * panel_h
        draw.text((10, top + 8), state, fill=(215, 252, 255, 255), font=label_font)
        for version_index, version in enumerate(versions):
            left = left_label + version_index * panel_w
            metrics = report[version][state]
            draw.rectangle(
                (left + 5, top + 5, left + panel_w - 5, top + panel_h - 5),
                outline=(42, 54, 82, 255),
            )
            for point_key, color in zip(("alpha_centroids", "head_centroids"), colors):
                points = [
                    (left + 10 + value[0] * 1.45, top + 10 + value[1] * 0.88)
                    for value in metrics[point_key]
                ]
                if len(points) > 1:
                    draw.line(points + [points[0]], fill=color, width=2, joint="curve")
                for index, point in enumerate(points):
                    radius = 4 if index == 0 else 3
                    draw.ellipse(
                        (point[0] - radius, point[1] - radius, point[0] + radius, point[1] + radius),
                        fill=color,
                    )
                    draw.text((point[0] + 4, point[1] - 8), str(index), fill=color, font=small_font)
            draw.text(
                (left + 10, top + panel_h - 25),
                f"alpha accel {metrics['alpha_acceleration_average']:.1f}  "
                f"head accel {metrics['head_acceleration_average']:.1f}",
                fill=(184, 194, 218, 255),
                font=small_font,
            )
    board.convert("RGB").save(out / "motion-traces.jpg", quality=94)


def write_csv(report: dict[str, dict[str, dict[str, object]]], out: Path) -> None:
    fields = (
        "version",
        "state",
        "motion_energy",
        "silhouette_iou_average",
        "silhouette_iou_minimum",
        "alpha_step_average",
        "alpha_step_maximum",
        "alpha_acceleration_average",
        "alpha_acceleration_maximum",
        "head_step_average",
        "head_step_maximum",
        "head_acceleration_average",
        "head_acceleration_maximum",
        "loop_alpha_step",
        "loop_head_step",
        "loop_silhouette_iou",
        "bbox_width_cv",
        "bbox_height_cv",
    )
    with (out / "summary.csv").open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for version, states in report.items():
            for state, metrics in states.items():
                writer.writerow(
                    {
                        "version": version,
                        "state": state,
                        **{field: metrics[field] for field in fields[2:]},
                    }
                )


def main() -> None:
    args = parse_args()
    out = args.out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    paths = {
        args.original_label: args.original.resolve(),
        args.baseline_label: args.baseline.resolve(),
        args.candidate_label: args.candidate.resolve(),
    }
    sheets = {name: Image.open(path).convert("RGBA") for name, path in paths.items()}
    for name, sheet in sheets.items():
        if sheet.size != (CELL_W * 8, CELL_H * 11):
            raise ValueError(f"{name} has unexpected atlas size {sheet.size}")

    report: dict[str, dict[str, dict[str, object]]] = {}
    for version, sheet in sheets.items():
        report[version] = {}
        for row, state in enumerate(STATE_NAMES):
            frames = frames_for_state(sheet, row)
            report[version][state] = summarize_frames(frames)

    payload = {
        "sources": {name: str(path) for name, path in paths.items()},
        "states": list(STATE_NAMES),
        "report": report,
    }
    (out / "audit.json").write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    write_csv(report, out)
    for row, state in enumerate(STATE_NAMES):
        render_state_board(state, row, sheets, report, out)
    render_trace_board(report, out)
    print(f"Wrote motion audit to {out}")


if __name__ == "__main__":
    main()
