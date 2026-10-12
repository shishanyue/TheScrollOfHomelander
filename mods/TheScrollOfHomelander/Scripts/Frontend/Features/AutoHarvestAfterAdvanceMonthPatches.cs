#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Config;
using FrameWork;
using FrameWork.UISystem.UIElements;
using Game.Views.Building;
using Game.Views.Building.BuildingAreaQuickActionMenu;
using Game.Views.Obtain;
using GameData.Domains.Building;
using GameData.Domains.Extra;
using GameData.Domains.Item.Display;
using GameData.GameDataBridge;
using GameData.Domains.World;
using GameData.Serializer;
using GameData.Utilities;
using HarmonyLib;
using UnityEngine;

namespace BetterTaiwuScroll.Frontend;

[HarmonyPatch(typeof(WorldDomainMethod.Call), "AdvanceMonth_DisplayedMonthlyNotifications")]
internal static class AutoHarvestAfterAdvanceMonthMarkerPatch
{
    private static void Postfix(bool saveWorld)
    {
        AutoHarvestAfterAdvanceMonthController.MarkPending(saveWorld);
    }
}

[HarmonyPatch(typeof(GlobalOperations), "OnWorldDataReady", new Type[] { })]
internal static class AutoHarvestAfterWorldLoadPatch
{
    private static void Postfix()
    {
        AutoHarvestAfterAdvanceMonthController.MarkPendingAfterWorldLoad();
    }
}

internal static class AutoHarvestAfterAdvanceMonthController
{
    private const int MaxWaitChecks = 240;

    private static bool _initialized;
    private static bool _pending;
    private static bool _scheduled;
    private static bool _running;
    private static int _waitChecks;
    private static int _run;

    internal static void Reset()
    {
        _run++;
        _pending = _scheduled = _running = false;
        _waitChecks = 0;
    }

    internal static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        GEvent.Add(EEvents.OnSavingWorldStateChange, OnWorldStateChanged);
        GEvent.Add(EEvents.OnAdvancingMonthStateChange, OnWorldStateChanged);
        GEvent.Add(EEvents.OnGameStateChange, OnWorldStateChanged);
        GEvent.Add(UiEvents.MonthNotifyProcessComplete, OnWorldStateChanged);
    }

    internal static void Dispose()
    {
        Reset();
        if (_initialized)
        {
            GEvent.Remove(EEvents.OnSavingWorldStateChange, OnWorldStateChanged);
            GEvent.Remove(EEvents.OnAdvancingMonthStateChange, OnWorldStateChanged);
            GEvent.Remove(EEvents.OnGameStateChange, OnWorldStateChanged);
            GEvent.Remove(UiEvents.MonthNotifyProcessComplete, OnWorldStateChanged);
        }

        _initialized = false;
        _pending = false;
        _scheduled = false;
        _running = false;
        _waitChecks = 0;
    }

    internal static void MarkPending(bool saveWorld)
    {
        // saveWorld controls autosaving only. The native method completes the
        // month even when saving is skipped, disabled, or unavailable.
        if (!Enabled || _pending || _running)
            return;

        _pending = true;
        _waitChecks = 0;
        ScheduleCheck(2u);
    }

    internal static void MarkPendingAfterWorldLoad()
    {
        if (!Enabled)
            return;

        // YieldHelper and AsyncMethodDispatcher are recreated while changing worlds.
        // Discard scheduling/running state from the previous world before queuing the
        // one-shot load harvest for the newly initialized archive.
        _run++;
        _pending = true;
        _scheduled = false;
        _running = false;
        _waitChecks = 0;
        ScheduleCheck(5u);
    }

    private static bool Enabled => Plugin.EnableAutoHarvestAfterAdvanceMonth;

    private static void OnWorldStateChanged(ArgumentBox _)
    {
        ScheduleCheck(1u);
    }

    private static void ScheduleCheck(uint delayFrames)
    {
        if (!_pending || _scheduled || _running)
            return;

        _scheduled = true;
        var session = ModSession.Generation;
        var run = _run;
        SingletonObject.getInstance<YieldHelper>().DelayFrameDo(delayFrames, delegate
        {
            if (!ModSession.IsCurrent(session) || run != _run) return;
            _scheduled = false;
            TryRunWhenReady();
        });
    }

    private static void TryRunWhenReady()
    {
        if (!_pending || _running)
            return;

        if (!Enabled)
        {
            _pending = false;
            return;
        }

        var basicGameData = SingletonObject.getInstance<BasicGameData>();
        var gameApp = GameApp.Instance;
        if (gameApp == null || gameApp.GetCurrentGameStateName() != EGameState.InGame)
        {
            // Loading can legitimately take longer than the normal month-transition
            // wait budget. The OnGameStateChange listener will schedule the next check
            // as soon as the archive actually enters the in-game state.
            return;
        }

        if (basicGameData == null
            || basicGameData.SavingWorld
            || basicGameData.AdvancingMonthState != 0)
        {
            if (++_waitChecks <= MaxWaitChecks)
                ScheduleCheck(5u);
            // A long save can outlast the frame polling budget. Keep this run
            // pending; the subscribed world-state events will wake it when ready.
            return;
        }

        _pending = false;
        _running = true;
        RequestAvailability();
    }

    private static void RequestAvailability()
    {
        var state = new HarvestAvailability();
        state.Run = ++_run;
        state.Session = ModSession.Generation;
        ModSession.Delay(30, () =>
        {
            if (IsCurrent(state)) FailRun(state, new TimeoutException("Harvest request timed out; no automatic retry."));
        });
        state.PendingRequests = CountEnabledHarvestTypes();
        if (state.PendingRequests == 0)
        {
            FinishRun();
            return;
        }

        try
        {
            if (Plugin.EnableAutoHarvestAfterAdvanceMonthSoldItems)
            {
                BuildingDomainMethod.AsyncCall.QuickCollectShopSoldItemCount(null, delegate(int offset, RawDataPool dataPool)
                {
                    HandleAvailabilityCallback(state, delegate
                    {
                        Serializer.Deserialize(dataPool, offset, ref state.SoldItemCount);
                    });
                });
            }

            if (Plugin.EnableAutoHarvestAfterAdvanceMonthItems)
            {
                BuildingDomainMethod.AsyncCall.QuickCollectShopItemCount(null, delegate(int offset, RawDataPool dataPool)
                {
                    HandleAvailabilityCallback(state, delegate
                    {
                        Serializer.Deserialize(dataPool, offset, ref state.ShopItemCount);
                    });
                });
            }

            if (Plugin.EnableAutoHarvestAfterAdvanceMonthPeople)
            {
                BuildingDomainMethod.AsyncCall.QuickRecruitPeopleCount(null, delegate(int offset, RawDataPool dataPool)
                {
                    HandleAvailabilityCallback(state, delegate
                    {
                        Serializer.Deserialize(dataPool, offset, ref state.PeopleCount);
                    });
                });
            }

            if (Plugin.EnableAutoHarvestAfterAdvanceMonthFeathers)
            {
                BuildingDomainMethod.AsyncCall.IsAnyChickensCanPluck(null, delegate(int offset, RawDataPool dataPool)
                {
                    HandleAvailabilityCallback(state, delegate
                    {
                        Serializer.Deserialize(dataPool, offset, ref state.HasFeathers);
                        state.HasFeathers = state.HasFeathers && CanPluckChickenFeathers();
                    });
                });
            }
        }
        catch (Exception ex)
        {
            FailRun(state, ex);
        }
    }

    private static int CountEnabledHarvestTypes()
    {
        var count = 0;
        if (Plugin.EnableAutoHarvestAfterAdvanceMonthSoldItems)
            count++;
        if (Plugin.EnableAutoHarvestAfterAdvanceMonthItems)
            count++;
        if (Plugin.EnableAutoHarvestAfterAdvanceMonthFeathers)
            count++;
        if (Plugin.EnableAutoHarvestAfterAdvanceMonthPeople)
            count++;
        return count;
    }

    private static void HandleAvailabilityCallback(HarvestAvailability state, Action action)
    {
        if (!IsCurrent(state))
            return;

        try
        {
            action();
            OnAvailabilityRequestDone(state);
        }
        catch (Exception ex)
        {
            FailRun(state, ex);
        }
    }

    private static void OnAvailabilityRequestDone(HarvestAvailability state)
    {
        if (!IsCurrent(state))
            return;

        if (--state.PendingRequests > 0)
            return;

        try
        {
            RunCollectAll(state);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Auto harvest after advance month failed: " + ex);
            FinishRun();
        }
    }

    private static void FailRun(HarvestAvailability state, Exception ex)
    {
        state.Cancelled = true;
        Debug.LogWarning("[BetterTaiwuScroll] Auto harvest after advance month failed: " + ex);
        FinishRun();
    }

    private static bool CanPluckChickenFeathers()
    {
        try
        {
            return SingletonObject.getInstance<WorldMapModel>().IsAtTaiwuVillage(-1, -1);
        }
        catch
        {
            return false;
        }
    }

    private static void RunCollectAll(HarvestAvailability state)
    {
        if (!IsCurrent(state)) return;
        if (state.SoldItemCount > 0)
            BuildingDomainMethod.Call.QuickCollectShopSoldItem();

        var pendingItemRequests = 0;
        var collectFeathers = state.HasFeathers;
        var collectItems = state.ShopItemCount > 0;
        var collectPeople = state.PeopleCount > 0;
        var allItems = new List<ItemDisplayData>();

        if (collectFeathers)
            pendingItemRequests++;
        if (collectItems)
            pendingItemRequests++;

        if (pendingItemRequests == 0)
        {
            if (collectPeople)
                HandleRecruitPeopleAfterAutoHarvest();

            RefreshQuickButtons();
            FinishRun();
            return;
        }

        if (collectFeathers)
        {
            BuildingDomainMethod.AsyncCall.PluckAllChickenFeathers(null, delegate(int offset, RawDataPool dataPool)
            {
                if (!IsCurrent(state)) return;
                try
                {
                    var items = new List<ItemDisplayData>();
                    Serializer.Deserialize(dataPool, offset, ref items);
                    allItems.AddRange(items);
                    if (--pendingItemRequests == 0)
                        ShowGetItem(allItems, collectItems, collectPeople);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BetterTaiwuScroll] Auto harvest after advance month failed while collecting feathers: " + ex);
                    FinishRun();
                }
            });
        }

        if (collectItems)
        {
            BuildingDomainMethod.AsyncCall.QuickCollectShopItem(null, delegate(int offset, RawDataPool dataPool)
            {
                if (!IsCurrent(state)) return;
                try
                {
                    var items = new List<ItemDisplayData>();
                    Serializer.Deserialize(dataPool, offset, ref items);
                    allItems.AddRange(items);
                    if (--pendingItemRequests == 0)
                        ShowGetItem(allItems, collectItems, collectPeople);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BetterTaiwuScroll] Auto harvest after advance month failed while collecting items: " + ex);
                    FinishRun();
                }
            });
        }
    }

    private static void ShowGetItem(List<ItemDisplayData> items, bool inWareHouse, bool collectPeople)
    {
        try
        {
            var argumentBox = EasyPool.Get<ArgumentBox>();
            argumentBox.SetObject("ItemList", items);
            if (inWareHouse)
                argumentBox.Set("InWareHouse", arg: true);
            if (collectPeople)
                argumentBox.SetObject("CloseAction", new Action(HandleRecruitPeopleAfterAutoHarvest));

            UIElement.GetItem.SetOnInitArgs(argumentBox);
            UIManager.Instance.MaskUI(UIElement.GetItem);
            if (Plugin.EnableAutoHarvestCloseObtainView)
                ScheduleAutoCloseGetItem(items);
            RefreshQuickButtons();
        }
        finally
        {
            FinishRun();
        }
    }

    private static void ScheduleAutoCloseGetItem(List<ItemDisplayData> expectedItems)
    {
        var session = ModSession.Generation;
        SingletonObject.getInstance<YieldHelper>().DelayFrameDo(12u, delegate
        {
            if (!ModSession.IsCurrent(session) || !Plugin.EnableAutoHarvestCloseObtainView) return;
            try
            {
                var view = UIElement.GetItem.UiBaseAs<ViewObtain>();
                if (view == null || !view.gameObject.activeInHierarchy)
                    return;

                var currentItems = Traverse.Create(view).Field("_itemList").GetValue<List<ItemDisplayData>>();
                if (!ReferenceEquals(currentItems, expectedItems))
                    return;

                Traverse.Create(view).Method("QuickHide").GetValue();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterTaiwuScroll] Auto close harvest obtain view failed: " + ex.Message);
            }
        });
    }

    private static void HandleRecruitPeopleAfterAutoHarvest()
    {
        if (Plugin.EnableAutoRecruitPeople)
        {
            RecruitPeopleAutoActionSupport.StartBackgroundAutoProcess();
            return;
        }

        EntryRecruitPeopleOverviewFromAutoHarvest();
    }

    private static void EntryRecruitPeopleOverviewFromAutoHarvest()
    {
        UI_RecruitPeopleOverview.EntryFromBuildingArea();
    }


    private static void RefreshQuickButtons()
    {
        GEvent.OnEvent(UiEvents.OnUpdateQuickBtnState);
        GEvent.OnEvent(UiEvents.UpdateAllBlockInfo);
    }

    private static void FinishRun()
    {
        _running = false;
    }

    private sealed class HarvestAvailability
    {
        internal int Run;
        internal int Session;
        internal int PendingRequests;
        internal int SoldItemCount;
        internal int ShopItemCount;
        internal int PeopleCount;
        internal bool HasFeathers;
        internal bool Cancelled;
    }

    private static bool IsCurrent(HarvestAvailability state) => !state.Cancelled && _running
        && state.Run == _run && ModSession.IsCurrent(state.Session) && Enabled;
}

