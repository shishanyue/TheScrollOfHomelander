# 开发位置与工作约定

自 2026-09-16 起，太祖绘卷（TheScrollOfHomelander）Mod 的唯一开发目录是本仓库：

`C:\Users\Administrator\Documents\TheScrollOfHomelander-Workspace`

Windows 开发使用以上目录；Linux 上同一目录挂载为：

`/run/media/shishanyue/Win11Pro X64/Users/Administrator/Documents/TheScrollOfHomelander-Workspace`

Linux Steam 游戏目录：`/home/shishanyue/.local/share/Steam/steamapps/common/The Scroll Of Taiwu`。
路径含空格且大小写敏感；前端程序集位于 `The Scroll of Taiwu_Data/Managed`，后端位于 `Backend`。

## 目录

- `mods\TheScrollOfHomelander`：Mod 正式源码、资产、Config/Settings 与已构建的 DLL/PDB。
- `tools/TaiwuStudio.RoslynWorker`：前端/后端编译程序（.NET 8）。
- `tools/TaiwuStudio.DecompilerWorker`：游戏程序集反编译与接口查询程序（.NET 8）。
- `tools/build-linux.sh`：从当前仓库源码构建两个工具，所有 Linux 产物与中间文件隔离在 `tools/.linux/`，不覆盖本机 Windows 构建的 `bin/Release`。编译产物不入库，首次克隆需从源码构建工具。
- `tools/run-worker.sh`：Linux 工具入口，参数为 `roslyn` 或 `decompiler`，通过 `dotnet` 运行 DLL，不直接运行 Windows `.exe`。
- `skills`：`taiwu-linux-development`（Linux 开发入口）、`taiwu-mod-authoring`（通用 Mod 工程规则）、`taiwu-decompiled-api`（游戏 API）、`the-scroll-of-homelander-mod`（本 Mod 结构）。根 `opencode.json` 已注册此目录，不依赖 `.dsh/skills` 的旧 Windows 链接。

旧目录 `C:\Users\Administrator\Documents\GitHub\taiwu_studio` 自本日期起只作历史存档；其 `mods\` 与 `tools\` 不再作为开发目标，也不要向它的 GitHub 远端推送本工作区的提交。

## 工作约定

- 源码修改、编译、部署都在本目录完成；部署目标仍是 Steam Mod 目录。
- Linux 开发先读 `skills/taiwu-linux-development/SKILL.md`。构建工具用 `bash tools/build-linux.sh`；API 查询和分端编译用 `bash tools/run-worker.sh`，具体参数见该 skill 的 references。
- Windows 开发先读 `skills/taiwu-windows-development/SKILL.md`，首次克隆先用 .NET 8 构建两个 worker。Mod 编译用 `.\mods\TheScrollOfHomelander\Build-Deploy.ps1`，部署加 `-Deploy`。该脚本目前不能只改 `-GameRoot` 就在 Linux 使用；Linux 分端编译是隔离检查，不生成正式发布清单，也不自动部署。
- 部署必须有完整备份、当前源码/游戏程序集/产物清单和逐文件 SHA256 验证。两端任一编译失败均禁止发布；不得以旧 DLL 或单端成功替代。
- 修改前先读懂当前游戏程序集接口：优先用 `tools\TaiwuStudio.DecompilerWorker` 反查类型与成员，不要凭旧源码猜测。
- 不启动游戏、不运行游戏内自动化测试；游戏内验收由用户完成。
- 部署后必须提示用户完全退出并重启游戏，DLL 才会重新加载。
- `C:\Users\Administrator\Documents\TheScrollOfHomelander` 是游戏写入 Mod 设置的数据目录（只有 JSON 配置），禁止把源码放进去或覆盖它。
- Linux 安装是 Windows 游戏版本，不能因宿主是 Linux 就改掉 `UNITY_STANDALONE_WIN` 或让 Mod 引用 .NET 10 运行库。Proton 用户数据需在实际 `compatdata/838350/pfx` 内确认，不能直接假定为宿主的 Documents；禁止通过启动游戏来自动验收。
