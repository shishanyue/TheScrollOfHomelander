#nullable disable

using System.Collections.Generic;
using LangSet = BetterTaiwuScroll.Frontend.ModLocalization.LangSet;

namespace BetterTaiwuScroll.Frontend;

/// <summary>
/// The single source of truth for all of the mod's translations.
///
/// * <see cref="ModText"/>  — text the MOD authors and displays (panel titles, buttons,
///   toggle labels, dropdown options, tooltips). English is the key; each entry lists the
///   other languages. To add a language, pass it to the <c>LangSet</c> (e.g. <c>ko: "..."</c>).
///   Looked up via <c>ModLocalization.T("English text")</c>. Missing translations fall
///   back to the English key.
///
/// * <see cref="GameLabels"/> — text the GAME renders that the mod matches against (sort
///   columns, filter names, archive headers). Keyed by what the game shows in Chinese, with
///   the value being what the same element shows in English (verbatim from the game's own
///   Language_EN files). Used only to recognize game UI in any client language, never shown.
///
/// Nothing else in the codebase should contain a hard-coded, user-visible English string.
/// </summary>
internal static class ModLocalizationCatalog
{
    // Exact alternative labels from the installed game's language packs.
    internal static readonly Dictionary<string, string[]> GameLabelVariants = new()
    {
        ["医术"] = new[] { "Medical Art", "醫術", "의술" },
        ["毒术"] = new[] { "Toxicology", "毒術", "독술" },
        ["术数"] = new[] { "Astrology", "術數", "술법" },
        ["杂学"] = new[] { "Unorthodox Arts", "雜學", "잡학" },
        ["数量"] = new[] { "Quantity", "數量", "수량" },
        ["技艺"] = new[] { "Fine Arts", "技藝", "기예" },
        ["定力"] = new[] { "Willpower", "定力", "정신력" },
        ["膂力"] = new[] { "Strength", "膂力", "완력" },
        ["状态"] = new[] { "State", "狀態", "상태" },
        ["暗器"] = new[] { "Concealed Weapons", "暗器", "암기" },
        ["木材"] = new[] { "Wood", "木材", "목재" },
        ["类型"] = new[] { "Type", "類型", "유형" },
        ["行为"] = new[] { "(Running script...)", "行為", "행위" },
        ["灵敏"] = new[] { "Agility", "靈敏", "민첩" },
        ["体质"] = new[] { "Constitution", "體質", "체질" },
        ["根骨"] = new[] { "Root Bone", "根骨", "근골" },
        ["悟性"] = new[] { "Comprehension", "悟性", "지력" },
        ["合道"] = new[] { "Harmony", "合道", "합도" },
        ["魅力"] = new[] { "Charm", "魅力", "매력" },
        ["重量"] = new[] { "Weight", "重量", "중량" },
        ["耐久"] = new[] { "Durability", "耐久", "내구" },
        ["音律"] = new[] { "Music", "音律", "음률" },
        ["弈棋"] = new[] { "Weiqi", "弈棋", "기성" },
        ["诗书"] = new[] { "Literature", "詩書", "서예" },
        ["绘画"] = new[] { "Painting", "繪畫", "회화" },
        ["品鉴"] = new[] { "Appreciation", "品鑑", "감정" },
        ["锻造"] = new[] { "Smithing", "鍛造", "단조" },
        ["制木"] = new[] { "Carpentry", "製木", "목공" },
        ["织锦"] = new[] { "Weaving", "織錦", "직금" },
        ["巧匠"] = new[] { "Jewelcrafting", "巧匠", "명장" },
        ["道法"] = new[] { "Taoism", "道法", "도교" },
        ["佛学"] = new[] { "Buddhism", "佛學", "불교" },
        ["厨艺"] = new[] { "Culinary Arts", "廚藝", "요리" },
        ["内功"] = new[] { "Qi Arts", "內功", "내공" },
        ["身法"] = new[] { "Footwork Arts", "身法", "신법" },
        ["绝技"] = new[] { "Unique Arts", "絕技", "비기" },
        ["拳掌"] = new[] { "Fist Arts", "拳掌", "권법" },
        ["指法"] = new[] { "Finger Arts", "指法", "지법" },
        ["腿法"] = new[] { "Kicking Arts", "腿法", "각법" },
        ["剑法"] = new[] { "Sword Arts", "劍法", "검법" },
        ["刀法"] = new[] { "Blade Arts", "刀法", "도법" },
        ["长兵"] = new[] { "Polearm Arts", "長兵", "창술", "Polearm" },
        ["奇门"] = new[] { "Exotic Weapons", "奇門", "기문" },
        ["软兵"] = new[] { "Whip Arts", "軟兵", "편술" },
        ["御射"] = new[] { "Ranged Weapons", "御射", "궁술" },
        ["乐器"] = new[] { "Instrument Arts", "樂器", "악기" },
        ["年龄"] = new[] { "Age", "年齡", "연령" },
        ["健康"] = new[] { "Health", "健康", "건강" },
        ["伤势"] = new[] { "Injuries", "傷勢", "부상" },
        ["立场"] = new[] { "Mindset", "立場", "입장" },
        ["心情"] = new[] { "Mood", "心情", "기분" },
        ["好感"] = new[] { "Favorability", "好感", "호감", "호감도" },
        ["轮回"] = new[] { "Reincarnations", "輪迴", "윤회" },
        ["名誉"] = new[] { "Reputation", "名譽", "명예" },
        ["成长"] = new[] { "Growth", "成長", "성장" },
        ["食材"] = new[] { "Foodstuff", "食材", "식재" },
        ["金铁"] = new[] { "Metal", "金鐵", "금철" },
        ["玉石"] = new[] { "Jade", "玉石", "옥석" },
        ["织物"] = new[] { "Fabric", "織物", "직물" },
        ["药材"] = new[] { "Herbs", "藥材", "약재" },
        ["银钱"] = new[] { "Silver", "銀錢", "은전" },
        ["威望"] = new[] { "Prestige", "威望", "명성" },
        ["行囊"] = new[] { "Travel Bag", "行囊", "행낭" },
        ["负重"] = new[] { "Weight", "負重", "부중" },
        ["身份"] = new[] { "Profession", "身分", "신분", "Rank" },
        ["属性"] = new[] { "Attributes", "屬性", "속성" },
        ["命中"] = new[] { "Hit Attributes", "命中", "명중", "Hit" },
        ["武学"] = new[] { "Martial Arts", "武學", "무학" },
        ["赋性"] = new[] { "Dispositions", "賦性", "천성" },
        ["持有"] = new[] { "Inventory", "持有", "배낭" },
        ["指令"] = new[] { "Commands", "指令", "지령" },
        ["戒心"] = new[] { "Wariness", "戒心", "경계심" },
        ["造诣"] = new[] { "Attainment", "造詣", "조예" },
        ["西域珍宝"] = new[] { "Western Regions Treasure", "西域珍寶", "서역 귀보" },
        ["价值"] = new[] { "Value", "價值", "가치" },
        ["价格"] = new[] { "Price", "價格", "가격" },
        ["名称"] = new[] { "Name", "名稱", "이름" },
        ["培养次数"] = new[] { "Training Count", "培養次數", "육성 횟수" },
        ["功法造诣"] = new[] { "Martial Art Attainment", "功法造詣", "공법조예", "공법 조예" },
        ["所在地点"] = new[] { "Location", "所在地點", "위치", "현재 위치" },
        ["名字"] = new[] { "Name", "名字", "이름" },
        ["工具效果"] = new[] { "Tool Effects", "工具效果", "공구 효과" },
        ["效率"] = new[] { "Efficiency", "效率", "효율" },
        ["内息紊乱"] = new[] { "Inner Breath Chaos", "內息紊亂", "내식 혼란" },
        ["品阶"] = new[] { "Tier", "品階", "품계" },
        ["效果"] = new[] { "Effects", "效果", "효과" },
        ["技艺书"] = new[] { "Fine Arts Book", "技藝書", "기예서" },
        ["功法书"] = new[] { "Martial Arts Book", "功法書", "공법서" },
        ["饲槽"] = new[] { "Trough", "飼槽", "사료통" },
        ["头像"] = new[] { "Avatar", "頭像", "프로필" },
        ["第几世"] = new[] { "Generation Number", "第幾世", "몇 세대" },
        ["存档时间"] = new[] { "Save Time", "存檔時間", "저장 시점" },
    };

    internal static readonly Dictionary<string, LangSet> ModText = new()
    {
        // ── Continuous-crafting: panel + controls ────────────────────────────────
        ["Continuous Crafting"] = new(cn: "连续制作"),
        ["Continuous Crafting Settings"] = new(cn: "连续制作设置"),
        ["Batch Crafting"] = new(cn: "批量制作"),
        ["Stop Crafting"] = new(cn: "停止制作"),
        ["Settings"] = new(cn: "设置"),
        ["Include Travel Bag"] = new(cn: "是否包括行囊"),
        ["Include Private Storage"] = new(cn: "是否包括私库"),
        ["Include Public Storage"] = new(cn: "是否包括公库"),
        ["Highest Reagent Tier Allowed"] = new(cn: "允许使用的最高引子品级"),
        ["Lowest Reagent Tier Allowed"] = new(cn: "允许使用的最低引子品级"),
        ["Preferred Tool Tier"] = new(cn: "优先使用工具的品级"),
        ["Batch Crafting Mode"] = new(cn: "批量制作模式"),
        ["Allow Bare-Hand Crafting"] = new(cn: "是否允许徒手制作"),
        ["Enable Durability Protection"] = new(cn: "是否开启耐久保护"),
        ["Batch Crafting Speed"] = new(cn: "批量制作速度"),
        ["High Tier"] = new(cn: "高品"),
        ["Low Tier"] = new(cn: "低品"),
        ["Checkbox Mode"] = new(cn: "勾选模式"),
        ["Button Mode"] = new(cn: "按钮模式"),

        // Continuous-crafting tooltips (title + description shown on hover)
        ["When checked, crafting continues to the next batch per your settings once complete."] =
            new(cn: "勾选后，制作完成时将按照设置继续进行下一次制作。"),
        ["Open the continuous-crafting settings panel."] = new(cn: "打开连续制作的设置界面。"),
        ["Batch-craft from the current recipe using your continuous-crafting settings."] =
            new(cn: "按照连续制作设置从当前制作开始批量制作。"),

        // ── Bulk purchase: panel + controls ──────────────────────────────────────
        ["Bulk Purchase"] = new(cn: "批量采购"),
        ["Bulk Purchase Settings"] = new(cn: "批量采购设置"),
        ["Lowest Purchase Tier"] = new(cn: "采购的最低品级"),
        ["Highest Purchase Tier"] = new(cn: "采购的最高品级"),
        ["Skip Price-Increased Items"] = new(cn: "不采购涨价的物品"),
        ["Skip Original-Price Items"] = new(cn: "不采购原价的物品"),
        ["Buy from Locked Pure-Essence Shops"] = new(cn: "未解锁精纯商店也采购"),
        ["Bulk-Buy Medicine Reagents"] = new(cn: "批量采购购买药材引子"),
        ["Bulk-Buy Poison Reagents"] = new(cn: "批量采购购买毒物引子"),
        ["Open the bulk purchase settings."] = new(cn: "打开批量采购设置。"),
        ["Add matching goods to the buy list per your settings."] = new(cn: "按照设置把符合条件的商品加入买入列表。"),

        // ── Exchange filter sync ─────────────────────────────────────────────────
        ["Sync"] = new(cn: "同步"),
        ["Sync Both Sides"] = new(cn: "左右同步"),
        ["When enabled, item-category filters stay in sync on both sides."] =
            new(cn: "开启此功能时，物品大分类的筛选会同步到两边"),

        // ── Search boxes ─────────────────────────────────────────────────────────
        ["Enter keyword"] = new(cn: "输入关键字"),

        // ── Make: extra "Food" target category the mod adds ──────────────────────
        ["Food"] = new(cn: "食物"),
        ["Meat and Vegetables"] = new(cn: "荤素"),
        ["Auto Repair Settings"] = new(cn: "自动修理设置"),
        ["Enable Auto Repair"] = new(cn: "是否开启自动修理"),
        ["Bare-Hand Repair Only"] = new(cn: "仅使用徒手修理"),
        ["Allow Bare-Hand Repair"] = new(cn: "允许徒手修理"),
        ["Chicken Care Settings"] = new(cn: "自动饲养元鸡设置"),
        ["Automatic Chicken Care"] = new(cn: "自动饲养元鸡"),
        ["Minimum Feeding Favorability"] = new(cn: "最低饲养好感度"),
        ["Prefer Feeding Trough"] = new(cn: "优先使用饲槽"),
        ["Allow Warehouse Items"] = new(cn: "允许使用库房"),
        ["Allow Travel Bag Items"] = new(cn: "允许使用行囊"),
        ["Cricket Storage Settings"] = new(cn: "自动存放促织设置"),
        ["Automatic Cricket Storage"] = new(cn: "自动存放促织"),
        ["Retrieve Automatically Stored Crickets"] = new(cn: "自动取回本次放入的促织"),
        ["Also Retrieve Previously Stored Crickets"] = new(cn: "同时取回玩家原先放置的促织"),
        ["Auto-Purchase from Merchant Companions After Month Advance"] = new(cn: "过月自动购买商人同道"),
        ["Default Storage"] = new(cn: "默认保存位置"),
        ["Auto-Purchase Only on Industry Tiles"] = new(cn: "仅在产业地块过月自动购买"),
        ["Protect Travel Bag Capacity"] = new(cn: "行囊负重保护"),
        ["Travel Bag Load Limit (%)"] = new(cn: "行囊负重阈值（百分比）"),
        ["Skip Travel Bag Purchases Outside Industry Tiles"] = new(cn: "不在产业地块不购入行囊"),
        ["Travel Bag"] = new(cn: "行囊"),
        ["Private Storage"] = new(cn: "私库"),
        ["Public Storage"] = new(cn: "公库"),
        ["Hide"] = new(cn: "隐藏"),
        ["Restore"] = new(cn: "恢复"),
        ["Owned"] = new(cn: "已拥有"),

        // ── Grade names (crafting/purchase dropdown options), Tier 1 (best) .. 9 ──
        // Two Chinese spellings exist: dotted (continuous-crafting panel) and plain
        // (bulk-purchase panel). Both map to the same English tier here.
        ["Tier 1"] = new(cn: "神·一品"),
        ["Tier 2"] = new(cn: "绝·二品"),
        ["Tier 3"] = new(cn: "超·三品"),
        ["Tier 4"] = new(cn: "极·四品"),
        ["Tier 5"] = new(cn: "秘·五品"),
        ["Tier 6"] = new(cn: "奇·六品"),
        ["Tier 7"] = new(cn: "上·七品"),
        ["Tier 8"] = new(cn: "中·八品"),
        ["Tier 9"] = new(cn: "下·九品"),
    };

    /// <summary>
    /// Plain (dot-less) Chinese grade spelling used by the bulk-purchase panel, indexed
    /// Tier 1 (best) .. Tier 9. The continuous-crafting panel uses the dotted spelling in
    /// <see cref="ModText"/>. Both surface as "Tier N" in English.
    /// </summary>
    internal static readonly string[] PlainGradeChinese =
    {
        "神一品", "绝二品", "超三品", "极四品", "秘五品", "妙六品", "上七品", "中八品", "下九品",
    };

    internal static readonly Dictionary<string, string> GameLabels = new()
    {
        // ── Item sort-column labels (game's own wording, from SortItem_language) ──
        ["名称"] = "Name",
        ["品阶"] = "Tier",
        ["数量"] = "Quantity",
        ["类型"] = "Type",
        ["重量"] = "Weight",
        ["耐久"] = "Durability",
        ["效率"] = "Efficiency",
        ["好感"] = "Favorability",
        ["功法造诣"] = "Martial Art Attainment",
        ["价值"] = "Value",
        ["价格"] = "Price",
        ["造诣"] = "Attainment",
        ["工具效果"] = "Tool Effects",
        ["效果"] = "Effects",
        ["年龄"] = "Age",
        ["心情"] = "Mood",
        ["健康"] = "Health",
        ["状态"] = "State",
        ["属性"] = "Attribute",
        ["命中"] = "Hit",
        ["技艺"] = "Fine Arts",
        ["武学"] = "Martial Arts",
        ["赋性"] = "Disposition",
        ["持有"] = "Inventory",
        ["指令"] = "Command",
        ["培养次数"] = "Training Count",
        ["培养"] = "Training",

        // ── Team/character sort-column labels ────────────────────────────────────
        ["行为"] = "Behavior",
        ["身份"] = "Rank",
        ["伤势"] = "Injuries",
        ["内息紊乱"] = "Inner Breath Chaos",
        ["魅力"] = "Charm",
        ["立场"] = "Mindset",
        ["戒心"] = "Wariness",
        ["轮回"] = "Reincarnations",
        ["名誉"] = "Reputation",
        ["膂力"] = "Strength",
        ["体质"] = "Constitution",
        ["灵敏"] = "Agility",
        ["根骨"] = "Root Bone",
        ["悟性"] = "Comprehension",
        ["定力"] = "Willpower",
        ["音律"] = "Music",
        ["弈棋"] = "Weiqi",
        ["诗书"] = "Literature",
        ["绘画"] = "Painting",
        ["术数"] = "Astrology",
        ["品鉴"] = "Appreciation",
        ["锻造"] = "Smithing",
        ["制木"] = "Carpentry",
        ["医术"] = "Medical Art",
        ["毒术"] = "Toxicology",
        ["织锦"] = "Weaving",
        ["巧匠"] = "Jewelcrafting",
        ["道法"] = "Taoism",
        ["佛学"] = "Buddhism",
        ["厨艺"] = "Culinary Arts",
        ["杂学"] = "Unorthodox Arts",
        ["合道"] = "Harmony",
        ["成长"] = "Growth",
        ["食材"] = "Foodstuff",
        ["木材"] = "Wood",
        ["金铁"] = "Metal",
        ["玉石"] = "Jade",
        ["织物"] = "Fabric",
        ["药材"] = "Herbs",
        ["银钱"] = "Silver",
        ["威望"] = "Prestige",
        ["负重"] = "Weight",
        ["行囊"] = "Travel Bag",
        ["内功"] = "Qi Arts",
        ["身法"] = "Footwork Arts",
        ["绝技"] = "Unique Arts",
        ["拳掌"] = "Fist Arts",
        ["指法"] = "Finger Arts",
        ["腿法"] = "Kicking Arts",
        ["暗器"] = "Concealed Weapons",
        ["剑法"] = "Sword Arts",
        ["刀法"] = "Blade Arts",
        ["长兵"] = "Polearm Arts",
        ["奇门"] = "Exotic Weapons",
        ["软兵"] = "Whip Arts",
        ["御射"] = "Ranged Weapons",
        ["乐器"] = "Instrument Arts",

        // ── Item-category filter names (game rendered) ───────────────────────────
        ["功法书"] = "Martial Arts Book",
        ["技艺书"] = "Fine Arts Book",
        ["西域珍宝"] = "Western Regions Treasure",

        // ── Revert-archive header row (hidden by the cloned settings panels) ──────
        ["头像"] = "Avatar",
        ["名字"] = "Name",
        ["第几世"] = "Generation Number",
        ["第几个"] = "Generation Number",
        ["存档时间"] = "Save Time",
        ["所在地点"] = "Location",
    };
}
