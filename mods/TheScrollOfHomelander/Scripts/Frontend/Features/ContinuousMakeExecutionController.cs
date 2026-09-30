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

internal static partial class ContinuousMakeExecutionController
{
    private const int MinBatchMakeSpeed = 1;
    private const int MaxBatchMakeSpeed = 20;

    private static readonly Dictionary<ViewMake, MakeSubPageMake> PendingPages = new();
    private static readonly HashSet<ViewMake> AwaitingDataRefreshViews = new();
    private static readonly HashSet<MakeSubPageMake> RunningPages = new();
    private static readonly HashSet<ViewMake> ActiveContinuousViews = new();
    private static readonly HashSet<ViewMake> StopRequestedViews = new();
    private static readonly HashSet<ViewMake> OneShotBatchMakeViews = new();
    private static readonly HashSet<MakeSubPageMake> PendingMaterialSlotCleanupPages = new();
    private static readonly HashSet<Game.Components.ListStyleGeneralScroll.Item.ItemListScroll> MaterialListScrolls = new();
    private static readonly Dictionary<ViewMake, ResultCollector> ResultCollectors = new();
    // The game's own label for the confirm button, captured before the batch relabels it.
    private static readonly Dictionary<MakeSubPageMake, string> _confirmLabelByPage = new();

    private static ViewMake _activeResultView;
    private static bool _suppressNextGetItemMask;
    private static bool _showingMergedGetItem;
    internal static bool IsSelectingMaterialForContinuation { get; private set; }

    internal static bool IsBatchActive(ViewMake view) => view != null &&
        (ContinuousMakeUiController.IsContinuousMakeEnabledFor(view) || OneShotBatchMakeViews.Contains(view));

    internal static void Reset()
    {
        MakeExecutionLifetime.CancelAll();
        MakeConfirmPatch.WaitingPages.Clear();
        PendingPages.Clear();
        AwaitingDataRefreshViews.Clear();
        RunningPages.Clear();
        ActiveContinuousViews.Clear();
        StopRequestedViews.Clear();
        OneShotBatchMakeViews.Clear();
        PendingMaterialSlotCleanupPages.Clear();
        MaterialListScrolls.Clear();
        ResultCollectors.Clear();
        _confirmLabelByPage.Clear();
        _activeResultView = null;
        _suppressNextGetItemMask = false;
        _showingMergedGetItem = false;
        IsSelectingMaterialForContinuation = false;
        MakePageDeferredActionQueue.Clear();
    }

    internal static void Close(MakeSubPageMake page)
    {
        if (page == null) return;
        var view = MakeSelectMaterialPatch.GetParentView(page);
        RunningPages.Remove(page);
        PendingMaterialSlotCleanupPages.Remove(page);
        _confirmLabelByPage.Remove(page);
        MaterialListScrolls.RemoveWhere(scroll => scroll == null || scroll.GetComponentInParent<MakeSubPageMake>(true) == page);
        if (view == null) return;
        PendingPages.Remove(view);
        AwaitingDataRefreshViews.Remove(view);
        ActiveContinuousViews.Remove(view);
        StopRequestedViews.Remove(view);
        OneShotBatchMakeViews.Remove(view);
        ResultCollectors.Remove(view);
        if (_activeResultView == view) { _activeResultView = null; _suppressNextGetItemMask = false; }
    }

    /// <summary>
    /// The panel's 制作 button always keeps the game's own behaviour. Only a batch that is
    /// actually running takes the click over (and that click then means "stop"). Enabling
    /// continuous mode or the batch button never disables normal crafting, so batch settings
    /// cannot affect 制作.
    /// </summary>
    internal static bool HandleConfirmClick(MakeSubPageMake page)
    {
        if (page == null)
            return true;

        if (TryStopByConfirmClick(page))
            return false;

        var view = MakeSelectMaterialPatch.GetParentView(page);
        if (view == null || !RunningPages.Contains(page))
            return true;

        // A batch is running on this page and owns the round it already started; let the
        // player stop it with the same button.
        StopRequestedViews.Add(view);
        RefreshConfirmButtonState(page);
        return false;
    }

    internal static bool TryStopByConfirmClick(MakeSubPageMake page)
    {
        var view = MakeSelectMaterialPatch.GetParentView(page);
        if (view == null || !ActiveContinuousViews.Contains(view))
            return false;

        StopRequestedViews.Add(view);
        PendingPages[view] = page;
        RefreshConfirmButtonState(page);
        return true;
    }

    internal static void RefreshConfirmButtonState(MakeSubPageMake page)
    {
        var view = MakeSelectMaterialPatch.GetParentView(page);
        if (view == null || !ActiveContinuousViews.Contains(view))
            return;

        var button = GetConfirmButton(page);
        var text = GetConfirmText(page);
        var tip = GetConfirmTip(page);
        var stopRequested = StopRequestedViews.Contains(view);

        if (text != null)
        {
            // Remember the game's own label the first time it is replaced, so it can be
            // handed back verbatim when the batch ends.
            if (!_confirmLabelByPage.ContainsKey(page))
                _confirmLabelByPage[page] = text.text;
            text.text = ModLocalization.T("Stop Crafting");
        }
        if (button != null)
            button.interactable = !stopRequested;
        if (tip != null)
            tip.enabled = false;
    }

    internal static bool TryStartOneShotBatch(MakeSubPageMake page)
    {
        var view = MakeSelectMaterialPatch.GetParentView(page);
        if (view == null)
            return false;

        // A second click while a one-shot batch is running requests a clean
        // stop. Keep the one-shot marker alive until EndContinuousMake clears
        // it, otherwise ShouldRun would abandon the state machine mid-round.
        if (TryStopByConfirmClick(page))
            return true;

        OneShotBatchMakeViews.Add(view);
        if (TryStartConfiguredBatch(page))
            return true;

        OneShotBatchMakeViews.Remove(view);
        RefreshMakeCondition(page);
        return false;
    }

    private static bool TryStartConfiguredBatch(MakeSubPageMake page)
    {
        var view = MakeSelectMaterialPatch.GetParentView(page);
        if (view == null
            || !ShouldRun(page)
            || MakeSubmissionRequest.IsPending(page)
            || ActiveContinuousViews.Contains(view)
            || RunningPages.Contains(page)
            || !CanStartCoroutine(page))
            return false;

        if (ResultCollectors.TryGetValue(view, out var existingCollector) && existingCollector.Finishing)
            return false;

        var material = GetNextMaterialForBatchStart(page);
        if (material == null)
        {
            LogNoEligibleMaterial(view, "start");
            return false;
        }

        ActiveContinuousViews.Add(view);
        StopRequestedViews.Remove(view);
        PendingPages.Remove(view);
        AwaitingDataRefreshViews.Remove(view);
        _activeResultView = view;
        RefreshConfirmButtonState(page);

        var settings = ContinuousMakeSettingsStore.GetFor(view);
        TryGetMaterialConfigGrade(material, out var rawGrade);
        Debug.Log("[BetterTaiwuScroll] Batch make started for life skill " + view.CurLifeSkillType
            + " with material " + material.RealKey.TemplateId
            + " (grade=" + RawGradeToDisplay(rawGrade)
            + ", allowed=" + settings.HighestMaterialGrade + "-" + settings.LowestMaterialGrade + ").");
        LogCombinedFoodGate(page, material);

        MakeExecutionLifetime.Run(page, ContinueAfterRefresh(page));
        return true;
    }

    internal static void MarkRequestDataStarted(ViewMake view)
    {
        if (view != null && PendingPages.ContainsKey(view))
            AwaitingDataRefreshViews.Add(view);
    }

    internal static void TryContinueAfterViewRefresh(ViewMake view)
    {
        if (view == null || !AwaitingDataRefreshViews.Remove(view))
            return;

        if (!PendingPages.TryGetValue(view, out var pendingPage))
            return;

        PendingPages.Remove(view);

        var page = GetActiveMakePage(view) ?? pendingPage;
        if (page == null || RunningPages.Contains(page) || !CanStartCoroutine(page))
        {
            FinishAndShowResults(view, null);
            return;
        }

        if (!ShouldContinue(page))
        {
            CancelExpiredMaterialSelection(page);
            FinishAndShowResults(view, page);
            return;
        }

        if (GetNextMaterialForBatchStart(page) == null)
        {
            LogNoEligibleMaterial(view, "refresh");
            CancelExpiredMaterialSelection(page);
            FinishAndShowResults(view, page);
            return;
        }

        MakeExecutionLifetime.Run(page, ContinueAfterRefresh(page));
    }

    internal static void CleanupAfterSubPageRefresh(MakeSubPageMake page)
    {
        if (page == null || !PendingMaterialSlotCleanupPages.Remove(page))
            return;

        var canceled = CancelExpiredMaterialSelection(page);
        ForceMaterialListRerender(page);
        if (canceled)
            RefreshMakeCondition(page);
        RefreshMaterialAvailability(page, "batch refresh");
    }

    internal static void CleanupAfterReloadSlot(MakeSubPageMake page)
    {
        CancelExpiredMaterialSelection(page);
        RefreshMaterialAvailability(page, "reload slot");
    }

    // Display amounts are updated locally during a burst and by RequestData afterwards.
    internal static void RefreshMaterialAvailability(MakeSubPageMake page, string reason)
    {
        if (page == null || page.gameObject == null || !page.gameObject.activeInHierarchy)
            return;

        try
        {
            RemoveZeroAmountMaterials(page, "_allMaterialList", false);
            var removedFromVisible = RemoveZeroAmountMaterials(page, "_materialList", false);
            // Re-render even when nothing was removed locally: the scroll keeps its own
            // filtered copy, and a stale positive amount only disappears after a rebuild.
            ForceMaterialListRerender(page);
            var canceled = CancelExpiredMaterialSelection(page);
            if (removedFromVisible || canceled)
                RefreshMakeCondition(page);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make material availability refresh failed ("
                + reason + "): " + ex.Message);
        }
    }

    private static bool IsSameMaterialSource(ItemDisplayData left, ItemDisplayData right)
    {
        return left != null
            && right != null
            && left.RealKey == right.RealKey
            && left.ItemSourceTypeEnum == right.ItemSourceTypeEnum;
    }

    /// <summary>
    /// Mirrors a locally consumed amount onto every list entry that shows the same
    /// material from the same source. The page's material lists and the slots can hold
    /// different ItemDisplayData instances for one stack, so updating only the selected
    /// instance leaves other rows showing an amount that was already consumed.
    /// </summary>
    private static void SyncConsumedMaterialAmount(MakeSubPageMake page, ItemDisplayData material, int remaining)
    {
        if (page == null || material == null)
            return;

        try
        {
            var traverse = Traverse.Create(page);
            foreach (var fieldName in new[] { "_allMaterialList", "_materialList" })
            {
                var list = traverse.Field(fieldName).GetValue<List<ItemDisplayData>>();
                if (list == null)
                    continue;

                foreach (var entry in list)
                {
                    if (IsSameMaterialSource(entry, material))
                        entry.Amount = remaining;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make material amount sync failed: " + ex.Message);
        }
    }

    internal static void RemoveZeroAmountMaterialsFromVisibleList(MakeSubPageMake page)
    {
        var removed = RemoveZeroAmountMaterials(page, "_materialList", false);
        if (!removed)
            return;

        ForceMaterialListRerender(page);
        CancelExpiredMaterialSelection(page);
        MakePageDeferredActionQueue.RequestCheckCondition(page);
    }

    internal static void RemoveZeroAmountMaterialsFromAllList(MakeSubPageMake page)
    {
        RemoveZeroAmountMaterials(page, "_allMaterialList", false);
    }

    /// <summary>
    /// Runs after the page has received a make result, i.e. once per craft and at the end
    /// of a batch, to drop catalysts that were just used up. Removing rows here cannot
    /// recurse: the condition refresh below only runs when a row was actually removed.
    /// </summary>
    internal static void ReconcileMaterialListAfterMakeResult(MakeSubPageMake page)
    {
        if (page == null || page.gameObject == null || !page.gameObject.activeInHierarchy)
            return;

        try
        {
            RemoveZeroAmountMaterials(page, "_allMaterialList", false);
            var removed = RemoveZeroAmountMaterials(page, "_materialList", false);
            if (!removed)
                return;

            ForceMaterialListRerender(page);
            if (CancelExpiredMaterialSelection(page))
                RefreshMakeCondition(page);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make material reconcile failed: " + ex.Message);
        }
    }

    internal static bool ShouldBlockZeroAmountMaterialClick(MakeSubPageMake page, object content)
    {
        if (page == null || content is not ItemDisplayData material || material.Amount > 0)
            return false;

        CancelExpiredMaterialSelection(page);
        ForceMaterialListRerender(page);
        RefreshMakeCondition(page);
        return true;
    }

    internal static void MarkMaterialListScroll(Game.Components.ListStyleGeneralScroll.Item.ItemListScroll scroll, string sortSaveKey)
    {
        if (scroll == null)
            return;

        var page = scroll.GetComponentInParent<MakeSubPageMake>(true);
        if (page != null && ReferenceEquals(Traverse.Create(page).Field("materialListScroll").GetValue(), scroll))
            MaterialListScrolls.Add(scroll);
    }

    internal static void RemoveZeroAmountMaterialsFromFilteredList(Game.Components.ListStyleGeneralScroll.Item.ItemListScroll scroll)
    {
        if (scroll == null || !MaterialListScrolls.Contains(scroll))
            return;

        try
        {
            var traverse = Traverse.Create(scroll);
            var filteredData = traverse.Field("_filteredData").GetValue() as IList;
            if (filteredData == null)
                return;

            var removedAny = false;
            var removedSelected = false;
            var selectedIndex = GetIntField(traverse, "_selectedIndex");
            for (var i = filteredData.Count - 1; i >= 0; i--)
            {
                if (!IsZeroAmountContent(filteredData[i]))
                    continue;

                if (i == selectedIndex)
                    removedSelected = true;
                else if (i < selectedIndex)
                    selectedIndex--;

                filteredData.RemoveAt(i);
                removedAny = true;
            }

            if (removedSelected || selectedIndex >= filteredData.Count)
                traverse.Field("_selectedIndex").SetValue(-1);
            else if (removedAny)
                traverse.Field("_selectedIndex").SetValue(selectedIndex);

            if (removedAny)
                AccessTools.Method(scroll.GetType(), "RefreshEmpty")?.Invoke(scroll, Array.Empty<object>());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make filtered material list cleanup failed: " + ex.Message);
        }
    }

    private static IEnumerator ContinueAfterRefresh(MakeSubPageMake page)
    {
        var current = MakeExecutionLifetime.Capture(page);
        RunningPages.Add(page);
        var view = MakeSelectMaterialPatch.GetParentView(page);
        var dataChanged = false;
        var continueAfterRefresh = false;
        try
        {
            var settings = ContinuousMakeSettingsStore.GetFor(view);
            var batchMakeSpeed = GetBatchMakeSpeed(settings);
            yield return WaitForUiStage(page, batchMakeSpeed, 1, null);

            if (!ShouldRun(page))
                yield break;
            if (IsStopRequested(page))
                yield break;

            for (var i = 0; i < batchMakeSpeed; i++)
            {
                if (!ShouldRun(page))
                    yield break;
                if (IsStopRequested(page))
                    break;

                var material = GetNextMaterial(page);
                if (material == null)
                    break;

                SelectMaterial(page, material);

                yield return WaitForUiStage(
                    page,
                    batchMakeSpeed,
                    GetMinimumStageFrames(batchMakeSpeed, ContinuationStage.AfterMaterialSelect),
                    () => IsSelectedMaterial(page, material));

                if (!ShouldRun(page))
                    yield break;
                if (IsStopRequested(page))
                    break;
                if (!IsSelectedMaterial(page, material))
                    yield break;

                var parentView = MakeSelectMaterialPatch.GetParentView(page);
                if (parentView != null && Plugin.EnableBestTool)
                    AccessTools.Method(typeof(ViewMake), "AutoSelectTool")?.Invoke(parentView, Array.Empty<object>());

                yield return WaitForUiStage(
                    page,
                    batchMakeSpeed,
                    GetMinimumStageFrames(batchMakeSpeed, ContinuationStage.AfterToolSelect),
                    () => IsToolSelectionReady(page, settings));

                if (!ShouldRun(page))
                    yield break;
                if (IsStopRequested(page))
                    break;

                if (!IsToolSelectionReady(page, settings))
                {
                    Debug.LogWarning("[BetterTaiwuScroll] Continuous make stopped: tool selection is not ready.");
                    yield break;
                }

                if (Plugin.EnableMaxProductCount)
                    MakeSelectMaterialPatch.SetMakeCountToMax(page);

                yield return WaitForUiStage(page, batchMakeSpeed, 1, () => IsConfirmInteractable(page));

                if (!ShouldRun(page))
                    yield break;
                if (IsStopRequested(page))
                    break;
                if (!IsConfirmInteractable(page))
                {
                    Debug.LogWarning("[BetterTaiwuScroll] Continuous make stopped: confirm state did not refresh.");
                    yield break;
                }

                if (!settings.AllowBareHand && IsSelectedToolEmpty(page))
                {
                    Debug.LogWarning("[BetterTaiwuScroll] Continuous make stopped: bare-hand making is disabled and no tool is selected.");
                    yield break;
                }

                if (settings.EnableDurabilityProtection && !ApplyDurabilityProtectionMakeCount(page))
                {
                    Debug.LogWarning("[BetterTaiwuScroll] Continuous make stopped: durability protection rejected the make count.");
                    yield break;
                }

                if (settings.EnableDurabilityProtection && WouldSelectedToolBreak(page))
                {
                    Debug.LogWarning("[BetterTaiwuScroll] Continuous make stopped: selected tool would break.");
                    yield break;
                }

                var makeResultReady = false;
                var currentMakeResult = default(MakeResult);
                yield return RefreshCurrentRandomMakeResult(page, (ready, result) =>
                {
                    makeResultReady = ready;
                    currentMakeResult = result;
                });
                if (IsStopRequested(page))
                    break;
                if (!makeResultReady)
                {
                    Debug.LogWarning("[BetterTaiwuScroll] Continuous make stopped: make result data is not ready.");
                    yield break;
                }

                // Use the game's current domain call sequence without opening
                // the native result window between continuation rounds.
                var makeResult = DirectMakeResult.Fail;
                yield return MakeOnceWithoutViewRefresh(page, material, currentMakeResult,
                    () => dataChanged = true, value => makeResult = value);
                if (!makeResult.Success)
                    yield break;

                // The lists and slots share display objects. Update them before
                // choosing the next material or calculating durability limits.
                ApplyLocalConsumption(page, material, makeResult.Tool, makeResult.MakeCount);
                var tool = makeResult.Tool;
                if (tool != null && !ViewMake.IsEmptyTool(tool) && tool.Durability <= 0)
                    break;
            }

            continueAfterRefresh = true;
        }
        finally
        {
            RunningPages.Remove(page);
            if (current() && dataChanged && CanRequestData(view))
            {
                // Even a failed/stopped burst must reload state after a make
                // was submitted. Only a normally completed burst may resume.
                if (!continueAfterRefresh)
                    StopRequestedViews.Add(view);
                ClearDisplayData(page);
                PendingMaterialSlotCleanupPages.Add(page);
                PendingPages[view] = page;
                view.RequestData();
            }
            else if (current())
            {
                // Nothing was submitted, but the lists may still hold amounts consumed
                // earlier in this session; re-sync before the batch button is judged.
                RefreshMaterialAvailability(page, "batch end");
                FinishAndShowResults(view, page);
            }
        }
    }

    internal static bool TryCaptureGetItemArgs(UIElement element, FrameWork.ArgumentBox box)
    {
        if (_showingMergedGetItem || element != UIElement.GetItem || _activeResultView == null || box == null)
            return false;

        if (!ResultCollectors.TryGetValue(_activeResultView, out var collector))
            return false;

        if (!box.Get("ItemList", out List<ItemDisplayData> itemList) || itemList == null)
            return false;

        if (!box.Get("ObtainType", out sbyte obtainType) || obtainType != 6)
            return false;

        collector.AddItems(itemList);
        if (box.Get("InWareHouse", out bool inWarehouse))
            collector.InWarehouse = inWarehouse;
        if (box.Get("CloseAction", out Action closeAction) && closeAction != null)
            collector.CloseActions.Add(closeAction);

        collector.CapturedBatchCount++;
        collector.LastCaptureFrame = Time.frameCount;
        _suppressNextGetItemMask = true;
        return true;
    }

    internal static bool ShouldSuppressGetItemMask(UIElement element)
    {
        if (element != UIElement.GetItem || !_suppressNextGetItemMask)
            return false;

        _suppressNextGetItemMask = false;
        return true;
    }

    private static bool ShouldRun(MakeSubPageMake page)
    {
        if (page == null || !CanStartCoroutine(page))
            return false;

        var view = MakeSelectMaterialPatch.GetParentView(page);
        return view != null
            && Plugin.IsEnabledForLifeSkill(view.CurLifeSkillType)
            && (ContinuousMakeUiController.IsContinuousMakeEnabledFor(view) || OneShotBatchMakeViews.Contains(view));
    }

    private static bool ShouldContinue(MakeSubPageMake page)
    {
        return ShouldRun(page) && !IsStopRequested(page);
    }

    private static bool IsStopRequested(MakeSubPageMake page)
    {
        var view = MakeSelectMaterialPatch.GetParentView(page);
        return view != null && StopRequestedViews.Contains(view);
    }

    /// <summary>
    /// Re-syncs the visible material list once and then looks for the next eligible
    /// catalyst, so a stale row left over from an earlier batch cannot make the batch
    /// button look dead while a valid catalyst is still listed underneath it.
    /// </summary>
    private static ItemDisplayData GetNextMaterialForBatchStart(MakeSubPageMake page)
    {
        var material = GetNextMaterial(page);
        if (material != null)
            return material;

        RefreshMaterialAvailability(page, "batch start");
        return GetNextMaterial(page);
    }

    private static int GetBatchMakeSpeed(ContinuousMakeSettings settings)
    {
        return Mathf.Clamp(settings?.BatchMakeSpeed ?? MinBatchMakeSpeed, MinBatchMakeSpeed, MaxBatchMakeSpeed);
    }

    private static IEnumerator WaitForUiStage(MakeSubPageMake page, int speed, int minimumFrames, Func<bool> ready)
    {
        speed = Mathf.Clamp(speed, MinBatchMakeSpeed, MaxBatchMakeSpeed);
        minimumFrames = Mathf.Max(0, minimumFrames);
        for (var frame = 0; frame < minimumFrames; frame++)
        {
            if (!ShouldContinue(page))
                yield break;

            yield return null;
        }

        if (ready == null)
            yield break;

        const int budget = 120;
        for (var frame = minimumFrames; frame < budget; frame++)
        {
            if (!ShouldContinue(page) || ready())
                yield break;

            yield return null;
        }
    }

    private static int GetMinimumStageFrames(int speed, ContinuationStage stage)
    {
        speed = Mathf.Clamp(speed, MinBatchMakeSpeed, MaxBatchMakeSpeed);
        if (speed <= 1)
            return 1;

        return stage switch
        {
            ContinuationStage.AfterMaterialSelect => speed >= 7 ? 0 : 1,
            ContinuationStage.AfterToolSelect => speed >= 4 ? 0 : 1,
            _ => 1
        };
    }

    private static bool CanStartCoroutine(MakeSubPageMake page)
    {
        return page != null && page.gameObject != null && page.gameObject.activeInHierarchy;
    }

    private static bool CanRequestData(ViewMake view)
    {
        return view != null && view.gameObject != null && view.gameObject.activeInHierarchy;
    }

    private static ResultCollector GetOrCreateCollector(ViewMake view)
    {
        if (!ResultCollectors.TryGetValue(view, out var collector))
        {
            collector = new ResultCollector();
            ResultCollectors[view] = collector;
        }

        return collector;
    }

    private static void FinishAndShowResults(ViewMake view, MonoBehaviour coroutineOwner)
    {
        var activePage = GetActiveMakePage(view) ?? coroutineOwner as MakeSubPageMake;
        EndContinuousMake(view, activePage);

        if (view == null || !ResultCollectors.TryGetValue(view, out var collector))
            return;

        if (collector.Finishing)
            return;

        collector.Finishing = true;
        PendingPages.Remove(view);
        AwaitingDataRefreshViews.Remove(view);
        if (activePage != null)
            PendingMaterialSlotCleanupPages.Remove(activePage);

        if (activePage != null && activePage.gameObject.activeInHierarchy)
            MakeExecutionLifetime.Run(activePage, ShowCollectedResultsWhenReady(view, collector));
        else
            ShowCollectedResults(view, collector);
    }

    private static void EndContinuousMake(ViewMake view, MakeSubPageMake page)
    {
        if (view != null)
        {
            ActiveContinuousViews.Remove(view);
            StopRequestedViews.Remove(view);
            OneShotBatchMakeViews.Remove(view);
            PendingPages.Remove(view);
            AwaitingDataRefreshViews.Remove(view);
        }

        if (page != null)
        {
            // The panel's 制作 button was repurposed while the batch ran; hand it back to the
            // game so a normal click works again.
            RestoreConfirmButton(page);
            RefreshMakeCondition(page);
        }
    }

    /// <summary>
    /// Restores the confirm button the game owns: while a batch runs the Mod relabels it to
    /// 停止制作, and leaving it that way is what makes normal crafting look broken afterwards.
    /// The original label is captured before it is first overwritten, so the restore does not
    /// depend on reproducing the game's own text format.
    /// </summary>
    private static void RestoreConfirmButton(MakeSubPageMake page)
    {
        if (page == null)
            return;

        try
        {
            var text = GetConfirmText(page);
            if (text != null && _confirmLabelByPage.TryGetValue(page, out var label))
            {
                text.text = label;
                _confirmLabelByPage.Remove(page);
            }

            var tip = GetConfirmTip(page);
            if (tip != null)
                tip.enabled = true;

            var button = GetConfirmButton(page);
            if (button != null)
                button.interactable = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make confirm button restore failed: " + ex.Message);
        }
    }

    private static IEnumerator ShowCollectedResultsWhenReady(ViewMake view, ResultCollector collector)
    {
        for (var i = 0; i < 90; i++)
        {
            if (collector.CapturedBatchCount >= collector.ExpectedBatchCount)
                break;

            yield return null;
        }

        ShowCollectedResults(view, collector);
    }

    private static void ShowCollectedResults(ViewMake view, ResultCollector collector)
    {
        if (view == null || collector == null)
            return;

        ResultCollectors.Remove(view);
        if (_activeResultView == view)
            _activeResultView = null;

        if (collector.Items.Count == 0)
            return;

        var items = new List<ItemDisplayData>(collector.Items);
        var closeActions = new List<Action>(collector.CloseActions);
        var argumentBox = FrameWork.EasyPool.Get<FrameWork.ArgumentBox>();
        argumentBox.SetObject("ItemList", items);
        argumentBox.Set("ObtainType", (sbyte)6);
        argumentBox.Set("InWareHouse", collector.InWarehouse);
        if (closeActions.Count > 0)
        {
            argumentBox.SetObject("CloseAction", (Action)(() =>
            {
                foreach (var action in closeActions)
                {
                    try
                    {
                        action?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[BetterTaiwuScroll] Continuous make CloseAction failed: " + ex);
                    }
                }
            }));
        }

        _showingMergedGetItem = true;
        try
        {
            UIElement.GetItem.SetOnInitArgs(argumentBox);
            UIManager.Instance.MaskUI(UIElement.GetItem);
        }
        finally
        {
            _showingMergedGetItem = false;
        }
    }

    private static MakeSubPageMake GetActiveMakePage(ViewMake view)
    {
        if (view == null)
            return null;

        var subPages = Traverse.Create(view).Field("subPages").GetValue<MakeSubPage[]>();
        if (subPages == null)
            return null;

        foreach (var subPage in subPages)
        {
            if (subPage is MakeSubPageMake makePage && makePage.gameObject.activeInHierarchy)
                return makePage;
        }

        return null;
    }

    private static ItemDisplayData GetNextMaterial(MakeSubPageMake page)
    {
        var traverse = Traverse.Create(page);
        var targetSlot = traverse.Field("targetSlot").GetValue<MakeTargetSlot>();
        if (targetSlot == null || !targetSlot.IsValid)
            return null;

        var randomMake = MakeSubPageMakeHelper.CheckIsRandomMake(targetSlot.ItemData);
        var randomMakeSubType = randomMake
            ? targetSlot.ItemData.RealKey.TemplateId
            : (short)-1;

        var allMaterials = traverse.Field("_allMaterialList").GetValue() as IEnumerable;
        if (allMaterials == null)
            return null;

        ItemDisplayData bestMaterial = null;
        foreach (var item in allMaterials)
        {
            if (item is not ItemDisplayData material || !IsMaterialAllowed(page, material, randomMake, randomMakeSubType))
                continue;

            if (bestMaterial == null || IsBetterMaterial(material, bestMaterial))
                bestMaterial = material;
        }

        return bestMaterial;
    }

    private static bool IsMaterialAllowed(MakeSubPageMake page, ItemDisplayData material, bool randomMake, short randomMakeSubType)
    {
        if (material == null || material.Amount <= 0)
            return false;

        var view = MakeSelectMaterialPatch.GetParentView(page);
        if (!IsMaterialSourceAllowed(page, material))
            return false;

        if (!IsMaterialCompatibleWithTarget(page, material, randomMake, randomMakeSubType))
            return false;

        return IsMaterialGradeAllowed(view, material);
    }

    private static bool IsMaterialGradeAllowed(ViewMake view, ItemDisplayData material)
    {
        if (view == null || !TryGetMaterialConfigGrade(material, out var grade))
            return false;

        var settings = ContinuousMakeSettingsStore.GetFor(view);
        var highestRawGrade = DisplayGradeToRaw(settings.HighestMaterialGrade);
        var lowestRawGrade = DisplayGradeToRaw(settings.LowestMaterialGrade);
        return grade <= highestRawGrade && grade >= lowestRawGrade;
    }

    private static bool IsMaterialSourceAllowed(MakeSubPageMake page, ItemDisplayData material)
    {
        var view = MakeSelectMaterialPatch.GetParentView(page);
        return view != null
            && material != null
            && ContinuousMakeSettingsStore.IsSourceAllowed(view.CurLifeSkillType, material.ItemSourceTypeEnum);
    }

    private static bool CanMaterialCookCombinedFoodGroup(ItemDisplayData material)
    {
        return MakeFoodTargetSupport.CanMakeCombinedFood(material);
    }

    private static bool IsMaterialCompatibleWithTarget(MakeSubPageMake page, ItemDisplayData material, bool randomMake, short randomMakeSubType)
    {
        if (page == null || material == null || material.RealKey.ItemType != 5)
            return false;

        if (randomMake)
        {
            // The combined 荤素 entry has no vanilla group of its own, so it uses the Mod's
            // two-group rule; every other random target keeps the game's own comparison.
            return MakeFoodTargetSupport.IsCombinedFoodTarget(randomMakeSubType)
                ? CanMaterialCookCombinedFoodGroup(material)
                : MakeSubPageMakeHelper.CheckCanMakeTargetRandomType(randomMakeSubType, material);
        }

        var targetSlot = Traverse.Create(page).Field("targetSlot").GetValue<MakeTargetSlot>();
        if (targetSlot == null || !targetSlot.IsValid || targetSlot.ItemData == null)
            return false;

        var target = targetSlot.ItemData;
        try
        {
            var makeItemTypeId = (short)GetIntField(Traverse.Create(page), "_makeItemTypeId", -1);
            if (makeItemTypeId < 0)
                return false;

            var makeItemType = Config.MakeItemType.Instance[makeItemTypeId];
            var materialConfig = Config.Material.Instance[material.RealKey.TemplateId];
            if (makeItemType == null || materialConfig == null || !materialConfig.CraftableItemTypes.Contains(makeItemType.TemplateId))
                return false;

            var targetGrade = target.Grade;
            var resultGrade = (sbyte)0;
            var requiredAttainment = (short)0;
            var displayData = Traverse.Create(page).Field("DisplayData").GetValue<GameData.Domains.Building.BuildingMakeDisplayData>();
            var buildingUpgradeMakeItem = displayData != null && displayData.BuildingUpgradeMakeItem;

            if (target.RealKey.ItemType == 7)
            {
                resultGrade = targetGrade;
                requiredAttainment = materialConfig.RequiredAttainment;
            }
            else
            {
                var parentView = MakeSelectMaterialPatch.GetParentView(page);
                if (parentView == null)
                    return false;

                var isManual = GetBoolField(Traverse.Create(page), "_isManual");
                var makeItemSubTypeId = isManual
                    ? (short)GetIntField(Traverse.Create(page), "_makeItemSubTypeId", -1)
                    : (short)-1;
                var maxFinalAttainment = GetIntField(Traverse.Create(page), "_maxFinalAttainment");
                var cookingSkillBookCount = displayData?.AllPagesReadCookingSkillBookCount ?? 0;
                var buildingAttainmentEffect = displayData?.BuildingAttainmentEffect ?? 0;

                requiredAttainment = GameData.Domains.Building.SharedMethods.GetMaterialGradeAndAttainment(
                    material.RealKey.TemplateId,
                    target.RealKey.ItemType,
                    parentView.CurLifeSkillType,
                    maxFinalAttainment,
                    makeItemType.MakeItemSubTypes,
                    out resultGrade,
                    out _,
                    cookingSkillBookCount,
                    makeItemSubTypeId,
                    buildingAttainmentEffect,
                    MakeGameApi.GetIsPerfect(page),
                    isManual,
                    -1);
            }

            var range = GameData.Domains.Building.SharedMethods.GetMakeResultGradeRange(resultGrade, target.RealKey.ItemType);
            var gradeMatchesTarget = targetGrade >= range.Item1 && targetGrade <= range.Item2;
            if (targetGrade == range.Item2 && !buildingUpgradeMakeItem && materialConfig.Transferable)
                gradeMatchesTarget = false;

            var maxFinal = GetIntField(Traverse.Create(page), "_maxFinalAttainment");
            return gradeMatchesTarget && maxFinal >= requiredAttainment;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make target/material compatibility check failed: " + ex.Message);
            return false;
        }
    }

    private static bool IsBetterMaterial(ItemDisplayData candidate, ItemDisplayData current)
    {
        if (!TryGetMaterialConfigGrade(candidate, out var candidateGrade))
            return false;
        if (!TryGetMaterialConfigGrade(current, out var currentGrade))
            return true;

        if (candidateGrade != currentGrade)
            return candidateGrade < currentGrade;

        return candidate.Value > current.Value;
    }

    private static bool TryGetMaterialConfigGrade(ItemDisplayData material, out sbyte grade)
    {
        grade = -1;
        if (material == null || material.RealKey.ItemType != 5)
            return false;

        try
        {
            var config = Config.Material.Instance[material.RealKey.TemplateId];
            if (config == null || config.Grade < 0 || config.Grade > 8)
                return false;

            grade = config.Grade;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int DisplayGradeToRaw(int displayGrade)
    {
        return 9 - Mathf.Clamp(displayGrade, 1, 9);
    }

    private static int RawGradeToDisplay(int rawGrade)
    {
        return 9 - Mathf.Clamp(rawGrade, 0, 8);
    }

    /// <summary>
    /// One line describing the state that decides whether a batch may continue on the
    /// combined food target: the resolved category, the submitted subtype, and how many
    /// listed catalysts can cook their own dish.
    /// </summary>
    private static void LogCombinedFoodGate(MakeSubPageMake page, ItemDisplayData firstMaterial)
    {
        try
        {
            var traverse = Traverse.Create(page);
            var targetSlot = traverse.Field("targetSlot").GetValue<MakeTargetSlot>();
            var target = targetSlot?.ItemData;
            if (!MakeSubPageMakeHelper.CheckIsRandomMake(target)
                || !MakeFoodTargetSupport.IsCombinedFoodTarget(target.RealKey.TemplateId))
                return;

            var randomMakeSubType = (short)GetIntField(traverse, "_currentSelectRandomMakeItemSubType", -1);
            var makeItemTypeId = (short)GetIntField(traverse, "_makeItemTypeId", -1);
            var subType = (short)GetIntField(traverse, "_makeItemSubTypeId", -1);
            var isManual = GetBoolField(traverse, "_isManual");
            var subResult = Config.MakeItemSubType.Instance[subType];

            var list = traverse.Field("_allMaterialList").GetValue<List<ItemDisplayData>>();
            var listed = 0;
            var cookable = 0;
            if (list != null)
            {
                foreach (var entry in list)
                {
                    if (entry == null || entry.Amount <= 0)
                        continue;

                    listed++;
                    if (CanMaterialCookCombinedFoodGroup(entry))
                        cookable++;
                }
            }

            Debug.Log("[BetterTaiwuScroll] Combined food gate: target=" + randomMakeSubType
                + " makeItemType=" + makeItemTypeId
                + " subType=" + subType
                + " subResult=" + (subResult == null ? "missing" : subResult.Result.ItemType + "/" + subResult.Result.TemplateId)
                + " manual=" + isManual
                + " firstMaterial=" + (firstMaterial == null ? "-" : firstMaterial.RealKey.TemplateId.ToString())
                + " listedWithAmount=" + listed
                + " cookable=" + cookable + ".");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Combined food gate log failed: " + ex.Message);
        }
    }

    private static void LogNoEligibleMaterial(ViewMake view, string stage)
    {
        if (view == null)
            return;

        var settings = ContinuousMakeSettingsStore.GetFor(view);
        var page = GetActiveMakePage(view);
        // Count what the list still advertises: if rows remain but none qualifies, the
        // panel is holding amounts the backend already consumed.
        var listed = 0;
        var listedWithAmount = 0;
        if (page != null)
        {
            var list = Traverse.Create(page).Field("_materialList").GetValue<List<ItemDisplayData>>();
            if (list != null)
            {
                listed = list.Count;
                foreach (var entry in list)
                {
                    if (entry != null && entry.Amount > 0)
                        listedWithAmount++;
                }
            }
        }

        Debug.Log("[BetterTaiwuScroll] Batch make stopped at " + stage
            + ": no eligible material for life skill " + view.CurLifeSkillType
            + " in configured grade range " + settings.HighestMaterialGrade
            + "-" + settings.LowestMaterialGrade
            + "; listed materials=" + listed + ", listed with amount>0=" + listedWithAmount + ".");
        LogGradeRangeMismatch(view, settings);
        LogMaterialRejectionReasons(view);
    }

    /// <summary>
    /// Distinguishes "the list is stale" from "your own grade filter excludes everything
    /// you own". When the best material on the page is below the configured range, the
    /// batch legitimately stops while the list keeps showing usable-looking rows, which
    /// reads as "the button does nothing".
    /// </summary>
    private static void LogGradeRangeMismatch(ViewMake view, ContinuousMakeSettings settings)
    {
        try
        {
            var page = GetActiveMakePage(view);
            if (page == null)
                return;

            var list = Traverse.Create(page).Field("_allMaterialList").GetValue<List<ItemDisplayData>>();
            if (list == null)
                return;

            var highestRaw = DisplayGradeToRaw(settings.HighestMaterialGrade);
            var lowestRaw = DisplayGradeToRaw(settings.LowestMaterialGrade);
            var bestRaw = -1;
            var bestTemplate = -1;
            var inRange = 0;
            var withAmount = 0;

            foreach (var entry in list)
            {
                if (entry == null || entry.Amount <= 0)
                    continue;

                withAmount++;
                if (!TryGetMaterialConfigGrade(entry, out var rawGrade))
                    continue;

                if (rawGrade > bestRaw)
                {
                    bestRaw = rawGrade;
                    bestTemplate = entry.RealKey.TemplateId;
                }

                if (rawGrade <= highestRaw && rawGrade >= lowestRaw)
                    inRange++;
            }

            if (withAmount == 0 || inRange > 0 || bestRaw < 0)
                return;

            Debug.Log("[BetterTaiwuScroll] Grade filter excludes all stocked catalysts: the best catalyst on this page is "
                + "template " + bestTemplate + " at display grade " + RawGradeToDisplay(bestRaw)
                + " (raw=" + bestRaw + "), but the settings for life skill " + view.CurLifeSkillType
                + " allow only display grade " + settings.LowestMaterialGrade + "-" + settings.HighestMaterialGrade
                + " (raw " + lowestRaw + "-" + highestRaw + "). Widen the batch make grade range to use these "
                + "materials; " + withAmount + " listed stacks have stock, " + inRange + " are inside the range.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make grade range diagnosis failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Dumps why each listed catalyst was rejected. A row that is visible but never
    /// eligible is the signature of a filter that no longer matches the game data.
    /// </summary>
    private static void LogMaterialRejectionReasons(ViewMake view)
    {
        var page = GetActiveMakePage(view);
        if (page == null)
            return;

        var traverse = Traverse.Create(page);
        var targetSlot = traverse.Field("targetSlot").GetValue<MakeTargetSlot>();
        var randomMake = MakeSubPageMakeHelper.CheckIsRandomMake(targetSlot?.ItemData);
        var randomMakeSubType = randomMake
            ? (short)GetIntField(traverse, "_currentSelectRandomMakeItemSubType", -1)
            : (short)-1;
        var settings = ContinuousMakeSettingsStore.GetFor(view);
        var highestRaw = DisplayGradeToRaw(settings.HighestMaterialGrade);
        var lowestRaw = DisplayGradeToRaw(settings.LowestMaterialGrade);

        var list = traverse.Field("_allMaterialList").GetValue<List<ItemDisplayData>>();
        if (list == null)
            return;

        var index = 0;
        foreach (var material in list)
        {
            if (material == null)
                continue;

            index++;
            if (index > 12)
                break;
            var hasGrade = TryGetMaterialConfigGrade(material, out var rawGrade);
            var sourceAllowed = IsMaterialSourceAllowed(page, material);
            var compatible = IsMaterialCompatibleWithTarget(page, material, randomMake, randomMakeSubType);
            Debug.Log("[BetterTaiwuScroll]   material[" + index + "] template=" + material.RealKey.TemplateId
                + " amount=" + material.Amount
                + " grade=" + (hasGrade ? RawGradeToDisplay(rawGrade).ToString() : "n/a")
                + "(raw=" + (hasGrade ? rawGrade.ToString() : "n/a") + ")"
                + " source=" + material.ItemSourceTypeEnum
                + " sourceAllowed=" + sourceAllowed
                + " compatible=" + compatible
                + " allowedRawRange=" + lowestRaw + "-" + highestRaw + ".");
        }
    }

    private static void SelectMaterial(MakeSubPageMake page, ItemDisplayData material)
    {
        var nativeOptions = MakePageNativeOptionSnapshot.Capture(page);
        IsSelectingMaterialForContinuation = true;
        try
        {
            AccessTools.Method(typeof(MakeSubPageMake), "SelectMaterial", new[] { typeof(ItemDisplayData), typeof(bool) })
                ?.Invoke(page, new object[] { material, false });
        }
        finally
        {
            IsSelectingMaterialForContinuation = false;
            nativeOptions.Restore(page);

            // Request the current game's asynchronous result data only after
            // restoring the saved options, so random targets populate
            // _materialMakeResultDict with the final material/tool state.
            try
            {
                AccessTools.Method(typeof(MakeSubPageMake), "CheckCondition")?.Invoke(page, new object[] { true });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterTaiwuScroll] Continuous make result refresh request failed: " + ex.Message);
            }
        }
    }

    private static void ApplyLocalConsumption(MakeSubPageMake page, ItemDisplayData material, ItemDisplayData tool, int makeCount)
    {
        var materialDepleted = false;
        if (material != null)
        {
            material.Amount = Math.Max(0, material.Amount - makeCount);
            // The slots and the two material lists can each hold a different
            // ItemDisplayData instance for the same stack; sync them so the visible list
            // cannot keep advertising an amount that was already consumed.
            SyncConsumedMaterialAmount(page, material, material.Amount);
            materialDepleted = material.Amount <= 0;
        }

        if (tool == null || material == null || ViewMake.IsEmptyTool(tool))
        {
            if (materialDepleted)
                CleanupDepletedMaterialAfterLocalConsumption(page);
            return;
        }

        var cost = ViewMake.GetToolDurabilityCost(tool, material.Grade);
        if (cost <= 0)
        {
            if (materialDepleted)
                CleanupDepletedMaterialAfterLocalConsumption(page);
            return;
        }

        tool.Durability = (short)Mathf.Max(0, tool.Durability - cost * makeCount);

        if (materialDepleted)
            CleanupDepletedMaterialAfterLocalConsumption(page);
    }

    private static void AddTutorialCloseAction(MakeSubPageMake page, ResultCollector collector, List<ItemDisplayData> itemDataList)
    {
        if (page == null || collector == null || itemDataList == null || itemDataList.Count == 0)
            return;

        var tutorialMethod = AccessTools.Method(typeof(MakeSubPageMake), "Tutorial");
        if (tutorialMethod == null)
            return;

        var items = new List<ItemDisplayData>(itemDataList);
        collector.CloseActions.Add(() =>
        {
            if (page == null)
                return;

            try
            {
                tutorialMethod.Invoke(page, new object[] { items });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterTaiwuScroll] Continuous make tutorial action failed: " + ex.Message);
            }
        });
    }

    private static void SaveLastMakeResourceCounts(MakeSubPageMake page, GameData.Domains.Character.ResourceInts resourceCount)
    {
        try
        {
            var traverse = Traverse.Create(page);
            var last = traverse.Field("_lastMakeResourceCountInts").GetValue<GameData.Domains.Character.ResourceInts>();
            last.Initialize();
            last.Add(ref resourceCount);
            traverse.Field("_lastMakeResourceCountInts").SetValue(last);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make resource count memory failed: " + ex.Message);
        }
    }

    private static void ClearDisplayData(MakeSubPageMake page)
    {
        try
        {
            Traverse.Create(page).Field("DisplayData").GetValue<BuildingMakeDisplayData>()?.Clear();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make display data clear failed: " + ex.Message);
        }
    }

    private static bool CancelExpiredMaterialSelection(MakeSubPageMake page)
    {
        try
        {
            var materialSlot = Traverse.Create(page).Field("materialSlot").GetValue<MakeTargetSlot>();
            var selectedMaterial = materialSlot?.ItemData;
            if (materialSlot != null
                && materialSlot.IsValid
                && selectedMaterial != null
                && (selectedMaterial.Amount <= 0 || !HasAvailableSelectedMaterial(page, selectedMaterial)))
            {
                materialSlot.Cancel();
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make material selection cleanup failed: " + ex.Message);
        }

        return false;
    }

    private static bool HasAvailableSelectedMaterial(MakeSubPageMake page, ItemDisplayData selectedMaterial)
    {
        // Batches can use other enabled sources while the visible list shows one source.
        var materialList = Traverse.Create(page).Field("_allMaterialList").GetValue() as IEnumerable;
        if (materialList == null)
            return false;

        foreach (var item in materialList)
        {
            if (item is ItemDisplayData material
                && material.RealKey == selectedMaterial.RealKey
                && material.ItemSourceTypeEnum == selectedMaterial.ItemSourceTypeEnum
                && material.Amount > 0)
                return true;
        }

        return false;
    }

    private static bool RemoveZeroAmountMaterials(MakeSubPageMake page, string fieldName, bool updateScroll)
    {
        try
        {
            var list = Traverse.Create(page).Field(fieldName).GetValue<List<ItemDisplayData>>();
            if (list == null)
                return false;

            var removed = list.RemoveAll(item => item == null || item.Amount <= 0);
            if (removed <= 0)
                return false;

            if (updateScroll)
            {
                ForceMaterialListRerender(page);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make zero amount material filter failed: " + ex.Message);
            return false;
        }
    }

    private static bool IsZeroAmountContent(object content)
    {
        if (content == null)
            return true;

        if (content is ItemDisplayData item)
            return item.Amount <= 0;

        try
        {
            var property = AccessTools.Property(content.GetType(), "Amount");
            if (property == null)
                return false;

            var value = property.GetValue(content, null);
            return value != null && Convert.ToInt32(value) <= 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ForceMaterialListRerender(MakeSubPageMake page)
    {
        try
        {
            var materialListScroll = Traverse.Create(page).Field("materialListScroll").GetValue();
            if (materialListScroll == null)
                return;

            var refreshList = AccessTools.Method(materialListScroll.GetType(), "RefreshList");
            if (refreshList != null)
            {
                refreshList.Invoke(materialListScroll, Array.Empty<object>());
                return;
            }

            AccessTools.Method(materialListScroll.GetType(), "ReRender")?.Invoke(materialListScroll, Array.Empty<object>());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make material list rerender failed: " + ex.Message);
        }
    }

    private static void CleanupDepletedMaterialAfterLocalConsumption(MakeSubPageMake page)
    {
        if (page == null)
            return;

        RemoveZeroAmountMaterials(page, "_allMaterialList", false);
        if (RemoveZeroAmountMaterials(page, "_materialList", true))
        {
            CancelExpiredMaterialSelection(page);
            RefreshMakeCondition(page);
        }
    }

    private static void RefreshMakeCondition(MakeSubPageMake page)
    {
        try
        {
            AccessTools.Method(typeof(MakeSubPageMake), "CheckCondition")?.Invoke(page, new object[] { false });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make condition refresh failed: " + ex.Message);
        }
    }

    private static bool IsToolSelectionReady(MakeSubPageMake page, ContinuousMakeSettings settings)
    {
        if (page == null)
            return false;

        var toolSlot = Traverse.Create(page).Field("toolSlot").GetValue<MakeTargetSlot>();
        if (toolSlot == null || !toolSlot.IsValid)
            return false;

        return settings == null || settings.AllowBareHand || !ViewMake.IsEmptyTool(toolSlot.ItemData);
    }

    private static IEnumerator RefreshCurrentRandomMakeResult(MakeSubPageMake page, Action<bool, MakeResult> setResult)
    {
        var current = MakeExecutionLifetime.Capture(page);
        setResult(false, default);

        var traverse = Traverse.Create(page);
        var targetSlot = traverse.Field("targetSlot").GetValue<MakeTargetSlot>();
        var materialSlot = traverse.Field("materialSlot").GetValue<MakeTargetSlot>();
        if (targetSlot == null || !targetSlot.IsValid || !MakeSubPageMakeHelper.CheckIsRandomMake(targetSlot.ItemData))
        {
            setResult(targetSlot != null && targetSlot.IsValid
                && materialSlot != null && materialSlot.IsValid, default);
            yield break;
        }

        var view = MakeSelectMaterialPatch.GetParentView(page);
        var toolSlot = traverse.Field("toolSlot").GetValue<MakeTargetSlot>();
        var subTypes = traverse.Field("_makeItemSubtypeIdList").GetValue<List<short>>();
        if (view == null || materialSlot == null || !materialSlot.IsValid
            || toolSlot == null || !toolSlot.IsValid || subTypes == null || subTypes.Count == 0)
            yield break;

        var material = materialSlot.ItemData;
        var toolKey = toolSlot.ItemData.Key;
        var targetKey = targetSlot.ItemData.RealKey;
        var isManual = GetBoolField(traverse, "_isManual") && subTypes.Count > 1;
        var subType = (short)(isManual ? GetIntField(traverse, "_makeItemSubTypeId", -1) : -1);
        var isPerfect = traverse.Property("IsPerfect").GetValue<bool>();
        var recipe = new MakeRecipeSnapshot(page);
        var itemType = Config.MakeItemSubType.Instance[subTypes[0]].Result.ItemType;
        var done = false;
        var result = default(MakeResult);
        // Fetch only the selected recipe. CurMakeResult can still contain the
        // preceding round while native preview requests are in flight.
        try
        {
            BuildingDomainMethod.AsyncCall.GetMakeResult(view, material.RealKey.TemplateId,
                toolKey, view.BuildingBlockKey, view.CurLifeSkillType, new List<short>(subTypes),
                subType, isPerfect, isManual, (offset, pool) =>
                {
                    if (!current()) return;
                    try
                    {
                        Serializer.Deserialize(pool, offset, ref result);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[BetterTaiwuScroll] Continuous make result deserialize failed: " + ex.Message);
                    }
                    finally
                    {
                        done = true;
                    }
                });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Continuous make result request failed: " + ex.Message);
            yield break;
        }

        for (var i = 0; i < 120; i++)
        {
            if (!ShouldContinue(page) || !recipe.IsCurrent() || !IsSelectedMaterial(page, material)
                || !toolSlot.IsValid || !toolSlot.ItemData.Key.Equals(toolKey)
                || !targetSlot.IsValid || !targetSlot.ItemData.RealKey.Equals(targetKey))
                yield break;

            if (done)
            {
                setResult(MakeResultValidation.IsUsable(result, itemType, subTypes, subType), result);
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning("[BetterTaiwuScroll] Continuous make stopped because native random make result timed out.");
    }

    private static bool IsSelectedMaterial(MakeSubPageMake page, ItemDisplayData material)
    {
        if (page == null || material == null)
            return false;

        var materialSlot = Traverse.Create(page).Field("materialSlot").GetValue<MakeTargetSlot>();
        var selected = materialSlot?.ItemData;
        return materialSlot != null
            && materialSlot.IsValid
            && selected != null
            && selected.Amount > 0
            && selected.RealKey.Equals(material.RealKey)
            && selected.ItemSourceTypeEnum == material.ItemSourceTypeEnum;
    }

    private static bool IsConfirmInteractable(MakeSubPageMake page)
    {
        var button = GetConfirmButton(page);
        return button != null && button.interactable;
    }

    private static CButton GetConfirmButton(MakeSubPageMake page)
    {
        return page != null ? Traverse.Create(page).Field("buttonConfirm").GetValue<CButton>() : null;
    }

    private static TextMeshProUGUI GetConfirmText(MakeSubPageMake page)
    {
        return page != null ? Traverse.Create(page).Field("textConfirm").GetValue<TextMeshProUGUI>() : null;
    }

    private static TooltipInvoker GetConfirmTip(MakeSubPageMake page)
    {
        return page != null ? Traverse.Create(page).Field("tipConfirm").GetValue<TooltipInvoker>() : null;
    }

    private static bool IsSelectedToolEmpty(MakeSubPageMake page)
    {
        var toolSlot = Traverse.Create(page).Field("toolSlot").GetValue<MakeTargetSlot>();
        return toolSlot != null && toolSlot.IsValid && ViewMake.IsEmptyTool(toolSlot.ItemData);
    }

    private static bool ApplyDurabilityProtectionMakeCount(MakeSubPageMake page)
    {
        var safeCount = GetSafeMakeCountByDurability(page);
        if (safeCount < 0)
            return true;
        if (safeCount <= 0)
            return false;

        var makeCount = Math.Max(1, GetIntField(Traverse.Create(page), "_makeCount", 1));
        if (safeCount < makeCount)
            MakeSelectMaterialPatch.SetMakeCount(page, safeCount);

        return true;
    }

    private static int GetSafeMakeCountByDurability(MakeSubPageMake page)
    {
        var toolSlot = Traverse.Create(page).Field("toolSlot").GetValue<MakeTargetSlot>();
        var materialSlot = Traverse.Create(page).Field("materialSlot").GetValue<MakeTargetSlot>();
        if (toolSlot == null || materialSlot == null || !toolSlot.IsValid || !materialSlot.IsValid)
            return -1;

        var tool = toolSlot.ItemData;
        if (tool == null || ViewMake.IsEmptyTool(tool))
            return -1;

        var cost = GetIntField(Traverse.Create(page), "_makeToolDurabilityCost");
        if (cost <= 0)
            cost = ViewMake.GetToolDurabilityCost(tool, materialSlot.ItemData.Grade);
        if (cost <= 0)
            return -1;

        return Math.Max(0, (tool.Durability - 1) / cost);
    }

    private static bool WouldSelectedToolBreak(MakeSubPageMake page)
    {
        var toolSlot = Traverse.Create(page).Field("toolSlot").GetValue<MakeTargetSlot>();
        var materialSlot = Traverse.Create(page).Field("materialSlot").GetValue<MakeTargetSlot>();
        if (toolSlot == null || materialSlot == null || !toolSlot.IsValid || !materialSlot.IsValid)
            return false;

        var tool = toolSlot.ItemData;
        if (tool == null || ViewMake.IsEmptyTool(tool))
            return false;

        var cost = GetIntField(Traverse.Create(page), "_makeToolDurabilityCost");
        if (cost <= 0)
            cost = ViewMake.GetToolDurabilityCost(tool, materialSlot.ItemData.Grade);
        if (cost <= 0)
            return false;

        var makeCount = Math.Max(1, GetIntField(Traverse.Create(page), "_makeCount", 1));
        return tool.Durability <= cost * makeCount;
    }

    private static int GetIntField(Traverse traverse, string fieldName, int fallback = 0)
    {
        var raw = traverse.Field(fieldName).GetValue();
        if (raw == null)
            return fallback;

        try
        {
            return Convert.ToInt32(raw);
        }
        catch
        {
            return fallback;
        }
    }

    private static bool GetBoolField(Traverse traverse, string fieldName)
    {
        var raw = traverse.Field(fieldName).GetValue();
        if (raw == null)
            return false;

        try
        {
            return Convert.ToBoolean(raw);
        }
        catch
        {
            return false;
        }
    }

    private sealed class ResultCollector
    {
        internal readonly List<ItemDisplayData> Items = new();
        internal readonly List<Action> CloseActions = new();
        internal int ExpectedBatchCount;
        internal int CapturedBatchCount;
        internal int LastCaptureFrame;
        internal bool InWarehouse;
        internal bool Finishing;

        internal void AddItems(IEnumerable<ItemDisplayData> items)
        {
            foreach (var item in items)
            {
                if (item == null)
                    continue;

                var existing = Items.Find(data => data != null && data.RealKey.Equals(item.RealKey));
                if (existing != null)
                {
                    existing.Amount += item.Amount;
                    continue;
                }

                Items.Add(item.Clone());
            }
        }
    }

    private readonly struct DirectMakeArguments
    {
        internal readonly MakeConditionArguments Condition;
        internal readonly StartMakeArguments Start;
        internal readonly GameData.Domains.Character.ResourceInts ResourceCount;
        internal readonly int MakeCount;
        internal readonly bool InWarehouse;

        internal DirectMakeArguments(
            MakeConditionArguments condition,
            StartMakeArguments start,
            GameData.Domains.Character.ResourceInts resourceCount,
            int makeCount,
            bool inWarehouse)
        {
            Condition = condition;
            Start = start;
            ResourceCount = resourceCount;
            MakeCount = makeCount;
            InWarehouse = inWarehouse;
        }
    }

    private readonly struct DirectMakeResult
    {
        internal static readonly DirectMakeResult Fail = new DirectMakeResult(false, 0, null);

        internal readonly bool Success;
        internal readonly int MakeCount;
        internal readonly ItemDisplayData Tool;

        internal DirectMakeResult(bool success, int makeCount, ItemDisplayData tool)
        {
            Success = success;
            MakeCount = makeCount;
            Tool = tool;
        }
    }

    private enum ContinuationStage
    {
        AfterMaterialSelect,
        AfterToolSelect
    }

    internal sealed class MakePageNativeOptionSnapshot
    {
        private bool _valid;
        private bool _isPerfect;
        private int _perfectDropdownValue;
        private GameData.Domains.Character.ResourceInts _currentResources;

        internal static MakePageNativeOptionSnapshot Capture(MakeSubPageMake page)
        {
            var snapshot = new MakePageNativeOptionSnapshot();
            if (page == null)
                return snapshot;

            try
            {
                var traverse = Traverse.Create(page);
                var targetSlot = traverse.Field("targetSlot").GetValue<MakeTargetSlot>();
                var perfectDropdown = traverse.Field("perfectDropdown").GetValue<CDropdown>();

                snapshot._isPerfect = targetSlot != null && targetSlot.IsToggleOn;
                snapshot._perfectDropdownValue = perfectDropdown != null ? perfectDropdown.value : 0;
                snapshot._currentResources = traverse.Field("_curMakeResourceCountInts").GetValue<GameData.Domains.Character.ResourceInts>();
                snapshot._valid = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterTaiwuScroll] Continuous make native option capture failed: " + ex.Message);
            }

            return snapshot;
        }

        internal void Restore(MakeSubPageMake page)
        {
            if (!_valid || page == null)
                return;

            try
            {
                var traverse = Traverse.Create(page);
                if (TryNormalizeResources(traverse, _currentResources, out var resources))
                {
                    traverse.Field("_curMakeResourceCountInts").SetValue(resources);
                    traverse.Field("_lastMakeResourceCountInts").SetValue(resources);
                }

                var targetSlot = traverse.Field("targetSlot").GetValue<MakeTargetSlot>();
                if (targetSlot != null)
                {
                    targetSlot.IsToggleOn = _isPerfect;
                    if (targetSlot.IsValid)
                        targetSlot.Refresh();
                }

                RestorePerfectDropdown(page, traverse);
                AccessTools.Method(typeof(MakeSubPageMake), "CheckCondition")?.Invoke(page, new object[] { false });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterTaiwuScroll] Continuous make native option restore failed: " + ex.Message);
            }
        }

        private static bool TryNormalizeResources(Traverse traverse, GameData.Domains.Character.ResourceInts source, out GameData.Domains.Character.ResourceInts result)
        {
            result = default;
            var maxResources = traverse.Field("_maxMakeResourceCountInts").GetValue<GameData.Domains.Character.ResourceInts>();
            var maxTotal = GetIntField(traverse, "_maxMakeResourceTotalCount");
            if (maxTotal <= 0)
                return false;

            var sum = 0;
            for (var i = 0; i < 6; i++)
            {
                var value = Mathf.Clamp(source.Get(i), 0, maxResources.Get(i));
                result.Set(i, value);
                sum += value;
            }

            if (sum != maxTotal)
            {
                var mainResourceType = (sbyte)GetIntField(traverse, "_mainRequiredResourceType", -1);
                if (mainResourceType < 0 || mainResourceType >= 6)
                    return false;

                var currentMainValue = result.Get(mainResourceType);
                var adjustedMainValue = Mathf.Clamp(currentMainValue + maxTotal - sum, 0, maxResources.Get(mainResourceType));
                result.Set(mainResourceType, adjustedMainValue);
                sum += adjustedMainValue - currentMainValue;
            }

            return sum == maxTotal;
        }

        private void RestorePerfectDropdown(MakeSubPageMake page, Traverse traverse)
        {
            var perfectDropdown = traverse.Field("perfectDropdown").GetValue<CDropdown>();
            if (perfectDropdown == null || !perfectDropdown.gameObject.activeSelf || perfectDropdown.options == null || perfectDropdown.options.Count == 0)
                return;

            var value = Mathf.Clamp(_perfectDropdownValue, 0, perfectDropdown.options.Count - 1);
            perfectDropdown.SetValueWithoutNotify(value);
            AccessTools.Method(typeof(MakeSubPageMake), "OnPerfectDropdownValueChanged")?.Invoke(page, new object[] { value });
        }
    }
}
