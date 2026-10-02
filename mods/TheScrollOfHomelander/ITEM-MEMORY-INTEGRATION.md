# 物品记忆功能合入（2026-10-02）

来源：用户提供的 `Downloads/3749026190/3749026190` 改进包。
前端 DLL SHA256：`6C90CA15FA02835927586B727A2413FAAD69E4B9268EA62698EDAA5C35D0954E`。

包内源码只有 `ContainerCompactPatches.cs` 有实质变化，加入了按页面记忆
详细/简略模式的 PlayerPrefs 实现。反查前端 DLL 发现另一个仅存在于 DLL 的
改动：筛选恢复时从调用 `RefreshList` 改为调用 `OnSortAndFilterChanged`。
本次按功能合入，未覆盖其他源码、配置值、资产或采用对方编译产物。

## 实现与修复

- 显示模式记忆使用现有 `MemoryOptimizationSettings.json`，新增可选的
  `ContainerCardModes` 字段，旧 JSON 经 Normalize 兼容。默认仍为卡片视图，
  用户选择详细列表后重新进入不再强制切回卡片。
- 角色物品页的主列表和已选列表共用模式；交易页按具体界面类型及 self/target
  分别记忆，避免双方模式互相覆盖。
- Init 及自动恢复期间不记录模式，Finalizer 保证异常路径释放初始化保护。
  只有模式变化才提交现有异步保存队列，保存快照深拷贝新增条目。
- 兼容读取贡献版本的 `BTS_CardMode_*` PlayerPrefs，合法值首次读入 JSON；
  不再使用 `PlayerPrefs.Save()` 同步写盘。
- 修复旧 `cardScroll` 字段名，使用当前游戏的 `groupedCardScroll`。
- 保留项目已有分类筛选记忆、刷新合并及延后一帧恢复流程。修正反射调用
  `RefreshList(bool keepSelectedIndex = false)` 时遗漏参数的问题。
  不采用贡献版本的父界面回调替换，避免仓库异步缓存尚未就绪时触发回调。
- 保持两个既有配置键及默认值：`container_default_card_mode` 和
  `memory_filter_enabled`，只更新功能描述，版本号未改变。

## 当前游戏 API 证据

前端 `Assembly-CSharp.dll` SHA256：
`958571B822591C6EA3CD7AFB766A152A8A79AFC7162734FFA471558DB868C11A`。
通过本仓库 DecompilerWorker 查询并检查声明类型及调用流程。

| 类型 | 成员 | Token | 用途 |
| --- | --- | --- | --- |
| `Game.Components.ListStyleGeneralScroll.Item.ItemListScroll` | `RefreshList(System.Boolean)` | `0x0600AE0E` | 私有反射调用；必须传入可选参数 |
| 同上 | `SwitchCardModeToggle(System.Int32,System.Int32)` | `0x0600AE33` | 既有 Postfix 记录用户模式 |
| `Game.Views.Exchange.ExchangeContainer` | `AddSwitchToggleListener()` | `0x06007995` | 既有 Prefix/Postfix 恢复双方切换按钮 |

`RefreshList` 经 `ApplySortAndFilter` 调用控制器 `GenerateFilter`，后者从 UI
重建有效筛选状态，因此不需要调用父界面回调。无初始化对象的 RefreshList
会立即返回。`CToggleGroup.Init` 会读取现有按钮状态，恢复按钮继续使用无通知设置。

## 验证与人工验收

Windows 正式双端构建通过，前端 0 错误/201 警告，后端 0 错误/141 警告。
`tools/ItemMemoryChecks` 的九项检查通过，实际加载新 DLL 和当前游戏程序集，
验证参数调用、字段、默认值、双方独立记忆、快照隔离及重复选择去重。
生成当前源码/游戏程序集/产物 SHA256 清单；未部署、未启动游戏。

用户部署后完全退出并重启游戏，再验收：

1. 物品页切到详细或简略，关闭重开、物品增减后保持模式及按钮状态。
2. 选择书籍、材料等分类并设置详细筛选；物品增减、使用、交易后检查按钮
   与实际内容一致；清除筛选后应记住“全部”。
3. 仓库、商店及其他交易页两侧选择不同显示模式，重开后应分别恢复；
   切换到不同交易页时不应互相覆盖。
4. 首次打开仓库、快速关闭重开及空列表不报异常；旧设置可继续加载。
5. 关闭相关配置开关后不再执行模式或筛选记忆，开启后继续恢复。

编译及独立检查不代表游戏内 UI 已验收。
