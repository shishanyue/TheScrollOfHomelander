# Auto harvest offline checks

This .NET 8 executable compiles the production frontend
`Scripts/Frontend/Features/AutoHarvestAfterAdvanceMonthPatches.cs` directly, using
the API doubles in `FakeGame.cs`. It does not load game assemblies, run Unity,
read saves or settings, or wait for real time. It is a controller regression
check, not a substitute for the two-sided Mod build or manual game acceptance.

Run from the workspace root:

```powershell
dotnet run --project tools/AutoHarvestChecks/AutoHarvestChecks.csproj -c Release
```

The checks invoke the production marker Postfix methods, run queued frame and
bridge callbacks explicitly, and dispatch the controller's subscribed events.
They cover month completion with and without saving, saving/month transitions
longer than the polling budget, loading into the game, disabled paths, source
switches, empty availability, duplicate notifications, reset and stale session
callbacks, merged item/feather UI, recruitment, and timeout cancellation.
Selected private state assertions distinguish preserved pending work from
silent cancellation and detect an old delay clearing a new scheduled check.

`Source` can select an older controller without replacing the workspace file.
For example, the following temporary source shows the regressions on the commit
before this fix, `9df3473` (an exit code of 1 is expected):

```powershell
$oldSource = Join-Path $env:TEMP ('AutoHarvestBeforeFix-' + [Guid]::NewGuid().ToString('N') + '.cs')
git show 9df3473cc2a0f83538abdd468befb45cd7a9450d:mods/TheScrollOfHomelander/Scripts/Frontend/Features/AutoHarvestAfterAdvanceMonthPatches.cs |
    Set-Content -LiteralPath $oldSource -Encoding utf8
dotnet run --project tools/AutoHarvestChecks/AutoHarvestChecks.csproj -c Release "-p:Source=$oldSource"
# Restore the normal linked source for the next build.
dotnet run --project tools/AutoHarvestChecks/AutoHarvestChecks.csproj -c Release
Remove-Item -LiteralPath $oldSource
```

The frame double is a FIFO callback queue; it preserves callback order but does
not model exact frame durations. Availability replies and timeout callbacks use
separate queues so a test can deliver old replies after a reset. Harmony's
attribute and reflection helpers are doubles: these checks do not prove the
installed game accepts the patch signatures.

## Current game API evidence

The frontend `Assembly-CSharp.dll` SHA256 was
`A677FC7FFA7118DBC7D1CC33FC354BF11F017E03DB0AF25C901CC2A1440FAACD`.
`WorldDomainMethod.Call.AdvanceMonth_DisplayedMonthlyNotifications(bool)` has
token `0x06010DF3`. `BasicGameData.OnAdvancingMonthState()` (`0x060008F6`)
passes `false` during the tutorial and otherwise uses `SkipSaving` and drive
space checks. `ViewMonthNotify.TryEndAdvancingMonth()` (`0x060064CF`) and
`UI_MonthlyEvent.TryEndAdvancingMonth()` (`0x060030EF`) pass
`_needSave && !GMFunc.DisableAutoSaving`.

The backend `GameData.dll` SHA256 was
`0F56B7B7BB324E95389E587B864CB9A06DC69D5859EF7F55C71AA0A05F29E1E4`.
`WorldDomain.AdvanceMonth_DisplayedMonthlyNotifications(DataContext, bool)`
(`0x0600619F`, decompiled lines 3899–3918) finishes the month and resets its
state in both paths; only `SaveWorld` depends on the Boolean. Therefore the
Boolean cannot be used as a harvest eligibility gate. This establishes the
code defect without claiming the user's `SkipSaving` option was enabled.
