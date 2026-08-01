from io import BytesIO
from struct import pack
from pathlib import Path
from math import ceil
from xml.etree import ElementTree

from PIL import Image, ImageChops, ImageDraw, ImageEnhance


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "Tessalume.App" / "Assets"
PROJECT = ROOT / "src" / "Tessalume.App" / "Tessalume.App.csproj"
ICON_STEM = ElementTree.parse(PROJECT).findtext("./PropertyGroup/AssemblyName") or PROJECT.stem
SOURCE = ASSETS / f"{ICON_STEM}.png"
ICON = ASSETS / f"{ICON_STEM}.ico"
PNG_SIZE = 1024
ICO_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
SAFE_PADDING_RATIO = 0.04
SMALL_ICON_MAX_SIZE = 48
SMALL_ICON_CROP = (0.13, 0.16, 0.87, 0.88)
SMALL_ICON_DARK_FLOOR = 42
SMALL_ICON_ALPHA_GAIN = 3.2
SMALL_ICON_BRIGHTNESS = 1.18


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


def make_small_icon_master(image: Image.Image) -> Image.Image:
    red, green, blue, original_alpha = image.split()
    brightness = ImageChops.lighter(red, ImageChops.lighter(green, blue))
    visible_alpha = brightness.point(
        lambda value: max(
            0,
            min(255, round((value - SMALL_ICON_DARK_FLOOR) * SMALL_ICON_ALPHA_GAIN)),
        )
    )

    width, height = image.size
    allowed = Image.new("L", image.size)
    draw = ImageDraw.Draw(allowed)
    left, top, right, bottom = SMALL_ICON_CROP
    draw.rectangle(
        (
            round(width * left),
            round(height * top),
            round(width * right),
            round(height * bottom),
        ),
        fill=255,
    )
    visible_alpha = ImageChops.darker(
        original_alpha,
        ImageChops.darker(visible_alpha, allowed),
    )

    brightened = ImageEnhance.Brightness(image).enhance(SMALL_ICON_BRIGHTNESS)
    brightened.putalpha(visible_alpha)
    bounds = visible_alpha.getbbox()
    if bounds is None:
        raise ValueError(f"Could not derive a small icon from: {SOURCE}")

    cropped = brightened.crop(bounds)
    content_size = max(cropped.size)
    padding = ceil(content_size * SAFE_PADDING_RATIO)
    canvas = Image.new(
        "RGBA",
        (content_size + padding * 2, content_size + padding * 2),
    )
    canvas.alpha_composite(
        cropped,
        (
            (canvas.width - cropped.width) // 2,
            (canvas.height - cropped.height) // 2,
        ),
    )
    return canvas.resize((PNG_SIZE, PNG_SIZE), Image.Resampling.LANCZOS)


def write_multiframe_ico(
    path: Path,
    full_master: Image.Image,
    small_master: Image.Image,
) -> None:
    encoded_frames: list[tuple[int, int, bytes]] = []
    for width, height in ICO_SIZES:
        master = small_master if width <= SMALL_ICON_MAX_SIZE else full_master
        frame = master.resize((width, height), Image.Resampling.LANCZOS)
        output = BytesIO()
        frame.save(output, format="PNG", optimize=True)
        encoded_frames.append((width, height, output.getvalue()))

    directory_size = 6 + len(encoded_frames) * 16
    offset = directory_size
    entries: list[bytes] = []
    for width, height, content in encoded_frames:
        entries.append(
            pack(
                "<BBBBHHII",
                0 if width == 256 else width,
                0 if height == 256 else height,
                0,
                0,
                1,
                32,
                len(content),
                offset,
            )
        )
        offset += len(content)

    with path.open("wb") as stream:
        stream.write(pack("<HHH", 0, 1, len(encoded_frames)))
        stream.writelines(entries)
        for _, _, content in encoded_frames:
            stream.write(content)


def main() -> None:
    if not SOURCE.exists():
        raise FileNotFoundError(f"Missing icon master: {SOURCE}")

    icon = normalize_master(Image.open(SOURCE))
    small_icon = make_small_icon_master(icon)
    icon.save(SOURCE, optimize=True)
    write_multiframe_ico(ICON, icon, small_icon)
    print(f"Wrote {SOURCE}")
    print(f"Wrote {ICON}")


if __name__ == "__main__":
    main()
