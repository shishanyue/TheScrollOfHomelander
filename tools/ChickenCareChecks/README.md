# 元鸡喂养与促织存取离线回归

在工作区根目录运行：

```powershell
dotnet run --project tools/ChickenCareChecks -c Release
```

此 .NET 8 程序直接编译正式 `mods/TheScrollOfHomelander/Scripts/Backend/AutoCultivationPatches.cs`，通过反射构造过月状态并调用生产 `StoreCrickets`、`Postfix`、`Finalizer`。领域 doubles 提供内存中的鸡、饲料、促织、蛰室和剧情状态；不执行会读取用户 JSON 的 `Prefix`，不加载游戏程序集、存档或设置，也不启动游戏。2026-10-12 验证共 34 项全部通过。

覆盖空饲槽到仓库/行囊的后备选择、所有来源无料、鸡和真实饲料 Id 为 0、缺失鸡、阈值与精确扣料、仓库物品删除参数、来源顺序与开关、品级排序、伏龙剧情缺失/鸡王离家、重复模板和跨聚落目标、原生返回正增益但目标未变化、喂养期间鸡被移除或转移、关闭功能、零阈值和缺失聚落。原生调用次数上限仅用于让错误循环在离线测试中及时失败。

假领域保留此次问题涉及的原生语义：`ItemKey.IsValid()` 仅检查 `Id >= 0`，所以 `default(ItemKey)` 有效；鸡数据是值类型，缺失 id 返回默认值；喂养私有入口按模板查找首只鸡，原生扣除行囊/饲槽物品但不处理仓库。固定幸福度增益用于验证调用、停止条件与扣料次数，不模拟游戏配置的全部饲料收益。

促织的 10 项检查覆盖存入蛰室→原生月度回复→取回行囊、保留玩家原先存放的促织、显式取回玩家促织、无罐、死亡和耐久全满、候选排序、关闭自动取回、过月异常 `Finalizer` 恢复、完成后不重复取回，以及原生不能联通仓库时将取回目标改为仓库。生产存取逻辑保持原样，测试仅在中间调用一个可配置回复周期的领域 double，以验证存取顺序，不替代原生寿命、受伤或建筑加成规则。

这些检查验证生产补丁的控制流和资源消耗条件，不能替代当前游戏程序集的正式双端编译、Harmony 目标检查或用户游戏内验收。

## 修复前复现

`AutoCultivationSource` MSBuild 属性可指向另一份完整源码。相对路径以本项目目录为基准。以下示例提取修复前提交，临时源码放在 Git 忽略的 `obj` 中：

```powershell
New-Item -ItemType Directory -Path tools/ChickenCareChecks/obj -Force | Out-Null
$chickenBefore = git show 9df3473cc2a0f83538abdd468befb45cd7a9450d:mods/TheScrollOfHomelander/Scripts/Backend/AutoCultivationPatches.cs
[IO.File]::WriteAllLines((Join-Path $PWD 'tools/ChickenCareChecks/obj/AutoCultivationPatches.before.cs'), [string[]]$chickenBefore, [Text.UTF8Encoding]::new($false))
dotnet run --project tools/ChickenCareChecks -c Release -p:AutoCultivationSource=obj/AutoCultivationPatches.before.cs
```

2026-10-08 初始 24 项元鸡检查中，修复前 11 项失败，进程返回 1；修复后 24 项全部通过，进程返回 0。最直接的复现是鸡幸福度 70、空饲槽、仓库存在饲料：旧码选择有效的默认 `ItemKey` 后停止，幸福度仍为 70；修复后从仓库扣除 2 份饲料，幸福度达到 90。随后新增的促织检查不改变这项历史结论。

## 2026-10-08 元鸡接口证据

依据 2026-10-08 安装的后端程序集，使用本仓库 `TaiwuStudio.DecompilerWorker` 直接核查。`GameData.dll` SHA256 为 `2C4748B858E4F4F8B481F0B0A17CDBC13606357C7DE5968340716D77D13C3206`，后端 `GameData.Shared.dll` 为 `0B10038F4A89016254059F4988F6BE12BBA5FF5FF0DC2A37C051826CB90C6106`。

| 后端成员 | DLL / token | 核实的行为 |
| --- | --- | --- |
| `WorldDomain.AdvanceMonth(DataContext)` | GameData / `0x0600619D` | 保留过月结束后的补充喂养入口 |
| `BuildingDomain.FeedChickenWithArgs(DataContext, int, ItemKey, ItemSourceType)` | GameData / `0x06005D58` | 私有方法；int 参数是模板；返回 sbyte；扣行囊和饲槽，不扣仓库 |
| `BuildingDomain.GetSettlementChickenList(int, bool)` | GameData / `0x06005D46` | 第二参数为 true 跳过伏龙过滤；false 时可解引用缺失剧情参数盒 |
| `BuildingDomain.TryGetElement_Chicken(int, out Chicken)` | GameData / `0x06005F41` | 按实例 id 验证鸡存在，避免缺失 id 的默认结构体 |
| `BuildingDomain.TryGetElement_ChickenByTemplateId(int, out Chicken)` | GameData / `0x06005D5A` | 遍历全部鸡并返回首个匹配模板，不能保证聚落/实例相同 |
| `ItemKey.IsValid()` | GameData.Shared / `0x06003907` | 只检查 Id 是否非负；默认值 Id 为 0 |

## 2026-10-12 促织接口复核

游戏更新到 build `25845203` 后，重新用本仓库 `TaiwuStudio.DecompilerWorker` 读取当前后端 DLL。`GameData.dll` SHA256 为 `0F56B7B7BB324E95389E587B864CB9A06DC69D5859EF7F55C71AA0A05F29E1E4`；`GameData.Shared.dll` 为 `4FA0A319209EF61AF2E0B82D1D447431F5D20CAD42A369388B5CF0C4FD962080`。本次临时原生证据保存在 Git 忽略的 `obj/api-evidence-20261012-cricket/`。

| 后端成员 | DLL / token | 核实的行为 |
| --- | --- | --- |
| `CharacterDomain.GetInventoryItems(int, short)` | GameData / `0x06007512` | 按物品 subtype 扫描行囊；当前原生 `BuildingDomain.GetInventoryCrickets` 也传入 1100 |
| `BuildingDomain.CricketCollectionAdd(DataContext, int, bool, ItemKey)` | GameData / `0x06005DB9` | bool 为 isCricket；从原物品来源移除，不删除物品；放入全局 17 个蛰室收藏槽之一 |
| `BuildingDomain.CricketCollectionRemove(DataContext, int, bool)` | GameData / `0x06005DBA` | 默认目标为行囊，委派到带来源版本 |
| `BuildingDomain.CricketCollectionRemoveToSource(DataContext, int, bool, ItemSourceType)` | GameData / `0x06005DBB` | 移出收藏槽；行囊目标在仓库联通检查不满足时改为仓库 |
| `TaiwuDomain.CanTransferItemToWarehouse(DataContext)` | GameData / `0x060052BB` | 检查所在产业地块是否建成对应仓储/蛰室建筑 |
| `BuildingDomain.UpdateCricketRoom(DataContext)` | GameData / `0x06005DD7` | 在原生过月执行中回复蛰室促织；晚于 `ItemDomain.UpdateCrickets`，早于 Mod 过月 `Postfix` |
| `ItemDomain.UpdateCrickets(DataContext)` | GameData / `0x06006379` | 到指定月份更新寿命；蛰室内促织适用相应减龄效果 |
| `Building.SharedMethods.CalcCricketRegenTime(sbyte)` | GameData.Shared / `0x0600497A` | 罐品决定回复间隔，公式为 `3 - Grade.GetGroup(jarGrade)` |

这次复核没有发现促织存取签名或 subtype 变化，不需要为升级更改存取参数。现有实现只存入存活且耐久不满的行囊促织，要求蛰室槽里已有罐。存入和取出都会由原生清零 `CricketRegen`；需要多月回复的罐若每月自动取回，不能累计回复进度，可关闭“自动取回本次放入的促织”让其持续存放。
