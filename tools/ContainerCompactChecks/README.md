# 容器紧凑化离线检查

用 .NET 8 加载当前前端游戏程序集和本次构建的前端插件，不安装 Harmony 补丁，不调用插件初始化，不启动游戏，不读取或写入 Mod 用户配置。

```powershell
dotnet run --project tools/ContainerCompactChecks -c Release -- --game-libs 'C:/Program Files (x86)/Steam/steamapps/common/The Scroll Of Taiwu/The Scroll of Taiwu_Data/Managed' --plugin 'mods/TheScrollOfHomelander/Plugins/Front/BetterTaiwuScrollFrontend.dll'
```

两个路径也可以作为位置参数。工具输出游戏与插件 SHA256；成功返回 0，断言失败返回 1，缺少参数返回 2。

检查覆盖：

- 当前 `CardStyleGroupedScroll`、`CombatSkillSelect`、`EquipCombatSkillGroupedSkillScroll` 的 `LoopVerticalScrollRect`、各自分组行模板、列数与高度字段；实际 Mod 缓存的 owner/row 类型、列表字段、`FieldInfo` 与重建/刷新 `MethodInfo` 对应当前 DLL 的成员。
- 三个新增补丁类的十个目标均可解析，Prefix/Postfix 的实例和具名参数类型与当前 API 一致。只枚举反射目标，不执行补丁安装。
- 可用宽度、单卡宽度、间距、padding、列数对横向尺寸的影响；不会放大；重复输入不会累乘缩小；无效尺寸保留原生布局。
- 焦点遮罩重新挂接后的缩放保留物品卡视觉尺寸，补偿全局 UI 与各轴父级缩放；父级任一轴接近零时返回原始缩放。尺寸观察器不是 Harmony 补丁，不增加补丁目标。
- 在 `RuntimeHelpers.GetUninitializedObject` 建立的对象上执行经 DecompilerWorker 确认为纯托管的 `GetItemsSize` 与功法 `BuildFlatRows`。验证标题和内容高度、末行、分组边界、数据完整性、选择模板映射及刷新清空。

这些检查证明当前程序集接口、目标签名和托管数据计算兼容。Unity 场景里的实际视觉布局、池行回收、点击/tooltip 位置和开关恢复仍需用户完全退出并重启游戏后验收。

首次验证的当前 `Assembly-CSharp.dll` SHA256：`23707C99B383B486B03C36A475C8665E1629C5745EE53343FC2B447923938092`。接口证据由工作区 `TaiwuStudio.DecompilerWorker --command find-type/decompile-type/decompile-member` 查询：物品卡行 `0x02000F0C`、物品卡 owner `0x02000F10`、功法选择 owner `0x02000FF4`、装备功法 owner `0x02000B78`；三个 `GetItemsSize(int,int)` token 分别为 `0x0600ADDE`、`0x0600B76B`、`0x06008C9B`。Token 仅用于记录首次证据，检查始终解析传入的当前 DLL。

执行原生方法前阅读的 `Assembly-CSharp.dll` 反编译源码范围：`CardStyleGroupedScroll.GetItemsSize(int,int)` 第 461–469 行、`CombatSkillSelect.GetItemsSize(int,int)` 第 587–595 行、`EquipCombatSkillGroupedSkillScroll.GetItemsSize(int,int)` 第 227–235 行；功法选择 `BuildFlatRows(List<SkillSection<CombatSkillDisplayDataForList>>)` token `0x0600B760` 第 340–387 行、装备功法 `BuildFlatRows(List<SkillSection<CombatSkillDisplayDataCharacterMenuListItem>>)` token `0x06008C95` 第 93–134 行。这些范围没有 Unity 场景访问或配置写盘。
