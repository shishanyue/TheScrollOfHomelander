# 制作结果回归检查

独立 .NET 8 检查程序，直接引用已安装游戏前端的 `GameData.Shared.dll` 和 Mod 的
`MakeResultValidation.cs`。不启动游戏，不读写存档。

从仓库根目录运行（PowerShell 7）：

```powershell
$artifacts = Join-Path $env:LOCALAPPDATA 'Temp\opencode\crafting-result-checks'
dotnet build ./mods/TheScrollOfHomelander/Checks/CraftingResults/CraftingResults.csproj -c Release --artifacts-path $artifacts
& (Join-Path $artifacts 'bin\CraftingResults\release\CraftingResults.exe')
```

游戏安装在其他目录时，构建命令加 `-p:GameRoot="实际游戏目录"`。
检查产物只放在临时目录，不部署到 Mod。

## 2026-09-19 修复依据

- `default(MakeResult).TargetResultStage.TemplateId` 为 `0`，食物模板 `0` 是“野果”。
  原生普通制作直接把这个值放进产物列表，必须先确认预览已初始化且对应当前配方。
- 荤素目标通过 `Material.CraftableItemTypes → MakeItemType.MakeItemSubTypes` 解析配方；
  不能对材料、制作类型和配方三个不同命名空间的模板编号做算术推导。
- 随机食物类别是 `701=荤`、`700=素`；合并目标 `799` 只用于 Mod 界面。
- 预览和条件检查保存配方快照；换材料、工具、建筑、手动/精制选项后拒绝旧响应。
  普通提交还核验数量和资源，阻止重复提交；失败或条件查询超时后恢复按钮，关闭界面清理请求。
  条件查询超时不自动重试制作，迟到的条件响应被丢弃。
- `RefreshPanel()` 在当前游戏中只是未被原生流程调用的兼容方法。
  制作后自动选材改为挂接实际 `Refresh(BuildingMakeDisplayData)`，避免再次点击已选材料将其取消。

以下接口由本仓库 `TaiwuStudio.DecompilerWorker` 从当前安装 DLL 核对：

| 侧 / 程序集 | 类型与成员 | Token | 用途 |
| --- | --- | --- | --- |
| 前端 / Assembly-CSharp.dll | Game.Views.Make.MakeSubPageMake.RequestMakeResult() | 0x0600728F | Prefix/Finalizer 捕获预览上下文；Postfix 处理无材料时无回调 |
| 前端 / Assembly-CSharp.dll | 同类型 OnGetMakeResult() | 0x06007283 | Postfix 恢复预览与批量按钮状态 |
| 前端 / Assembly-CSharp.dll | 同类型 OnClickButtonConfirm() | 0x06007287 | Prefix 拒绝缺失、过期预览和重复提交 |
| 前端 / Assembly-CSharp.dll | 同类型 RefreshMakeType() | 0x06007270 | Prefix 解析荤素配方 |
| 前端 / Assembly-CSharp.dll | 同类型 CheckCondition(bool) | 0x06007281 | 核对请求/仅重绘两个分支 |
| 前端 / Assembly-CSharp.dll | 同类型 Refresh(BuildingMakeDisplayData) | 0x06007253 | Prefix 结束提交等待；Postfix 自动选材 |
| 前端 / Assembly-CSharp.dll | 同类型 OnDisable() | 0x06007250 | Postfix 清理请求及等待状态 |
| 前端 / Assembly-CSharp.dll | GameData.Domains.Building.BuildingDomainMethod+AsyncCall.CheckMakeCondition(IAsyncMethodRequestHandler, MakeConditionArguments, AsyncMethodCallbackDelegate) | 0x06011E59 | Prefix 包装条件响应 |
| 前端 / Assembly-CSharp.dll | 同类型 GetMakeResult(IAsyncMethodRequestHandler, short, ItemKey, BuildingBlockKey, sbyte, List<short>, short, bool, bool, AsyncMethodCallbackDelegate) | 0x06011E90 | Prefix 包装预览响应，等待完整结果后允许普通提交 |
| 后端 / GameData.dll | GameData.Domains.Building.BuildingDomain.GetMakeResult(short, ItemKey, BuildingBlockKey, sbyte, List<short>, short, bool, bool) | 0x06005D95 | 核对已初始化阶段和模板/子类型成对结果；无新增后端补丁 |

反编译阅读范围：`MakeSubPageMake` 397–487、574–767、1232–1429、1593–1915、
2010–2073、2256–2266；`BuildingDomain` 8247–8346。Token 和行号仅对应本次安装版本，
游戏更新后需要重新查询。构建清单 `release-manifest.json` 记录游戏 DLL 指纹。

## 覆盖与验收

15 项离线检查覆盖缺失/未初始化结果、越界阶段、造诣不足、错误物品类型、过期配方、
手动选择、随机池一致性，以及合法模板 `0` 不被全局禁用。
这些检查不验证 Unity 界面或真实制作流程。

部署后完全退出并重启游戏，手动验证：

1. 分别选择荤、素、荤素，使用不同材料进行普通及连续制作；产物应符合当前材料配方。
2. 快速切换材料、工具、手动子类、精制选项；预览等待时禁止提交，随后恢复可制作状态。
3. 普通制作后自动选材正常；同一材料仍有剩余时不会被再次点击取消。
4. 背包与仓库持有同模板材料时，耗尽一处不会误取消另一处的有效选择；列表无零数量残留。
5. 条件检查失败、停止批量、无材料、快速关闭再打开界面后，按钮和选材状态正常。
6. 关闭对应技艺的制作优化后不自动填资源或进入连续制作；允许徒手时无可用工具可正确回退。
7. 批量制作设为勾选模式，勾选后点击原生“制作”应启动连续批量，按钮变为停止。
   再次点击应停止；取消勾选后点击应只做原生单次制作。按钮模式仍由独立批量按钮启动。
   勾选模式没有符合范围的材料或启动条件不满足时不得退回单次制作绕过限制。

2026-09-30：当前 `Game.Views.Make.MakeSubPageMake.OnClickButtonConfirm()`
（前端 `Assembly-CSharp.dll`，token `0x06006B45`）仍为原生单次提交。
Mod 的 `ContinuousMakeConfirmPatch` 在最高优先级 Prefix 中将勾选模式路由到
现有 `TryStartConfiguredBatch`，与独立批量按钮共用执行、刷新续作及停止流程。
