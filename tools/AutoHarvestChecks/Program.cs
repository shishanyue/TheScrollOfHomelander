using System.Reflection;
using BetterTaiwuScroll.Frontend;

int passed = 0;
int failed = 0;
string[] sources = { "sold", "items", "people", "feathers" };

foreach (bool saveWorld in new[] { false, true })
{
    Check($"month completion collects once when saveWorld={saveWorld}", () =>
    {
        FakeGame.SoldCount = 7;
        MarkMonth(saveWorld);
        Settle();
        Equal(1, Actions("sold"));
        Equal(4, Queries());
        FakeGame.Emit(EEvents.OnSavingWorldStateChange);
        FakeGame.Emit(UiEvents.MonthNotifyProcessComplete);
        Settle();
        Equal(1, Actions("sold"));
        Equal(4, Queries());
    });
}

Check("saving blocks collection until the save-complete event", () =>
{
    FakeGame.BasicData.SavingWorld = true;
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    StepFrame();
    Equal(0, Queries());
    FakeGame.BasicData.SavingWorld = false;
    FakeGame.Emit(EEvents.OnSavingWorldStateChange);
    Settle();
    Equal(1, Actions("sold"));
});

Check("long save exhausts polling but preserves pending work for the event", () =>
{
    FakeGame.BasicData.SavingWorld = true;
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    int checks = FakeGame.Frames.Drain(300);
    True(checks > 240, "the save must outlast the old polling limit");
    Equal(0, FakeGame.Frames.Count);
    Equal(0, Queries());
    True(State("_pending"), "long saving discarded the pending harvest");
    FakeGame.BasicData.SavingWorld = false;
    FakeGame.Emit(EEvents.OnSavingWorldStateChange);
    Settle();
    Equal(1, Actions("sold"));
    Equal(4, Queries());
});

Check("long month transition resumes after its state-change event", () =>
{
    FakeGame.BasicData.AdvancingMonthState = 7;
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    FakeGame.Frames.Drain(300);
    Equal(0, Queries());
    FakeGame.BasicData.AdvancingMonthState = 0;
    FakeGame.Emit(EEvents.OnAdvancingMonthStateChange);
    Settle();
    Equal(1, Actions("sold"));
});

Check("world load waits for InGame and then collects", () =>
{
    FakeGame.App.State = EGameState.Loading;
    FakeGame.SoldCount = 3;
    MarkWorldReady();
    FakeGame.Frames.Drain();
    Equal(0, Queries());
    True(State("_pending"));
    FakeGame.App.State = EGameState.InGame;
    FakeGame.Emit(EEvents.OnGameStateChange);
    Settle();
    Equal(1, Actions("sold"));
});

Check("disabled feature neither schedules month nor load harvest", () =>
{
    Plugin.EnableAutoHarvestAfterAdvanceMonth = false;
    FakeGame.SoldCount = 3;
    MarkMonth(false);
    MarkMonth(true);
    MarkWorldReady();
    FakeGame.Emit(EEvents.OnSavingWorldStateChange);
    Equal(0, FakeGame.Frames.Count);
    Settle();
    Equal(0, Queries());
    Equal(0, Actions());
});

Check("disabling while waiting cancels collection", () =>
{
    MarkMonth(true);
    Plugin.EnableAutoHarvestAfterAdvanceMonth = false;
    Settle();
    Equal(0, Queries());
    True(!State("_pending"));
});

Check("disabled availability replies cannot trigger consumption", () =>
{
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    StepFrame();
    Equal(4, Queries());
    Plugin.EnableAutoHarvestAfterAdvanceMonth = false;
    FakeGame.FlushCallbacks();
    Equal(0, Actions());
    AutoHarvestAfterAdvanceMonthController.Reset();
});

Check("reset cancels queued work", () =>
{
    MarkMonth(true);
    AutoHarvestAfterAdvanceMonthController.Reset();
    Settle();
    Equal(0, Queries());
    True(!State("_pending"));
});

Check("old delay after same-session reset cannot consume the new marker", () =>
{
    MarkMonth(true);
    AutoHarvestAfterAdvanceMonthController.Reset();
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    Equal(2, FakeGame.Frames.Count);
    StepFrame();
    Equal(0, Queries());
    True(State("_scheduled"), "old delay cleared the new scheduled check");
    True(State("_pending"));
    StepFrame();
    FakeGame.FlushCallbacks();
    Equal(1, Actions("sold"));
    Equal(4, Queries());
});

Check("old world-ready delay cannot consume a replacement world-ready marker", () =>
{
    MarkWorldReady();
    MarkWorldReady();
    StepFrame();
    Equal(0, Queries());
    True(State("_scheduled"));
    StepFrame();
    FakeGame.FlushCallbacks();
    Equal(4, Queries());
});

Check("old session delay cannot consume new-session work", () =>
{
    MarkMonth(true);
    ModSession.Generation++;
    AutoHarvestAfterAdvanceMonthController.Reset();
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    StepFrame();
    Equal(0, Queries());
    True(State("_scheduled"));
    StepFrame();
    FakeGame.FlushCallbacks();
    Equal(1, Actions("sold"));
});

Check("reset ignores stale availability replies while a new run is active", () =>
{
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    StepFrame();
    var staleReplies = FakeGame.Callbacks.ToArray();
    FakeGame.Callbacks.Clear();
    AutoHarvestAfterAdvanceMonthController.Reset();
    MarkMonth(true);
    StepFrame();
    foreach (var reply in staleReplies) reply();
    Equal(0, Actions());
    True(State("_running"));
    FakeGame.FlushCallbacks();
    Equal(1, Actions("sold"));
});

Check("new session ignores stale availability replies", () =>
{
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    StepFrame();
    ModSession.Generation++;
    FakeGame.FlushCallbacks();
    Equal(0, Actions());
    AutoHarvestAfterAdvanceMonthController.Reset();
});

Check("duplicate pending markers coalesce into one run", () =>
{
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    MarkMonth(false);
    MarkMonth(true);
    Equal(1, FakeGame.Frames.Count);
    Settle();
    Equal(4, Queries());
    Equal(1, Actions("sold"));
});

Check("duplicate marker while running cannot create a later extra run", () =>
{
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    StepFrame();
    MarkMonth(true);
    Equal(0, FakeGame.Frames.Count);
    FakeGame.FlushCallbacks();
    FakeGame.Emit(EEvents.OnSavingWorldStateChange);
    Settle();
    Equal(4, Queries());
    Equal(1, Actions("sold"));
    True(!State("_pending"));
});

Check("a later completed month can start a fresh run", () =>
{
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    Settle();
    FakeGame.SoldCount = 4;
    MarkMonth(true);
    Settle();
    Equal(8, Queries());
    Equal(2, Actions("sold"));
});

Check("empty availability makes no collection or UI actions", () =>
{
    MarkMonth(true);
    Settle();
    Equal(4, Queries());
    Equal(0, Actions());
    True(!State("_running"));
});

Check("all child switches disabled makes no queries or actions", () =>
{
    SetOnlySource(null);
    SetAllAvailable();
    MarkMonth(true);
    Settle();
    Equal(0, Queries());
    Equal(0, Actions());
    True(!State("_running"));
});

foreach (string source in sources)
{
    Check($"only enabled source {source} is queried and processed", () =>
    {
        SetOnlySource(source);
        SetAllAvailable();
        MarkMonth(true);
        Settle();
        Equal(1, Queries());
        Equal(1, FakeGame.QueryCount.GetValueOrDefault(source));
        foreach (string other in sources.Where(x => x != source))
            Equal(0, Actions(other));
        Equal(1, Actions(source == "people" ? "show-people" : source));
        Equal(source == "items" || source == "feathers" ? 1 : 0, Actions("show-items"));
    });
}

Check("feathers are not collected outside Taiwu Village", () =>
{
    SetOnlySource("feathers");
    FakeGame.HasFeathers = true;
    FakeGame.AtVillage = false;
    MarkMonth(true);
    Settle();
    Equal(1, Queries());
    Equal(0, Actions());
});

Check("items and feathers share one obtain view before recruiting", () =>
{
    SetAllAvailable();
    Plugin.EnableAutoHarvestCloseObtainView = true;
    MarkMonth(true);
    Settle();
    Equal(1, Actions("sold"));
    Equal(1, Actions("items"));
    Equal(1, Actions("feathers"));
    Equal(1, Actions("show-items"));
    Equal(2, FakeGame.ObtainView.Items.Count);
    True(FakeGame.ObtainView.InWareHouse);
    Equal(1, Actions("close-items"));
    Equal(1, Actions("show-people"));
});

Check("automatic recruiting keeps the existing background action", () =>
{
    SetOnlySource("people");
    FakeGame.PeopleCount = 2;
    Plugin.EnableAutoRecruitPeople = true;
    MarkMonth(true);
    Settle();
    Equal(1, Actions("recruit-people"));
    Equal(0, Actions("show-people"));
});

Check("availability timeout cancels without automatic retry", () =>
{
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    StepFrame();
    foreach (var timeout in FakeGame.Timeouts.ToArray()) timeout();
    True(FakeGame.Warnings.Any(x => x.Contains("timed out")));
    FakeGame.FlushCallbacks();
    FakeGame.Emit(EEvents.OnSavingWorldStateChange);
    Settle();
    Equal(4, Queries());
    Equal(0, Actions());
    True(!State("_running"));
});

Check("stale timeout does not cancel a replacement run", () =>
{
    MarkMonth(true);
    StepFrame();
    var oldTimeouts = FakeGame.Timeouts.ToArray();
    FakeGame.Callbacks.Clear();
    FakeGame.Timeouts.Clear();
    AutoHarvestAfterAdvanceMonthController.Reset();
    FakeGame.SoldCount = 3;
    MarkMonth(true);
    StepFrame();
    foreach (var timeout in oldTimeouts) timeout();
    True(State("_running"));
    Equal(0, FakeGame.Warnings.Count);
    FakeGame.FlushCallbacks();
    Equal(1, Actions("sold"));
});

AutoHarvestAfterAdvanceMonthController.Dispose();
Console.WriteLine($"AutoHarvestChecks: {passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

void Check(string name, Action test)
{
    AutoHarvestAfterAdvanceMonthController.Dispose();
    FakeGame.Reset();
    AutoHarvestAfterAdvanceMonthController.Initialize();
    try
    {
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine("FAIL " + name + ": " + ex.Message);
    }
}

static void MarkMonth(bool saveWorld) => typeof(AutoHarvestAfterAdvanceMonthMarkerPatch)
    .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!
    .Invoke(null, new object[] { saveWorld });

static void MarkWorldReady() => typeof(AutoHarvestAfterWorldLoadPatch)
    .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);

static bool State(string field) => (bool)typeof(AutoHarvestAfterAdvanceMonthController)
    .GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

static void StepFrame() => True(FakeGame.Frames.RunNext(), "expected a scheduled frame callback");

static void Settle()
{
    for (int round = 0; round < 20; round++)
    {
        FakeGame.Frames.Drain(300);
        FakeGame.FlushCallbacks();
        if (FakeGame.Frames.Count == 0 && FakeGame.Callbacks.Count == 0) return;
    }
    throw new InvalidOperationException("frame and bridge queues did not settle");
}

static int Queries() => FakeGame.QueryCount.Values.Sum();
static int Actions(string source = null) => source == null
    ? FakeGame.ActionCount.Values.Sum() : FakeGame.ActionCount.GetValueOrDefault(source);

static void SetOnlySource(string source)
{
    Plugin.EnableAutoHarvestAfterAdvanceMonthSoldItems = source == "sold";
    Plugin.EnableAutoHarvestAfterAdvanceMonthItems = source == "items";
    Plugin.EnableAutoHarvestAfterAdvanceMonthPeople = source == "people";
    Plugin.EnableAutoHarvestAfterAdvanceMonthFeathers = source == "feathers";
}

static void SetAllAvailable()
{
    FakeGame.SoldCount = 3;
    FakeGame.ItemCount = 4;
    FakeGame.PeopleCount = 2;
    FakeGame.HasFeathers = true;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected {expected}, got {actual}");
}

static void True(bool actual, string message = "expected true")
{
    if (!actual) throw new InvalidOperationException(message);
}
