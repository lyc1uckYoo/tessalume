from __future__ import annotations

import argparse
import hashlib
import json
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


CELL_SIZE = (192, 208)
FRAME_SEQUENCE = ("open", "micro", "half", "closed", "half", "open")
EYE_ROIS = ((86, 175, 185, 265), (205, 175, 310, 265))


def parse_args() -> argparse.Namespace:
    project = Path(__file__).resolve().parents[1]
    run = project / "build" / "hatch-run"
    parser = argparse.ArgumentParser(
        description="Build an exact-source seated idle loop with imagegen eye edits only."
    )
    parser.add_argument(
        "--source",
        type=Path,
        default=project / "references" / "user" / "idle-exact-source.png",
    )
    parser.add_argument(
        "--micro-edit",
        type=Path,
        default=project
        / "assets"
        / "keyframes"
        / "idle-exact-edits"
        / "micro-closed-imagegen.png",
    )
    parser.add_argument(
        "--half-edit",
        type=Path,
        default=project
        / "assets"
        / "keyframes"
        / "idle-exact-edits"
        / "half-closed-imagegen-v2.png",
    )
    parser.add_argument(
        "--closed-edit",
        type=Path,
        default=project
        / "assets"
        / "keyframes"
        / "idle-exact-edits"
        / "closed-imagegen.png",
    )
    parser.add_argument("--frames-dir", type=Path, default=run / "frames" / "idle")
    parser.add_argument(
        "--strip-output",
        type=Path,
        default=project / "assets" / "keyframes" / "00-idle-strip-chroma.png",
    )
    parser.add_argument("--decoded-output", type=Path, default=run / "decoded" / "idle.png")
    parser.add_argument("--qa-dir", type=Path, default=run / "qa")
    return parser.parse_args()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def border_background(rgb: np.ndarray) -> np.ndarray:
    height, width, _ = rgb.shape
    minimum = rgb.min(axis=2)
    maximum = rgb.max(axis=2)
    candidate = (minimum >= 240) & ((maximum - minimum) <= 18)
    visited = np.zeros((height, width), dtype=bool)
    queue: deque[tuple[int, int]] = deque()

    def add(x: int, y: int) -> None:
        if candidate[y, x] and not visited[y, x]:
            visited[y, x] = True
            queue.append((x, y))

    for x in range(width):
        add(x, 0)
        add(x, height - 1)
    for y in range(height):
        add(0, y)
        add(width - 1, y)
    while queue:
        x, y = queue.popleft()
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < width and 0 <= ny < height:
                add(nx, ny)
    return visited


def largest_component(mask: np.ndarray) -> np.ndarray:
    height, width = mask.shape
    seen = np.zeros_like(mask)
    best: list[tuple[int, int]] = []
    for y in range(height):
        for x in range(width):
            if not mask[y, x] or seen[y, x]:
                continue
            seen[y, x] = True
            queue = deque([(x, y)])
            component: list[tuple[int, int]] = []
            while queue:
                cx, cy = queue.popleft()
                component.append((cx, cy))
                for nx in range(max(0, cx - 1), min(width, cx + 2)):
                    for ny in range(max(0, cy - 1), min(height, cy + 2)):
                        if mask[ny, nx] and not seen[ny, nx]:
                            seen[ny, nx] = True
                            queue.append((nx, ny))
            if len(component) > len(best):
                best = component
    result = np.zeros_like(mask)
    for x, y in best:
        result[y, x] = True
    return result


def bounds(mask: np.ndarray) -> tuple[int, int, int, int]:
    ys, xs = np.nonzero(mask)
    if not len(xs):
        raise ValueError("foreground mask is empty")
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def normalized_edit(path: Path, size: tuple[int, int]) -> Image.Image:
    with Image.open(path) as opened:
        return opened.convert("RGB").resize(size, Image.Resampling.LANCZOS)


def eye_mask(source: Image.Image) -> Image.Image:
    rgb = np.asarray(source.convert("RGB"), dtype=np.uint8)
    red = rgb[:, :, 0].astype(np.int16)
    green = rgb[:, :, 1].astype(np.int16)
    blue = rgb[:, :, 2].astype(np.int16)
    gray = (red * 30 + green * 59 + blue * 11) // 100
    candidate = (gray < 150) | ((blue > red + 7) & (blue > green + 10) & (blue > 85))
    core_array = np.zeros((source.height, source.width), dtype=bool)
    for left, top, right, bottom in EYE_ROIS:
        local = candidate[top:bottom, left:right]
        seen = np.zeros_like(local)
        local_height, local_width = local.shape
        best: list[tuple[int, int]] = []
        for local_y in range(local_height):
            for local_x in range(local_width):
                if not local[local_y, local_x] or seen[local_y, local_x]:
                    continue
                seen[local_y, local_x] = True
                queue = deque([(local_x, local_y)])
                component: list[tuple[int, int]] = []
                while queue:
                    current_x, current_y = queue.popleft()
                    component.append((current_x, current_y))
                    for next_x in range(max(0, current_x - 1), min(local_width, current_x + 2)):
                        for next_y in range(max(0, current_y - 1), min(local_height, current_y + 2)):
                            if local[next_y, next_x] and not seen[next_y, next_x]:
                                seen[next_y, next_x] = True
                                queue.append((next_x, next_y))
                centroid_y = sum(point[1] for point in component) / len(component)
                if centroid_y >= 29 and len(component) > len(best):
                    best = component
        if not best:
            raise ValueError(f"could not isolate source eye in ROI {(left, top, right, bottom)}")
        component_mask = np.zeros_like(local)
        for local_x, local_y in best:
            component_mask[local_y, local_x] = True
        background = ~component_mask
        outside = np.zeros_like(background)
        queue = deque()
        for local_x in range(local_width):
            for local_y in (0, local_height - 1):
                if background[local_y, local_x] and not outside[local_y, local_x]:
                    outside[local_y, local_x] = True
                    queue.append((local_x, local_y))
        for local_y in range(local_height):
            for local_x in (0, local_width - 1):
                if background[local_y, local_x] and not outside[local_y, local_x]:
                    outside[local_y, local_x] = True
                    queue.append((local_x, local_y))
        while queue:
            current_x, current_y = queue.popleft()
            for next_x, next_y in (
                (current_x - 1, current_y),
                (current_x + 1, current_y),
                (current_x, current_y - 1),
                (current_x, current_y + 1),
            ):
                if (
                    0 <= next_x < local_width
                    and 0 <= next_y < local_height
                    and background[next_y, next_x]
                    and not outside[next_y, next_x]
                ):
                    outside[next_y, next_x] = True
                    queue.append((next_x, next_y))
        component_mask |= background & ~outside
        core_array[top:bottom, left:right] |= component_mask
    core = Image.fromarray((core_array.astype(np.uint8) * 255), mode="L").filter(
        ImageFilter.MaxFilter(5)
    )
    outer = core.filter(ImageFilter.MaxFilter(15)).filter(ImageFilter.GaussianBlur(3.5))
    return Image.fromarray(
        np.maximum(np.asarray(core, dtype=np.uint8), np.asarray(outer, dtype=np.uint8)),
        mode="L",
    )


def brow_restore_mask(source: Image.Image) -> Image.Image:
    rgb = np.asarray(source.convert("RGB"), dtype=np.uint8)
    gray = (
        rgb[:, :, 0].astype(np.uint16) * 30
        + rgb[:, :, 1].astype(np.uint16) * 59
        + rgb[:, :, 2].astype(np.uint16) * 11
    ) // 100
    dark = gray < 105
    height, width = dark.shape
    region = np.zeros_like(dark)
    region[150:207, 65:min(width, 325)] = True
    active = dark & region
    seen = np.zeros_like(active)
    selected = np.zeros_like(active)
    for y in range(150, min(207, height)):
        for x in range(65, min(325, width)):
            if not active[y, x] or seen[y, x]:
                continue
            seen[y, x] = True
            queue = deque([(x, y)])
            component: list[tuple[int, int]] = []
            while queue:
                cx, cy = queue.popleft()
                component.append((cx, cy))
                for nx in range(max(0, cx - 1), min(width, cx + 2)):
                    for ny in range(max(0, cy - 1), min(height, cy + 2)):
                        if active[ny, nx] and not seen[ny, nx]:
                            seen[ny, nx] = True
                            queue.append((nx, ny))
            xs = [point[0] for point in component]
            ys = [point[1] for point in component]
            component_width = max(xs) - min(xs) + 1
            component_height = max(ys) - min(ys) + 1
            centroid_y = sum(ys) / len(ys)
            if (
                len(component) >= 100
                and component_width >= 35
                and component_height <= 36
                and centroid_y < 188
            ):
                for cx, cy in component:
                    selected[cy, cx] = True
    mask = Image.fromarray((selected.astype(np.uint8) * 255), mode="L")
    mask = mask.filter(ImageFilter.MaxFilter(5))
    draw = ImageDraw.Draw(mask)
    draw.rectangle((0, 263, source.width, source.height), fill=255)
    draw.rectangle((0, 0, 92, source.height), fill=255)
    draw.rectangle((305, 0, source.width, source.height), fill=255)
    draw.ellipse((168, 222, 211, 258), fill=255)
    restore = np.asarray(mask, dtype=np.uint8).copy()
    red = rgb[:, :, 0].astype(np.int16)
    green = rgb[:, :, 1].astype(np.int16)
    blue = rgb[:, :, 2].astype(np.int16)
    y_grid, _ = np.mgrid[0:source.height, 0:source.width]
    blush = (
        (y_grid >= 218)
        & (y_grid < 264)
        & (red > 220)
        & (red - green > 14)
        & (red - blue > 7)
    )
    restore[blush] = 255
    return Image.fromarray(restore, mode="L")


def composite_state(
    source: Image.Image,
    edit: Image.Image,
    mask: Image.Image,
    brows: Image.Image,
) -> Image.Image:
    source_rgb = np.asarray(source.convert("RGB"), dtype=np.float32)
    mask_values = np.asarray(mask.convert("L"), dtype=np.uint8)
    inside = mask_values >= 128
    active = mask_values > 0
    height, width, _ = source_rgb.shape
    y_grid, x_grid = np.mgrid[0:height, 0:width]
    x_normalized = (x_grid - width / 2) / width
    y_normalized = (y_grid - height / 2) / height
    features = np.stack(
        (
            np.ones_like(x_normalized),
            x_normalized,
            y_normalized,
            x_normalized * x_normalized,
            x_normalized * y_normalized,
            y_normalized * y_normalized,
        ),
        axis=2,
    )
    red_source = source_rgb[:, :, 0]
    green_source = source_rgb[:, :, 1]
    blue_source = source_rgb[:, :, 2]
    face_region = (x_grid >= 78) & (x_grid < 320) & (y_grid >= 160) & (y_grid < 274)
    skin = (
        face_region
        & ~inside
        & (red_source > 205)
        & (green_source > 160)
        & (blue_source > 165)
        & (blue_source < red_source + 18)
    )
    design = features[skin]
    if design.shape[0] < 100:
        raise ValueError("not enough source skin pixels to reconstruct the eye background")
    predicted = np.zeros_like(source_rgb)
    flattened_features = features.reshape((-1, features.shape[2]))
    for channel in range(3):
        coefficients, *_ = np.linalg.lstsq(design, source_rgb[:, :, channel][skin], rcond=None)
        predicted[:, :, channel] = (flattened_features @ coefficients).reshape((height, width))
    filled = source_rgb.copy()
    filled[active] = predicted[active]
    inpainted = Image.fromarray(np.clip(filled, 0, 255).astype(np.uint8), mode="RGB")
    base = Image.composite(inpainted, source, mask)

    edit_rgb = np.asarray(edit.convert("RGB"), dtype=np.uint8)
    red = edit_rgb[:, :, 0].astype(np.int16)
    green = edit_rgb[:, :, 1].astype(np.int16)
    blue = edit_rgb[:, :, 2].astype(np.int16)
    gray = (red * 30 + green * 59 + blue * 11) // 100
    candidate = (gray < 145) | ((blue > red + 7) & (blue > green + 10) & (blue > 85))
    art_mask = np.zeros(inside.shape, dtype=bool)
    for left, top, right, bottom in ((96, 202, 178, 260), (212, 202, 300, 260)):
        local = candidate[top:bottom, left:right]
        seen = np.zeros_like(local)
        best: list[tuple[int, int]] = []
        local_height, local_width = local.shape
        for local_y in range(local_height):
            for local_x in range(local_width):
                if not local[local_y, local_x] or seen[local_y, local_x]:
                    continue
                seen[local_y, local_x] = True
                queue = deque([(local_x, local_y)])
                component: list[tuple[int, int]] = []
                while queue:
                    current_x, current_y = queue.popleft()
                    component.append((current_x, current_y))
                    for next_x in range(max(0, current_x - 1), min(local_width, current_x + 2)):
                        for next_y in range(max(0, current_y - 1), min(local_height, current_y + 2)):
                            if local[next_y, next_x] and not seen[next_y, next_x]:
                                seen[next_y, next_x] = True
                                queue.append((next_x, next_y))
                if len(component) > len(best):
                    best = component
        component_mask = np.zeros_like(local)
        for local_x, local_y in best:
            component_mask[local_y, local_x] = True
        if best:
            component_background = ~component_mask
            outside = np.zeros_like(component_background)
            queue = deque()
            for local_x in range(local_width):
                for local_y in (0, local_height - 1):
                    if component_background[local_y, local_x] and not outside[local_y, local_x]:
                        outside[local_y, local_x] = True
                        queue.append((local_x, local_y))
            for local_y in range(local_height):
                for local_x in (0, local_width - 1):
                    if component_background[local_y, local_x] and not outside[local_y, local_x]:
                        outside[local_y, local_x] = True
                        queue.append((local_x, local_y))
            while queue:
                current_x, current_y = queue.popleft()
                for next_x, next_y in (
                    (current_x - 1, current_y),
                    (current_x + 1, current_y),
                    (current_x, current_y - 1),
                    (current_x, current_y + 1),
                ):
                    if (
                        0 <= next_x < local_width
                        and 0 <= next_y < local_height
                        and component_background[next_y, next_x]
                        and not outside[next_y, next_x]
                    ):
                        outside[next_y, next_x] = True
                        queue.append((next_x, next_y))
            component_mask |= component_background & ~outside
        art_mask[top:bottom, left:right] |= component_mask
    art = Image.fromarray((art_mask.astype(np.uint8) * 255), mode="L")
    art = art.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(0.35))
    result = Image.composite(edit, base, art)
    return Image.composite(source, result, brows)


def cutout_from_source_mask(image: Image.Image, source_mask: np.ndarray) -> Image.Image:
    rgba = np.zeros((image.height, image.width, 4), dtype=np.uint8)
    rgba[:, :, :3] = np.asarray(image.convert("RGB"), dtype=np.uint8)
    rgba[:, :, 3] = source_mask.astype(np.uint8) * 255
    rgba[~source_mask, :3] = 0
    return Image.fromarray(rgba, mode="RGBA")


def resize_premultiplied(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    rgba = np.asarray(image.convert("RGBA"), dtype=np.float32)
    alpha = rgba[:, :, 3:4] / 255.0
    premultiplied = np.concatenate((rgba[:, :, :3] * alpha, rgba[:, :, 3:4]), axis=2)
    channels = []
    for index in range(4):
        channel = Image.fromarray(np.clip(premultiplied[:, :, index], 0, 255).astype(np.uint8), "L")
        channels.append(np.asarray(channel.resize(size, Image.Resampling.LANCZOS), dtype=np.float32))
    resized = np.stack(channels, axis=2)
    out_alpha = resized[:, :, 3:4]
    out_rgb = np.zeros_like(resized[:, :, :3])
    nonzero = out_alpha[:, :, 0] > 0
    out_rgb[nonzero] = (
        resized[:, :, :3][nonzero] * 255.0 / out_alpha[nonzero]
    )
    output = np.concatenate((np.clip(out_rgb, 0, 255), np.clip(out_alpha, 0, 255)), axis=2)
    return Image.fromarray(output.astype(np.uint8), mode="RGBA")


def fit_to_cell(
    cutout: Image.Image,
    foreground_bounds: tuple[int, int, int, int],
) -> tuple[Image.Image, dict[str, int | float]]:
    cropped = cutout.crop(foreground_bounds)
    max_width = 184
    max_height = 200
    scale = min(max_width / cropped.width, max_height / cropped.height)
    width = max(1, round(cropped.width * scale))
    height = max(1, round(cropped.height * scale))
    resized = resize_premultiplied(cropped, (width, height))
    x = (CELL_SIZE[0] - width) // 2
    y = CELL_SIZE[1] - 4 - height
    cell = Image.new("RGBA", CELL_SIZE, (0, 0, 0, 0))
    cell.alpha_composite(resized, (x, y))
    return cell, {"x": x, "y": y, "width": width, "height": height, "scale": scale}


def changed_pixels(a: Image.Image, b: Image.Image, excluded: Image.Image) -> int:
    first = np.asarray(a.convert("RGBA"), dtype=np.uint8)
    second = np.asarray(b.convert("RGBA"), dtype=np.uint8)
    difference = np.any(first != second, axis=2)
    outside = np.asarray(excluded.convert("L"), dtype=np.uint8) == 0
    return int(np.count_nonzero(difference & outside))


def total_changed_pixels(a: Image.Image, b: Image.Image) -> int:
    first = np.asarray(a.convert("RGBA"), dtype=np.uint8)
    second = np.asarray(b.convert("RGBA"), dtype=np.uint8)
    return int(np.count_nonzero(np.any(first != second, axis=2)))


def transformed_eye_mask(
    mask: Image.Image,
    foreground_bounds: tuple[int, int, int, int],
    placement: dict[str, int | float],
) -> Image.Image:
    cropped = mask.crop(foreground_bounds)
    resized = cropped.resize(
        (int(placement["width"]), int(placement["height"])),
        Image.Resampling.LANCZOS,
    )
    result = Image.new("L", CELL_SIZE, 0)
    result.paste(resized, (int(placement["x"]), int(placement["y"])))
    return result.filter(ImageFilter.MaxFilter(7))


def save_strip(frames: list[Image.Image], path: Path) -> None:
    strip = Image.new("RGB", (CELL_SIZE[0] * len(frames), CELL_SIZE[1]), (0, 255, 0))
    for index, frame in enumerate(frames):
        tile = Image.new("RGBA", CELL_SIZE, (0, 255, 0, 255))
        tile.alpha_composite(frame)
        strip.paste(tile.convert("RGB"), (index * CELL_SIZE[0], 0))
    path.parent.mkdir(parents=True, exist_ok=True)
    strip.save(path)


def save_preview(frames: list[Image.Image], path: Path) -> None:
    scale = 3
    gap = 8
    tile_width = CELL_SIZE[0] * scale
    tile_height = CELL_SIZE[1] * scale
    sheet = Image.new(
        "RGBA",
        (tile_width * len(frames) + gap * (len(frames) - 1), tile_height),
        (18, 22, 39, 255),
    )
    for index, frame in enumerate(frames):
        rendered = Image.new("RGBA", CELL_SIZE, (18, 22, 39, 255))
        rendered.alpha_composite(frame)
        rendered = rendered.resize((tile_width, tile_height), Image.Resampling.NEAREST)
        sheet.alpha_composite(rendered, (index * (tile_width + gap), 0))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


def main() -> int:
    args = parse_args()
    for path in (args.source, args.micro_edit, args.half_edit, args.closed_edit):
        if not path.is_file():
            raise FileNotFoundError(path)

    with Image.open(args.source) as opened:
        source = opened.convert("RGB")
    micro_edit = normalized_edit(args.micro_edit, source.size)
    half_edit = normalized_edit(args.half_edit, source.size)
    closed_edit = normalized_edit(args.closed_edit, source.size)
    mask = eye_mask(source)
    brows = brow_restore_mask(source)

    micro = composite_state(source, micro_edit, mask, brows)
    half = composite_state(source, half_edit, mask, brows)
    closed = composite_state(source, closed_edit, mask, brows)

    edit_dir = args.strip_output.parent / "idle-exact-edits"
    edit_dir.mkdir(parents=True, exist_ok=True)
    micro.save(edit_dir / "micro-closed-composited.png")
    half.save(edit_dir / "half-closed-composited.png")
    closed.save(edit_dir / "closed-composited.png")
    mask.save(edit_dir / "eye-edit-mask.png")
    brows.save(edit_dir / "brow-restore-mask.png")

    rgb = np.asarray(source, dtype=np.uint8)
    background = border_background(rgb)
    foreground = largest_component(~background)
    foreground_bounds = bounds(foreground)

    states = {"open": source, "micro": micro, "half": half, "closed": closed}
    cells: dict[str, Image.Image] = {}
    placement: dict[str, int | float] | None = None
    for name, state in states.items():
        cutout = cutout_from_source_mask(state, foreground)
        cell, current_placement = fit_to_cell(cutout, foreground_bounds)
        cells[name] = cell
        placement = current_placement
    assert placement is not None

    transformed_mask = transformed_eye_mask(mask, foreground_bounds, placement)
    frames = [
        cells["open"].copy(),
        cells["micro"].copy(),
        cells["half"].copy(),
        cells["closed"].copy(),
        cells["half"].copy(),
        cells["open"].copy(),
    ]
    args.frames_dir.mkdir(parents=True, exist_ok=True)
    for index, frame in enumerate(frames):
        frame.save(args.frames_dir / f"{index:02d}.png")
    save_strip(frames, args.strip_output)
    save_strip(frames, args.decoded_output)

    source_outside_micro = changed_pixels(source, micro, mask)
    source_outside_half = changed_pixels(source, half, mask)
    source_outside_closed = changed_pixels(source, closed, mask)
    cell_outside_micro = changed_pixels(cells["open"], cells["micro"], transformed_mask)
    cell_outside_half = changed_pixels(cells["open"], cells["half"], transformed_mask)
    cell_outside_closed = changed_pixels(cells["open"], cells["closed"], transformed_mask)
    adjacent_changes = {
        f"{index:02d}-{(index + 1) % len(frames):02d}": total_changed_pixels(
            frames[index], frames[(index + 1) % len(frames)]
        )
        for index in range(len(frames))
    }
    adjacent_outside_eye_changes = {
        f"{index:02d}-{(index + 1) % len(frames):02d}": changed_pixels(
            frames[index], frames[(index + 1) % len(frames)], transformed_mask
        )
        for index in range(len(frames))
    }
    report = {
        "ok": all(
            value == 0
            for value in (
                source_outside_micro,
                source_outside_half,
                source_outside_closed,
                cell_outside_micro,
                cell_outside_half,
                cell_outside_closed,
                *adjacent_outside_eye_changes.values(),
            )
        )
        and all(value > 0 for value in list(adjacent_changes.values())[:-1])
        and list(adjacent_changes.values())[-1] == 0,
        "source": str(args.source.resolve()),
        "sourceSha256": sha256(args.source.resolve()),
        "halfEdit": str(args.half_edit.resolve()),
        "halfEditSha256": sha256(args.half_edit.resolve()),
        "closedEdit": str(args.closed_edit.resolve()),
        "closedEditSha256": sha256(args.closed_edit.resolve()),
        "sourceSize": list(source.size),
        "microEdit": str(args.micro_edit.resolve()),
        "microEditSha256": sha256(args.micro_edit.resolve()),
        "foregroundBounds": list(foreground_bounds),
        "cellPlacement": placement,
        "frameSequence": list(FRAME_SEQUENCE),
        "outsideEyeChangedPixels": {
            "sourceMicro": source_outside_micro,
            "sourceHalf": source_outside_half,
            "sourceClosed": source_outside_closed,
            "cellMicro": cell_outside_micro,
            "cellHalf": cell_outside_half,
            "cellClosed": cell_outside_closed,
        },
        "adjacentChangedPixels": adjacent_changes,
        "adjacentOutsideEyeChangedPixels": adjacent_outside_eye_changes,
        "allAdjacentFramesDistinct": all(value > 0 for value in adjacent_changes.values()),
        "allMotionStepsDistinct": all(
            value > 0 for value in list(adjacent_changes.values())[:-1]
        ),
        "loopBoundaryStable": list(adjacent_changes.values())[-1] == 0,
        "removedDetachedForegroundPixels": int(np.count_nonzero((~background) & ~foreground)),
    }
    args.qa_dir.mkdir(parents=True, exist_ok=True)
    (args.qa_dir / "idle-exactness-report.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    transformed_mask.save(args.qa_dir / "idle-eye-mask-cell.png")
    save_preview(frames, args.qa_dir / "idle-exact-blink-preview.png")
    print(json.dumps(report, ensure_ascii=False))
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
