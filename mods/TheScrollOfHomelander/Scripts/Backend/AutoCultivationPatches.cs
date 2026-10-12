#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameData.Common;
using GameData.Domains;
using GameData.Domains.Building;
using GameData.Domains.Item;
using GameData.Domains.Map;
using GameData.Domains.Taiwu;
using GameData.Domains.World;
using GameData.Utilities;
using HarmonyLib;
using Newtonsoft.Json;

namespace BetterTaiwuScroll.Backend;

internal static class AutoCultivationFeatureSettings
{
    internal static bool AutoChickenCareEnabled;
    internal static bool AutoCricketRoomEnabled;

    internal static void Load(string modId)
    {
        var chicken = false;
        var cricket = false;
        TryGet(modId, "auto_chicken_care", ref chicken);
        TryGet(modId, "auto_cricket_room", ref cricket);
        AutoChickenCareEnabled = chicken;
        AutoCricketRoomEnabled = cricket;
    }

    private static void TryGet(string modId, string key, ref bool value)
    {
        try
        {
            DomainManager.Mod.GetSetting(modId, key, ref value);
        }
        catch
        {
        }
    }
}

internal sealed class AutoChickenCareSettings
{
    public bool Enabled = true;
    public int HappinessThreshold = 90;
    public bool UseTrough = true;
    public bool UseWarehouse = true;
    public bool UseInventory;

    internal void Normalize()
    {
        HappinessThreshold = Math.Clamp(HappinessThreshold, 0, 100);
    }
}

internal sealed class AutoCricketRoomSettings
{
    public bool Enabled = true;
    public bool RetrieveAutomaticallyStored = true;
    public bool RetrievePlayerStored;
}

internal static class AutoCultivationSettingsStore
{
    internal static AutoChickenCareSettings LoadChicken()
    {
        var settings = Load<AutoChickenCareSettings>("AutoChickenCareSettings.json") ?? new AutoChickenCareSettings();
        settings.Normalize();
        return settings;
    }

    internal static AutoCricketRoomSettings LoadCricket()
    {
        return Load<AutoCricketRoomSettings>("AutoCricketRoomSettings.json") ?? new AutoCricketRoomSettings();
    }

    private static T Load<T>(string fileName) where T : class
    {
        foreach (var path in GetCandidates(fileName))
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                AdaptableLog.Warning("[BetterTaiwuScroll] Failed to load " + fileName + " from " + path + ": " + ex.Message, false);
            }
        }
        return null;
    }

    private static IEnumerable<string> GetCandidates(string fileName)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents))
            yield return Path.Combine(documents, "TheScrollOfHomelander", fileName);

        var assemblyPath = typeof(Plugin).Assembly.Location;
        if (!string.IsNullOrEmpty(assemblyPath))
        {
            var modRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(assemblyPath), "..", ".."));
            yield return Path.Combine(modRoot, "UserData", fileName);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            yield return Path.Combine(appData, "TaiwuStudio", "Taiwu Studio", "data", "mods", "TheScrollOfHomelander", "UserData", fileName);
        }
    }
}

[HarmonyPatch(typeof(WorldDomain), nameof(WorldDomain.AdvanceMonth), new[] { typeof(DataContext) })]
internal static class AutoCultivationAdvanceMonthPatch
{
    private static readonly MethodInfo FeedChickenWithArgsMethod = AccessTools.Method(
        typeof(BuildingDomain),
        "FeedChickenWithArgs",
        new[] { typeof(DataContext), typeof(int), typeof(ItemKey), typeof(ItemSourceType) });

    private static void Prefix(DataContext context, ref MonthState __state)
    {
        __state = new MonthState();
        try
        {
            var taiwu = DomainManager.Taiwu.GetTaiwu();
            if (taiwu == null || !TryGetIndustrySettlementLocation(taiwu.GetLocation(), out var settlementLocation))
                return;

            __state.OnIndustryTile = true;
            __state.SettlementLocation = settlementLocation;
            __state.ChickenSettings = AutoCultivationFeatureSettings.AutoChickenCareEnabled
                ? AutoCultivationSettingsStore.LoadChicken()
                : null;
            __state.CricketSettings = AutoCultivationFeatureSettings.AutoCricketRoomEnabled
                ? AutoCultivationSettingsStore.LoadCricket()
                : null;

            if (__state.CricketSettings?.Enabled == true)
                StoreCrickets(context, __state);
        }
        catch (Exception ex)
        {
            AdaptableLog.Error("[BetterTaiwuScroll] Auto cultivation preparation failed: " + ex);
        }
    }

    private static void Postfix(DataContext context, MonthState __state)
    {
        if (__state == null || !__state.OnIndustryTile)
            return;

        try
        {
            if (__state.ChickenSettings?.Enabled == true)
                FeedChickens(context, __state.ChickenSettings, __state.SettlementLocation);
        }
        catch (Exception ex)
        {
            AdaptableLog.Error("[BetterTaiwuScroll] Auto chicken care failed: " + ex);
        }

        RestoreCrickets(context, __state);
        __state.Completed = true;
    }

    private static Exception Finalizer(DataContext context, MonthState __state, Exception __exception)
    {
        if (__state != null && __state.OnIndustryTile && !__state.Completed)
            RestoreCrickets(context, __state);
        return __exception;
    }

    private static bool TryGetIndustrySettlementLocation(Location location, out Location settlementLocation)
    {
        settlementLocation = Location.Invalid;
        if (!location.IsValid())
            return false;
        var rootLocation = DomainManager.Map.GetBlock(location).GetRootBlock().GetLocation();
        if (!DomainManager.Building.TryGetElement_BuildingAreas(rootLocation, out BuildingAreaData areaData)
            || areaData == null || DomainManager.Organization.GetSettlementByLocation(rootLocation) == null)
            return false;
        settlementLocation = rootLocation;
        return true;
    }

    private static void FeedChickens(DataContext context, AutoChickenCareSettings settings, Location settlementLocation)
    {
        if (!settings.Enabled || settings.HappinessThreshold <= 0 || (!settings.UseTrough && !settings.UseWarehouse && !settings.UseInventory))
            return;
        if (FeedChickenWithArgsMethod == null)
        {
            AdaptableLog.Error("[BetterTaiwuScroll] Auto chicken care stopped: FeedChickenWithArgs was not found.");
            return;
        }

        // Month settlement and story events can invalidate the state captured by Prefix.
        // Keep the native Fulong absence filter, but validate its dereferenced inputs first.
        if (!settlementLocation.IsValid())
            return;
        var settlement = DomainManager.Organization.GetSettlementByLocation(settlementLocation);
        if (settlement == null)
            return;
        const sbyte fulongSectId = 14;
        var fulongStateUnavailable = DomainManager.Story.GetSectMainStoryTaskStatus(fulongSectId) == 0
            && DomainManager.Taiwu.GetTaiwuVillageLocation().Equals(settlement.GetLocation())
            && DomainManager.Extra.GetSectMainStoryEventArgBox(fulongSectId) == null;

        // Without the optional story data the native filter dereferences null.
        // Continue caring for ordinary chickens, but conservatively exclude the king.
        var chickenIds = DomainManager.Building.GetSettlementChickenList(settlement.GetId(), fulongStateUnavailable);
        if (chickenIds == null || chickenIds.Count == 0)
            return;
        var fedCount = 0;
        var usedItems = 0;
        foreach (var chickenId in chickenIds.Where(id => id >= 0).Distinct().OrderBy(id => id).ToArray())
        {
            var fedThisChicken = false;
            // Id zero is valid; GetChickenData returns a default struct for missing ids.
            while (DomainManager.Building.TryGetElement_Chicken(chickenId, out var current)
                && current.Id == chickenId && current.CurrentSettlementId == settlement.GetId()
                && (!fulongStateUnavailable || current.TemplateId != 63)
                && current.Happiness < settings.HappinessThreshold)
            {
                if (!TrySelectFeedItem(settings, out var key, out var source))
                    break;
                if (!FeedOneChicken(context, current, key, source))
                    break;
                usedItems++;
                fedThisChicken = true;
            }
            if (fedThisChicken)
                fedCount++;
        }

        if (fedCount > 0 || usedItems > 0)
        {
            AdaptableLog.Info("[BetterTaiwuScroll] Auto chicken care finished: chickens=" + fedCount
                + ", items=" + usedItems + ", threshold=" + settings.HappinessThreshold + ".");
        }
    }

    private static bool TrySelectFeedItem(AutoChickenCareSettings settings, out ItemKey key, out ItemSourceType source)
    {
        if (settings.UseTrough && TrySelectFromDictionary(DomainManager.Taiwu.TroughItems, out key))
        {
            source = ItemSourceType.Trough;
            return true;
        }
        if (settings.UseWarehouse && TrySelectFromDictionary(DomainManager.Taiwu.WarehouseItems, out key))
        {
            source = ItemSourceType.Warehouse;
            return true;
        }
        if (settings.UseInventory)
        {
            var items = DomainManager.Character.GetAllInventoryItems(DomainManager.Taiwu.GetTaiwuCharId());
            key = items
                .Where(item => item != null && item.Amount > 0 && item.RealKey.IsValid()
                    && GameData.Domains.Building.SharedMethods.CheckItemCanFeedChicken(item.RealKey))
                .OrderBy(item => ItemTemplateHelper.GetGrade(item.RealKey.ItemType, item.RealKey.TemplateId))
                .ThenBy(item => item.RealKey.ItemType)
                .ThenBy(item => item.RealKey.TemplateId)
                .ThenBy(item => item.RealKey.Id)
                .Select(item => item.RealKey)
                .DefaultIfEmpty(ItemKey.Invalid).First();
            if (key.IsValid())
            {
                source = ItemSourceType.Inventory;
                return true;
            }
        }

        key = ItemKey.Invalid;
        source = ItemSourceType.Invalid;
        return false;
    }

    private static bool TrySelectFromDictionary(Dictionary<ItemKey, int> items, out ItemKey key)
    {
        key = items
            .Where(pair => pair.Value > 0 && pair.Key.IsValid()
                && GameData.Domains.Building.SharedMethods.CheckItemCanFeedChicken(pair.Key))
            .OrderBy(pair => ItemTemplateHelper.GetGrade(pair.Key.ItemType, pair.Key.TemplateId))
            .ThenBy(pair => pair.Key.ItemType)
            .ThenBy(pair => pair.Key.TemplateId)
            .ThenBy(pair => pair.Key.Id)
            .Select(pair => pair.Key)
            // default(ItemKey) has Id zero and passes the game's IsValid check.
            .DefaultIfEmpty(ItemKey.Invalid).First();
        return key.IsValid();
    }

    private static bool FeedOneChicken(DataContext context, GameData.Domains.Building.Chicken chicken, ItemKey key, ItemSourceType source)
    {
        try
        {
            // The native method resolves by template, not instance id. Never feed
            // another settlement's chicken or loop on an unchanged duplicate.
            if (!DomainManager.Building.TryGetElement_ChickenByTemplateId(chicken.TemplateId, out var nativeTarget)
                || nativeTarget.Id != chicken.Id)
            {
                AdaptableLog.Warning("[BetterTaiwuScroll] Auto chicken care skipped an ambiguous template: chicken="
                    + chicken.Id + ", template=" + chicken.TemplateId + ".", false);
                return false;
            }

            var result = FeedChickenWithArgsMethod.Invoke(
                DomainManager.Building,
                new object[] { context, (int)chicken.TemplateId, key, source });
            var happinessChange = result == null ? 0 : Convert.ToInt32(result);
            if (happinessChange <= 0)
            {
                AdaptableLog.Warning("[BetterTaiwuScroll] Auto chicken care stopped for one chicken because feed made no positive progress: item=" + key + ".", false);
                return false;
            }
            // Native feeding consumes inventory/trough items, but has no warehouse branch.
            if (source == ItemSourceType.Warehouse)
                DomainManager.Taiwu.RemoveItem(context, key, 1, ItemSourceType.Warehouse, true, false);
            if (!DomainManager.Building.TryGetElement_Chicken(chicken.Id, out var updated)
                || updated.Id != chicken.Id || updated.Happiness <= chicken.Happiness)
            {
                AdaptableLog.Warning("[BetterTaiwuScroll] Auto chicken care stopped because the target did not gain happiness: chicken="
                    + chicken.Id + ", item=" + key + ".", false);
                return false;
            }
            return true;
        }
        catch (TargetInvocationException ex)
        {
            AdaptableLog.Warning("[BetterTaiwuScroll] Failed to feed chicken with " + key + ": " + (ex.InnerException ?? ex), false);
            return false;
        }
        catch (Exception ex)
        {
            AdaptableLog.Warning("[BetterTaiwuScroll] Failed to feed chicken with " + key + ": " + ex, false);
            return false;
        }
    }

    private static void StoreCrickets(DataContext context, MonthState state)
    {
        var slots = DomainManager.Extra.GetCricketCollectionDataList();
        if (slots == null)
            return;
        foreach (var slot in slots)
        {
            if (slot.Cricket.IsValid())
                state.PlayerStored.Add(slot.Cricket);
        }

        var freeSlots = new Queue<int>(Enumerable.Range(0, slots.Count)
            .Where(index => slots[index].CricketJar.IsValid() && !slots[index].Cricket.IsValid()));
        if (freeSlots.Count == 0)
            return;

        var candidates = new List<CricketCandidate>();
        foreach (var item in DomainManager.Character.GetInventoryItems(DomainManager.Taiwu.GetTaiwuCharId(), 1100))
        {
            if (item == null || !item.RealKey.IsValid())
                continue;

            try
            {
                if (!DomainManager.Item.TryGetElement_Crickets(item.RealKey.Id, out var cricket)
                    || cricket == null
                    || !cricket.IsAlive
                    || cricket.GetCurrDurability() >= cricket.GetMaxDurability())
                    continue;
                candidates.Add(new CricketCandidate(item.RealKey, cricket));
            }
            catch (Exception ex)
            {
                AdaptableLog.Warning("[BetterTaiwuScroll] Ignoring invalid cricket inventory item " + item.RealKey + ": " + ex.Message, false);
            }
        }

        candidates.Sort((left, right) =>
        {
            var leftRatio = (double)left.Cricket.GetCurrDurability() / Math.Max(1, (int)left.Cricket.GetMaxDurability());
            var rightRatio = (double)right.Cricket.GetCurrDurability() / Math.Max(1, (int)right.Cricket.GetMaxDurability());
            var comparison = leftRatio.CompareTo(rightRatio);
            if (comparison != 0)
                return comparison;
            comparison = right.Cricket.GetGrade().CompareTo(left.Cricket.GetGrade());
            return comparison != 0 ? comparison : left.Key.Id.CompareTo(right.Key.Id);
        });

        foreach (var candidate in candidates)
        {
            if (freeSlots.Count == 0)
                break;
            var index = freeSlots.Dequeue();
            try
            {
                DomainManager.Building.CricketCollectionAdd(context, index, true, candidate.Key);
                state.AutomaticallyStored.Add(candidate.Key);
            }
            catch (Exception ex)
            {
                AdaptableLog.Warning("[BetterTaiwuScroll] Failed to auto-store cricket " + candidate.Key + ": " + ex, false);
            }
        }

        if (state.AutomaticallyStored.Count > 0)
            AdaptableLog.Info("[BetterTaiwuScroll] Auto cricket storage prepared: stored=" + state.AutomaticallyStored.Count + ".");
    }

    private static void RestoreCrickets(DataContext context, MonthState state)
    {
        if (state?.CricketSettings?.Enabled != true)
            return;
        try
        {
            var keys = new List<ItemKey>();
            if (state.CricketSettings.RetrieveAutomaticallyStored)
                keys.AddRange(state.AutomaticallyStored);
            if (state.CricketSettings.RetrievePlayerStored)
                keys.AddRange(state.PlayerStored);

            var retrieved = 0;
            foreach (var key in keys.Distinct())
            {
                var slots = DomainManager.Extra.GetCricketCollectionDataList();
                if (slots == null)
                    break;
                var index = slots.FindIndex(slot => slot.Cricket == key);
                if (index < 0)
                    continue;
                DomainManager.Building.CricketCollectionRemove(context, index, true);
                retrieved++;
            }
            if (retrieved > 0)
                AdaptableLog.Info("[BetterTaiwuScroll] Auto cricket storage finished: retrieved=" + retrieved + ".");
        }
        catch (Exception ex)
        {
            AdaptableLog.Error("[BetterTaiwuScroll] Failed to restore auto-stored crickets: " + ex);
        }
    }

    private sealed class MonthState
    {
        internal bool OnIndustryTile;
        internal Location SettlementLocation;
        internal bool Completed;
        internal AutoChickenCareSettings ChickenSettings;
        internal AutoCricketRoomSettings CricketSettings;
        internal readonly List<ItemKey> AutomaticallyStored = new List<ItemKey>();
        internal readonly List<ItemKey> PlayerStored = new List<ItemKey>();
    }

    private readonly struct CricketCandidate
    {
        internal readonly ItemKey Key;
        internal readonly GameData.Domains.Item.Cricket Cricket;

        internal CricketCandidate(ItemKey key, GameData.Domains.Item.Cricket cricket)
        {
            Key = key;
            Cricket = cricket;
        }
    }
}
