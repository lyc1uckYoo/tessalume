from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image


ATLAS_SIZE = (1536, 2288)
CELL_SIZE = (192, 208)
IDLE_COLUMNS = 6
STATE_FRAME_INDEX = {
    "open": 0,
    "micro": 1,
    "half": 2,
    "closed": 3,
}
RUNTIME_SEQUENCE = ("open", "micro", "half", "closed", "half", "micro", "open")
RUNTIME_DURATIONS_MS = (3000, 55, 55, 70, 55, 55, 3310)
MAX_FILE_BYTES = 20 * 1024 * 1024


def parse_args() -> argparse.Namespace:
    project = Path(__file__).resolve().parents[1]
    run = project / "build" / "hatch-run"
    parser = argparse.ArgumentParser(
        description=(
            "Embed a smooth seated blink inside an animated WebP atlas while keeping "
            "Codex's six slow idle cells visually identical."
        )
    )
    parser.add_argument(
        "--source-atlas",
        type=Path,
        default=run / "final" / "spritesheet-static.webp",
    )
    parser.add_argument(
        "--idle-frames-dir",
        type=Path,
        default=run / "frames" / "idle",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=run / "runtime-experiment" / "spritesheet-animated.png",
    )
    parser.add_argument(
        "--report",
        type=Path,
        default=run / "runtime-experiment" / "runtime-animated-idle-report.json",
    )
    return parser.parse_args()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def load_rgba(path: Path, expected_size: tuple[int, int]) -> Image.Image:
    if not path.is_file():
        raise FileNotFoundError(path)
    with Image.open(path) as opened:
        image = opened.convert("RGBA")
    if image.size != expected_size:
        raise ValueError(f"unexpected image size for {path}: {image.size}")
    return image


def load_idle_states(frames_dir: Path) -> dict[str, Image.Image]:
    return {
        state: load_rgba(frames_dir / f"{index:02d}.png", CELL_SIZE)
        for state, index in STATE_FRAME_INDEX.items()
    }


def animated_atlas_frame(base: Image.Image, idle_state: Image.Image) -> Image.Image:
    frame = base.copy()
    for column in range(IDLE_COLUMNS):
        left = column * CELL_SIZE[0]
        frame.paste((0, 0, 0, 0), (left, 0, left + CELL_SIZE[0], CELL_SIZE[1]))
        frame.alpha_composite(idle_state, (left, 0))
    return frame


def frames_are_equal(first: Image.Image, second: Image.Image) -> bool:
    first_rgba = np.asarray(first)
    second_rgba = np.asarray(second)
    if not np.array_equal(first_rgba[:, :, 3], second_rgba[:, :, 3]):
        return False
    visible = np.logical_or(first_rgba[:, :, 3] > 0, second_rgba[:, :, 3] > 0)
    return np.array_equal(first_rgba[:, :, :3][visible], second_rgba[:, :, :3][visible])


def main() -> int:
    args = parse_args()
    source = args.source_atlas.resolve()
    output = args.output.resolve()
    report_path = args.report.resolve()
    if source == output:
        raise ValueError("source atlas and animated output must be different files")

    base = load_rgba(source, ATLAS_SIZE)
    states = load_idle_states(args.idle_frames_dir.resolve())
    encoded_frames = [animated_atlas_frame(base, states[state]) for state in RUNTIME_SEQUENCE]

    output.parent.mkdir(parents=True, exist_ok=True)
    if output.suffix.lower() == ".png":
        encoded_frames[0].save(
            output,
            format="PNG",
            save_all=True,
            append_images=encoded_frames[1:],
            duration=list(RUNTIME_DURATIONS_MS),
            loop=0,
            disposal=[0] * len(encoded_frames),
            blend=[0] * len(encoded_frames),
            optimize=True,
            compress_level=9,
        )
    elif output.suffix.lower() == ".webp":
        encoded_frames[0].save(
            output,
            format="WEBP",
            save_all=True,
            append_images=encoded_frames[1:],
            duration=list(RUNTIME_DURATIONS_MS),
            loop=0,
            lossless=True,
            quality=100,
            method=6,
            minimize_size=True,
        )
    else:
        raise ValueError("animated atlas output must use .png or .webp")

    decoded: list[Image.Image] = []
    decoded_durations: list[int] = []
    with Image.open(output) as opened:
        decoded_format = opened.format
        is_animated = bool(getattr(opened, "is_animated", False))
        frame_count = int(getattr(opened, "n_frames", 1))
        for index in range(frame_count):
            opened.seek(index)
            decoded.append(opened.convert("RGBA"))
            decoded_durations.append(int(opened.info.get("duration", 0)))

    outside_idle = (0, CELL_SIZE[1], ATLAS_SIZE[0], ATLAS_SIZE[1])
    outside_idle_stable = all(
        frames_are_equal(decoded[0].crop(outside_idle), frame.crop(outside_idle))
        for frame in decoded[1:]
    )
    synchronized_idle_cells = True
    for frame in decoded:
        reference = frame.crop((0, 0, CELL_SIZE[0], CELL_SIZE[1]))
        for column in range(1, IDLE_COLUMNS):
            cell = frame.crop(
                (
                    column * CELL_SIZE[0],
                    0,
                    (column + 1) * CELL_SIZE[0],
                    CELL_SIZE[1],
                )
            )
            if not frames_are_equal(reference, cell):
                synchronized_idle_cells = False

    size_bytes = output.stat().st_size
    report = {
        "ok": (
            is_animated
            and frame_count == len(RUNTIME_SEQUENCE)
            and decoded_durations == list(RUNTIME_DURATIONS_MS)
            and outside_idle_stable
            and synchronized_idle_cells
            and size_bytes <= MAX_FILE_BYTES
        ),
        "source": str(source),
        "sourceSha256": sha256(source),
        "output": str(output),
        "outputSha256": sha256(output),
        "format": decoded_format,
        "sizeBytes": size_bytes,
        "maxSizeBytes": MAX_FILE_BYTES,
        "isAnimated": is_animated,
        "frameCount": frame_count,
        "sequence": list(RUNTIME_SEQUENCE),
        "durationsMs": decoded_durations,
        "loopDurationMs": sum(decoded_durations),
        "outsideIdleStable": outside_idle_stable,
        "idleCellsSynchronized": synchronized_idle_cells,
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(report, ensure_ascii=False))
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
