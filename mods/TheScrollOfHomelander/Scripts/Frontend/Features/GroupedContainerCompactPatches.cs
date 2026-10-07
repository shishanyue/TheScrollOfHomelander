#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game.Components.Common;
using Game.Components.ListStyleGeneralScroll.Item;
using Game.Components.SortAndFilter.CombatSkill;
using Game.Views.CharacterMenu;
using GameData.Domains.CombatSkill;
using FrameWork;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTaiwuScroll.Frontend;

// 1.1.27 pools title/content rows through LoopVerticalScrollRect. Scaling an old
// InfinityScroll no longer affects these lists. Keep native grouping and row rendering;
// only change the number of cards and the content root's visual size.
internal static class GroupedContainerCompactSupport
{
    private static readonly OwnerApi CardApi = new(typeof(CardStyleGroupedScroll), typeof(CardGroupedRowItem), "_cardCountPerRow", "contentCardItem");
    private static readonly OwnerApi SkillApi = new(typeof(CombatSkillSelect), typeof(CombatSkillGroupedRowItem), "_itemsPerLine", "contentItemTemplate");
    private static readonly OwnerApi EquipApi = new(typeof(EquipCombatSkillGroupedSkillScroll), typeof(EquipCombatSkillGroupRowItem), "_itemsPerLine", "contentItemTemplate");
    private static readonly FieldInfo CardScrollField = AccessTools.Field(typeof(ItemListScroll), "groupedCardScroll");
    private static readonly ConditionalWeakTable<Component, LayoutState> States = new();
    private static readonly ConditionalWeakTable<EquipCombatSkillGroupedSkillScroll, EquipSectionsState> EquipSections = new();
    private static readonly FieldInfo FocusMaskField = AccessTools.Field(typeof(ItemListScroll), "focusItemMask");
    private static bool _restoring;
    private static int _refreshGeneration;

    internal static float CalculateScale(float availableWidth, float cellWidth, float spacing, int horizontalPadding, int columns)
    {
        if (!IsFinite(availableWidth) || !IsFinite(cellWidth) || !IsFinite(spacing)
            || availableWidth <= 0f || cellWidth <= 0f || spacing < 0f || horizontalPadding < 0 || columns <= 0)
            return 1f;
        var required = cellWidth * columns + spacing * Math.Max(0, columns - 1) + horizontalPadding;
        return IsFinite(required) && required > 0f ? Mathf.Min(1f, availableWidth / required) : 1f;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static void RegisterItemList(ItemListScroll list, bool rerender)
    {
        var owner = list == null ? null : CardScrollField?.GetValue(list) as CardStyleGroupedScroll;
        if (owner != null && ApplyOwner(owner) && rerender)
            Rebuild(owner);
    }

    internal static void RefreshAllActive()
    {
        foreach (var api in new[] { CardApi, SkillApi, EquipApi })
        {
            foreach (var obj in UnityEngine.Object.FindObjectsOfType(api.OwnerType))
            {
                var owner = obj as Component;
                if (owner != null && ApplyOwner(owner))
                    Rebuild(owner);
            }
        }
    }

    internal static void RestoreAll()
    {
        _restoring = true;
        _refreshGeneration++;
        try
        {
            foreach (var api in new[] { CardApi, SkillApi, EquipApi })
            {
                foreach (var obj in Resources.FindObjectsOfTypeAll(api.OwnerType))
                {
                    var owner = obj as Component;
                    if (owner == null || !States.TryGetValue(owner, out var state))
                        continue;
                    try
                    {
                        if (state.Touched)
                        {
                            var changed = ApplyOwner(owner);
                            foreach (var row in owner.GetComponentsInChildren(api.RowType, true))
                                ApplyRow(row, state);
                            if (changed && owner is MonoBehaviour behaviour && behaviour.isActiveAndEnabled)
                                Rebuild(owner);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[BetterTaiwuScroll] Failed to restore grouped container layout: " + ex);
                    }
                    finally
                    {
                        state.PendingRefresh = false;
                        if (state.Observer != null)
                        {
                            state.Observer.Owner = null;
                            UnityEngine.Object.Destroy(state.Observer);
                            state.Observer = null;
                        }
                    }
                }
            }
        }
        finally
        {
            _restoring = false;
        }
    }

    internal static bool TryGetHighlightScale(ItemListScroll list, RowItem item, out Vector3 scale)
    {
        scale = item == null ? Vector3.one : item.transform.localScale;
        var owner = item == null ? null : item.GetComponentInParent<CardStyleGroupedScroll>(true);
        if (owner == null || !States.TryGetValue(owner, out var state) || !state.Compact)
            return false;
        var mask = list == null ? null : FocusMaskField?.GetValue(list) as Transform;
        if (mask == null)
            return false;
        scale = CalculateHighlightScale(item.transform.lossyScale, mask.lossyScale, scale);
        return true;
    }

    internal static Vector3 CalculateHighlightScale(Vector3 worldScale, Vector3 parentScale, Vector3 fallback)
    {
        if (Mathf.Abs(parentScale.x) < 0.0001f || Mathf.Abs(parentScale.y) < 0.0001f || Mathf.Abs(parentScale.z) < 0.0001f)
            return fallback;
        return new Vector3(worldScale.x / parentScale.x, worldScale.y / parentScale.y, worldScale.z / parentScale.z);
    }

    internal static bool ApplyOwner(Component owner)
    {
        var api = ResolveApi(owner);
        var template = api?.RowTemplate.GetValue(owner) as Component;
        var scroll = api?.ScrollView.GetValue(owner) as LoopVerticalScrollRect;
        if (template == null || scroll == null)
            return false;

        var columns = ResolveColumns(owner, api);
        var eligible = !_restoring && Plugin.EnableContainerCompact && columns > 0;
        if (api == CardApi)
        {
            var list = owner.GetComponentInParent<ItemListScroll>(true);
            eligible &= list != null && list.IsCardMode;
        }
        if (!eligible && (!States.TryGetValue(owner, out var previous) || !previous.Touched))
            return false;

        if (States.TryGetValue(owner, out var cached) && cached.Template != template)
            States.Remove(owner);
        if (!States.TryGetValue(owner, out var state))
        {
            var root = api.ContentRoot.GetValue(template) as GameObject;
            var item = api.ContentItem.GetValue(template) as Component;
            var rect = root == null ? null : root.transform as RectTransform;
            var itemRect = item == null ? null : item.transform as RectTransform;
            if (rect == null || itemRect == null)
                return false;

            var layout = root.GetComponent<HorizontalOrVerticalLayoutGroup>();
            var grid = root.GetComponent<GridLayoutGroup>();
            var cellWidth = grid != null ? grid.cellSize.x : itemRect.rect.width;
            if (cellWidth <= 0f && item.TryGetComponent<LayoutElement>(out var itemLayout))
                cellWidth = itemLayout.preferredWidth;
            var height = (float)api.ContentHeight.GetValue(template);
            if (cellWidth <= 0f || height <= 0f)
                return false;

            state = new LayoutState
            {
                Api = api,
                Template = template,
                Root = new RootLayout(rect),
                CellWidth = cellWidth,
                Spacing = grid != null ? grid.spacing.x : layout?.spacing ?? 0f,
                Padding = grid != null ? grid.padding.horizontal : layout?.padding.horizontal ?? 0,
                ContentHeight = height,
                OriginalColumns = api.RowColumns == null ? 0 : (int)api.RowColumns.GetValue(template),
            };
            States.Add(owner, state);
        }

        if (!_restoring && eligible && state.Observer == null)
        {
            state.Observer = owner.gameObject.AddComponent<GroupedContainerDimensionObserver>();
            state.Observer.Owner = owner;
        }

        var width = ResolveWidth(owner, scroll);
        var compact = eligible && width > 0f;
        var scale = compact ? CalculateScale(width, state.CellWidth, state.Spacing, state.Padding, columns) : 1f;
        // A setting equal to/below the native density keeps native visuals and count.
        compact &= scale < 0.9999f;
        if (!compact && !state.Touched)
            return false;
        if (!compact)
        {
            scale = 1f;
            columns = state.OriginalColumns;
            if (api == CardApi)
                columns = state.Template is CardGroupedRowItem card ? card.GetCardCountPerRow(width) : 1;
        }

        var changed = state.Compact != compact || state.Columns != columns || Mathf.Abs(state.Scale - scale) > 0.0001f || Mathf.Abs(state.Width - width) > 0.1f;
        state.Compact = compact;
        state.Touched |= compact;
        state.Columns = Math.Max(1, columns);
        state.Scale = scale;
        state.Width = width;
        ApplyRow(state.Template, state);
        api.OwnerColumns.SetValue(owner, state.Columns);
        api.OwnerContentHeight.SetValue(owner, state.ContentHeight * scale);
        return changed;
    }

    internal static void PrepareInitialization(Component owner)
    {
        // Reopening a populated pooled list must rebuild its flattened rows before
        // changing their capacity. Its old rows remain internally consistent until
        // the coalesced refresh, rather than dropping items during OnEnable.
        var api = ResolveApi(owner);
        if (api != null && api.FlatRows.GetValue(owner) is ICollection rows && rows.Count > 0)
        {
            if (States.TryGetValue(owner, out _))
                RequestRebuild(owner);
            else if (ApplyOwner(owner))
                // A populated view can first become eligible while it is hidden.
                // Establish its baseline and rebuild now, before native OnEnable
                // renders any row with the new template capacity.
                Rebuild(owner);
        }
        else
            ApplyOwner(owner);
    }

    internal static void RequestRebuild(Component owner)
    {
        if (_restoring || SingletonObject.IsDestroying || owner == null
            || owner is not MonoBehaviour activeOwner || !activeOwner.isActiveAndEnabled || !owner.gameObject.activeInHierarchy
            || !States.TryGetValue(owner, out var state)
            || state.PendingRefresh || state.Api.FlatRows.GetValue(owner) is not ICollection rows || rows.Count == 0)
            return;
        if (!state.Touched && !Plugin.EnableContainerCompact)
            return;
        state.PendingRefresh = true;
        var generation = _refreshGeneration;
        try
        {
            SingletonObject.getInstance<YieldHelper>().DelayFrameDo(1u, () =>
            {
                state.PendingRefresh = false;
                if (generation != _refreshGeneration || _restoring || owner == null
                    || owner is not MonoBehaviour behaviour || !behaviour.isActiveAndEnabled || !owner.gameObject.activeInHierarchy)
                    return;
                try
                {
                    ApplyOwner(owner);
                    if (state.Touched)
                        Rebuild(owner);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BetterTaiwuScroll] Failed to refresh grouped container layout: " + ex);
                }
            });
        }
        catch (Exception ex)
        {
            state.PendingRefresh = false;
            Debug.LogWarning("[BetterTaiwuScroll] Failed to schedule grouped container refresh: " + ex);
        }
    }

    private static float ResolveWidth(Component owner, LoopVerticalScrollRect scroll)
    {
        var rect = owner.transform as RectTransform;
        var width = rect == null ? 0f : rect.rect.width;
        if (scroll.viewport != null && scroll.viewport.rect.width > 0f)
            width = width > 0f ? Mathf.Min(width, scroll.viewport.rect.width) : scroll.viewport.rect.width;
        return width;
    }

    private static int ResolveColumns(Component owner, OwnerApi api)
    {
        if (api == SkillApi)
            return Plugin.SkillContainerLineCount;
        if (api == EquipApi)
            return Plugin.EquipCombatSkillContainerLineCount;
        return ContainerCompactPatches.ResolveItemListLineCount(owner.GetComponentInParent<ItemListScroll>(true));
    }

    internal static void ApplyRenderedRow(Component row)
    {
        var api = row == null ? null : ResolveRowApi(row);
        var owner = api == null ? null : row.GetComponentInParent(api.OwnerType, true) as Component;
        if (owner != null)
        {
            if (States.TryGetValue(owner, out var state) && state.Touched)
                ApplyRow(row, state);
        }
    }

    private static void ApplyRow(Component row, LayoutState state)
    {
        var api = state.Api;
        var root = api.ContentRoot.GetValue(row) as GameObject;
        var rect = root == null ? null : root.transform as RectTransform;
        if (rect == null)
            return;

        api.ContentHeight.SetValue(row, state.ContentHeight * state.Scale);
        if (api.RowColumns != null && (int)api.RowColumns.GetValue(row) != state.Columns)
        {
            api.RowColumns.SetValue(row, state.Columns);
            // Skill rows initialize their children once. Invalidate only when the
            // count changes so pooled rows can safely grow/shrink on setting changes.
            api.ContentInitialized?.SetValue(row, false);
        }

        if (!state.Compact)
        {
            state.Root.Restore(rect);
            return;
        }

        // Keep each card's native rect, callbacks and hierarchy. The content row
        // is wider before scaling, so its original layout group fits the new count.
        // Titles are siblings of this root and retain their native height and font.
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(state.Width / state.Scale, state.ContentHeight);
        rect.localScale = new Vector3(state.Root.Scale.x * state.Scale, state.Root.Scale.y * state.Scale, state.Root.Scale.z);
        var grid = root.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = state.Columns;
        }
    }

    internal static void RememberEquipSections(EquipCombatSkillGroupedSkillScroll owner, ViewCharacterMenuEquipCombatSkill view, List<SkillSection<CombatSkillDisplayDataCharacterMenuListItem>> sections)
    {
        ApplyOwner(owner);
        if (owner != null)
        {
            var state = EquipSections.GetOrCreateValue(owner);
            state.View = view;
            state.Sections = sections;
        }
    }

    private static void Rebuild(Component owner)
    {
        if (owner is CardStyleGroupedScroll)
        {
            CardApi.Rebuild.Invoke(owner, null);
            CardApi.QueueRefresh.Invoke(owner, new object[] { true });
        }
        else if (owner is CombatSkillSelect)
        {
            SkillApi.Rebuild.Invoke(owner, new object[] { false });
        }
        else if (owner is EquipCombatSkillGroupedSkillScroll equip && EquipSections.TryGetValue(equip, out var state) && state.Sections != null && state.View != null)
        {
            equip.SetSections(state.View, state.Sections, true);
        }
    }

    private static OwnerApi ResolveApi(Component owner) => owner is CardStyleGroupedScroll ? CardApi : owner is CombatSkillSelect ? SkillApi : owner is EquipCombatSkillGroupedSkillScroll ? EquipApi : null;
    private static OwnerApi ResolveRowApi(Component row) => row is CardGroupedRowItem ? CardApi : row is CombatSkillGroupedRowItem ? SkillApi : row is EquipCombatSkillGroupRowItem ? EquipApi : null;

    private sealed class OwnerApi
    {
        internal readonly Type OwnerType, RowType;
        internal readonly FieldInfo RowTemplate, ScrollView, OwnerColumns, OwnerContentHeight, FlatRows, ContentRoot, ContentItem, ContentHeight, RowColumns, ContentInitialized;
        internal readonly MethodInfo Rebuild, QueueRefresh;

        internal OwnerApi(Type owner, Type row, string columns, string item)
        {
            OwnerType = owner;
            RowType = row;
            RowTemplate = AccessTools.Field(owner, "rowTemplate");
            ScrollView = AccessTools.Field(owner, "scrollView");
            OwnerColumns = AccessTools.Field(owner, columns);
            OwnerContentHeight = AccessTools.Field(owner, "_contentHeight");
            FlatRows = AccessTools.Field(owner, "_flatRows");
            ContentRoot = AccessTools.Field(row, "contentRoot");
            ContentItem = AccessTools.Field(row, item);
            ContentHeight = AccessTools.Field(row, "contentPreferredHeight");
            RowColumns = AccessTools.Field(row, "itemCountPerLine");
            ContentInitialized = AccessTools.Field(row, "_contentInitialized");
            if (owner == typeof(CardStyleGroupedScroll))
            {
                Rebuild = AccessTools.Method(owner, "RebuildFlatRows", Type.EmptyTypes);
                QueueRefresh = AccessTools.Method(owner, "QueueRefreshScrollView", new[] { typeof(bool) });
            }
            else if (owner == typeof(CombatSkillSelect))
            {
                Rebuild = AccessTools.Method(owner, "RebuildAndRefresh", new[] { typeof(bool) });
            }
        }
    }

    private sealed class LayoutState
    {
        internal OwnerApi Api;
        internal Component Template;
        internal RootLayout Root;
        internal float CellWidth, Spacing, ContentHeight, Width, Scale = 1f;
        internal int Padding, OriginalColumns, Columns;
        internal bool Compact, Touched;
        internal bool PendingRefresh;
        internal GroupedContainerDimensionObserver Observer;
    }

    private sealed class EquipSectionsState
    {
        internal ViewCharacterMenuEquipCombatSkill View;
        internal List<SkillSection<CombatSkillDisplayDataCharacterMenuListItem>> Sections;
    }

    private sealed class RootLayout
    {
        internal readonly Vector3 Scale;
        private readonly Vector2 _anchorMin, _anchorMax, _pivot, _position, _size;
        private readonly GridLayoutGroup.Constraint _gridConstraint;
        private readonly int _gridColumns;
        internal RootLayout(RectTransform rect)
        {
            Scale = rect.localScale;
            _anchorMin = rect.anchorMin;
            _anchorMax = rect.anchorMax;
            _pivot = rect.pivot;
            _position = rect.anchoredPosition;
            _size = rect.sizeDelta;
            var grid = rect.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                _gridConstraint = grid.constraint;
                _gridColumns = grid.constraintCount;
            }
        }
        internal void Restore(RectTransform rect)
        {
            rect.anchorMin = _anchorMin;
            rect.anchorMax = _anchorMax;
            rect.pivot = _pivot;
            rect.anchoredPosition = _position;
            rect.sizeDelta = _size;
            rect.localScale = Scale;
            var grid = rect.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                grid.constraint = _gridConstraint;
                grid.constraintCount = _gridColumns;
            }
        }
    }
}

[HarmonyPatch]
internal static class GroupedContainerCompactOwnerPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(CardStyleGroupedScroll), "TryInitScrollView", Type.EmptyTypes);
        yield return AccessTools.Method(typeof(CardStyleGroupedScroll), "RefreshCardCountPerRow", Type.EmptyTypes);
        yield return AccessTools.Method(typeof(CombatSkillSelect), "TryInitScrollView", Type.EmptyTypes);
        yield return AccessTools.Method(typeof(CombatSkillSelect), "ApplySearchAndRebuild", new[] { typeof(bool) });
        yield return AccessTools.Method(typeof(EquipCombatSkillGroupedSkillScroll), "TryInitScrollView", Type.EmptyTypes);
        yield return AccessTools.Method(typeof(EquipCombatSkillGroupedSkillScroll), "BuildFlatRows", new[] { typeof(List<SkillSection<CombatSkillDisplayDataCharacterMenuListItem>>) });
    }

    private static void Prefix(Component __instance, MethodBase __originalMethod)
    {
        if (__originalMethod.Name == "TryInitScrollView")
            GroupedContainerCompactSupport.PrepareInitialization(__instance);
        else
            GroupedContainerCompactSupport.ApplyOwner(__instance);
    }
    private static void Postfix(Component __instance, MethodBase __originalMethod)
    {
        if (__originalMethod.Name != "TryInitScrollView")
            GroupedContainerCompactSupport.ApplyOwner(__instance);
    }
}

[HarmonyPatch]
internal static class GroupedContainerCompactRowPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(CardGroupedRowItem), "Set");
        yield return AccessTools.Method(typeof(CombatSkillGroupedRowItem), "Set");
        yield return AccessTools.Method(typeof(EquipCombatSkillGroupRowItem), "Set");
    }

    private static void Prefix(Component __instance) => GroupedContainerCompactSupport.ApplyRenderedRow(__instance);
}

[HarmonyPatch(typeof(EquipCombatSkillGroupedSkillScroll), nameof(EquipCombatSkillGroupedSkillScroll.SetSections))]
internal static class GroupedContainerCompactEquipSectionsPatch
{
    private static void Prefix(EquipCombatSkillGroupedSkillScroll __instance, ViewCharacterMenuEquipCombatSkill parentView, List<SkillSection<CombatSkillDisplayDataCharacterMenuListItem>> sections)
        => GroupedContainerCompactSupport.RememberEquipSections(__instance, parentView, sections);
}

internal sealed class GroupedContainerDimensionObserver : MonoBehaviour
{
    internal Component Owner;
    private void OnEnable() => GroupedContainerCompactSupport.RequestRebuild(Owner);
    private void OnRectTransformDimensionsChange() => GroupedContainerCompactSupport.RequestRebuild(Owner);
}
