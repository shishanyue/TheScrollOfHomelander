// Offline API doubles for the linked production harvest controller. No game
// assemblies, saves, user settings, wall-clock timers, or Unity process are used.
using System;
using System.Collections.Generic;
using System.Reflection;
using FrameWork;
using FrameWork.UISystem.UIElements;
using Game.Views.Obtain;
using GameData.Domains.Item.Display;
using GameData.Utilities;

internal static class FakeGame
{
    internal static readonly FrameQueue Frames = new();
    internal static readonly Queue<Action> Callbacks = new();
    internal static readonly List<Action> Timeouts = new();
    internal static readonly Dictionary<string, int> QueryCount = new();
    internal static readonly Dictionary<string, int> ActionCount = new();
    internal static readonly List<string> Warnings = new();
    internal static BasicGameData BasicData = new();
    internal static GameApp App = new();
    internal static ViewObtain ObtainView => UIElement.GetItem.View;
    internal static int SoldCount;
    internal static int ItemCount;
    internal static int PeopleCount;
    internal static bool HasFeathers;
    internal static bool AtVillage;

    internal static void Reset()
    {
        Frames.Clear();
        Callbacks.Clear();
        Timeouts.Clear();
        QueryCount.Clear();
        ActionCount.Clear();
        Warnings.Clear();
        GEvent.Clear();
        BasicData = new BasicGameData { SavingWorld = false, AdvancingMonthState = 0 };
        App = new GameApp { State = EGameState.InGame };
        UIElement.GetItem.Reset();
        SoldCount = ItemCount = PeopleCount = 0;
        HasFeathers = false;
        AtVillage = true;
        BetterTaiwuScroll.Frontend.ModSession.Generation = 1;
        BetterTaiwuScroll.Frontend.ModSession.Active = true;
        BetterTaiwuScroll.Frontend.Plugin.EnableAutoHarvestAfterAdvanceMonth = true;
        BetterTaiwuScroll.Frontend.Plugin.EnableAutoHarvestAfterAdvanceMonthSoldItems = true;
        BetterTaiwuScroll.Frontend.Plugin.EnableAutoHarvestAfterAdvanceMonthItems = true;
        BetterTaiwuScroll.Frontend.Plugin.EnableAutoHarvestAfterAdvanceMonthPeople = true;
        BetterTaiwuScroll.Frontend.Plugin.EnableAutoHarvestAfterAdvanceMonthFeathers = true;
        BetterTaiwuScroll.Frontend.Plugin.EnableAutoHarvestCloseObtainView = false;
        BetterTaiwuScroll.Frontend.Plugin.EnableAutoRecruitPeople = false;
    }

    internal static void Emit(object eventId) => GEvent.OnEvent(eventId);

    internal static int FlushCallbacks(int max = 1000)
    {
        if (max < 0) throw new ArgumentOutOfRangeException(nameof(max));
        var executed = 0;
        while (Callbacks.Count > 0 && executed < max)
        {
            Callbacks.Dequeue()();
            executed++;
        }
        if (Callbacks.Count > 0)
            throw new InvalidOperationException("The offline callback queue did not settle within its limit.");
        return executed;
    }

    internal static void Record(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    internal static void Query(string key, object result, Action<int, RawDataPool> callback)
    {
        Record(QueryCount, key);
        Reply(result, callback);
    }

    internal static void Reply(object result, Action<int, RawDataPool> callback)
    {
        // Capture the reply separately from dispatch so tests can deliver stale or
        // out-of-order responses explicitly after a world/session transition.
        var pool = new RawDataPool(result);
        Callbacks.Enqueue(() => callback(0, pool));
    }

    internal sealed class FrameQueue
    {
        private readonly Queue<Action> _queue = new();
        internal int Count => _queue.Count;
        internal void Enqueue(Action action) => _queue.Enqueue(action);
        internal void Clear() => _queue.Clear();

        internal bool RunNext()
        {
            if (_queue.Count == 0) return false;
            _queue.Dequeue()();
            return true;
        }

        internal int Drain(int max = 1000)
        {
            if (max < 0) throw new ArgumentOutOfRangeException(nameof(max));
            var executed = 0;
            while (_queue.Count > 0 && executed < max)
            {
                RunNext();
                executed++;
            }
            if (_queue.Count > 0)
                throw new InvalidOperationException("The offline frame queue did not settle within its limit.");
            return executed;
        }
    }
}

internal enum EGameState { InGame, Loading, Menu }
internal enum EEvents { OnSavingWorldStateChange, OnAdvancingMonthStateChange, OnGameStateChange }
internal enum UiEvents { MonthNotifyProcessComplete, OnUpdateQuickBtnState, UpdateAllBlockInfo }

internal sealed class BasicGameData
{
    internal bool SavingWorld;
    internal int AdvancingMonthState;
}

internal sealed class GameApp
{
    internal static GameApp Instance { get => FakeGame.App; set => FakeGame.App = value; }
    internal EGameState State = EGameState.InGame;
    internal EGameState GetCurrentGameStateName() => State;
}

internal sealed class GlobalOperations { public void OnWorldDataReady() { } }
internal sealed class WorldMapModel
{
    public bool IsAtTaiwuVillage(int areaId, int blockId) => FakeGame.AtVillage;
}

namespace BetterTaiwuScroll.Frontend
{
    internal static class Plugin
    {
        internal static bool EnableAutoHarvestAfterAdvanceMonth;
        internal static bool EnableAutoHarvestAfterAdvanceMonthSoldItems;
        internal static bool EnableAutoHarvestAfterAdvanceMonthItems;
        internal static bool EnableAutoHarvestAfterAdvanceMonthPeople;
        internal static bool EnableAutoHarvestAfterAdvanceMonthFeathers;
        internal static bool EnableAutoHarvestCloseObtainView;
        internal static bool EnableAutoRecruitPeople;
    }

    internal static class ModSession
    {
        internal static int Generation { get; set; }
        internal static bool Active = true;
        internal static bool IsCurrent(int generation) => Active && Generation == generation;
        internal static void Delay(float seconds, Action action)
        {
            var generation = Generation;
            if (Active) FakeGame.Timeouts.Add(() => { if (IsCurrent(generation)) action(); });
        }
    }

    internal static class RecruitPeopleAutoActionSupport
    {
        internal static void StartBackgroundAutoProcess()
        {
            FakeGame.Record(FakeGame.ActionCount, "recruit-people");
            FakeGame.Record(FakeGame.ActionCount, "people");
            FakeGame.PeopleCount = 0;
        }
    }
}

namespace FrameWork
{
    internal static class SingletonObject
    {
        private static readonly YieldHelper Yield = new();
        private static readonly WorldMapModel WorldMap = new();
        internal static T getInstance<T>() where T : class
        {
            if (typeof(T) == typeof(BasicGameData)) return FakeGame.BasicData as T;
            if (typeof(T) == typeof(YieldHelper)) return Yield as T;
            if (typeof(T) == typeof(WorldMapModel)) return WorldMap as T;
            throw new InvalidOperationException("Unexpected offline singleton: " + typeof(T).FullName);
        }
    }

    internal sealed class YieldHelper
    {
        public void DelayFrameDo(uint frames, Action action) => FakeGame.Frames.Enqueue(action);
    }

    internal sealed class ArgumentBox
    {
        private readonly Dictionary<string, object> _values = new();
        public void SetObject(string key, object value) => _values[key] = value;
        public void Set(string key, bool arg) => _values[key] = arg;
        internal T Get<T>(string key) => _values.TryGetValue(key, out var value) ? (T)value : default;
    }

    internal static class EasyPool
    {
        public static T Get<T>() where T : new() => new();
    }

    internal static class GEvent
    {
        private static readonly Dictionary<object, Action<ArgumentBox>> Handlers = new();
        public static void Add(object eventId, Action<ArgumentBox> handler)
        {
            Handlers.TryGetValue(eventId, out var previous);
            Handlers[eventId] = previous + handler;
        }
        public static void Remove(object eventId, Action<ArgumentBox> handler)
        {
            if (!Handlers.TryGetValue(eventId, out var previous)) return;
            var remaining = previous - handler;
            if (remaining == null) Handlers.Remove(eventId);
            else Handlers[eventId] = remaining;
        }
        public static void OnEvent(object eventId)
        {
            if (Handlers.TryGetValue(eventId, out var handlers)) handlers(new ArgumentBox());
        }
        internal static void Clear() => Handlers.Clear();
    }

    internal sealed class UIManager
    {
        public static UIManager Instance { get; } = new();
        public void MaskUI(FakeUiElement element)
        {
            FakeGame.Record(FakeGame.ActionCount, "show-items");
            element.Show();
        }
    }
}

namespace FrameWork.UISystem.UIElements
{
    internal static class UIElement
    {
        public static FakeUiElement GetItem { get; } = new();
    }

    internal sealed class FakeUiElement
    {
        private ArgumentBox _arguments;
        internal ViewObtain View { get; private set; } = new();
        public void SetOnInitArgs(ArgumentBox arguments) => _arguments = arguments;
        public T UiBaseAs<T>() where T : class => View as T;
        internal void Show() => View.Initialize(_arguments);
        internal void Reset() { _arguments = null; View = new ViewObtain(); }
    }
}

namespace Game.Views.Obtain
{
    internal sealed class ViewObtain
    {
        private List<ItemDisplayData> _itemList;
        private Action _closeAction;
        public UnityEngine.GameObject gameObject { get; } = new();
        internal List<ItemDisplayData> Items => _itemList;
        internal bool InWareHouse { get; private set; }
        internal void Initialize(ArgumentBox arguments)
        {
            _itemList = arguments.Get<List<ItemDisplayData>>("ItemList");
            _closeAction = arguments.Get<Action>("CloseAction");
            InWareHouse = arguments.Get<bool>("InWareHouse");
            gameObject.activeInHierarchy = true;
        }
        private void QuickHide()
        {
            if (!gameObject.activeInHierarchy) return;
            gameObject.activeInHierarchy = false;
            FakeGame.Record(FakeGame.ActionCount, "close-items");
            var closeAction = _closeAction;
            _closeAction = null;
            closeAction?.Invoke();
        }
        internal void Close() => QuickHide();
    }
}

namespace Game.Views.Building
{
    internal static class UI_RecruitPeopleOverview
    {
        public static void EntryFromBuildingArea() => FakeGame.Record(FakeGame.ActionCount, "show-people");
    }
}

namespace GameData.Domains.Building
{
    internal static class BuildingDomainMethod
    {
        internal static class Call
        {
            public static void QuickCollectShopSoldItem()
            {
                FakeGame.Record(FakeGame.ActionCount, "sold");
                FakeGame.SoldCount = 0;
            }
        }

        internal static class AsyncCall
        {
            public static void QuickCollectShopSoldItemCount(object owner, Action<int, RawDataPool> callback) =>
                FakeGame.Query("sold", FakeGame.SoldCount, callback);
            public static void QuickCollectShopItemCount(object owner, Action<int, RawDataPool> callback) =>
                FakeGame.Query("items", FakeGame.ItemCount, callback);
            public static void QuickRecruitPeopleCount(object owner, Action<int, RawDataPool> callback) =>
                FakeGame.Query("people", FakeGame.PeopleCount, callback);
            public static void IsAnyChickensCanPluck(object owner, Action<int, RawDataPool> callback) =>
                FakeGame.Query("feathers", FakeGame.HasFeathers, callback);
            public static void PluckAllChickenFeathers(object owner, Action<int, RawDataPool> callback)
            {
                FakeGame.Record(FakeGame.ActionCount, "feathers");
                FakeGame.HasFeathers = false;
                FakeGame.Reply(new List<ItemDisplayData> { new() { Source = "feathers" } }, callback);
            }
            public static void QuickCollectShopItem(object owner, Action<int, RawDataPool> callback)
            {
                FakeGame.Record(FakeGame.ActionCount, "items");
                FakeGame.ItemCount = 0;
                FakeGame.Reply(new List<ItemDisplayData> { new() { Source = "items" } }, callback);
            }
        }
    }
}

namespace GameData.Domains.World
{
    internal static class WorldDomainMethod
    {
        internal static class Call { public static void AdvanceMonth_DisplayedMonthlyNotifications(bool saveWorld) { } }
    }
}

namespace GameData.Domains.Item.Display
{
    internal sealed class ItemDisplayData { internal string Source { get; init; } }
}

namespace GameData.Utilities
{
    internal sealed class RawDataPool
    {
        internal RawDataPool(object value) => Value = value;
        internal object Value { get; }
    }
}

namespace GameData.Serializer
{
    internal static class Serializer
    {
        public static void Deserialize<T>(RawDataPool pool, int offset, ref T value) => value = (T)pool.Value;
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    internal sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(Type type, string method, Type[] parameters) { }
    }

    internal sealed class Traverse
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly object _instance;
        private FieldInfo _field;
        private MethodInfo _method;
        private Traverse(object instance) => _instance = instance;
        public static Traverse Create(object instance) => new(instance);
        public Traverse Field(string name)
        {
            _field = _instance.GetType().GetField(name, Flags)
                ?? throw new MissingFieldException(_instance.GetType().FullName, name);
            return this;
        }
        public Traverse Method(string name)
        {
            _method = _instance.GetType().GetMethod(name, Flags)
                ?? throw new MissingMethodException(_instance.GetType().FullName, name);
            return this;
        }
        public T GetValue<T>() => (T)GetValue();
        public object GetValue() => _field != null ? _field.GetValue(_instance) : _method.Invoke(_instance, null);
    }
}

namespace UnityEngine
{
    internal static class Debug
    {
        public static void LogWarning(object message) => FakeGame.Warnings.Add(message?.ToString() ?? string.Empty);
    }
    internal sealed class GameObject { public bool activeInHierarchy; }
}

// Production imports used by other variants of this controller. These empty
// namespaces satisfy imports without pretending to implement unrelated APIs.
namespace Config { }
namespace Game.Views.Building.BuildingAreaQuickActionMenu { }
namespace GameData.Domains.Extra { }
namespace GameData.GameDataBridge { }
