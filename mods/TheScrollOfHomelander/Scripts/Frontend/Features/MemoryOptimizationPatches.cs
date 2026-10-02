#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Config;
using HarmonyLib;
using UnityEngine;
using GameData.Domains.Building;
using GameData.Domains.Item.Display;
using Game.Views.Building.BuildingManage;
using Game.Views.Building.BuildingManage.Production;
using Game.Views.Make;
using GameData.Domains.Taiwu.ExchangeSystem;
using ItemListScroll = Game.Components.ListStyleGeneralScroll.Item.ItemListScroll;
using ExchangeContainerView = Game.Views.Exchange.ExchangeContainer;
using ExchangeViewBase = Game.Views.Exchange.ViewExchangeBase;
using FilterConfig = Game.Components.SortAndFilter.SortAndFilterConfig;
using FilterDetailedLineState = Game.Components.SortAndFilter.DetailedFilterLineState;
using FilterDetailedState = Game.Components.SortAndFilter.DetailedFilterState;
using FilterLineState = Game.Components.SortAndFilter.LineState;
using FilterMenuState = Game.Components.SortAndFilter.DetailedFilterMenuState;
using FilterView = Game.Components.SortAndFilter.SortAndFilter;
using SortButtonGroup = Game.Components.SortAndFilter.SortButtonGroup;
using SortItemState = Game.Components.SortAndFilter.SortItemState;
using SortStateData = Game.Components.SortAndFilter.SortStateData;
using FilterToggleKey = Game.Components.SortAndFilter.ToggleKey;
using DebateView = Game.Views.Debate.ViewDebate;
using LifeSkillCombatBeginView = Game.Views.LifeSkillCombat.ViewLifeSkillCombatBegin;
using ToggleGroup = FrameWork.UISystem.UIElements.CToggleGroup;

namespace BetterTaiwuScroll.Frontend;

[HarmonyPatch(typeof(ExchangeViewBase), "Refresh")]
internal static class ViewExchangeBaseRefreshPreserveFilterStatePatch
{
    private sealed class RefreshFilterState
    {
        internal ItemListScroll SelfList;
        internal ItemListScroll TargetList;
        internal bool SelfSaved;
        internal bool TargetSaved;
    }

    private static void Prefix(ExchangeViewBase __instance, out RefreshFilterState __state)
    {
        __state = Capture(__instance);
    }

    private static void Postfix(RefreshFilterState __state)
    {
        Restore(__state);
    }

    private static RefreshFilterState Capture(ExchangeViewBase view)
    {
        var state = new RefreshFilterState();
        try
        {
            var container = Traverse.Create(view).Field("exchangeContainer").GetValue<ExchangeContainerView>();
            state.SelfList = container?.selfItemList;
            state.TargetList = container?.targetItemList;
            state.SelfSaved = TrySave(state.SelfList);
            state.TargetSaved = TrySave(state.TargetList);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to capture exchange filter state: " + ex);
        }

        return state;
    }

    private static bool TrySave(ItemListScroll list)
    {
        if (list?.SortAndFilterController == null)
            return false;

        try
        {
            list.SortAndFilterController.SaveFilterStateFromUI();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save exchange filter state before refresh: " + ex);
            return false;
        }
    }

    private static void Restore(RefreshFilterState state)
    {
        if (state == null)
            return;

        TryRestore(state.SelfList, state.SelfSaved);
        TryRestore(state.TargetList, state.TargetSaved);
    }

    private static void TryRestore(ItemListScroll list, bool saved)
    {
        if (!saved || list?.SortAndFilterController == null)
            return;

        try
        {
            list.SortAndFilterController.RestoreFilterState();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to restore exchange filter state after refresh: " + ex);
        }
        finally
        {
            // The updated game does not include the synthetic first-line category
            // ("All" + an inline category such as Medicine/Poison) in
            // SaveFilterStateFromUI. RestoreFilterState therefore clears that category
            // after SetItemList has already scheduled its normal de-duplicated restore.
            // Schedule again even when the exchange callback throws after clearing it.
            FilterMemoryController.ScheduleRestore(list, force: true);
        }
    }
}

internal static class FilterMemoryController
{
    private static bool _applying;
    private static readonly MethodInfo ItemListScrollRefreshListMethod =
        AccessTools.Method(typeof(ItemListScroll), "RefreshList", new[] { typeof(bool) });
    private static readonly FieldInfo ItemListSortAndFilterField =
        AccessTools.Field(typeof(ItemListScroll), "sortAndFilter");
    private static readonly ConditionalWeakTable<ItemListScroll, ItemListFilterCache> ItemListFilters =
        new ConditionalWeakTable<ItemListScroll, ItemListFilterCache>();

    private sealed class ItemListFilterCache
    {
        internal FilterView View;
    }

    internal static void TryRestore(FilterView sortAndFilter, ItemListScroll ownerList = null)
    {
        if (BatchItemFilterController.IsBatchFilterView(sortAndFilter)
            || !Plugin.EnableFilterMemory
            || sortAndFilter?.Config?.LineConfigs == null)
            return;

        var key = BuildKey(sortAndFilter);
        var signature = BuildSignature(sortAndFilter.Config);
        var entry = MemoryOptimizationSettingsStore.GetFilterMemory(key, signature);
        if (entry?.Lines == null || entry.Lines.Count == 0)
            return;

        var lineStates = BuildLineStates(sortAndFilter.Config, entry);
        if (lineStates == null || lineStates.Count == 0)
            return;

        try
        {
            var currentStates = sortAndFilter.GetStateFromUI().LineStates;
            if (AreLineStatesEquivalent(sortAndFilter, currentStates, lineStates))
                return;
        }
        catch
        {
            // The view may still be completing Setup. Continue with the guarded
            // restore path; lifecycle failures are handled below.
        }

        try
        {
            _applying = true;
            sortAndFilter.ApplyFilterLineStates(lineStates);

            // ItemListScroll owns its controller state and refresh callback.  Calling the
            // config callback as well causes a second, lifecycle-sensitive refresh; since the
            // July update that callback can run while the exchange view is only half rebuilt.
            // Generate the controller state from the restored UI and refresh the exact owner
            // list once instead.
            var resolvedOwnerList = ResolveOwnerList(sortAndFilter, ownerList);
            if (resolvedOwnerList != null)
                ForceOwnerListRefresh(resolvedOwnerList);
            else
                NotifyRestoredFilterChanged(sortAndFilter, lineStates);

        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to restore filter memory: " + ex);
        }
        finally
        {
            _applying = false;
        }
    }

    internal static string GetCurrentSignature(FilterView sortAndFilter)
    {
        if (sortAndFilter?.Config?.LineConfigs == null)
            return string.Empty;

        var key = BuildKey(sortAndFilter);
        var configSignature = BuildSignature(sortAndFilter.Config);
        var entry = MemoryOptimizationSettingsStore.GetFilterMemory(key, configSignature);
        return key + "|" + configSignature + "|" + BuildEntrySignature(entry)
            + "|ui:" + BuildUiStateSignature(sortAndFilter);
    }

    private static string BuildEntrySignature(FilterMemoryEntry entry)
    {
        if (entry?.Lines == null || entry.Lines.Count == 0)
            return "none";

        var builder = new StringBuilder(256);
        foreach (var line in entry.Lines)
        {
            if (line == null)
                continue;

            builder.Append(line.LineId).Append(',')
                .Append(line.Type).Append(',')
                .Append(line.IsActive ? 1 : 0).Append(',')
                .Append(line.ToggleIsAll ? 1 : 0).Append(',')
                .Append(line.ToggleIndex).Append('[');

            if (line.Menus != null)
            {
                foreach (var menu in line.Menus.OrderBy(menu => menu.MenuId))
                {
                    if (menu == null)
                        continue;

                    builder.Append(menu.MenuId).Append(':')
                        .Append(menu.IsActive ? 1 : 0).Append(':');
                    if (menu.SelectedIndices != null)
                    {
                        foreach (var index in menu.SelectedIndices.OrderBy(index => index))
                            builder.Append(index).Append('.');
                    }

                    builder.Append(';');
                }
            }

            builder.Append("]|");
        }

        return builder.ToString();
    }

    internal static void ScheduleRestore(ItemListScroll itemListScroll, bool force = false)
    {
        if (_applying || !Plugin.EnableFilterMemory || itemListScroll == null)
            return;

        try
        {
            var cache = ItemListFilters.GetOrCreateValue(itemListScroll);
            cache.View ??= ItemListSortAndFilterField?.GetValue(itemListScroll) as FilterView;
            var sortAndFilter = cache.View;
            // The restore signature includes the current UI state. A normal
            // de-duplicated schedule therefore still detects genuine game-side
            // resets without reapplying the same filter after every SetItemList.
            FilterMemoryRestoreState.GetOrAdd(sortAndFilter)?.Schedule(1, itemListScroll, force);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to schedule item list filter memory restore: " + ex);
        }
    }

    private static void NotifyRestoredFilterChanged(FilterView sortAndFilter, IReadOnlyList<FilterLineState> lineStates)
    {
        var config = sortAndFilter?.Config;
        if (config?.LineConfigs == null || config.OnFilterChanged == null || lineStates == null)
            return;

        var notified = new HashSet<int>();
        var count = Math.Min(config.LineConfigs.Count, lineStates.Count);
        if (count > 0)
        {
            var lineId = config.LineConfigs[0].Id;
            if (lineId >= 0 && notified.Add(lineId))
                SafeNotifyFilterChanged(config, lineId);
        }

        for (var i = 1; i < count; i++)
        {
            var lineState = lineStates[i];
            if (!HasActiveDetailedSelection(lineState))
                continue;

            var lineId = config.LineConfigs[i].Id;
            if (lineId >= 0 && notified.Add(lineId))
                SafeNotifyFilterChanged(config, lineId);
        }

        if (notified.Count == 0)
            SafeNotifyFilterChanged(config, -1);
    }

    private static void SafeNotifyFilterChanged(FilterConfig config, int lineId)
    {
        if (config?.OnFilterChanged == null)
            return;

        try
        {
            config.OnFilterChanged.Invoke(lineId);
        }
        catch (Exception ex)
        {
            // A restored UI state is still useful even when a non-list owner is between its
            // Setup and OnInit phases.  Never let one game callback abort the remaining restore.
            Debug.LogWarning("[BetterTaiwuScroll] Filter memory callback was not ready for line "
                + lineId + ": " + ex);
        }
    }

    private static bool HasActiveDetailedSelection(FilterLineState lineState)
    {
        var menuStates = lineState.DetailedFilterState?.State.MenuStateDict;
        if (menuStates == null)
            return false;

        foreach (var pair in menuStates)
        {
            if (pair.Value.IsActive && pair.Value.SelectedIndices != null && pair.Value.SelectedIndices.Count > 0)
                return true;
        }

        return false;
    }

    private static ItemListScroll ResolveOwnerList(FilterView sortAndFilter, ItemListScroll ownerList)
    {
        if (ownerList != null || sortAndFilter == null)
            return ownerList;

        var directOwner = sortAndFilter.GetComponentInParent<ItemListScroll>(true);
        if (directOwner != null)
            return directOwner;

        // Some updated prefabs keep SortAndFilter beside (rather than below) ItemListScroll.
        // Walk up the local view hierarchy and match the list's serialized filter reference.
        var current = sortAndFilter.transform.parent;
        while (current != null)
        {
            foreach (var candidate in current.GetComponentsInChildren<ItemListScroll>(true))
            {
                if (candidate == null)
                    continue;

                try
                {
                    var candidateFilter = Traverse.Create(candidate).Field("sortAndFilter").GetValue<FilterView>();
                    if (candidateFilter == sortAndFilter)
                        return candidate;
                }
                catch
                {
                    // Continue looking; a prefab without this serialized field is unrelated.
                }
            }

            current = current.parent;
        }

        return null;
    }

    private static void ForceOwnerListRefresh(ItemListScroll itemListScroll)
    {
        if (itemListScroll == null)
            return;

        try
        {
            // OnSortAndFilterChanged invokes the owning exchange view before refreshing the
            // list. ViewWarehouse obtains its cache asynchronously, so that callback can read
            // a null cache while the warehouse is opening or rebuilding. RefreshList performs
            // the same filter generation for this list without touching the parent view.
            // Reflection does not supply C# optional arguments. The current game
            // declares RefreshList(bool keepSelectedIndex = false).
            ItemListScrollRefreshListMethod?.Invoke(itemListScroll, new object[] { false });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to refresh item list after filter memory restore: " + ex);
        }
    }

    internal static void TrySave(FilterView sortAndFilter)
    {
        if (_applying
            || BatchItemFilterController.IsBatchFilterView(sortAndFilter)
            || !Plugin.EnableFilterMemory
            || sortAndFilter?.Config?.LineConfigs == null)
            return;

        try
        {
            var state = sortAndFilter.GetStateFromUI();
            if (state.LineStates == null || state.LineStates.Count == 0)
                return;

            var entry = new FilterMemoryEntry
            {
                Key = BuildKey(sortAndFilter),
                Signature = BuildSignature(sortAndFilter.Config),
                Lines = BuildLineMemories(sortAndFilter, sortAndFilter.Config, state.LineStates)
            };

            MemoryOptimizationSettingsStore.SetFilterMemory(entry);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save filter memory: " + ex);
        }
    }

    private static string FormatFirstLine(IReadOnlyList<FilterLineState> lineStates)
    {
        if (lineStates == null || lineStates.Count == 0)
            return "none";

        var first = lineStates[0];
        return "isAll=" + first.ToggleGroupState.IsAll
            + ",index=" + first.ToggleGroupState.Index
            + ",active=" + first.IsActive;
    }

    private static bool AreLineStatesEquivalent(
        FilterView view,
        IReadOnlyList<FilterLineState> current,
        IReadOnlyList<FilterLineState> expected)
    {
        if (current == null || expected == null || current.Count != expected.Count)
            return false;

        for (var i = 0; i < current.Count; i++)
        {
            var left = current[i];
            var right = expected[i];
            if (left.IsActive != right.IsActive
                || left.Type != right.Type
                || left.ToggleGroupState.IsAll != right.ToggleGroupState.IsAll
                || left.ToggleGroupState.Index != right.ToggleGroupState.Index)
                return false;

            var leftMenus = left.DetailedFilterState?.State.MenuStateDict;
            var rightMenus = right.DetailedFilterState?.State.MenuStateDict;
            var ignoredMenuId = i == 0
                && left.Type == Game.Components.SortAndFilter.ESortAndFilterOneLineType.ToggleGroup
                ? int.MinValue
                : (int?)null;
            if (!AreMenuStatesEquivalent(leftMenus, rightMenus, ignoredMenuId))
                return false;
        }

        if (current.Count > 0
            && current[0].Type == Game.Components.SortAndFilter.ESortAndFilterOneLineType.ToggleGroup)
        {
            var currentEffective = view != null
                ? view.GetEffectiveFirstToggleGroupState()
                : current[0].ToggleGroupState;
            var expectedEffective = GetExpectedEffectiveFirstToggleGroupState(expected[0]);
            if (currentEffective.IsAll != expectedEffective.IsAll
                || currentEffective.Index != expectedEffective.Index)
                return false;
        }

        return true;
    }

    private static bool AreMenuStatesEquivalent(
        IReadOnlyDictionary<int, FilterMenuState> left,
        IReadOnlyDictionary<int, FilterMenuState> right,
        int? ignoredMenuId = null)
    {
        var leftCount = CountComparableMenus(left, ignoredMenuId);
        var rightCount = CountComparableMenus(right, ignoredMenuId);
        if (leftCount != rightCount)
            return false;
        if (leftCount == 0)
            return true;

        foreach (var pair in left)
        {
            if (ignoredMenuId.HasValue && pair.Key == ignoredMenuId.Value)
                continue;

            if (right == null || !right.TryGetValue(pair.Key, out var other))
                return false;
            if (pair.Value.IsActive != other.IsActive)
                return false;

            var leftIndices = pair.Value.SelectedIndices;
            var rightIndices = other.SelectedIndices;
            var leftIndicesCount = leftIndices?.Count ?? 0;
            var rightIndicesCount = rightIndices?.Count ?? 0;
            if (leftIndicesCount != rightIndicesCount)
                return false;
            if (leftIndicesCount > 0)
            {
                foreach (var index in leftIndices)
                {
                    if (!rightIndices.Contains(index))
                        return false;
                }
            }
        }

        return true;
    }

    private static int CountComparableMenus(
        IReadOnlyDictionary<int, FilterMenuState> menus,
        int? ignoredMenuId)
    {
        if (menus == null || menus.Count == 0)
            return 0;
        if (!ignoredMenuId.HasValue || !menus.ContainsKey(ignoredMenuId.Value))
            return menus.Count;
        return menus.Count - 1;
    }

    private static FilterToggleKey GetExpectedEffectiveFirstToggleGroupState(FilterLineState state)
    {
        if (!state.ToggleGroupState.IsAll)
            return state.ToggleGroupState;

        var menus = state.DetailedFilterState?.State.MenuStateDict;
        if (menus != null
            && menus.TryGetValue(int.MinValue, out var syntheticMenu)
            && syntheticMenu.SelectedIndices != null
            && syntheticMenu.SelectedIndices.Count > 0)
        {
            var selectedIndex = syntheticMenu.SelectedIndices.First();
            if (selectedIndex < 0)
                return new FilterToggleKey { IsAll = true, Index = -1 };

            return new FilterToggleKey
            {
                IsAll = false,
                Index = selectedIndex
            };
        }

        return new FilterToggleKey
        {
            IsAll = true,
            Index = -1
        };
    }

    private static string BuildUiStateSignature(FilterView view)
    {
        try
        {
            var states = view?.GetStateFromUI().LineStates;
            if (states == null || states.Count == 0)
                return "none";

            var builder = new StringBuilder(128);
            for (var i = 0; i < states.Count; i++)
            {
                var state = states[i];
                builder.Append(state.IsActive ? '1' : '0').Append(':')
                    .Append((int)state.Type).Append(':')
                    .Append(state.ToggleGroupState.IsAll ? '1' : '0').Append(':')
                    .Append(state.ToggleGroupState.Index).Append('[');

                var menus = state.DetailedFilterState?.State.MenuStateDict;
                if (menus != null)
                {
                    foreach (var pair in menus.OrderBy(item => item.Key))
                    {
                        builder.Append(pair.Key).Append(':')
                            .Append(pair.Value.IsActive ? '1' : '0').Append(':');
                        if (pair.Value.SelectedIndices != null)
                        {
                            foreach (var index in pair.Value.SelectedIndices.OrderBy(value => value))
                                builder.Append(index).Append('.');
                        }
                        builder.Append(';');
                    }
                }

                builder.Append("]|");
            }

            var effectiveToggle = view.GetEffectiveFirstToggleGroupState();
            builder.Append("effective:")
                .Append(effectiveToggle.IsAll ? '1' : '0').Append(':')
                .Append(effectiveToggle.Index);
            return builder.ToString();
        }
        catch
        {
            return "pending";
        }
    }

    internal static List<FilterLineMemory> BuildLineMemories(
        FilterView sortAndFilter,
        FilterConfig config,
        IReadOnlyList<FilterLineState> lineStates)
    {
        var result = new List<FilterLineMemory>();
        var count = Math.Min(config.LineConfigs.Count, lineStates.Count);
        for (var i = 0; i < count; i++)
        {
            var lineConfig = config.LineConfigs[i];
            var lineState = lineStates[i];
            var memory = new FilterLineMemory
            {
                LineId = lineConfig.Id,
                Type = (int)lineConfig.Type,
                IsActive = lineState.IsActive,
                ToggleIsAll = lineState.ToggleGroupState.IsAll,
                ToggleIndex = lineState.ToggleGroupState.Index,
                Menus = new List<FilterMenuMemory>()
            };

            var menuStates = lineState.DetailedFilterState?.State.MenuStateDict;
            if (menuStates != null)
            {
                foreach (var pair in menuStates.OrderBy(pair => pair.Key))
                {
                    memory.Menus.Add(new FilterMenuMemory
                    {
                        MenuId = pair.Key,
                        IsActive = pair.Value.IsActive,
                        SelectedIndices = pair.Value.SelectedIndices?.ToList() ?? new List<int>()
                    });
                }
            }

            // In the updated UI, selecting the top-level "All" icon exposes the same item
            // categories as a second inline row.  That row is not part of LineState at all:
            // SortAndFilter stores it privately as (lineId, int.MinValue) in _selectedIndices,
            // and GetStateFromUI therefore reports only IsAll=true.  Preserve the effective
            // inline category as the game's own synthetic menu entry so ApplyFilterLineStates
            // can put it back without changing the top-level "All" icon.
            if (i == 0
                && lineState.Type == Game.Components.SortAndFilter.ESortAndFilterOneLineType.ToggleGroup
                && lineState.ToggleGroupState.IsAll
                && sortAndFilter != null)
            {
                var effectiveToggle = sortAndFilter.GetEffectiveFirstToggleGroupState();
                if (!effectiveToggle.IsAll && effectiveToggle.Index >= 0)
                {
                    memory.Menus.RemoveAll(menu => menu.MenuId == int.MinValue);
                    memory.Menus.Add(new FilterMenuMemory
                    {
                        MenuId = int.MinValue,
                        IsActive = true,
                        SelectedIndices = new List<int> { effectiveToggle.Index }
                    });
                }
            }

            result.Add(memory);
        }

        return result;
    }

    internal static List<FilterLineState> BuildLineStates(FilterConfig config, FilterMemoryEntry entry)
    {
        if (config.LineConfigs.Count != entry.Lines.Count)
            return null;

        var result = new List<FilterLineState>();
        for (var i = 0; i < config.LineConfigs.Count; i++)
        {
            var lineConfig = config.LineConfigs[i];
            var memory = entry.Lines[i];
            if (memory.LineId != lineConfig.Id || memory.Type != (int)lineConfig.Type)
                return null;

            var menuStates = new Dictionary<int, FilterMenuState>();
            foreach (var menu in memory.Menus)
                menuStates[menu.MenuId] = new FilterMenuState(menu.SelectedIndices ?? new List<int>(), menu.IsActive);

            result.Add(new FilterLineState
            {
                IsActive = memory.IsActive,
                Type = lineConfig.Type,
                ToggleGroupState = new FilterToggleKey
                {
                    IsAll = memory.ToggleIsAll,
                    Index = memory.ToggleIsAll ? -1 : memory.ToggleIndex
                },
                DetailedFilterState = new FilterDetailedLineState
                {
                    State = new FilterDetailedState
                    {
                        MenuStateDict = menuStates
                    }
                }
            });
        }

        return result;
    }

    private static string BuildKey(FilterView sortAndFilter)
    {
        var viewName = FindOwnerViewName(sortAndFilter.transform);
        var path = BuildShortPath(sortAndFilter.transform);
        return viewName + "|" + path;
    }

    internal static string FindOwnerViewName(Transform transform)
    {
        var current = transform;
        while (current != null)
        {
            foreach (var component in current.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                    continue;

                var type = component.GetType();
                if (type == typeof(FilterView) || type.Namespace == "Game.Components.SortAndFilter")
                    continue;

                if (type.Namespace != null && type.Namespace.StartsWith("Game.Views", StringComparison.Ordinal))
                    return type.FullName;

                if (type.Name.StartsWith("UI_", StringComparison.Ordinal))
                    return type.FullName;
            }

            current = current.parent;
        }

        return "UnknownView";
    }

    internal static string BuildShortPath(Transform transform)
    {
        var names = new Stack<string>();
        var current = transform;
        while (current != null && names.Count < 5)
        {
            names.Push(current.name.Replace("(Clone)", string.Empty));
            current = current.parent;
        }

        return string.Join("/", names.ToArray());
    }

    private static string BuildSignature(FilterConfig config)
    {
        var parts = new List<string>();
        foreach (var line in config.LineConfigs)
        {
            var menuIds = line.DetailedFilterLineConfig?.Config.MenuConfigs == null
                ? string.Empty
                : string.Join(",", line.DetailedFilterLineConfig.Config.MenuConfigs.Select(menu => menu.Id.ToString()).ToArray());
            parts.Add($"{line.Id}:{(int)line.Type}:{menuIds}");
        }

        return string.Join("|", parts.ToArray());
    }
}

[HarmonyPatch(typeof(SortButtonGroup), "RefreshAll")]
internal static class SortButtonGroupRefreshAllMemoryPatch
{
    private static void Postfix(SortButtonGroup __instance)
    {
        SortMemoryController.ScheduleRestore(__instance);
    }
}

[HarmonyPatch]
internal static class SortButtonGroupClickMemoryPatch
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(SortButtonGroup), "OnClickItem");
    }

    private static void Postfix(SortButtonGroup __instance)
    {
        SortMemoryController.TrySave(__instance);
    }
}

[HarmonyPatch(typeof(SortButtonGroup), "SetSortData")]
internal static class SortButtonGroupSetSortDataMemoryPatch
{
    private static void Postfix(SortButtonGroup __instance)
    {
        SortMemoryController.ScheduleRestore(__instance);
    }
}

internal static class SortMemoryController
{
    internal static bool SuppressSave;

    internal static void ScheduleRestore(SortButtonGroup sortButtonGroup)
    {
        if (!Plugin.EnableSortMemory || sortButtonGroup == null)
            return;

        SortMemoryRestoreState.GetOrAdd(sortButtonGroup)?.Schedule();
    }

    internal static bool TryRestore(SortButtonGroup sortButtonGroup)
    {
        if (!Plugin.EnableSortMemory || sortButtonGroup == null)
            return true;

        if (!TryGetVisibleSortIds(sortButtonGroup, out var validSortIds))
            return false;

        var key = BuildKey(sortButtonGroup.transform);
        var signature = BuildSignature(sortButtonGroup);
        var entry = MemoryOptimizationSettingsStore.GetSortMemory(key, signature);
        if (entry?.Items == null || entry.Items.Count == 0)
        {
            ClearInheritedSortState(sortButtonGroup);
            return true;
        }

        var itemStates = new List<SortItemState>();
        foreach (var item in entry.Items)
        {
            if (item == null || item.SortId < 0 || (validSortIds.Count > 0 && !validSortIds.Contains(item.SortId)))
                continue;

            itemStates.Add(new SortItemState
            {
                SortId = item.SortId,
                SortDirection = (Game.Components.SortAndFilter.ESortDirection)item.Direction
            });
        }

        if (itemStates.Count == 0)
        {
            ClearInheritedSortState(sortButtonGroup);
            return true;
        }

        var currentData = sortButtonGroup.GetSortData();
        if (IsSameSortState(currentData?.ItemStates, itemStates))
            return true;

        try
        {
            SuppressSave = true;
            sortButtonGroup.SetSortData(new SortStateData { ItemStates = itemStates });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to restore sort memory: " + ex);
        }
        finally
        {
            SuppressSave = false;
        }

        return true;
    }

    private static void ClearInheritedSortState(SortButtonGroup sortButtonGroup)
    {
        var currentData = sortButtonGroup.GetSortData();
        if (currentData?.ItemStates == null || currentData.ItemStates.Count == 0)
            return;

        try
        {
            SuppressSave = true;
            sortButtonGroup.SetSortData(new SortStateData { ItemStates = new List<SortItemState>() });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to clear inherited sort memory: " + ex);
        }
        finally
        {
            SuppressSave = false;
        }
    }

    internal static void TrySave(SortButtonGroup sortButtonGroup)
    {
        if (SuppressSave || !Plugin.EnableSortMemory || sortButtonGroup == null)
            return;

        try
        {
            var data = sortButtonGroup.GetSortData();
            if (!TryGetVisibleSortIds(sortButtonGroup, out var visibleSortIds))
                return;

            var entry = new SortMemoryEntry
            {
                Key = BuildKey(sortButtonGroup.transform),
                Signature = BuildSignature(sortButtonGroup),
                Items = (data?.ItemStates ?? new List<SortItemState>())
                    .Where(item => item.SortId >= 0
                        && visibleSortIds.Contains(item.SortId)
                        && item.SortDirection != Game.Components.SortAndFilter.ESortDirection.None)
                    .Select(item => new SortItemMemory
                    {
                        SortId = item.SortId,
                        Direction = (int)item.SortDirection
                    })
                    .ToList()
            };

            MemoryOptimizationSettingsStore.SetSortMemory(entry);
            SortMemoryRestoreState.GetOrAdd(sortButtonGroup)?.MarkUserSaved();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save sort memory: " + ex);
        }
    }

    private static string BuildKey(Transform transform)
    {
        var viewName = FilterMemoryController.FindOwnerViewName(transform);
        var path = FilterMemoryController.BuildShortPath(transform);
        return viewName + "|" + path;
    }

    private static string BuildSignature(SortButtonGroup sortButtonGroup)
    {
        var allSortIds = GetSortIds(sortButtonGroup);
        var visiblePart = TryGetVisibleSortIds(sortButtonGroup, out var visibleSortIds)
            ? JoinSortIds(visibleSortIds)
            : "pending";
        return "all:" + JoinSortIds(allSortIds)
            + "|visible:" + visiblePart
            + "|filter:" + BuildFilterStateSignature(sortButtonGroup);
    }

    internal static string GetCurrentSignature(SortButtonGroup sortButtonGroup)
    {
        return BuildSignature(sortButtonGroup);
    }

    private static string JoinSortIds(IEnumerable<short> sortIds)
    {
        return string.Join(",", sortIds.OrderBy(id => id).Select(id => id.ToString()).ToArray());
    }

    private static HashSet<short> GetSortIds(SortButtonGroup sortButtonGroup)
    {
        var result = new HashSet<short>();
        var sortIds = Traverse.Create(sortButtonGroup).Field("_sortIds").GetValue<List<short>>();
        if (sortIds != null)
        {
            foreach (var id in sortIds)
            {
                if (id >= 0)
                    result.Add(id);
            }
        }

        var displaying = sortButtonGroup.DisplayingSortIds;
        if (displaying != null)
        {
            foreach (var id in displaying)
            {
                if (id >= 0)
                    result.Add(id);
            }
        }

        return result;
    }

    private static bool TryGetVisibleSortIds(SortButtonGroup sortButtonGroup, out HashSet<short> result)
    {
        result = new HashSet<short>();
        var displaying = sortButtonGroup.DisplayingSortIds;
        if (displaying == null || displaying.Count == 0)
            return false;

        foreach (var id in displaying)
        {
            if (id >= 0)
                result.Add(id);
        }

        return result.Count > 0;
    }

    private static string BuildFilterStateSignature(SortButtonGroup sortButtonGroup)
    {
        try
        {
            var owner = sortButtonGroup.GetComponentInParent<FilterView>(true);
            var states = owner?.GetStateFromUI().LineStates;
            if (states == null || states.Count == 0)
                return string.Empty;

            var parts = new List<string>();
            for (var i = 0; i < states.Count; i++)
            {
                var state = states[i];
                var toggle = state.ToggleGroupState;
                var menuText = string.Empty;
                var menuStates = state.DetailedFilterState?.State.MenuStateDict;
                if (menuStates != null && menuStates.Count > 0)
                {
                    menuText = string.Join(",", menuStates
                        .OrderBy(pair => pair.Key)
                        .Select(pair =>
                        {
                            var indices = pair.Value.SelectedIndices == null
                                ? string.Empty
                                : string.Join(".", pair.Value.SelectedIndices);
                            return pair.Key + ":" + pair.Value.IsActive + ":" + indices;
                        })
                        .ToArray());
                }

                parts.Add(i + ":" + state.IsActive + ":" + (int)state.Type + ":"
                    + toggle.IsAll + ":" + toggle.Index + ":" + menuText);
            }

            return string.Join("|", parts.ToArray());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsSameSortState(IReadOnlyList<SortItemState> current, IReadOnlyList<SortItemState> expected)
    {
        if (current == null)
            return expected == null || expected.Count == 0;

        if (expected == null || current.Count != expected.Count)
            return false;

        for (var i = 0; i < current.Count; i++)
        {
            if (current[i].SortId != expected[i].SortId || current[i].SortDirection != expected[i].SortDirection)
                return false;
        }

        return true;
    }
}

internal sealed class SortMemoryRestoreState : MonoBehaviour
{
    private const int RefreshFrames = 1;

    private SortButtonGroup _owner;
    private int _remainingFrames;
    private int _skipUntilFrame;
    private string _completedSignature;

    internal static SortMemoryRestoreState GetOrAdd(SortButtonGroup owner)
    {
        if (owner == null)
            return null;

        var state = owner.GetComponent<SortMemoryRestoreState>();
        if (state == null)
            state = owner.gameObject.AddComponent<SortMemoryRestoreState>();

        state._owner = owner;
        if (state._remainingFrames <= 0)
            state.enabled = false;
        return state;
    }

    internal void Schedule()
    {
        if (_owner != null && Time.frameCount < _skipUntilFrame)
            return;

        var signature = _owner != null ? SortMemoryController.GetCurrentSignature(_owner) : string.Empty;
        if (!string.IsNullOrEmpty(signature) && signature == _completedSignature)
            return;

        _remainingFrames = RefreshFrames;
        enabled = true;
    }

    internal void MarkUserSaved()
    {
        _completedSignature = _owner != null ? SortMemoryController.GetCurrentSignature(_owner) : _completedSignature;
        _skipUntilFrame = Time.frameCount + 60;
        _remainingFrames = 0;
        enabled = false;
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

        if (SortMemoryController.TryRestore(_owner))
        {
            _completedSignature = SortMemoryController.GetCurrentSignature(_owner);
            _remainingFrames = 0;
        }
        if (_remainingFrames <= 0)
            enabled = false;
    }
}

[HarmonyPatch(typeof(FilterView), "Setup")]
internal static class SortAndFilterSetupMemoryPatch
{
    private static void Postfix(FilterView __instance)
    {
        FilterMemoryRestoreState.GetOrAdd(__instance)?.Schedule();
    }
}

internal sealed class FilterMemoryRestoreState : MonoBehaviour
{
    private const int RestoreFrames = 1;

    private FilterView _owner;
    private ItemListScroll _ownerList;
    private int _remainingFrames;
    private string _pendingSignature;
    private string _completedSignature;

    internal static FilterMemoryRestoreState GetOrAdd(FilterView owner)
    {
        if (owner == null)
            return null;

        var state = owner.GetComponent<FilterMemoryRestoreState>();
        if (state == null)
            state = owner.gameObject.AddComponent<FilterMemoryRestoreState>();

        state._owner = owner;
        if (state._remainingFrames <= 0)
            state.enabled = false;
        return state;
    }

    internal void Schedule(
        int frames = RestoreFrames,
        ItemListScroll ownerList = null,
        bool force = false)
    {
        if (ownerList != null)
            _ownerList = ownerList;

        var signature = FilterMemoryController.GetCurrentSignature(_owner);
        if (!force && !string.IsNullOrEmpty(signature))
        {
            if (signature == _completedSignature)
                return;

            if (_remainingFrames > 0 && signature == _pendingSignature)
                return;
        }

        _pendingSignature = signature;
        _remainingFrames = Math.Max(1, frames);
        enabled = true;
    }

    private void LateUpdate()
    {
        if (_remainingFrames <= 0)
            return;

        _remainingFrames--;
        if (_remainingFrames > 0)
            return;

        if (_owner == null || !_owner.gameObject.activeInHierarchy)
        {
            enabled = false;
            return;
        }

        FilterMemoryController.TryRestore(_owner, _ownerList);
        _completedSignature = FilterMemoryController.GetCurrentSignature(_owner);
        _pendingSignature = null;
        enabled = false;
    }
}

[HarmonyPatch(typeof(FilterView), "OnFirstToggleGroupChanged")]
internal static class SortAndFilterFirstToggleGroupChangedMemoryPatch
{
    private static void Prefix(FilterView __instance)
    {
        // ToggleGroupLine has already changed its UI state before this callback begins.  Save
        // that state before the game's OnFilterChanged callback can synchronously rebuild the
        // item view (or throw while the view is closing), otherwise the Postfix may never run.
        FilterMemoryController.TrySave(__instance);
    }
}

[HarmonyPatch(typeof(FilterView), "OnSectionSelectionChanged")]
internal static class SortAndFilterSectionSelectionChangedMemoryPatch
{
    private static void Postfix(FilterView __instance)
    {
        FilterMemoryController.TrySave(__instance);
    }
}

[HarmonyPatch(typeof(FilterView), "OnSummaryItemDelete")]
internal static class SortAndFilterSummaryItemDeleteMemoryPatch
{
    private static void Postfix(FilterView __instance)
    {
        FilterMemoryController.TrySave(__instance);
    }
}

[HarmonyPatch(typeof(FilterView), "OnSummaryClearButtonClicked")]
internal static class SortAndFilterSummaryClearButtonMemoryPatch
{
    private static void Postfix(FilterView __instance)
    {
        FilterMemoryController.TrySave(__instance);
    }
}

[HarmonyPatch(typeof(LifeSkillCombatBeginView), "OnPresetStrategyToggleGroupActiveIndexChange")]
internal static class LifeSkillCombatPresetStrategySavePatch
{
    private static void Postfix(LifeSkillCombatBeginView __instance, int newIndex)
    {
        if (!Plugin.EnableStrategyPresetMemory || LifeSkillCombatPresetStrategyMemoryController.SuppressSave || __instance == null || newIndex < 0)
            return;

        try
        {
            var lifeSkillType = Traverse.Create(__instance).Field("_selectedSkillType").GetValue<sbyte>();
            MemoryOptimizationSettingsStore.SetStrategyPreset(lifeSkillType, newIndex);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save life skill strategy preset memory: " + ex);
        }
    }
}

[HarmonyPatch(typeof(LifeSkillCombatBeginView), "OnCurrentStageChanged")]
internal static class LifeSkillCombatPresetStrategyRestorePatch
{
    private static void Prefix()
    {
        LifeSkillCombatPresetStrategyMemoryController.SuppressSave = true;
    }

    private static void Postfix(LifeSkillCombatBeginView __instance)
    {
        try
        {
            if (Plugin.EnableStrategyPresetMemory && __instance != null)
                Restore(__instance);
        }
        finally
        {
            LifeSkillCombatPresetStrategyMemoryController.SuppressSave = false;
        }
    }

    private static void Restore(LifeSkillCombatBeginView __instance)
    {
        if (!Plugin.EnableStrategyPresetMemory || __instance == null)
            return;

        try
        {
            var traverse = Traverse.Create(__instance);
            var selectStrategyHolder = traverse.Field("selectStrategyHolder").GetValue<RectTransform>();
            if (selectStrategyHolder == null || !selectStrategyHolder.gameObject.activeSelf)
                return;

            var lifeSkillType = traverse.Field("_selectedSkillType").GetValue<sbyte>();
            var rememberedIndex = MemoryOptimizationSettingsStore.GetStrategyPreset(lifeSkillType);
            if (rememberedIndex < 0)
                return;

            var toggleGroup = traverse.Field("presetStrategyToggleGroup").GetValue<ToggleGroup>();
            if (toggleGroup == null || rememberedIndex >= toggleGroup.Count() || toggleGroup.GetActiveIndex() == rememberedIndex)
                return;

            toggleGroup.Set(rememberedIndex);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to restore life skill strategy preset memory: " + ex);
        }
    }
}

internal static class LifeSkillCombatPresetStrategyMemoryController
{
    internal static bool SuppressSave;
}

internal static class MakeSubtypeMemoryController
{
    internal static bool SuppressSave;
    internal static bool IsRefreshingSubtypeToggleGroup;

    internal static void Save(MakeSubPageMake page, int newIndex)
    {
        if (!Plugin.EnableMakeSubtypeMemory || SuppressSave || IsRefreshingSubtypeToggleGroup || page == null || newIndex < 0)
            return;

        try
        {
            if (!TryGetMemoryKey(page, out var lifeSkillType, out var makeItemTypeId, out var signature, out var subtypeIds))
                return;

            if (newIndex >= subtypeIds.Count)
                return;

            var subtypeId = subtypeIds[newIndex];
            MemoryOptimizationSettingsStore.SetMakeSubtypeIndex(lifeSkillType, makeItemTypeId, signature, newIndex, subtypeId, GetSubtypeName(subtypeId));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save make subtype memory: " + ex);
        }
    }

    internal static void Restore(MakeSubPageMake page)
    {
        if (!Plugin.EnableMakeSubtypeMemory || page == null)
            return;

        try
        {
            var toggleGroup = Traverse.Create(page).Field("subTypeToggleGroup").GetValue<ToggleGroup>();
            if (toggleGroup == null || !toggleGroup.gameObject.activeSelf)
                return;

            if (!TryGetMemoryKey(page, out var lifeSkillType, out var makeItemTypeId, out var signature, out var subtypeIds))
                return;

            var memory = GetLatestSubtypeMemory(lifeSkillType)
                ?? MemoryOptimizationSettingsStore.GetMakeSubtypeMemory(lifeSkillType, makeItemTypeId, signature);
            if (memory == null)
                return;

            var rememberedIndex = ResolveRememberedIndex(memory, subtypeIds);
            if (rememberedIndex < 0 || rememberedIndex >= subtypeIds.Count || toggleGroup.GetActiveIndex() == rememberedIndex)
                return;

            SuppressSave = true;
            try
            {
                toggleGroup.Set(rememberedIndex, forceRaiseEvent: true);
            }
            finally
            {
                SuppressSave = false;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to restore make subtype memory: " + ex);
        }
    }

    private static bool TryGetMemoryKey(MakeSubPageMake page, out sbyte lifeSkillType, out short makeItemTypeId, out string signature, out List<short> subtypeIds)
    {
        lifeSkillType = -1;
        makeItemTypeId = -1;
        signature = string.Empty;
        subtypeIds = null;

        var view = MakeSelectMaterialPatch.GetParentView(page);
        if (view == null)
            return false;

        subtypeIds = Traverse.Create(page).Field("_makeItemSubtypeIdList").GetValue<List<short>>();
        if (subtypeIds == null || subtypeIds.Count <= 1)
            return false;

        makeItemTypeId = Traverse.Create(page).Field("_makeItemTypeId").GetValue<short>();
        if (makeItemTypeId < 0)
            return false;

        lifeSkillType = view.CurLifeSkillType;
        signature = string.Join(",", subtypeIds.Select(id => id.ToString()).ToArray());
        return !string.IsNullOrEmpty(signature);
    }

    private static int ResolveRememberedIndex(MakeSubtypeMemoryEntry memory, List<short> subtypeIds)
    {
        if (memory == null || subtypeIds == null)
            return -1;

        if (memory.HasSubtypeId)
        {
            var index = subtypeIds.IndexOf((short)memory.SubtypeId);
            if (index >= 0)
                return index;
        }

        if (!string.IsNullOrEmpty(memory.SubtypeName))
        {
            for (var i = 0; i < subtypeIds.Count; i++)
            {
                if (GetSubtypeName(subtypeIds[i]) == memory.SubtypeName)
                    return i;
            }
        }

        return memory.SubtypeIndex;
    }

    private static MakeSubtypeMemoryEntry GetLatestSubtypeMemory(sbyte lifeSkillType)
    {
        var settings = MemoryOptimizationSettingsStore.Current;
        if (!settings.HasMakeSubtypeLastSelection || settings.MakeSubtypeLastLifeSkillType != lifeSkillType)
            return null;

        return new MakeSubtypeMemoryEntry
        {
            LifeSkillType = settings.MakeSubtypeLastLifeSkillType,
            MakeItemTypeId = -1,
            Signature = "latest",
            SubtypeIndex = -1,
            HasSubtypeId = settings.MakeSubtypeLastHasSubtypeId,
            SubtypeId = settings.MakeSubtypeLastSubtypeId,
            SubtypeName = settings.MakeSubtypeLastSubtypeName
        };
    }

    private static string GetSubtypeName(short subtypeId)
    {
        try
        {
            return MakeItemSubType.Instance[subtypeId]?.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "SubTypeToggleGroupOnActiveIndexChange")]
internal static class MakeSubtypeSelectionMemorySavePatch
{
    private static void Postfix(MakeSubPageMake __instance, int newIndex)
    {
        MakeSubtypeMemoryController.Save(__instance, newIndex);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "RefreshSubTypeToggleGroup")]
internal static class MakeSubtypeSelectionMemoryRestorePatch
{
    private static void Prefix()
    {
        MakeSubtypeMemoryController.IsRefreshingSubtypeToggleGroup = true;
    }

    private static void Postfix(MakeSubPageMake __instance)
    {
        MakeSubtypeMemoryController.Restore(__instance);
    }

    private static void Finalizer()
    {
        MakeSubtypeMemoryController.IsRefreshingSubtypeToggleGroup = false;
    }
}

internal static class MakeSubPageMakePerfectMemoryController
{
    private const int NoPerfectEffectId = 0;
    private const int NoPerfectDropdownIndex = -1;
    private const string NoPerfectName = "no-perfect";
    private const int RandomPerfectEffectId = -2;
    private const int RandomPerfectDropdownIndex = 1;
    private const int PerfectEffectDropdownOffset = 2;
    private const string RandomPerfectName = "random-perfect";
    private const int RandomMakeTargetItemTypeBase = 100000;
    private static readonly HashSet<MakeSubPageMake> ApplyingRestore = new HashSet<MakeSubPageMake>();
    private static int RefreshingPerfectDropdownDepth;
    private static int HandlingPerfectToggleDepth;
    private static int HandlingResourceChangeDepth;

    internal static bool SuppressSave => ApplyingRestore.Count > 0;
    internal static bool IsRefreshingPerfectDropdown => RefreshingPerfectDropdownDepth > 0;
    internal static bool IsHandlingPerfectToggle => HandlingPerfectToggleDepth > 0;
    internal static bool IsHandlingResourceChange => HandlingResourceChangeDepth > 0;

    internal static void BeginRefreshPerfectDropdown()
    {
        RefreshingPerfectDropdownDepth++;
    }

    internal static void EndRefreshPerfectDropdown()
    {
        if (RefreshingPerfectDropdownDepth > 0)
            RefreshingPerfectDropdownDepth--;
    }

    internal static void BeginPerfectToggleChange()
    {
        HandlingPerfectToggleDepth++;
    }

    internal static void EndPerfectToggleChange()
    {
        if (HandlingPerfectToggleDepth > 0)
            HandlingPerfectToggleDepth--;
    }

    internal static void BeginResourceChange()
    {
        HandlingResourceChangeDepth++;
    }

    internal static void EndResourceChange()
    {
        if (HandlingResourceChangeDepth > 0)
            HandlingResourceChangeDepth--;
    }

    internal static void RestoreSelectionAndResources(MakeSubPageMake page)
    {
        if (!Plugin.EnableMakePerfectMemory || page == null || ApplyingRestore.Contains(page))
            return;

        try
        {
            if (!TryGetTargetContext(page, out var lifeSkillType, out var targetItemType, out var targetTemplateId))
                return;

            var selectionKey = BuildSelectionKey(lifeSkillType, targetItemType, targetTemplateId);
            var selection = Plugin.EnableMakePerfectAffixMemory
                ? MemoryOptimizationSettingsStore.GetMakePerfectSelection(selectionKey)
                : null;
            var dropdown = GetPerfectDropdown(page);
            if (dropdown == null)
                return;

            ApplyingRestore.Add(page);
            try
            {
                var changed = false;
                if (Plugin.EnableMakePerfectAffixMemory)
                {
                    var desiredPerfect = selection?.PerfectEnabled ?? false;
                    if (desiredPerfect && selection?.HasSelection == true)
                    {
                        changed |= RestoreDropdownSelection(page, selection);
                    }
                    else if (dropdown.gameObject.activeSelf && dropdown.value != 0)
                    {
                        dropdown.SetValueWithoutNotify(0);
                        InvokePrivate(page, "OnPerfectDropdownValueChanged", 0);
                        changed = true;
                    }
                }

                if (Plugin.EnableMakePerfectRatioMemory)
                    changed |= RestoreResourceCounts(page);
                if (changed)
                    MakePageDeferredActionQueue.RequestCheckCondition(page);
            }
            finally
            {
                ApplyingRestore.Remove(page);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to restore make perfect memory for MakeSubPageMake: " + ex);
        }
    }

    internal static void RestoreAfterPerfectDropdownRefresh(MakeSubPageMake page)
    {
        if (!Plugin.EnableMakePerfectMemory
            || page == null
            || ApplyingRestore.Contains(page)
            || IsHandlingPerfectToggle
            || IsHandlingResourceChange)
            return;

        RestoreSelectionAndResources(page);
    }

    internal static void RestoreResourcesOnly(MakeSubPageMake page)
    {
        if (!Plugin.EnableMakePerfectRatioMemory || page == null || ApplyingRestore.Contains(page))
            return;

        try
        {
            ApplyingRestore.Add(page);
            try
            {
                if (RestoreResourceCounts(page))
                    MakePageDeferredActionQueue.RequestCheckCondition(page);
            }
            finally
            {
                ApplyingRestore.Remove(page);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to restore make resource memory for MakeSubPageMake: " + ex);
        }
    }

    internal static void SaveSelection(MakeSubPageMake page)
    {
        if (!Plugin.EnableMakePerfectAffixMemory || SuppressSave || IsRefreshingPerfectDropdown || page == null)
            return;

        try
        {
            if (!TryBuildSelectionEntry(page, out var selection))
                return;

            MemoryOptimizationSettingsStore.SetMakePerfectSelection(selection);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save make perfect selection memory for MakeSubPageMake: " + ex);
        }
    }

    internal static void SaveResources(MakeSubPageMake page)
    {
        if (!Plugin.EnableMakePerfectRatioMemory || SuppressSave || page == null)
            return;

        try
        {
            if (!TryBuildResourceEntry(page, out var resource))
                return;

            MemoryOptimizationSettingsStore.SetMakePerfectResource(resource);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save make resource memory for MakeSubPageMake: " + ex);
        }
    }

    internal static void SaveSelectionThenRestoreResources(MakeSubPageMake page)
    {
        if (!Plugin.EnableMakePerfectMemory || SuppressSave || IsRefreshingPerfectDropdown || page == null)
            return;

        SaveSelection(page);
        RestoreResourcesOnly(page);
    }

    private static bool TryBuildSelectionEntry(MakeSubPageMake page, out MakePerfectSelectionMemoryEntry selection)
    {
        selection = null;
        if (!TryGetTargetContext(page, out var lifeSkillType, out var targetItemType, out var targetTemplateId))
            return false;

        var dropdown = GetPerfectDropdown(page);
        if (dropdown == null)
            return false;

        var perfectEnabled = dropdown.gameObject.activeSelf && dropdown.value > 0;
        var selectedEffectId = -1;
        var selectedDropdownIndex = -1;
        var selectedName = string.Empty;
        var hasSelection = perfectEnabled && TryGetSelectedPerfectEffect(page, out selectedEffectId, out selectedDropdownIndex, out selectedName);

        selection = new MakePerfectSelectionMemoryEntry
        {
            MemoryKey = BuildSelectionKey(lifeSkillType, targetItemType, targetTemplateId),
            LifeSkillType = lifeSkillType,
            TargetItemType = targetItemType,
            TargetTemplateId = targetTemplateId,
            PerfectEnabled = perfectEnabled,
            HasSelection = hasSelection,
            SelectedSubtypeId = hasSelection ? selectedEffectId : -1,
            SelectedTemplateId = hasSelection ? selectedDropdownIndex : -1,
            SelectedName = hasSelection ? selectedName : string.Empty
        };
        return true;
    }

    private static bool TryBuildResourceEntry(MakeSubPageMake page, out MakePerfectResourceMemoryEntry resource)
    {
        resource = null;
        if (!TryGetTargetContext(page, out var lifeSkillType, out var targetItemType, out var targetTemplateId))
            return false;

        GetActivePerfectResourceKey(page, out var effectId, out var dropdownIndex, out var effectName);
        resource = new MakePerfectResourceMemoryEntry
        {
            MemoryKey = BuildResourceKey(lifeSkillType, targetItemType, targetTemplateId, effectId, dropdownIndex, effectName),
            LifeSkillType = lifeSkillType,
            TargetItemType = targetItemType,
            TargetTemplateId = targetTemplateId,
            SelectedSubtypeId = effectId,
            SelectedTemplateId = dropdownIndex,
            SelectedName = effectName,
            Wood = GetCurrentResourceCount(page, 1),
            Metal = GetCurrentResourceCount(page, 2),
            Jade = GetCurrentResourceCount(page, 3),
            Fabric = GetCurrentResourceCount(page, 4)
        };
        return true;
    }

    private static bool RestoreDropdownSelection(MakeSubPageMake page, MakePerfectSelectionMemoryEntry selection)
    {
        var dropdown = GetPerfectDropdown(page);
        if (dropdown == null || !dropdown.gameObject.activeSelf)
            return false;

        var index = FindPerfectEffectDropdownIndex(
            page,
            selection.SelectedSubtypeId,
            selection.SelectedTemplateId,
            selection.SelectedName);
        if (index < 0)
            return false;

        if (dropdown.value == index)
            return false;

        dropdown.SetValueWithoutNotify(index);
        InvokePrivate(page, "OnPerfectDropdownValueChanged", index);
        return true;
    }

    private static bool RestoreResourceCounts(MakeSubPageMake page)
    {
        if (!TryGetTargetContext(page, out var lifeSkillType, out var targetItemType, out var targetTemplateId))
            return false;

        GetActivePerfectResourceKey(page, out var effectId, out var dropdownIndex, out var effectName);
        var resourceKey = BuildResourceKey(lifeSkillType, targetItemType, targetTemplateId, effectId, dropdownIndex, effectName);
        var memory = MemoryOptimizationSettingsStore.GetMakePerfectResource(resourceKey);
        var traverse = Traverse.Create(page);
        var currentField = traverse.Field("_curMakeResourceCountInts");
        var lastField = traverse.Field("_lastMakeResourceCountInts");
        var resourceInts = currentField.GetValue<GameData.Domains.Character.ResourceInts>();
        var original = resourceInts;
        if (memory == null)
        {
            resourceInts.Initialize();
            if (ResourceEquals(original, resourceInts))
                return false;

            currentField.SetValue(resourceInts);
            lastField.SetValue(resourceInts);
            return true;
        }

        var totalMax = Math.Max(0, (int)traverse.Field("_maxMakeResourceTotalCount").GetValue<short>());
        var runningTotal = 0;

        RestoreResource(1, memory.Wood);
        RestoreResource(2, memory.Metal);
        RestoreResource(3, memory.Jade);
        RestoreResource(4, memory.Fabric);

        currentField.SetValue(resourceInts);
        lastField.SetValue(resourceInts);
        return !ResourceEquals(original, resourceInts);

        void RestoreResource(sbyte type, int wanted)
        {
            var max = GetResourceMax(page, type);
            var value = Mathf.Clamp(wanted, 0, max);
            if (totalMax > 0)
                value = Mathf.Clamp(value, 0, Math.Max(0, totalMax - runningTotal));

            SetResourceCount(ref resourceInts, type, value);
            runningTotal += value;
        }
    }

    private static bool ResourceEquals(GameData.Domains.Character.ResourceInts left, GameData.Domains.Character.ResourceInts right)
    {
        for (sbyte resourceType = 0; resourceType < 8; resourceType++)
        {
            if (left.Get(resourceType) != right.Get(resourceType))
                return false;
        }

        return true;
    }

    private static bool TryGetTargetContext(MakeSubPageMake page, out sbyte lifeSkillType, out int targetItemType, out int targetTemplateId)
    {
        lifeSkillType = -1;
        targetItemType = -1;
        targetTemplateId = -1;

        var view = MakeSelectMaterialPatch.GetParentView(page);
        var targetSlot = GetTargetSlot(page);
        if (view == null || targetSlot == null || !targetSlot.IsValid || targetSlot.ItemData == null)
            return false;

        lifeSkillType = view.CurLifeSkillType;
        if (lifeSkillType < 0)
            return false;

        var realKey = targetSlot.ItemData.RealKey;
        if (realKey.HasTemplate && realKey.ItemType >= 0)
        {
            targetItemType = realKey.ItemType;
            targetTemplateId = realKey.TemplateId;
            return targetTemplateId >= 0;
        }

        var key = targetSlot.ItemData.Key;
        if (key.HasTemplate)
        {
            targetItemType = key.ItemType >= 0 ? key.ItemType : GetRandomTargetItemType(page);
            targetTemplateId = key.TemplateId;
            return targetItemType >= 0 && targetTemplateId >= 0;
        }

        var makeItemSubTypeId = Traverse.Create(page).Field("_makeItemSubTypeId").GetValue<short>();
        if (makeItemSubTypeId >= 0)
        {
            targetItemType = GetRandomTargetItemType(page);
            targetTemplateId = makeItemSubTypeId;
            return targetItemType >= 0;
        }

        return lifeSkillType >= 0 && targetItemType >= 0 && targetTemplateId >= 0;
    }

    private static int GetRandomTargetItemType(MakeSubPageMake page)
    {
        try
        {
            var makeItemTypeId = Traverse.Create(page).Field("_makeItemTypeId").GetValue<short>();
            return RandomMakeTargetItemTypeBase + Math.Max(0, (int)makeItemTypeId);
        }
        catch
        {
            return RandomMakeTargetItemTypeBase;
        }
    }

    private static void GetActivePerfectResourceKey(MakeSubPageMake page, out int effectId, out int dropdownIndex, out string effectName)
    {
        if (TryGetSelectedPerfectEffect(page, out effectId, out dropdownIndex, out effectName))
            return;

        effectId = NoPerfectEffectId;
        dropdownIndex = NoPerfectDropdownIndex;
        effectName = NoPerfectName;
    }

    private static bool TryGetSelectedPerfectEffect(MakeSubPageMake page, out int effectId, out int dropdownIndex, out string effectName)
    {
        effectId = -1;
        dropdownIndex = -1;
        effectName = string.Empty;

        var dropdown = GetPerfectDropdown(page);
        if (dropdown == null || !dropdown.gameObject.activeSelf || dropdown.value <= 0)
            return false;

        dropdownIndex = dropdown.value;
        if (dropdownIndex == RandomPerfectDropdownIndex)
        {
            effectId = RandomPerfectEffectId;
            effectName = RandomPerfectName;
            return true;
        }

        var effectIds = Traverse.Create(page).Field("_perfectEffectIdList").GetValue<List<short>>();
        var effectIndex = dropdownIndex - PerfectEffectDropdownOffset;
        if (effectIds == null || effectIndex < 0 || effectIndex >= effectIds.Count)
            return false;

        effectId = effectIds[effectIndex];
        effectName = GetPerfectEffectName(effectId);
        return effectId >= 0;
    }

    private static int FindPerfectEffectDropdownIndex(
        MakeSubPageMake page,
        int effectId,
        int savedDropdownIndex,
        string effectName)
    {
        if (effectId == RandomPerfectEffectId
            || savedDropdownIndex == RandomPerfectDropdownIndex
            || effectName == RandomPerfectName)
            return RandomPerfectDropdownIndex;

        var effectIds = Traverse.Create(page).Field("_perfectEffectIdList").GetValue<List<short>>();
        if (effectIds == null || effectIds.Count == 0)
            return -1;

        if (effectId >= 0)
        {
            var index = effectIds.IndexOf((short)effectId);
            if (index >= 0)
                return index + PerfectEffectDropdownOffset;
        }

        if (!string.IsNullOrEmpty(effectName))
        {
            for (var i = 0; i < effectIds.Count; i++)
            {
                if (GetPerfectEffectName(effectIds[i]) == effectName)
                    return i + PerfectEffectDropdownOffset;
            }
        }

        return -1;
    }

    private static string GetPerfectEffectName(int effectId)
    {
        try
        {
            return effectId >= 0 ? EquipmentEffect.Instance[(short)effectId]?.Name ?? string.Empty : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static MakeTargetSlot GetTargetSlot(MakeSubPageMake page)
    {
        return Traverse.Create(page).Field("targetSlot").GetValue<MakeTargetSlot>();
    }

    private static FrameWork.UISystem.UIElements.CDropdown GetPerfectDropdown(MakeSubPageMake page)
    {
        return Traverse.Create(page).Field("perfectDropdown").GetValue<FrameWork.UISystem.UIElements.CDropdown>();
    }

    private static int GetCurrentResourceCount(MakeSubPageMake page, sbyte resourceType)
    {
        var resourceInts = Traverse.Create(page).Field("_curMakeResourceCountInts").GetValue<GameData.Domains.Character.ResourceInts>();
        return GetResourceCount(resourceInts, resourceType);
    }

    private static int GetResourceMax(MakeSubPageMake page, sbyte resourceType)
    {
        var maxResourceInts = Traverse.Create(page).Field("_maxMakeResourceCountInts").GetValue<GameData.Domains.Character.ResourceInts>();
        return Mathf.Max(0, GetResourceCount(maxResourceInts, resourceType));
    }

    private static int GetResourceCount(GameData.Domains.Character.ResourceInts resourceInts, sbyte resourceType)
    {
        return resourceType >= 0 && resourceType < 8 ? resourceInts.Get(resourceType) : 0;
    }

    private static void SetResourceCount(ref GameData.Domains.Character.ResourceInts resourceInts, sbyte resourceType, int count)
    {
        if (resourceType < 0 || resourceType >= 8)
            return;

        resourceInts.Set(resourceType, count);
    }

    private static string BuildSelectionKey(sbyte lifeSkillType, int targetItemType, int targetTemplateId)
    {
        return $"make-perfect-v2-selection:{lifeSkillType}:{targetItemType}:{targetTemplateId}";
    }

    private static string BuildResourceKey(sbyte lifeSkillType, int targetItemType, int targetTemplateId, int effectId, int dropdownIndex, string effectName)
    {
        return $"make-perfect-v2-resource:{lifeSkillType}:{targetItemType}:{targetTemplateId}:{effectId}:{dropdownIndex}:{effectName ?? string.Empty}";
    }

    private static void InvokePrivate(MakeSubPageMake page, string methodName, params object[] args)
    {
        AccessTools.Method(typeof(MakeSubPageMake), methodName)?.Invoke(page, args);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "SelectTarget")]
internal static class MakeSubPageMakePerfectMemoryRestoreTargetPatch
{
    private static void Postfix(MakeSubPageMake __instance)
    {
        MakeSubPageMakePerfectMemoryController.RestoreSelectionAndResources(__instance);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "SelectMaterial")]
internal static class MakeSubPageMakePerfectMemoryRestoreMaterialPatch
{
    private static void Postfix(MakeSubPageMake __instance)
    {
        MakeSubPageMakePerfectMemoryController.RestoreSelectionAndResources(__instance);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "RefreshPerfectDropdown", new Type[] { })]
internal static class MakeSubPageMakePerfectMemoryRefreshDropdownPatch
{
    private static void Prefix()
    {
        MakeSubPageMakePerfectMemoryController.BeginRefreshPerfectDropdown();
    }

    private static void Postfix(MakeSubPageMake __instance)
    {
        MakeSubPageMakePerfectMemoryController.RestoreAfterPerfectDropdownRefresh(__instance);
    }

    private static void Finalizer()
    {
        MakeSubPageMakePerfectMemoryController.EndRefreshPerfectDropdown();
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "OnMakePerfectToggleValueChanged")]
internal static class MakeSubPageMakePerfectMemorySaveTogglePatch
{
    // This callback was removed from MakeSubPageMake in the 2026-07 game update.
    // Skip this optional patch when running against a build without the callback;
    // otherwise Harmony aborts PatchAll and prevents every other mod patch loading.
    [HarmonyPrepare]
    private static bool Prepare()
    {
        return AccessTools.Method(typeof(MakeSubPageMake), "OnMakePerfectToggleValueChanged") != null;
    }

    private static void Prefix()
    {
        MakeSubPageMakePerfectMemoryController.BeginPerfectToggleChange();
    }

    private static void Postfix(MakeSubPageMake __instance)
    {
        MakeSubPageMakePerfectMemoryController.SaveSelectionThenRestoreResources(__instance);
    }

    private static void Finalizer()
    {
        MakeSubPageMakePerfectMemoryController.EndPerfectToggleChange();
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "OnPerfectDropdownValueChanged")]
internal static class MakeSubPageMakePerfectMemorySaveDropdownPatch
{
    private static void Postfix(MakeSubPageMake __instance)
    {
        MakeSubPageMakePerfectMemoryController.SaveSelectionThenRestoreResources(__instance);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "OnResourceCountChanged")]
internal static class MakeSubPageMakePerfectMemorySaveResourcePatch
{
    private static void Prefix()
    {
        MakeSubPageMakePerfectMemoryController.BeginResourceChange();
    }

    private static void Postfix(MakeSubPageMake __instance)
    {
        MakeSubPageMakePerfectMemoryController.SaveResources(__instance);
    }

    private static void Finalizer()
    {
        MakeSubPageMakePerfectMemoryController.EndResourceChange();
    }
}

internal static class LifeSkillAutoModeMemoryController
{
    internal static void SaveCurrent()
    {
        if (!Plugin.EnableLifeSkillAutoModeMemory)
            return;

        try
        {
            var model = SingletonObject.getInstance<LifeSkillCombatModel>();
            if (model != null)
                MemoryOptimizationSettingsStore.SetLifeSkillAutoMode(model.IsAuto);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save life skill auto mode memory: " + ex);
        }
    }

    internal static void RestoreCurrent(object view, MethodInfo updateAutoFightMarkMethod, MethodInfo refreshButtonInteractableMethod = null)
    {
        if (!Plugin.EnableLifeSkillAutoModeMemory
            || view == null
            || !MemoryOptimizationSettingsStore.Current.HasLifeSkillAutoMode)
            return;

        try
        {
            var model = SingletonObject.getInstance<LifeSkillCombatModel>();
            if (model == null)
                return;

            model.IsAuto = MemoryOptimizationSettingsStore.Current.LifeSkillAutoMode;
            updateAutoFightMarkMethod?.Invoke(view, new object[] { model.IsAuto });
            refreshButtonInteractableMethod?.Invoke(view, null);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to restore life skill auto mode memory: " + ex);
        }
    }
}

[HarmonyPatch(typeof(UI_LifeSkillCombat2), "OnClickAutoFight")]
internal static class LifeSkillCombatAutoModeSavePatch
{
    private static void Postfix()
    {
        LifeSkillAutoModeMemoryController.SaveCurrent();
    }
}

[HarmonyPatch(typeof(UI_LifeSkillCombat2), "OnInit")]
internal static class LifeSkillCombatAutoModeRestorePatch
{
    private static readonly MethodInfo UpdateAutoFightMarkMethod =
        AccessTools.Method(typeof(UI_LifeSkillCombat2), "UpdateAutoFightMark", new[] { typeof(bool) });

    private static void Postfix(UI_LifeSkillCombat2 __instance)
    {
        LifeSkillAutoModeMemoryController.RestoreCurrent(__instance, UpdateAutoFightMarkMethod);
    }
}

[HarmonyPatch(typeof(DebateView), "OnClickButtonAutoFight")]
internal static class DebateViewAutoModeSavePatch
{
    private static void Postfix()
    {
        LifeSkillAutoModeMemoryController.SaveCurrent();
    }
}

[HarmonyPatch(typeof(DebateView), "OnInit")]
internal static class DebateViewAutoModeRestorePatch
{
    private static readonly MethodInfo UpdateAutoFightMarkMethod =
        AccessTools.Method(typeof(DebateView), "UpdateAutoFightMark", new[] { typeof(bool) });

    private static readonly MethodInfo RefreshButtonInteractableMethod =
        AccessTools.Method(typeof(DebateView), "RefreshButtonInteractable");

    private static void Postfix(DebateView __instance)
    {
        LifeSkillAutoModeMemoryController.RestoreCurrent(__instance, UpdateAutoFightMarkMethod, RefreshButtonInteractableMethod);
    }
}
