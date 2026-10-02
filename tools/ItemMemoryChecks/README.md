# Item memory regression checks

Run after building both Mod plugins with `Build-Deploy.ps1`:

```powershell
dotnet run --project tools/ItemMemoryChecks -- "C:/Program Files (x86)/Steam/steamapps/common/The Scroll Of Taiwu/The Scroll of Taiwu_Data/Managed" "$PWD/mods/TheScrollOfHomelander/Plugins/Front/BetterTaiwuScrollFrontend.dll"
```

Uses .NET 8 and the installed frontend assemblies. Checks the actual compiled
Mod's refresh overload, invocation arguments, card-scroll field, independent
exchange-side memory, default values, immutable save snapshots, and unchanged
selection handling. It calls `RefreshList(false)` only on an uninitialized object,
where the game's `_inited` guard returns immediately. It does not launch the game,
construct Unity objects, or invoke a settings save that could touch user data.

These checks do not validate Unity UI lifecycle or replace manual acceptance.
