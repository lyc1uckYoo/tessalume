# Flagship first-pass completeness

This is a blocking production checklist, not optional polish. Apply it before
image generation, before the first build and again during runtime QA.

## 1. Character research gate

Use current official or primary sources where available. Write a compact
identity sheet before prompting images:

- face shape, apparent age, body type and height/proportion;
- hair color, length, silhouette, fringe and ornaments;
- eye color and characteristic expression;
- costume silhouette, materials, dominant colors and non-negotiable details;
- weapon, ability language, symbols, motifs and environmental cues;
- personality, emotional range and characteristic posture;
- distinct forms, transformations and which details change or remain invariant.

The user may provide generated references. Treat them as composition/style
references only when they conflict with official identity, unless the user
explicitly declares them authoritative. Compare face, proportions, hair,
ornaments, costume and weapon after every generation. A beautiful image with
the wrong character is a failed asset.

## 2. Eleven-slot art matrix

Finish all eleven standard slots. Do not reuse one crop everywhere.

| Slot | Required composition |
|---|---|
| `hero-light` | Wide home banner; character biased right; left copy-safe area |
| `hero-dark` | Distinct wide banner; character biased right; left copy-safe area |
| `sidebar-light` | Tall crop; character occupies roughly 3/5-4/5; readable navigation |
| `sidebar-dark` | Distinct tall crop; character occupies roughly 3/5-4/5 |
| `chat-light` | Wide task scene; character centered so the chat viewport retains the full subject |
| `chat-dark` | Distinct centered task scene; controlled luminance for readable prose |
| `task-left` | Portrait crop designed for `146x234`; clear face and silhouette |
| `task-right-secondary` | Portrait crop designed for `188x334`; distinct pose/form |
| `task-right-primary` | Portrait crop designed for `188x334`; distinct pose/form |
| `memory-light` | Quiet texture or symbolic scene that supports overlaid text |
| `memory-dark` | Distinct dark texture or symbolic scene with restrained contrast |

When a character has multiple forms, distribute them across surfaces and both
color modes. “Alternate forms” means the mode does not permanently own one
form; it does not mean placing both characters into every image or animating
between two people.

For each row record: form, pose, crop, focal point, dominant color, negative
space and intended mode. Reject identical poses, mirrored duplicates, simple
recolors and artwork that relies on CSS to repair a wrong composition.

## 3. Required visual surface coverage

### Home

- Both light and dark banners select the correct asset and copy treatment.
- Title, accent, kicker, subtitle and note remain readable in both modes.
- Home motion has multiple character-specific layers and at least three visual
  rhythms (for example drift, pulse and orbit). A single scan line or a row of
  generic dots is incomplete.
- Suggestion cards, hover/focus states and native home surfaces belong to the
  same art direction.

### Sidebar

- Style `aside.app-shell-left-panel` itself in light and dark mode, not only its
  child rows.
- Use pseudo/layered scene treatment to control art, veil and edge lighting.
- Verify project rows, thread rows, active/hover states, section headings and
  icons against both artworks.
- Preserve a large, visible character without using an opaque wash to hide it.

### Stable task canvas

- Paint the character on the isolated themed `main::before`; keep the
  readability veil on `main::after`.
- Never position or transform `main > *` globally.
- Center the subject for chat compositions. Adjust background size/position and
  veil independently for light and dark mode.
- Dark artwork must not overpower text, task controls or transparent bubbles.

### Task title and environment information

- Style the task-header shell and the task-title row, including its button,
  icon, title text, status/mode badge and hover/focus behavior.
- Style the environment panel shell plus its internal sections, section
  headers, separators, item buttons, icons, counters and expanded states.
- Do not stop at coloring the outer card; native inner gray bars or default
  list rows indicate an unfinished theme.

### Messages

- Assistant and user messages have distinct, visible directional frames.
- Message fills remain transparent when chat artwork exists.
- Both message types have deliberate inner padding and content spacing; border
  alone is insufficient.
- Headings, code, links, lists, edited-file summaries and diff/status surfaces
  remain legible in both modes.

### Composer

- Style the surface, focus ring, text, placeholder and editor states.
- Style the footer row: add button, permission badge, model picker, context
  indicator, microphone and send button, including hover/active/disabled states.
- The accessory is a recognizable weapon, sigil or character object. A generic
  circle, crown, orb or letter inside a ring is a draft.

### Theme-owned flagship components

- Left card, both right cards, captions and crops are independently tuned.
- Memory card uses character-specific text, texture, meter/sigil and animation.
- Sync panel has a designed shell, copy hierarchy, core/instrument, meter and
  state. It must not be only a scanning border plus a small ball.
- Give home motion, memory, sync and accessory separate animation rhythms.
  Respect `prefers-reduced-motion`.

## 4. First-build gate

Before building, search for and remove:

- `data-theme-draft`, placeholder asset paths and starter sample copy;
- generic starter keyframes or starter component markup left unchanged;
- missing `.light-only`/`.dark-only` exclusivity;
- task-title, environment-panel or composer-footer selectors with no skin;
- message frames without padding;
- dark chat art with no dedicated luminance/veil tuning;
- duplicated selectors or declarations appended outside sections 01-13.

The contract validator catches objective remnants. The qualitative checks still
require human judgment and runtime screenshots.

## 5. Runtime QA matrix

Check all four primary states: home light, home dark, task light and task dark.
Then test task-to-task, task-to-home and home-to-task navigation, environment
panel open/closed, composer focus/disabled states and adaptive card hiding.

Before judging screenshots, prove the current payload is active with a
build-specific class, SVG group, asset URL, keyframe name or computed style.
The full build does not refresh an already injected theme automatically.
