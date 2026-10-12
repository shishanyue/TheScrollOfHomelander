# Localization and Patch Isolation Checks

This standalone .NET 8 executable compiles the actual localization and patch-group
sources, using a .NET 8-compatible Harmony package for the test process. The game's
older Mono Harmony crashes under CoreCLR; production builds still reference only
the installed game library. This executable patches only its own test
methods. Language and Unity component stubs exercise text and row identity without
launching the game or accessing saves.

Patch isolation checks use real Harmony installation and deliberately missing
targets. They verify that a failed `make` group rolls back its earlier patches
while the UI hook remains active. Monthly scenarios independently fail diagnostics,
performance, both, and purchasing, then invoke the healthy purchase and cultivation
hooks to confirm they still execute. The checks use production `ModPatchGroups.Classify`
and require separate `month-diagnostics`, `month`, `monthly-purchase`, and `cultivation`
groups. Each scenario verifies failure reports, rollback, and restoration after
disposal; a final healthy scenario also catches patches leaked by earlier failures.

Run from the repository root in PowerShell 7:

```powershell
$artifacts = Join-Path $env:LOCALAPPDATA 'Temp/codex/homelander-localization-checks'
dotnet build ./mods/TheScrollOfHomelander/Checks/LocalizationAndPatches/LocalizationAndPatches.csproj -c Release --artifacts-path $artifacts
& (Join-Path $artifacts 'bin/LocalizationAndPatches/release/LocalizationAndPatches.exe')
```

Check English and Chinese panel layout manually in the game after a full restart.
