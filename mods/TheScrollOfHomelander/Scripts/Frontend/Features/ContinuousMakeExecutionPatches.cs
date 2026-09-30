#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using GameData.Domains.Building;
using GameData.Domains.Item.Display;
using GameData.Domains.TaiwuEvent;
using GameData.Serializer;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BetterTaiwuScroll.Frontend;

[HarmonyPatch(typeof(MakeSubPageMake), "OnClickButtonConfirm")]
internal static class ContinuousMakeConfirmPatch
{
    // Route batch start/stop before the single-craft preview guard.
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(MakeSubPageMake __instance)
    {
        return ContinuousMakeExecutionController.HandleConfirmClick(__instance);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "CheckCondition")]
internal static class ContinuousMakeConfirmButtonStatePatch
{
    private static void Postfix(MakeSubPageMake __instance)
    {
        ContinuousMakeExecutionController.RefreshConfirmButtonState(__instance);
        ContinuousMakeUiController.RefreshBatchMakeButtonState(__instance);
    }
}

[HarmonyPatch(typeof(ViewMake), "RequestData")]
internal static class ContinuousMakeRequestDataPatch
{
    private static void Postfix(ViewMake __instance)
    {
        ContinuousMakeExecutionController.MarkRequestDataStarted(__instance);
    }
}

[HarmonyPatch(typeof(ViewMake), "Refresh")]
internal static class ContinuousMakeViewRefreshPatch
{
    private static void Postfix(ViewMake __instance)
    {
        ContinuousMakeExecutionController.TryContinueAfterViewRefresh(__instance);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "Refresh")]
internal static class ContinuousMakeSubPageRefreshPatch
{
    private static void Postfix(MakeSubPageMake __instance)
    {
        ContinuousMakeExecutionController.CleanupAfterSubPageRefresh(__instance);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "ReloadSlot")]
internal static class ContinuousMakeReloadSlotPatch
{
    private static void Postfix(MakeSubPageMake __instance)
    {
        ContinuousMakeExecutionController.CleanupAfterReloadSlot(__instance);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "RefreshAllMaterialList")]
internal static class ContinuousMakeRefreshAllMaterialListPatch
{
    private static void Postfix(MakeSubPageMake __instance)
    {
        ContinuousMakeExecutionController.RemoveZeroAmountMaterialsFromAllList(__instance);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "RefreshMaterialList")]
internal static class ContinuousMakeRefreshMaterialListPatch
{
    private static void Postfix(MakeSubPageMake __instance)
    {
        ContinuousMakeExecutionController.RemoveZeroAmountMaterialsFromVisibleList(__instance);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "OnItemClickMaterial")]
internal static class ContinuousMakeMaterialClickPatch
{
    private static bool Prefix(MakeSubPageMake __instance, object content)
    {
        return !ContinuousMakeExecutionController.ShouldBlockZeroAmountMaterialClick(__instance, content);
    }
}

[HarmonyPatch(typeof(Game.Components.ListStyleGeneralScroll.Item.ItemListScroll), "Init")]
internal static class ContinuousMakeMaterialListScrollInitPatch
{
    private static void Prefix(Game.Components.ListStyleGeneralScroll.Item.ItemListScroll __instance, string sortSaveKey)
    {
        ContinuousMakeExecutionController.MarkMaterialListScroll(__instance, sortSaveKey);
    }
}

[HarmonyPatch(typeof(Game.Components.ListStyleGeneralScroll.Item.ItemListScroll), "ApplySortAndFilter")]
internal static class ContinuousMakeMaterialListScrollApplySortAndFilterPatch
{
    private static void Postfix(Game.Components.ListStyleGeneralScroll.Item.ItemListScroll __instance)
    {
        ContinuousMakeExecutionController.RemoveZeroAmountMaterialsFromFilteredList(__instance);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "OnGetMakeResult")]
internal static class ContinuousMakeGetMakeResultCleanupPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(MakeSubPageMake __instance)
    {
        ContinuousMakeExecutionController.ReconcileMaterialListAfterMakeResult(__instance);
        MakePreviewRequest.RefreshConfirmState(__instance);
        ContinuousMakeExecutionController.RefreshConfirmButtonState(__instance);
        ContinuousMakeUiController.RefreshBatchMakeButtonState(__instance);
    }
}

[HarmonyPatch(typeof(UIElement), "SetOnInitArgs")]
internal static class ContinuousMakeGetItemArgsPatch
{
    private static bool Prefix(UIElement __instance, FrameWork.ArgumentBox box)
    {
        return !ContinuousMakeExecutionController.TryCaptureGetItemArgs(__instance, box);
    }
}

[HarmonyPatch(typeof(UIManager), "MaskUI")]
internal static class ContinuousMakeGetItemMaskPatch
{
    private static bool Prefix(UIElement elem)
    {
        return !ContinuousMakeExecutionController.ShouldSuppressGetItemMask(elem);
    }
}
