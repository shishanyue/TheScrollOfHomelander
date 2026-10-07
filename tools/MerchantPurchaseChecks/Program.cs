using BetterTaiwuScroll.Backend;
using GameData.Common;
using GameData.Domains;
using GameData.Domains.Taiwu.Display;
using GameData.Utilities;

int passed = 0;
Check("all merchant companions, including automatic occupation", () =>
{
    FakeGame.AddMerchant(11, new FakeItem(1));
    FakeGame.AddMerchant(12, new FakeItem(2));
    FakeGame.AddMerchant(13, new FakeItem(3));
    FakeGame.DesignatedTypes[12] = 7;
    FakeGame.DesignatedTypes.Remove(13);
    Run();
    Equal("11,12,13", string.Join(',', FakeGame.Purchases.Select(x => x.Merchant)));
});
Check("empty first shop still visits later merchant", () =>
{
    FakeGame.AddMerchant(11);
    FakeGame.AddMerchant(12, new FakeItem(2));
    Run();
    Equal("11,12", string.Join(',', FakeGame.Visits));
    Equal(12, FakeGame.Purchases.Single().Merchant);
});
Check("native resolver restores a missing merchant identity map", () =>
{
    FakeGame.AddMerchant(11, new FakeItem(1));
    DomainManager.Extra.Types.Remove(11);
    Run();
    Equal(11, FakeGame.Purchases.Single().Merchant);
    True(DomainManager.Extra.Types.ContainsKey(11));
});
Check("ordinary companion never gets a generated shop", () =>
{
    DomainManager.Taiwu.Group.Ids.Add(11);
    DomainManager.Character.Characters[11] = new();
    Run();
    Equal(0, FakeGame.Visits.Count);
    Equal(0, FakeGame.Purchases.Count);
});
Check("shop failure is isolated to one merchant", () =>
{
    FakeGame.AddMerchant(11);
    FakeGame.Shops[11] = () => throw new InvalidOperationException("broken shop");
    FakeGame.AddMerchant(12, new FakeItem(2));
    Run();
    Equal(12, FakeGame.Purchases.Single().Merchant);
    True(AdaptableLog.Messages.Any(x => x.Contains("failedMerchants=1")));
});
Check("heavy item does not stop later light merchant", () =>
{
    FakeGame.Load = 85;
    FakeGame.AddMerchant(11, new FakeItem(1, weight: 6));
    FakeGame.AddMerchant(12, new FakeItem(2, count: 2, weight: 2));
    Run(inventory: true);
    Equal(12, FakeGame.Purchases.Single().Merchant);
    Equal(89, FakeGame.Load);
});
Check("partial heavy stack preserves remaining space", () =>
{
    FakeGame.Load = 80;
    FakeGame.AddMerchant(11, new FakeItem(1, count: 3, weight: 6));
    FakeGame.AddMerchant(12, new FakeItem(2, count: 2, weight: 2));
    Run(inventory: true);
    Equal("11:1,12:2", string.Join(',', FakeGame.Purchases.Select(x => $"{x.Merchant}:{x.Count}")));
    Equal(90, FakeGame.Load);
});
Check("true full inventory stops further shops", () =>
{
    FakeGame.Load = 89;
    FakeGame.AddMerchant(11, new FakeItem(1));
    FakeGame.AddMerchant(12, new FakeItem(2));
    Run(inventory: true);
    Equal("11", string.Join(',', FakeGame.Visits));
});
Check("expensive first shop does not prevent affordable later shop", () =>
{
    FakeGame.Money = 5;
    FakeGame.AddMerchant(11, new FakeItem(1, price: 10));
    FakeGame.AddMerchant(12, new FakeItem(2, count: 3, price: 2));
    Run();
    Equal(12, FakeGame.Purchases.Single().Merchant);
    Equal(2, FakeGame.Purchases.Single().Count);
    Equal(1L, FakeGame.Money);
});
Check("zero funds skips trades without aborting merchant traversal", () =>
{
    FakeGame.Money = 0;
    FakeGame.AddMerchant(11, new FakeItem(1));
    FakeGame.AddMerchant(12, new FakeItem(2));
    Run();
    Equal(0, FakeGame.Purchases.Count);
    Equal("11,12", string.Join(',', FakeGame.Visits));
});
Check("trade callback mutating group does not invalidate enumeration", () =>
{
    FakeGame.AddMerchant(11, new FakeItem(1));
    FakeGame.AddMerchant(12, new FakeItem(2));
    FakeGame.AfterPurchase = id => { if (id == 11) FakeGame.AddMerchant(13, new FakeItem(3)); };
    Run();
    Equal("11,12", string.Join(',', FakeGame.Visits));
});
Check("departed snapshot companion is revalidated", () =>
{
    FakeGame.AddMerchant(11, new FakeItem(1));
    FakeGame.AddMerchant(12, new FakeItem(2));
    FakeGame.AddMerchant(13, new FakeItem(3));
    FakeGame.AfterPurchase = id => { if (id == 11) DomainManager.Taiwu.Group.Ids.Remove(12); };
    Run();
    Equal("11,13", string.Join(',', FakeGame.Visits));
});
Check("limited-purchase debt is consumed on a cloned preview", () =>
{
    FakeGame.AddMerchant(11);
    ShopDisplayData shop = FakeGame.Shop(11, new FakeItem(1, count: 5, page: 1));
    shop.Exchange.MerchantOverFavorData.MerchantOverFavorLevelDataArray[1].BuyCount = 2;
    FakeGame.Shops[11] = () => shop;
    Run(includeLimited: true);
    Equal(2, FakeGame.Purchases.Single().Count);
    Equal((short)2, shop.Exchange.MerchantOverFavorData.MerchantOverFavorLevelDataArray[1].BuyCount);
    Equal((short)0, shop.Exchange.TradeArguments.OverFavorData.MerchantOverFavorLevelDataArray[1].BuyCount);
});
Check("disabled feature preserves native behavior", () =>
{
    FakeGame.AddMerchant(11, new FakeItem(1));
    AutoMonthlyPurchaseService.Run(new DataContext(), new AutoMonthlyPurchaseSettings());
    Equal(0, FakeGame.Visits.Count);
});
Console.WriteLine($"Passed {passed} production-service regression checks.");

void Run(bool inventory = false, bool includeLimited = false)
{
    var settings = new AutoMonthlyPurchaseSettings
    {
        AutoPurchaseGroupMerchants = true,
        AutoPurchaseTargetSource = inventory ? 0 : 1,
        IncludeLimitedPurityLockedLevels = includeLimited,
    };
    settings.Normalize();
    AutoMonthlyPurchaseService.Run(new DataContext(), settings);
}
void Check(string name, Action test)
{
    FakeGame.Reset();
    try { test(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { throw new Exception("FAIL " + name, ex); }
}
static void Equal<T>(T expected, T actual)
{ if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
static void True(bool value) { if (!value) throw new Exception("Expected true."); }
