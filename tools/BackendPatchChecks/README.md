# Backend patch checks

Checks the **built backend plugin DLL** against the currently installed backend
assemblies. This catches Harmony target changes which still allow the Mod to
compile, including the October 2026 change from four to five parameters in
`CharacterPlanningAgent.SelectActionTarget`.

Run from the workspace root with .NET 8:

```powershell
dotnet run --project tools/BackendPatchChecks -- `
  --game-libs 'C:/Program Files (x86)/Steam/steamapps/common/The Scroll Of Taiwu/Backend' `
  --plugin 'mods/TheScrollOfHomelander/Plugins/Back/BetterTaiwuScrollBackend.dll' `
  --report 'tools/BackendPatchChecks/bin/current-check.json'
```

The game and plugin paths are mandatory; `--report` is optional. No NuGet Harmony
replacement or game assembly copy is used. Framework dependencies use the host
.NET 8 runtime; game, Harmony and MonoMod dependencies resolve from the supplied
backend directory. The tool runs in its own process. Its generated `bin`/`obj`
files and example JSON report are ignored by Git.

## What it verifies

1. Discover every class carrying the built DLL's `[HarmonyPatch]` marker, and call
   its built `ModPatchGroups.Classify`. Require automatic monthly purchase,
   cultivation and month diagnostics to retain their independent groups.
2. Use the installed Harmony's `PatchClassProcessor` metadata handling and target
   resolution. Evaluate the reviewed `Prepare` and `TargetMethod(s)` callbacks,
   with normal `Prepare=false` explicitly reported as a skip.
3. Resolve every enabled target to an assembly in the selected backend directory;
   check managed method bodies, injected original argument names/types,
   `__instance`, `__result`, `__state` and exception types. Missing targets are
   failures, including an exact type/parameter description in the report.
4. Combine all active patch classes on each target in the Mod's group/class
   installation order. Build a private `PatchInfo`, copy the original method IL
   with `DynamicMethodDefinition`, run
   `HarmonyManipulator.Manipulate(original, patchInfo, context)`, and generate the
   wrapper with the installed MonoMod. This exercises Harmony's actual wrapper
   generation, including transpilers and interactions between patches on the same
   method, without detouring the original method.
5. Require zero registered live Harmony patches at the end. The JSON report
   records runtime, plugin/dependency SHA256 fingerprints, patch classes and
   groups, hook signatures, resolved target signatures/tokens, skipped classes,
   wrapper results and errors. Any failed check returns exit code 1.

The reflection adapter deliberately fails on unsupported hook signatures or
changed Harmony internals. It must be reviewed when the game changes Harmony.
It does not silently fall back to a substitute Harmony version.

## Execution boundary

Use only the trusted backend Mod built from this workspace. This is **not a
sandbox for arbitrary plugin DLLs**: the Mod's target-selection callbacks,
parameterless `bool Prepare` callback, transpilers and any IL manipulators do execute. The current
callbacks were reviewed to perform reflection/IL processing and in-memory
diagnostic accounting only.

The tool does not call `Plugin.Initialize`, settings loaders, gameplay methods,
or Prefix/Postfix/Finalizer hooks; it does not create domain objects, load saves,
start the game, install an `ILHook`, or change native detours. Generated wrappers
are emitted but never invoked or force-JITed. It does not prove IL/JIT validity or native detour compatibility,
third-party Mod compatibility, or the actual gameplay behavior. The user still
performs in-game acceptance after a full game restart.

## Verified regression, 2026-10-12

Against backend `GameData.dll` SHA256
`0F56B7B7BB324E95389E587B864CB9A06DC69D5859EF7F55C71AA0A05F29E1E4`
and backend Harmony `2.16.1.0` SHA256
`70E7647876B6C3D874EA99B56627A95D1DCE2E1DD791981A002578A2FEBC9BBD`:

- The old plugin (`2433B29A730C1C76CD497FC99EDF8161A5F4F0B325381861B3956691E4E893E6`)
  fails on the four-parameter `SelectActionTarget` target. It also exposes 29
  patch classes in the shared `month` rollback group rather than their intended
  isolated groups.
- The fixed plugin (`589CA2FFACD9CA1916467023FB86EC4F59B8857582AE3EDE8F3BBC42D4525F32`)
  resolves 50 patch classes: 49 active, one native-holder-query `Prepare=false`
  skip, 69 class/target bindings, and 57/57 combined wrappers generated.
  `SelectActionTarget` resolves to token `0x06008295` with the fifth `bool`
  parameter. There are zero errors and zero live Harmony patches.

These hashes identify this check's inputs, not a promise that a future build has
the same hash. Re-run the tool after building or updating the game.
