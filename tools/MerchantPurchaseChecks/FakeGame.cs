// Domain doubles for executing the production monthly purchase service without
// loading game assemblies, creating saves, or reading/writing user settings.
global using GameData.Domains.Merchant;
using GameData.Common;
using GameData.Domains;
using GameData.Domains.Item;
using GameData.Domains.Item.Display;
using GameData.Domains.Taiwu;
using GameData.Domains.Taiwu.Display;
using GameData.Domains.Taiwu.ExchangeSystem;

internal static class FakeGame
{
    internal static readonly List<int> Visits = new();
    internal static readonly List<(int Merchant, int Count)> Purchases = new();
    internal static readonly Dictionary<int, Func<ShopDisplayData>> Shops = new();
    internal static readonly Dictionary<int, sbyte> DesignatedTypes = new();
    internal static readonly Dictionary<int, sbyte> NativeMerchantTypes = new();
    internal static long Money;
    internal static int Load;
    internal static int MaxLoad;
    internal static Action<int> AfterPurchase;

    internal static void Reset(long money = 1000, int load = 0, int maxLoad = 100)
    {
        Visits.Clear(); Purchases.Clear(); Shops.Clear(); DesignatedTypes.Clear(); NativeMerchantTypes.Clear();
        Money = money; Load = load; MaxLoad = maxLoad; AfterPurchase = null;
        DomainManager.Taiwu.Group.Ids.Clear();
        DomainManager.Character.Characters.Clear();
        DomainManager.Extra.Types.Clear();
        GameData.Utilities.AdaptableLog.Messages.Clear();
        Config.Material.Instance.Items.Clear();
    }

    internal static void AddMerchant(int id, params FakeItem[] items)
    {
        DomainManager.Taiwu.Group.Ids.Add(id);
        DomainManager.Character.Characters[id] = new();
        DomainManager.Extra.Types[id] = 0;
        DesignatedTypes[id] = 0;
        NativeMerchantTypes[id] = 0;
        Shops[id] = () => Shop(id, items);
    }

    internal static ShopDisplayData Shop(int id, params FakeItem[] items)
    {
        var baseline = new MerchantOverFavorData();
        var exchange = new ShopExchange
        {
            MerchantOverFavorData = baseline,
            TradeArguments = new() { MerchantData = new() { CharId = id }, OverFavorData = baseline },
            TaiwuValueBase = Money,
            TaiwuInventoryCurLoadPreview = Load,
            TaiwuInventoryMaxLoadPreview = MaxLoad,
        };
        var result = new ShopDisplayData { Exchange = exchange };
        foreach (var item in items)
        {
            Config.Material.Instance.Items[item.RealKey.TemplateId] = new() { Grade = 0, ItemSubType = 501 };
            result.Pages[item.ItemSourceType - 10].Add(item);
        }
        return result;
    }
}

internal sealed class FakeItem : ITradeableContent
{
    internal FakeItem(short template, int count = 1, int weight = 1, long price = 10, int page = 0)
    { RealKey = new(5, template); Amount = count; Weight = weight; Price = price; ItemSourceType = (sbyte)(10 + page); }
    public ItemKey RealKey { get; }
    public int Amount { get; set; }
    public int CharacterId => -1;
    public sbyte ItemSourceType { get; }
    public ItemDisplayData.ItemUsingType UsingType => ItemDisplayData.ItemUsingType.Invalid;
    public int Weight { get; }
    public long Price { get; }
    internal FakeItem WithCount(int count) => new(RealKey.TemplateId, count, Weight, Price, ItemSourceType - 10);
}

namespace BetterTaiwuScroll.Backend { public sealed class Plugin { } }
namespace BetterTaiwuScroll.Shared { public static class ModProtocol { public const string PurchaseSettings = "unused.json"; } }
namespace Newtonsoft.Json { public static class JsonConvert { public static T DeserializeObject<T>(string _) => default; } }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method, Type[] parameters) { } }
}
namespace Config
{
    public sealed class MaterialItem { public sbyte Grade; public short ItemSubType; }
    public sealed class Material
    {
        public static readonly Material Instance = new();
        public readonly Dictionary<short, MaterialItem> Items = new();
        public MaterialItem this[short id] => Items[id];
    }
}
namespace GameData.Common { public sealed class DataContext { } }
namespace GameData.Domains
{
    public static class DomainManager
    {
        public static readonly Taiwu.TaiwuDomain Taiwu = new();
        public static readonly Character.CharacterDomain Character = new();
        public static readonly Extra.ExtraDomain Extra = new();
        public static readonly Merchant.MerchantDomain Merchant = new();
        public static readonly Map.MapDomain Map = new();
        public static readonly Building.BuildingDomain Building = new();
    }
}
namespace GameData.Domains.Map
{
    public readonly struct Location { public bool IsValid() => true; }
    public sealed class MapBlock { public MapBlock GetRootBlock() => this; public Location GetLocation() => new(); }
    public sealed class MapDomain { public MapBlock GetBlock(Location _) => new(); }
}
namespace GameData.Domains.Building
{
    public sealed class BuildingAreaData { }
    public sealed class BuildingDomain
    { public bool TryGetElement_BuildingAreas(Map.Location _, out BuildingAreaData area) { area = new(); return true; } }
}
namespace GameData.Domains.Character
{
    public sealed class Character { public int GetAgeGroup() => 1; public Map.Location GetLocation() => new(); }
    public sealed class CharacterSet
    {
        public readonly List<int> Ids = new();
        public List<int> GetCollection() => Ids;
        public bool Contains(int id) => Ids.Contains(id);
    }
    public sealed class CharacterDomain
    {
        public readonly Dictionary<int, Character> Characters = new();
        public bool TryGetElement_Objects(int id, out Character character) => Characters.TryGetValue(id, out character);
    }
}
namespace GameData.Domains.Extra
{
    public sealed class ExtraDomain
    {
        public readonly Dictionary<int, sbyte> Types = new();
        public bool TryGetMerchantCharToType(int id, out sbyte type) => Types.TryGetValue(id, out type);
    }
}
namespace GameData.Domains.Item
{
    public readonly record struct ItemKey(sbyte ItemType, short TemplateId);
    public static class ItemTemplateHelper { public static int GetBaseWeight(sbyte type, short template) => 1; }
}
namespace GameData.Domains.Item.Display
{
    public sealed class ItemDisplayData { public enum ItemUsingType { Invalid } }
    public interface ITradeableContent
    {
        ItemKey RealKey { get; }
        int Amount { get; }
        int CharacterId { get; }
        sbyte ItemSourceType { get; }
        ItemDisplayData.ItemUsingType UsingType { get; }
        int Weight { get; }
        long Price { get; }
    }
}
namespace GameData.Domains.Merchant
{
    public sealed class OpenShopEventArguments
    { public int Id; public sbyte MerchantSourceType; public bool Refresh, IgnoreFavorability, IgnoreWorldProgress; public int CurrPage; }
    public sealed class MerchantData { public int CharId; }
    public sealed class MerchantDomain
    {
        public MerchantData GetMerchantData(DataContext context, int id) => new() { CharId = id };
        public sbyte GetOrCreateMerchantTemplateId(DataContext context, int id)
        {
            if (!FakeGame.NativeMerchantTypes.TryGetValue(id, out var type)) return -1;
            DomainManager.Extra.Types[id] = type;
            return 0;
        }
    }
    public sealed class MerchantTradeArguments { public MerchantData MerchantData; public MerchantOverFavorData OverFavorData; }
    public sealed class MerchantOverFavorLevelData { public short BuyCount = short.MaxValue; }
    public sealed class MerchantOverFavorData
    {
        public MerchantOverFavorLevelData[] MerchantOverFavorLevelDataArray =
            Enumerable.Range(0, 7).Select(_ => new MerchantOverFavorLevelData()).ToArray();
        public object Clone() => new MerchantOverFavorData
        {
            MerchantOverFavorLevelDataArray = MerchantOverFavorLevelDataArray
                .Select(x => x == null ? null : new MerchantOverFavorLevelData { BuyCount = x.BuyCount }).ToArray()
        };
    }
}
namespace GameData.Domains.Taiwu
{
    public enum ItemSourceType : sbyte { Inventory = 1, Warehouse = 2, Treasury = 3 }
    public sealed class TaiwuDomain
    {
        public readonly Character.CharacterSet Group = new();
        public Character.Character GetTaiwu() => new();
        public int GetTaiwuCharId() => 0;
        public sbyte GetMerchantType(int id) => FakeGame.DesignatedTypes.GetValueOrDefault(id, (sbyte)7);
        public Character.CharacterSet GetGroupCharIds() => Group;
        public ShopDisplayData GetShopDisplayData(DataContext context, OpenShopEventArguments arguments)
        { FakeGame.Visits.Add(arguments.Id); return FakeGame.Shops[arguments.Id](); }
        public void ConfirmShopExchange(DataContext _, ShopExchange exchange)
        {
            if (!exchange.CanConfirmExchange) throw new Exception("Native confirm requires valid caller input in this test.");
            int count = exchange.TargetContentList.Sum(x => x.Amount);
            FakeGame.Money -= exchange.TotalValueWithAdvantage;
            FakeGame.Load = exchange.TaiwuInventoryCurLoadPreview;
            FakeGame.Purchases.Add((exchange.MerchantData.CharId, count));
            FakeGame.AfterPurchase?.Invoke(exchange.MerchantData.CharId);
        }
    }
}
namespace GameData.Domains.Taiwu.Display
{
    public sealed class ShopDisplayData
    {
        public ShopExchange Exchange;
        public readonly List<ITradeableContent>[] Pages = Enumerable.Range(0, 7).Select(_ => new List<ITradeableContent>()).ToArray();
        public List<ITradeableContent> this[int page] => Pages[page];
    }
}
namespace GameData.Domains.Taiwu.ExchangeSystem
{
    public sealed class ShopExchange
    {
        public MerchantTradeArguments TradeArguments;
        public MerchantOverFavorData MerchantOverFavorData;
        public MerchantData MerchantData => TradeArguments.MerchantData;
        public int MinDebtLevel;
        public long TaiwuValueBase;
        public int TaiwuInventoryCurLoadPreview;
        public int TaiwuInventoryMaxLoadPreview;
        public long TotalValueWithAdvantage => TargetContentList.Sum(x => x.Price * x.Amount);
        public readonly List<ITradeableContent> TargetContentList = new();
        private ItemSourceType _target;
        public bool CanConfirmExchange => TargetContentList.Count > 0 && TotalValueWithAdvantage <= TaiwuValueBase
            && TradeArguments.OverFavorData.MerchantOverFavorLevelDataArray.All(x => (x?.BuyCount ?? 0) >= 0);
        public void SetItemSource(sbyte source) => _target = (ItemSourceType)source;
        public bool IsPageShow(int page) => page >= 0 && page <= 6;
        public static bool IsShopItem(ITradeableContent item) => item.ItemSourceType >= 10 && item.ItemSourceType <= 16;
        public int GetPriceChangePercentValue(ITradeableContent _, bool isBuy) => 0;
        public long CalcBaseValue(ITradeableContent item) => item.Price;
        public void SelectTargetItem(ITradeableContent item, int count)
        {
            TargetContentList.Add(((FakeItem)item).WithCount(count));
            if (_target == ItemSourceType.Inventory) TaiwuInventoryCurLoadPreview += item.Weight * count;
        }
        public int RefreshDebt()
        {
            for (int page = 0; page < 7; page++)
            {
                var preview = TradeArguments.OverFavorData.MerchantOverFavorLevelDataArray[page];
                var original = MerchantOverFavorData.MerchantOverFavorLevelDataArray[page];
                if (preview == null || original == null) continue;
                preview.BuyCount = original.BuyCount;
                if (page > MinDebtLevel && preview.BuyCount != short.MaxValue)
                    preview.BuyCount -= (short)TargetContentList.Where(x => x.ItemSourceType - 10 == page).Sum(x => x.Amount);
            }
            return 0;
        }
    }
}
namespace GameData.Domains.World { public sealed class WorldDomain { public void AdvanceMonth(DataContext _) { } } }
namespace GameData.Utilities
{
    public static class CollectionExtensions { public static bool CheckIndex<T>(this T[] values, int index) => index >= 0 && index < values.Length; }
    public static class AdaptableLog
    {
        public static readonly List<string> Messages = new();
        public static void Info(string message) => Messages.Add(message);
        public static void Warning(string message, bool _) => Messages.Add(message);
        public static void Error(string message) => Messages.Add(message);
    }
}
