"""Package an already validated v2 atlas and its real animation previews."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

from PIL import Image, ImageDraw

STATES = [
    ("idle", "待机", 6, [280, 110, 110, 140, 140, 320]),
    ("move-right", "向右御剑", 8, [120] * 7 + [220]),
    ("move-left", "向左御剑", 8, [120] * 7 + [220]),
    ("wave-touch", "招手", 4, [140] * 3 + [280]),
    ("jump", "轻跃", 5, [140] * 4 + [280]),
    ("blocked", "受挫", 8, [140] * 7 + [240]),
    ("needs-input", "等待回应", 6, [150] * 5 + [260]),
    ("running", "凝神运剑", 6, [120] * 5 + [220]),
    ("ready", "凝目查看", 6, [150] * 5 + [280]),
]


def gif(frames, durations, target):
    frames[0].save(target, save_all=True, append_images=frames[1:], duration=durations, loop=0, disposal=2, optimize=False)
    with Image.open(target) as result:
        return result.n_frames


def styled(frame, scale=3):
    bg = Image.new("RGBA", (192, 208), "#f6f8fd")
    bg.alpha_composite(frame)
    return bg.convert("RGB").resize((192 * scale, 208 * scale), Image.Resampling.LANCZOS)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--atlas", type=Path, required=True)
    parser.add_argument("--validation", type=Path, required=True)
    parser.add_argument("--despill", type=Path, required=True)
    parser.add_argument("--visual-qa", type=Path, required=True)
    parser.add_argument("--blind-validation", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    assert json.loads(args.validation.read_text(encoding="utf-8"))["ok"]
    assert json.loads(args.despill.read_text(encoding="utf-8"))["ok"]
    visual_qa = json.loads(args.visual_qa.read_text(encoding="utf-8"))
    assert visual_qa["visual_qa"] == "pass"
    expected_directions = {"000", "022.5", "045", "067.5", "090", "112.5", "135", "157.5", "180", "202.5", "225", "247.5", "270", "292.5", "315", "337.5"}
    directions = visual_qa["directions"]
    assert len(directions) == 16 and {item["direction"] for item in directions} == expected_directions
    assert all(item["verdict"] in {"pass", "warning"} and all(item.get(key) for key in ("expected", "observed", "reason")) for item in directions)
    assert json.loads(args.blind_validation.read_text(encoding="utf-8"))["ok"]
    project = json.loads(args.project.read_text(encoding="utf-8"))
    atlas = Image.open(args.atlas).convert("RGBA")
    assert atlas.size == (1536, 2288)
    out = args.output
    (out / "previews").mkdir(parents=True, exist_ok=True)
    shutil.copy2(args.atlas, out / "spritesheet.webp")
    pet = {"id": project["id"], "displayName": project["displayName"], "description": "云影随身、以弦御剑的清冷小剑仙。", "spriteVersionNumber": 2, "spritesheetPath": "spritesheet.webp"}
    (out / "pet.json").write_text(json.dumps(pet, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    previews = []
    action_frames = []
    for row, (key, label, count, durations) in enumerate(STATES):
        cells = [atlas.crop((col * 192, row * 208, (col + 1) * 192, (row + 1) * 208)) for col in range(count)]
        action_frames.append(cells)
        path = f"previews/{row:02d}-{key}.gif"
        actual = gif([styled(im) for im in cells], durations, out / path)
        assert actual == count, f"{key}: GIF unexpectedly merged frames"
        previews.append({"path": path, "kind": "action", "mediaType": "image/gif", "actionKey": key, "stateKey": key, "label": label, "expectedFrameCount": actual, "width": 576, "height": 624, "representativeFrame": 0, "loop": True})
    look_cells = [atlas.crop((col * 192, row * 208, (col + 1) * 192, (row + 1) * 208)) for row in (9, 10) for col in range(8)]
    path = "previews/10-gaze-clockwise.gif"
    actual = gif([styled(im) for im in look_cells], [160] * 16, out / path)
    previews.append({"path": path, "kind": "direction", "mediaType": "image/gif", "actionKey": "gaze-clockwise", "stateKey": "gaze-clockwise", "label": "十六向注视", "expectedFrameCount": actual, "width": 576, "height": 624, "representativeFrame": 0, "loop": True})
    showcase = []
    for index in range(24):
        canvas = Image.new("RGB", (576, 624), "#f6f8fd")
        for row, cells in enumerate(action_frames):
            canvas.paste(styled(cells[index % len(cells)], 1), ((row % 3) * 192, (row // 3) * 208))
        showcase.append(canvas)
    path = "previews/showcase.gif"
    actual = gif(showcase, [150] * 24, out / path)
    previews.append({"path": path, "kind": "showcase", "mediaType": "image/gif", "actionKey": "showcase", "stateKey": "showcase", "label": "动作总览", "expectedFrameCount": actual, "width": 576, "height": 624, "representativeFrame": 0, "loop": True})
    catalog = {key: project[key] for key in ("id", "displayName", "protocol", "author", "license", "rights", "recommendedThemeIds")}
    catalog.update({"schemaVersion": 2, "description": "云影随身、以弦御剑的清冷小剑仙。九种动作与十六向注视，完整待验收版。", "productVersion": "1.0.0", "files": [], "previews": previews})
    for path, role in [("pet.json", "codex-manifest"), ("spritesheet.webp", "codex-spritesheet")] + [(p["path"], "preview") for p in previews]:
        data = (out / path).read_bytes()
        catalog["files"].append({"path": path, "sha256": hashlib.sha256(data).hexdigest(), "size": len(data), "role": role})
    (out / "catalog.json").write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(out), "previews": len(previews), "files": len(catalog["files"])}))


if __name__ == "__main__":
    main()
