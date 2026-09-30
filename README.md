# 太祖绘卷 / TheScrollOfHomelander

《太吾绘卷》Mod：物品筛选与排序、连续制作、批量购买、仓库与建筑记忆、自动修理、产业收益收取及界面优化。各项功能可在 Mod 设置中启用或关闭。

A Mod for The Scroll of Taiwu with compact inventory controls, continuous crafting,
bulk purchasing, storage preferences, equipment repair and configurable UI improvements.

## Language / 语言

The crafting and purchasing panels, related buttons, options and tooltips follow the
game's active language (`LocalStringManager.CurLanguageKey`). Simplified Chinese and
English translations are included. Config.lua presents bilingual setting names and
descriptions. Native UI labels are recognized across the installed CN/CNH/EN/KO
language packs. Missing Mod translations retain their source text; full Traditional
Chinese and Korean translations are not included. Reopen panels after changing the
game language; restart the game if an existing panel retains its earlier text.

制作、购买设置面板及相关按钮随游戏语言显示中文或英文。设置项提供中英对照。
未翻译的文字保留原文，繁体中文和韩文暂未提供完整 Mod 翻译。

## Source Layout / 源码

| Path | Contents |
| --- | --- |
| `mods/TheScrollOfHomelander` | Mod source, Config/Settings, assets and build/deploy script |
| `tools/TaiwuStudio.RoslynWorker` | .NET 8 compiler, frontend/backend compiled separately |
| `tools/TaiwuStudio.DecompilerWorker` | Installed game API inspection |
| `skills` | Windows/Linux development, Mod authoring and API guidance |
| `tools/build-linux.sh`, `tools/run-worker.sh` | Isolated Linux tool build and invocation |

This repository replaces the historical Mod-only checkout. The canonical development
directory is `C:\Users\Administrator\Documents\TheScrollOfHomelander-Workspace`.
The old Taiwu Studio checkout is a synchronized copy, not the development source.
Both upstream and workspace histories are preserved. PR #2's localization and patch
isolation have been adapted to the current split architecture and installed game API.

## Windows Build / 编译

Requires PowerShell 7, .NET 8 SDK and an installed Windows version of the game.
From the repository root:

```powershell
dotnet build ./tools/TaiwuStudio.RoslynWorker/TaiwuStudio.RoslynWorker.csproj -c Release
dotnet build ./tools/TaiwuStudio.DecompilerWorker/TaiwuStudio.DecompilerWorker.csproj -c Release
./mods/TheScrollOfHomelander/Build-Deploy.ps1
```

Use `-GameRoot` when Steam is installed elsewhere. Both sides must compile without
errors. The script generates `Plugins`, compiler diagnostics and a release manifest
with source, game assembly and output SHA256 hashes. Generated binaries are not tracked.

## Installation / 安装

For an existing installation:

```powershell
./mods/TheScrollOfHomelander/Build-Deploy.ps1 -Deploy
```

The deployment script verifies a complete backup, then copies source and declared
plugins and checks hashes. It preserves installed configuration and assets, so it
does not automatically publish updated bilingual Config metadata. For a fresh
installation, build first, then copy `Config.lua`, `Settings.Lua`, `Assets`,
`GradeBackgrounds` and generated `Plugins` from the Mod directory into
`<game>/Mod/TheScrollOfHomelander`. Config plugin paths are relative to `Plugins`.

Do not copy game DLLs, worker binaries or `bin/obj` into the Mod. Preserve existing
settings and back up any existing installation before replacing files. Fully exit
and restart the game after changing DLLs. Runtime acceptance is performed by the user;
build success alone does not establish game compatibility for every feature.

用户 JSON 设置保存在 `Documents/TheScrollOfHomelander`，不是源码目录，请勿覆盖。
部署后必须完全退出并重启游戏，DLL 才会重新加载。

## Linux Development

```bash
bash tools/build-linux.sh
bash tools/run-worker.sh decompiler --help
```

See [Linux development](skills/taiwu-linux-development/SKILL.md) for paths, API queries,
side-specific compilation and Proton user data. Linux output is isolated under
`tools/.linux`; it is a compilation check, not the Windows release/deploy pipeline.
Keep `UNITY_STANDALONE_WIN` and the game's runtime references when using Proton.

## Checks

[Localization and patch isolation checks](mods/TheScrollOfHomelander/Checks/LocalizationAndPatches/README.md)
exercise translations, stable settings identity, missing-target rollback and independent
patch groups without launching the game. Production plugins use the game's Harmony;
the .NET test harness uses a CoreCLR-compatible Harmony package.

See [Mod architecture and manual acceptance](mods/TheScrollOfHomelander/README.md)
and [development rules](AGENTS.md).
