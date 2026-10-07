# 同道商人过月采购回归

运行 `dotnet run --project tools/MerchantPurchaseChecks -c Release`。

此程序直接编译并执行正式 `AutoMonthlyPurchasePatches.cs`，仅以假领域替代游戏运行环境；不启动游戏，不加载游戏存档，不读取或写入 Mod 用户设置。覆盖多商人遍历、首店无货或失败、重物与真实满载区别、资金限量、交易回调改变同道名单、离队重新校验、关闭功能，以及超好感额度预览与基线隔离。

假领域验证自动采购的控制流与提交条件，不能替代当前游戏程序集的正式双端编译、Harmony 目标检查或用户游戏内验收。

本次修复依据 2026-10-07 安装的游戏后端程序集，由仓库 `TaiwuStudio.DecompilerWorker` 直接读取。`GameData.dll` SHA256 为 `2C4748B858E4F4F8B481F0B0A17CDBC13606357C7DE5968340716D77D13C3206`，后端 `GameData.Shared.dll` 为 `0B10038F4A89016254059F4988F6BE12BBA5FF5FF0DC2A37C051826CB90C6106`。

| 后端成员 | DLL / token | 核实的行为 |
| --- | --- | --- |
| `WorldDomain.AdvanceMonth(DataContext)` | GameData / `0x0600619D` | 保留原有过月 Postfix 入口 |
| `TaiwuDomain.GetMerchantType(int)` | GameData / `0x0600561F` | 返回村民岗位指定类型，7 包括自行决定和无岗位；不能作为实际商人身份 |
| `ExtraDomain.TryGetMerchantCharToType(int, out sbyte)` | GameData / `0x06006BE1` | 当前实际商人身份映射 |
| `Character.ChangeMerchantType(DataContext, OrganizationInfo)` | GameData / `0x060079C8` | 原生组织商人与指定/自行决定村民商人均写入身份映射 |
| `MerchantDomain.GetOrCreateMerchantTemplateId(DataContext, int)` | GameData / `0x06002878` | 映射缺失时原生验证身份并注册；不生成商品 |
| `TaiwuDomain.GetShopDisplayData(DataContext, OpenShopEventArguments)` | GameData / `0x06005451` | 债务基线和交易预览直接引用同一个领域对象 |
| `TaiwuDomain.ConfirmShopExchange(DataContext, ShopExchange)` | GameData / `0x06005453` | 结算前不校验资金、不刷新购买债务 |
| `MerchantOverFavorData.Clone()` | GameData.Shared / `0x06003571` | 深复制各层购买额度 |
| `ShopExchange.RefreshDebt()` | GameData.Shared / `0x0600309C` | 按所选商品更新购买额度，原版前端提交前执行 |
| `ShopExchange.get_CanConfirmExchange()` | GameData.Shared / `0x0600308B` | 原生资金和购买额度校验 |
| `ShopExchange.CalcBaseValue(ITradeableContent)` | GameData.Shared / `0x0600308D` | 采购单价；Exchange 总价为此单价乘数量 |

修复前源码在相同假领域运行首项回归时只能购买商人 11；商人 12 的指定类型为自行决定、商人 13 没有村民营业岗位，尽管两人的实际商人映射有效。修复后 14 项检查全部通过。
