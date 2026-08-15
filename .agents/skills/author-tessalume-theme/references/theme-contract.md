# Tessalume canonical theme contract

## Ownership boundary

The Studio runtime owns:

- route detection and the `*-is-home` / `*-is-task` state;
- the persistent main, sidebar, home, window-bar, chat, message, output, and task-header bindings;
- mutation observation, resize handling, debounce timing, periodic repair, data cleanup, and unmount cleanup;
- stage geometry, composer-accessory placement, and optional panel-above-cards placement.
- resolution of original assets, versioned theme recommendations and sparse
  per-user light/dark artwork overrides for the home hero, sidebar artwork and
  stable chat background;
- the single final background size/position, effects and veil applied to those
  three artwork layers;
- optional image-layer motion composed only from relative translation, scale
  and opacity deltas after the final static placement is resolved.

The theme package owns:

- manifest metadata and declared local assets;
- light/dark color variables and artwork selection;
- the original hero/sidebar/chat assets and a versioned `artwork-defaults.json`
  recommendation for all three regions in light and dark mode;
- markup inside `#tessalume-theme-root`;
- character-specific home effects, sidebar art, cards, message frames, memory
  display, sync instrument, composer accessory, internal SVG, copy, symbols,
  and CSS-only animation;
- only the small `onEnsure` callback needed to position or reparent theme-owned elements.

Do not move runtime-owned work back into a theme.
Do not move character-owned visual identity into a generic template skin.

The three user-adjustable artwork layers must not declare their image,
background size/position, static transform, animation, filter, opacity, blend
mode or readability veil in theme CSS. `artwork-defaults.json` references the
six original manifest assets and owns the complete recommended placement and
effects plus any optional relative motion. Tessalume resolves that recommendation
with sparse user overrides and paints one final result. Decorative borders,
symbols and character animation belong on separate identity layers so user
correction never changes text, controls or card surfaces.

## Artwork motion invariant

Image motion is slot-level optional data. Use `motion.mode: "none"` when the
recommended image is static. A looping recommendation declares duration,
easing, direction and ordered keyframes from `at: 0` through `at: 100`.
Keyframes may contain only `translateX`/`translateY` px or percentage deltas,
unitless `scaleDelta` and percentage-point `opacityDelta`. `scaleDelta` is a
relative multiplier increment: the motion layer uses `1 + scaleDelta` after
the final static placement has been resolved. It never changes persisted crop,
size, position or the user's custom composition.

Do not encode absolute background size/position in motion, and do not restore
image keyframes to `skin.css`. The shared runtime owns full/reduced/off motion;
operating-system reduced-motion disables image motion. Theme-owned DOM/SVG
decoration may still use character-specific CSS keyframes on its own layers.

Flagship Template 1.0 additionally freezes shared DOM parts, sizes, positions,
alignment and adaptive priorities. Read
[template-v1.md](template-v1.md) before creating a flagship theme.

## Required lifecycle shape

Each `theme.js` must:

1. Call `registerTheme({ mount, unmount })`.
2. Render one stage with `data-theme-stage`.
3. Add the semantic roles below.
4. Call `context.mountCanonicalTheme(...)` exactly once with:
   - `namespace`;
   - `themeClass`;
   - `templateVersion: "1.0"` for flagship themes;
   - `preserveRoot: true` when markup is rendered before the call;
   - `adaptiveLayout: true` for Template 1.0;
   - optional `sidebar` data;
   - optional `onEnsure` for theme-owned geometry.
5. Keep `unmount` empty. The canonical host performs cleanup.

Forbidden in theme packages:

- `MutationObserver`, `context.observe`, route polling, custom route classes, or lifecycle timers;
- direct Codex DOM cleanup loops;
- background artwork on `.*-chat-paper::before`;
- remote assets, network fetching, or undeclared files;
- hard-coded local paths, usernames, repository paths, build output paths, or ports.

## New-theme production invariant

Every publishable package starts from `assets/theme-template/` and replaces its
entire draft visual layer. The starter's circles, scan line, sample copy,
placeholder assets and generic keyframes demonstrate DOM ownership only; they
are not an acceptable first version of a character theme.

Before coding, complete the character research gate and eleven-slot art matrix
in [flagship-completeness.md](flagship-completeness.md). Before building,
complete every native and theme-owned visual surface in that checklist. Treat
missing deep selectors, generic starter components and unresolved draft markers
as contract failures, not later polish.

## Required semantic roles

Use these attributes even when class names and artwork differ:

| Role | Cardinality | Purpose |
|---|---:|---|
| `data-theme-stage` | 1 | Persistent overlay stage |
| `data-theme-role="hero"` | 1 | Home hero copy/effects host |
| `data-theme-role="identity"` | 1 | Top-center theme identity |
| `data-theme-role="task-left"` | 1 | Left task portrait |
| `data-theme-role="task-right"` | 1+ | Right task portrait(s) |
| `data-theme-role="memory"` | 1 | Left memory card |
| `data-theme-role="composer-accessory"` | 1 | Composer-side themed object |
| `data-theme-role="sync-panel"` | 0–1 | Optional canonical panel above right cards |

Template 1.0 requires exactly two right cards, one primary and one secondary,
plus one secondary sync panel. Its baseline is: identity at top center, left
portrait at `4px/72px` with `146×234`, memory at `4px/334px` with `146px`
width, and two `188×334` right portraits at the bottom-right. The complete
geometry and stable `data-theme-part` names live in
[template-v1.md](template-v1.md).

## Route and background invariant

The route class changes immediately in the canonical host, so the shared runtime
paints the resolved chat asset on the stable isolated `main::before` layer without
waiting for task-message DOM. It paints the resolved gradient/readability veil on
`main::after`. Placement, mirroring, filter, opacity and both veils come only from
`artwork-defaults.json` plus user overrides; theme CSS must not redeclare them.

The shared negative artwork layers stay inside the isolated `main` stacking context
and therefore sit behind native content without changing its geometry. Never apply
`position: relative` (or any other positioning override) to `main > *`: Codex
uses fixed and absolute direct children for its title bar and work panels, and a
blanket override will stretch or displace those native surfaces.

The chat paper may keep borders and shadows, but its background remains transparent. This prevents black flashes when React replaces the conversation subtree.

Transparency applies to the message fill, not to the message identity. Both
assistant and user messages keep a visible border, directional accent or
equivalent theme-specific frame. Edited-file summaries and diff containers may
also be transparent, but their state text and boundaries remain legible.

## Manifest and package rules

- `schemaVersion`: `2`
- `engineVersion`: `2`
- `type`: `advanced`
- version for this repository: `"1.0"`
- author for official themes: the repository owner's GitHub name
- every asset and preview must be relative, declared, present, and inside the package
- `entryPoints.artworkDefaults` must be `artwork-defaults.json`; it must validate
  against `theme-artwork-defaults-v1.schema.json`, match the manifest theme id,
  and define all six hero/sidebar/chat light/dark slots using their original
  manifest asset keys
- every `var(--tessalume-asset-<name>)` reference in CSS must map to a manifest asset
  key `<name>`; when preserving legacy CSS, keep its old asset keys as aliases
  or deliberately rename every CSS reference
- only direct children of `themes/` are publishable packages; the build embeds only files declared by the root manifest
- keep design sources and historical alternates under ignored `.sources/`, `.references/`, or `.legacy/`
- do not publish theme-local changelogs or process notes

## Verification

For every theme change:

1. Parse all JSON.
2. Run `node --check theme.js`.
3. Validate CSS braces and required selectors.
4. Verify every declared asset exists.
5. Run `validate_theme_contract.py`.
6. For Template 1.0, run `sync_template_geometry.py --check`.
7. Complete the blocking first-pass audit in
   [flagship-completeness.md](flagship-completeness.md).
8. In the application repository, run the repository-root `一键构建EXE.ps1`;
   the build owns portable synchronization and optimized assets. In a portable
   creator workspace marked by `TESSALUME_CREATOR_WORKSPACE.md`, skip the EXE
   build and hand the validated `themes/<theme>` folder to Tessalume for import.
9. Reapply the current source or current build to Codex before visual
   inspection. Verify a change-specific DOM node, asset or computed style to
   prove the running page is not an older injected payload.
10. Navigate task → task → home → task in both light and dark mode when visual
    QA is in scope. Check every item in the flagship runtime QA matrix,
    including deep environment-panel and composer-footer states.

Do not hand-copy files into portable output.
For runtime, C#, XAML or build-script changes, also run the tests relevant to
that code in addition to the complete build.
