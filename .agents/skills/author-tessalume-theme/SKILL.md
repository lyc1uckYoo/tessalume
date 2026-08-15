---
name: author-tessalume-theme
description: Create and validate brand-new Tessalume character themes from zero with Flagship Template 1.0. Use whenever working under themes/, changing the Template 1.0 starter, authoring theme artwork or motion, or preparing Tessalume theme builds and visual QA. Enforces character research, eleven finished assets, complete light/dark visual coverage, character-specific components, frozen shared geometry, and runtime verification.
---

# Author Tessalume themes

Create every theme from the canonical Template 1.0 scaffold. The scaffold owns
structure, not art direction. A structurally valid theme is still unfinished
until every visible surface has a character-specific design in light and dark
mode.

## Required reading

Read all three references before editing or generating images:

1. [references/theme-contract.md](references/theme-contract.md)
2. [references/template-v1.md](references/template-v1.md)
3. [references/flagship-completeness.md](references/flagship-completeness.md)

## Repository branch policy

If the workspace root contains `TESSALUME_CREATOR_WORKSPACE.md`, use portable
creator mode. This workspace is intentionally not the Tessalume application
repository: do not require Git, do not look for a `main` branch, and do not run
the application EXE build. Create the theme under `themes/`, run the bundled
geometry and contract validators, and hand off that theme folder for import in
Tessalume.

Otherwise, use repository mode:

Perform all Tessalume work directly on the existing `main` branch. Before
editing, confirm `git branch --show-current` returns `main`; if it does not,
return safely to `main` before making changes. Never create or switch to a
feature branch, worktree branch or pull-request branch, and never open a pull
request. For commit or push requests, use ordinary Git directly on `main`; do
not invoke any publishing workflow whose default behavior creates a branch or
pull request. A push still requires the user's explicit permission in the
current task.

## Workflow

1. Inspect repository instructions, the working tree and the target character.
   Research current official or primary sources before designing. Record the
   character's invariant face, body type, hair, eyes, costume, accessories,
   weapon, symbols, palette, personality and any distinct forms.
2. Before image generation, write the eleven-slot art matrix from
   `flagship-completeness.md`. Give each slot its own crop, pose, focal point,
   light/dark assignment and readability plan. Light/dark mode must not be
   mechanically bound to a character form unless the user explicitly asks.
   Present the character research sheet and complete matrix to the user, then
   pause. Do not generate any image until the user explicitly approves them.
3. Create the package only with `scripts/scaffold_theme.py`. Never copy another
   published theme. Declare `templateVersion: "1.0"` and keep the canonical
   roles, parts, priorities and frozen geometry unchanged. Complete all six
   original-asset recommendations in `artwork-defaults.json`; if the image
   itself needs ambient motion, encode only relative placement deltas there.
4. Generate or prepare all eleven final assets at production quality. After
   every image, complete the fixed identity checklist in
   `flagship-completeness.md` and reject any failed item. Use the
   required composition for banner, centered chat artwork, sidebar and cards.
   Reject copied poses, wrong proportions, wrong ornaments and merely recolored
   duplicates.
5. Replace every starter asset, draft marker, sample sentence, generic circle,
   scan line and placeholder animation. Home motion, memory instrument, sync
   panel and composer accessory must derive from the character's weapon,
   abilities, symbols, temperament or story. The starter is never shippable.
6. Complete all native-surface coverage listed in
   `flagship-completeness.md`: home, `aside.app-shell-left-panel`, stable chat
   canvas, task-title row, assistant/user frames and padding, environment panel
   internals, composer footer controls, three cards, memory, sync and accessory.
   Style both light and dark states explicitly.
7. Call `context.mountCanonicalTheme(...)` exactly once with
   `templateVersion: "1.0"`, `preserveRoot: true` and `adaptiveLayout: true`.
   Use both canonical positioning helpers with the Template 1.0 dimensions.
8. Let the shared runtime paint chat artwork on the isolated themed `main` from
   `artwork-defaults.json`. Keep message fills transparent while retaining
   visible directional frames. Judge dark artwork from its actual result:
   retain its original brightness when already balanced, and adjust the data
   recommendation only when readability or composition requires it. The same
   runtime owns optional image-layer motion and accessibility reduction.
9. Keep `skin.css` in canonical 01-13 order. Modify only copy, color, texture,
   symbols and theme-owned decoration animation. Never put the three adjustable
   images, crop/effects or veils back into CSS, restore image-layer keyframes,
   append a late override dump, or edit the frozen geometry block.
10. Before building, run both blocking checks:

   ```powershell
   python .agents/skills/author-tessalume-theme/scripts/sync_template_geometry.py `
     --check themes/<theme>

   python .agents/skills/author-tessalume-theme/scripts/validate_theme_contract.py `
     --repo-root . themes/<theme>
   ```

   Treat draft remnants, missing visual coverage, asset errors, generic starter
   components, contract errors and shared-geometry isolation failures as
   blocking.
11. In repository mode, run the repository-root `一键构建EXE.ps1` and let the
   build fully replace `dist/portable-win-x64` and optimized assets. Never
   hand-sync or merge portable output. In portable creator mode, do not build
   Tessalume; the two blocking validators are the handoff gate.
12. Prepare the result for runtime QA. In portable creator mode, tell the user
   to import `themes/<theme>` from Tessalume. In repository mode, reapply the
   current source or build and confirm a unique DOM, asset or computed-style
   signal first. Screenshots are optional unless the user explicitly requests
   them.
13. Distinguish static validation, build validation and runtime visual QA in the
   handoff. Never push without explicit permission in the current task; before
   a permitted push, inspect status and include exactly the authorized scope.

## Reusable resources

- `assets/theme-template/` is the only skeleton for new themes. Its explicit
  draft markers force replacement of generic visual examples.
- `scripts/sync_template_geometry.py` validates runtime-owned shared geometry.
- `scripts/validate_theme_contract.py` validates structure, assets, frozen
  geometry, unresolved drafts and flagship visual coverage.
- Use `scripts/sync_template_example.py` only when the repository-owned example
  must be regenerated from the template asset.

Shared geometry never justifies a generic theme, and visual ambition never
justifies changing shared sizes, positions, adaptive priorities or lifecycle.
