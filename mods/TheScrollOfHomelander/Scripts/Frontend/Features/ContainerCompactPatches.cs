#nullable disable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using FrameWork.UISystem.Components;
using FrameWork.UISystem.UIElements;
using Game.Components.ListStyleGeneralScroll.Item;
using GameData.Domains.CombatSkill;
using GameData.Domains.Taiwu.ExchangeSystem;
using Game.Views.CharacterMenu;
using Game.Views.Exchange;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTaiwuScroll.Frontend;

[HarmonyPatch]
internal static class ContainerCompactPatches
{
    private const int MaxParentDepth = 18;

    private static readonly string[] PageViewNames =
    {
        "Game.Views.CharacterMenu.ViewCharacterMenuItems",
        "Game.Views.Exchange.ViewExchangeBase",
    };

    private static readonly FieldInfo CellSizeField = AccessTools.Field(typeof(InfinityScroll), "_cellSize");
    private static readonly FieldInfo ContainerField = AccessTools.Field(typeof(InfinityScroll), "_container");
    private static readonly FieldInfo DirectionField = AccessTools.Field(typeof(InfinityScroll), "scrollDirection");
    private static readonly FieldInfo ItemListPlainScrollField = AccessTools.Field(typeof(ItemListScroll), "scroll");
    private static readonly FieldInfo ItemListGroupedScrollField = AccessTools.Field(typeof(ItemListScroll), "groupedScroll");
    private static readonly FieldInfo ItemListGroupedCardScrollField = AccessTools.Field(typeof(ItemListScroll), "groupedCardScroll");
    private static readonly MethodInfo RefreshStyleMetricsMethod = AccessTools.Method(typeof(InfinityScroll), "RefreshStyleMetrics");

    private static readonly ConditionalWeakTable<InfinityScroll, OriginalInfinityLayout> OriginalLayouts = new();
    private static ConditionalWeakTable<InfinityScroll, TargetInfo> TargetInfos = new();
    private static ConditionalWeakTable<ItemListScroll, TargetInfo> ItemListTargetInfos = new();

    private static Type _cardScrollType;
    private static Type _combatSkillSelectType;
    private static readonly Type[] PageViewTypes = new Type[PageViewNames.Length];
    private static bool _typeLookupDone;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InfinityScroll), "RefreshStyleMetrics")]
    private static void RefreshStyleMetricsPostfix(InfinityScroll __instance)
    {
        ApplyOrRestore(__instance, rerender: false);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InfinityScroll), "SetDataCount")]
    private static void SetDataCountPostfix(InfinityScroll __instance)
    {
        // SetDataCount already rebuilds the visible cells. ReRender here used to
        // start a second complete render pass every time a list was populated.
        ApplyOrRestore(__instance, rerender: false);
    }

    private static void ApplyOrRestore(InfinityScroll instance, bool rerender)
    {
        if (instance == null)
            return;

        if (!IsCurrentCardModeScroll(instance))
        {
            RestoreLayout(instance, rerender);
            return;
        }

        var targetLineCount = ResolveTargetLineCount(instance);
        var scale = ResolveScale(instance, targetLineCount);
        if (!Plugin.EnableContainerCompact || scale <= 0f || scale >= 1f)
        {
            RestoreLayout(instance, rerender);
            return;
        }

        var changed = ApplyLayout(instance, scale, targetLineCount);
        if (rerender && changed)
            instance.ReRender();
    }

    private static bool IsCurrentCardModeScroll(InfinityScroll instance)
    {
        var itemListScroll = instance == null ? null : instance.GetComponentInParent<ItemListScroll>(true);
        if (itemListScroll == null)
            return true;

        try
        {
            return itemListScroll.IsCardMode && ReferenceEquals(GetActiveInfinityScroll(itemListScroll), instance);
        }
        catch
        {
            return false;
        }
    }

    private static bool ApplyLayout(InfinityScroll instance, float scale, int targetLineCount)
    {
        var changed = false;
        var original = GetOrCacheOriginal(instance);
        if (instance.srcPrefab != null)
        {
            var targetScale = new Vector3(original.PrefabScale.x * scale, original.PrefabScale.y * scale, original.PrefabScale.z);
            if (!Approximately(instance.srcPrefab.transform.localScale, targetScale))
            {
                instance.srcPrefab.transform.localScale = targetScale;
                changed = true;
            }
        }

        if (CellSizeField != null)
        {
            var targetCellSize = original.CellSize * scale;
            var currentCellSize = (Vector2)CellSizeField.GetValue(instance);
            if (!Approximately(currentCellSize, targetCellSize))
            {
                CellSizeField.SetValue(instance, targetCellSize);
                changed = true;
            }
        }

        var targetGap = original.Gap * scale;
        if (!Approximately(instance.gap, targetGap))
        {
            instance.gap = targetGap;
            changed = true;
        }

        var targetPadding = original.Padding * scale;
        if (!Approximately(instance.padding, targetPadding))
        {
            instance.padding = targetPadding;
            changed = true;
        }

        var targetLines = Math.Max(1, targetLineCount);
        if (instance.lineCount != targetLines)
        {
            instance.lineCount = targetLines;
            changed = true;
        }

        changed |= ResizeCrossAxis(instance);
        changed |= ResizeMainAxis(instance, scale, addTrailingPadding: true);
        return changed;
    }

    private static bool RestoreLayout(InfinityScroll instance, bool rerender)
    {
        if (!OriginalLayouts.TryGetValue(instance, out var original))
            return false;

        var changed = false;

        if (instance.srcPrefab != null && !Approximately(instance.srcPrefab.transform.localScale, original.PrefabScale))
        {
            instance.srcPrefab.transform.localScale = original.PrefabScale;
            changed = true;
        }

        if (CellSizeField != null)
        {
            var currentCellSize = (Vector2)CellSizeField.GetValue(instance);
            if (!Approximately(currentCellSize, original.CellSize))
            {
                CellSizeField.SetValue(instance, original.CellSize);
                changed = true;
            }
        }

        if (!Approximately(instance.gap, original.Gap))
        {
            instance.gap = original.Gap;
            changed = true;
        }
        if (!Approximately(instance.padding, original.Padding))
        {
            instance.padding = original.Padding;
            changed = true;
        }
        var targetLines = Math.Max(1, original.LineCount);
        if (instance.lineCount != targetLines)
        {
            instance.lineCount = targetLines;
            changed = true;
        }
        changed |= ResizeCrossAxis(instance);
        changed |= ResizeMainAxis(instance, 1f, addTrailingPadding: false);
        if (rerender && changed)
            instance.ReRender();
        return changed;
    }

    private static bool ResizeMainAxis(InfinityScroll instance, float scale, bool addTrailingPadding)
    {
        if (ContainerField == null || DirectionField == null || CellSizeField == null)
            return false;

        var container = ContainerField.GetValue(instance) as RectTransform;
        if (container == null || instance.CurrentDataCount <= 0)
            return false;

        var direction = (int)DirectionField.GetValue(instance);
        var verticalMainAxis = direction < 2;
        if (!verticalMainAxis)
            return false;

        var cellSize = (Vector2)CellSizeField.GetValue(instance);
        var lineCount = Math.Max(1, instance.lineCount);
        var rowCount = Mathf.Max(1, Mathf.CeilToInt((float)instance.CurrentDataCount / lineCount));
        var cellMainSize = cellSize.y;
        var gapMainSize = instance.gap.y;
        var paddingMainSize = instance.padding.y;
        var mainSize = paddingMainSize * 2f + (cellMainSize + gapMainSize) * (rowCount - 1) + cellMainSize;
        if (addTrailingPadding)
            mainSize += ResolveTrailingPadding(instance, scale);

        if (Mathf.Abs(container.rect.height - mainSize) <= 0.1f)
            return false;

        container.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, mainSize);
        instance.Scroll?.UpdateScrollBarValue();
        return true;
    }

    private static float ResolveTrailingPadding(InfinityScroll instance, float scale)
    {
        if (scale >= 1f || !OriginalLayouts.TryGetValue(instance, out var original))
            return 0f;

        var missingCellHeight = original.CellSize.y * (1f - scale);
        return Mathf.Clamp(missingCellHeight * 0.35f, 12f, 36f);
    }

    private static bool ResizeCrossAxis(InfinityScroll instance)
    {
        if (ContainerField == null || DirectionField == null)
            return false;

        var container = ContainerField.GetValue(instance) as RectTransform;
        if (container == null)
            return false;

        var cellSize = CellSizeField != null ? (Vector2)CellSizeField.GetValue(instance) : Vector2.zero;
        var lineCount = Math.Max(1, instance.lineCount);
        var direction = (int)DirectionField.GetValue(instance);
        var verticalMainAxis = direction < 2;
        var cellCrossSize = verticalMainAxis ? cellSize.x : cellSize.y;
        var gapCrossSize = verticalMainAxis ? instance.gap.x : instance.gap.y;
        var paddingCrossSize = verticalMainAxis ? instance.padding.x : instance.padding.y;
        var crossSize = paddingCrossSize * 2f + (gapCrossSize + cellCrossSize) * (lineCount - 1) + cellCrossSize;

        var currentSize = verticalMainAxis ? container.rect.width : container.rect.height;
        if (Mathf.Abs(currentSize - crossSize) <= 0.1f)
            return false;

        container.SetSizeWithCurrentAnchors(verticalMainAxis ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical, crossSize);
        return true;
    }

    private static bool Approximately(Vector2 left, Vector2 right)
    {
        return Mathf.Abs(left.x - right.x) <= 0.001f && Mathf.Abs(left.y - right.y) <= 0.001f;
    }

    private static bool Approximately(Vector3 left, Vector3 right)
    {
        return Mathf.Abs(left.x - right.x) <= 0.001f
               && Mathf.Abs(left.y - right.y) <= 0.001f
               && Mathf.Abs(left.z - right.z) <= 0.001f;
    }

    private static float GetCrossSize(Vector2 size, InfinityScroll instance)
    {
        var direction = DirectionField != null ? (int)DirectionField.GetValue(instance) : 0;
        return direction < 2 ? size.x : size.y;
    }

    private static OriginalInfinityLayout GetOrCacheOriginal(InfinityScroll instance)
    {
        return OriginalLayouts.GetValue(instance, scroll =>
        {
            var cellSize = CellSizeField != null ? (Vector2)CellSizeField.GetValue(scroll) : Vector2.zero;
            if (cellSize == Vector2.zero && scroll.srcPrefab != null)
            {
                var prefabRect = scroll.srcPrefab.GetComponent<RectTransform>();
                if (prefabRect != null)
                    cellSize = prefabRect.rect.size;
            }

            return new OriginalInfinityLayout
            {
                CellSize = cellSize,
                LineCount = Math.Max(1, scroll.lineCount),
                Gap = scroll.gap,
                Padding = scroll.padding,
                PrefabScale = scroll.srcPrefab == null ? Vector3.one : scroll.srcPrefab.transform.localScale,
            };
        });
    }

    internal static float ResolveScale(InfinityScroll instance)
    {
        return ResolveScale(instance, ResolveTargetLineCount(instance));
    }

    private static float ResolveScale(InfinityScroll instance, int targetLineCount)
    {
        if (targetLineCount <= 0)
            return 0f;

        var original = GetOrCacheOriginal(instance);
        if (original.LineCount <= 0)
            return 0f;

        if (targetLineCount <= original.LineCount)
            return 1f;

        var direction = DirectionField != null ? (int)DirectionField.GetValue(instance) : 0;
        var verticalMainAxis = direction < 2;
        var cellCrossSize = verticalMainAxis ? original.CellSize.x : original.CellSize.y;
        var gapCrossSize = verticalMainAxis ? original.Gap.x : original.Gap.y;
        var paddingCrossSize = verticalMainAxis ? original.Padding.x : original.Padding.y;
        var originalCrossSize = ResolveCrossSize(original.LineCount, cellCrossSize, gapCrossSize, paddingCrossSize);
        var targetCrossSize = ResolveCrossSize(targetLineCount, cellCrossSize, gapCrossSize, paddingCrossSize);
        if (originalCrossSize <= 0f || targetCrossSize <= 0f)
            return Mathf.Clamp((float)original.LineCount / targetLineCount, 0.3f, 1f);

        return Mathf.Clamp(originalCrossSize / targetCrossSize, 0.3f, 1f);
    }

    private static float ResolveCrossSize(int lineCount, float cellCrossSize, float gapCrossSize, float paddingCrossSize)
    {
        lineCount = Math.Max(1, lineCount);
        return paddingCrossSize * 2f + (gapCrossSize + cellCrossSize) * (lineCount - 1) + cellCrossSize;
    }

    private static int ResolveTargetLineCount(InfinityScroll instance)
    {
        return ResolveTargetKind(instance) switch
        {
            TargetKind.Inventory => Plugin.InventoryContainerLineCount,
            TargetKind.Exchange => Plugin.ExchangeContainerLineCount,
            TargetKind.Skill => Plugin.SkillContainerLineCount,
            _ => 0,
        };
    }

    private static TargetKind ResolveTargetKind(InfinityScroll instance)
    {
        if (instance == null)
            return TargetKind.None;

        var transform = instance.transform;
        var parentId = GetInstanceId(transform == null ? null : transform.parent);
        var rootId = GetInstanceId(transform == null ? null : transform.root);
        if (TargetInfos.TryGetValue(instance, out var cached)
            && cached.ParentId == parentId
            && cached.RootId == rootId)
            return cached.Kind;

        var kind = DetectTargetKind(instance);
        TargetInfos.Remove(instance);
        TargetInfos.Add(instance, new TargetInfo
        {
            ParentId = parentId,
            RootId = rootId,
            Kind = kind,
        });
        return kind;
    }

    private static TargetKind DetectTargetKind(InfinityScroll instance)
    {
        EnsureTypes();

        var current = instance.transform;
        var depth = 0;
        var hasCardScroll = false;
        var hasItemListScroll = false;
        var hasCombatSkillSelect = false;
        var inWarehouseView = false;
        var pageIndex = -1;
        while (current != null && depth < MaxParentDepth)
        {
            if (!hasCardScroll && _cardScrollType != null && current.GetComponent(_cardScrollType) != null)
                hasCardScroll = true;

            if (!hasItemListScroll && current.GetComponent<ItemListScroll>() != null)
                hasItemListScroll = true;

            if (!hasCombatSkillSelect && _combatSkillSelectType != null && current.GetComponent(_combatSkillSelectType) != null)
                hasCombatSkillSelect = true;

            if (!inWarehouseView && current.GetComponent<ViewWarehouse>() != null)
                inWarehouseView = true;

            if (pageIndex < 0)
            {
                for (var i = 0; i < PageViewTypes.Length; i++)
                {
                    var pageType = PageViewTypes[i];
                    if (pageType != null && current.GetComponent(pageType) != null)
                    {
                        pageIndex = i;
                        break;
                    }
                }
            }

            current = current.parent;
            depth++;
        }

        if (hasCombatSkillSelect)
            return TargetKind.Skill;
        if (inWarehouseView && (hasCardScroll || hasItemListScroll))
            return TargetKind.Exchange;
        if (!hasCardScroll || pageIndex < 0)
            return TargetKind.None;

        return pageIndex switch
        {
            0 => TargetKind.Inventory,
            1 => TargetKind.Exchange,
            _ => TargetKind.None,
        };
    }

    internal static void RegisterItemListScroll(ItemListScroll itemListScroll, bool rerender)
    {
        if (itemListScroll == null)
            return;

        var kind = ResolveTargetKind(itemListScroll);
        if (kind == TargetKind.None)
            return;

        InfinityScroll infinityScroll;
        try
        {
            infinityScroll = GetActiveInfinityScroll(itemListScroll);
        }
        catch
        {
            return;
        }

        if (infinityScroll == null)
            return;

        CacheTargetKind(infinityScroll, kind);
        ApplyOrRestore(infinityScroll, rerender);
    }

    private static InfinityScroll GetActiveInfinityScroll(ItemListScroll itemListScroll)
    {
        if (itemListScroll == null)
            return null;

        try
        {
            Component owner;
            if (itemListScroll.IsCardMode)
                owner = ItemListGroupedCardScrollField?.GetValue(itemListScroll) as Component;
            else if (ItemListGroupedScrollField?.GetValue(itemListScroll) is Component grouped
                && ItemListPlainScrollField?.GetValue(itemListScroll) == null)
                owner = grouped;
            else
                owner = ItemListPlainScrollField?.GetValue(itemListScroll) as Component;

            if (owner == null)
                return null;
            return AccessTools.Property(owner.GetType(), "InfiniteScroll")?.GetValue(owner) as InfinityScroll;
        }
        catch
        {
            return null;
        }
    }

    private static TargetKind ResolveTargetKind(ItemListScroll itemListScroll)
    {
        if (itemListScroll == null)
            return TargetKind.None;

        var transform = itemListScroll.transform;
        var parentId = GetInstanceId(transform == null ? null : transform.parent);
        var rootId = GetInstanceId(transform == null ? null : transform.root);
        if (ItemListTargetInfos.TryGetValue(itemListScroll, out var cached)
            && cached.ParentId == parentId
            && cached.RootId == rootId)
            return cached.Kind;

        var kind = DetectTargetKind(itemListScroll);
        ItemListTargetInfos.Remove(itemListScroll);
        ItemListTargetInfos.Add(itemListScroll, new TargetInfo
        {
            ParentId = parentId,
            RootId = rootId,
            Kind = kind,
        });
        return kind;
    }

    private static TargetKind DetectTargetKind(ItemListScroll itemListScroll)
    {
        EnsureTypes();

        var current = itemListScroll.transform;
        var depth = 0;
        var pageIndex = -1;
        while (current != null && depth < MaxParentDepth)
        {
            if (current.GetComponent<ViewWarehouse>() != null)
                return TargetKind.Exchange;

            if (pageIndex < 0)
            {
                for (var i = 0; i < PageViewTypes.Length; i++)
                {
                    var pageType = PageViewTypes[i];
                    if (pageType != null && current.GetComponent(pageType) != null)
                    {
                        pageIndex = i;
                        break;
                    }
                }
            }

            current = current.parent;
            depth++;
        }

        return pageIndex switch
        {
            0 => TargetKind.Inventory,
            1 => TargetKind.Exchange,
            _ => TargetKind.None,
        };
    }

    private static void CacheTargetKind(InfinityScroll instance, TargetKind kind)
    {
        var transform = instance.transform;
        TargetInfos.Remove(instance);
        TargetInfos.Add(instance, new TargetInfo
        {
            ParentId = GetInstanceId(transform == null ? null : transform.parent),
            RootId = GetInstanceId(transform == null ? null : transform.root),
            Kind = kind,
        });
    }

    private static int GetInstanceId(UnityEngine.Object obj)
    {
        return obj == null ? 0 : obj.GetInstanceID();
    }

    private static void EnsureTypes()
    {
        if (_typeLookupDone)
            return;

        _typeLookupDone = true;
        _cardScrollType = AccessTools.TypeByName("Game.Components.ListStyleGeneralScroll.Item.CardStyleGeneralScroll");
        _combatSkillSelectType = AccessTools.TypeByName("Game.Components.Common.CombatSkillSelect");
        for (var i = 0; i < PageViewNames.Length; i++)
            PageViewTypes[i] = AccessTools.TypeByName(PageViewNames[i]);
    }

    internal static void RefreshAllActive(bool allowRestore)
    {
        EnsureTypes();
        TargetInfos = new ConditionalWeakTable<InfinityScroll, TargetInfo>();
        ItemListTargetInfos = new ConditionalWeakTable<ItemListScroll, TargetInfo>();
        var scrolls = UnityEngine.Object.FindObjectsOfType<InfinityScroll>();
        foreach (var scroll in scrolls)
        {
            if (scroll == null)
                continue;

            try
            {
                if (Plugin.EnableContainerCompact)
                {
                    RefreshStyleMetricsMethod?.Invoke(scroll, null);
                    scroll.ReRender();
                }
                else if (allowRestore)
                {
                    RestoreLayout(scroll, rerender: true);
                }
            }
            catch
            {
            }
        }

        EquipCombatSkillGroupedSkillScrollCompactPatch.RefreshAllActive(allowRestore);
    }

    private sealed class OriginalInfinityLayout
    {
        public Vector2 CellSize;
        public int LineCount;
        public Vector2 Gap;
        public Vector2 Padding;
        public Vector3 PrefabScale;
    }

    private enum TargetKind
    {
        None,
        Inventory,
        Exchange,
        Skill,
    }

    private sealed class TargetInfo
    {
        public int ParentId;
        public int RootId;
        public TargetKind Kind;
    }
}

[HarmonyPatch]
internal static class EquipCombatSkillGroupedSkillScrollCompactPatch
{
    private const float MinEquipSkillItemScale = 0.62f;
    private const string VisualRootName = "BetterTaiwuScrollEquipSkillVisualRoot";

    private static readonly FieldInfo ContentSpaceInitField = AccessTools.Field(typeof(LoopScrollRectBase), "m_ContentSpaceInit");
    private static readonly FieldInfo ContentConstraintCountInitField = AccessTools.Field(typeof(LoopScrollRectBase), "m_ContentConstraintCountInit");
    private static readonly FieldInfo GridLayoutField = AccessTools.Field(typeof(LoopScrollRectBase), "m_GridLayout");

    private static readonly ConditionalWeakTable<LoopVerticalScrollRect, OriginalLoopGridLayout> OriginalLayouts = new();

    private static Type _groupedSkillScrollType;
    private static FieldInfo _scrollViewField;
    private static FieldInfo _itemTemplateField;
    private static bool _typeLookupDone;

    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        EnsureTypes();
        if (_groupedSkillScrollType == null)
            yield break;

        var setMethod = AccessTools.Method(_groupedSkillScrollType, "Set");
        if (setMethod != null)
            yield return setMethod;

        var onEnableMethod = AccessTools.Method(_groupedSkillScrollType, "OnEnable");
        if (onEnableMethod != null)
            yield return onEnableMethod;
    }

    private static void Prefix(object __instance)
    {
        ApplyOrRestore(__instance as Component, allowRestore: true, rerender: false);
    }

    internal static void RefreshAllActive(bool allowRestore)
    {
        EnsureTypes();
        if (_groupedSkillScrollType == null)
            return;

        var instances = UnityEngine.Object.FindObjectsOfType(_groupedSkillScrollType);
        foreach (var instance in instances)
        {
            try
            {
                ApplyOrRestore(instance as Component, allowRestore, rerender: true);
            }
            catch
            {
            }
        }
    }

    private static void ApplyOrRestore(Component groupedSkillScroll, bool allowRestore, bool rerender)
    {
        if (groupedSkillScroll == null)
            return;

        EnsureTypes();
        var scrollView = _scrollViewField?.GetValue(groupedSkillScroll) as LoopVerticalScrollRect;
        if (scrollView == null)
            return;

        var grid = scrollView.content == null ? null : scrollView.content.GetComponent<GridLayoutGroup>();
        if (grid == null)
            return;

        var original = GetOrCacheOriginal(scrollView, groupedSkillScroll, grid);
        var targetLineCount = Mathf.Clamp(Plugin.EquipCombatSkillContainerLineCount, 3, 7);
        if (!Plugin.EnableContainerCompact)
        {
            if (allowRestore)
                RestoreLayout(scrollView, groupedSkillScroll, original, rerender);
            return;
        }

        var layout = CalculateLayout(scrollView, original, targetLineCount);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = targetLineCount;
        grid.cellSize = layout.CellSize;
        grid.spacing = layout.Spacing;
        grid.padding = layout.Padding;

        SetTemplateScale(groupedSkillScroll, original.TemplateScale, 1f);
        ApplyContentChildrenVisualScale(scrollView.content, layout.VisualScale, original.CellSize);
        ResetLoopScrollRectCaches(scrollView);
        RefreshLoopScroll(scrollView, rerender);
        ApplyContentChildrenVisualScale(scrollView.content, layout.VisualScale, original.CellSize);
    }

    private static EquipSkillGridLayout CalculateLayout(LoopVerticalScrollRect scrollView, OriginalLoopGridLayout original, int targetLineCount)
    {
        var padding = ClonePadding(original.Padding);
        var spacing = original.Spacing;
        var availableWidth = ResolveAvailableWidth(scrollView, original);
        var cellWidth = original.CellSize.x;
        if (availableWidth > 0f && targetLineCount > 0)
        {
            var usableWidth = availableWidth - padding.left - padding.right - spacing.x * Math.Max(targetLineCount - 1, 0);
            if (usableWidth > 0f)
                cellWidth = usableWidth / targetLineCount;
        }

        var scale = Mathf.Clamp(cellWidth / Mathf.Max(original.CellSize.x, 1f), MinEquipSkillItemScale, 1f);
        return new EquipSkillGridLayout
        {
            CellSize = new Vector2(cellWidth, original.CellSize.y * scale),
            Spacing = new Vector2(spacing.x, original.Spacing.y * scale),
            Padding = padding,
            VisualScale = scale,
        };
    }

    private static float ResolveAvailableWidth(LoopVerticalScrollRect scrollView, OriginalLoopGridLayout original)
    {
        var viewport = scrollView == null ? null : scrollView.viewport;
        var viewportWidth = viewport == null ? 0f : viewport.rect.width;
        if (viewportWidth > 0f)
            return viewportWidth;

        var rect = scrollView == null ? null : scrollView.transform as RectTransform;
        var rectWidth = rect == null ? 0f : rect.rect.width;
        if (rectWidth > 0f)
            return rectWidth;

        return original.Padding.left
               + original.Padding.right
               + original.CellSize.x * original.ConstraintCount
               + original.Spacing.x * Math.Max(original.ConstraintCount - 1, 0);
    }

    private static OriginalLoopGridLayout GetOrCacheOriginal(LoopVerticalScrollRect scrollView, Component groupedSkillScroll, GridLayoutGroup grid)
    {
        return OriginalLayouts.GetValue(scrollView, _ =>
        {
            var template = _itemTemplateField?.GetValue(groupedSkillScroll) as Component;
            return new OriginalLoopGridLayout
            {
                ConstraintCount = Math.Max(1, grid.constraintCount),
                CellSize = grid.cellSize,
                Spacing = grid.spacing,
                Padding = ClonePadding(grid.padding),
                TemplateScale = template == null ? Vector3.one : template.transform.localScale,
            };
        });
    }

    private static void RestoreLayout(LoopVerticalScrollRect scrollView, Component groupedSkillScroll, OriginalLoopGridLayout original, bool rerender)
    {
        var grid = scrollView.content == null ? null : scrollView.content.GetComponent<GridLayoutGroup>();
        if (grid == null)
            return;

        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = original.ConstraintCount;
        grid.cellSize = original.CellSize;
        grid.spacing = original.Spacing;
        grid.padding = ClonePadding(original.Padding);
        SetTemplateScale(groupedSkillScroll, original.TemplateScale, 1f);
        ApplyContentChildrenVisualScale(scrollView.content, 1f, original.CellSize);
        ResetLoopScrollRectCaches(scrollView);
        RefreshLoopScroll(scrollView, rerender);
        ApplyContentChildrenVisualScale(scrollView.content, 1f, original.CellSize);
    }

    private static void SetTemplateScale(Component groupedSkillScroll, Vector3 originalScale, float scale)
    {
        var template = _itemTemplateField?.GetValue(groupedSkillScroll) as Component;
        if (template != null)
            template.transform.localScale = new Vector3(originalScale.x * scale, originalScale.y * scale, originalScale.z);
    }

    private static void SetContentChildrenScale(Transform content, Vector3 originalScale, float scale)
    {
        if (content == null)
            return;

        var targetScale = new Vector3(originalScale.x * scale, originalScale.y * scale, originalScale.z);
        for (var i = 0; i < content.childCount; i++)
        {
            var child = content.GetChild(i);
            if (child != null)
                child.localScale = targetScale;
        }
    }

    private static void ApplyContentChildrenVisualScale(Transform content, float scale, Vector2 originalCellSize)
    {
        if (content == null)
            return;

        for (var i = 0; i < content.childCount; i++)
        {
            var child = content.GetChild(i);
            if (child != null)
                ApplyCellVisualScale(child, scale, originalCellSize);
        }
    }

    internal static void ApplyItemVisualScale(EquipCombatSkillItem item)
    {
        if (item == null)
            return;

        var scrollView = item.GetComponentInParent<LoopVerticalScrollRect>(true);
        var content = scrollView == null ? null : scrollView.content;
        if (content == null || !OriginalLayouts.TryGetValue(scrollView, out var original))
            return;

        var cellRoot = ResolveContentChildRoot(item.transform, content);
        if (cellRoot == null)
            return;

        var scale = 1f;
        if (Plugin.EnableContainerCompact)
        {
            var grid = content.GetComponent<GridLayoutGroup>();
            if (grid != null && original.CellSize.x > 0f)
                scale = Mathf.Clamp(grid.cellSize.x / original.CellSize.x, MinEquipSkillItemScale, 1f);
        }

        ApplyCellVisualScale(cellRoot, scale, original.CellSize);
    }

    private static Transform ResolveContentChildRoot(Transform itemTransform, Transform content)
    {
        if (itemTransform == null || content == null)
            return null;

        var current = itemTransform;
        while (current != null && current.parent != content)
            current = current.parent;

        return current;
    }

    private static void ApplyCellVisualScale(Transform cellRoot, float scale, Vector2 originalCellSize)
    {
        if (cellRoot == null)
            return;

        var clampedScale = Mathf.Clamp(scale, MinEquipSkillItemScale, 1f);
        var visualRoot = EnsureVisualRoot(cellRoot, originalCellSize);
        if (visualRoot == null)
            return;

        visualRoot.localScale = new Vector3(clampedScale, clampedScale, 1f);
    }

    private static RectTransform EnsureVisualRoot(Transform cellRoot, Vector2 originalCellSize)
    {
        if (cellRoot == null)
            return null;

        var existing = cellRoot.Find(VisualRootName) as RectTransform;
        if (existing != null)
        {
            ConfigureVisualRootRect(existing, originalCellSize);
            return existing;
        }

        var wrapperObj = new GameObject(VisualRootName, typeof(RectTransform));
        var wrapper = wrapperObj.transform as RectTransform;
        wrapper.SetParent(cellRoot, false);
        ConfigureVisualRootRect(wrapper, originalCellSize);
        wrapper.SetAsFirstSibling();

        var children = new List<Transform>();
        for (var i = 0; i < cellRoot.childCount; i++)
        {
            var child = cellRoot.GetChild(i);
            if (child != null && child != wrapper)
                children.Add(child);
        }

        foreach (var child in children)
            child.SetParent(wrapper, false);

        return wrapper;
    }

    private static void ConfigureVisualRootRect(RectTransform wrapper, Vector2 originalCellSize)
    {
        if (wrapper == null)
            return;

        wrapper.anchorMin = new Vector2(0.5f, 0.5f);
        wrapper.anchorMax = new Vector2(0.5f, 0.5f);
        wrapper.pivot = new Vector2(0.5f, 0.5f);
        wrapper.anchoredPosition = Vector2.zero;
        wrapper.sizeDelta = originalCellSize;
    }

    private static void ResetLoopScrollRectCaches(LoopVerticalScrollRect scrollView)
    {
        ContentSpaceInitField?.SetValue(scrollView, false);
        ContentConstraintCountInitField?.SetValue(scrollView, false);
        GridLayoutField?.SetValue(scrollView, null);
    }

    private static void RefreshLoopScroll(LoopVerticalScrollRect scrollView, bool rerender)
    {
        if (scrollView == null || !rerender || scrollView.prefabSource == null || !scrollView.isActiveAndEnabled)
            return;

        scrollView.RefillCells();
        if (scrollView.content != null)
            LayoutRebuilder.MarkLayoutForRebuild(scrollView.content);
    }

    private static RectOffset ClonePadding(RectOffset padding)
    {
        return padding == null ? new RectOffset() : new RectOffset(padding.left, padding.right, padding.top, padding.bottom);
    }

    private static void EnsureTypes()
    {
        if (_typeLookupDone)
            return;

        _typeLookupDone = true;
        _groupedSkillScrollType = AccessTools.TypeByName("Game.Views.CharacterMenu.EquipCombatSkillGroupedSkillScroll");
        if (_groupedSkillScrollType == null)
            return;

        _scrollViewField = AccessTools.Field(_groupedSkillScrollType, "scrollView");
        _itemTemplateField = AccessTools.Field(_groupedSkillScrollType, "itemTemplate");
    }

    private sealed class OriginalLoopGridLayout
    {
        public int ConstraintCount;
        public Vector2 CellSize;
        public Vector2 Spacing;
        public RectOffset Padding;
        public Vector3 TemplateScale;
    }

    private sealed class EquipSkillGridLayout
    {
        public Vector2 CellSize;
        public Vector2 Spacing;
        public RectOffset Padding;
        public float VisualScale;
    }

}

[HarmonyPatch(typeof(EquipCombatSkillItem), "Set", new[]
{
    typeof(ViewCharacterMenuEquipCombatSkill),
    typeof(int),
    typeof(CombatSkillDisplayDataCharacterMenuListItem),
    typeof(EEquipCombatSkillItemType),
    typeof(bool),
})]
internal static class EquipCombatSkillItemCompactVisualScalePatch
{
    private static void Postfix(EquipCombatSkillItem __instance)
    {
        EquipCombatSkillGroupedSkillScrollCompactPatch.ApplyItemVisualScale(__instance);
    }
}

[HarmonyPatch]
internal static class ContainerDefaultCardModePatch
{
    private static readonly FieldInfo IsCardModeField = AccessTools.Field(typeof(ItemListScroll), "_isCardMode");
    private static readonly FieldInfo UseGroupedScrollField = AccessTools.Field(typeof(ItemListScroll), "useGroupedScroll");
    private static readonly FieldInfo ScrollField = AccessTools.Field(typeof(ItemListScroll), "scroll");
    private static readonly FieldInfo GroupedScrollField = AccessTools.Field(typeof(ItemListScroll), "groupedScroll");
    private static readonly FieldInfo CardScrollField = AccessTools.Field(typeof(ItemListScroll), "groupedCardScroll");
    private static readonly FieldInfo BtnSwitchCardModeField = AccessTools.Field(typeof(ItemListScroll), "btnSwitchCardMode");
    private static readonly FieldInfo DefaultSwitchIndexField = AccessTools.Field(typeof(ItemListScroll), "defaultSwithIndex");
    private static readonly MethodInfo RefreshCardModeMethod = AccessTools.Method(typeof(ItemListScroll), "RefreshCardMode");
    private static readonly MethodInfo SwitchCardModeToggleMethod = AccessTools.Method(typeof(ItemListScroll), "SwitchCardModeToggle", new[] { typeof(int), typeof(int) });
    private static readonly FieldInfo ToggleActiveIndexField = AccessTools.Field(AccessTools.TypeByName("FrameWork.UISystem.UIElements.CToggleGroup"), "_activeIndex");

    private static Type _viewCharacterMenuItemsType;
    private static Type _viewExchangeBaseType;
    private static bool _typeLookupDone;
    private static int _initializing;

    internal static string GetPageKey(Component owner)
    {
        if (owner == null)
            return null;

        var current = owner.transform;
        for (var depth = 0; current != null && depth < 12; depth++, current = current.parent)
        {
            if (current.GetComponent<ViewCharacterMenuItems>() != null)
                return "CharacterMenuItems";
            var exchange = current.GetComponent<ViewExchangeBase>();
            if (exchange != null)
                return "Exchange_" + exchange.GetType().Name;
        }
        return null;
    }

    private static string GetMemoryKey(ItemListScroll instance)
    {
        var page = GetPageKey(instance);
        if (page == null || page == "CharacterMenuItems")
            return page;

        var current = instance.transform;
        for (var depth = 0; current != null && depth < 12; depth++, current = current.parent)
        {
            var container = current.GetComponent<ExchangeContainer>();
            if (container == null)
                continue;
            if (instance == container.selfItemList || instance == container.selfExchangeList)
                return page + ":self";
            if (instance == container.targetItemList || instance == container.targetExchangeList)
                return page + ":target";
        }
        return null;
    }

    internal static int GetDesiredSwitchIndex(string key)
    {
        var saved = MemoryOptimizationSettingsStore.GetContainerCardMode(key);
        // Read the contributor's preference only as a migration fallback. New
        // changes use the Mod's existing atomic asynchronous JSON writer.
        if (!saved.HasValue && !string.IsNullOrEmpty(key))
        {
            var separator = key.IndexOf(':');
            var legacyKey = "BTS_CardMode_" + (separator < 0 ? key : key.Substring(0, separator));
            if (PlayerPrefs.HasKey(legacyKey))
            {
                var value = PlayerPrefs.GetInt(legacyKey, -1);
                if (value == 0 || value == 1)
                {
                    saved = value == 1;
                    MemoryOptimizationSettingsStore.SetContainerCardMode(key, saved.Value);
                }
            }
        }
        return saved.HasValue && !saved.Value ? 0 : 1;
    }

    internal static void SaveCardMode(ItemListScroll instance)
    {
        if (_initializing != 0 || !ShouldUseDefaultCardMode(instance))
            return;
        MemoryOptimizationSettingsStore.SetContainerCardMode(GetMemoryKey(instance), instance.IsCardMode);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ItemListScroll), "Init")]
    private static void InitPrefix(ItemListScroll __instance, out bool __state)
    {
        __state = ShouldUseDefaultCardMode(__instance);
        if (!__state)
            return;

        _initializing++;
        var targetIndex = GetDesiredSwitchIndex(GetMemoryKey(__instance));
        DefaultSwitchIndexField?.SetValue(__instance, targetIndex);
        NormalizeToggleGroup(BtnSwitchCardModeField?.GetValue(__instance), targetIndex);
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(ItemListScroll), "Init")]
    private static void InitFinalizer(bool __state)
    {
        if (__state)
            _initializing--;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemListScroll), "Init")]
    private static void InitPostfix(ItemListScroll __instance)
    {
        if (!ShouldUseDefaultCardMode(__instance))
            return;

        EnsureDefaultCardMode(__instance);
    }

    internal static bool ShouldUseDefaultCardMode(ItemListScroll instance)
    {
        if (instance == null || !Plugin.EnableContainerCompact || !Plugin.EnableDefaultContainerCardMode || !IsSupportedPage(instance))
            return false;

        return CardScrollField?.GetValue(instance) is Component card && card != null;
    }

    private static void EnsureDefaultCardMode(ItemListScroll instance)
    {
        _initializing++;
        try
        {
            var targetIndex = GetDesiredSwitchIndex(GetMemoryKey(instance));
            var targetIsCardMode = targetIndex == 1;
            var isCardMode = instance.IsCardMode;
            if (isCardMode != targetIsCardMode)
            {
                if (SwitchCardModeToggleMethod != null)
                {
                    SwitchCardModeToggleMethod.Invoke(instance, new object[] { targetIndex, isCardMode ? 1 : 0 });
                }
                else
                {
                    IsCardModeField?.SetValue(instance, targetIsCardMode);
                    RefreshCardModeMethod?.Invoke(instance, null);
                }
            }

            SyncModeVisibility(instance);
            SyncToggleState(instance);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to apply default card mode: " + ex);
        }
        finally
        {
            _initializing--;
        }
    }

    private static bool IsSupportedPage(ItemListScroll instance)
    {
        if (instance == null)
            return false;

        EnsureTypes();
        var current = instance.transform;
        var depth = 0;
        while (current != null && depth < 12)
        {
            if (_viewCharacterMenuItemsType != null && current.GetComponent(_viewCharacterMenuItemsType) != null)
                return true;
            if (_viewExchangeBaseType != null && current.GetComponent(_viewExchangeBaseType) != null)
                return true;

            current = current.parent;
            depth++;
        }

        return false;
    }

    private static void EnsureTypes()
    {
        if (_typeLookupDone)
            return;

        _typeLookupDone = true;
        _viewCharacterMenuItemsType = AccessTools.TypeByName("Game.Views.CharacterMenu.ViewCharacterMenuItems");
        _viewExchangeBaseType = AccessTools.TypeByName("Game.Views.Exchange.ViewExchangeBase");
    }

    internal static void SyncModeVisibility(ItemListScroll instance)
    {
        if (instance == null)
            return;

        var isCardMode = IsCardModeField?.GetValue(instance) is bool value && value;
        var useGroupedScroll = UseGroupedScrollField?.GetValue(instance) is bool grouped && grouped;
        SetActive(CardScrollField?.GetValue(instance) as Component, isCardMode);
        SetActive(ScrollField?.GetValue(instance) as Component, !isCardMode && !useGroupedScroll);
        SetActive(GroupedScrollField?.GetValue(instance) as Component, !isCardMode && useGroupedScroll);
    }

    internal static void SyncToggleState(ItemListScroll instance)
    {
        if (instance == null)
            return;

        var switchToggle = BtnSwitchCardModeField?.GetValue(instance);
        if (switchToggle == null)
            return;

        var isCardMode = IsCardModeField?.GetValue(instance) is bool value && value;
        var targetIndex = isCardMode ? 1 : 0;
        NormalizeToggleGroup(switchToggle, targetIndex);
    }

    internal static bool IsFeatureEnabled()
    {
        return Plugin.EnableContainerCompact && Plugin.EnableDefaultContainerCardMode;
    }

    internal static void SetToggleGroupActiveWithoutNotify(CToggleGroup toggleGroup, int targetIndex)
    {
        if (toggleGroup != null)
            NormalizeToggleGroup(toggleGroup, targetIndex);
    }

    private static void NormalizeToggleGroup(object switchToggle, int targetIndex)
    {
        if (switchToggle == null)
            return;

        if (switchToggle is CToggleGroup toggleGroup)
        {
            var toggles = toggleGroup.GetAll();
            if (toggles != null)
            {
                var effectiveIndex = targetIndex >= 0 && targetIndex < toggles.Count ? targetIndex : 0;
                for (var i = 0; i < toggles.Count; i++)
                {
                    var toggle = toggles[i];
                    if (toggle != null)
                        toggle.SetIsOnWithoutNotify(i == effectiveIndex);
                }

                ToggleActiveIndexField?.SetValue(switchToggle, effectiveIndex);
                return;
            }
        }

        ToggleActiveIndexField?.SetValue(switchToggle, targetIndex);
    }

    private static void SetActive(Component component, bool active)
    {
        if (component != null && component.gameObject.activeSelf != active)
            component.gameObject.SetActive(active);
    }
}

[HarmonyPatch(typeof(ItemListScroll), "RefreshCardMode")]
internal static class ItemListScrollRefreshCardModeCompactVisibilityPatch
{
    private static void Postfix(ItemListScroll __instance)
    {
        if (ContainerDefaultCardModePatch.ShouldUseDefaultCardMode(__instance))
        {
            ContainerDefaultCardModePatch.SyncModeVisibility(__instance);
            ContainerDefaultCardModePatch.SyncToggleState(__instance);
        }

        ContainerCompactPatches.RegisterItemListScroll(__instance, rerender: false);
    }
}

[HarmonyPatch(typeof(ItemListScroll), "Init")]
internal static class ItemListScrollInitCompactLayoutPatch
{
    private static void Postfix(ItemListScroll __instance)
    {
        ContainerCompactPatches.RegisterItemListScroll(__instance, rerender: false);
    }
}

[HarmonyPatch(typeof(ItemListScroll), "SwitchCardModeToggle")]
internal static class ItemListScrollSwitchCardModeToggleCompactLayoutPatch
{
    private static void Postfix(ItemListScroll __instance)
    {
        ContainerCompactPatches.RegisterItemListScroll(__instance, rerender: false);
        ContainerDefaultCardModePatch.SaveCardMode(__instance);
    }
}

[HarmonyPatch(typeof(ItemListScroll), "HighLightItemView")]
internal static class ItemListScrollHighlightCompactScalePatch
{
    private static void Prefix(RowItem itemView, out Vector3 __state)
    {
        __state = itemView == null ? Vector3.one : itemView.transform.localScale;
    }

    private static void Postfix(RowItem itemView, Vector3 __state)
    {
        if (!Plugin.EnableContainerCompact || itemView == null)
            return;

        itemView.transform.localScale = __state;
    }
}

[HarmonyPatch(typeof(ViewCharacterMenuItems), "Awake")]
internal static class ViewCharacterMenuItemsDefaultCardModeTogglePatch
{
    private static readonly FieldInfo TargetToggleGroupField = AccessTools.Field(typeof(ViewCharacterMenuItems), "targetToggleGroup");

    private static void Prefix(ViewCharacterMenuItems __instance)
    {
        if (!ContainerDefaultCardModePatch.IsFeatureEnabled())
            return;

        ContainerDefaultCardModePatch.SetToggleGroupActiveWithoutNotify(TargetToggleGroupField?.GetValue(__instance) as CToggleGroup,
            ContainerDefaultCardModePatch.GetDesiredSwitchIndex("CharacterMenuItems"));
    }

    private static void Postfix(ViewCharacterMenuItems __instance)
    {
        if (!ContainerDefaultCardModePatch.IsFeatureEnabled())
            return;

        ContainerDefaultCardModePatch.SetToggleGroupActiveWithoutNotify(TargetToggleGroupField?.GetValue(__instance) as CToggleGroup,
            ContainerDefaultCardModePatch.GetDesiredSwitchIndex("CharacterMenuItems"));
    }
}

[HarmonyPatch(typeof(ExchangeContainer), "AddSwitchToggleListener")]
internal static class ExchangeContainerDefaultCardModeTogglePatch
{
    private static void Prefix(ExchangeContainer __instance)
    {
        Apply(__instance);
    }

    private static void Postfix(ExchangeContainer __instance)
    {
        Apply(__instance);
    }

    private static void Apply(ExchangeContainer container)
    {
        if (container == null || !ContainerDefaultCardModePatch.IsFeatureEnabled())
            return;

        var page = ContainerDefaultCardModePatch.GetPageKey(container);
        if (page == null)
            return;
        ContainerDefaultCardModePatch.SetToggleGroupActiveWithoutNotify(container.targetToggleGroup,
            ContainerDefaultCardModePatch.GetDesiredSwitchIndex(page + ":target"));
        ContainerDefaultCardModePatch.SetToggleGroupActiveWithoutNotify(container.selfToggleGroup,
            ContainerDefaultCardModePatch.GetDesiredSwitchIndex(page + ":self"));
    }
}
