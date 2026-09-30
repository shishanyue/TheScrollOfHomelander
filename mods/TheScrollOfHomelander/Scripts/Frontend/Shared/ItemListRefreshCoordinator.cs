#nullable disable

using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game.Components.ListStyleGeneralScroll.Item;
using GameData.Domains.Taiwu.ExchangeSystem;
using HarmonyLib;

namespace BetterTaiwuScroll.Frontend;

internal static class ItemListRefreshCoordinator
{
    private sealed class InstanceState
    {
        internal bool Applying;
        internal IReadOnlyList<ITradeableContent> LastSource;
    }

    private static readonly ConditionalWeakTable<ItemListScroll, InstanceState> States =
        new ConditionalWeakTable<ItemListScroll, InstanceState>();

    internal static void PrepareItemList(
        ItemListScroll list,
        ref IReadOnlyList<ITradeableContent> source)
    {
        if (list == null)
            return;

        var state = States.GetOrCreateValue(list);
        if (state.Applying)
            return;

        try
        {
            state.Applying = true;
            state.LastSource = source;
            source = ExchangeSearchBoxOptimizationSupport.PrepareItemList(list, source);
        }
        finally
        {
            state.Applying = false;
        }
    }

    internal static void OnItemListSet(ItemListScroll list)
    {
        if (list == null)
            return;

        // Restore only when the live UI signature differs from memory; the restore state
        // coalesces repeated SetItemList calls into one next-frame check.
        FilterMemoryController.ScheduleRestore(list);

        // ContainerCompactPatches compares its complete layout signature and performs
        // no writes or rerenders when row count, scale and original dimensions are stable.
        ContainerCompactPatches.RegisterItemListScroll(list, rerender: false);
    }
}

internal static class ItemListRefreshCoordinatorPatchLifecycle
{
    private static readonly MethodInfo BasicMethod = AccessTools.Method(
        typeof(ItemListScroll),
        nameof(ItemListScroll.SetItemList),
        new[] { typeof(IReadOnlyList<ITradeableContent>) });
    private static readonly MethodInfo SelectedMethod = AccessTools.Method(
        typeof(ItemListScroll),
        nameof(ItemListScroll.SetItemList),
        new[] { typeof(IReadOnlyList<ITradeableContent>), typeof(int) });
    private static bool _installed;
    private static bool _unavailable;

    internal static void Refresh(Harmony harmony)
    {
        if (harmony == null || _unavailable)
            return;

        var shouldInstall = Plugin.EnableInventorySearchBoxOptimization
            || Plugin.EnableFilterMemory
            || Plugin.EnableContainerCompact;
        if (shouldInstall == _installed)
            return;

        if (shouldInstall)
        {
            try
            {
                harmony.CreateClassProcessor(typeof(ItemListRefreshCoordinatorBasicPatch)).Patch();
                harmony.CreateClassProcessor(typeof(ItemListRefreshCoordinatorSelectedPatch)).Patch();
            }
            catch (System.Exception ex)
            {
                _unavailable = true;
                UnityEngine.Debug.LogWarning("[BetterTaiwuScroll] Disabled item-list coordinator patches: " + ex);
                try
                {
                    if (BasicMethod != null) harmony.Unpatch(BasicMethod, HarmonyPatchType.All, harmony.Id);
                    if (SelectedMethod != null) harmony.Unpatch(SelectedMethod, HarmonyPatchType.All, harmony.Id);
                }
                catch (System.Exception cleanup)
                {
                    UnityEngine.Debug.LogWarning("[BetterTaiwuScroll] Item-list coordinator cleanup incomplete: " + cleanup);
                }
                return;
            }
        }
        else
        {
            if (BasicMethod != null)
                harmony.Unpatch(BasicMethod, HarmonyPatchType.All, harmony.Id);
            if (SelectedMethod != null)
                harmony.Unpatch(SelectedMethod, HarmonyPatchType.All, harmony.Id);
        }
        _installed = shouldInstall;
    }

    internal static void Reset()
    {
        _installed = false;
        _unavailable = false;
    }
}

[HarmonyPatch(typeof(ItemListScroll), nameof(ItemListScroll.SetItemList),
    new[] { typeof(IReadOnlyList<ITradeableContent>) })]
internal static class ItemListRefreshCoordinatorBasicPatch
{
    private static void Prefix(ItemListScroll __instance, ref IReadOnlyList<ITradeableContent> list)
    {
        ItemListRefreshCoordinator.PrepareItemList(__instance, ref list);
    }

    private static void Postfix(ItemListScroll __instance)
    {
        ItemListRefreshCoordinator.OnItemListSet(__instance);
    }
}

[HarmonyPatch(typeof(ItemListScroll), nameof(ItemListScroll.SetItemList),
    new[] { typeof(IReadOnlyList<ITradeableContent>), typeof(int) })]
internal static class ItemListRefreshCoordinatorSelectedPatch
{
    private static void Prefix(ItemListScroll __instance, ref IReadOnlyList<ITradeableContent> list)
    {
        ItemListRefreshCoordinator.PrepareItemList(__instance, ref list);
    }

    private static void Postfix(ItemListScroll __instance)
    {
        ItemListRefreshCoordinator.OnItemListSet(__instance);
    }
}
