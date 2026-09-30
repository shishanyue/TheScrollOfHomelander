#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Game.Components.SortAndFilter;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterTaiwuScroll.Frontend;

// Long item-category filter names shortened to fit inline buttons. Keyed by the game's
// rendered text in each language (Chinese on a CN client, English on an EN client).
internal static class FilterLabelAbbreviations
{
    private static readonly Dictionary<string, string> Map = new()
    {
        ["功法书"] = "功法",
        ["技艺书"] = "技艺",
        ["西域珍宝"] = "西域",
        ["Martial Arts Book"] = "Martial Arts",
        ["Fine Arts Book"] = "Fine Arts",
        ["Western Regions Treasure"] = "Western",
    };

    internal static bool TryShorten(string text, out string shortened)
    {
        return Map.TryGetValue((text ?? string.Empty).Trim(), out shortened);
    }

    internal static string Shorten(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return Map.TryGetValue(trimmed, out var shortened) ? shortened : trimmed;
    }
}

[HarmonyPatch(typeof(Game.Views.Exchange.ViewExchangeBase), "OnSelfItemRender")]
internal static class ExchangeSelfItemTextColorPatch
{
    private static void Postfix(
        Game.Views.Exchange.ViewExchangeBase __instance,
        GameData.Domains.Item.Display.ITradeableContent itemData,
        Game.Components.ListStyleGeneralScroll.Item.RowItemLine rowItemLine)
    {
        ExchangeItemTextColorHelper.Apply(__instance, rowItemLine);
    }
}

[HarmonyPatch(typeof(Game.Views.Exchange.ViewExchangeBase), "OnTargetItemRender")]
internal static class ExchangeTargetItemTextColorPatch
{
    private static void Postfix(
        Game.Views.Exchange.ViewExchangeBase __instance,
        GameData.Domains.Item.Display.ITradeableContent itemData,
        Game.Components.ListStyleGeneralScroll.Item.RowItemLine rowItemLine)
    {
        ExchangeItemTextColorHelper.Apply(__instance, rowItemLine);
    }
}

[HarmonyPatch(typeof(Game.Views.Exchange.ViewExchangeBase), "OnExchangingTargetItemRender")]
internal static class ExchangeTargetExchangedItemTextColorPatch
{
    private static void Postfix(
        Game.Views.Exchange.ViewExchangeBase __instance,
        GameData.Domains.Item.Display.ITradeableContent itemData,
        Game.Components.ListStyleGeneralScroll.Item.RowItemLine rowItemLine)
    {
        ExchangeItemTextColorHelper.Apply(__instance, rowItemLine);
    }
}

internal static class ExchangeItemTextColorHelper
{
    private static readonly Color ItemInfoTextColor = new(1f, 1f, 1f, 1f);

    internal static void Apply(
        Game.Views.Exchange.ViewExchangeBase view,
        Game.Components.ListStyleGeneralScroll.Item.RowItemLine rowItemLine)
    {
        if (!IsEnabledFor(view) || rowItemLine == null)
        {
            return;
        }

        var cache = rowItemLine.GetComponent<ExchangeItemTextColorCache>()
            ?? rowItemLine.gameObject.AddComponent<ExchangeItemTextColorCache>();

        foreach (var group in cache.CanvasGroups)
        {
            if (group != null && group.alpha < 1f)
                group.alpha = 1f;
        }

        foreach (var text in cache.Texts)
        {
            if (text == null)
                continue;

            var color = text.color;
            var wasOverrideColorTags = text.overrideColorTags;
            if (wasOverrideColorTags)
                text.overrideColorTags = false;
            if (!Mathf.Approximately(text.alpha, 1f))
                text.alpha = 1f;
            if (!Mathf.Approximately(text.canvasRenderer.GetAlpha(), 1f))
                text.canvasRenderer.SetAlpha(1f);
            if (wasOverrideColorTags || IsDimNeutralText(color))
            {
                if (text.color != ItemInfoTextColor)
                    text.color = ItemInfoTextColor;
            }
            else if (color.a < 1f)
                text.color = new Color(color.r, color.g, color.b, 1f);
        }
    }

    private static bool IsEnabledFor(Game.Views.Exchange.ViewExchangeBase view)
    {
        return view switch
        {
            Game.Views.Exchange.ViewShop => Plugin.EnableShopSelfItemTextColor,
            Game.Views.Exchange.ViewShopGift => Plugin.EnableShopGiftSelfItemTextColor,
            Game.Views.Exchange.ViewSettlementShop => Plugin.EnableSettlementShopSelfItemTextColor,
            Game.Views.Exchange.ViewWarehouse => Plugin.EnableWarehouseSelfItemTextColor,
            Game.Views.Exchange.ViewExchangeBook => Plugin.EnableExchangeBookSelfItemTextColor,
            Game.Views.Exchange.ViewExchange => Plugin.EnableExchangeSelfItemTextColor,
            _ => false,
        };
    }

    private static bool IsDimNeutralText(Color color)
    {
        var max = Mathf.Max(color.r, color.g, color.b);
        var min = Mathf.Min(color.r, color.g, color.b);
        return max - min < 0.08f && max < 0.9f;
    }
}

internal sealed class ExchangeItemTextColorCache : MonoBehaviour
{
    private CanvasGroup[] _canvasGroups;
    private TMP_Text[] _texts;

    internal CanvasGroup[] CanvasGroups => _canvasGroups ??= GetComponentsInChildren<CanvasGroup>(true);

    internal TMP_Text[] Texts => _texts ??= GetComponentsInChildren<TMP_Text>(true);
}

[HarmonyPatch(typeof(Game.Components.SortAndFilter.SortButtonGroup), "RefreshAll")]
internal static class CompactItemSortButtonGroupPatch
{
    private const float CompactButtonWidth = 112f;
    private const float CompactButtonHeight = 48f;
    private const float CompactSpacing = 4f;
    private const float CompactTopGap = 8f;
    private const float CompactTopGapWithSummary = 2f;
    private const float CompactBottomGap = 12f;
    private const float MinButtonWidth = 44f;
    private const float MaxButtonWidth = 112f;
    private const float AncestorWidthMargin = 24f;
    private const int PreferSingleRowMaxButtonCount = 8;

    private static readonly FieldInfo ItemRootField =
        AccessTools.Field(typeof(Game.Components.SortAndFilter.SortButtonGroup), "itemRoot");

    private static readonly FieldInfo FilterSummaryAreaField =
        AccessTools.Field(typeof(Game.Components.SortAndFilter.SortAndFilter), "filterSummaryArea");

    // Recognized by matching the game's rendered sort-column labels. Built to contain both
    // the Chinese labels and the English text the game shows, so recognition works on any client.
    private static readonly HashSet<string> ItemSortLabels = ModLocalization.BuildBilingualLabelSet(new[]
    {
        "名称",
        "品阶",
        "数量",
        "类型",
        "重量",
        "耐久",
        "效率",
        "好感",
        "功法造诣",
        "价值",
        "价格",
        "造诣",
        "工具效果",
        "效果",
        "年龄",
        "心情",
        "健康",
        "状态",
        "属性",
        "命中",
        "技艺",
        "武学",
        "赋性",
        "持有",
        "指令",
        "培养次数",
        "培养",
    });

    private static readonly HashSet<string> TeamSortLabels = ModLocalization.BuildBilingualLabelSet(new[]
    {
        "名称",
        "品阶",
        "行为",
        "赋性",
        "身份",
        "年龄",
        "健康",
        "伤势",
        "内息紊乱",
        "魅力",
        "立场",
        "心情",
        "好感",
        "戒心",
        "轮回",
        "名誉",
        "膂力",
        "体质",
        "灵敏",
        "根骨",
        "悟性",
        "定力",
        "音律",
        "弈棋",
        "诗书",
        "绘画",
        "术数",
        "品鉴",
        "锻造",
        "制木",
        "医术",
        "毒术",
        "织锦",
        "巧匠",
        "道法",
        "佛学",
        "厨艺",
        "杂学",
        "合道",
        "成长",
        "食材",
        "木材",
        "金铁",
        "玉石",
        "织物",
        "药材",
        "银钱",
        "威望",
        "负重",
        "行囊",
        "内功",
        "身法",
        "绝技",
        "拳掌",
        "指法",
        "腿法",
        "暗器",
        "剑法",
        "刀法",
        "长兵",
        "奇门",
        "软兵",
        "御射",
        "乐器",
        "持有",
        "指令",
    });

    // Long sort-column labels shortened to fit compact buttons. Keyed by the game's rendered
    // text in each language (Chinese on a CN client, English on an EN client) -> short form.
    private static readonly Dictionary<string, string> CompactSortLabelShortening = new()
    {
        ["工具效果"] = "效果",
        ["培养次数"] = "培养",
        ["功法造诣"] = "造诣",
        ["Tool Effects"] = "Effects",
        ["Training Count"] = "Training",
        ["Martial Art Attainment"] = "Attainment",
    };

    private static void Postfix(Game.Components.SortAndFilter.SortButtonGroup __instance)
    {
        ApplyIfRecognized(__instance, scheduleDelayed: true);
    }

    internal static bool ApplyIfRecognized(Game.Components.SortAndFilter.SortButtonGroup __instance, bool scheduleDelayed)
    {
        if (__instance == null || !Plugin.EnableCompactSortButtons)
            return false;

        var itemRoot = ItemRootField?.GetValue(__instance) as RectTransform;
        if (itemRoot == null)
            return false;

        var activeCount = GetActiveChildCount(itemRoot);
        var isTeamSortGroup = IsInTeamView(__instance) || LooksLikeTeamSortGroup(itemRoot);
        if (!isTeamSortGroup && !LooksLikeItemSortGroup(itemRoot))
            return false;

        var topGap = HasVisibleFilterSummary(__instance) ? CompactTopGapWithSummary : CompactTopGap;
        var preferSingleRow = isTeamSortGroup || activeCount <= PreferSingleRowMaxButtonCount;
        var availableWidth = GetAvailableWidth(itemRoot, allowFullAncestorWidth: true);
        var signature = BuildLayoutSignature(itemRoot, activeCount, isTeamSortGroup, topGap, preferSingleRow, availableWidth);
        var state = CompactSortButtonLayoutState.GetOrAdd(__instance);
        if (state != null && state.IsApplied(signature))
            return true;

        ApplyCompactLayout(__instance.transform as RectTransform, itemRoot, topGap, preferSingleRow, availableWidth);
        state?.MarkApplied(signature);
        if (scheduleDelayed)
            state?.Schedule();
        return true;
    }

    private static bool LooksLikeItemSortGroup(RectTransform itemRoot)
    {
        var matched = 0;
        for (var i = 0; i < itemRoot.childCount; i++)
        {
            var child = itemRoot.GetChild(i);
            if (child == null || !child.gameObject.activeSelf)
                continue;

            var text = SortButtonTextCache.GetFirstText(child);
            if (!string.IsNullOrEmpty(text) && ItemSortLabels.Contains(text))
                matched++;
        }

        return matched >= Math.Min(3, GetActiveChildCount(itemRoot));
    }

    private static bool LooksLikeTeamSortGroup(RectTransform itemRoot)
    {
        var matched = 0;
        for (var i = 0; i < itemRoot.childCount; i++)
        {
            var child = itemRoot.GetChild(i);
            if (child == null || !child.gameObject.activeSelf)
                continue;

            var text = SortButtonTextCache.GetFirstText(child);
            if (!string.IsNullOrEmpty(text) && TeamSortLabels.Contains(text))
                matched++;
        }

        return matched >= Math.Min(3, GetActiveChildCount(itemRoot));
    }

    private static bool IsInTeamView(Component component)
    {
        return component != null && component.GetComponentInParent<Game.Views.CharacterMenu.Team.ViewCharacterMenuTeam>(true) != null;
    }

    private static void ApplyCompactLayout(RectTransform sortGroupRect, RectTransform itemRoot, float topGap, bool preferSingleRow, float availableWidth)
    {
        var activeCount = GetActiveChildCount(itemRoot);
        var columns = preferSingleRow
            ? CalculateSingleRowColumnCount(availableWidth, activeCount)
            : CalculateColumnCount(availableWidth, activeCount);
        var rows = Mathf.Max(1, Mathf.CeilToInt(activeCount / (float)columns));
        var buttonWidth = CalculateButtonWidth(availableWidth, columns);
        var targetWidth = columns * buttonWidth + CompactSpacing * Math.Max(columns - 1, 0);
        var targetHeight = topGap + rows * CompactButtonHeight + CompactSpacing * Math.Max(rows - 1, 0) + CompactBottomGap;

        if (preferSingleRow)
        {
            if (sortGroupRect != null)
                sortGroupRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
            itemRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
        }
        itemRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);

        if (sortGroupRect != null)
            sortGroupRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);

        var grid = itemRoot.GetComponent<GridLayoutGroup>();
        var horizontal = itemRoot.GetComponent<HorizontalLayoutGroup>();
        var vertical = itemRoot.GetComponent<VerticalLayoutGroup>();
        var useHorizontal = grid == null && horizontal != null && rows == 1;
        var useManualGrid = grid == null && !useHorizontal && (horizontal != null || vertical != null);

        // Unity does not allow two LayoutGroup components on one GameObject. Reuse the
        // original horizontal group for a single row and position children manually when a
        // wrapped layout is required. A GridLayoutGroup is added only to roots with no layout.
        if (grid == null && !useHorizontal && !useManualGrid)
            grid = itemRoot.gameObject.AddComponent<GridLayoutGroup>();

        if (grid != null)
        {
            grid.enabled = true;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.cellSize = new Vector2(buttonWidth, CompactButtonHeight);
            grid.spacing = new Vector2(CompactSpacing, CompactSpacing);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.padding = new RectOffset(grid.padding.left, grid.padding.right, Mathf.RoundToInt(topGap), grid.padding.bottom);
        }

        if (horizontal != null)
        {
            horizontal.enabled = useHorizontal;
            if (useHorizontal)
            {
                horizontal.spacing = CompactSpacing;
                horizontal.childAlignment = TextAnchor.UpperLeft;
                horizontal.padding = new RectOffset(
                    horizontal.padding.left,
                    horizontal.padding.right,
                    Mathf.RoundToInt(topGap),
                    horizontal.padding.bottom);
                horizontal.childControlWidth = true;
                horizontal.childControlHeight = true;
                horizontal.childForceExpandWidth = false;
                horizontal.childForceExpandHeight = false;
            }
        }

        if (vertical != null)
            vertical.enabled = false;

        var rootLayoutElement = itemRoot.GetComponent<LayoutElement>() ?? itemRoot.gameObject.AddComponent<LayoutElement>();
        rootLayoutElement.minWidth = preferSingleRow ? targetWidth : -1f;
        rootLayoutElement.preferredWidth = preferSingleRow ? targetWidth : -1f;
        rootLayoutElement.minHeight = targetHeight;
        rootLayoutElement.preferredHeight = targetHeight;
        rootLayoutElement.flexibleWidth = -1f;
        rootLayoutElement.flexibleHeight = 0f;

        if (sortGroupRect != null)
        {
            var groupLayoutElement = sortGroupRect.GetComponent<LayoutElement>() ?? sortGroupRect.gameObject.AddComponent<LayoutElement>();
            groupLayoutElement.minWidth = preferSingleRow ? targetWidth : -1f;
            groupLayoutElement.preferredWidth = preferSingleRow ? targetWidth : -1f;
            groupLayoutElement.minHeight = targetHeight;
            groupLayoutElement.preferredHeight = targetHeight;
            groupLayoutElement.flexibleWidth = preferSingleRow ? 0f : -1f;
            groupLayoutElement.flexibleHeight = 0f;
        }

        var activeIndex = 0;
        var manualLeft = horizontal?.padding.left ?? vertical?.padding.left ?? 0;
        for (var i = 0; i < itemRoot.childCount; i++)
        {
            var child = itemRoot.GetChild(i) as RectTransform;
            if (child == null)
                continue;

            child.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, buttonWidth);
            child.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, CompactButtonHeight);

            var layoutElement = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
            layoutElement.minWidth = buttonWidth;
            layoutElement.preferredWidth = buttonWidth;
            layoutElement.flexibleWidth = 0f;
            layoutElement.minHeight = CompactButtonHeight;
            layoutElement.preferredHeight = CompactButtonHeight;
            layoutElement.flexibleHeight = 0f;

            if (useManualGrid && child.gameObject.activeSelf)
            {
                var column = activeIndex % columns;
                var row = activeIndex / columns;
                child.anchorMin = new Vector2(0f, 1f);
                child.anchorMax = new Vector2(0f, 1f);
                child.pivot = new Vector2(0f, 1f);
                child.anchoredPosition = new Vector2(
                    manualLeft + column * (buttonWidth + CompactSpacing),
                    -(topGap + row * (CompactButtonHeight + CompactSpacing)));
                activeIndex++;
            }

            foreach (var label in SortButtonTextCache.GetLabels(child))
            {
                if (label == null)
                    continue;

                var text = label.text?.Trim();
                if (!string.IsNullOrEmpty(text) && CompactSortLabelShortening.TryGetValue(text, out var shortened))
                    label.SetText(shortened);

                label.enableAutoSizing = true;
                label.fontSizeMax = Math.Min(label.fontSizeMax <= 0f ? 28f : label.fontSizeMax, 20f);
                label.fontSizeMin = Math.Max(label.fontSizeMin, 12f);
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        UiLayoutRefreshQueue.Request(itemRoot);
        if (sortGroupRect != null)
            UiLayoutRefreshQueue.Request(sortGroupRect);
    }

    private static string BuildLayoutSignature(
        RectTransform itemRoot,
        int activeCount,
        bool isTeamSortGroup,
        float topGap,
        bool preferSingleRow,
        float availableWidth)
    {
        var builder = new StringBuilder(128);
        builder.Append(activeCount).Append('|')
            .Append(isTeamSortGroup ? 1 : 0).Append('|')
            .Append(Mathf.RoundToInt(topGap)).Append('|')
            .Append(preferSingleRow ? 1 : 0).Append('|')
            .Append(Mathf.RoundToInt(availableWidth)).Append('|')
            .Append(itemRoot == null ? 0 : itemRoot.childCount);

        if (itemRoot == null)
            return builder.ToString();

        for (var i = 0; i < itemRoot.childCount; i++)
        {
            var child = itemRoot.GetChild(i);
            builder.Append('|').Append(child != null && child.gameObject.activeSelf ? 1 : 0).Append(':');
            if (child == null)
                continue;

            builder.Append(SortButtonTextCache.GetFirstText(child));
        }

        return builder.ToString();
    }

    private static bool HasVisibleFilterSummary(Game.Components.SortAndFilter.SortButtonGroup sortButtonGroup)
    {
        var owner = sortButtonGroup == null ? null : sortButtonGroup.GetComponentInParent<Game.Components.SortAndFilter.SortAndFilter>(true);
        if (owner == null)
            return false;

        var summaryArea = FilterSummaryAreaField?.GetValue(owner) as RectTransform;
        return summaryArea != null && summaryArea.gameObject.activeInHierarchy;
    }

    private static int GetActiveChildCount(RectTransform itemRoot)
    {
        var count = 0;
        for (var i = 0; i < itemRoot.childCount; i++)
        {
            var child = itemRoot.GetChild(i);
            if (child != null && child.gameObject.activeSelf)
                count++;
        }

        return Math.Max(count, 1);
    }

    private static int CalculateColumnCount(float availableWidth, int activeCount)
    {
        if (availableWidth <= 0f)
            return activeCount;

        var maxColumns = Mathf.FloorToInt((availableWidth + CompactSpacing) / (CompactButtonWidth + CompactSpacing));
        return Mathf.Clamp(maxColumns, 1, activeCount);
    }

    private static int CalculateSingleRowColumnCount(float availableWidth, int activeCount)
    {
        if (availableWidth <= 0f)
            return activeCount;

        var maxColumnsAtMinWidth = Mathf.FloorToInt((availableWidth + CompactSpacing) / (MinButtonWidth + CompactSpacing));
        return Mathf.Clamp(maxColumnsAtMinWidth, 1, activeCount);
    }

    private static float CalculateButtonWidth(float availableWidth, int columns)
    {
        if (availableWidth <= 0f)
            return CompactButtonWidth;

        var spacingTotal = CompactSpacing * Math.Max(columns - 1, 0);
        var width = (availableWidth - spacingTotal) / columns;
        return Mathf.Clamp(width, MinButtonWidth, MaxButtonWidth);
    }

    private static float GetAvailableWidth(RectTransform itemRoot, bool allowFullAncestorWidth)
    {
        var currentWidth = itemRoot.rect.width;
        var bestWidth = currentWidth;
        var current = itemRoot.parent as RectTransform;
        while (current != null)
        {
            var local = current.InverseTransformPoint(itemRoot.position);
            var leftSpace = current.rect.width * (1f - current.pivot.x) - local.x;
            var rightAnchoredWidth = Mathf.Max(current.rect.width - Mathf.Max(0f, local.x), 0f);
            var candidate = Mathf.Max(leftSpace, rightAnchoredWidth) - AncestorWidthMargin;
            if (candidate > bestWidth)
                bestWidth = candidate;

            if (allowFullAncestorWidth)
            {
                var fullAncestorWidth = current.rect.width - AncestorWidthMargin;
                if (fullAncestorWidth > bestWidth)
                    bestWidth = fullAncestorWidth;
            }

            current = current.parent as RectTransform;
        }

        return Mathf.Max(bestWidth, currentWidth);
    }
}

internal sealed class SortButtonTextCache : MonoBehaviour
{
    private TextMeshProUGUI[] _labels;

    internal static TextMeshProUGUI[] GetLabels(Transform transform)
    {
        if (transform == null)
            return Array.Empty<TextMeshProUGUI>();

        var cache = transform.GetComponent<SortButtonTextCache>();
        if (cache == null)
            cache = transform.gameObject.AddComponent<SortButtonTextCache>();

        return cache._labels ??= transform.GetComponentsInChildren<TextMeshProUGUI>(true);
    }

    internal static string GetFirstText(Transform transform)
    {
        var labels = GetLabels(transform);
        for (var i = 0; i < labels.Length; i++)
        {
            var text = labels[i] == null ? string.Empty : labels[i].text?.Trim();
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        return string.Empty;
    }
}

internal sealed class CompactSortButtonLayoutState : MonoBehaviour
{
    private const int RefreshFrames = 1;

    private Game.Components.SortAndFilter.SortButtonGroup _owner;
    private int _remainingFrames;
    private string _appliedSignature;

    internal static CompactSortButtonLayoutState GetOrAdd(Game.Components.SortAndFilter.SortButtonGroup owner)
    {
        if (owner == null)
            return null;

        var state = owner.GetComponent<CompactSortButtonLayoutState>();
        if (state == null)
            state = owner.gameObject.AddComponent<CompactSortButtonLayoutState>();

        state._owner = owner;
        if (state._remainingFrames <= 0)
            state.enabled = false;
        return state;
    }

    internal void Schedule()
    {
        _remainingFrames = RefreshFrames;
        enabled = true;
    }

    internal bool IsApplied(string signature)
    {
        return !string.IsNullOrEmpty(signature) && signature == _appliedSignature;
    }

    internal void MarkApplied(string signature)
    {
        _appliedSignature = signature;
    }

    private void LateUpdate()
    {
        if (_remainingFrames <= 0)
            return;

        _remainingFrames--;
        if (_owner == null || !_owner.gameObject.activeInHierarchy)
        {
            if (_remainingFrames <= 0)
                enabled = false;
            return;
        }

        CompactItemSortButtonGroupPatch.ApplyIfRecognized(_owner, scheduleDelayed: false);
        if (_remainingFrames <= 0)
            enabled = false;
    }
}

[HarmonyPatch(
    typeof(Game.Components.SortAndFilter.FilterToggle),
    "Refresh",
    new[] { typeof(Game.Components.SortAndFilter.FilterToggleConfig) })]
internal static class SimplifiedFilterTogglePatch
{
    private static void Postfix(
        Game.Components.SortAndFilter.FilterToggle __instance,
        Game.Components.SortAndFilter.FilterToggleConfig config)
    {
        if (__instance == null)
            return;

        SimplifiedFilterToggleVisual.ApplyFromRefresh(__instance, config.TipContent.GetString());
    }
}

[HarmonyPatch(typeof(Game.Components.SortAndFilter.ToggleGroupLine), "RefreshToggles")]
internal static class SimplifiedFilterToggleGroupLayoutPatch
{
    private static void Postfix(Game.Components.SortAndFilter.ToggleGroupLine __instance)
    {
        SimplifiedFilterToggleVisual.ApplyToggleGroupLayout(__instance);
        InventorySearchBoxOptimizationSupport.EnsureToggleLineSearchBox(__instance);
    }
}

internal static class SimplifiedFilterToggleVisual
{
    internal const float SimplifiedWidth = 68f;
    internal const float SimplifiedHeight = 38f;
    internal const float SimplifiedSpacing = 4f;

    private static readonly FieldInfo ToggleRootField =
        AccessTools.Field(typeof(Game.Components.SortAndFilter.ToggleGroupLine), "toggleRoot");

    private static readonly FieldInfo FirstToggleGroupLineField =
        AccessTools.Field(typeof(Game.Components.SortAndFilter.SortAndFilter), "firstToggleGroupLine");

    private static readonly FieldInfo FirstToggleGroupIndexField =
        AccessTools.Field(typeof(Game.Components.SortAndFilter.SortAndFilter), "_firstToggleGroupIndex");

    internal static void ApplyFromRefresh(Game.Components.SortAndFilter.FilterToggle toggle, string labelText)
    {
        if (toggle == null)
            return;

        var state = toggle.GetComponent<SimplifiedFilterToggleState>() ?? toggle.gameObject.AddComponent<SimplifiedFilterToggleState>();
        state.SetText(labelText);
        state.SetHasFollowUpMenu(HasFollowUpLine(toggle));
        state.CaptureOriginalVisuals();
        state.Apply(Plugin.EnableSimplifyFilterIcons);
    }

    internal static void RefreshAllActive()
    {
        foreach (var toggle in Resources.FindObjectsOfTypeAll<Game.Components.SortAndFilter.FilterToggle>())
        {
            if (toggle == null || !toggle.gameObject.scene.IsValid())
                continue;

            var state = toggle.GetComponent<SimplifiedFilterToggleState>();
            if (state != null)
            {
                state.SetHasFollowUpMenu(HasFollowUpLine(toggle));
                state.Apply(Plugin.EnableSimplifyFilterIcons);
            }
        }

        foreach (var line in Resources.FindObjectsOfTypeAll<Game.Components.SortAndFilter.ToggleGroupLine>())
        {
            if (line == null || !line.gameObject.scene.IsValid())
                continue;

            ApplyToggleGroupLayout(line);
        }
    }

    internal static void ApplyToggleGroupLayout(Game.Components.SortAndFilter.ToggleGroupLine line)
    {
        if (line == null)
            return;

        var toggleRoot = ToggleRootField?.GetValue(line) as RectTransform;
        if (toggleRoot == null)
            return;

        var rootState = toggleRoot.GetComponent<SimplifiedFilterToggleRootState>()
            ?? toggleRoot.gameObject.AddComponent<SimplifiedFilterToggleRootState>();
        rootState.Apply(toggleRoot, Plugin.EnableSimplifyFilterIcons, Plugin.EnableSimplifyFilterIcons);

        UiLayoutRefreshQueue.Request(toggleRoot, parentDepth: 1);
    }

    private static bool HasFollowUpLine(Game.Components.SortAndFilter.FilterToggle toggle)
    {
        if (toggle == null)
            return false;

        var owner = toggle.GetComponentInParent<SortAndFilter>(true);
        var line = toggle.GetComponentInParent<Game.Components.SortAndFilter.ToggleGroupLine>(true);
        if (owner?.Config?.LineConfigs == null || line == null)
            return false;

        if (!TryResolveToggleLineId(owner, line, out var lineId))
            return false;

        var optionIndex = toggle.transform.GetSiblingIndex() - 1;
        if (optionIndex < 0)
            return false;

        return HasLineDependingOnToggle(owner.Config.LineConfigs, lineId, optionIndex);
    }

    private static bool TryResolveToggleLineId(SortAndFilter owner, Game.Components.SortAndFilter.ToggleGroupLine line, out int lineId)
    {
        lineId = default;
        var firstToggleGroupLine = FirstToggleGroupLineField?.GetValue(owner) as Game.Components.SortAndFilter.ToggleGroupLine;
        var firstToggleGroupIndex = FirstToggleGroupIndexField?.GetValue(owner) is int index ? index : -1;
        if (firstToggleGroupLine == line
            && firstToggleGroupIndex >= 0
            && firstToggleGroupIndex < owner.Config.LineConfigs.Count)
        {
            lineId = owner.Config.LineConfigs[firstToggleGroupIndex].Id;
            return true;
        }

        for (var i = 0; i < owner.Config.LineConfigs.Count; i++)
        {
            var config = owner.Config.LineConfigs[i];
            if (config.Type != Game.Components.SortAndFilter.ESortAndFilterOneLineType.ToggleGroup)
                continue;

            var toggleRoot = ToggleRootField?.GetValue(line) as RectTransform;
            var toggleCount = toggleRoot == null ? 0 : toggleRoot.childCount - 1;
            var configCount = config.ToggleGroupLineConfig?.Config.FilterToggleConfigs?.Count ?? -1;
            if (toggleCount == configCount)
            {
                lineId = config.Id;
                return true;
            }
        }

        return false;
    }

    internal static bool HasLineDependingOnToggle(
        List<Game.Components.SortAndFilter.LineConfig> lineConfigs,
        int targetLineId,
        int targetOptionIndex)
    {
        foreach (var lineConfig in lineConfigs)
        {
            var dependencies = lineConfig.ActiveCondition?.ActiveDependsOn;
            if (dependencies == null)
                continue;

            foreach (var dependency in dependencies)
            {
                if (DoesDependencyTargetToggle(lineConfigs, dependency, targetLineId, targetOptionIndex, null))
                    return true;
            }
        }

        return false;
    }

    private static bool DoesDependencyTargetToggle(
        List<Game.Components.SortAndFilter.LineConfig> lineConfigs,
        Game.Components.SortAndFilter.ToggleIdIndex dependency,
        int targetLineId,
        int targetOptionIndex,
        HashSet<int> visitingLineIds)
    {
        if (dependency.LineId == targetLineId
            && !dependency.ToggleKey.IsAll
            && dependency.ToggleKey.Index == targetOptionIndex)
            return true;

        var parentLine = lineConfigs.Find(config => config.Id == dependency.LineId);
        var parentDependencies = parentLine?.ActiveCondition?.ActiveDependsOn;
        if (parentDependencies == null)
            return false;

        visitingLineIds ??= new HashSet<int>();
        if (!visitingLineIds.Add(dependency.LineId))
            return false;

        foreach (var parentDependency in parentDependencies)
        {
            if (DoesDependencyTargetToggle(lineConfigs, parentDependency, targetLineId, targetOptionIndex, visitingLineIds))
            {
                visitingLineIds.Remove(dependency.LineId);
                return true;
            }
        }

        visitingLineIds.Remove(dependency.LineId);
        return false;
    }
}

internal sealed class SimplifiedFilterToggleState : MonoBehaviour
{
    private static readonly Color NormalBackgroundColor = new(0.10f, 0.13f, 0.12f, 0.92f);
    private static readonly Color SelectedBackgroundColor = new(0.42f, 0.07f, 0.06f, 0.96f);
    private static readonly Color TextColor = new(0.86f, 0.86f, 0.78f, 1f);

    private FrameWork.UISystem.UIElements.CToggle _toggle;
    private TooltipInvoker[] _tooltips;
    private bool[] _originalTooltipEnabledStates;
    private UnityEngine.UI.Graphic _targetGraphic;
    private UnityEngine.UI.Graphic _selectedGraphic;
    private Sprite _originalTargetSprite;
    private Sprite _originalSelectedSprite;
    private Color _originalTargetColor;
    private Color _originalSelectedColor;
    private UnityEngine.UI.Selectable.Transition _originalTransition;
    private UnityEngine.UI.SpriteState _originalSpriteState;
    private RectTransform _rect;
    private Vector2 _originalSizeDelta;
    private LayoutElement _layoutElement;
    private bool _hadLayoutElement;
    private float _originalMinWidth;
    private float _originalPreferredWidth;
    private float _originalFlexibleWidth;
    private float _originalMinHeight;
    private float _originalPreferredHeight;
    private float _originalFlexibleHeight;
    private TextMeshProUGUI _label;
    private RectTransform _followUpUnderline;
    private string _text = string.Empty;
    private bool _hasFollowUpMenu;
    private bool _hasOriginalVisuals;
    private bool _hasOriginalLayout;
    private bool _hasOriginalTooltips;
    private bool _isSimplified;
    private int _applyFrames;

    internal void SetText(string text)
    {
        _text = ShortenFilterLabel(text);
        if (_label != null)
            _label.SetText(_text);
    }

    internal void SetHasFollowUpMenu(bool hasFollowUpMenu)
    {
        _hasFollowUpMenu = hasFollowUpMenu;
        UpdateFollowUpUnderline();
    }

    internal void CaptureOriginalVisuals()
    {
        _toggle = GetComponent<FrameWork.UISystem.UIElements.CToggle>();
        if (_toggle == null)
            return;

        _targetGraphic = _toggle.targetGraphic;
        _selectedGraphic = _toggle.graphic;
        _originalTransition = _toggle.transition;
        _originalSpriteState = _toggle.spriteState;
        CaptureOriginalTooltips();
        CaptureOriginalLayout();

        if (_targetGraphic != null)
        {
            _originalTargetColor = _targetGraphic.color;
            _originalTargetSprite = GetSprite(_targetGraphic);
        }

        if (_selectedGraphic != null)
        {
            _originalSelectedColor = _selectedGraphic.color;
            _originalSelectedSprite = GetSprite(_selectedGraphic);
        }

        _hasOriginalVisuals = true;
    }

    internal void Apply(bool enabled)
    {
        EnsureLabel();
        if (!enabled)
        {
            _applyFrames = 0;
            this.enabled = false;
            RestoreOriginalVisuals();
            return;
        }

        _applyFrames = 1;
        this.enabled = true;
        ApplySimplifiedVisuals();
    }

    private void LateUpdate()
    {
        if (_isSimplified && Plugin.EnableSimplifyFilterIcons && _applyFrames > 0)
        {
            _applyFrames--;
            ApplySimplifiedVisuals();
            if (_applyFrames <= 0)
                enabled = false;
        }
    }

    private void EnsureLabel()
    {
        if (_label != null)
            return;

        var labelObject = new GameObject("BetterTaiwuScrollSimplifiedFilterText", typeof(RectTransform));
        labelObject.transform.SetParent(transform, false);
        var rect = labelObject.transform as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4f, 1f);
            rect.offsetMax = new Vector2(-4f, -1f);
        }

        _label = labelObject.AddComponent<TextMeshProUGUI>();
        ApplyFontFromNearbyLabel(_label);
        _label.raycastTarget = false;
        _label.alignment = TextAlignmentOptions.Center;
        _label.enableAutoSizing = true;
        _label.fontSizeMin = 12f;
        _label.fontSizeMax = 18f;
        _label.overflowMode = TextOverflowModes.Ellipsis;
        _label.color = TextColor;
        _label.SetText(_text);
        _label.gameObject.SetActive(false);
    }

    private void ApplySimplifiedVisuals()
    {
        if (_toggle == null)
            _toggle = GetComponent<FrameWork.UISystem.UIElements.CToggle>();

        if (_toggle != null)
            _toggle.transition = UnityEngine.UI.Selectable.Transition.None;

        SetTooltipsEnabled(false);
        ApplySimplifiedLayout();
        ApplyGraphicAsBackground(_targetGraphic, NormalBackgroundColor);
        ApplyGraphicAsBackground(_selectedGraphic, SelectedBackgroundColor);

        if (_label != null)
        {
            if (_label.font == null)
                ApplyFontFromNearbyLabel(_label);
            _label.color = TextColor;
            _label.canvasRenderer.SetAlpha(1f);
            _label.SetText(_text);
            _label.gameObject.SetActive(true);
            _label.transform.SetAsLastSibling();
        }

        _isSimplified = true;
        UpdateFollowUpUnderline();
    }

    private void RestoreOriginalVisuals()
    {
        if (_label != null)
            _label.gameObject.SetActive(false);
        if (_followUpUnderline != null)
            _followUpUnderline.gameObject.SetActive(false);

        SetTooltipsEnabled(true);
        RestoreOriginalLayout();
        if (_toggle != null && _hasOriginalVisuals)
        {
            _toggle.transition = _originalTransition;
            _toggle.spriteState = _originalSpriteState;
        }

        if (_hasOriginalVisuals)
        {
            RestoreGraphic(_targetGraphic, _originalTargetSprite, _originalTargetColor);
            RestoreGraphic(_selectedGraphic, _originalSelectedSprite, _originalSelectedColor);
        }

        _isSimplified = false;
    }

    internal void ApplySimplifiedLayout()
    {
        CaptureOriginalLayout();
        _rect ??= transform as RectTransform;
        if (_rect != null)
        {
            _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, SimplifiedFilterToggleVisual.SimplifiedWidth);
            if (_rect.rect.height <= 0f || _rect.rect.height > SimplifiedFilterToggleVisual.SimplifiedHeight + 8f)
                _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, SimplifiedFilterToggleVisual.SimplifiedHeight);
        }

        _layoutElement ??= GetComponent<LayoutElement>();
        if (_layoutElement == null)
            _layoutElement = gameObject.AddComponent<LayoutElement>();

        _layoutElement.minWidth = SimplifiedFilterToggleVisual.SimplifiedWidth;
        _layoutElement.preferredWidth = SimplifiedFilterToggleVisual.SimplifiedWidth;
        _layoutElement.flexibleWidth = 0f;
        if (_rect == null || _rect.rect.height <= SimplifiedFilterToggleVisual.SimplifiedHeight + 8f)
        {
            _layoutElement.minHeight = SimplifiedFilterToggleVisual.SimplifiedHeight;
            _layoutElement.preferredHeight = SimplifiedFilterToggleVisual.SimplifiedHeight;
            _layoutElement.flexibleHeight = 0f;
        }
    }

    internal void RestoreOriginalLayout()
    {
        if (!_hasOriginalLayout)
            return;

        _rect ??= transform as RectTransform;
        if (_rect != null)
            _rect.sizeDelta = _originalSizeDelta;

        _layoutElement ??= GetComponent<LayoutElement>();
        if (_layoutElement != null)
        {
            if (_hadLayoutElement)
            {
                _layoutElement.minWidth = _originalMinWidth;
                _layoutElement.preferredWidth = _originalPreferredWidth;
                _layoutElement.flexibleWidth = _originalFlexibleWidth;
                _layoutElement.minHeight = _originalMinHeight;
                _layoutElement.preferredHeight = _originalPreferredHeight;
                _layoutElement.flexibleHeight = _originalFlexibleHeight;
            }
            else
            {
                Destroy(_layoutElement);
                _layoutElement = null;
            }
        }
    }

    private void CaptureOriginalLayout()
    {
        if (_hasOriginalLayout)
            return;

        _rect = transform as RectTransform;
        if (_rect != null)
            _originalSizeDelta = _rect.sizeDelta;

        _layoutElement = GetComponent<LayoutElement>();
        _hadLayoutElement = _layoutElement != null;
        if (_layoutElement != null)
        {
            _originalMinWidth = _layoutElement.minWidth;
            _originalPreferredWidth = _layoutElement.preferredWidth;
            _originalFlexibleWidth = _layoutElement.flexibleWidth;
            _originalMinHeight = _layoutElement.minHeight;
            _originalPreferredHeight = _layoutElement.preferredHeight;
            _originalFlexibleHeight = _layoutElement.flexibleHeight;
        }

        _hasOriginalLayout = true;
    }

    private void CaptureOriginalTooltips()
    {
        if (_hasOriginalTooltips)
            return;

        _tooltips = GetComponentsInChildren<TooltipInvoker>(true);
        _originalTooltipEnabledStates = new bool[_tooltips.Length];
        for (var i = 0; i < _tooltips.Length; i++)
            _originalTooltipEnabledStates[i] = _tooltips[i] != null && _tooltips[i].enabled;

        _hasOriginalTooltips = true;
    }

    private void SetTooltipsEnabled(bool enabled)
    {
        CaptureOriginalTooltips();
        if (_tooltips == null)
            return;

        for (var i = 0; i < _tooltips.Length; i++)
        {
            var tooltip = _tooltips[i];
            if (tooltip == null)
                continue;

            if (!enabled)
            {
                tooltip.HideTips();
                tooltip.enabled = false;
            }
            else
            {
                tooltip.enabled = i < _originalTooltipEnabledStates.Length && _originalTooltipEnabledStates[i];
            }
        }
    }

    private void ApplyFontFromNearbyLabel(TextMeshProUGUI label)
    {
        if (label == null)
            return;

        TextMeshProUGUI source = null;
        var owner = GetComponentInParent<SortAndFilter>(true);
        var labels = owner == null
            ? transform.root.GetComponentsInChildren<TextMeshProUGUI>(true)
            : owner.GetComponentsInChildren<TextMeshProUGUI>(true);

        foreach (var candidate in labels)
        {
            if (candidate == null || candidate == label || candidate.name == "BetterTaiwuScrollSimplifiedFilterText")
                continue;

            if (candidate.font != null)
            {
                source = candidate;
                break;
            }
        }

        if (source != null)
        {
            label.font = source.font;
            label.fontSharedMaterial = source.fontSharedMaterial;
            label.spriteAsset = source.spriteAsset;
            label.styleSheet = source.styleSheet;
            label.fontStyle = source.fontStyle;
        }
        else if (TMP_Settings.defaultFontAsset != null)
        {
            label.font = TMP_Settings.defaultFontAsset;
        }
    }

    private void UpdateFollowUpUnderline()
    {
        EnsureFollowUpUnderline();
        if (_followUpUnderline == null)
            return;

        var visible = _hasFollowUpMenu
            && Plugin.EnableSimplifyFilterIcons
            && Plugin.EnableSimplifiedFilterIconUnderlines
            && _isSimplified;
        _followUpUnderline.gameObject.SetActive(visible);
        if (!visible)
            return;

        var width = _label == null ? 32f : Mathf.Clamp(_label.preferredWidth, 22f, SimplifiedFilterToggleVisual.SimplifiedWidth - 14f);
        _followUpUnderline.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        _followUpUnderline.SetAsLastSibling();
    }

    private void EnsureFollowUpUnderline()
    {
        if (_followUpUnderline != null)
            return;

        var underlineObject = new GameObject("BetterTaiwuScrollFollowUpUnderline", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        underlineObject.transform.SetParent(transform, false);
        _followUpUnderline = underlineObject.transform as RectTransform;
        if (_followUpUnderline != null)
        {
            _followUpUnderline.anchorMin = new Vector2(0.5f, 0f);
            _followUpUnderline.anchorMax = new Vector2(0.5f, 0f);
            _followUpUnderline.pivot = new Vector2(0.5f, 0.5f);
            _followUpUnderline.anchoredPosition = new Vector2(0f, 7f);
            _followUpUnderline.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 32f);
            _followUpUnderline.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 2f);
        }

        var image = underlineObject.GetComponent<UnityEngine.UI.Image>();
        image.raycastTarget = false;
        image.color = TextColor;
        underlineObject.SetActive(false);
    }

    private static void ApplyGraphicAsBackground(UnityEngine.UI.Graphic graphic, Color color)
    {
        if (graphic == null)
            return;

        StretchGraphicToParent(graphic);
        SetSprite(graphic, null);
        graphic.color = color;
    }

    private static void RestoreGraphic(UnityEngine.UI.Graphic graphic, Sprite sprite, Color color)
    {
        if (graphic == null)
            return;

        SetSprite(graphic, sprite);
        graphic.color = color;
    }

    private static void StretchGraphicToParent(UnityEngine.UI.Graphic graphic)
    {
        if (graphic.transform is not RectTransform rect)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static Sprite GetSprite(UnityEngine.UI.Graphic graphic)
    {
        return graphic is UnityEngine.UI.Image image ? image.sprite : null;
    }

    private static void SetSprite(UnityEngine.UI.Graphic graphic, Sprite sprite)
    {
        if (graphic is UnityEngine.UI.Image image)
            image.sprite = sprite;
    }

    private static string ShortenFilterLabel(string text)
    {
        return FilterLabelAbbreviations.Shorten(text);
    }
}

internal sealed class SimplifiedFilterToggleRootState : MonoBehaviour
{
    private RectTransform _rect;
    private Vector2 _originalSizeDelta;
    private GridLayoutGroup _grid;
    private bool _originalGridEnabled;
    private Vector2 _originalGridCellSize;
    private Vector2 _originalGridSpacing;
    private HorizontalLayoutGroup _horizontal;
    private bool _originalHorizontalEnabled;
    private float _originalHorizontalSpacing;
    private bool _originalChildControlWidth;
    private bool _originalChildForceExpandWidth;
    private VerticalLayoutGroup _vertical;
    private bool _originalVerticalEnabled;
    private bool _hasOriginal;

    internal void Apply(RectTransform root, bool forceSingleRow, bool simplifyChildren)
    {
        if (root == null)
            return;

        Capture(root);
        if (!forceSingleRow && !simplifyChildren)
        {
            Restore();
            return;
        }

        var activeCount = CountActiveChildren(root);
        if (forceSingleRow)
        {
            if (_horizontal != null)
            {
                _horizontal.enabled = true;
                _horizontal.spacing = SimplifiedFilterToggleVisual.SimplifiedSpacing;
                _horizontal.childControlWidth = true;
                _horizontal.childControlHeight = true;
                _horizontal.childForceExpandWidth = false;
                _horizontal.childForceExpandHeight = false;
                _horizontal.childAlignment = TextAnchor.UpperLeft;
                if (_grid != null)
                    _grid.enabled = false;
            }
            else if (_grid != null)
            {
                _grid.enabled = true;
                _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                _grid.constraintCount = Math.Max(activeCount, 1);
                _grid.cellSize = new Vector2(SimplifiedFilterToggleVisual.SimplifiedWidth, Mathf.Max(_originalGridCellSize.y, SimplifiedFilterToggleVisual.SimplifiedHeight));
                _grid.spacing = new Vector2(SimplifiedFilterToggleVisual.SimplifiedSpacing, SimplifiedFilterToggleVisual.SimplifiedSpacing);
                _grid.childAlignment = TextAnchor.UpperLeft;
            }

            if (_vertical != null)
                _vertical.enabled = false;
        }

        if (simplifyChildren)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                var state = child == null ? null : child.GetComponent<SimplifiedFilterToggleState>();
                state?.ApplySimplifiedLayout();
            }
        }

        if (forceSingleRow && activeCount > 0)
        {
            var width = activeCount * SimplifiedFilterToggleVisual.SimplifiedWidth
                + Math.Max(activeCount - 1, 0) * SimplifiedFilterToggleVisual.SimplifiedSpacing;
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }
    }

    private void Capture(RectTransform root)
    {
        if (_hasOriginal)
            return;

        _rect = root;
        _originalSizeDelta = root.sizeDelta;
        _grid = root.GetComponent<GridLayoutGroup>();
        if (_grid != null)
        {
            _originalGridEnabled = _grid.enabled;
            _originalGridCellSize = _grid.cellSize;
            _originalGridSpacing = _grid.spacing;
        }

        _horizontal = root.GetComponent<HorizontalLayoutGroup>();
        if (_horizontal != null)
        {
            _originalHorizontalEnabled = _horizontal.enabled;
            _originalHorizontalSpacing = _horizontal.spacing;
            _originalChildControlWidth = _horizontal.childControlWidth;
            _originalChildForceExpandWidth = _horizontal.childForceExpandWidth;
        }

        _vertical = root.GetComponent<VerticalLayoutGroup>();
        if (_vertical != null)
            _originalVerticalEnabled = _vertical.enabled;

        _hasOriginal = true;
    }

    private void Restore()
    {
        if (!_hasOriginal)
            return;

        if (_rect != null)
            _rect.sizeDelta = _originalSizeDelta;

        if (_grid != null)
        {
            _grid.enabled = _originalGridEnabled;
            _grid.cellSize = _originalGridCellSize;
            _grid.spacing = _originalGridSpacing;
        }

        if (_horizontal != null)
        {
            _horizontal.enabled = _originalHorizontalEnabled;
            _horizontal.spacing = _originalHorizontalSpacing;
            _horizontal.childControlWidth = _originalChildControlWidth;
            _horizontal.childForceExpandWidth = _originalChildForceExpandWidth;
        }

        if (_vertical != null)
            _vertical.enabled = _originalVerticalEnabled;

        if (_rect != null)
        {
            for (var i = 0; i < _rect.childCount; i++)
            {
                var child = _rect.GetChild(i);
                var state = child == null ? null : child.GetComponent<SimplifiedFilterToggleState>();
                state?.RestoreOriginalLayout();
            }
        }
    }

    private static int CountActiveChildren(RectTransform root)
    {
        var count = 0;
        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child != null && child.gameObject.activeSelf)
                count++;
        }

        return count;
    }
}

[HarmonyPatch(typeof(SortAndFilter), "Setup")]
internal static class SortAndFilterSetupInlineFilterPatch
{
    private static void Postfix(SortAndFilter __instance)
    {
        InlineFilterButtonsController.GetOrAdd(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(SortAndFilter), "UpdateLineActive")]
internal static class SortAndFilterUpdateLineActiveInlineFilterPatch
{
    private static void Postfix(SortAndFilter __instance)
    {
        InlineFilterButtonsController.Get(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(SortAndFilter), "ApplyFilterLineStates")]
internal static class SortAndFilterApplyLineStatesInlineFilterPatch
{
    private static void Postfix(SortAndFilter __instance)
    {
        InlineFilterButtonsController.Get(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(SortAndFilter), "SetDropdownOption")]
internal static class SortAndFilterSetDropdownOptionInlineFilterPatch
{
    private static void Postfix(SortAndFilter __instance)
    {
        InlineFilterButtonsController.Get(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(SortAndFilter), "SetToggleVisible")]
internal static class SortAndFilterSetToggleVisibleInlineFilterPatch
{
    private static void Postfix(SortAndFilter __instance)
    {
        InlineFilterButtonsController.Get(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(SortAndFilter), "SetToggleIsOnWithoutNotify")]
internal static class SortAndFilterSetToggleIsOnWithoutNotifyInlineFilterPatch
{
    private static void Postfix(SortAndFilter __instance)
    {
        InlineFilterButtonsController.Get(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(SortAndFilter), "SetVisibleDropdownMenus")]
internal static class SortAndFilterSetVisibleDropdownMenusInlineFilterPatch
{
    private static void Postfix(SortAndFilter __instance)
    {
        InlineFilterButtonsController.Get(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(SortAndFilter), "ClearAllFilter")]
internal static class SortAndFilterClearAllInlineFilterPatch
{
    private static void Postfix(SortAndFilter __instance)
    {
        InlineFilterButtonsController.Get(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(SortAndFilter), "IsPointOverEntryButton")]
internal static class SortAndFilterInlineFilterPointerPatch
{
    private static void Postfix(SortAndFilter __instance, Vector2 screenPoint, ref bool __result)
    {
        if (!__result && Plugin.EnableInlineFilterButtons)
            __result = InlineFilterButtonsController.Get(__instance)?.ContainsScreenPoint(screenPoint) == true;
    }
}

[HarmonyPatch(typeof(SortAndFilter), "GetSummaryItems")]
internal static class SortAndFilterGetSummaryItemsInlineFilterPatch
{
    private static void Postfix(SortAndFilter __instance, ref List<SortAndFilter.SummaryItemData> __result)
    {
        InlineFilterButtonsController.Get(__instance)?.RemoveInlineRootSummary(__result);
    }
}

internal sealed class InlineFilterButtonsController : MonoBehaviour
{
    internal const string InlineSectionName = "BetterTaiwuScrollInlineFilterButtons";
    private const float InlinePanelHeight = 52f;
    private const float InlineButtonHeight = 38f;
    private const float InlineButtonMinWidth = 46f;
    private const float InlineButtonPreferredWidth = 60f;
    private const float InlineButtonMaxWidth = 96f;
    private const float InlineButtonSpacing = 4f;
    private const float CompactSummaryHeight = 34f;

    private static readonly FieldInfo FilterPanelOwnerField = AccessTools.Field(typeof(FilterPanel), "_owner");
    private static readonly FieldInfo OwnerFilterPanelField = AccessTools.Field(typeof(SortAndFilter), "filterPanel");
    private static readonly FieldInfo OwnerSummaryAreaField = AccessTools.Field(typeof(SortAndFilter), "filterSummaryArea");
    private static readonly FieldInfo OwnerSummaryRootField = AccessTools.Field(typeof(SortAndFilter), "filterSummaryRoot");
    private static readonly FieldInfo OwnerForceHideEntryField = AccessTools.Field(typeof(SortAndFilter), "_forceHideEntry");
    private static readonly FieldInfo OwnerEntryToggleField = AccessTools.Field(typeof(SortAndFilter), "entryToggle");
    private static readonly FieldInfo PanelSectionTemplateField = AccessTools.Field(typeof(FilterPanel), "sectionTemplate");
    private static readonly FieldInfo PanelSectionMapField = AccessTools.Field(typeof(FilterPanel), "_sectionMap");
    private static readonly MethodInfo OwnerRefreshSummaryMethod = AccessTools.Method(typeof(SortAndFilter), "RefreshSummary");

    private SortAndFilter _owner;
    private FilterPanel _filterPanel;
    private RectTransform _entryRect;
    private FilterSection _inlineSection;
    private int _lineId;
    private int _lineIndex;
    private int _menuId;
    private bool _hasInlineRoot;
    private readonly List<int> _inlineOptionIndexMap = new();
    private bool _entryForceHiddenByInline;
    private bool _originalEntryForceHidden;
    private Coroutine _layoutRefreshRoutine;
    private bool _showRootSectionInPanel;
    private string _lastRefreshSignature;
    private readonly HashSet<int> _inlineOptionsWithFollowUpMenus = new();
    private RectTransform _stableInlineWidthParent;
    private float _stableInlineAvailableWidth;

    internal static InlineFilterButtonsController GetOrAdd(SortAndFilter owner)
    {
        if (owner == null)
            return null;

        // The batch selector deliberately keeps the complete vanilla filter panel.
        // Moving its root filter into the inline entry row would hide that filter
        // together with the cloned entry controls.
        if (BatchItemFilterController.IsBatchFilterView(owner))
        {
            var batchController = owner.GetComponent<InlineFilterButtonsController>();
            batchController?.Restore();
            return null;
        }

        var controller = owner.GetComponent<InlineFilterButtonsController>();
        if (controller == null)
            controller = owner.gameObject.AddComponent<InlineFilterButtonsController>();

        controller.Initialize(owner);
        return controller;
    }

    internal static InlineFilterButtonsController Get(SortAndFilter owner)
    {
        return owner == null || BatchItemFilterController.IsBatchFilterView(owner)
            ? null
            : owner.GetComponent<InlineFilterButtonsController>();
    }

    internal static InlineFilterButtonsController GetFromPanel(FilterPanel panel)
    {
        var owner = FilterPanelOwnerField?.GetValue(panel) as SortAndFilter;
        return Get(owner);
    }

    internal static void RefreshAllActive(bool allowRestore)
    {
        foreach (var owner in Resources.FindObjectsOfTypeAll<SortAndFilter>())
        {
            if (owner == null || !owner.gameObject.scene.IsValid())
                continue;

            if (Plugin.EnableInlineFilterButtons)
                GetOrAdd(owner)?.Refresh();
            else if (allowRestore)
                Get(owner)?.Restore();
        }
    }

    internal static void RestoreAll()
    {
        foreach (var controller in Resources.FindObjectsOfTypeAll<InlineFilterButtonsController>())
            controller.Restore();
    }

    internal bool MatchesInlineRoot(int lineId, int menuId)
    {
        return _hasInlineRoot && _lineId == lineId && _menuId == menuId;
    }

    internal bool TryGetInlineRoot(out int lineId, out int menuId)
    {
        lineId = _lineId;
        menuId = _menuId;
        return _hasInlineRoot;
    }

    internal bool TryGetInlineOptionText(int originalOptionIndex, out string text)
    {
        text = string.Empty;
        if (originalOptionIndex < 0)
            return _hasInlineRoot;

        if (!TryGetInlineMenuConfig(out var menuConfig))
            return false;

        var itemConfigs = menuConfig.DropdownConfig.ItemConfigs;
        if (itemConfigs == null || originalOptionIndex < 0 || originalOptionIndex >= itemConfigs.Count)
            return false;

        text = NormalizeInlineOptionText(itemConfigs[originalOptionIndex].Text.GetString());
        return !string.IsNullOrEmpty(text);
    }

    internal bool TryFindInlineOptionByText(string text, out int originalOptionIndex)
    {
        originalOptionIndex = -1;
        var normalized = NormalizeInlineOptionText(text);
        if (string.IsNullOrEmpty(normalized))
            return _hasInlineRoot;

        if (!TryGetInlineMenuConfig(out var menuConfig))
            return false;

        var itemConfigs = menuConfig.DropdownConfig.ItemConfigs;
        if (itemConfigs == null)
            return false;

        foreach (var candidateIndex in _inlineOptionIndexMap)
        {
            if (candidateIndex < 0 || candidateIndex >= itemConfigs.Count)
                continue;

            var candidateText = NormalizeInlineOptionText(itemConfigs[candidateIndex].Text.GetString());
            if (string.Equals(candidateText, normalized, StringComparison.Ordinal))
            {
                originalOptionIndex = candidateIndex;
                return true;
            }
        }

        return false;
    }

    internal bool HasInlineOriginalOption(int originalOptionIndex)
    {
        if (originalOptionIndex < 0)
            return _hasInlineRoot;

        return _inlineOptionIndexMap.Contains(originalOptionIndex);
    }

    private bool TryGetInlineMenuConfig(out DetailedFilterMenuConfig menuConfig)
    {
        menuConfig = default;
        if (!_hasInlineRoot || _owner == null)
            return false;

        var maybeConfig = _owner.GetMenuConfig(_lineIndex, _menuId);
        if (!maybeConfig.HasValue)
            return false;

        menuConfig = maybeConfig.Value;
        return true;
    }

    private static string NormalizeInlineOptionText(string text)
    {
        return (text ?? string.Empty).Trim();
    }

    private void Initialize(SortAndFilter owner)
    {
        if (!ReferenceEquals(_owner, owner))
            ResetStableInlineWidth();

        _owner = owner;
        _filterPanel = OwnerFilterPanelField?.GetValue(owner) as FilterPanel;
        var entryToggle = OwnerEntryToggleField?.GetValue(owner);
        _entryRect = entryToggle is Component component ? component.transform as RectTransform : null;
    }

    internal void Refresh()
    {
        if (_owner == null)
            return;

        if (!Plugin.EnableInlineFilterButtons)
        {
            Restore();
            return;
        }

        if (!TryGetRootSection(out var section, out var menuConfig))
        {
            _hasInlineRoot = false;
            _lastRefreshSignature = null;
            RestoreEntryButton();
            SetInlineActive(false);
            return;
        }

        HideEntryButton();
        EnsureInlineSection();
        if (_inlineSection == null)
        {
            _hasInlineRoot = false;
            return;
        }

        _lineId = section.LineId;
        _lineIndex = section.LineIndex;
        _menuId = section.MenuId;

        var itemConfigs = menuConfig.DropdownConfig.ItemConfigs;
        if (itemConfigs == null)
        {
            _hasInlineRoot = false;
            _lastRefreshSignature = null;
            SetInlineActive(false);
            return;
        }

        _hasInlineRoot = true;
        var inlineItemConfigs = BuildInlineItemConfigs(itemConfigs);
        var selectedOriginalIndex = _owner.GetInitialSectionState(_lineId, _menuId);
        var rect = _inlineSection.transform as RectTransform;
        var panelWidth = rect == null ? 0f : GetInlineAvailableWidth(rect);
        var signature = BuildInlineRefreshSignature(section, inlineItemConfigs, panelWidth);
        if (_inlineSection.gameObject.activeInHierarchy && signature == _lastRefreshSignature)
        {
            _inlineSection.SetSelectedIndex(ToDisplayOptionIndex(selectedOriginalIndex), notify: false);
            FilterMultiSelectSupport.ApplySelectionVisuals(_inlineSection, _owner, _lineId, _menuId);
            return;
        }

        RefreshInlineFollowUpOptionSet();
        _inlineSection.gameObject.SetActive(true);
        _inlineSection.Setup(_menuId, string.Empty, inlineItemConfigs, OnInlineSelectionChanged);
        _inlineSection.SetSelectedIndex(ToDisplayOptionIndex(selectedOriginalIndex), notify: false);
        ApplyInlineLayout();
        RequestDelayedLayoutRefresh();
        AfterPanelRefresh();
        RefreshOwnerSummary();
        TuneSummaryAreaLayout();
        _lastRefreshSignature = signature;
    }

    private List<FilterDropdownItemConfig> BuildInlineItemConfigs(List<FilterDropdownItemConfig> itemConfigs)
    {
        _inlineOptionIndexMap.Clear();
        if (itemConfigs == null)
            return new List<FilterDropdownItemConfig>();

        for (var i = 0; i < itemConfigs.Count; i++)
            _inlineOptionIndexMap.Add(i);

        return itemConfigs;
    }

    private int ToDisplayOptionIndex(int originalIndex)
    {
        if (originalIndex < 0)
            return -1;

        var displayIndex = _inlineOptionIndexMap.IndexOf(originalIndex);
        return displayIndex >= 0 ? displayIndex : -1;
    }

    private int ToOriginalOptionIndex(int displayIndex)
    {
        if (displayIndex < 0)
            return -1;

        return displayIndex >= 0 && displayIndex < _inlineOptionIndexMap.Count
            ? _inlineOptionIndexMap[displayIndex]
            : displayIndex;
    }

    internal void Restore()
    {
        _hasInlineRoot = false;
        _lastRefreshSignature = null;
        if (_layoutRefreshRoutine != null)
        {
            StopCoroutine(_layoutRefreshRoutine);
            _layoutRefreshRoutine = null;
        }
        RestoreEntryButton();
        SetInlineActive(false);
    }

    private IEnumerator RefreshLayoutNextFrame()
    {
        yield return null;
        _layoutRefreshRoutine = null;

        if (_hasInlineRoot && _inlineSection != null && _inlineSection.gameObject.activeInHierarchy)
        {
            ApplyInlineLayout();
            TuneSummaryAreaLayout();
        }
    }

    private void OnRectTransformDimensionsChange()
    {
        if (_hasInlineRoot && isActiveAndEnabled)
            RequestDelayedLayoutRefresh();
    }

    private void RequestDelayedLayoutRefresh()
    {
        if (_layoutRefreshRoutine == null && isActiveAndEnabled)
            _layoutRefreshRoutine = StartCoroutine(RefreshLayoutNextFrame());
    }

    internal void RefreshVillagerSearchLayout()
    {
        if (_inlineSection != null)
            ApplyInlineLayout();

        _owner?.GetComponent<FilterEntryFullRowLayoutState>()?.RefreshInlineHeight();
        RequestDelayedLayoutRefresh();
    }

    internal void AfterPanelRefresh()
    {
        if (!Plugin.EnableInlineFilterButtons || _owner == null || _filterPanel == null)
            return;

        if (_showRootSectionInPanel)
            ShowPanelRootSection();
        else
            HidePanelRootSection();

        TuneSummaryAreaLayout();
    }

    internal void RemoveInlineRootSummary(List<SortAndFilter.SummaryItemData> items)
    {
        if (!Plugin.EnableInlineFilterButtons || !_hasInlineRoot || items == null)
            return;

        items.RemoveAll(item => item.LineId == _lineId && item.MenuId == _menuId);
    }

    internal bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (_inlineSection == null || !_inlineSection.gameObject.activeInHierarchy)
            return false;

        var rect = _inlineSection.transform as RectTransform;
        return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, UIManager.Instance.UiCamera);
    }

    private bool TryGetRootSection(out SortAndFilter.SectionViewData section, out DetailedFilterMenuConfig menuConfig)
    {
        foreach (var candidate in _owner.Sections)
        {
            if (!candidate.IsActive)
                continue;

            var maybeConfig = _owner.GetMenuConfig(candidate.LineIndex, candidate.MenuId);
            if (!maybeConfig.HasValue)
                continue;

            var config = maybeConfig.Value;
            if (config.DropdownContext.Dependency.HasValue)
                continue;

            var itemConfigs = config.DropdownConfig.ItemConfigs;
            if (itemConfigs == null || itemConfigs.Count == 0)
                continue;

            section = candidate;
            menuConfig = config;
            return true;
        }

        section = default;
        menuConfig = default;
        return false;
    }

    private void EnsureInlineSection()
    {
        if (_filterPanel == null)
            _filterPanel = OwnerFilterPanelField?.GetValue(_owner) as FilterPanel;

        var sectionTemplate = PanelSectionTemplateField?.GetValue(_filterPanel) as FilterSection;
        if (sectionTemplate == null || _entryRect == null)
            return;

        var parent = _entryRect.parent as RectTransform;
        if (parent == null)
            return;

        if (_inlineSection == null)
        {
            _inlineSection = Instantiate(sectionTemplate, parent);
            _inlineSection.name = InlineSectionName;
        }
        else if (_inlineSection.transform.parent != parent)
        {
            _inlineSection.transform.SetParent(parent, false);
        }

        _inlineSection.transform.SetSiblingIndex(_entryRect.GetSiblingIndex());
        _inlineSection.gameObject.SetActive(true);
    }

    private void ApplyInlineLayout()
    {
        if (_inlineSection == null || _entryRect == null)
            return;

        var rect = _inlineSection.transform as RectTransform;
        if (rect == null)
            return;

        rect.anchorMin = _entryRect.anchorMin;
        rect.anchorMax = _entryRect.anchorMax;
        rect.pivot = _entryRect.pivot;
        rect.anchoredPosition = _entryRect.anchoredPosition;
        rect.localScale = Vector3.one;

        var contentRoot = _inlineSection.GetContentRoot();
        if (contentRoot == null)
            return;

        contentRoot.anchorMin = Vector2.zero;
        contentRoot.anchorMax = Vector2.one;
        contentRoot.offsetMin = Vector2.zero;
        contentRoot.offsetMax = Vector2.zero;
        ApplyInlineOptionTextLayout(contentRoot);
        ApplyShortInlineLabels(contentRoot);
        ApplyInlineFollowUpUnderlines(contentRoot);
        AttachInlineClickRelays(contentRoot);
        FilterMultiSelectSupport.ApplySelectionVisuals(_inlineSection, _owner, _lineId, _menuId);

        var grid = contentRoot.GetComponent<GridLayoutGroup>();
        var panelHeight = InlinePanelHeight;
        var activeCount = Math.Max(CountActiveChildren(contentRoot), 1);
        var panelWidth = GetInlineAvailableWidth(rect);
        if (grid != null)
        {
            var horizontalPadding = grid.padding.left + grid.padding.right;
            var verticalPadding = grid.padding.top + grid.padding.bottom;
            var cellAreaWidth = Mathf.Max(panelWidth - horizontalPadding, InlineButtonMinWidth);
            var columns = CalculateInlineColumnCount(cellAreaWidth, activeCount);
            var rows = Mathf.CeilToInt(activeCount / (float)columns);
            var width = Mathf.Clamp(
                (cellAreaWidth - InlineButtonSpacing * Math.Max(columns - 1, 0)) / columns,
                InlineButtonMinWidth,
                InlineButtonMaxWidth);
            panelHeight = verticalPadding
                + rows * InlineButtonHeight
                + InlineButtonSpacing * Math.Max(rows - 1, 0);

            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.cellSize = new Vector2(width, InlineButtonHeight);
            grid.spacing = new Vector2(InlineButtonSpacing, InlineButtonSpacing);
        }

        var searchLayout = rect.parent == null
            ? null
            : rect.parent.GetComponent<CharacterSearchLayoutMarker>();
        if (searchLayout != null && searchLayout.Active)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, panelHeight);
        }

        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, panelWidth);
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);
        var layoutElement = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = false;
        layoutElement.minHeight = panelHeight;
        layoutElement.preferredHeight = panelHeight;
        layoutElement.minWidth = -1f;
        layoutElement.preferredWidth = -1f;
        layoutElement.flexibleHeight = 0f;
        layoutElement.flexibleWidth = 1f;

        _owner?.GetComponent<FilterEntryFullRowLayoutState>()?.RefreshInlineHeight();
        UiLayoutRefreshQueue.Request(contentRoot);
        UiLayoutRefreshQueue.Request(rect, parentDepth: 6);
    }

    internal float GetInlinePanelPreferredHeight()
    {
        if (_inlineSection == null || !_inlineSection.gameObject.activeInHierarchy)
            return 0f;

        var rect = _inlineSection.transform as RectTransform;
        var layout = rect == null ? null : rect.GetComponent<LayoutElement>();
        var inlineHeight = layout != null && layout.preferredHeight > 1f
            ? layout.preferredHeight
            : rect == null
                ? 0f
                : Mathf.Max(0f, rect.rect.height);
        var searchLayout = rect == null || rect.parent == null
            ? null
            : rect.parent.GetComponent<CharacterSearchLayoutMarker>();

        return searchLayout == null
            ? inlineHeight
            : searchLayout.GetCombinedHeight(inlineHeight);
    }

    private void TuneSummaryAreaLayout()
    {
        if (_owner == null)
            return;

        var summaryArea = OwnerSummaryAreaField?.GetValue(_owner) as RectTransform;
        if (summaryArea == null || !summaryArea.gameObject.activeSelf)
            return;

        summaryArea.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, CompactSummaryHeight);
        var layoutElement = summaryArea.GetComponent<LayoutElement>() ?? summaryArea.gameObject.AddComponent<LayoutElement>();
        layoutElement.minHeight = CompactSummaryHeight;
        layoutElement.preferredHeight = CompactSummaryHeight;
        layoutElement.flexibleHeight = 0f;

        var summaryRoot = OwnerSummaryRootField?.GetValue(_owner) as RectTransform;
        if (summaryRoot != null)
            UiLayoutRefreshQueue.Request(summaryRoot);

        UiLayoutRefreshQueue.Request(summaryArea);
    }

    private static int CalculateInlineColumnCount(float availableWidth, int activeCount)
    {
        if (availableWidth <= 0f)
            return activeCount;

        var oneRowMinWidth = activeCount * InlineButtonMinWidth + InlineButtonSpacing * Math.Max(activeCount - 1, 0);
        if (oneRowMinWidth <= availableWidth)
            return activeCount;

        var maxColumnsAtMinWidth = Mathf.Max(
            1,
            Mathf.FloorToInt((availableWidth + InlineButtonSpacing) / (InlineButtonMinWidth + InlineButtonSpacing)));
        var preferredColumns = Mathf.Max(
            1,
            Mathf.RoundToInt((availableWidth + InlineButtonSpacing) / (InlineButtonPreferredWidth + InlineButtonSpacing)));
        return Mathf.Clamp(Math.Min(preferredColumns, maxColumnsAtMinWidth), 1, activeCount);
    }

    private float GetInlineAvailableWidth(RectTransform rect)
    {
        var parent = rect == null ? null : rect.parent as RectTransform;
        if (!ReferenceEquals(parent, _stableInlineWidthParent))
        {
            _stableInlineWidthParent = parent;
            _stableInlineAvailableWidth = 0f;
        }

        var width = ResolveStableInlineAvailableWidth(rect, parent);
        if (width >= InlineButtonMinWidth)
            _stableInlineAvailableWidth = width;

        return Mathf.Max(_stableInlineAvailableWidth, InlineButtonMinWidth);
    }

    private float ResolveStableInlineAvailableWidth(RectTransform rect, RectTransform parent)
    {
        var width = GetInnerRectWidth(parent);
        if (width >= InlineButtonMinWidth)
            return width;

        width = GetRectWidth(rect);
        if (width >= InlineButtonMinWidth)
            return width;

        return GetNearestUsableAncestorWidth(_entryRect ?? rect);
    }

    private static float GetNearestUsableAncestorWidth(RectTransform start)
    {
        if (start == null)
            return 0f;

        var current = start.parent as RectTransform;
        var depth = 0;
        while (current != null && depth < 12)
        {
            var width = GetInnerRectWidth(current);
            if (width >= InlineButtonMinWidth)
                return width;

            current = current.parent as RectTransform;
            depth++;
        }

        return 0f;
    }

    private static float GetInnerRectWidth(RectTransform rect)
    {
        var width = GetRectWidth(rect);
        if (width <= 0f || rect == null)
            return width;

        var horizontalOrVertical = rect.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (horizontalOrVertical != null && horizontalOrVertical.enabled)
            return Mathf.Max(0f, width - horizontalOrVertical.padding.left - horizontalOrVertical.padding.right);

        var grid = rect.GetComponent<GridLayoutGroup>();
        if (grid != null && grid.enabled)
            return Mathf.Max(0f, width - grid.padding.left - grid.padding.right);

        return width;
    }

    private static float GetRectWidth(RectTransform rect)
    {
        if (rect == null)
            return 0f;

        var width = Mathf.Abs(rect.rect.width);
        if (width <= 0f)
            width = Mathf.Abs(rect.sizeDelta.x);
        if (width <= 0f)
            width = Mathf.Abs(LayoutUtility.GetPreferredWidth(rect));

        return width;
    }

    private void ResetStableInlineWidth()
    {
        _stableInlineWidthParent = null;
        _stableInlineAvailableWidth = 0f;
    }

    private static void ApplyInlineOptionTextLayout(RectTransform contentRoot)
    {
        if (contentRoot == null)
            return;

        for (var i = 0; i < contentRoot.childCount; i++)
        {
            var child = contentRoot.GetChild(i);
            if (child == null || !child.gameObject.activeSelf)
                continue;

            foreach (var label in child.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (label == null)
                    continue;

                var labelRect = label.transform as RectTransform;
                if (labelRect != null)
                {
                    labelRect.anchorMin = Vector2.zero;
                    labelRect.anchorMax = Vector2.one;
                    labelRect.offsetMin = new Vector2(2f, 0f);
                    labelRect.offsetMax = new Vector2(-2f, 0f);
                    labelRect.pivot = new Vector2(0.5f, 0.5f);
                }

                label.alignment = TextAlignmentOptions.Center;
                label.enableWordWrapping = false;
            }
        }
    }

    private void ApplyShortInlineLabels(RectTransform contentRoot)
    {
        foreach (var label in contentRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label == null)
                continue;

            if (FilterLabelAbbreviations.TryShorten(label.text, out var shortened))
                label.SetText(shortened);

            label.enableAutoSizing = true;
            label.fontSizeMax = Math.Min(label.fontSizeMax <= 0f ? 28f : label.fontSizeMax, 20f);
            label.fontSizeMin = Math.Max(label.fontSizeMin, 12f);
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    private void ApplyInlineFollowUpUnderlines(RectTransform contentRoot)
    {
        if (contentRoot == null)
            return;

        for (var i = 0; i < contentRoot.childCount; i++)
        {
            var child = contentRoot.GetChild(i);
            if (child == null)
                continue;

            var originalOptionIndex = ToOriginalOptionIndex(i - 1);
            var hasFollowUp = originalOptionIndex >= 0 && _inlineOptionsWithFollowUpMenus.Contains(originalOptionIndex);
            foreach (var label in child.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (label == null)
                    continue;

                label.fontStyle = hasFollowUp
                    ? label.fontStyle | FontStyles.Underline
                    : label.fontStyle & ~FontStyles.Underline;
            }
        }
    }

    private void AttachInlineClickRelays(RectTransform contentRoot)
    {
        if (contentRoot == null)
            return;

        for (var i = 0; i < contentRoot.childCount; i++)
        {
            var child = contentRoot.GetChild(i);
            if (child == null || !child.gameObject.activeSelf)
                continue;

            var relay = child.GetComponent<InlineFilterOptionClickRelay>() ?? child.gameObject.AddComponent<InlineFilterOptionClickRelay>();
            relay.Setup(this, i - 1);
            FilterMultiSelectSupport.BindOption(child.gameObject, _owner, _inlineSection, _lineId, _menuId, ToOriginalOptionIndex(i - 1));
        }
    }

    private void RefreshInlineFollowUpOptionSet()
    {
        _inlineOptionsWithFollowUpMenus.Clear();
        if (_owner == null || _owner.Config?.LineConfigs == null || _lineIndex < 0 || _lineIndex >= _owner.Config.LineConfigs.Count)
            return;

        var lineConfig = _owner.Config.LineConfigs[_lineIndex];
        var menuConfigs = lineConfig.DetailedFilterLineConfig?.Config.MenuConfigs;
        if (menuConfigs != null)
        {
            var menuConfigMap = new Dictionary<int, DetailedFilterMenuConfig>();
            foreach (var menuConfig in menuConfigs)
            {
                var maybeConfig = _owner.GetMenuConfig(_lineIndex, menuConfig.Id);
                menuConfigMap[menuConfig.Id] = maybeConfig ?? menuConfig;
            }

            foreach (var entry in menuConfigMap)
            {
                var dependency = entry.Value.DropdownContext.Dependency;
                if (!dependency.HasValue)
                    continue;

                var itemConfigs = menuConfigMap.TryGetValue(_menuId, out var rootMenu)
                    ? rootMenu.DropdownConfig.ItemConfigs
                    : null;
                var count = itemConfigs?.Count ?? 0;
                for (var i = 0; i < count; i++)
                {
                    if (DoesMenuDependOnOption(entry.Value, _menuId, i, menuConfigMap, null))
                        _inlineOptionsWithFollowUpMenus.Add(i);
                }
            }
        }

        for (var i = 0; i < _inlineOptionIndexMap.Count; i++)
        {
            var originalOptionIndex = _inlineOptionIndexMap[i];
            if (SimplifiedFilterToggleVisual.HasLineDependingOnToggle(_owner.Config.LineConfigs, _lineId, originalOptionIndex))
                _inlineOptionsWithFollowUpMenus.Add(originalOptionIndex);
        }
    }

    private static bool DoesMenuDependOnOption(
        DetailedFilterMenuConfig menuConfig,
        int targetMenuId,
        int targetOptionIndex,
        Dictionary<int, DetailedFilterMenuConfig> menuConfigMap,
        HashSet<int> visitingMenuIds)
    {
        var dependency = menuConfig.DropdownContext.Dependency;
        if (!dependency.HasValue)
            return false;

        var value = dependency.Value;
        if (value.MenuId == targetMenuId && value.OptionIndex == targetOptionIndex)
            return true;

        visitingMenuIds ??= new HashSet<int>();
        if (!visitingMenuIds.Add(menuConfig.Id))
            return false;

        var result = menuConfigMap.TryGetValue(value.MenuId, out var parentMenuConfig)
            && DoesMenuDependOnOption(parentMenuConfig, targetMenuId, targetOptionIndex, menuConfigMap, visitingMenuIds);
        visitingMenuIds.Remove(menuConfig.Id);
        return result;
    }

    internal bool IsInlineDisplayOptionSelected(int displayIndex)
    {
        if (_owner == null)
            return false;

        var originalIndex = ToOriginalOptionIndex(displayIndex);
        var selected = FilterMultiSelectSupport.GetSelectedIndices(_owner, _lineId, _menuId);
        return originalIndex < 0 ? selected.Count == 0 : selected.Contains(originalIndex);
    }

    internal void OnInlineDisplayOptionRepeatedClick(int displayIndex)
    {
        if (_owner == null)
            return;

        if (displayIndex < 0)
        {
            _showRootSectionInPanel = true;
            _owner.OpenFilterPanel();
            ShowPanelRootSection();
            return;
        }

        _showRootSectionInPanel = false;
        if (!HasFollowUpSectionsForDisplayOption(displayIndex))
        {
            _owner.CloseFilterPanel();
            return;
        }

        _owner.OpenFilterPanel();
        HidePanelRootSection();
    }

    private void OnInlineSelectionChanged(int selectedIndex)
    {
        if (_owner == null)
            return;

        _showRootSectionInPanel = false;
        _owner.SetDropdownOption(_lineId, _menuId, ToOriginalOptionIndex(selectedIndex));
        FilterMemoryController.TrySave(_owner);
        _owner.CloseFilterPanel();
    }

    private bool HasFollowUpSectionsForDisplayOption(int displayIndex)
    {
        var originalIndex = ToOriginalOptionIndex(displayIndex);
        if (originalIndex >= 0 && _inlineOptionsWithFollowUpMenus.Contains(originalIndex))
            return true;

        foreach (var section in _owner.Sections)
        {
            if (!section.IsActive || section.Type == Game.Components.SortAndFilter.ESortAndFilterOneLineType.ToggleGroup)
                continue;

            if (section.LineId != _lineId || section.MenuId != _menuId)
                return true;
        }

        return false;
    }

    private void HidePanelRootSection()
    {
        if (_filterPanel == null)
            return;

        var sectionMap = PanelSectionMapField?.GetValue(_filterPanel)
            as Dictionary<(int, int), FilterSection>;
        if (sectionMap == null)
            return;

        if (sectionMap.TryGetValue((_lineId, _menuId), out var rootSection) && rootSection != null && rootSection != _inlineSection)
            rootSection.gameObject.SetActive(false);
    }

    private void ShowPanelRootSection()
    {
        if (_filterPanel == null)
            return;

        var sectionMap = PanelSectionMapField?.GetValue(_filterPanel)
            as Dictionary<(int, int), FilterSection>;
        if (sectionMap == null)
            return;

        if (sectionMap.TryGetValue((_lineId, _menuId), out var rootSection) && rootSection != null && rootSection != _inlineSection)
            rootSection.gameObject.SetActive(true);
    }

    private void HideEntryButton()
    {
        if (_owner != null && !_entryForceHiddenByInline)
        {
            _originalEntryForceHidden = OwnerForceHideEntryField?.GetValue(_owner) is bool hidden && hidden;
            _owner.SetEntryButtonForceHidden(true);
            _entryForceHiddenByInline = true;
        }

        if (_entryRect != null)
            _entryRect.gameObject.SetActive(false);
    }

    private void RestoreEntryButton()
    {
        if (_owner == null)
            return;

        if (_entryForceHiddenByInline)
        {
            _owner.SetEntryButtonForceHidden(_originalEntryForceHidden);
            _entryForceHiddenByInline = false;
        }

        if (_entryRect == null)
            return;

        var forceHideEntry = OwnerForceHideEntryField?.GetValue(_owner) is bool hidden && hidden;
        _entryRect.gameObject.SetActive(!forceHideEntry && _owner.ShowFilterEntryButton);
    }

    private void SetInlineActive(bool active)
    {
        if (_inlineSection != null)
            _inlineSection.gameObject.SetActive(active);
        if (!active)
        {
            if (_layoutRefreshRoutine != null)
            {
                StopCoroutine(_layoutRefreshRoutine);
                _layoutRefreshRoutine = null;
            }
            _showRootSectionInPanel = false;
            ResetStableInlineWidth();
        }
    }

    private void RefreshOwnerSummary()
    {
        if (_owner == null)
            return;

        OwnerRefreshSummaryMethod?.Invoke(_owner, null);
    }

    private string BuildInlineRefreshSignature(
        SortAndFilter.SectionViewData section,
        List<FilterDropdownItemConfig> itemConfigs,
        float panelWidth)
    {
        var builder = new StringBuilder(128);
        builder.Append(section.LineId).Append('|')
            .Append(section.LineIndex).Append('|')
            .Append(section.MenuId).Append('|')
            .Append(Mathf.RoundToInt(panelWidth)).Append('|')
            .Append(itemConfigs == null ? 0 : itemConfigs.Count);

        if (itemConfigs != null)
        {
            for (var i = 0; i < itemConfigs.Count; i++)
                builder.Append('|').Append(itemConfigs[i].Text.GetString());
        }

        return builder.ToString();
    }

    private static int CountActiveChildren(RectTransform root)
    {
        var count = 0;
        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child != null && child.gameObject.activeSelf)
                count++;
        }

        return count;
    }
}

internal sealed class InlineFilterOptionClickRelay : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    private InlineFilterButtonsController _owner;
    private int _displayIndex;
    private bool _wasSelectedOnPointerDown;

    internal void Setup(InlineFilterButtonsController owner, int displayIndex)
    {
        _owner = owner;
        _displayIndex = displayIndex;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _wasSelectedOnPointerDown = _owner != null && _owner.IsInlineDisplayOptionSelected(_displayIndex);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_wasSelectedOnPointerDown)
            _owner?.OnInlineDisplayOptionRepeatedClick(_displayIndex);
    }
}

[HarmonyPatch(typeof(SortAndFilter), "Setup")]
internal static class SortAndFilterSetupFilterEntryFullRowPatch
{
    private static void Postfix(SortAndFilter __instance)
    {
        FilterEntryFullRowLayoutSupport.Apply(__instance);
    }
}

[HarmonyPatch(typeof(SortAndFilter), "RefreshEntryVisibility")]
internal static class SortAndFilterRefreshEntryVisibilityFullRowPatch
{
    private static void Postfix(SortAndFilter __instance)
    {
        FilterEntryFullRowLayoutSupport.Apply(__instance);
    }
}

internal static class FilterEntryFullRowLayoutSupport
{
    private const string RowName = "BetterTaiwuScrollFilterEntryRow";
    private const float RowHeight = 56f;
    private const float HorizontalPadding = 0f;

    private static readonly FieldInfo EntryToggleField =
        AccessTools.Field(typeof(SortAndFilter), "entryToggle");

    private static readonly FieldInfo FirstToggleGroupLineField =
        AccessTools.Field(typeof(SortAndFilter), "firstToggleGroupLine");

    internal static void Apply(SortAndFilter owner)
    {
        if (owner == null)
            return;

        var state = owner.GetComponent<FilterEntryFullRowLayoutState>()
            ?? owner.gameObject.AddComponent<FilterEntryFullRowLayoutState>();

        if (!Plugin.EnableFilterEntryFullRow)
        {
            state.Restore();
            return;
        }

        // ViewMapBlockCharList already reserves one compact horizontal strip for its
        // affiliation filters and search box.  Splitting the entry into another row pushes
        // the filters below the search box and leaves the top row half empty.  Keep this one
        // view on its original same-row layout without changing the global setting.
        if (owner.GetComponentInParent<Game.Views.MapBlockCharList.ViewMapBlockCharList>(true) != null)
        {
            state.Restore();
            return;
        }

        if (!HasInlineRootCandidate(owner))
        {
            state.Restore();
            return;
        }

        var entryToggle = EntryToggleField?.GetValue(owner) as Component;
        var firstToggleGroupLine = FirstToggleGroupLineField?.GetValue(owner) as Component;
        state.Apply(owner, entryToggle, firstToggleGroupLine);
    }

    internal static bool HasInlineRootCandidate(SortAndFilter owner)
    {
        if (owner?.Config?.LineConfigs == null || owner.Sections == null)
            return false;

        foreach (var candidate in owner.Sections)
        {
            if (!candidate.IsActive)
                continue;

            if (candidate.LineIndex < 0 || candidate.LineIndex >= owner.Config.LineConfigs.Count)
                continue;

            var maybeConfig = owner.GetMenuConfig(candidate.LineIndex, candidate.MenuId);
            if (!maybeConfig.HasValue)
                continue;

            var menuConfig = maybeConfig.Value;
            if (menuConfig.DropdownContext.Dependency.HasValue)
                continue;

            var itemConfigs = menuConfig.DropdownConfig.ItemConfigs;
            if (itemConfigs != null && itemConfigs.Count > 0)
                return true;
        }

        return false;
    }

    internal static void RefreshAllActive(bool allowRestore)
    {
        foreach (var owner in Resources.FindObjectsOfTypeAll<SortAndFilter>())
        {
            if (owner == null)
                continue;

            if (Plugin.EnableFilterEntryFullRow)
                Apply(owner);
            else if (allowRestore)
                owner.GetComponent<FilterEntryFullRowLayoutState>()?.Restore();
        }
    }

    internal static void RestoreAll()
    {
        foreach (var state in Resources.FindObjectsOfTypeAll<FilterEntryFullRowLayoutState>())
            state.Restore();
    }

    internal static bool LooksLikeHorizontalContainer(RectTransform parent)
    {
        if (parent == null)
            return false;

        var horizontal = parent.GetComponent<HorizontalLayoutGroup>();
        if (horizontal != null && horizontal.enabled)
            return true;

        var grid = parent.GetComponent<GridLayoutGroup>();
        if (grid != null && grid.enabled && grid.constraint == GridLayoutGroup.Constraint.FixedRowCount)
            return true;

        return false;
    }

    internal static void PrepareFullWidthRow(RectTransform row, RectTransform sourceParent, RectTransform reference)
    {
        if (row == null)
            return;

        row.name = RowName;
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.offsetMin = new Vector2(HorizontalPadding, row.offsetMin.y);
        row.offsetMax = new Vector2(-HorizontalPadding, row.offsetMax.y);
        row.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, RowHeight);

        var layout = row.GetComponent<HorizontalLayoutGroup>() ?? row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.enabled = true;
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 0f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = row.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            UnityEngine.Object.Destroy(fitter);

        var layoutElement = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
        layoutElement.minWidth = -1f;
        layoutElement.preferredWidth = -1f;
        layoutElement.flexibleWidth = 1f;
        layoutElement.minHeight = RowHeight;
        layoutElement.preferredHeight = RowHeight;
        layoutElement.flexibleHeight = 0f;

        if (sourceParent != null)
        {
            row.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, sourceParent.rect.width);
            if (!ParentUsesLayout(sourceParent) && reference != null)
            {
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(sourceParent, reference);
                var targetTopY = bounds.min.y - 4f;
                row.anchoredPosition = new Vector2(0f, targetTopY - sourceParent.rect.yMax);
            }
        }
    }

    internal static bool ParentUsesLayout(RectTransform parent)
    {
        if (parent == null)
            return false;

        var horizontal = parent.GetComponent<HorizontalLayoutGroup>();
        if (horizontal != null && horizontal.enabled)
            return true;

        var vertical = parent.GetComponent<VerticalLayoutGroup>();
        if (vertical != null && vertical.enabled)
            return true;

        var grid = parent.GetComponent<GridLayoutGroup>();
        return grid != null && grid.enabled;
    }

    internal static bool IsSameVisualRow(RectTransform first, RectTransform second, RectTransform relativeTo)
    {
        if (first == null || second == null || relativeTo == null)
            return false;

        var firstBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(relativeTo, first);
        var secondBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(relativeTo, second);
        var firstCenter = (firstBounds.min.y + firstBounds.max.y) * 0.5f;
        var secondCenter = (secondBounds.min.y + secondBounds.max.y) * 0.5f;
        var firstHeight = Mathf.Max(firstBounds.size.y, 1f);
        var secondHeight = Mathf.Max(secondBounds.size.y, 1f);
        return Mathf.Abs(firstCenter - secondCenter) <= Mathf.Max(firstHeight, secondHeight) * 0.65f;
    }

    internal static RectTransform FindCommonAncestor(RectTransform first, RectTransform second)
    {
        if (first == null || second == null)
            return null;

        var ancestors = new HashSet<Transform>();
        for (var current = first; current != null; current = current.parent as RectTransform)
            ancestors.Add(current);

        for (var current = second; current != null; current = current.parent as RectTransform)
        {
            if (ancestors.Contains(current))
                return current;
        }

        return null;
    }

    internal static RectTransform FindDirectChildUnder(RectTransform ancestor, RectTransform descendant)
    {
        if (ancestor == null || descendant == null || ReferenceEquals(ancestor, descendant))
            return descendant;

        var current = descendant;
        var parent = current.parent as RectTransform;
        while (parent != null && !ReferenceEquals(parent, ancestor))
        {
            current = parent;
            parent = current.parent as RectTransform;
        }

        return ReferenceEquals(parent, ancestor) ? current : null;
    }
}

internal sealed class FilterEntryFullRowLayoutState : MonoBehaviour
{
    private const float RowSpacing = 4f;
    private const float FallbackToggleRowHeight = 42f;
    private const float FallbackEntryRowHeight = 52f;

    private RectTransform _entryRect;
    private RectTransform _toggleLineRect;
    private RectTransform _entryBranch;
    private RectTransform _toggleBranch;
    private RectTransform _rowContainer;
    private RectTransform _layoutParent;
    private RectTransform _entryOriginalParent;
    private int _entryBranchOriginalSiblingIndex;
    private int _toggleBranchOriginalSiblingIndex;
    private HorizontalLayoutGroup _horizontalLayout;
    private VerticalLayoutGroup _verticalLayout;
    private bool _originalHorizontalEnabled;
    private bool _hadVerticalLayout;
    private bool _originalVerticalEnabled;
    private LayoutGroupSnapshot _verticalLayoutSnapshot;
    private LayoutElementSnapshot _rowContainerLayoutSnapshot;
    private LayoutElementSnapshot _entryBranchLayoutSnapshot;
    private LayoutElementSnapshot _toggleBranchLayoutSnapshot;
    private LayoutElementSnapshot _entryLayoutSnapshot;
    private bool _hasOriginal;

    internal void Apply(SortAndFilter owner, Component entryToggle, Component firstToggleGroupLine)
    {
        if (owner == null || entryToggle == null || firstToggleGroupLine == null)
        {
            Restore();
            return;
        }

        _entryRect = entryToggle.transform as RectTransform;
        _toggleLineRect = firstToggleGroupLine.transform as RectTransform;
        if (_entryRect == null || _toggleLineRect == null)
        {
            Restore();
            return;
        }

        if (!_hasOriginal)
        {
            if (!TryCaptureOriginalPlacement())
                return;
        }

        if (_rowContainer == null || _entryBranch == null || _toggleBranch == null || _entryRect == null)
        {
            Restore();
            return;
        }

        ApplyLayout();
        RequestLayout();
    }

    internal void RefreshInlineHeight()
    {
        if (!_hasOriginal || !Plugin.EnableFilterEntryFullRow)
            return;

        ApplyLayout();
        RequestLayout();
    }

    internal void Restore()
    {
        if (!_hasOriginal)
            return;

        RestoreSiblingOrder();
        if (_entryBranch != null && _entryOriginalParent != null && !ReferenceEquals(_entryBranch.parent, _entryOriginalParent))
        {
            _entryBranch.SetParent(_entryOriginalParent, false);
            _entryBranch.SetSiblingIndex(Mathf.Clamp(_entryBranchOriginalSiblingIndex, 0, _entryOriginalParent.childCount - 1));
        }

        if (_horizontalLayout != null)
            _horizontalLayout.enabled = _originalHorizontalEnabled;

        if (_verticalLayout != null)
        {
            if (_hadVerticalLayout)
            {
                _verticalLayoutSnapshot?.Restore(_verticalLayout);
                _verticalLayout.enabled = _originalVerticalEnabled;
            }
            else
            {
                Destroy(_verticalLayout);
                _verticalLayout = null;
            }
        }

        _entryLayoutSnapshot?.Restore();
        _toggleBranchLayoutSnapshot?.Restore();
        _entryBranchLayoutSnapshot?.Restore();
        _rowContainerLayoutSnapshot?.Restore();

        _hasOriginal = false;
        RequestLayout();
    }

    private bool TryCaptureOriginalPlacement()
    {
        if (_entryRect == null || _toggleLineRect == null)
            return false;

        var common = FilterEntryFullRowLayoutSupport.FindCommonAncestor(_entryRect, _toggleLineRect);
        if (common == null || common.GetComponentInParent<SortAndFilter>(true) != GetComponent<SortAndFilter>())
            return false;

        var entryBranch = FilterEntryFullRowLayoutSupport.FindDirectChildUnder(common, _entryRect);
        var toggleBranch = FilterEntryFullRowLayoutSupport.FindDirectChildUnder(common, _toggleLineRect);
        if (entryBranch == null || toggleBranch == null || ReferenceEquals(entryBranch, toggleBranch))
            return false;

        var layoutParent = common.parent as RectTransform;
        if (layoutParent == null)
            return false;

        _horizontalLayout = common.GetComponent<HorizontalLayoutGroup>();
        if (_horizontalLayout == null)
            return false;

        _rowContainer = common;
        _layoutParent = layoutParent;
        _entryBranch = entryBranch;
        _toggleBranch = toggleBranch;
        _entryOriginalParent = _entryBranch.parent as RectTransform;
        _entryBranchOriginalSiblingIndex = _entryBranch.GetSiblingIndex();
        _toggleBranchOriginalSiblingIndex = _toggleBranch.GetSiblingIndex();
        _originalHorizontalEnabled = _horizontalLayout.enabled;

        _verticalLayout = _rowContainer.GetComponent<VerticalLayoutGroup>();
        _hadVerticalLayout = _verticalLayout != null;
        if (_verticalLayout != null)
        {
            _originalVerticalEnabled = _verticalLayout.enabled;
            _verticalLayoutSnapshot = LayoutGroupSnapshot.Capture(_verticalLayout);
        }

        _rowContainerLayoutSnapshot = LayoutElementSnapshot.Capture(_rowContainer);
        _entryBranchLayoutSnapshot = LayoutElementSnapshot.Capture(_entryBranch);
        _toggleBranchLayoutSnapshot = LayoutElementSnapshot.Capture(_toggleBranch);
        _entryLayoutSnapshot = LayoutElementSnapshot.Capture(_entryRect);

        _hasOriginal = true;
        return true;
    }

    private void ApplyLayout()
    {
        if (_rowContainer == null || _layoutParent == null || _entryBranch == null || _toggleBranch == null || _entryRect == null)
            return;

        if (_horizontalLayout != null)
            _horizontalLayout.enabled = _originalHorizontalEnabled;

        if (!ReferenceEquals(_rowContainer.parent, _layoutParent))
            return;

        if (!ReferenceEquals(_toggleBranch.parent, _rowContainer))
            _toggleBranch.SetParent(_rowContainer, false);

        if (!ReferenceEquals(_entryBranch.parent, _layoutParent))
            _entryBranch.SetParent(_layoutParent, false);

        _entryBranch.SetSiblingIndex(Mathf.Clamp(_rowContainer.GetSiblingIndex() + 1, 0, _layoutParent.childCount - 1));

        var toggleHeight = ResolvePreferredHeight(_toggleBranch, FallbackToggleRowHeight);
        var entryHeight = ResolvePreferredHeight(_entryRect, FallbackEntryRowHeight);
        var inlineHeight = GetComponent<InlineFilterButtonsController>()?.GetInlinePanelPreferredHeight() ?? 0f;
        entryHeight = Mathf.Max(entryHeight, inlineHeight);

        ConfigureLayoutElement(_rowContainer, -1f, -1f, 1f, toggleHeight, toggleHeight, 0f);
        ConfigureLayoutElement(_toggleBranch, -1f, -1f, 1f, toggleHeight, toggleHeight, 0f);
        ConfigureLayoutElement(_entryBranch, -1f, -1f, 1f, entryHeight, entryHeight, 0f);
        ConfigureLayoutElement(_entryRect, -1f, -1f, 1f, entryHeight, entryHeight, 0f);

        var entryHorizontal = _entryBranch.GetComponent<HorizontalLayoutGroup>();
        if (entryHorizontal != null)
        {
            var searchLayout = _entryBranch.GetComponentInChildren<CharacterSearchLayoutMarker>(true);
            if (searchLayout != null && searchLayout.Active)
            {
                entryHorizontal.enabled = false;
            }
            else
            {
                entryHorizontal.enabled = true;
                entryHorizontal.childAlignment = TextAnchor.MiddleCenter;
                entryHorizontal.childControlWidth = true;
                entryHorizontal.childControlHeight = true;
                entryHorizontal.childForceExpandWidth = true;
                entryHorizontal.childForceExpandHeight = false;
            }
        }
    }

    private void ConfigureVerticalLayout(VerticalLayoutGroup layout)
    {
        if (layout == null)
            return;

        layout.enabled = true;
        layout.padding = _horizontalLayout != null
            ? new RectOffset(
                _horizontalLayout.padding.left,
                _horizontalLayout.padding.right,
                _horizontalLayout.padding.top,
                _horizontalLayout.padding.bottom)
            : new RectOffset(0, 0, 0, 0);
        layout.spacing = RowSpacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private static void ConfigureLayoutElement(
        RectTransform rect,
        float minWidth,
        float preferredWidth,
        float flexibleWidth,
        float minHeight,
        float preferredHeight,
        float flexibleHeight)
    {
        if (rect == null)
            return;

        var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = false;
        layout.minWidth = minWidth;
        layout.preferredWidth = preferredWidth;
        layout.flexibleWidth = flexibleWidth;
        layout.minHeight = minHeight;
        layout.preferredHeight = preferredHeight;
        layout.flexibleHeight = flexibleHeight;
    }

    private static float ResolvePreferredHeight(RectTransform rect, float fallback)
    {
        if (rect == null)
            return fallback;

        var layout = rect.GetComponent<LayoutElement>();
        if (layout != null && layout.preferredHeight > 1f)
            return Mathf.Clamp(layout.preferredHeight, 24f, 80f);

        var preferred = LayoutUtility.GetPreferredHeight(rect);
        if (preferred > 1f)
            return Mathf.Clamp(preferred, 24f, 80f);

        if (rect.rect.height > 1f)
            return Mathf.Clamp(rect.rect.height, 24f, 80f);

        return fallback;
    }

    private void RestoreSiblingOrder()
    {
        if (_rowContainer == null || _entryBranch == null || _toggleBranch == null)
            return;

        if (_entryOriginalParent != null)
        {
            if (!ReferenceEquals(_entryBranch.parent, _entryOriginalParent))
                _entryBranch.SetParent(_entryOriginalParent, false);
            _entryBranch.SetSiblingIndex(Mathf.Clamp(_entryBranchOriginalSiblingIndex, 0, _entryOriginalParent.childCount - 1));
        }

        if (ReferenceEquals(_toggleBranch.parent, _rowContainer))
        {
            _toggleBranch.SetSiblingIndex(Mathf.Clamp(_toggleBranchOriginalSiblingIndex, 0, _rowContainer.childCount - 1));
        }
    }

    private void RequestLayout()
    {
        if (_entryRect != null)
            UiLayoutRefreshQueue.Request(_entryRect);
        if (_entryBranch != null)
            UiLayoutRefreshQueue.Request(_entryBranch);
        if (_toggleBranch != null)
            UiLayoutRefreshQueue.Request(_toggleBranch);
        if (_rowContainer != null)
            UiLayoutRefreshQueue.Request(_rowContainer, parentDepth: 3);
        if (_layoutParent != null)
            UiLayoutRefreshQueue.Request(_layoutParent, parentDepth: 2);
    }

    private sealed class LayoutGroupSnapshot
    {
        private RectOffset _padding;
        private float _spacing;
        private TextAnchor _childAlignment;
        private bool _childControlWidth;
        private bool _childControlHeight;
        private bool _childForceExpandWidth;
        private bool _childForceExpandHeight;

        internal static LayoutGroupSnapshot Capture(HorizontalOrVerticalLayoutGroup layout)
        {
            if (layout == null)
                return null;

            return new LayoutGroupSnapshot
            {
                _padding = new RectOffset(
                    layout.padding.left,
                    layout.padding.right,
                    layout.padding.top,
                    layout.padding.bottom),
                _spacing = layout.spacing,
                _childAlignment = layout.childAlignment,
                _childControlWidth = layout.childControlWidth,
                _childControlHeight = layout.childControlHeight,
                _childForceExpandWidth = layout.childForceExpandWidth,
                _childForceExpandHeight = layout.childForceExpandHeight,
            };
        }

        internal void Restore(HorizontalOrVerticalLayoutGroup layout)
        {
            if (layout == null)
                return;

            layout.padding = new RectOffset(_padding.left, _padding.right, _padding.top, _padding.bottom);
            layout.spacing = _spacing;
            layout.childAlignment = _childAlignment;
            layout.childControlWidth = _childControlWidth;
            layout.childControlHeight = _childControlHeight;
            layout.childForceExpandWidth = _childForceExpandWidth;
            layout.childForceExpandHeight = _childForceExpandHeight;
        }
    }

    private sealed class LayoutElementSnapshot
    {
        private RectTransform _rect;
        private LayoutElement _element;
        private bool _hadElement;
        private bool _ignoreLayout;
        private float _minWidth;
        private float _preferredWidth;
        private float _flexibleWidth;
        private float _minHeight;
        private float _preferredHeight;
        private float _flexibleHeight;

        internal static LayoutElementSnapshot Capture(RectTransform rect)
        {
            if (rect == null)
                return null;

            var element = rect.GetComponent<LayoutElement>();
            var snapshot = new LayoutElementSnapshot
            {
                _rect = rect,
                _element = element,
                _hadElement = element != null,
            };

            if (element != null)
            {
                snapshot._ignoreLayout = element.ignoreLayout;
                snapshot._minWidth = element.minWidth;
                snapshot._preferredWidth = element.preferredWidth;
                snapshot._flexibleWidth = element.flexibleWidth;
                snapshot._minHeight = element.minHeight;
                snapshot._preferredHeight = element.preferredHeight;
                snapshot._flexibleHeight = element.flexibleHeight;
            }

            return snapshot;
        }

        internal void Restore()
        {
            if (_rect == null)
                return;

            _element ??= _rect.GetComponent<LayoutElement>();
            if (_element == null)
                return;

            if (!_hadElement)
            {
                UnityEngine.Object.Destroy(_element);
                _element = null;
                return;
            }

            _element.ignoreLayout = _ignoreLayout;
            _element.minWidth = _minWidth;
            _element.preferredWidth = _preferredWidth;
            _element.flexibleWidth = _flexibleWidth;
            _element.minHeight = _minHeight;
            _element.preferredHeight = _preferredHeight;
            _element.flexibleHeight = _flexibleHeight;
        }
    }
}
