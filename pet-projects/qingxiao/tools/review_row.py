"""Produce inspection media from already extracted Qingxiao frames."""
import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw

DURATIONS = {
    "idle": [280, 110, 110, 140, 140, 320],
    "running-right": [120] * 7 + [220],
    "running-left": [120] * 7 + [220],
    "waving": [140] * 3 + [280],
    "jumping": [140] * 4 + [280],
    "failed": [140] * 7 + [240],
    "waiting": [150] * 5 + [260],
    "running": [120] * 5 + [220],
    "review": [150] * 5 + [280],
}

parser = argparse.ArgumentParser()
parser.add_argument("--frames-root", type=Path, required=True)
parser.add_argument("--state", choices=DURATIONS, required=True)
parser.add_argument("--output-dir", type=Path, required=True)
args = parser.parse_args()
files = sorted((args.frames_root / args.state).glob("*.png"))
frames = [Image.open(p).convert("RGBA") for p in files]
assert len(frames) == len(DURATIONS[args.state]), "Unexpected frame count"
assert all(im.size == (192, 208) for im in frames)
args.output_dir.mkdir(parents=True, exist_ok=True)
sheet = Image.new("RGB", (192 * len(frames), 452), "#fafafa")
draw = ImageDraw.Draw(sheet)
draw.rectangle((0, 226, sheet.width, 451), fill="#192130")
for row, y in enumerate((18, 244)):
    for i, frame in enumerate(frames):
        sheet.paste(frame, (192 * i, y), frame)
        draw.text((192 * i + 5, y - 15), f"{args.state} {i + 1}", fill="#161b23" if row == 0 else "#ffffff")
sheet.save(args.output_dir / "contact-sheet.png")
preview_frames = []
for frame in frames:
    background = Image.new("RGBA", (192, 208), "#f8f9fd")
    background.alpha_composite(frame)
    preview_frames.append(background.convert("RGB").resize((576, 624), Image.Resampling.LANCZOS))
preview_path = args.output_dir / f"{args.state}.gif"
preview_frames[0].save(preview_path, save_all=True, append_images=preview_frames[1:], duration=DURATIONS[args.state], loop=0, disposal=2, optimize=False)
stats = {"state": args.state, "frames": len(frames), "source": str(args.frames_root), "bounds": [im.getbbox() for im in frames], "contact_sheet": str(args.output_dir / "contact-sheet.png"), "preview": str(preview_path)}
(args.output_dir / "media.json").write_text(json.dumps(stats, indent=2), encoding="utf-8")
print(json.dumps(stats))
