using System.Reflection;
using BetterTaiwuScroll.Backend;
using GameData.Common;
using GameData.Domains;
using GameData.Domains.Item;
using GameData.Domains.Taiwu;

int passed = 0;
int failed = 0;
Check("empty trough falls through to warehouse", () =>
{
    FakeGame.AddChicken(1);
    var key = FakeGame.AddFeed(ItemSourceType.Warehouse);
    Run();
    Equal((sbyte)90, DomainManager.Building.Chickens[1].Happiness);
    Equal(2, FakeGame.NativeCalls);
    Equal(8, FakeGame.Count(key, ItemSourceType.Warehouse));
});
Check("empty trough and warehouse fall through to inventory", () =>
{
    FakeGame.AddChicken(1);
    var key = FakeGame.AddFeed(ItemSourceType.Inventory);
    Run(new() { UseInventory = true });
    Equal((sbyte)90, DomainManager.Building.Chickens[1].Happiness);
    Equal(8, FakeGame.Count(key, ItemSourceType.Inventory));
});
Check("no feed never invokes native feeding, despite valid default ItemKey", () =>
{
    True(default(ItemKey).IsValid());
    FakeGame.AddChicken(1);
    Run(new() { UseInventory = true });
    Equal(0, FakeGame.NativeCalls);
});
Check("first native chicken id zero is fed", () =>
{
    FakeGame.AddChicken(0);
    FakeGame.AddFeed(ItemSourceType.Trough);
    Run();
    Equal((sbyte)90, DomainManager.Building.Chickens[0].Happiness);
    Equal(2, FakeGame.NativeCalls);
});
Check("real feed item id zero remains selectable", () =>
{
    FakeGame.AddChicken(1, happiness: 80);
    var key = FakeGame.AddFeed(ItemSourceType.Trough, count: 1, id: 0);
    Run();
    Equal((sbyte)90, DomainManager.Building.Chickens[1].Happiness);
    Equal(key, FakeGame.Feeds.Single().Item);
    Equal(0, FakeGame.Count(key, ItemSourceType.Trough));
});
Check("missing id does not fabricate a default chicken", () =>
{
    FakeGame.AddChicken(0, template: 0, settlement: 8);
    DomainManager.Building.SettlementIds.Add(123);
    FakeGame.AddFeed(ItemSourceType.Trough);
    Run();
    Equal(0, FakeGame.NativeCalls);
    Equal((sbyte)70, DomainManager.Building.Chickens[0].Happiness);
});
Check("threshold stops feeding with exact warehouse debit", () =>
{
    FakeGame.AddChicken(1, happiness: 75);
    var key = FakeGame.AddFeed(ItemSourceType.Warehouse, count: 7);
    Run(new() { UseTrough = false, HappinessThreshold = 90 });
    Equal((sbyte)95, DomainManager.Building.Chickens[1].Happiness);
    Equal(2, FakeGame.NativeCalls);
    Equal(2, FakeGame.Removals.Count);
    Equal(5, FakeGame.Count(key, ItemSourceType.Warehouse));
});
Check("warehouse feed requests deletion of consumed item instances", () =>
{
    FakeGame.AddChicken(1, happiness: 80);
    FakeGame.AddFeed(ItemSourceType.Warehouse, count: 1);
    Run(new() { UseTrough = false });
    True(FakeGame.Removals.Single().DeleteItem);
});
Check("sources drain in trough, warehouse, inventory order", () =>
{
    FakeGame.AddChicken(1, happiness: 50);
    FakeGame.AddFeed(ItemSourceType.Trough, count: 1);
    FakeGame.AddFeed(ItemSourceType.Warehouse, count: 2, template: 2, id: 2);
    FakeGame.AddFeed(ItemSourceType.Inventory, count: 5, template: 3, id: 3);
    Run(new() { UseInventory = true });
    Equal("Trough,Warehouse,Warehouse,Inventory", string.Join(',', FakeGame.Feeds.Select(feed => feed.Source)));
    Equal(4, FakeGame.Removals.Count);
});
Check("disabled sources remain untouched", () =>
{
    FakeGame.AddChicken(1);
    var trough = FakeGame.AddFeed(ItemSourceType.Trough);
    var warehouse = FakeGame.AddFeed(ItemSourceType.Warehouse, template: 2, id: 2);
    var inventory = FakeGame.AddFeed(ItemSourceType.Inventory, template: 3, id: 3);
    Run(new() { UseTrough = false, UseWarehouse = false, UseInventory = true });
    Equal(10, FakeGame.Count(trough, ItemSourceType.Trough));
    Equal(10, FakeGame.Count(warehouse, ItemSourceType.Warehouse));
    Equal(8, FakeGame.Count(inventory, ItemSourceType.Inventory));
});
Check("zero-count and non-feed items are ignored before lower grade selection", () =>
{
    FakeGame.AddChicken(1, happiness: 80);
    FakeGame.AddFeed(ItemSourceType.Trough, count: 0, template: 1, grade: 0, id: 1);
    var notFeed = FakeGame.AddFeed(ItemSourceType.Trough, template: 2, grade: 0, id: 2);
    FakeGame.Feedable.Remove(notFeed);
    FakeGame.AddFeed(ItemSourceType.Trough, template: 3, grade: 3, id: 3);
    var lowGrade = FakeGame.AddFeed(ItemSourceType.Trough, template: 4, grade: 1, id: 4);
    Run();
    Equal(lowGrade, FakeGame.Feeds.Single().Item);
});
Check("unknown Fulong event state feeds ordinary chickens but excludes king", () =>
{
    FakeGame.AddChicken(1);
    FakeGame.AddChicken(2, template: 63);
    DomainManager.Story.Status = 0;
    DomainManager.Extra.EventBox = null;
    FakeGame.AddFeed(ItemSourceType.Trough);
    Run();
    Equal((sbyte)90, DomainManager.Building.Chickens[1].Happiness);
    Equal((sbyte)70, DomainManager.Building.Chickens[2].Happiness);
    Equal(2, FakeGame.NativeCalls);
});
Check("known Fulong absence excludes king", () =>
{
    FakeGame.AddChicken(1);
    FakeGame.AddChicken(2, template: 63);
    DomainManager.Story.Status = 0;
    DomainManager.Extra.EventBox.KingAway = true;
    FakeGame.AddFeed(ItemSourceType.Trough);
    Run();
    Equal((sbyte)90, DomainManager.Building.Chickens[1].Happiness);
    Equal((sbyte)70, DomainManager.Building.Chickens[2].Happiness);
});
Check("known Fulong presence permits king", () =>
{
    FakeGame.AddChicken(1, template: 63);
    DomainManager.Story.Status = 0;
    FakeGame.AddFeed(ItemSourceType.Trough);
    Run();
    Equal((sbyte)90, DomainManager.Building.Chickens[1].Happiness);
});
Check("duplicate templates do not feed the wrong chicken or drain feed", () =>
{
    FakeGame.AddChicken(1, happiness: 90);
    FakeGame.AddChicken(2);
    var key = FakeGame.AddFeed(ItemSourceType.Trough);
    Run();
    Equal(0, FakeGame.NativeCalls);
    Equal(10, FakeGame.Count(key, ItemSourceType.Trough));
    Equal((sbyte)70, DomainManager.Building.Chickens[2].Happiness);
});
Check("template resolving outside settlement is rejected", () =>
{
    FakeGame.AddChicken(9, settlement: 8);
    FakeGame.AddChicken(1);
    FakeGame.AddFeed(ItemSourceType.Trough);
    Run();
    Equal(0, FakeGame.NativeCalls);
    Equal((sbyte)70, DomainManager.Building.Chickens[9].Happiness);
});
Check("positive return without target progress stops after first feed", () =>
{
    FakeGame.AddChicken(1);
    FakeGame.AddFeed(ItemSourceType.Trough, count: 100);
    FakeGame.ChangeHappiness = false;
    Run();
    Equal(1, FakeGame.NativeCalls);
    Equal((sbyte)70, DomainManager.Building.Chickens[1].Happiness);
});
Check("zero native gain stops after first attempt", () =>
{
    FakeGame.AddChicken(1);
    FakeGame.AddFeed(ItemSourceType.Trough);
    FakeGame.ReturnHappiness = 0;
    Run();
    Equal(1, FakeGame.NativeCalls);
});
Check("removed chicken is not followed through stale id", () =>
{
    FakeGame.AddChicken(1);
    FakeGame.AddFeed(ItemSourceType.Trough);
    FakeGame.AfterFeed = id => DomainManager.Building.Chickens.Remove(id);
    Run();
    Equal(1, FakeGame.NativeCalls);
});
Check("chicken transferred during feeding stops at settlement boundary", () =>
{
    FakeGame.AddChicken(1);
    FakeGame.AddFeed(ItemSourceType.Trough);
    FakeGame.AfterFeed = id =>
    {
        var chicken = DomainManager.Building.Chickens[id];
        chicken.CurrentSettlementId = 8;
        DomainManager.Building.Chickens[id] = chicken;
    };
    Run();
    Equal(1, FakeGame.NativeCalls);
    Equal((sbyte)80, DomainManager.Building.Chickens[1].Happiness);
});
Check("disabled setting preserves inventory and happiness", () =>
{
    FakeGame.AddChicken(1);
    FakeGame.AddFeed(ItemSourceType.Trough);
    Run(new() { Enabled = false });
    Equal(0, FakeGame.NativeCalls);
    Equal((sbyte)70, DomainManager.Building.Chickens[1].Happiness);
});
Check("already at threshold is a no-op", () =>
{
    FakeGame.AddChicken(1, happiness: 90);
    FakeGame.AddFeed(ItemSourceType.Trough);
    Run();
    Equal(0, FakeGame.NativeCalls);
});
Check("zero threshold and no sources never feed a hungry chicken", () =>
{
    FakeGame.AddChicken(1);
    FakeGame.AddFeed(ItemSourceType.Trough);
    Run(new() { HappinessThreshold = 0 });
    Run(new() { UseTrough = false, UseWarehouse = false });
    Equal(0, FakeGame.NativeCalls);
});
Check("missing settlement is a safe no-op", () =>
{
    FakeGame.AddChicken(1);
    FakeGame.AddFeed(ItemSourceType.Trough);
    DomainManager.Organization.SettlementExists = false;
    Run();
    Equal(0, FakeGame.NativeCalls);
});
Check("cricket enters room before monthly recovery and returns to inventory", () =>
{
    var key = FakeGame.AddCricket(101);
    var room = FakeGame.AddCricketRoom();
    RunCricketMonth();
    Equal((short)2, DomainManager.Item.Crickets[key.Id].Durability);
    Equal(1, FakeGame.Count(key, ItemSourceType.Inventory));
    Equal(ItemKey.Invalid, room.Cricket);
    Equal(key, FakeGame.CricketStores.Single().Item);
    Equal(key, FakeGame.CricketRetrievals.Single().Item);
    Equal((short)1100, DomainManager.Character.RequestedSubTypes.Single());
    Equal(false, FakeGame.Removals.Single().DeleteItem);
});
Check("player-stored cricket remains while automatically stored cricket returns", () =>
{
    var playerKey = FakeGame.AddCricket(101);
    var playerRoom = FakeGame.AddCricketRoom();
    FakeGame.PutCricketInRoom(playerKey, playerRoom);
    var autoKey = FakeGame.AddCricket(102);
    var autoRoom = FakeGame.AddCricketRoom();
    RunCricketMonth();
    Equal(playerKey, playerRoom.Cricket);
    Equal(0, FakeGame.Count(playerKey, ItemSourceType.Inventory));
    Equal(1, FakeGame.Count(autoKey, ItemSourceType.Inventory));
    Equal(ItemKey.Invalid, autoRoom.Cricket);
    Equal(autoKey, FakeGame.CricketRetrievals.Single().Item);
});
Check("explicit player-stored retrieval returns that cricket even with no free slot", () =>
{
    var key = FakeGame.AddCricket(101);
    var room = FakeGame.AddCricketRoom();
    FakeGame.PutCricketInRoom(key, room);
    RunCricketMonth(new() { RetrievePlayerStored = true });
    Equal(0, FakeGame.CricketStores.Count);
    Equal(key, FakeGame.CricketRetrievals.Single().Item);
    Equal(1, FakeGame.Count(key, ItemSourceType.Inventory));
});
Check("no jar leaves carried cricket untouched", () =>
{
    var key = FakeGame.AddCricket(101);
    FakeGame.AddCricketRoom(withJar: false);
    RunCricketMonth();
    Equal(0, FakeGame.CricketStores.Count);
    Equal(1, FakeGame.Count(key, ItemSourceType.Inventory));
});
Check("dead and full-durability crickets are not stored", () =>
{
    var dead = FakeGame.AddCricket(101, alive: false);
    var full = FakeGame.AddCricket(102, durability: 3);
    FakeGame.AddCricketRoom();
    FakeGame.AddCricketRoom();
    RunCricketMonth();
    Equal(0, FakeGame.CricketStores.Count);
    Equal(1, FakeGame.Count(dead, ItemSourceType.Inventory));
    Equal(1, FakeGame.Count(full, ItemSourceType.Inventory));
});
Check("cricket storage prioritizes lowest durability ratio then highest grade", () =>
{
    FakeGame.AddCricket(101, durability: 2, maximum: 4, grade: 8);
    FakeGame.AddCricket(102, durability: 1, maximum: 4, grade: 3);
    var expected = FakeGame.AddCricket(103, durability: 1, maximum: 4, grade: 6);
    FakeGame.AddCricketRoom();
    RunCricketMonth();
    Equal(expected, FakeGame.CricketStores.Single().Item);
});
Check("disabled auto-retrieval keeps cricket in its room", () =>
{
    var key = FakeGame.AddCricket(101);
    var room = FakeGame.AddCricketRoom();
    RunCricketMonth(new() { RetrieveAutomaticallyStored = false });
    Equal(key, room.Cricket);
    Equal(0, FakeGame.Count(key, ItemSourceType.Inventory));
    Equal(0, FakeGame.CricketRetrievals.Count);
});
Check("month finalizer recovers automatically stored cricket and preserves exception", () =>
{
    var key = FakeGame.AddCricket(101);
    var room = FakeGame.AddCricketRoom();
    var expected = new InvalidOperationException("Simulated native month failure.");
    var actual = RunCricketMonth(monthFailure: expected);
    True(ReferenceEquals(expected, actual));
    Equal(key, FakeGame.CricketRetrievals.Single().Item);
    Equal(1, FakeGame.Count(key, ItemSourceType.Inventory));
    Equal(ItemKey.Invalid, room.Cricket);
});
Check("finalizer after completed postfix does not duplicate retrieved cricket", () =>
{
    var key = FakeGame.AddCricket(101);
    FakeGame.AddCricketRoom();
    RunCricketMonth();
    Equal(1, FakeGame.CricketRetrievals.Count);
    Equal(1, FakeGame.Count(key, ItemSourceType.Inventory));
});
Check("native retrieval fallback sends cricket to warehouse when transfer is unavailable", () =>
{
    var key = FakeGame.AddCricket(101);
    FakeGame.AddCricketRoom();
    DomainManager.Taiwu.CanTransferToWarehouse = false;
    RunCricketMonth();
    Equal(0, FakeGame.Count(key, ItemSourceType.Inventory));
    Equal(1, FakeGame.Count(key, ItemSourceType.Warehouse));
    Equal(ItemSourceType.Warehouse, FakeGame.CricketRetrievals.Single().Target);
});

Console.WriteLine($"Passed {passed}; failed {failed} production-patch regression checks.");
return failed == 0 ? 0 : 1;

void Run(AutoChickenCareSettings settings = null)
{
    // Construct the existing month state and invoke Postfix directly. Prefix is
    // intentionally excluded because it reads the user's persisted settings.
    var patch = typeof(AutoCultivationAdvanceMonthPatch);
    var stateType = patch.GetNestedType("MonthState", BindingFlags.NonPublic);
    var state = Activator.CreateInstance(stateType, true);
    stateType.GetField("OnIndustryTile", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, true);
    stateType.GetField("SettlementLocation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, FakeGame.Home);
    stateType.GetField("ChickenSettings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, settings ?? new());
    patch.GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { new DataContext(), state });
    var errors = GameData.Utilities.AdaptableLog.Messages.Where(message => message.Contains("Auto chicken care failed:")).ToList();
    if (errors.Count > 0) throw new Exception(string.Join(Environment.NewLine, errors));
}

Exception RunCricketMonth(AutoCricketRoomSettings settings = null, Exception monthFailure = null)
{
    var patch = typeof(AutoCultivationAdvanceMonthPatch);
    var stateType = patch.GetNestedType("MonthState", BindingFlags.NonPublic);
    var state = Activator.CreateInstance(stateType, true);
    var context = new DataContext();
    stateType.GetField("OnIndustryTile", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, true);
    stateType.GetField("SettlementLocation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, FakeGame.Home);
    stateType.GetField("CricketSettings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, settings ?? new());
    // Exercise production storage and restore around a narrow native month double;
    // calling Prefix would read the user's persisted settings and is excluded.
    patch.GetMethod("StoreCrickets", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { context, state });
    if (monthFailure == null)
    {
        DomainManager.Building.UpdateCricketRoom(context);
        patch.GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { context, state });
    }
    var result = (Exception)patch.GetMethod("Finalizer", BindingFlags.NonPublic | BindingFlags.Static)
        .Invoke(null, new object[] { context, state, monthFailure });
    var errors = GameData.Utilities.AdaptableLog.Messages.Where(message => message.Contains("Failed to restore auto-stored crickets:")).ToList();
    if (errors.Count > 0) throw new Exception(string.Join(Environment.NewLine, errors));
    return result;
}

void Check(string name, Action test)
{
    FakeGame.Reset();
    try { test(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.Message); failed++; }
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}; got {actual}.");
}
static void True(bool value) { if (!value) throw new Exception("Expected true."); }
