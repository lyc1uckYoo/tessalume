# Tessalume feature modules

Tessalume.App is a modular monolith. Each user-facing capability is implemented as a vertical feature slice instead of adding more controls and handlers to `MainWindow`.

## Module boundary

Each directory under `Features/` may own:

- `*View.xaml` and its presentation-only code-behind;
- immutable view state or result records;
- an application service that coordinates existing infrastructure and Core APIs;
- focused tests and snapshot commands for the feature.

The window shell owns only window lifetime, navigation, feature composition and truly cross-feature notifications. A feature view must not reach into another feature's controls. Cross-feature work is exposed through events, immutable requests/results or a small service interface.

## Dependency direction

```text
Shell -> Features -> Infrastructure adapters -> Tessalume.Core
                  -> shell-provided shared controls/styles
```

`Tessalume.Core` remains independent of WPF. Shared controls and styles cannot depend on an individual feature. Feature views resolve the shell design system through dynamic resources, so they do not copy or shadow theme brushes. Portable storage, Codex integration and operating-system APIs stay behind application or infrastructure services rather than being called from XAML views.

## Size and maintenance targets

- New feature views should stay below 400 lines where practical; split large sections into child views.
- View code-behind handles rendering and UI events, not filesystem, network or runtime orchestration.
- A coordinator or service should stay below 350 lines and have one reason to change.
- When a legacy file is migrated, add an architecture test that prevents its responsibilities from returning to `MainWindow`.
