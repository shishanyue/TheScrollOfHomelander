# Crafting preview lifetime checks

Run from the workspace root:

```powershell
$artifacts = Join-Path $env:TEMP 'homelander-crafting-preview-checks'
dotnet build mods/TheScrollOfHomelander/Checks/CraftingPreview/CraftingPreview.csproj -c Release --artifacts-path $artifacts
& (Join-Path $artifacts 'bin/CraftingPreview/release/CraftingPreview.exe')
```

This compiles the frontend's real `MakePreviewBarrier` without Unity or the game. It covers stop followed by immediate confirmation, callbacks arriving after stop or recipe replacement, all-response completion, and recovery request coalescing. Result/template validation against the installed game structs is covered by the adjacent `CraftingResults` check. Fixed targets bypass the preview barrier in `MakePreviewRequest.CanSubmit`; valid normal crafts still enter the native confirm method with `continuous_make_ui` disabled.
