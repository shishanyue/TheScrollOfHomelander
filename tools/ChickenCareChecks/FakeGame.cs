// Domain doubles execute the production patch without loading the game, a save,
// or user JSON. Semantics exercised here were checked in the installed DLLs.
using System.Reflection;
using GameData.Common;
using GameData.Domains;
using GameData.Domains.Building;
using GameData.Domains.Item;
using GameData.Domains.Map;
using GameData.Domains.Taiwu;

internal static class FakeGame
{
    internal static readonly Location Home = new(1, 1);
    internal const short SettlementId = 7;
    internal static readonly Dictionary<ItemKey, int> Grades = new();
    internal static readonly HashSet<ItemKey> Feedable = new();
    internal static readonly List<(int ChickenId, ItemKey Item, ItemSourceType Source)> Feeds = new();
    internal static readonly List<(ItemKey Item, ItemSourceType Source, bool DeleteItem)> Removals = new();
    internal static readonly List<(ItemKey Item, int Slot, bool IsCricket)> CricketStores = new();
    internal static readonly List<(ItemKey Item, int Slot, bool IsCricket, ItemSourceType Target)> CricketRetrievals = new();
    internal static int NativeCalls;
    internal static int NativeCallLimit = 40;
    internal static int ReturnHappiness = 10;
    internal static bool ChangeHappiness = true;
    internal static Action<int> AfterFeed;

    internal static void Reset()
    {
        Grades.Clear(); Feedable.Clear(); Feeds.Clear(); Removals.Clear();
        CricketStores.Clear(); CricketRetrievals.Clear();
        NativeCalls = 0; ReturnHappiness = 10; ChangeHappiness = true; AfterFeed = null;
        DomainManager.Building.Chickens.Clear();
        DomainManager.Building.SettlementIds.Clear();
        DomainManager.Taiwu.TroughItems.Clear();
        DomainManager.Taiwu.WarehouseItems.Clear();
        DomainManager.Character.Inventory.Clear();
        DomainManager.Character.RequestedSubTypes.Clear();
        DomainManager.Item.Crickets.Clear();
        DomainManager.Extra.CricketSlots.Clear();
        DomainManager.Taiwu.CanTransferToWarehouse = true;
        DomainManager.Story.Status = 1;
        DomainManager.Extra.EventBox = new();
        DomainManager.Organization.SettlementExists = true;
        GameData.Utilities.AdaptableLog.Messages.Clear();
    }

    internal static void AddChicken(int id, short template = 1, sbyte happiness = 70, short settlement = SettlementId)
    {
        DomainManager.Building.Chickens[id] = new()
        {
            Id = id, TemplateId = template, Happiness = happiness, CurrentSettlementId = settlement
        };
        if (settlement == SettlementId) DomainManager.Building.SettlementIds.Add(id);
    }

    internal static ItemKey AddFeed(ItemSourceType source, int count = 10, short template = 1, int grade = 0, int id = 1)
    {
        var key = new ItemKey(5, template, id);
        Feedable.Add(key); Grades[key] = grade;
        if (source == ItemSourceType.Inventory)
            DomainManager.Character.Inventory.Add(new() { RealKey = key, Amount = count });
        else
            (source == ItemSourceType.Trough ? DomainManager.Taiwu.TroughItems : DomainManager.Taiwu.WarehouseItems)[key] = count;
        return key;
    }

    internal static ItemKey AddCricket(int id, short durability = 1, short maximum = 3, bool alive = true, sbyte grade = 0)
    {
        var key = new ItemKey(11, 0, id);
        DomainManager.Item.Crickets[id] = new()
        { Alive = alive, Durability = durability, Maximum = maximum, Grade = grade };
        DomainManager.Character.Inventory.Add(new() { RealKey = key, Amount = 1, SubType = 1100 });
        return key;
    }

    internal static GameData.Domains.Extra.CricketCollectionData AddCricketRoom(bool withJar = true, int regenMonths = 1)
    {
        var slot = new GameData.Domains.Extra.CricketCollectionData
        {
            Cricket = ItemKey.Invalid,
            CricketJar = withJar ? new ItemKey(12, 0, 1000 + DomainManager.Extra.CricketSlots.Count) : ItemKey.Invalid,
            RegenMonths = regenMonths,
        };
        DomainManager.Extra.CricketSlots.Add(slot);
        return slot;
    }

    internal static void PutCricketInRoom(ItemKey key, GameData.Domains.Extra.CricketCollectionData room)
    {
        Remove(key, ItemSourceType.Inventory, 1, false);
        room.Cricket = key;
    }

    internal static int Count(ItemKey key, ItemSourceType source) => source == ItemSourceType.Inventory
        ? DomainManager.Character.Inventory.Where(item => item != null && item.RealKey == key).Sum(item => item.Amount)
        : (source == ItemSourceType.Trough ? DomainManager.Taiwu.TroughItems : DomainManager.Taiwu.WarehouseItems).GetValueOrDefault(key);

    internal static void Remove(ItemKey key, ItemSourceType source, int amount, bool deleteItem = true)
    {
        if (Count(key, source) < amount) throw new InvalidOperationException("Removing unavailable feed.");
        Removals.Add((key, source, deleteItem));
        if (source == ItemSourceType.Inventory)
            DomainManager.Character.Inventory.First(item => item != null && item.RealKey == key).Amount -= amount;
        else
        {
            var items = source == ItemSourceType.Trough ? DomainManager.Taiwu.TroughItems : DomainManager.Taiwu.WarehouseItems;
            items[key] -= amount;
        }
    }
}

namespace BetterTaiwuScroll.Backend { public sealed class Plugin { } }
namespace Newtonsoft.Json
{
    public static class JsonConvert
    {
        public static T DeserializeObject<T>(string _) => throw new InvalidOperationException("Regression checks must not read user settings.");
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string method, Type[] parameters) { } }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] parameters) =>
            type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, parameters, null);
    }
}
namespace GameData.Common { public sealed class DataContext { } }
namespace GameData.Domains
{
    public static class DomainManager
    {
        public static readonly BuildingDomain Building = new();
        public static readonly TaiwuDomain Taiwu = new();
        public static readonly Character.CharacterDomain Character = new();
        public static readonly Extra.ExtraDomain Extra = new();
        public static readonly Story.StoryDomain Story = new();
        public static readonly Organization.OrganizationDomain Organization = new();
        public static readonly MapDomain Map = new();
        public static readonly Item.ItemDomain Item = new();
        public static readonly Mod.ModDomain Mod = new();
    }
}
namespace GameData.Domains.Map
{
    public readonly record struct Location(short AreaId, short BlockId)
    {
        public static Location Invalid => new(-1, -1);
        public bool IsValid() => AreaId >= 0 && BlockId >= 0;
    }
    public sealed class MapBlock
    {
        public MapBlock GetRootBlock() => this;
        public Location GetLocation() => FakeGame.Home;
    }
    public sealed class MapDomain { public MapBlock GetBlock(Location _) => new(); }
}
namespace GameData.Domains.Building
{
    public struct Chicken
    {
        public int Id;
        public short TemplateId;
        public sbyte Happiness;
        public short CurrentSettlementId;
    }
    public sealed class BuildingAreaData { }
    public static class SharedMethods
    { public static bool CheckItemCanFeedChicken(ItemKey key) => FakeGame.Feedable.Contains(key); }
    public sealed class BuildingDomain
    {
        public readonly Dictionary<int, Chicken> Chickens = new();
        public readonly List<int> SettlementIds = new();
        public bool TryGetElement_BuildingAreas(Location _, out BuildingAreaData area) { area = new(); return true; }
        public IReadOnlyList<int> GetSettlementChickenIdList(int settlement) => SettlementIds;
        public List<int> GetSettlementChickenList(int settlement, bool ignoreFulong = true)
        {
            var result = SettlementIds.ToList();
            if (!ignoreFulong && DomainManager.Story.Status == 0)
            {
                var box = DomainManager.Extra.EventBox ?? throw new NullReferenceException("Native Fulong event box is absent.");
                if (box.KingAway)
                {
                    var kingIndex = result.FindLastIndex(id => Chickens.TryGetValue(id, out var chicken) && chicken.TemplateId == 63);
                    if (kingIndex >= 0) result.RemoveAt(kingIndex);
                }
            }
            return result;
        }
        public Chicken GetChickenData(int id) => Chickens.GetValueOrDefault(id);
        public bool TryGetElement_Chicken(int id, out Chicken chicken) => Chickens.TryGetValue(id, out chicken);
        public bool TryGetElement_ChickenByTemplateId(int templateId, out Chicken chicken)
        {
            foreach (var current in Chickens.Values)
                if (current.TemplateId == templateId) { chicken = current; return true; }
            chicken = default; return false;
        }
        private sbyte FeedChickenWithArgs(DataContext context, int templateId, ItemKey key, ItemSourceType source)
        {
            FakeGame.NativeCalls++;
            if (FakeGame.NativeCalls > FakeGame.NativeCallLimit)
                throw new InvalidOperationException("Native feed-call guard: probable infinite loop.");
            if (!TryGetElement_ChickenByTemplateId(templateId, out var chicken)) return 0;
            // A fake item is recorded as a call but has no positive effect. The real
            // method has no generic ownership guard; callers must select a real item.
            if (!FakeGame.Feedable.Contains(key) || FakeGame.Count(key, source) <= 0) return 0;
            if (FakeGame.ChangeHappiness)
            {
                chicken.Happiness = (sbyte)Math.Clamp(chicken.Happiness + FakeGame.ReturnHappiness, 0, 100);
                Chickens[chicken.Id] = chicken;
            }
            FakeGame.Feeds.Add((chicken.Id, key, source));
            if (source is ItemSourceType.Inventory or ItemSourceType.Trough) FakeGame.Remove(key, source, 1);
            FakeGame.AfterFeed?.Invoke(chicken.Id);
            return (sbyte)FakeGame.ReturnHappiness;
        }
        public void CricketCollectionAdd(DataContext _, int index, bool isCricket, ItemKey key)
        {
            if (!isCricket) throw new InvalidOperationException("The production patch must store crickets, not jars.");
            var slot = DomainManager.Extra.CricketSlots[index];
            if (slot.Cricket.IsValid()) throw new InvalidOperationException("A stored cricket would be overwritten.");
            var source = DomainManager.Taiwu.WarehouseItems.ContainsKey(key) ? ItemSourceType.Warehouse : ItemSourceType.Inventory;
            FakeGame.Remove(key, source, 1, false);
            slot.Cricket = key;
            slot.CricketRegen = 0;
            FakeGame.CricketStores.Add((key, index, isCricket));
        }
        public void CricketCollectionRemove(DataContext _, int index, bool isCricket)
        {
            if (!isCricket) throw new InvalidOperationException("The production patch must retrieve crickets, not jars.");
            var slot = DomainManager.Extra.CricketSlots[index];
            var key = slot.Cricket;
            if (!key.IsValid()) throw new InvalidOperationException("Cannot retrieve an empty cricket slot.");
            var target = DomainManager.Taiwu.CanTransferToWarehouse ? ItemSourceType.Inventory : ItemSourceType.Warehouse;
            if (target == ItemSourceType.Inventory)
            {
                var item = DomainManager.Character.Inventory.FirstOrDefault(item => item.RealKey == key);
                if (item == null) DomainManager.Character.Inventory.Add(new() { RealKey = key, Amount = 1, SubType = 1100 });
                else item.Amount++;
            }
            else DomainManager.Taiwu.WarehouseItems[key] = DomainManager.Taiwu.WarehouseItems.GetValueOrDefault(key) + 1;
            slot.Cricket = ItemKey.Invalid;
            slot.CricketRegen = 0;
            FakeGame.CricketRetrievals.Add((key, index, isCricket, target));
        }
        public void UpdateCricketRoom(DataContext _)
        {
            // A narrow native double: configurable recovery interval avoids
            // importing game configuration while proving store/month/restore order.
            foreach (var slot in DomainManager.Extra.CricketSlots)
            {
                if (!slot.Cricket.IsValid()) continue;
                var cricket = DomainManager.Item.Crickets[slot.Cricket.Id];
                if (!cricket.Alive || cricket.Durability >= cricket.Maximum) continue;
                if (++slot.CricketRegen >= slot.RegenMonths)
                {
                    slot.CricketRegen = 0;
                    cricket.Durability++;
                }
            }
        }
    }
}
namespace GameData.Domains.Character
{
    public sealed class Character { public Location GetLocation() => FakeGame.Home; }
    public sealed class InventoryItem { public ItemKey RealKey; public int Amount; public short SubType; }
    public sealed class CharacterDomain
    {
        public readonly List<InventoryItem> Inventory = new();
        public readonly List<short> RequestedSubTypes = new();
        public List<InventoryItem> GetAllInventoryItems(int _) => Inventory;
        public List<InventoryItem> GetInventoryItems(int _, short type)
        {
            RequestedSubTypes.Add(type);
            return Inventory.Where(item => item != null && item.Amount > 0 && item.SubType == type).ToList();
        }
    }
}
namespace GameData.Domains.Organization
{
    public sealed class Settlement
    {
        public short GetId() => FakeGame.SettlementId;
        public Location GetLocation() => FakeGame.Home;
    }
    public sealed class OrganizationDomain
    {
        public bool SettlementExists = true;
        public Settlement GetSettlementByLocation(Location _) => SettlementExists ? new() : null;
    }
}
namespace GameData.Domains.Story
{
    public sealed class StoryDomain
    {
        public sbyte Status = 1;
        public sbyte GetSectMainStoryTaskStatus(sbyte _) => Status;
    }
}
namespace GameData.Domains.TaiwuEvent
{
    public sealed class EventArgBox
    {
        public bool KingAway;
        public void Get<T>(string key, ref T value) { if (typeof(T) == typeof(bool)) value = (T)(object)KingAway; }
    }
}
namespace GameData.Domains.Extra
{
    public sealed class CricketCollectionData
    {
        public ItemKey Cricket = ItemKey.Invalid, CricketJar = ItemKey.Invalid;
        public int CricketRegen;
        public int RegenMonths = 1;
    }
    public sealed class ExtraDomain
    {
        public TaiwuEvent.EventArgBox EventBox = new();
        public readonly List<CricketCollectionData> CricketSlots = new();
        public TaiwuEvent.EventArgBox GetSectMainStoryEventArgBox(sbyte _) => EventBox;
        public List<CricketCollectionData> GetCricketCollectionDataList() => CricketSlots;
    }
}
namespace GameData.Domains.Item
{
    public readonly record struct ItemKey(sbyte ItemType, short TemplateId, int Id)
    {
        public static ItemKey Invalid => new(-1, -1, -1);
        // The game's default(ItemKey) has zero fields and passes this check.
        public bool IsValid() => Id >= 0;
    }
    public static class ItemTemplateHelper
    {
        public static int GetGrade(sbyte type, short template) => FakeGame.Grades
            .Where(pair => pair.Key.ItemType == type && pair.Key.TemplateId == template)
            .Select(pair => pair.Value).FirstOrDefault();
    }
    public sealed class Cricket
    {
        public bool Alive = true;
        public short Durability = 1, Maximum = 2;
        public sbyte Grade;
        public bool IsAlive => Alive;
        public short GetCurrDurability() => Durability;
        public short GetMaxDurability() => Maximum;
        public sbyte GetGrade() => Grade;
    }
    public sealed class ItemDomain
    {
        public readonly Dictionary<int, Cricket> Crickets = new();
        public bool TryGetElement_Crickets(int id, out Cricket cricket) => Crickets.TryGetValue(id, out cricket);
    }
}
namespace GameData.Domains.Taiwu
{
    public enum ItemSourceType : sbyte { Invalid = -1, Inventory = 1, Warehouse = 2, Trough = 4 }
    public sealed class TaiwuDomain
    {
        public readonly Dictionary<ItemKey, int> TroughItems = new();
        public readonly Dictionary<ItemKey, int> WarehouseItems = new();
        public bool CanTransferToWarehouse = true;
        public Character.Character GetTaiwu() => new();
        public int GetTaiwuCharId() => 0;
        public Location GetTaiwuVillageLocation() => FakeGame.Home;
        public void RemoveItem(DataContext context, ItemKey key, int count, ItemSourceType source, bool deleteItem, bool _)
            => FakeGame.Remove(key, source, count, deleteItem);
    }
}
namespace GameData.Domains.Mod
{
    public sealed class ModDomain { public void GetSetting(string mod, string key, ref bool value) { } }
}
namespace GameData.Domains.World
{ public sealed class WorldDomain { public void AdvanceMonth(DataContext context) { } } }
namespace GameData.Utilities
{
    public static class AdaptableLog
    {
        public static readonly List<string> Messages = new();
        public static void Info(string message) => Messages.Add(message);
        public static void Warning(string message, bool _) => Messages.Add(message);
        public static void Error(string message) => Messages.Add(message);
    }
}
