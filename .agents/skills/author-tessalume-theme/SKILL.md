---
name: author-tessalume-theme
description: Create, migrate, refactor, redraw, or validate Tessalume advanced themes with Flagship Template 1.0 while preserving each character theme's original visual identity, light/dark artwork, message frames, SVG components, and signature animation. Use whenever working under themes/, changing the examples/ Template 1.0 package, editing theme runtime behavior, or preparing Tessalume theme builds and visual QA.
---

# Author Tessalume themes

Use Flagship Template 1.0 for shared structure and geometry. Preserve the
theme's character-specific skin and motion. Never interpret “match the
template” as permission to replace original art direction, internal SVG,
component identity, or keyframes with the template examples.

## Required reading

1. Read [references/theme-contract.md](references/theme-contract.md).
2. Read [references/template-v1.md](references/template-v1.md) for every
   Template 1.0 task.

## Workflow

1. Inspect repository instructions and the complete working tree before edits.
2. Choose the correct path:
   - New theme: run `scripts/scaffold_theme.py`; do not copy a published theme.
   - Existing theme migration: keep the original package as the visual
     baseline. Inventory its home, sidebar, chat background, assistant/user
     frames, left card, both right cards, memory, sync panel, composer
     accessory, light/dark variants, asset variables, SVG and keyframes.
3. For a migration, run `scripts/audit_migration_preservation.py` against the
   unmodified Git baseline. Add `data-theme-role`, `data-theme-part` and
   priority attributes to the existing DOM wherever possible. Do not replace
   the original inner markup with the template sample components.
4. Preserve every role, part, priority and runtime-owned shared geometry. Change only
   declared assets, copy, color tokens, visual skin and character-specific
   animation. Remove only theme-owned lifecycle code and geometry declarations
   that conflict with the canonical host.
5. Call `context.mountCanonicalTheme(...)` exactly once with
   `templateVersion: "1.0"`, `preserveRoot: true` and
   `adaptiveLayout: true`. Use both canonical positioning helpers with the
   Template 1.0 dimensions.
6. Paint chat artwork on the isolated themed `main`. Keep message-container
   fills transparent when artwork exists, but retain visible assistant and user
   borders or equivalent directional frames.
7. Redraw only the assets the user requested. Preserve character identity,
   distinct light/dark forms and region-specific composition; do not reuse a
   generic pose or decoration from another theme.
8. Before building, run the preservation audit for migrations, then run the
   geometry and contract checks for every theme:

   ```powershell
   python .agents/skills/author-tessalume-theme/scripts/audit_migration_preservation.py `
     --repo-root . --baseline-ref HEAD themes/<theme>

   python .agents/skills/author-tessalume-theme/scripts/sync_template_geometry.py `
     --check themes/<theme>

   python .agents/skills/author-tessalume-theme/scripts/validate_theme_contract.py `
     --repo-root . themes/<theme>
   ```

   Treat lost baseline keyframes/assets, undeclared asset variables, geometry
   overrides, contract errors and shared-geometry isolation failures as blocking.
9. Run the repository-root `一键构建EXE.ps1` after every theme, template,
   runtime or asset change. Let the build fully replace
   `dist/portable-win-x64`, optimized assets and trust fingerprints. Never
   hand-sync or merge portable output.
10. For visual QA, reapply the current source or current build to the running
    Codex page before inspecting it. Confirm a change-specific DOM or computed
    style signal so an older injected payload cannot be mistaken for the new
    build. Check home and task views in both light and dark mode; keep only the
    final critical screenshots unless diagnosis needs more.
11. Distinguish static validation, build validation and actual runtime visual
    validation in the handoff. If the user chooses to inspect visually, state
    that boundary.
12. Never push without explicit permission in the current task. Before a
    permitted push, inspect the full status and include exactly the scope the
    user authorized.

## Reusable resources

- `assets/theme-template/` is the only canonical skeleton for new themes.
- `scripts/sync_template_geometry.py` validates the runtime-owned shared geometry and rejects theme-local geometry.
- `scripts/validate_theme_contract.py` validates structure, assets, geometry
  conflicts and portable synchronization after the build.
- `scripts/audit_migration_preservation.py` compares an existing theme with its
  Git baseline so lost SVG-related classes, asset variables and keyframes are
  visible before commit.
- Use `scripts/sync_template_example.py` only when the repository-owned example
  must be regenerated from the template asset.

Theme-specific visual differences never justify changing shared sizes,
positions, adaptive priorities, route logic or cleanup. Shared geometry never
justifies erasing theme-specific visual identity.
