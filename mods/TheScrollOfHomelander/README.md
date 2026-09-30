# TheScrollOfHomelander

Canonical Mod source. This directory is independent of Taiwu Studio implementation
and generated `target` output. The initial source and assets were copied from the
installed Steam Mod on 2026-09-09. Missing backend types were recovered from its
deployed DLL; `AutoCultivationPatches` also incorporates the deployed settlement fix.

## Build And Deploy

Run with PowerShell 7 from this repository:

```powershell
./mods/TheScrollOfHomelander/Build-Deploy.ps1
./mods/TheScrollOfHomelander/Build-Deploy.ps1 -Deploy
```

Requires the Release TaiwuStudio.RoslynWorker built from this repository and installed game libraries.
Frontend and backend compile separately, each including `Scripts/Shared`.
Compilation errors abort before deployment. No game process or test is launched.
The script records compiler diagnostics and a release manifest containing the source
digest, individual source hashes, game assembly fingerprints and DLL/PDB hashes.

Deployment backs up the Steam Mod, updates source and the declared plugin files,
and checks their SHA256 hashes. Existing configuration, settings, assets and saves
are preserved. A separate development copy is maintained under
`%LOCALAPPDATA%/TaiwuStudio/ModDevelopment/TheScrollOfHomelander-quality`; it does not
replace any pre-existing unrelated AppData checkout. Restart the game before
manual acceptance because a running Unity process retains loaded assemblies.

## Ownership

- `Frontend/Shared/ModSession`: world generation, delayed work and unload cleanup.
- `Frontend/Shared/MakeExecutionLifetime`: only Mod-owned crafting coroutines.
- `Frontend/Features/ContinuousMakeBusiness`: native crafting arguments and calls.
- `Frontend/Features/ContinuousMakeExecutionController`: batching and UI coordination.
- `Frontend/Features/RecruitPeopleAutoActionSupport`: bounded recruitment queue.
- `Frontend/Features/BuildingEarningsSafetyPatches`: native area refresh validity.
- `Frontend/Shared/MemoryOptimizationSettingsStore`: persisted memory and snapshots.
- `Frontend/Features/MakeStorageLocationMemoryPatches`: storage destination memory.
- `Frontend/Shared/AsyncSettingsSaveQueue`: single writer, atomic replace, bounded retries.
- `Shared/ModPatchGroups`: independent installation and rollback by feature group.
- `Shared/ModProtocol`: shared names and existing protocol version.

Submitted gameplay mutations are not retried on timeout. The next normal data load
is authoritative. Settings failures retain the old disk file and latest in-memory
snapshot; at most three attempts run until a new edit or explicit flush.

## Manual Acceptance

PR #2 was recalibrated against the current installed game. Language is read from
`LocalStringManager.CurLanguageKey`; Chinese settings identities remain stable when
labels are translated. Crafting/purchase panels and related controls include English
translations. Config.lua's 94 setting keys, defaults and ranges are preserved, with
bilingual display text and version `1.0.44.31`. Existing grouped Harmony installation
retains transactional rollback, adds resilient type discovery and reports failures.

For standalone localization and patch isolation checks, see
[LocalizationAndPatches](Checks/LocalizationAndPatches/README.md). Check English and
Chinese panels, setting persistence, storage dropdowns, chicken trough buttons and
filter/sort controls manually. Full Korean/Traditional Chinese Mod translation is
not included. Reopen panels after changing language.

For the food/result-preview fix, see [crafting regression checks and acceptance](Checks/CraftingResults/README.md).

Verify recruitment/refusal without an open building area, across settlements, and
after rapid closing/reopening. Verify all batch speeds, tool priority, perfect
affixes, depleted materials and durability protection. Verify search failure/retry,
successive month advances, save switching and settings persistence. Long-session
stutter and CTD elimination require the player's game results; compilation alone
does not establish that outcome.
