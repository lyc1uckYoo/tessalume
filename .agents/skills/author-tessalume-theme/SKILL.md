---
name: author-tessalume-theme
description: Create, migrate, refactor, or validate Tessalume advanced themes with Flagship Template 1.0, canonical injection, frozen shared geometry, adaptive widget visibility, stable light/dark backgrounds, portable synchronization, and trust fingerprints. Use whenever working under themes/, changing the examples/ Template 1.0 package or theme runtime, or developing a new Tessalume theme package.
---

# Author Tessalume themes

Use Flagship Template 1.0 and the canonical host. Do not implement route
observers, Codex DOM marking, cleanup, debounce, or shared geometry inside an
individual theme.

## Workflow

1. Read [references/theme-contract.md](references/theme-contract.md).
2. For Template 1.0 work, also read
   [references/template-v1.md](references/template-v1.md).
3. Start new themes with `scripts/scaffold_theme.py`; do not copy a published
   character theme.
4. Preserve every `data-theme-role`, `data-theme-part`, priority and frozen
   geometry rule. Change only local assets, copy, color tokens, visual skin and
   character-specific CSS animation.
5. Call `context.mountCanonicalTheme(...)` exactly once with
   `templateVersion: "1.0"` and `adaptiveLayout: true`.
6. Paint chat artwork on the stable themed `main`; never paint it on the
   replaceable chat-content pseudo-element.
7. Run `scripts/sync_template_geometry.py --check` and
   `scripts/validate_theme_contract.py` for every Template 1.0 theme.
8. Sync changed package files to
   `dist/portable-win-x64/themes/<directory>/` only when that portable
   directory exists, then update its trusted fingerprint.
9. For runtime, C#, XAML, or build-script changes, build and run relevant
   tests. For theme-only changes, do not build unless the user requests it.
10. Never push without explicit permission for the current task.

## Reusable resources

- Use `assets/theme-template/` as the only canonical Template 1.0 package
  skeleton.
- Run:

```powershell
python .agents/skills/author-tessalume-theme/scripts/scaffold_theme.py `
  --repo-root . --directory my-theme --id creator.my-theme `
  --name "English Name" --author "github-user" --namespace abc
```

- Validate:

```powershell
python .agents/skills/author-tessalume-theme/scripts/sync_template_geometry.py `
  --check themes/my-theme

python .agents/skills/author-tessalume-theme/scripts/validate_theme_contract.py `
  --repo-root . --author lyc1uckYoo `
  themes/xin.moonfox-sovereign `
  themes/aemeath-star-voyage `
  themes/danya.bubble-void-duality
```

Use `scripts/sync_template_example.py` only when the repository-owned example
must be regenerated from the template asset.

Treat any contract or geometry error as blocking. Theme-specific visual
differences never justify moving sizes, positions, visibility, route logic, or
cleanup back into theme-owned code.
