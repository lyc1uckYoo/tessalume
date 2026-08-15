#!/usr/bin/env python3
"""Validate shared Template 1.0 themes and their isolated skin contract."""

from __future__ import annotations

import argparse
import json
import math
import re
import sys
from pathlib import Path


BASE_ASSETS = (
    "hero-light", "hero-dark", "sidebar-light", "sidebar-dark", "chat-light", "chat-dark",
    "task-left", "task-right-secondary", "task-right-primary", "memory-light", "memory-dark",
)
OPTIONAL_DARK_TASK_ASSETS = (
    "task-left-dark", "task-right-secondary-dark", "task-right-primary-dark",
)
REQUIRED_SLOTS = (
    "stageClass", "hero", "identity", "taskLeft", "taskSecondary", "taskPrimary",
    "memory", "syncPanel", "composerAccessory",
)
REQUIRED_INNER_PARTS = (
    "hero-kicker", "hero-title-light", "hero-title-dark", "hero-motion", "hero-note",
    "identity-emblem", "identity-copy", "identity-status", "task-card-art",
    "task-card-caption", "memory-meter", "sync-copy", "sync-core", "sync-meter", "sync-state",
)
SECTION_TITLES = (
    "01 Light and dark tokens", "02 App and native planes", "03 Sidebar skin",
    "04 Home skin", "05 Identity skin", "06 Task-card skin", "07 Memory skin",
    "08 Sync-panel skin", "09 Message and output frames", "10 Composer skin",
    "11 Character components", "12 Theme-only media behavior", "13 Character keyframes",
)
ASSET_VARIABLE_RE = re.compile(r"--tessalume-asset-([A-Za-z0-9][A-Za-z0-9._-]*)")
CSS_RULE_RE = re.compile(r"([^{}]+)\{([^{}]*)\}")
KEYFRAME_RE = re.compile(r"@keyframes\s+([A-Za-z_][\w-]*)")
CLASS_NAME_RE = re.compile(r'(?:className|stageClass)\s*:\s*"([^"]+)"')
GEOMETRY_PROPERTY_RE = re.compile(
    r"(?:^|;)\s*(position|inset|left|right|top|bottom|width|height|min-width|max-width|"
    r"min-height|max-height|display|z-index|box-sizing|overflow-x|flex|"
    r"align-items|justify-content|gap|margin(?:-(?:left|right|top|bottom))?|"
    r"padding(?:-(?:left|right|top|bottom))?|pointer-events|white-space)\s*:",
    re.IGNORECASE,
)
LEGACY_TOKENS = (
    "TESSALUME_TEMPLATE_V1_SURFACE", "TESSALUME_TEMPLATE_V1_GEOMETRY",
    "Moonheart Fox sovereign", "Requiem Stage",
)
DRAFT_TOKENS = (
    "data-theme-draft=", "assets/placeholder.svg", "assets/placeholder.png",
    "在这里填写", "亮色主标题", "暗色主标题", "角色挂件",
)
ARTWORK_REGIONS = ("hero", "sidebar", "chat")
ARTWORK_MODES = ("light", "dark")
CSS_LENGTH_RE = re.compile(
    r"^(?:auto|cover|contain|(?:[1-9][0-9]*(?:\.[0-9]+)?|0\.0*[1-9][0-9]*)(?:%|px))$"
)
CSS_POSITION_RE = re.compile(
    r"^(?:left|center|right|top|bottom|-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:%|px))$"
)
MOTION_DELTA_RE = re.compile(
    r"^-?(?:0(?:\.[0-9]+)?|[1-9][0-9]{0,3}(?:\.[0-9]+)?|10000(?:\.0+)?)(?:%|px)$"
)
HEX_COLOR_RE = re.compile(r"^#[0-9A-Fa-f]{6}$")
SEMVER_RE = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+$")
EFFECT_RANGES = {
    "brightness": (20, 180),
    "contrast": (20, 180),
    "saturation": (0, 200),
    "opacity": (0, 100),
    "grayscale": (0, 100),
    "hueRotate": (-180, 180),
    "blur": (0, 20),
    "vignette": (0, 100),
}
BLEND_MODES = {
    "normal", "multiply", "screen", "overlay", "darken", "lighten",
    "color-dodge", "color-burn", "hard-light", "soft-light", "difference",
    "exclusion", "hue", "saturation", "color", "luminosity", "plus-lighter",
}
MOTION_EASINGS = {"linear", "ease", "ease-in", "ease-out", "ease-in-out"}
MOTION_DIRECTIONS = {"normal", "reverse", "alternate", "alternate-reverse"}


def is_finite_number(value: object, minimum: float, maximum: float) -> bool:
    return (
        isinstance(value, (int, float))
        and not isinstance(value, bool)
        and math.isfinite(value)
        and minimum <= value <= maximum
    )


def validate_gradient_veil(value: object, label: str, errors: list[str]) -> None:
    if not isinstance(value, dict):
        errors.append(f"{label}: gradientVeil must be an object")
        return
    enabled = value.get("enabled")
    strength = value.get("strength")
    layers = value.get("layers")
    if not isinstance(enabled, bool):
        errors.append(f"{label}: gradientVeil.enabled must be Boolean")
    if not is_finite_number(strength, 0, 100):
        errors.append(f"{label}: gradientVeil.strength must be between 0 and 100")
    if not isinstance(layers, list) or len(layers) > 4:
        errors.append(f"{label}: gradientVeil.layers must contain at most four layers")
        return
    if enabled and not layers:
        errors.append(f"{label}: enabled gradientVeil needs at least one layer")
    for layer_index, layer in enumerate(layers):
        layer_label = f"{label}: gradientVeil.layers[{layer_index}]"
        if not isinstance(layer, dict):
            errors.append(f"{layer_label} must be an object")
            continue
        direction = layer.get("directionDeg")
        start = layer.get("start")
        end = layer.get("end")
        stops = layer.get("stops")
        if not is_finite_number(direction, -360, 360):
            errors.append(f"{layer_label}.directionDeg must be between -360 and 360")
        if not is_finite_number(start, 0, 100) or not is_finite_number(end, 0, 100):
            errors.append(f"{layer_label} start/end must be between 0 and 100")
        if not isinstance(stops, list) or not 2 <= len(stops) <= 8:
            errors.append(f"{layer_label}.stops must contain two to eight stops")
            continue
        positions: list[float] = []
        for stop_index, stop in enumerate(stops):
            stop_label = f"{layer_label}.stops[{stop_index}]"
            if not isinstance(stop, dict):
                errors.append(f"{stop_label} must be an object")
                continue
            position = stop.get("position")
            color = stop.get("color")
            opacity = stop.get("opacity")
            if not is_finite_number(position, 0, 100):
                errors.append(f"{stop_label}.position must be between 0 and 100")
            else:
                positions.append(float(position))
            if not isinstance(color, str) or not HEX_COLOR_RE.fullmatch(color):
                errors.append(f"{stop_label}.color must be #RRGGBB")
            if not is_finite_number(opacity, 0, 100):
                errors.append(f"{stop_label}.opacity must be between 0 and 100")
        if positions and positions != sorted(positions):
            errors.append(f"{layer_label}.stops must be ordered by position")
        if positions and (start != positions[0] or end != positions[-1]):
            errors.append(f"{layer_label} start/end must match the first/last stop")


def validate_readability_veil(value: object, label: str, errors: list[str]) -> None:
    readability = value
    if not isinstance(readability, dict):
        errors.append(f"{label}: effects.readabilityVeil must be an object")
    else:
        if not isinstance(readability.get("enabled"), bool):
            errors.append(f"{label}: effects.readabilityVeil.enabled must be Boolean")
        if not isinstance(readability.get("color"), str) or not HEX_COLOR_RE.fullmatch(readability["color"]):
            errors.append(f"{label}: effects.readabilityVeil.color must be #RRGGBB")
        for name, minimum, maximum in (
            ("opacity", 0, 100), ("directionDeg", -360, 360),
            ("rangeStart", 0, 100), ("rangeEnd", 0, 100),
        ):
            if not is_finite_number(readability.get(name), minimum, maximum):
                errors.append(f"{label}: effects.readabilityVeil.{name} is out of range")
        if (
            is_finite_number(readability.get("rangeStart"), 0, 100)
            and is_finite_number(readability.get("rangeEnd"), 0, 100)
            and readability["rangeStart"] > readability["rangeEnd"]
        ):
            errors.append(f"{label}: readabilityVeil rangeStart cannot exceed rangeEnd")


def validate_effects(value: object, label: str, errors: list[str]) -> None:
    if not isinstance(value, dict):
        errors.append(f"{label}: effects must be an object")
        return
    for name, (minimum, maximum) in EFFECT_RANGES.items():
        if not is_finite_number(value.get(name), minimum, maximum):
            errors.append(f"{label}: effects.{name} must be between {minimum} and {maximum}")
    if value.get("blendMode") not in BLEND_MODES:
        errors.append(f"{label}: unsupported effects.blendMode")
    overlay = value.get("overlay")
    if not isinstance(overlay, dict):
        errors.append(f"{label}: effects.overlay must be an object")
    else:
        if not isinstance(overlay.get("color"), str) or not HEX_COLOR_RE.fullmatch(overlay["color"]):
            errors.append(f"{label}: effects.overlay.color must be #RRGGBB")
        if not is_finite_number(overlay.get("opacity"), 0, 100):
            errors.append(f"{label}: effects.overlay.opacity must be between 0 and 100")
    validate_gradient_veil(value.get("gradientVeil"), label, errors)
    validate_readability_veil(value.get("readabilityVeil"), label, errors)


def validate_motion(value: object, label: str, errors: list[str]) -> None:
    if not isinstance(value, dict):
        errors.append(f"{label}: motion must be an object")
        return
    mode = value.get("mode")
    if mode == "none":
        if set(value) != {"mode"}:
            errors.append(f"{label}: motion mode none cannot declare playback fields")
        return
    if mode != "loop":
        errors.append(f"{label}: motion.mode must be none or loop")
        return
    required = {"mode", "durationMs", "easing", "direction", "keyframes"}
    if set(value) != required:
        errors.append(f"{label}: loop motion must define exactly {', '.join(sorted(required))}")
    duration = value.get("durationMs")
    if (
        not isinstance(duration, int)
        or isinstance(duration, bool)
        or not 100 <= duration <= 300000
    ):
        errors.append(f"{label}: motion.durationMs must be an integer from 100 to 300000")
    if value.get("easing") not in MOTION_EASINGS:
        errors.append(f"{label}: unsupported motion.easing")
    if value.get("direction") not in MOTION_DIRECTIONS:
        errors.append(f"{label}: unsupported motion.direction")
    keyframes = value.get("keyframes")
    if not isinstance(keyframes, list) or not 2 <= len(keyframes) <= 16:
        errors.append(f"{label}: motion.keyframes must contain 2 to 16 frames")
        return
    positions: list[float] = []
    frame_fields = {"at", "translateX", "translateY", "scaleDelta", "opacityDelta"}
    for index, frame in enumerate(keyframes):
        frame_label = f"{label}: motion.keyframes[{index}]"
        if not isinstance(frame, dict):
            errors.append(f"{frame_label} must be an object")
            continue
        if set(frame) != frame_fields:
            errors.append(f"{frame_label} must define only relative motion delta fields")
        at = frame.get("at")
        if not is_finite_number(at, 0, 100):
            errors.append(f"{frame_label}.at must be between 0 and 100")
        else:
            positions.append(float(at))
        for axis in ("translateX", "translateY"):
            token = frame.get(axis)
            if not isinstance(token, str) or not MOTION_DELTA_RE.fullmatch(token):
                errors.append(f"{frame_label}.{axis} must be a px or percent delta")
        if not is_finite_number(frame.get("scaleDelta"), -0.9, 1):
            errors.append(f"{frame_label}.scaleDelta must be between -0.9 and 1")
        if not is_finite_number(frame.get("opacityDelta"), -100, 100):
            errors.append(f"{frame_label}.opacityDelta must be between -100 and 100")
    if len(positions) == len(keyframes):
        if positions != sorted(set(positions)):
            errors.append(f"{label}: motion keyframe positions must be strictly increasing")
        if positions[0] != 0 or positions[-1] != 100:
            errors.append(f"{label}: motion keyframes must start at 0 and end at 100")


def validate_artwork_defaults(manifest: dict, defaults: object, label: str) -> list[str]:
    errors: list[str] = []
    if not isinstance(defaults, dict):
        return [f"{label}: artwork defaults must be an object"]
    if defaults.get("schemaVersion") != 1:
        errors.append(f"{label}: artwork defaults schemaVersion must be 1")
    if defaults.get("themeId") != manifest.get("id"):
        errors.append(f"{label}: artwork defaults themeId must match manifest id")
    if not isinstance(defaults.get("defaultsVersion"), str) or not SEMVER_RE.fullmatch(defaults["defaultsVersion"]):
        errors.append(f"{label}: artwork defaultsVersion must be SemVer x.y.z")
    slots = defaults.get("slots")
    if not isinstance(slots, dict) or set(slots) != set(ARTWORK_REGIONS):
        errors.append(f"{label}: artwork defaults must define exactly hero, sidebar and chat")
        return errors
    declared_assets = manifest.get("assets") or {}
    for region in ARTWORK_REGIONS:
        mode_pair = slots.get(region)
        if not isinstance(mode_pair, dict) or set(mode_pair) != set(ARTWORK_MODES):
            errors.append(f"{label}: artwork defaults {region} must define exactly light and dark")
            continue
        for mode in ARTWORK_MODES:
            slot_value = mode_pair.get(mode)
            slot_label = f"{label}: artwork defaults {region}/{mode}"
            if not isinstance(slot_value, dict):
                errors.append(f"{slot_label} must be an object")
                continue
            expected_asset = f"{region}-{mode}"
            if slot_value.get("asset") != expected_asset:
                errors.append(f"{slot_label} must reference original manifest asset {expected_asset}")
            elif expected_asset not in declared_assets:
                errors.append(f"{slot_label} references undeclared asset {expected_asset}")
            placement = slot_value.get("placement")
            if not isinstance(placement, dict):
                errors.append(f"{slot_label}: placement must be an object")
            else:
                size = placement.get("size")
                if not isinstance(size, dict):
                    errors.append(f"{slot_label}: placement.size must be an object")
                else:
                    width, height = size.get("width"), size.get("height")
                    if not isinstance(width, str) or not CSS_LENGTH_RE.fullmatch(width):
                        errors.append(f"{slot_label}: invalid placement.size.width")
                    if not isinstance(height, str) or not CSS_LENGTH_RE.fullmatch(height):
                        errors.append(f"{slot_label}: invalid placement.size.height")
                    if width in {"cover", "contain"} and height != "auto":
                        errors.append(f"{slot_label}: cover/contain requires height auto")
                    if height in {"cover", "contain"}:
                        errors.append(f"{slot_label}: cover/contain belongs in size.width")
                for group_name in ("position", "origin"):
                    group = placement.get(group_name)
                    if not isinstance(group, dict):
                        errors.append(f"{slot_label}: placement.{group_name} must be an object")
                        continue
                    for axis in ("x", "y"):
                        token = group.get(axis)
                        if not isinstance(token, str) or not CSS_POSITION_RE.fullmatch(token):
                            errors.append(f"{slot_label}: invalid placement.{group_name}.{axis}")
                if not is_finite_number(placement.get("scale"), 0.1, 10):
                    errors.append(f"{slot_label}: placement.scale must be between 0.1 and 10")
                for name in ("mirrorX", "mirrorY"):
                    if not isinstance(placement.get(name), bool):
                        errors.append(f"{slot_label}: placement.{name} must be Boolean")
            validate_effects(slot_value.get("effects"), slot_label, errors)
            if "motion" in slot_value:
                validate_motion(slot_value.get("motion"), slot_label, errors)
            variants = slot_value.get("responsiveVariants", [])
            if not isinstance(variants, list) or len(variants) > 8:
                errors.append(f"{slot_label}: responsiveVariants must contain at most eight items")
                continue
            for index, variant in enumerate(variants):
                variant_label = f"{slot_label}: responsiveVariants[{index}]"
                if not isinstance(variant, dict):
                    errors.append(f"{variant_label} must be an object")
                    continue
                minimum = variant.get("minWidth")
                maximum = variant.get("maxWidth")
                if minimum is None and maximum is None:
                    errors.append(f"{variant_label} needs minWidth or maxWidth")
                if minimum is not None and not is_finite_number(minimum, 1, 10000):
                    errors.append(f"{variant_label}.minWidth is out of range")
                if maximum is not None and not is_finite_number(maximum, 1, 10000):
                    errors.append(f"{variant_label}.maxWidth is out of range")
                if minimum is not None and maximum is not None and minimum > maximum:
                    errors.append(f"{variant_label}: minWidth cannot exceed maxWidth")
                if "gradientVeil" not in variant and "readabilityVeil" not in variant:
                    errors.append(f"{variant_label} needs gradientVeil or readabilityVeil")
                if "gradientVeil" in variant:
                    validate_gradient_veil(variant.get("gradientVeil"), variant_label, errors)
                if "readabilityVeil" in variant:
                    validate_readability_veil(
                        variant.get("readabilityVeil"), variant_label, errors
                    )
    return errors


def selector_has_property(
    css: str,
    selector_tokens: tuple[str, ...],
    property_name: str,
) -> bool:
    property_re = re.compile(rf"(?:^|;)\s*{re.escape(property_name)}\s*:", re.IGNORECASE)
    for selector, body in CSS_RULE_RE.findall(css):
        if all(token in selector for token in selector_tokens) and property_re.search(body):
            return True
    return False


def css_entry(theme: Path, manifest: dict) -> Path:
    relative = (manifest.get("entryPoints") or {}).get("css")
    if relative != "skin.css":
        raise ValueError('entryPoints.css must be "skin.css"')
    return theme / relative


def duplicate_selectors(css: str) -> list[str]:
    duplicates: list[str] = []
    contexts: list[str] = []
    rule_depths: list[int] = []
    seen: set[tuple[tuple[str, ...], str]] = set()
    depth = 0
    for raw_line in css.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("/*"):
            continue
        if line.endswith("{"):
            header = line[:-1].strip()
            if header.startswith("@media") or header.startswith("@keyframes"):
                contexts.append(header)
                rule_depths.append(depth)
            elif not re.fullmatch(r"(?:from|to|[\d\s%,.]+)", header):
                key = (tuple(contexts), header)
                if key in seen:
                    duplicates.append(header)
                seen.add(key)
            depth += 1
        depth -= line.count("}")
        while rule_depths and depth <= rule_depths[-1]:
            rule_depths.pop()
            contexts.pop()
    return duplicates


def selector_targets_outer(selector: str, outer_classes: set[str]) -> bool:
    if "[data-theme-role=" in selector or "[data-theme-stage]" in selector:
        return True
    for branch in selector.split(","):
        branch = branch.strip()
        if "::" in branch:
            continue
        for class_name in outer_classes:
            match = re.search(rf"\.{re.escape(class_name)}(?![A-Za-z0-9_-])", branch)
            if match and re.fullmatch(
                r"(?:\[[^\]]+\]|:[A-Za-z-]+(?:\([^)]*\))?)*",
                branch[match.end():],
            ):
                return True
    return False


def validate_theme(
    repo_root: Path,
    theme: Path,
    expected_author: str | None,
    check_portable: bool,
) -> list[str]:
    errors: list[str] = []
    theme = theme.resolve()
    label = theme.name
    try:
        manifest = json.loads((theme / "manifest.json").read_text(encoding="utf-8"))
        script = (theme / "theme.js").read_text(encoding="utf-8")
        skin_path = css_entry(theme, manifest)
        css = skin_path.read_text(encoding="utf-8")
        artwork_relative = (manifest.get("entryPoints") or {}).get("artworkDefaults")
        if artwork_relative != "artwork-defaults.json":
            raise ValueError('entryPoints.artworkDefaults must be "artwork-defaults.json"')
        artwork_defaults = json.loads((theme / artwork_relative).read_text(encoding="utf-8"))
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        return [f"{label}: {exc}"]

    errors.extend(validate_artwork_defaults(manifest, artwork_defaults, label))

    if manifest.get("version") != "1.0":
        errors.append(f'{label}: manifest version must be "1.0"')
    if expected_author and manifest.get("author") != expected_author:
        errors.append(f'{label}: manifest author must be "{expected_author}"')
    if manifest.get("schemaVersion") != 2 or manifest.get("engineVersion") != 2:
        errors.append(f"{label}: schemaVersion and engineVersion must both be 2")
    if manifest.get("type") != "advanced":
        errors.append(f'{label}: manifest type must be "advanced"')
    if manifest.get("template") != {"id": "flagship", "version": "1.0", "style": "shared"}:
        errors.append(f"{label}: shared Template 1.0 declaration is missing or invalid")

    declared_assets = manifest.get("assets") or {}
    missing_keys = set(BASE_ASSETS) - set(declared_assets)
    unknown_keys = set(declared_assets) - set(BASE_ASSETS) - set(OPTIONAL_DARK_TASK_ASSETS)
    if missing_keys:
        errors.append(f"{label}: missing standard assets: {', '.join(sorted(missing_keys))}")
    if unknown_keys:
        errors.append(f"{label}: nonstandard asset keys: {', '.join(sorted(unknown_keys))}")
    for key, relative in declared_assets.items():
        candidate = (theme / relative).resolve()
        try:
            candidate.relative_to(theme)
        except ValueError:
            errors.append(f"{label}: asset escapes package: {relative}")
            continue
        if not candidate.is_file():
            errors.append(f"{label}: missing asset: {relative}")
        if candidate.stem != key:
            errors.append(f"{label}: asset filename must match its slot: {key} -> {relative}")

    for relative in (manifest.get("entryPoints") or {}).values():
        if not (theme / relative).is_file():
            errors.append(f"{label}: missing entry point: {relative}")

    serialized_manifest = json.dumps(manifest, ensure_ascii=False)
    serialized_artwork = json.dumps(artwork_defaults, ensure_ascii=False)
    for token in DRAFT_TOKENS:
        if token in serialized_manifest or token in serialized_artwork or token in script or token in css:
            errors.append(f"{label}: unresolved starter draft remains: {token}")

    if script.count("context.renderTemplateV1(") != 1:
        errors.append(f"{label}: theme.js must call context.renderTemplateV1 exactly once")
    if script.count("context.mountCanonicalTheme(") != 1:
        errors.append(f"{label}: theme.js must call context.mountCanonicalTheme exactly once")
    for slot in REQUIRED_SLOTS:
        if not re.search(rf"\b{re.escape(slot)}\s*:", script):
            errors.append(f"{label}: missing Template 1.0 slot {slot}")
    for part in REQUIRED_INNER_PARTS:
        if f'data-theme-part="{part}"' not in script:
            errors.append(f"{label}: missing Template 1.0 inner part {part}")
    for forbidden in (
        "root.innerHTML", "insertAdjacentHTML", "data-theme-role=", "data-theme-stage",
        "MutationObserver", "context.observe(", "setInterval(", "context.interval(",
    ):
        if forbidden in script:
            errors.append(f"{label}: forbidden theme-owned structure/lifecycle code: {forbidden}")
    for required in (
        'templateVersion: "1.0"', "adaptiveLayout: true", "preserveRoot: true",
        "positionComposerAccessory", "positionPanelAboveCards",
    ):
        if required not in script:
            errors.append(f"{label}: missing canonical runtime declaration: {required}")
    if not re.search(
        r"positionPanelAboveCards\s*\(\s*main\s*,.*?,.*?,\s*320\s*,\s*56\s*,\s*40\s*,?\s*\)",
        script,
        re.DOTALL,
    ):
        errors.append(f"{label}: sync panel must use canonical 320, 56, 40 positioning")

    namespace_match = re.search(r'namespace:\s*"([a-z][a-z0-9]*)"', script)
    if not namespace_match:
        errors.append(f"{label}: canonical namespace is missing")
    else:
        namespace = namespace_match.group(1)
        asset_refs = set(ASSET_VARIABLE_RE.findall(css))
        for region in ARTWORK_REGIONS:
            for mode in ARTWORK_MODES:
                slot_value = ((artwork_defaults.get("slots") or {}).get(region) or {}).get(mode) or {}
                asset = slot_value.get("asset")
                if isinstance(asset, str):
                    asset_refs.add(asset)
        undeclared = asset_refs - set(declared_assets)
        unused = set(declared_assets) - asset_refs
        if undeclared:
            errors.append(f"{label}: undeclared asset variables: {', '.join(sorted(undeclared))}")
        if unused:
            errors.append(f"{label}: declared assets are unused: {', '.join(sorted(unused))}")
        quality_gate = (manifest.get("config") or {}).get("qualityGate")
        if quality_gate == "flagship-complete-1":
            visual_coverage = {
                "explicit light/dark visibility": (
                    f".{namespace}-dark-only", f".{namespace}-light-only", "display:none",
                ),
                "layered sidebar artwork": ("aside.app-shell-left-panel::after",),
                "task-title row": (f".{namespace}-task-title",),
                "environment panel sections": (f".{namespace}-output-panel>div>section",),
                "environment panel item buttons": ('data-slot="thread-summary-panel-item-button"',),
                "composer footer controls": ("_footer_",),
                "composer model picker": ("_ModelPickerTrigger",),
            }
            compact_css = re.sub(r"\s+", "", css)
            for surface, fragments in visual_coverage.items():
                if not all(fragment.replace(" ", "") in compact_css for fragment in fragments):
                    errors.append(f"{label}: flagship visual coverage missing: {surface}")
            if not selector_has_property(
                css,
                (f".{namespace}-message-assistant", f".{namespace}-markdown"),
                "padding",
            ):
                errors.append(f"{label}: assistant message frame needs deliberate inner padding")
            if not selector_has_property(
                css,
                (f".{namespace}-message-user", "data-user-message-bubble"),
                "padding",
            ):
                errors.append(f"{label}: user message frame needs deliberate inner padding")
        elif quality_gate is not None:
            errors.append(f"{label}: unknown flagship quality gate: {quality_gate}")
        for selector, body in CSS_RULE_RE.findall(css):
            normalized_selector = selector.strip()
            properties = {
                match.group(1).lower()
                for match in re.finditer(r"(?:^|;)\s*([\w-]+)\s*:", body)
            }
            artwork_layer = None
            if "aside.app-shell-left-panel::after" in normalized_selector:
                artwork_layer = "sidebar"
            elif (f".{namespace}-home" in normalized_selector and
                  "div:first-child>div:first-child>div:first-child::before" in normalized_selector):
                artwork_layer = "hero"
            elif (f"-is-task main.{namespace}-main::before" in normalized_selector):
                artwork_layer = "chat"
            if artwork_layer:
                hard_coded_visual = properties & {
                    "background", "background-image", "background-size", "background-position",
                    "background-position-x", "background-position-y", "background-blend-mode",
                    "filter", "opacity", "transform", "transform-origin", "translate", "scale",
                    "animation", "animation-name",
                }
                if hard_coded_visual:
                    errors.append(
                        f"{label}: {artwork_layer} final artwork values belong to artwork-defaults.json, "
                        f"not {normalized_selector}: {', '.join(sorted(hard_coded_visual))}"
                    )
            if f"-is-task main.{namespace}-main::after" in normalized_selector:
                hidden_veil = properties & {"background", "background-image"}
                if hidden_veil:
                    errors.append(
                        f"{label}: chat readability veil belongs to artwork-defaults.json, "
                        f"not {normalized_selector}: {', '.join(sorted(hidden_veil))}"
                    )
            artwork_owners = {
                f"var(--{namespace}-hero)": f".{namespace}-home",
                f"var(--{namespace}-sidebar-art)": "aside.app-shell-left-panel::after",
                f"var(--{namespace}-chat-art)": f"main.{namespace}-main::before",
            }
            for artwork_token, owner_selector in artwork_owners.items():
                if artwork_token in body and owner_selector not in normalized_selector:
                    errors.append(
                        f"{label}: {artwork_token} must be painted only by {owner_selector}"
                    )
            if f"main.{namespace}-main::before" in normalized_selector or f"main.{namespace}-main::after" in normalized_selector:
                shared = properties & {"content", "position", "inset", "z-index", "pointer-events", "isolation"}
                if shared:
                    errors.append(
                        f"{label}: skin owns shared task-canvas structure on {normalized_selector}: "
                        f"{', '.join(sorted(shared))}"
                    )
            owns_transparent_fill = (
                f".{namespace}-chat-paper" in normalized_selector or
                (
                    f".{namespace}-message-assistant" in normalized_selector and
                    f".{namespace}-markdown" in normalized_selector
                ) or
                (
                    f".{namespace}-message-user" in normalized_selector and
                    (f".{namespace}-markdown" in normalized_selector or "data-user-message-bubble" in normalized_selector)
                )
            )
            shared_fill = properties & {
                "background", "background-color", "background-image", "backdrop-filter",
                "-webkit-backdrop-filter",
            }
            if owns_transparent_fill and shared_fill:
                errors.append(
                    f"{label}: skin owns shared transparent fill on {normalized_selector}: "
                    f"{', '.join(sorted(shared_fill))}"
                )

    for token in LEGACY_TOKENS:
        if token in css or token in script:
            errors.append(f"{label}: legacy token remains: {token}")
    if "[data-theme-role=" in css or "[data-theme-stage]" in css:
        errors.append(f"{label}: skin.css contains runtime-owned semantic geometry")
    if css.count("{") != css.count("}"):
        errors.append(f"{label}: CSS braces are unbalanced")

    section_positions = [css.find(f"/* {title} */") for title in SECTION_TITLES]
    if any(position < 0 for position in section_positions):
        missing = [title for title, position in zip(SECTION_TITLES, section_positions) if position < 0]
        errors.append(f"{label}: missing canonical skin sections: {', '.join(missing)}")
    elif section_positions != sorted(section_positions):
        errors.append(f"{label}: canonical skin sections are out of order")

    outer_classes: set[str] = set()
    for value in CLASS_NAME_RE.findall(script):
        outer_classes.update(value.split())
    for selector, body in CSS_RULE_RE.findall(css):
        if selector_targets_outer(selector.strip(), outer_classes):
            match = GEOMETRY_PROPERTY_RE.search(body)
            if match:
                errors.append(f"{label}: skin owns {match.group(1)} on shared outer slot: {selector.strip()}")

    portable = repo_root / "dist" / "portable-win-x64" / "themes" / theme.name
    if check_portable and portable.is_dir():
        for name in (
            manifest["entryPoints"]["script"],
            manifest["entryPoints"]["css"],
            manifest["entryPoints"]["artworkDefaults"],
        ):
            source = theme / name
            target = portable / name
            if not target.is_file():
                errors.append(f"{label}: portable {name} is not synchronized")
            elif source.read_text(encoding="utf-8").replace("\r\n", "\n") != target.read_text(encoding="utf-8").replace("\r\n", "\n"):
                errors.append(f"{label}: portable {name} is not synchronized")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("themes", nargs="+", type=Path)
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--author")
    parser.add_argument("--check-portable", action="store_true")
    args = parser.parse_args()
    errors: list[str] = []
    for theme in args.themes:
        errors.extend(validate_theme(
            args.repo_root.resolve(),
            theme,
            args.author,
            args.check_portable,
        ))
    if errors:
        print("\n".join(f"ERROR {error}" for error in errors), file=sys.stderr)
        return 1
    print(f"PASS canonical contract: {len(args.themes)} theme(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
