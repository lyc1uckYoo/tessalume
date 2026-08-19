#!/usr/bin/env python3
"""Build the smooth, non-pixel Flying Snowfluff Codex desktop pet.

The approved V6 identity master and curated action keyframes are the only art
inputs. This builder keeps those drawings intact and adds deterministic
micro-motion, state effects, direction sampling, previews, and the desktop V2
8x11 atlas layout.
"""

from __future__ import annotations

import hashlib
import json
import math
import shutil
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont, ImageOps


ROOT = Path(__file__).resolve().parents[1]
IDENTITY = ROOT / "assets" / "identity"
KEYFRAMES = ROOT / "assets" / "keyframes"
OUT = ROOT / "build" / "final-motion-candidate"
MANIFEST = ROOT / "pet.json"
VERSION_FILE = ROOT / "VERSION"
IDENTITY_MASTER = IDENTITY / "flying-snowfluff-master.png"
NEUTRAL_FRAME = IDENTITY / "reduced-motion-neutral.png"
IDENTITY_MASTER_SHA256 = "E2882F78DCD7256CDF0C1AB4DD08C7AED4BADCCB63AC955A04F5F856884CC200"

COLS = 8
ROWS = 11
CELL_W = 192
CELL_H = 208
SHEET_SIZE = (CELL_W * COLS, CELL_H * ROWS)
TRANSPARENT = (0, 0, 0, 0)

CYAN = (104, 236, 248, 255)
PALE_CYAN = (213, 252, 255, 255)
PINK = (255, 116, 190, 255)
PALE_PINK = (255, 211, 228, 255)
LAVENDER = (206, 202, 246, 255)
NAVY = (43, 38, 72, 255)
WHITE = (252, 252, 255, 255)
GOLD = (255, 226, 122, 255)

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
    "gaze-upper",
    "gaze-lower",
)
USED_COUNTS = (7, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8)
ANIMATION_COUNTS = (6, 8, 8, 4, 5, 8, 6, 6, 6)
FRAME_DURATIONS = (
    (180, 90, 90, 100, 100, 180),
    (90, 90, 90, 90, 90, 90, 90, 110),
    (90, 90, 90, 90, 90, 90, 90, 110),
    (140, 80, 120, 160),
    (110, 90, 100, 100, 130),
    (110, 100, 100, 110, 110, 100, 110, 140),
    (110, 100, 100, 100, 110, 140),
    (100, 90, 90, 90, 100, 130),
    (120, 90, 110, 80, 100, 150),
)

KEYFRAME_FILES = {
    "idle": (
        "idle/00-neutral.png",
        "idle/01-inhale.png",
        "idle/02-glance.png",
        "idle/03-blink.png",
        "idle/04-recover.png",
        "idle/05-settle.png",
    ),
    "move_right": (
        "move-right-sweep/00-glide.png",
        "move-right-sweep/01-down-early.png",
        "move-right-sweep/02-down-mid.png",
        "move-right-sweep/03-down-full.png",
        "move-right-sweep/04-rebound.png",
        "move-right-sweep/05-up-early.png",
        "move-right-sweep/06-up-full-corrected-scaled.png",
        "move-right-sweep/07-return.png",
    ),
    "wave": (
        "wave-touch-loop/00-greet.png",
        "wave-touch-loop/01-reach.png",
        "wave-touch-loop/02-touch.png",
        "wave-touch-loop/03-settle.png",
    ),
    "jump": (
        "jump-smooth/00-anticipation.png",
        "jump-smooth/01-push-off.png",
        "jump-smooth/02-rise.png",
        "jump-smooth/03-descend.png",
        "jump-smooth/04-land.png",
    ),
    "blocked": (
        "blocked/00-interrupt.png",
        "blocked/01-listen.png",
        "blocked/02-weaken.png",
        "blocked/03-deep.png",
        "blocked/04-reconnect.png",
        "blocked/05-recover.png",
    ),
    "needs_input": (
        "needs-input/00-heard.png",
        "needs-input/01-listen.png",
        "needs-input/02-indicate.png",
        "needs-input/03-invite.png",
        "needs-input/04-wait.png",
        "needs-input/05-remind.png",
    ),
    "running": (
        "running/00-focus.png",
        "running/01-tap.png",
        "running/02-swipe.png",
        "running/03-expand.png",
        "running/04-cross-check.png",
        "running/05-resolve.png",
    ),
    "ready": (
        "ready/00-notice.png",
        "ready/01-catch.png",
        "ready/02-wind-up.png",
        "ready/03-throw.png",
        "ready/04-celebrate.png",
        "ready/05-present.png",
    ),
    "gaze_right": (
        "gaze-right/00-back.png",
        "gaze-right/01-back-22.png",
        "gaze-right/02-rear-side.png",
        "gaze-right/03-back-67.png",
        "gaze-right/04-profile.png",
        "gaze-right/05-front-112.png",
        "gaze-right/06-front-135.png",
        "gaze-right/07-front-157.png",
        "gaze-right/08-front.png",
    ),
}

RUNNING_WING_FILES = {
    2: "running/02-swipe-left-wing.png",
    4: "running/04-cross-check-left-wing.png",
}


def clear_transparent_rgb(image: Image.Image) -> Image.Image:
    rgba = image.convert("RGBA")
    data = bytearray(rgba.tobytes())
    for offset in range(0, len(data), 4):
        if data[offset + 3] == 0:
            data[offset] = 0
            data[offset + 1] = 0
            data[offset + 2] = 0
    return Image.frombytes("RGBA", rgba.size, bytes(data))


def repair_visible_green_edge_residue(image: Image.Image) -> tuple[Image.Image, int]:
    """Replace only visible green-screen edge residue while preserving alpha."""

    rgba = clear_transparent_rgb(image)
    pixels = rgba.load()
    alpha = rgba.getchannel("A")
    transparent = alpha.point(lambda value: 255 if value == 0 else 0)
    near_transparency = transparent.filter(ImageFilter.MaxFilter(5))
    contaminated: list[tuple[int, int]] = []
    distance_limit_squared = 96 * 96
    for y in range(rgba.height):
        for x in range(rgba.width):
            red, green, blue, opacity = pixels[x, y]
            if (
                opacity >= 16
                and near_transparency.getpixel((x, y)) > 0
                and red * red + (green - 255) * (green - 255) + blue * blue
                <= distance_limit_squared
            ):
                contaminated.append((x, y))

    for x, y in contaminated:
        replacement: tuple[int, int, int] | None = None
        for radius in range(1, 6):
            candidates: list[tuple[int, int, int, int]] = []
            for candidate_y in range(max(0, y - radius), min(rgba.height, y + radius + 1)):
                for candidate_x in range(max(0, x - radius), min(rgba.width, x + radius + 1)):
                    if max(abs(candidate_x - x), abs(candidate_y - y)) != radius:
                        continue
                    red, green, blue, opacity = pixels[candidate_x, candidate_y]
                    if opacity < 16:
                        continue
                    distance_squared = red * red + (green - 255) * (green - 255) + blue * blue
                    if distance_squared > distance_limit_squared:
                        candidates.append((opacity, red, green, blue))
            if candidates:
                _, red, green, blue = max(candidates)
                replacement = (red, green, blue)
                break
        if replacement is None:
            replacement = (104, 236, 248)
        pixels[x, y] = (*replacement, pixels[x, y][3])

    return clear_transparent_rgb(rgba), len(contaminated)


def resize_rgba(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    """Resize in premultiplied-alpha space to avoid dark edge fringes."""

    width = max(1, int(size[0]))
    height = max(1, int(size[1]))
    premultiplied = image.convert("RGBa")
    resized = premultiplied.resize((width, height), Image.Resampling.LANCZOS)
    return clear_transparent_rgb(resized.convert("RGBA"))


def load_required_image(path: Path, label: str) -> Image.Image:
    image = Image.open(path).convert("RGBA")
    if image.getchannel("A").getbbox() is None:
        raise ValueError(f"empty {label}: {path}")
    return clear_transparent_rgb(image)


def load_keyframes(key: str) -> list[Image.Image]:
    images: list[Image.Image] = []
    for relative_path in KEYFRAME_FILES[key]:
        path = KEYFRAMES / relative_path
        image = Image.open(path).convert("RGBA")
        if image.getchannel("A").getbbox() is None:
            raise ValueError(f"empty key pose: {path}")
        images.append(clear_transparent_rgb(image))
    return images


def load_running_wings() -> dict[int, Image.Image]:
    return {
        frame_index: load_required_image(KEYFRAMES / relative_path, "running wing overlay")
        for frame_index, relative_path in RUNNING_WING_FILES.items()
    }


def normalize_anchor(
    image: Image.Image,
    *,
    target_height: int = 164,
    max_width: int = 184,
    bottom: int = 196,
    dx: int = 0,
) -> Image.Image:
    bbox = image.getchannel("A").getbbox()
    if bbox is None:
        raise ValueError("cannot normalize an empty image")
    crop = image.crop(bbox)
    scale = min(target_height / crop.height, max_width / crop.width)
    resized = resize_rgba(crop, (round(crop.width * scale), round(crop.height * scale)))
    canvas = Image.new("RGBA", (CELL_W, CELL_H), TRANSPARENT)
    left = round((CELL_W - resized.width) / 2) + dx
    top = bottom - resized.height
    canvas.alpha_composite(resized, (left, top))
    return clear_transparent_rgb(canvas)


def normalize_group(
    images: list[Image.Image],
    *,
    target_height: int,
    max_width: int,
    bottom: int | None = 196,
    center_y: int | None = None,
    align_alpha_centroid: bool = False,
) -> list[Image.Image]:
    """Normalize a generated key-pose group with one shared scale.

    ImageGen sheets are authored at a common visual scale. A shared resize factor
    preserves that scale even when the wings or limbs produce different bboxes.
    """

    if (bottom is None) == (center_y is None):
        raise ValueError("provide exactly one of bottom or center_y")
    crops: list[Image.Image] = []
    for image in images:
        bbox = image.getchannel("A").getbbox()
        if bbox is None:
            raise ValueError("cannot normalize an empty key pose")
        crops.append(image.crop(bbox))
    group_width = max(crop.width for crop in crops)
    group_height = max(crop.height for crop in crops)
    scale = min(target_height / group_height, max_width / group_width)
    normalized: list[Image.Image] = []
    for crop in crops:
        resized = resize_rgba(crop, (round(crop.width * scale), round(crop.height * scale)))
        canvas = Image.new("RGBA", (CELL_W, CELL_H), TRANSPARENT)
        if center_y is not None and align_alpha_centroid:
            alpha = resized.getchannel("A")
            weights = alpha.tobytes()
            total = sum(weights)
            if not total:
                raise ValueError("cannot align an empty key pose")
            x_total = 0
            y_total = 0
            for offset, value in enumerate(weights):
                if value:
                    x_total += (offset % resized.width) * value
                    y_total += (offset // resized.width) * value
            left = round(CELL_W / 2 - x_total / total)
            top = round(center_y - y_total / total)
        else:
            left = round((CELL_W - resized.width) / 2)
            top = (
                round(center_y - resized.height / 2)
                if center_y is not None
                else int(bottom) - resized.height
            )
        canvas.alpha_composite(resized, (left, top))
        normalized.append(clear_transparent_rgb(canvas))
    return normalized


def normalize_accessory(
    image: Image.Image,
    *,
    max_width: int,
    max_height: int,
) -> Image.Image:
    """Trim ImageGen extraction haze and resize a transparent accessory layer."""

    alpha = image.getchannel("A")
    meaningful = alpha.point(lambda value: 255 if value >= 24 else 0)
    bbox = meaningful.getbbox()
    if bbox is None:
        raise ValueError("cannot normalize an empty accessory")
    crop = image.crop(bbox)
    scale = min(max_width / crop.width, max_height / crop.height)
    return resize_rgba(crop, (round(crop.width * scale), round(crop.height * scale)))


def warm_head_centroid(image: Image.Image) -> tuple[float, float]:
    """Locate the stable pink/skin head mass for pose-to-pose registration."""

    rgba = image.convert("RGBA")
    pixels = rgba.load()
    x_total = 0.0
    y_total = 0.0
    weight = 0.0
    for y in range(18, min(151, rgba.height)):
        for x in range(25, min(168, rgba.width)):
            red, green, blue, alpha = pixels[x, y]
            if (
                alpha > 64
                and red > 145
                and blue > 80
                and red > green * 1.025
                and red > blue * 0.94
            ):
                x_total += x * alpha
                y_total += y * alpha
                weight += alpha
    if not weight:
        raise ValueError("cannot locate the warm head mass")
    return x_total / weight, y_total / weight


def align_head(
    image: Image.Image,
    target: tuple[float, float],
    *,
    factor: float = 1.0,
    dx: int = 0,
    dy: int = 0,
) -> Image.Image:
    current = warm_head_centroid(image)
    return transform_cell(
        image,
        dx=round((target[0] - current[0]) * factor) + dx,
        dy=round((target[1] - current[1]) * factor) + dy,
    )


def transform_cell(
    image: Image.Image,
    *,
    scale: float = 1.0,
    scale_x: float = 1.0,
    angle: float = 0.0,
    dx: int = 0,
    dy: int = 0,
) -> Image.Image:
    bbox = image.getchannel("A").getbbox()
    if bbox is None:
        return image.copy()
    crop = image.crop(bbox)
    resized = resize_rgba(
        crop,
        (
            round(crop.width * scale * scale_x),
            round(crop.height * scale),
        ),
    )
    if angle:
        resized = clear_transparent_rgb(
            resized.rotate(
                angle,
                resample=Image.Resampling.BICUBIC,
                expand=True,
            )
        )
    center_x = (bbox[0] + bbox[2]) / 2 + dx
    baseline = bbox[3] + dy
    left = round(center_x - resized.width / 2)
    top = round(baseline - resized.height)
    canvas = Image.new("RGBA", image.size, TRANSPARENT)
    canvas.alpha_composite(resized, (left, top))
    return clear_transparent_rgb(canvas)


def mirror(image: Image.Image) -> Image.Image:
    return clear_transparent_rgb(ImageOps.mirror(image))


def with_underlay(frame: Image.Image, underlay: Image.Image) -> Image.Image:
    output = underlay.copy()
    output.alpha_composite(frame)
    return clear_transparent_rgb(output)


def dim(frame: Image.Image, factor: float) -> Image.Image:
    alpha = frame.getchannel("A")
    rgb = ImageEnhance.Brightness(frame.convert("RGB")).enhance(factor)
    output = rgb.convert("RGBA")
    output.putalpha(alpha)
    return clear_transparent_rgb(output)


def draw_glowing_line(
    canvas: Image.Image,
    points: list[tuple[int, int]],
    *,
    color: tuple[int, int, int, int] = CYAN,
    width: int = 2,
    glow: int = 3,
) -> None:
    glow_layer = Image.new("RGBA", canvas.size, TRANSPARENT)
    glow_draw = ImageDraw.Draw(glow_layer)
    glow_color = color[:3] + (80,)
    glow_draw.line(points, fill=glow_color, width=width + glow, joint="curve")
    glow_layer = glow_layer.filter(ImageFilter.GaussianBlur(max(1, glow / 2)))
    canvas.alpha_composite(glow_layer)
    ImageDraw.Draw(canvas).line(points, fill=color, width=width, joint="curve")


def draw_star(
    canvas: Image.Image,
    x: int,
    y: int,
    radius: int,
    color: tuple[int, int, int, int],
) -> None:
    draw = ImageDraw.Draw(canvas)
    points = [(x, y - radius), (x + 2, y - 2), (x + radius, y), (x + 2, y + 2),
              (x, y + radius), (x - 2, y + 2), (x - radius, y), (x - 2, y - 2)]
    draw.polygon(points, fill=color)


def draw_status_diamond(
    canvas: Image.Image,
    center: tuple[int, int],
    *,
    radius: int = 12,
    fill: tuple[int, int, int, int] = (28, 42, 69, 225),
    outline: tuple[int, int, int, int] = PALE_CYAN,
) -> None:
    x, y = center
    ImageDraw.Draw(canvas).polygon(
        ((x, y - radius), (x + radius, y), (x, y + radius), (x - radius, y)),
        fill=fill,
        outline=outline,
    )


def add_core_pulse(frame: Image.Image, phase: float) -> Image.Image:
    output = frame.copy()
    overlay = Image.new("RGBA", frame.size, TRANSPARENT)
    radius = 2 + round(phase * 2)
    glow = Image.new("RGBA", frame.size, TRANSPARENT)
    ImageDraw.Draw(glow).ellipse(
        (96 - radius * 2, 148 - radius * 2, 96 + radius * 2, 148 + radius * 2),
        fill=(90, 244, 255, 70),
    )
    glow = glow.filter(ImageFilter.GaussianBlur(3))
    overlay.alpha_composite(glow)
    draw_star(overlay, 96, 148, radius + 1, PALE_CYAN)
    output.alpha_composite(overlay)
    return clear_transparent_rgb(output)


def add_idle_snow(frame: Image.Image, index: int) -> Image.Image:
    output = frame.copy()
    overlay = Image.new("RGBA", frame.size, TRANSPARENT)
    positions = (
        ((18, 88), (169, 124), (38, 166)),
        ((22, 84), (165, 119), (42, 163)),
        ((27, 80), (160, 115), (47, 159)),
        ((31, 84), (156, 111), (43, 155)),
        ((27, 89), (160, 116), (39, 159)),
        ((22, 92), (165, 121), (36, 164)),
    )[index]
    for pos_index, (x, y) in enumerate(positions):
        color = (PALE_CYAN, PALE_PINK, GOLD)[pos_index]
        draw_star(overlay, x, y, 3 if pos_index == 0 else 2, color)
    output.alpha_composite(overlay)
    return clear_transparent_rgb(output)


def movement_underlay(index: int) -> Image.Image:
    layer = Image.new("RGBA", (CELL_W, CELL_H), TRANSPARENT)
    draw = ImageDraw.Draw(layer)
    shifts = (0, 1, 5, 10, 12, 8, 3, 0)
    shift = shifts[index]
    if index not in (2, 3, 4, 5):
        return layer
    for line_index, y in enumerate((91, 151)):
        length = 19 + line_index * 7 + shift
        x2 = 45 + shift
        x1 = max(7, x2 - length)
        color = (90, 238, 249, 80 - line_index * 18)
        draw.line((x1, y, x2, y), fill=color, width=1)
        draw.rectangle((x1 - 2, y, x1, y), fill=(255, 135, 201, 65))
    return layer


def working_underlay(index: int) -> Image.Image:
    layer = Image.new("RGBA", (CELL_W, CELL_H), TRANSPARENT)
    glow = Image.new("RGBA", layer.size, TRANSPARENT)
    glow_draw = ImageDraw.Draw(glow)
    panel = ((54, 132), (138, 132), (148, 169), (44, 169))
    glow_draw.polygon(panel, fill=(65, 232, 244, 42), outline=(112, 245, 252, 105))
    glow = glow.filter(ImageFilter.GaussianBlur(3))
    layer.alpha_composite(glow)
    draw = ImageDraw.Draw(layer)
    draw.polygon(panel, fill=(34, 184, 210, 32), outline=(151, 248, 255, 170))
    split = 96 + (-8, -3, 3, 8, 2, -5)[index]
    draw.line((split, 136, split, 165), fill=(255, 184, 224, 105), width=1)
    return layer


def draw_ripple(canvas: Image.Image, center: tuple[int, int], radius: int, alpha: int) -> None:
    draw = ImageDraw.Draw(canvas)
    x, y = center
    draw.ellipse((x - radius, y - radius, x + radius, y + radius), outline=(112, 239, 250, alpha), width=2)


def add_glitch(frame: Image.Image, index: int) -> Image.Image:
    output = Image.new("RGBA", frame.size, TRANSPARENT)
    alpha = frame.getchannel("A")
    strength = (0, 50, 80, 105, 70, 45, 25, 10)[index]
    if strength:
        for color, shift in (((70, 240, 252, strength), -2), ((255, 105, 185, strength), 2)):
            ghost = Image.new("RGBA", frame.size, color)
            ghost.putalpha(alpha.point(lambda value, a=strength: round(value * a / 255)))
            output.alpha_composite(ghost, (shift, 0))
    output.alpha_composite(frame)
    if index in (2, 3, 4):
        draw = ImageDraw.Draw(output)
        bars = ((52, 80, 131, 83), (43, 122, 151, 125), (62, 163, 137, 165))
        for bar_index, box in enumerate(bars):
            dx = (-3, 4, -2)[bar_index] if index % 2 == 0 else (3, -4, 2)[bar_index]
            strip = frame.crop(box)
            output.alpha_composite(strip, (box[0] + dx, box[1]))
            draw.line((box[0], box[1], box[2], box[1]), fill=(CYAN if bar_index % 2 == 0 else PINK), width=1)
    return clear_transparent_rgb(output)


def add_notification_ping(frame: Image.Image, index: int) -> Image.Image:
    output = frame.copy()
    overlay = Image.new("RGBA", frame.size, TRANSPARENT)
    center = (160, 76)
    radius = 14 + (index % 3) * 3
    draw_ripple(overlay, center, radius, 115 - (index % 3) * 20)
    draw = ImageDraw.Draw(overlay)
    x, y = center
    draw.polygon(
        ((x, y - 10), (x + 9, y), (x, y + 10), (x - 9, y)),
        fill=(28, 53, 75, 218),
        outline=PALE_CYAN,
    )
    draw.line((x, y - 5, x, y + 2), fill=PALE_PINK, width=2)
    draw.ellipse((x - 1, y + 5, x + 1, y + 7), fill=PALE_PINK)
    if index in (2, 3):
        draw_star(overlay, 177, 64, 3, PALE_CYAN)
    output.alpha_composite(overlay)
    return clear_transparent_rgb(output)


def add_blocked_badge(frame: Image.Image, index: int) -> Image.Image:
    """Keep the error state legible even when reduced motion shows frame zero."""

    output = frame.copy()
    overlay = Image.new("RGBA", frame.size, TRANSPARENT)
    center = (158, 66)
    pulse = (10, 11, 13, 15, 15, 13, 11, 10)[index]
    draw_ripple(overlay, center, pulse, 90)
    draw_status_diamond(
        overlay,
        center,
        radius=11,
        fill=(57, 29, 62, 226),
        outline=PALE_PINK,
    )
    draw = ImageDraw.Draw(overlay)
    draw.line((153, 61, 163, 71), fill=PALE_PINK, width=3)
    draw.line((163, 61, 153, 71), fill=PALE_CYAN, width=3)
    output.alpha_composite(overlay)
    return clear_transparent_rgb(output)


def add_ready_badge(frame: Image.Image, index: int) -> Image.Image:
    output = frame.copy()
    overlay = Image.new("RGBA", frame.size, TRANSPARENT)
    center = (33, 62)
    radius = 11 + (1 if index in (3, 4) else 0)
    draw_status_diamond(overlay, center, radius=radius)
    draw = ImageDraw.Draw(overlay)
    draw.line((28, 62, 32, 66, 39, 57), fill=PALE_CYAN, width=3, joint="curve")
    if index in (3, 4):
        draw_star(overlay, 48, 51, 3, GOLD)
    output.alpha_composite(overlay)
    return clear_transparent_rgb(output)


def add_running_scan(frame: Image.Image, index: int) -> Image.Image:
    output = frame.copy()
    overlay = Image.new("RGBA", frame.size, TRANSPARENT)
    scan_y = (157, 161, 165, 169, 173, 163)[index]
    draw_glowing_line(overlay, [(62, scan_y), (131, scan_y)], width=1, glow=2)
    draw = ImageDraw.Draw(overlay)
    for dot in range(4):
        active = dot <= index % 5
        fill = PALE_CYAN if active else (104, 236, 248, 70)
        x = 76 + dot * 13
        draw.rounded_rectangle((x, 185, x + 7, 188), radius=1, fill=fill)
    if index in (2, 3):
        draw_star(overlay, 153, 117, 3, PALE_PINK)
    spinner = (148, 54, 172, 78)
    for segment in range(6):
        start = (segment * 60 + index * 60) % 360
        color = PALE_CYAN if segment in (0, 1) else (104, 236, 248, 70)
        draw.arc(spinner, start, start + 34, fill=color, width=2)
    draw_star(overlay, 160, 66, 2, PALE_PINK)
    output.alpha_composite(overlay)
    return clear_transparent_rgb(output)


def paper_plane(center: tuple[int, int], angle: float, scale: float = 1.0) -> Image.Image:
    size = round(28 * scale)
    tile = Image.new("RGBA", (size * 2, size * 2), TRANSPARENT)
    cx = cy = size
    tail_top = (cx - size // 2, cy - size // 4)
    nose = (cx + size // 2, cy)
    tail_bottom = (cx - size // 3, cy + size // 3)
    fold = (cx - size // 10, cy)
    points = [tail_top, nose, tail_bottom, fold]
    draw = ImageDraw.Draw(tile)
    draw.polygon(points, fill=WHITE)
    draw.polygon((tail_top, fold, tail_bottom), fill=LAVENDER)
    draw.line(points + [points[0]], fill=NAVY, width=2, joint="curve")
    draw.line((tail_bottom, fold, nose), fill=CYAN, width=2, joint="curve")
    tile = clear_transparent_rgb(tile.rotate(angle, resample=Image.Resampling.BICUBIC, expand=True))
    canvas = Image.new("RGBA", (CELL_W, CELL_H), TRANSPARENT)
    left = round(center[0] - tile.width / 2)
    top = round(center[1] - tile.height / 2)
    glow = Image.new("RGBA", tile.size, (88, 238, 250, 0))
    glow.putalpha(tile.getchannel("A").point(lambda value: round(value * 0.45)))
    glow = glow.filter(ImageFilter.GaussianBlur(3))
    canvas.alpha_composite(glow, (left, top))
    canvas.alpha_composite(tile, (left, top))
    return canvas


def add_sunglasses(frame: Image.Image, *, dx: int = 0, dy: int = 0) -> Image.Image:
    """One-frame cameo based on the approved V3 sunglasses form."""

    output = frame.copy()
    overlay = Image.new("RGBA", frame.size, TRANSPARENT)
    draw = ImageDraw.Draw(overlay)
    draw.rounded_rectangle(
        (69 + dx, 98 + dy, 95 + dx, 113 + dy),
        radius=3,
        fill=(18, 23, 38, 245),
        outline=(8, 11, 22, 255),
        width=2,
    )
    draw.rounded_rectangle(
        (97 + dx, 98 + dy, 123 + dx, 113 + dy),
        radius=3,
        fill=(25, 20, 47, 245),
        outline=(8, 11, 22, 255),
        width=2,
    )
    draw.rectangle((93 + dx, 102 + dy, 99 + dx, 106 + dy), fill=(8, 11, 22, 255))
    draw.rectangle((73 + dx, 101 + dy, 82 + dx, 110 + dy), fill=(72, 217, 226, 135))
    draw.rectangle((105 + dx, 101 + dy, 119 + dx, 110 + dy), fill=(185, 88, 200, 125))
    output.alpha_composite(overlay)
    return clear_transparent_rgb(output)


def build_idle(keyposes: list[Image.Image], neutral_anchor: Image.Image) -> list[Image.Image]:
    poses = normalize_group(keyposes, target_height=164, max_width=184, bottom=196)
    target_head = warm_head_centroid(poses[0])
    poses = [align_head(pose, target_head, factor=0.82) for pose in poses]
    dys = (0, -1, 0, 0, 0, 1)
    scales = (1.0, 1.002, 1.0, 1.0, 1.0, 0.998)
    frames = []
    for index, pose in enumerate(poses):
        frame = pose
        frame = transform_cell(frame, scale=scales[index], dy=dys[index])
        frame = add_core_pulse(frame, (math.sin(index / 6 * math.tau) + 1) / 2)
        frames.append(add_idle_snow(frame, index))
    neutral = normalize_anchor(neutral_anchor, target_height=164, max_width=184, bottom=196)
    return frames + [neutral]


def build_move(keyposes: list[Image.Image]) -> list[Image.Image]:
    poses = normalize_group(
        keyposes,
        target_height=168,
        max_width=188,
        bottom=None,
        center_y=115,
    )
    head_points = [warm_head_centroid(pose) for pose in poses]
    head_pivot = (
        sum(point[0] for point in head_points) / len(head_points),
        sum(point[1] for point in head_points) / len(head_points),
    )
    poses = [align_head(pose, head_pivot) for pose in poses]
    dxs = (-1, 0, 0, 1, 1, 0, -1, -1)
    dys = (1, 0, -1, -2, -1, 0, 1, 1)
    angles = (-0.2, -0.1, 0.0, 0.12, 0.18, 0.08, -0.08, -0.18)
    scales = (0.999, 1.0, 1.001, 1.002, 1.001, 1.0, 0.999, 0.999)
    frames = []
    for index in range(8):
        pose = transform_cell(
            poses[index],
            scale=scales[index],
            angle=angles[index],
            dx=dxs[index],
            dy=dys[index],
        )
        frames.append(with_underlay(pose, movement_underlay(index)))
    return frames


def build_wave(keyposes: list[Image.Image]) -> list[Image.Image]:
    poses = normalize_group(keyposes, target_height=166, max_width=188, bottom=196)
    offsets = ((0, 0), (5, 1), (4, 5), (3, 0))
    frames = []
    for index, (pose, (dx, dy)) in enumerate(zip(poses, offsets)):
        frame = transform_cell(pose, dx=dx, dy=dy)
        overlay = Image.new("RGBA", frame.size, TRANSPARENT)
        cue_alpha = (55, 105, 220, 95)[index]
        draw = ImageDraw.Draw(overlay)
        draw.line((170, 73, 170, 151), fill=(104, 236, 248, cue_alpha), width=1)
        draw.ellipse((144, 105, 158, 119), outline=(112, 239, 250, cue_alpha), width=2)
        if index == 0:
            draw.arc((126, 76, 151, 101), 250, 55, fill=PALE_CYAN, width=2)
        if index == 1:
            draw_star(overlay, 156, 82, 3, PALE_CYAN)
            draw.line((136, 111, 146, 111), fill=PALE_CYAN, width=2)
        if index == 2:
            draw_ripple(overlay, (149, 112), 10, 180)
            draw_ripple(overlay, (149, 112), 15, 85)
        if index == 3:
            draw_ripple(overlay, (149, 112), 12, 65)
        frame.alpha_composite(overlay)
        frames.append(clear_transparent_rgb(frame))
    return frames


def build_jump(keyposes: list[Image.Image]) -> list[Image.Image]:
    poses = normalize_group(
        keyposes,
        target_height=156,
        max_width=184,
        bottom=None,
        center_y=104,
        align_alpha_centroid=True,
    )
    dys = (6, 0, -6, 0, 6)
    scales = (1.0, 1.0, 1.0, 1.0, 1.0)
    angles = (0.0, -0.3, 0.0, 0.3, 0.0)
    frames = []
    for index in range(5):
        frame = transform_cell(
            poses[index],
            scale=scales[index],
            angle=angles[index],
            dy=dys[index],
        )
        overlay = Image.new("RGBA", frame.size, TRANSPARENT)
        if index in (0, 4):
            draw = ImageDraw.Draw(overlay)
            draw.arc((58, 181, 134, 204), 190, 350, fill=(104, 236, 248, 190), width=2)
        if index == 1:
            draw_glowing_line(overlay, [(48, 166), (48, 190)], width=1, glow=2)
            draw_glowing_line(overlay, [(144, 162), (144, 188)], width=1, glow=2)
        if index == 2:
            draw_glowing_line(overlay, [(42, 153), (42, 186)], width=1, glow=2)
            draw_glowing_line(overlay, [(150, 149), (150, 183)], width=1, glow=2)
            draw_star(overlay, 46, 69, 4, PALE_CYAN)
            draw_star(overlay, 148, 81, 3, PALE_PINK)
        if index == 3:
            draw_glowing_line(overlay, [(47, 61), (47, 84)], width=1, glow=2)
            draw_glowing_line(overlay, [(145, 65), (145, 89)], width=1, glow=2)
        frame.alpha_composite(overlay)
        frames.append(clear_transparent_rgb(frame))
    return frames


def build_blocked(keyposes: list[Image.Image]) -> list[Image.Image]:
    poses = normalize_group(keyposes, target_height=164, max_width=184, bottom=196)
    pose_order = (0, 1, 2, 3, 3, 4, 5, 0)
    brightness = (0.95, 0.90, 0.80, 0.66, 0.60, 0.76, 0.94, 0.98)
    offsets = ((0, 0), (-1, -1), (2, 12), (-1, 0), (1, 1), (1, 7), (4, 1), (0, 0))
    frames = []
    for index in range(8):
        frame = transform_cell(
            poses[pose_order[index]],
            dx=offsets[index][0],
            dy=offsets[index][1],
        )
        frame = dim(frame, brightness[index])
        frame = add_glitch(frame, index)
        if index in (5, 6):
            overlay = Image.new("RGBA", frame.size, TRANSPARENT)
            draw_star(overlay, 96, 27, 4 + (index - 5), PALE_CYAN)
            frame.alpha_composite(overlay)
        frames.append(add_blocked_badge(frame, index))
    return frames


def build_needs_input(keyposes: list[Image.Image]) -> list[Image.Image]:
    poses = normalize_group(keyposes, target_height=166, max_width=184, bottom=196)
    target_head = warm_head_centroid(poses[0])
    poses = [align_head(pose, target_head, factor=0.65) for pose in poses]
    pose_order = (0, 1, 2, 3, 4, 5)
    offsets = ((0, 0), (1, -1), (2, 0), (2, 1), (0, 0), (1, -1))
    frames = []
    for index, (pose_index, (dx, dy)) in enumerate(zip(pose_order, offsets)):
        pose = transform_cell(poses[pose_index], dx=dx, dy=dy)
        frames.append(add_notification_ping(pose, index))
    return frames


def build_running(
    keyposes: list[Image.Image],
    wing_overlays: dict[int, Image.Image],
) -> list[Image.Image]:
    poses = normalize_group(keyposes, target_height=166, max_width=184, bottom=196)
    target_head = warm_head_centroid(poses[0])
    poses = [align_head(pose, target_head, factor=0.72) for pose in poses]
    normalized_wings = {
        2: normalize_accessory(wing_overlays[2], max_width=88, max_height=62),
        4: normalize_accessory(wing_overlays[4], max_width=84, max_height=82),
    }
    wing_positions = {2: (4, 91), 4: (6, 68)}
    offsets = ((0, 0), (0, -1), (1, 0), (1, -1), (0, 1), (0, 0))
    frames = []
    for index, (pose, (dx, dy)) in enumerate(zip(poses, offsets)):
        pose = transform_cell(pose, dx=dx, dy=dy)
        underlay = working_underlay(index)
        if index in normalized_wings:
            underlay.alpha_composite(normalized_wings[index], wing_positions[index])
        frame = with_underlay(pose, underlay)
        frame = add_core_pulse(frame, 0.35 + (index % 3) * 0.25)
        frames.append(add_running_scan(frame, index))
    return frames


def build_ready(keyposes: list[Image.Image]) -> list[Image.Image]:
    poses = normalize_group(keyposes, target_height=168, max_width=188, bottom=196)
    head_points = [warm_head_centroid(pose) for pose in poses]
    head_pivot = (
        sum(point[0] for point in head_points) / len(head_points),
        sum(point[1] for point in head_points) / len(head_points),
    )
    poses = [align_head(pose, head_pivot, factor=0.78) for pose in poses]
    pose_order = (0, 1, 2, 3, 4, 5)
    positions = ((161, 111), (112, 126), (63, 110), (145, 91), (130, 49), (62, 78))
    angles = (168, 145, -32, 8, 35, -55)
    plane_scales = (1.12, 1.08, 1.18, 1.30, 1.20, 1.12)
    offsets = ((0, 0), (0, -2), (0, -3), (-1, 0), (0, 1), (0, 0))
    frames = []
    for index, (pose_index, position, plane_angle, plane_scale, (dx, dy)) in enumerate(
        zip(pose_order, positions, angles, plane_scales, offsets)
    ):
        frame = transform_cell(poses[pose_index], dx=dx, dy=dy)
        plane_position = (position[0] + dx, position[1] + dy)
        plane = paper_plane(plane_position, plane_angle, plane_scale)
        # The returning plane passes behind Snowfluff instead of cutting across
        # her halo and hair, preserving a believable depth order.
        if index == 5:
            frame = with_underlay(frame, plane)
        else:
            frame.alpha_composite(plane)
        overlay = Image.new("RGBA", frame.size, TRANSPARENT)
        if index in (1, 2, 3):
            draw_star(overlay, 31 + index * 8, 72 + index * 5, 3, GOLD if index == 2 else PALE_CYAN)
        frame.alpha_composite(overlay)
        if index == 4:
            frame = add_sunglasses(frame, dx=dx, dy=dy)
        frames.append(add_ready_badge(frame, index))
    return frames


def build_gaze(keyposes: list[Image.Image]) -> list[Image.Image]:
    # ImageGen authored nine genuine angular drawings. The sheet's fourth cell
    # is visually between cells two and three, so order by observed yaw rather
    # than by grid position. Mirroring the interior seven poses completes a
    # seamless 16-direction orbit without synthesising flattened silhouettes.
    poses = normalize_group(keyposes, target_height=162, max_width=184, bottom=196)
    # Different yaw silhouettes have very different bbox widths and generated
    # head heights. Register the warm head mass to one rotation pivot; this is a
    # hovering character, so a stable cranial/halo axis reads more naturally
    # than pinning every view's feet to an artificial ground line.
    poses = [align_head(pose, (96, 104)) for pose in poses]
    yaw_order = (0, 1, 3, 2, 4, 5, 6, 7, 8)
    right_half = [poses[index] for index in yaw_order]
    left_half = [mirror(frame) for frame in reversed(right_half[1:8])]
    frames = right_half + left_half
    if len(frames) != 16:
        raise AssertionError(f"expected 16 gaze frames, got {len(frames)}")
    for index, frame in enumerate(frames):
        overlay = Image.new("RGBA", frame.size, TRANSPARENT)
        radians = math.radians(index * 22.5)
        x = round(96 + math.sin(radians) * 70)
        y = round(110 - math.cos(radians) * 54)
        ImageDraw.Draw(overlay).ellipse((x - 1, y - 1, x + 1, y + 1), fill=(104, 236, 248, 145))
        frame.alpha_composite(overlay)
        frames[index] = clear_transparent_rgb(frame)
    return frames


def alpha_digest(image: Image.Image) -> str:
    return hashlib.sha256(image.tobytes()).hexdigest()


def save_frame_rows(rows: list[list[Image.Image]]) -> Image.Image:
    sheet = Image.new("RGBA", SHEET_SIZE, TRANSPARENT)
    frames_dir = OUT / "frames"
    for row_index, frames in enumerate(rows):
        state_dir = frames_dir / f"{row_index:02d}-{STATE_NAMES[row_index]}"
        state_dir.mkdir(parents=True, exist_ok=True)
        if len(frames) != USED_COUNTS[row_index]:
            raise ValueError(
                f"{STATE_NAMES[row_index]} produced {len(frames)} frames; "
                f"expected {USED_COUNTS[row_index]}"
            )
        for col_index, frame in enumerate(frames):
            frame = clear_transparent_rgb(frame)
            frame.save(state_dir / f"{col_index:02d}.png")
            sheet.alpha_composite(frame, (col_index * CELL_W, row_index * CELL_H))
    return clear_transparent_rgb(sheet)


def dark_tile(frame: Image.Image, background: tuple[int, int, int, int] = (10, 14, 31, 255)) -> Image.Image:
    tile = Image.new("RGBA", frame.size, background)
    tile.alpha_composite(frame)
    return tile


def font(size: int = 13) -> ImageFont.ImageFont:
    for path in ("C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/consola.ttf"):
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            pass
    return ImageFont.load_default()


def make_previews(rows: list[list[Image.Image]], sheet: Image.Image) -> None:
    preview_dir = OUT / "previews"
    preview_dir.mkdir(parents=True, exist_ok=True)
    for row_index in range(9):
        frames = [dark_tile(frame).convert("RGB") for frame in rows[row_index][:ANIMATION_COUNTS[row_index]]]
        scaled = [frame.resize((CELL_W * 3, CELL_H * 3), Image.Resampling.LANCZOS) for frame in frames]
        scaled[0].save(
            preview_dir / f"{row_index:02d}-{STATE_NAMES[row_index]}.gif",
            save_all=True,
            append_images=scaled[1:],
            duration=list(FRAME_DURATIONS[row_index]),
            loop=0,
            disposal=2,
        )

    direction_frames = rows[9] + rows[10]
    direction_tiles = []
    label_font = font(12)
    for index, frame in enumerate(direction_frames):
        tile = Image.new("RGBA", (CELL_W, CELL_H + 20), (10, 14, 31, 255))
        tile.alpha_composite(frame)
        ImageDraw.Draw(tile).text((6, CELL_H + 1), f"{index * 22.5:05.1f} deg", fill=PALE_CYAN, font=label_font)
        direction_tiles.append(tile.convert("RGB").resize((CELL_W * 3, (CELL_H + 20) * 3), Image.Resampling.LANCZOS))
    direction_tiles[0].save(
        preview_dir / "10-gaze-clockwise.gif",
        save_all=True,
        append_images=direction_tiles[1:],
        duration=90,
        loop=0,
        disposal=2,
    )

    showcase = []
    for phase in range(8):
        grid = Image.new("RGBA", (CELL_W * 3, CELL_H * 3), (10, 14, 31, 255))
        for state_index in range(9):
            frames = rows[state_index][:ANIMATION_COUNTS[state_index]]
            frame_index = min(len(frames) - 1, phase * len(frames) // 8)
            x = (state_index % 3) * CELL_W
            y = (state_index // 3) * CELL_H
            grid.alpha_composite(frames[frame_index], (x, y))
        showcase.append(grid.convert("RGB").resize((CELL_W * 6, CELL_H * 6), Image.Resampling.LANCZOS))
    showcase[0].save(
        OUT / "showcase.gif",
        save_all=True,
        append_images=showcase[1:],
        duration=100,
        loop=0,
        disposal=2,
    )

    overview = Image.new("RGBA", sheet.size, (10, 14, 31, 255))
    overview.alpha_composite(sheet)
    overview.save(OUT / "full-atlas-overview.png")

    backgrounds = ((10, 14, 31, 255), (246, 247, 250, 255), (89, 96, 117, 255), (20, 79, 86, 255))
    static_rows = (0, 5, 6, 7, 8)
    board = Image.new("RGBA", (CELL_W * len(static_rows), CELL_H * len(backgrounds)), TRANSPARENT)
    for bg_index, background in enumerate(backgrounds):
        for state_index, row_index in enumerate(static_rows):
            tile = dark_tile(rows[row_index][0], background)
            board.alpha_composite(tile, (state_index * CELL_W, bg_index * CELL_H))
    board.save(OUT / "background-check.png")


def source_image_report(path: Path, image: Image.Image) -> dict[str, object]:
    bbox = image.getchannel("A").getbbox()
    return {
        "path": str(path.relative_to(ROOT)).replace("\\", "/"),
        "sha256_file": hashlib.sha256(path.read_bytes()).hexdigest(),
        "sha256_rgba": alpha_digest(image),
        "size": list(image.size),
        "alpha_bbox": list(bbox) if bbox else None,
    }


def keyframe_report(keyframes: dict[str, list[Image.Image]]) -> dict[str, object]:
    report: dict[str, object] = {}
    for key, images in keyframes.items():
        items = []
        for relative_path, image in zip(KEYFRAME_FILES[key], images):
            path = KEYFRAMES / relative_path
            bbox = image.getchannel("A").getbbox()
            items.append(
                {
                    "path": str(path.relative_to(ROOT)).replace("\\", "/"),
                    "sha256_file": hashlib.sha256(path.read_bytes()).hexdigest(),
                    "sha256_rgba": alpha_digest(image),
                    "size": list(image.size),
                    "alpha_bbox": list(bbox) if bbox else None,
                }
            )
        report[key] = items
    return report


def alpha_centroid(frame: Image.Image) -> tuple[float, float]:
    alpha = frame.getchannel("A")
    pixels = alpha.tobytes()
    weight = sum(pixels)
    if not weight:
        raise ValueError("cannot measure an empty frame")
    x_weight = 0
    y_weight = 0
    for index, value in enumerate(pixels):
        if value:
            x_weight += (index % frame.width) * value
            y_weight += (index // frame.width) * value
    return x_weight / weight, y_weight / weight


def summarize_motion(frames: list[Image.Image]) -> dict[str, object]:
    """Measure adjacent continuity and the explicit last-to-first seam."""

    masks = [
        bytes(1 if value > 20 else 0 for value in frame.getchannel("A").tobytes())
        for frame in frames
    ]
    centroids = [alpha_centroid(frame) for frame in frames]
    ious: list[float] = []
    centroid_steps: list[float] = []
    for index in range(len(frames)):
        next_index = (index + 1) % len(frames)
        intersection = sum(a and b for a, b in zip(masks[index], masks[next_index]))
        union = sum(a or b for a, b in zip(masks[index], masks[next_index]))
        ious.append(intersection / union if union else 1.0)
        centroid_steps.append(math.dist(centroids[index], centroids[next_index]))
    return {
        "adjacent_silhouette_iou_average": round(sum(ious) / len(ious), 4),
        "adjacent_silhouette_iou_minimum": round(min(ious), 4),
        "centroid_step_average": round(sum(centroid_steps) / len(centroid_steps), 3),
        "centroid_step_maximum": round(max(centroid_steps), 3),
        "loop_silhouette_iou": round(ious[-1], 4),
        "loop_centroid_step": round(centroid_steps[-1], 3),
    }


def motion_report(rows: list[list[Image.Image]]) -> dict[str, object]:
    report: dict[str, object] = {}
    for row_index in range(9):
        frames = rows[row_index][:ANIMATION_COUNTS[row_index]]
        report[STATE_NAMES[row_index]] = summarize_motion(frames)
    report["gaze-360"] = summarize_motion(rows[9] + rows[10])
    return report


def build() -> None:
    if OUT.exists():
        shutil.rmtree(OUT)
    OUT.mkdir(parents=True)

    identity_master_hash = hashlib.sha256(IDENTITY_MASTER.read_bytes()).hexdigest().upper()
    if identity_master_hash != IDENTITY_MASTER_SHA256:
        raise ValueError(
            f"identity master hash mismatch: expected {IDENTITY_MASTER_SHA256}, "
            f"got {identity_master_hash}"
        )
    neutral_frame = load_required_image(NEUTRAL_FRAME, "reduced-motion neutral frame")
    project_version = VERSION_FILE.read_text(encoding="utf-8").strip()
    if not project_version:
        raise ValueError(f"empty project version: {VERSION_FILE}")
    keyframes = {key: load_keyframes(key) for key in KEYFRAME_FILES}
    running_wings = load_running_wings()
    rows: list[list[Image.Image]] = []
    rows.append(build_idle(keyframes["idle"], neutral_frame))
    move_right = build_move(keyframes["move_right"])
    rows.append(move_right)
    rows.append([mirror(frame) for frame in move_right])
    rows.append(build_wave(keyframes["wave"]))
    rows.append(build_jump(keyframes["jump"]))
    rows.append(build_blocked(keyframes["blocked"]))
    rows.append(build_needs_input(keyframes["needs_input"]))
    rows.append(build_running(keyframes["running"], running_wings))
    rows.append(build_ready(keyframes["ready"]))
    gaze = build_gaze(keyframes["gaze_right"])
    rows.append(gaze[:8])
    rows.append(gaze[8:])

    green_edge_repairs: dict[str, int] = {}
    for row_index, frames in enumerate(rows):
        for frame_index, frame in enumerate(frames):
            repaired, repair_count = repair_visible_green_edge_residue(frame)
            rows[row_index][frame_index] = repaired
            if repair_count:
                green_edge_repairs[f"r{row_index}c{frame_index}"] = repair_count

    sheet = save_frame_rows(rows)
    sheet_path = OUT / "spritesheet.webp"
    sheet.save(sheet_path, format="WEBP", lossless=True, exact=True, quality=100, method=6)
    encoded = Image.open(sheet_path).convert("RGBA")
    if encoded.tobytes() != sheet.tobytes():
        raise RuntimeError("lossless WebP round-trip changed RGBA pixels")

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    (OUT / "pet.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    make_previews(rows, encoded)

    report = {
        "version": "final-motion-candidate",
        "project_version": project_version,
        "identity_master": str(IDENTITY_MASTER.relative_to(ROOT)).replace("\\", "/"),
        "identity_master_sha256": identity_master_hash,
        "sheet": {
            "path": str(sheet_path.relative_to(ROOT)).replace("\\", "/"),
            "size": list(encoded.size),
            "sha256": hashlib.sha256(sheet_path.read_bytes()).hexdigest(),
            "used_counts": list(USED_COUNTS),
        },
        "reduced_motion_neutral": source_image_report(NEUTRAL_FRAME, neutral_frame),
        "keyframes": keyframe_report(keyframes),
        "running_wing_overlays": {
            str(frame_index): source_image_report(KEYFRAMES / RUNNING_WING_FILES[frame_index], image)
            for frame_index, image in running_wings.items()
        },
        "green_edge_repairs": {
            "method": "nearest-clean-edge-rgb-with-alpha-preserved",
            "total": sum(green_edge_repairs.values()),
            "by_cell": green_edge_repairs,
        },
        "motion": motion_report(rows),
        "state_first_frame_hashes": {
            STATE_NAMES[index]: alpha_digest(rows[index][0]) for index in range(9)
        },
        "gaze_unique_frames": len({alpha_digest(frame) for frame in gaze}),
    }
    (OUT / "build-report.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Wrote smooth desktop pet candidate to {OUT}")
    print(f"Spritesheet: {sheet_path} ({encoded.width}x{encoded.height})")
    print(f"Gaze directions: {report['gaze_unique_frames']}/16 unique")


if __name__ == "__main__":
    build()
