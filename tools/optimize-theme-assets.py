from __future__ import annotations

import argparse
import json
import shutil
from pathlib import Path

from PIL import Image


CACHE_VERSION = 1
TEXT_EXTENSIONS = {".css", ".js", ".json", ".md"}
CACHE_FILE_NAME = ".optimizer-cache.json"


def is_private_working_path(path: Path, root: Path) -> bool:
    return any(part.startswith(".") for part in path.relative_to(root).parts)


def replace_png_references(value: object) -> object:
    if isinstance(value, str):
        return value.replace(".png", ".webp")
    if isinstance(value, list):
        return [replace_png_references(item) for item in value]
    if isinstance(value, dict):
        return {key: replace_png_references(item) for key, item in value.items()}
    return value


def write_if_changed(path: Path, content: bytes) -> bool:
    if path.is_file() and path.read_bytes() == content:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f"{path.name}.tmp")
    temporary.write_bytes(content)
    temporary.replace(path)
    return True


def copy_if_changed(source: Path, destination: Path) -> bool:
    if destination.is_file():
        source_stat = source.stat()
        destination_stat = destination.stat()
        if (
            source_stat.st_size == destination_stat.st_size
            and source_stat.st_mtime_ns == destination_stat.st_mtime_ns
        ):
            return False
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    return True


def source_signature(path: Path) -> dict[str, int]:
    stat = path.stat()
    return {"size": stat.st_size, "mtime_ns": stat.st_mtime_ns}


def load_cache(path: Path, quality: int) -> dict[str, object]:
    if not path.is_file():
        return {}
    try:
        cache = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}
    if cache.get("version") != CACHE_VERSION or cache.get("quality") != quality:
        return {}
    return cache


def convert_png(source: Path, destination: Path, quality: int) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(f"{destination.name}.tmp")
    with Image.open(source) as image:
        save_options: dict[str, object] = {
            "format": "WEBP",
            "quality": quality,
            "method": 6,
            "exact": True,
        }
        if "icc_profile" in image.info:
            save_options["icc_profile"] = image.info["icc_profile"]
        if "exif" in image.info:
            save_options["exif"] = image.info["exif"]
        image.save(temporary, **save_options)
    temporary.replace(destination)


def create_card_preview(source: Path, destination: Path, dark: bool) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(f"{destination.name}.tmp")
    with Image.open(source) as image:
        image.thumbnail((720, 480), Image.Resampling.LANCZOS)
        background_color = (24, 19, 31) if dark else (244, 240, 244)
        if "A" in image.getbands():
            background = Image.new("RGB", image.size, background_color)
            background.paste(image, mask=image.getchannel("A"))
            image = background
        else:
            image = image.convert("RGB")
        image.save(
            temporary,
            "JPEG",
            quality=86,
            optimize=True,
            progressive=True,
        )
    temporary.replace(destination)


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Incrementally update the persistent optimized theme library."
    )
    parser.add_argument("themes", type=Path, help="Source PNG theme library. It is never modified.")
    parser.add_argument("--output", type=Path, required=True, help="Persistent optimized theme library.")
    parser.add_argument("--quality", type=int, default=90)
    args = parser.parse_args()

    source_root = args.themes.resolve()
    output_root = args.output.resolve()
    if output_root == source_root or output_root.is_relative_to(source_root):
        raise ValueError("The optimized output must be outside the source theme library.")
    output_root.mkdir(parents=True, exist_ok=True)

    cache_path = output_root / CACHE_FILE_NAME
    old_cache = load_cache(cache_path, args.quality)
    old_png = old_cache.get("png", {}) if isinstance(old_cache.get("png"), dict) else {}
    old_previews = (
        old_cache.get("previews", {}) if isinstance(old_cache.get("previews"), dict) else {}
    )

    manifests: dict[Path, dict[str, object]] = {}
    preview_jobs: dict[str, tuple[Path, bool]] = {}
    for manifest_path in sorted(source_root.rglob("manifest.json")):
        if is_private_working_path(manifest_path, source_root):
            continue
        document = json.loads(manifest_path.read_text(encoding="utf-8"))
        manifests[manifest_path] = document
        previews = document.get("previews")
        if not isinstance(previews, dict):
            continue
        for mode in ("light", "dark"):
            relative = previews.get(mode)
            if not isinstance(relative, str) or not relative.lower().endswith(".png"):
                continue
            source = (manifest_path.parent / relative).resolve()
            if not source.is_file():
                raise FileNotFoundError(f"Missing preview source: {source}")
            card = source.with_name(f"{source.stem}-card.jpg")
            card_relative = card.relative_to(source_root).as_posix()
            preview_jobs.setdefault(card_relative, (source, mode == "dark"))

    expected_files = {CACHE_FILE_NAME}
    new_png: dict[str, dict[str, int]] = {}
    converted_count = 0
    reused_count = 0
    original_total = 0
    optimized_total = 0

    for source in sorted(source_root.rglob("*.png")):
        if is_private_working_path(source, source_root):
            continue
        relative = source.relative_to(source_root)
        key = relative.as_posix()
        destination_relative = relative.with_suffix(".webp")
        destination = output_root / destination_relative
        expected_files.add(destination_relative.as_posix())
        signature = source_signature(source)
        new_png[key] = signature
        original_total += signature["size"]

        if old_png.get(key) == signature and destination.is_file():
            reused_count += 1
        else:
            convert_png(source, destination, args.quality)
            converted_count += 1
        optimized_total += destination.stat().st_size

    new_previews: dict[str, dict[str, object]] = {}
    preview_generated = 0
    preview_reused = 0
    for card_relative, (source, dark) in sorted(preview_jobs.items()):
        destination = output_root / card_relative
        expected_files.add(card_relative)
        signature: dict[str, object] = {
            **source_signature(source),
            "dark": dark,
            "width": 720,
            "height": 480,
            "quality": 86,
        }
        new_previews[card_relative] = signature
        if old_previews.get(card_relative) == signature and destination.is_file():
            preview_reused += 1
        else:
            create_card_preview(source, destination, dark)
            preview_generated += 1

    copied_count = 0
    rewritten_count = 0
    for source in sorted(source_root.rglob("*")):
        if (
            not source.is_file()
            or is_private_working_path(source, source_root)
            or source.suffix.lower() == ".png"
        ):
            continue
        relative = source.relative_to(source_root)
        destination = output_root / relative
        expected_files.add(relative.as_posix())
        if source.name == "manifest.json":
            original_document = manifests[source]
            document = replace_png_references(original_document)
            if not isinstance(document, dict):
                raise TypeError(f"Theme manifest root must be an object: {source}")
            previews = document.get("previews")
            original_previews = original_document.get("previews")
            if isinstance(previews, dict) and isinstance(original_previews, dict):
                for mode in ("light", "dark"):
                    configured = original_previews.get(mode)
                    if not isinstance(configured, str) or not configured.lower().endswith(".png"):
                        continue
                    preview_source = (source.parent / configured).resolve()
                    card = preview_source.with_name(f"{preview_source.stem}-card.jpg")
                    previews[mode] = card.relative_to(source.parent).as_posix()
            content = (json.dumps(document, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
            if write_if_changed(destination, content):
                rewritten_count += 1
        elif source.suffix.lower() in TEXT_EXTENSIONS:
            content = source.read_text(encoding="utf-8").replace(".png", ".webp")
            if write_if_changed(destination, content.encode("utf-8")):
                rewritten_count += 1
        elif copy_if_changed(source, destination):
            copied_count += 1

    removed_count = 0
    for output in sorted(output_root.rglob("*")):
        if not output.is_file():
            continue
        relative = output.relative_to(output_root).as_posix()
        if relative not in expected_files:
            output.unlink()
            removed_count += 1
    for directory in sorted(
        (path for path in output_root.rglob("*") if path.is_dir()),
        key=lambda path: len(path.parts),
        reverse=True,
    ):
        try:
            directory.rmdir()
        except OSError:
            pass

    cache = {
        "version": CACHE_VERSION,
        "quality": args.quality,
        "png": new_png,
        "previews": new_previews,
    }
    write_if_changed(
        cache_path,
        (json.dumps(cache, ensure_ascii=False, indent=2) + "\n").encode("utf-8"),
    )

    reduction = 0 if original_total == 0 else (1 - optimized_total / original_total) * 100
    print(
        "Incremental theme cache updated: "
        f"{converted_count} converted, {reused_count} reused, "
        f"{preview_generated} card previews generated, {preview_reused} reused, "
        f"{copied_count + rewritten_count} small files updated, {removed_count} stale files removed."
    )
    print(
        f"Theme images: {original_total / 1048576:.2f} MB source -> "
        f"{optimized_total / 1048576:.2f} MB WebP ({reduction:.1f}% smaller)."
    )


if __name__ == "__main__":
    main()
