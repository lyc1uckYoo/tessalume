from pathlib import Path
from math import ceil
from xml.etree import ElementTree

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "Tessalume.App" / "Assets"
PROJECT = ROOT / "src" / "Tessalume.App" / "Tessalume.App.csproj"
ICON_STEM = ElementTree.parse(PROJECT).findtext("./PropertyGroup/AssemblyName") or PROJECT.stem
SOURCE = ASSETS / f"{ICON_STEM}.png"
ICON = ASSETS / f"{ICON_STEM}.ico"
PNG_SIZE = 1024
ICO_SIZES = [
    (16, 16),
    (20, 20),
    (24, 24),
    (32, 32),
    (40, 40),
    (48, 48),
    (64, 64),
    (128, 128),
    (256, 256),
]
SAFE_PADDING_RATIO = 0.04


def normalize_master(image: Image.Image) -> Image.Image:
    image = image.convert("RGBA")
    red, green, blue, alpha = image.split()
    alpha = alpha.point(lambda value: 0 if value < 24 else value)
    image = Image.merge("RGBA", (red, green, blue, alpha))

    bounds = alpha.getbbox()
    if bounds is None:
        raise ValueError(f"Icon master has no visible pixels: {SOURCE}")

    left, top, right, bottom = bounds
    content_size = max(right - left, bottom - top)
    padding = ceil(content_size * SAFE_PADDING_RATIO)
    crop_size = content_size + padding * 2
    center_x = (left + right) / 2
    center_y = (top + bottom) / 2
    crop_left = round(center_x - crop_size / 2)
    crop_top = round(center_y - crop_size / 2)
    crop_box = (crop_left, crop_top, crop_left + crop_size, crop_top + crop_size)

    return image.crop(crop_box).resize((PNG_SIZE, PNG_SIZE), Image.Resampling.LANCZOS)


def main() -> None:
    if not SOURCE.exists():
        raise FileNotFoundError(f"Missing icon master: {SOURCE}")

    icon = normalize_master(Image.open(SOURCE))
    icon.save(ICON, format="ICO", sizes=ICO_SIZES)
    print(f"Read {SOURCE}")
    print(f"Wrote {ICON}")


if __name__ == "__main__":
    main()
