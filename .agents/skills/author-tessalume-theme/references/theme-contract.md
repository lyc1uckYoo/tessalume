# Tessalume canonical theme contract

## Ownership boundary

The Studio runtime owns:

- route detection and the `*-is-home` / `*-is-task` state;
- the persistent main, sidebar, home, window-bar, chat, message, output, and task-header bindings;
- mutation observation, resize handling, debounce timing, periodic repair, data cleanup, and unmount cleanup;
- stage geometry, composer-accessory placement, and optional panel-above-cards placement.

The theme package owns:

- manifest metadata and declared local assets;
- light/dark color variables and artwork selection;
- markup inside `#cts-theme-root`;
- character-specific cards, copy, symbols, and CSS-only animation;
- only the small `onEnsure` callback needed to position or reparent theme-owned elements.

Do not move runtime-owned work back into a theme.

Flagship Template 1.0 additionally freezes shared DOM parts, sizes, positions,
alignment and adaptive priorities. Read
[template-v1.md](template-v1.md) before creating or migrating a flagship theme.

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

The route class changes immediately in the canonical host. The task background must therefore be paintable without waiting for task message DOM. If artwork needs horizontal mirroring, mirror only the stable `main::before` artwork layer; keep the readability gradient in an untransformed `main::after` layer:

```css
html.NS-theme.NS-is-task main.NS-main {
  position: relative;
  isolation: isolate;
  background: var(--NS-task-fallback) !important;
}

html.NS-theme.NS-is-task main.NS-main::before {
  content: "";
  position: absolute;
  z-index: -2;
  inset: 0;
  background: var(--NS-chat-art) right center / auto 110% no-repeat;
  transform: scaleX(-1);
}

html.NS-theme.NS-is-task main.NS-main::after {
  content: "";
  position: absolute;
  z-index: -1;
  inset: 0;
  background: linear-gradient(...);
}

.NS-chat-paper::before {
  content: none !important;
}
```

The negative artwork layers stay inside the isolated `main` stacking context and
therefore sit behind native content without changing its geometry. Never apply
`position: relative` (or any other positioning override) to `main > *`: Codex
uses fixed and absolute direct children for its title bar and work panels, and a
blanket override will stretch or displace those native surfaces.

The chat paper may keep borders and shadows, but its background remains transparent. This prevents black flashes when React replaces the conversation subtree.

## Manifest and package rules

- `schemaVersion`: `2`
- `engineVersion`: `2`
- `type`: `advanced`
- version for this repository: `"1.0"`
- author for official themes: the repository owner's GitHub name
- every asset and preview must be relative, declared, present, and inside the package
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
7. Compare source and portable package files when portable output exists.
8. Recalculate portable trust fingerprints.
9. When possible, apply the theme to a running Codex target and navigate task → task → home → task in both light and dark mode.

For runtime changes, also build Release and run the core test suite.
