# Tessalume compatibility packs

The embedded files in this directory are the permanent, last-known-good baseline used by the app.
Themes do not own Codex DOM selectors. `compatibility-profile-v3.json` maps stable semantic surface
names to the current Codex selectors. Runtime source lives in `Runtime/` and is split into bootstrap,
page recognition, adaptive layout, surface decoration, and cleanup/recovery responsibilities. The
fixed `runtime-bundle.json` order is assembled into `theme-runtime-v2.js` when built-in resources are
installed or an official compatibility pack is created.

Small official compatibility releases use tags named `compat-vX.Y.Z` and contain:

- `Tessalume-Compatibility.zip`
- `SHA256SUMS.txt`

The ZIP still contains only `compatibility-pack.json`, assembled `theme-runtime-v2.js`, and
`compatibility-profile-v3.json`. Build it with:

```powershell
.\tools\New-CompatibilityPack.ps1 -Version 3.0.2
```

Compatibility releases must be created with GitHub's “latest” flag disabled so they never replace
the latest application release. The app accepts a pack only after validating the GitHub asset hash,
every file hash, the fixed file list, the minimum app version, runtime contract, selector schema, and
archive boundaries. Installed packs live under `data/compatibility`; the embedded baseline is never
overwritten. A runtime injection failure automatically rolls back to the previous verified pack or
the embedded baseline.
