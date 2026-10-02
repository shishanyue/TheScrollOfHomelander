#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Config;
using HarmonyLib;
using UnityEngine;
using GameData.Domains.Building;
using GameData.Domains.Item.Display;
using Game.Views.Building.BuildingManage;
using Game.Views.Building.BuildingManage.Production;
using Game.Views.Make;
using GameData.Domains.Taiwu.ExchangeSystem;
using ItemListScroll = Game.Components.ListStyleGeneralScroll.Item.ItemListScroll;
using ExchangeContainerView = Game.Views.Exchange.ExchangeContainer;
using ExchangeViewBase = Game.Views.Exchange.ViewExchangeBase;
using FilterConfig = Game.Components.SortAndFilter.SortAndFilterConfig;
using FilterDetailedLineState = Game.Components.SortAndFilter.DetailedFilterLineState;
using FilterDetailedState = Game.Components.SortAndFilter.DetailedFilterState;
using FilterLineState = Game.Components.SortAndFilter.LineState;
using FilterMenuState = Game.Components.SortAndFilter.DetailedFilterMenuState;
using FilterView = Game.Components.SortAndFilter.SortAndFilter;
using SortButtonGroup = Game.Components.SortAndFilter.SortButtonGroup;
using SortItemState = Game.Components.SortAndFilter.SortItemState;
using SortStateData = Game.Components.SortAndFilter.SortStateData;
using FilterToggleKey = Game.Components.SortAndFilter.ToggleKey;
using DebateView = Game.Views.Debate.ViewDebate;
using LifeSkillCombatBeginView = Game.Views.LifeSkillCombat.ViewLifeSkillCombatBegin;
using ToggleGroup = FrameWork.UISystem.UIElements.CToggleGroup;

namespace BetterTaiwuScroll.Frontend;

[Serializable]
public sealed class MemoryOptimizationSettings
{
    public List<ContainerCardModeMemoryEntry> ContainerCardModes = new List<ContainerCardModeMemoryEntry>();
    public List<FilterMemoryEntry> FilterMemories = new List<FilterMemoryEntry>();
    public List<SortMemoryEntry> SortMemories = new List<SortMemoryEntry>();
    public List<StrategyPresetMemoryEntry> StrategyPresetMemories = new List<StrategyPresetMemoryEntry>();
    public List<MakeSubtypeMemoryEntry> MakeSubtypeMemories = new List<MakeSubtypeMemoryEntry>();
    public List<MakePerfectSelectionMemoryEntry> MakePerfectSelectionMemories = new List<MakePerfectSelectionMemoryEntry>();
    public List<MakePerfectResourceMemoryEntry> MakePerfectResourceMemories = new List<MakePerfectResourceMemoryEntry>();
    public bool HasLifeSkillAutoMode;
    public bool LifeSkillAutoMode;
    public bool HasMakeSubtypeLastSelection;
    public int MakeSubtypeLastLifeSkillType = -1;
    public bool MakeSubtypeLastHasSubtypeId;
    public int MakeSubtypeLastSubtypeId;
    public string MakeSubtypeLastSubtypeName;

    internal void Normalize()
    {
        ContainerCardModes ??= new List<ContainerCardModeMemoryEntry>();
        ContainerCardModes.RemoveAll(entry => entry == null || string.IsNullOrEmpty(entry.Key));
        FilterMemories ??= new List<FilterMemoryEntry>();
        SortMemories ??= new List<SortMemoryEntry>();
        StrategyPresetMemories ??= new List<StrategyPresetMemoryEntry>();
        MakeSubtypeMemories ??= new List<MakeSubtypeMemoryEntry>();
        MakePerfectSelectionMemories ??= new List<MakePerfectSelectionMemoryEntry>();
        MakePerfectResourceMemories ??= new List<MakePerfectResourceMemoryEntry>();

        FilterMemories.RemoveAll(entry => entry == null || string.IsNullOrEmpty(entry.Key));
        foreach (var entry in FilterMemories)
            entry.Normalize();

        SortMemories.RemoveAll(entry => entry == null || string.IsNullOrEmpty(entry.Key));
        foreach (var entry in SortMemories)
            entry.Normalize();

        StrategyPresetMemories.RemoveAll(entry => entry == null);
        foreach (var entry in StrategyPresetMemories)
            entry.PresetIndex = Mathf.Clamp(entry.PresetIndex, 0, 8);

        MakeSubtypeMemories.RemoveAll(entry => entry == null || entry.LifeSkillType < 0 || entry.MakeItemTypeId < 0 || string.IsNullOrEmpty(entry.Signature));
        foreach (var entry in MakeSubtypeMemories)
        {
            entry.SubtypeIndex = Mathf.Clamp(entry.SubtypeIndex, 0, 16);
            if (entry.HasSubtypeId && entry.SubtypeId < 0)
                entry.HasSubtypeId = false;
        }

        MakePerfectSelectionMemories.RemoveAll(entry => entry == null || entry.LifeSkillType < 0 || entry.TargetItemType < 0 || entry.TargetTemplateId < -1);
        foreach (var entry in MakePerfectSelectionMemories)
        {
            entry.MemoryKey ??= string.Empty;
            if (!entry.HasSelection || entry.SelectedSubtypeId < -2 || entry.SelectedTemplateId < -1)
            {
                entry.HasSelection = false;
                entry.SelectedSubtypeId = -1;
                entry.SelectedTemplateId = -1;
            }
        }

        MakePerfectResourceMemories.RemoveAll(entry =>
            entry == null
            || entry.LifeSkillType < 0
            || entry.TargetItemType < 0
            || entry.TargetTemplateId < -1
            || entry.SelectedSubtypeId < -2
            || entry.SelectedTemplateId < -1);
        foreach (var entry in MakePerfectResourceMemories)
        {
            entry.MemoryKey ??= string.Empty;
            entry.Wood = Mathf.Clamp(entry.Wood, 0, 999);
            entry.Metal = Mathf.Clamp(entry.Metal, 0, 999);
            entry.Jade = Mathf.Clamp(entry.Jade, 0, 999);
            entry.Fabric = Mathf.Clamp(entry.Fabric, 0, 999);
        }

        if (HasMakeSubtypeLastSelection && MakeSubtypeLastLifeSkillType < 0)
            HasMakeSubtypeLastSelection = false;
        MakeSubtypeLastSubtypeName ??= string.Empty;
    }
}

[Serializable]
public sealed class ContainerCardModeMemoryEntry
{
    public string Key;
    public bool IsCardMode;
}

[Serializable]
public sealed class FilterMemoryEntry
{
    public string Key;
    public string Signature;
    public List<FilterLineMemory> Lines = new List<FilterLineMemory>();

    internal void Normalize()
    {
        Lines ??= new List<FilterLineMemory>();
        Lines.RemoveAll(line => line == null);
        foreach (var line in Lines)
            line.Normalize();
    }
}

[Serializable]
public sealed class FilterLineMemory
{
    public int LineId;
    public int Type;
    public bool IsActive = true;
    public bool ToggleIsAll = true;
    public int ToggleIndex = -1;
    public List<FilterMenuMemory> Menus = new List<FilterMenuMemory>();

    internal void Normalize()
    {
        Menus ??= new List<FilterMenuMemory>();
        Menus.RemoveAll(menu => menu == null);
        foreach (var menu in Menus)
            menu.Normalize();
    }
}

[Serializable]
public sealed class FilterMenuMemory
{
    public int MenuId;
    public bool IsActive;
    public List<int> SelectedIndices = new List<int>();

    internal void Normalize()
    {
        SelectedIndices ??= new List<int>();
        SelectedIndices.RemoveAll(index => index < 0);
    }
}

[Serializable]
public sealed class SortMemoryEntry
{
    public string Key;
    public string Signature;
    public List<SortItemMemory> Items = new List<SortItemMemory>();

    internal void Normalize()
    {
        Items ??= new List<SortItemMemory>();
        Items.RemoveAll(item => item == null || item.SortId < 0);
    }
}

[Serializable]
public sealed class SortItemMemory
{
    public short SortId;
    public int Direction;
}

[Serializable]
public sealed class StrategyPresetMemoryEntry
{
    public int LifeSkillType;
    public int PresetIndex;
}

[Serializable]
public sealed class MakeSubtypeMemoryEntry
{
    public int LifeSkillType;
    public int MakeItemTypeId;
    public string Signature;
    public int SubtypeIndex;
    public bool HasSubtypeId;
    public int SubtypeId;
    public string SubtypeName;
}

[Serializable]
public sealed class MakePerfectSelectionMemoryEntry
{
    public string MemoryKey;
    public int LifeSkillType;
    public int TargetItemType;
    public int TargetTemplateId;
    public bool PerfectEnabled;
    public bool HasSelection;
    public int SelectedSubtypeId = -1;
    public int SelectedTemplateId = -1;
    public string SelectedName;
}

[Serializable]
internal sealed class BuildingOutputStorageSettings
{
    public int ResourceStorage = (int)GameData.Domains.Taiwu.TaiwuVillageStorageType.Inventory;
    public int ItemStorage = (int)GameData.Domains.Taiwu.TaiwuVillageStorageType.Warehouse;

    internal void Normalize()
    {
        var resourceStorage = (GameData.Domains.Taiwu.TaiwuVillageStorageType)ResourceStorage;
        if (resourceStorage != GameData.Domains.Taiwu.TaiwuVillageStorageType.Inventory
            && resourceStorage != GameData.Domains.Taiwu.TaiwuVillageStorageType.Treasury)
        {
            ResourceStorage = (int)GameData.Domains.Taiwu.TaiwuVillageStorageType.Inventory;
        }

        var itemStorage = (GameData.Domains.Taiwu.TaiwuVillageStorageType)ItemStorage;
        if (itemStorage != GameData.Domains.Taiwu.TaiwuVillageStorageType.Warehouse
            && itemStorage != GameData.Domains.Taiwu.TaiwuVillageStorageType.Treasury
            && itemStorage != GameData.Domains.Taiwu.TaiwuVillageStorageType.Stock)
        {
            ItemStorage = (int)GameData.Domains.Taiwu.TaiwuVillageStorageType.Warehouse;
        }
    }

    internal BuildingResourceOutputSetting ToGameSetting()
    {
        Normalize();
        var result = new BuildingResourceOutputSetting();
        result.Init();
        result.ResourceStorage = (GameData.Domains.Taiwu.TaiwuVillageStorageType)ResourceStorage;
        result.ItemStorage = (GameData.Domains.Taiwu.TaiwuVillageStorageType)ItemStorage;
        return result;
    }
}

internal static class BuildingOutputStorageSettingsStore
{
    private const string FileNamePrefix = "BuildingOutputStorageSettings_";
    private static readonly Dictionary<string, BuildingOutputStorageSettings> SettingsByBuilding =
        new Dictionary<string, BuildingOutputStorageSettings>();

    internal static BuildingOutputStorageSettings GetFor(BuildingBlockKey key, short templateId)
    {
        var memoryKey = GetMemoryKey(key, templateId);
        if (SettingsByBuilding.TryGetValue(memoryKey, out var settings))
            return settings;

        settings = LoadFor(key, templateId);
        SettingsByBuilding[memoryKey] = settings;
        return settings;
    }

    internal static void Save(BuildingBlockKey key, short templateId, BuildingResourceOutputSetting setting)
    {
        if (key.IsInvalid || templateId < 0 || setting == null)
            return;

        try
        {
            var settings = new BuildingOutputStorageSettings
            {
                ResourceStorage = (int)setting.ResourceStorage,
                ItemStorage = (int)setting.ItemStorage
            };
            settings.Normalize();
            SettingsByBuilding[GetMemoryKey(key, templateId)] = settings;
            Persist(key, templateId, settings);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save building output storage settings for building "
                + GetMemoryKey(key, templateId) + ": " + ex);
        }
    }

    private static BuildingOutputStorageSettings LoadFor(BuildingBlockKey key, short templateId)
    {
        try
        {
            foreach (var path in GetSettingsPathCandidates(key, templateId))
            {
                if (!File.Exists(path))
                    continue;

                var settings = JsonUtility.FromJson<BuildingOutputStorageSettings>(File.ReadAllText(path))
                    ?? new BuildingOutputStorageSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to load building output storage settings for building "
                + GetMemoryKey(key, templateId) + ": " + ex);
        }

        return new BuildingOutputStorageSettings();
    }

    private static void Persist(BuildingBlockKey key, short templateId, BuildingOutputStorageSettings settings)
    {
        var path = GetSettingsPath(key, templateId);
        AsyncSettingsSaveQueue.Enqueue(path, new BuildingOutputStorageSettings
        {
            ResourceStorage = settings.ResourceStorage,
            ItemStorage = settings.ItemStorage
        });
    }

    private static string GetMemoryKey(BuildingBlockKey key, short templateId)
    {
        return key.AreaId + "_" + key.BlockId + "_" + key.BuildingBlockIndex + "_" + templateId;
    }

    private static string GetSettingsPath(BuildingBlockKey key, short templateId)
    {
        return ModUserDataPaths.GetFilePath(FileNamePrefix + GetMemoryKey(key, templateId) + ".json");
    }

    private static IEnumerable<string> GetSettingsPathCandidates(BuildingBlockKey key, short templateId)
    {
        var fileName = FileNamePrefix + GetMemoryKey(key, templateId) + ".json";
        foreach (var path in ModUserDataPaths.GetFilePathCandidates(fileName))
            yield return path;
    }

}

[Serializable]
internal sealed class MakeStorageLocationSettings
{
    // The vanilla default for Taiwu's own crafting is the private warehouse.
    // Starting each unseen building from this value prevents the last building's
    // global -1 cache entry from becoming the new building's initial memory.
    public int StorageIndex = 1;

    internal void Normalize()
    {
        StorageIndex = Mathf.Clamp(StorageIndex, 0, 3);
    }
}

internal static class MakeStorageLocationSettingsStore
{
    private const string FileNamePrefix = "MakeStorageLocationSettings_";
    private static readonly Dictionary<string, MakeStorageLocationSettings> SettingsByBuilding =
        new Dictionary<string, MakeStorageLocationSettings>();

    internal static int GetFor(BuildingBlockKey key, short templateId)
    {
        var memoryKey = GetMemoryKey(key, templateId);
        if (!SettingsByBuilding.TryGetValue(memoryKey, out var settings))
        {
            settings = LoadFor(key, templateId);
            SettingsByBuilding[memoryKey] = settings;
        }

        return settings.StorageIndex;
    }

    internal static void Save(BuildingBlockKey key, short templateId, int storageIndex)
    {
        if (key.IsInvalid || templateId < 0 || storageIndex < 0 || storageIndex > 3)
            return;

        try
        {
            var settings = new MakeStorageLocationSettings { StorageIndex = storageIndex };
            SettingsByBuilding[GetMemoryKey(key, templateId)] = settings;
            Persist(key, templateId, settings);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save make storage for "
                + FormatContext(key, templateId) + ": " + ex);
        }
    }

    private static MakeStorageLocationSettings LoadFor(BuildingBlockKey key, short templateId)
    {
        try
        {
            foreach (var path in GetSettingsPathCandidates(key, templateId))
            {
                if (!File.Exists(path))
                    continue;

                var settings = JsonUtility.FromJson<MakeStorageLocationSettings>(File.ReadAllText(path))
                    ?? new MakeStorageLocationSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to load make storage for "
                + FormatContext(key, templateId) + ": " + ex);
        }

        return new MakeStorageLocationSettings();
    }

    private static void Persist(BuildingBlockKey key, short templateId, MakeStorageLocationSettings settings)
    {
        var path = GetSettingsPath(key, templateId);
        AsyncSettingsSaveQueue.Enqueue(path, new MakeStorageLocationSettings
        {
            StorageIndex = settings.StorageIndex
        });
    }

    private static string GetMemoryKey(BuildingBlockKey key, short templateId)
    {
        return key.AreaId + "_" + key.BlockId + "_" + key.BuildingBlockIndex + "_" + templateId;
    }

    private static string FormatContext(BuildingBlockKey key, short templateId)
    {
        return key.AreaId + "/" + key.BlockId + "/" + key.BuildingBlockIndex + "/" + templateId;
    }

    private static string GetSettingsPath(BuildingBlockKey key, short templateId)
    {
        return ModUserDataPaths.GetFilePath(FileNamePrefix + GetMemoryKey(key, templateId) + ".json");
    }

    private static IEnumerable<string> GetSettingsPathCandidates(BuildingBlockKey key, short templateId)
    {
        var fileName = FileNamePrefix + GetMemoryKey(key, templateId) + ".json";
        foreach (var path in ModUserDataPaths.GetFilePathCandidates(fileName))
            yield return path;
    }
}
[Serializable]
public sealed class MakePerfectResourceMemoryEntry
{
    public string MemoryKey;
    public int LifeSkillType;
    public int TargetItemType;
    public int TargetTemplateId;
    public int SelectedSubtypeId;
    public int SelectedTemplateId;
    public string SelectedName;
    public int Wood;
    public int Metal;
    public int Jade;
    public int Fabric;
}

internal static class MemoryOptimizationSettingsStore
{
    private const string FileName = "MemoryOptimizationSettings.json";

    private static readonly Dictionary<string, FilterMemoryEntry> FilterByKey = new Dictionary<string, FilterMemoryEntry>();
    private static readonly Dictionary<string, SortMemoryEntry> SortByKey = new Dictionary<string, SortMemoryEntry>();
    private static readonly Dictionary<int, StrategyPresetMemoryEntry> StrategyByLifeSkill = new Dictionary<int, StrategyPresetMemoryEntry>();
    private static readonly Dictionary<string, MakeSubtypeMemoryEntry> MakeSubtypeByKey = new Dictionary<string, MakeSubtypeMemoryEntry>();
    private static readonly Dictionary<string, MakePerfectSelectionMemoryEntry> PerfectSelectionByMemoryKey = new Dictionary<string, MakePerfectSelectionMemoryEntry>();
    private static readonly Dictionary<string, MakePerfectSelectionMemoryEntry> PerfectSelectionByTarget = new Dictionary<string, MakePerfectSelectionMemoryEntry>();
    private static readonly Dictionary<string, MakePerfectSelectionMemoryEntry> PerfectSelectionBySelected = new Dictionary<string, MakePerfectSelectionMemoryEntry>();
    private static readonly Dictionary<int, MakePerfectSelectionMemoryEntry> LastPerfectSelectionByLifeSkill = new Dictionary<int, MakePerfectSelectionMemoryEntry>();
    private static readonly Dictionary<string, MakePerfectResourceMemoryEntry> PerfectResourceByMemoryKey = new Dictionary<string, MakePerfectResourceMemoryEntry>();
    private static readonly Dictionary<string, MakePerfectResourceMemoryEntry> PerfectResourceByTarget = new Dictionary<string, MakePerfectResourceMemoryEntry>();
    private static readonly Dictionary<string, MakePerfectResourceMemoryEntry> PerfectResourceBySelected = new Dictionary<string, MakePerfectResourceMemoryEntry>();
    private static bool _loaded;

    internal static MemoryOptimizationSettings Current { get; private set; } = new MemoryOptimizationSettings();

    internal static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            foreach (var path in GetSettingsPathCandidates())
            {
                if (!File.Exists(path))
                    continue;

                Current = JsonUtility.FromJson<MemoryOptimizationSettings>(File.ReadAllText(path)) ?? new MemoryOptimizationSettings();
                break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to load memory optimization settings: " + ex);
            Current = new MemoryOptimizationSettings();
        }

        Current.Normalize();
        RebuildIndexes();
    }

    internal static void Save()
    {
        try
        {
            AsyncSettingsSaveQueue.Enqueue(GetSettingsPath(), CloneCurrent());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save memory optimization settings: " + ex);
        }
    }

    internal static FilterMemoryEntry GetFilterMemory(string key, string signature)
    {
        FilterByKey.TryGetValue(ComposeKey(key, signature), out var entry);
        return entry;
    }

    internal static bool? GetContainerCardMode(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        return Current.ContainerCardModes.Find(entry => entry.Key == key)?.IsCardMode;
    }

    internal static void SetContainerCardMode(string key, bool isCardMode)
    {
        if (string.IsNullOrEmpty(key))
            return;
        var entry = Current.ContainerCardModes.Find(item => item.Key == key);
        if (entry != null && entry.IsCardMode == isCardMode)
            return;
        if (entry == null)
        {
            entry = new ContainerCardModeMemoryEntry { Key = key };
            Current.ContainerCardModes.Add(entry);
        }
        entry.IsCardMode = isCardMode;
        Save();
    }

    internal static void SetFilterMemory(FilterMemoryEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Key))
            return;

        entry.Normalize();
        var key = ComposeKey(entry.Key, entry.Signature);
        if (FilterByKey.TryGetValue(key, out var existing) && FilterEquals(existing, entry))
            return;
        if (existing != null)
        {
            var index = Current.FilterMemories.IndexOf(existing);
            if (index >= 0)
                Current.FilterMemories[index] = entry;
            else
                Current.FilterMemories.Add(entry);
        }
        else
            Current.FilterMemories.Add(entry);
        FilterByKey[key] = entry;
        Save();
    }

    internal static SortMemoryEntry GetSortMemory(string key, string signature)
    {
        SortByKey.TryGetValue(ComposeKey(key, signature), out var entry);
        return entry;
    }

    internal static void SetSortMemory(SortMemoryEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Key))
            return;

        entry.Normalize();
        var key = ComposeKey(entry.Key, entry.Signature);
        if (SortByKey.TryGetValue(key, out var existing) && SortEquals(existing, entry))
            return;
        if (existing != null)
        {
            var index = Current.SortMemories.IndexOf(existing);
            if (index >= 0)
                Current.SortMemories[index] = entry;
            else
                Current.SortMemories.Add(entry);
        }
        else
            Current.SortMemories.Add(entry);
        SortByKey[key] = entry;
        Save();
    }

    internal static int GetStrategyPreset(sbyte lifeSkillType)
    {
        StrategyByLifeSkill.TryGetValue(lifeSkillType, out var entry);
        return entry != null ? Mathf.Clamp(entry.PresetIndex, 0, 8) : -1;
    }

    internal static void SetStrategyPreset(sbyte lifeSkillType, int presetIndex)
    {
        if (lifeSkillType < 0 || presetIndex < 0)
            return;

        presetIndex = Mathf.Clamp(presetIndex, 0, 8);
        StrategyByLifeSkill.TryGetValue(lifeSkillType, out var entry);
        if (entry == null)
        {
            entry = new StrategyPresetMemoryEntry { LifeSkillType = lifeSkillType };
            Current.StrategyPresetMemories.Add(entry);
            StrategyByLifeSkill[lifeSkillType] = entry;
        }
        else if (entry.PresetIndex == presetIndex)
            return;
        entry.PresetIndex = presetIndex;
        Save();
    }

    internal static int GetMakeSubtypeIndex(sbyte lifeSkillType, short makeItemTypeId, string signature)
    {
        var entry = GetMakeSubtypeMemory(lifeSkillType, makeItemTypeId, signature);
        return entry == null ? -1 : entry.SubtypeIndex;
    }

    internal static MakeSubtypeMemoryEntry GetMakeSubtypeMemory(sbyte lifeSkillType, short makeItemTypeId, string signature)
    {
        if (lifeSkillType < 0 || makeItemTypeId < 0 || string.IsNullOrEmpty(signature))
            return null;

        MakeSubtypeByKey.TryGetValue(ComposeMakeSubtypeKey(lifeSkillType, makeItemTypeId, signature), out var entry);
        return entry;
    }

    internal static void SetMakeSubtypeIndex(sbyte lifeSkillType, short makeItemTypeId, string signature, int subtypeIndex, int subtypeId = -1, string subtypeName = null)
    {
        if (lifeSkillType < 0 || makeItemTypeId < 0 || string.IsNullOrEmpty(signature) || subtypeIndex < 0)
            return;

        var key = ComposeMakeSubtypeKey(lifeSkillType, makeItemTypeId, signature);
        MakeSubtypeByKey.TryGetValue(key, out var entry);
        var normalizedName = subtypeName ?? string.Empty;
        var unchanged = entry != null
            && entry.SubtypeIndex == subtypeIndex
            && entry.HasSubtypeId == (subtypeId >= 0)
            && entry.SubtypeId == subtypeId
            && string.Equals(entry.SubtypeName ?? string.Empty, normalizedName, StringComparison.Ordinal)
            && Current.HasMakeSubtypeLastSelection
            && Current.MakeSubtypeLastLifeSkillType == lifeSkillType
            && Current.MakeSubtypeLastHasSubtypeId == (subtypeId >= 0)
            && Current.MakeSubtypeLastSubtypeId == subtypeId
            && string.Equals(Current.MakeSubtypeLastSubtypeName ?? string.Empty, normalizedName, StringComparison.Ordinal);
        if (unchanged)
            return;
        if (entry == null)
        {
            entry = new MakeSubtypeMemoryEntry
            {
                LifeSkillType = lifeSkillType,
                MakeItemTypeId = makeItemTypeId,
                Signature = signature
            };
            Current.MakeSubtypeMemories.Add(entry);
            MakeSubtypeByKey[key] = entry;
        }

        entry.SubtypeIndex = subtypeIndex;
        entry.HasSubtypeId = subtypeId >= 0;
        entry.SubtypeId = subtypeId;
        entry.SubtypeName = normalizedName;
        Current.HasMakeSubtypeLastSelection = true;
        Current.MakeSubtypeLastLifeSkillType = lifeSkillType;
        Current.MakeSubtypeLastHasSubtypeId = subtypeId >= 0;
        Current.MakeSubtypeLastSubtypeId = subtypeId;
        Current.MakeSubtypeLastSubtypeName = normalizedName;
        Save();
    }

    internal static MakePerfectSelectionMemoryEntry GetMakePerfectSelection(sbyte lifeSkillType, int productSubtypeId, int productTemplateId)
    {
        if (lifeSkillType < 0)
            return null;

        if (productSubtypeId >= 0 && productTemplateId >= -1)
        {
            PerfectSelectionByTarget.TryGetValue(ComposeTripleKey(lifeSkillType, productSubtypeId, productTemplateId), out var target);
            if (target != null)
                return target;
            PerfectSelectionBySelected.TryGetValue(ComposeTripleKey(lifeSkillType, productSubtypeId, productTemplateId), out var selected);
            return selected;
        }
        LastPerfectSelectionByLifeSkill.TryGetValue(lifeSkillType, out var last);
        return last;
    }

    internal static MakePerfectSelectionMemoryEntry GetMakePerfectSelection(string memoryKey)
    {
        if (string.IsNullOrEmpty(memoryKey))
            return null;

        PerfectSelectionByMemoryKey.TryGetValue(memoryKey, out var entry);
        return entry;
    }

    internal static MakePerfectResourceMemoryEntry GetMakePerfectResource(sbyte lifeSkillType, int productSubtypeId, int productTemplateId)
    {
        if (lifeSkillType < 0 || productSubtypeId < 0 || productTemplateId < -1)
            return null;

        var key = ComposeTripleKey(lifeSkillType, productSubtypeId, productTemplateId);
        PerfectResourceByTarget.TryGetValue(key, out var target);
        if (target != null)
            return target;
        PerfectResourceBySelected.TryGetValue(key, out var selected);
        return selected;
    }

    internal static MakePerfectResourceMemoryEntry GetMakePerfectResource(string memoryKey)
    {
        if (string.IsNullOrEmpty(memoryKey))
            return null;

        PerfectResourceByMemoryKey.TryGetValue(memoryKey, out var entry);
        return entry;
    }

    internal static void SetMakePerfect(MakePerfectSelectionMemoryEntry selection, MakePerfectResourceMemoryEntry resource)
    {
        var selectionValid = selection != null && selection.LifeSkillType >= 0;
        var resourceValid = resource != null
            && resource.LifeSkillType >= 0
            && resource.SelectedSubtypeId >= -2
            && resource.SelectedTemplateId >= -1
            && resource.TargetItemType >= 0
            && resource.TargetTemplateId >= -1;
        if (!selectionValid && !resourceValid)
            return;

        MakePerfectSelectionMemoryEntry oldSelection = null;
        if (selectionValid)
        {
            oldSelection = !string.IsNullOrEmpty(selection.MemoryKey)
                ? GetMakePerfectSelection(selection.MemoryKey)
                : GetMakePerfectSelection((sbyte)selection.LifeSkillType, selection.TargetItemType, selection.TargetTemplateId);
        }

        MakePerfectResourceMemoryEntry oldResource = null;
        if (resourceValid)
        {
            oldResource = !string.IsNullOrEmpty(resource.MemoryKey)
                ? GetMakePerfectResource(resource.MemoryKey)
                : GetMakePerfectResource((sbyte)resource.LifeSkillType, resource.TargetItemType, resource.TargetTemplateId);
        }
        var selectionChanged = selectionValid && !PerfectSelectionEquals(oldSelection, selection);
        var resourceChanged = resourceValid && !PerfectResourceEquals(oldResource, resource);
        if (!selectionChanged && !resourceChanged)
            return;

        if (selectionChanged)
        {
            var selectionIndex = !string.IsNullOrEmpty(selection.MemoryKey)
                ? Current.MakePerfectSelectionMemories.FindIndex(item => item.MemoryKey == selection.MemoryKey)
                : Current.MakePerfectSelectionMemories.FindIndex(item =>
                    item.LifeSkillType == selection.LifeSkillType
                    && item.TargetItemType == selection.TargetItemType
                    && item.TargetTemplateId == selection.TargetTemplateId);
            if (selectionIndex >= 0)
                Current.MakePerfectSelectionMemories.RemoveAt(selectionIndex);

            Current.MakePerfectSelectionMemories.Add(selection);
        }

        if (resourceChanged)
        {
            var resourceIndex = !string.IsNullOrEmpty(resource.MemoryKey)
                ? Current.MakePerfectResourceMemories.FindIndex(item => item.MemoryKey == resource.MemoryKey)
                : Current.MakePerfectResourceMemories.FindIndex(item =>
                    item.LifeSkillType == resource.LifeSkillType
                    && item.TargetItemType == resource.TargetItemType
                    && item.TargetTemplateId == resource.TargetTemplateId
                    && item.SelectedSubtypeId == resource.SelectedSubtypeId
                    && item.SelectedTemplateId == resource.SelectedTemplateId);
            if (resourceIndex >= 0)
                Current.MakePerfectResourceMemories[resourceIndex] = resource;
            else
                Current.MakePerfectResourceMemories.Add(resource);
        }
        RebuildPerfectIndexes();
        Save();
    }

    internal static void SetMakePerfectSelection(MakePerfectSelectionMemoryEntry selection)
    {
        SetMakePerfect(selection, null);
    }

    internal static void SetMakePerfectResource(MakePerfectResourceMemoryEntry resource)
    {
        SetMakePerfect(null, resource);
    }

    internal static void SetLifeSkillAutoMode(bool isAuto)
    {
        if (Current.HasLifeSkillAutoMode && Current.LifeSkillAutoMode == isAuto)
            return;
        Current.HasLifeSkillAutoMode = true;
        Current.LifeSkillAutoMode = isAuto;
        Save();
    }

    private static string GetSettingsPath()
    {
        return ModUserDataPaths.GetFilePath(FileName);
    }

    private static IEnumerable<string> GetSettingsPathCandidates()
    {
        foreach (var path in ModUserDataPaths.GetFilePathCandidates(FileName))
            yield return path;
    }

    private static void RebuildIndexes()
    {
        FilterByKey.Clear();
        foreach (var entry in Current.FilterMemories)
            FilterByKey[ComposeKey(entry.Key, entry.Signature)] = entry;

        SortByKey.Clear();
        foreach (var entry in Current.SortMemories)
            SortByKey[ComposeKey(entry.Key, entry.Signature)] = entry;

        StrategyByLifeSkill.Clear();
        foreach (var entry in Current.StrategyPresetMemories)
            StrategyByLifeSkill[entry.LifeSkillType] = entry;

        MakeSubtypeByKey.Clear();
        foreach (var entry in Current.MakeSubtypeMemories)
            MakeSubtypeByKey[ComposeMakeSubtypeKey((sbyte)entry.LifeSkillType, (short)entry.MakeItemTypeId, entry.Signature)] = entry;

        RebuildPerfectIndexes();
    }

    private static void RebuildPerfectIndexes()
    {
        PerfectSelectionByMemoryKey.Clear();
        PerfectSelectionByTarget.Clear();
        PerfectSelectionBySelected.Clear();
        LastPerfectSelectionByLifeSkill.Clear();
        foreach (var entry in Current.MakePerfectSelectionMemories)
        {
            if (!string.IsNullOrEmpty(entry.MemoryKey))
                PerfectSelectionByMemoryKey[entry.MemoryKey] = entry;
            PerfectSelectionByTarget[ComposeTripleKey(entry.LifeSkillType, entry.TargetItemType, entry.TargetTemplateId)] = entry;
            PerfectSelectionBySelected[ComposeTripleKey(entry.LifeSkillType, entry.SelectedSubtypeId, entry.SelectedTemplateId)] = entry;
            LastPerfectSelectionByLifeSkill[entry.LifeSkillType] = entry;
        }

        PerfectResourceByMemoryKey.Clear();
        PerfectResourceByTarget.Clear();
        PerfectResourceBySelected.Clear();
        foreach (var entry in Current.MakePerfectResourceMemories)
        {
            if (!string.IsNullOrEmpty(entry.MemoryKey))
                PerfectResourceByMemoryKey[entry.MemoryKey] = entry;
            PerfectResourceByTarget[ComposeTripleKey(entry.LifeSkillType, entry.TargetItemType, entry.TargetTemplateId)] = entry;
            PerfectResourceBySelected[ComposeTripleKey(entry.LifeSkillType, entry.SelectedSubtypeId, entry.SelectedTemplateId)] = entry;
        }
    }

    private static string ComposeKey(string key, string signature)
    {
        return (key ?? string.Empty) + "\u001f" + (signature ?? string.Empty);
    }

    private static string ComposeMakeSubtypeKey(sbyte lifeSkillType, short makeItemTypeId, string signature)
    {
        return lifeSkillType + "\u001f" + makeItemTypeId + "\u001f" + (signature ?? string.Empty);
    }

    private static string ComposeTripleKey(int first, int second, int third)
    {
        return first + "\u001f" + second + "\u001f" + third;
    }

    private static bool FilterEquals(FilterMemoryEntry left, FilterMemoryEntry right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Lines.Count != right.Lines.Count)
            return false;
        for (var i = 0; i < left.Lines.Count; i++)
        {
            var a = left.Lines[i];
            var b = right.Lines[i];
            if (a.LineId != b.LineId || a.Type != b.Type || a.IsActive != b.IsActive
                || a.ToggleIsAll != b.ToggleIsAll || a.ToggleIndex != b.ToggleIndex
                || a.Menus.Count != b.Menus.Count)
                return false;
            for (var menuIndex = 0; menuIndex < a.Menus.Count; menuIndex++)
            {
                var am = a.Menus[menuIndex];
                var bm = b.Menus[menuIndex];
                if (am.MenuId != bm.MenuId || am.IsActive != bm.IsActive
                    || !am.SelectedIndices.SequenceEqual(bm.SelectedIndices))
                    return false;
            }
        }
        return true;
    }

    private static bool SortEquals(SortMemoryEntry left, SortMemoryEntry right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Items.Count != right.Items.Count)
            return false;
        for (var i = 0; i < left.Items.Count; i++)
        {
            if (left.Items[i].SortId != right.Items[i].SortId || left.Items[i].Direction != right.Items[i].Direction)
                return false;
        }
        return true;
    }

    private static bool PerfectSelectionEquals(MakePerfectSelectionMemoryEntry left, MakePerfectSelectionMemoryEntry right)
    {
        return left != null && right != null
            && left.MemoryKey == right.MemoryKey && left.LifeSkillType == right.LifeSkillType
            && left.TargetItemType == right.TargetItemType && left.TargetTemplateId == right.TargetTemplateId
            && left.PerfectEnabled == right.PerfectEnabled && left.HasSelection == right.HasSelection
            && left.SelectedSubtypeId == right.SelectedSubtypeId && left.SelectedTemplateId == right.SelectedTemplateId
            && left.SelectedName == right.SelectedName;
    }

    private static bool PerfectResourceEquals(MakePerfectResourceMemoryEntry left, MakePerfectResourceMemoryEntry right)
    {
        return left != null && right != null
            && left.MemoryKey == right.MemoryKey && left.LifeSkillType == right.LifeSkillType
            && left.TargetItemType == right.TargetItemType && left.TargetTemplateId == right.TargetTemplateId
            && left.SelectedSubtypeId == right.SelectedSubtypeId && left.SelectedTemplateId == right.SelectedTemplateId
            && left.SelectedName == right.SelectedName && left.Wood == right.Wood && left.Metal == right.Metal
            && left.Jade == right.Jade && left.Fabric == right.Fabric;
    }

    private static MemoryOptimizationSettings CloneCurrent()
    {
        return new MemoryOptimizationSettings
        {
            ContainerCardModes = Current.ContainerCardModes.Select(entry => new ContainerCardModeMemoryEntry
            {
                Key = entry.Key,
                IsCardMode = entry.IsCardMode
            }).ToList(),
            FilterMemories = Current.FilterMemories.Select(CloneFilter).ToList(),
            SortMemories = Current.SortMemories.Select(entry => new SortMemoryEntry
            {
                Key = entry.Key,
                Signature = entry.Signature,
                Items = entry.Items.Select(item => new SortItemMemory { SortId = item.SortId, Direction = item.Direction }).ToList()
            }).ToList(),
            StrategyPresetMemories = Current.StrategyPresetMemories.Select(entry => new StrategyPresetMemoryEntry
            {
                LifeSkillType = entry.LifeSkillType,
                PresetIndex = entry.PresetIndex
            }).ToList(),
            MakeSubtypeMemories = Current.MakeSubtypeMemories.Select(entry => new MakeSubtypeMemoryEntry
            {
                LifeSkillType = entry.LifeSkillType,
                MakeItemTypeId = entry.MakeItemTypeId,
                Signature = entry.Signature,
                SubtypeIndex = entry.SubtypeIndex,
                HasSubtypeId = entry.HasSubtypeId,
                SubtypeId = entry.SubtypeId,
                SubtypeName = entry.SubtypeName
            }).ToList(),
            MakePerfectSelectionMemories = Current.MakePerfectSelectionMemories.Select(entry => new MakePerfectSelectionMemoryEntry
            {
                MemoryKey = entry.MemoryKey,
                LifeSkillType = entry.LifeSkillType,
                TargetItemType = entry.TargetItemType,
                TargetTemplateId = entry.TargetTemplateId,
                PerfectEnabled = entry.PerfectEnabled,
                HasSelection = entry.HasSelection,
                SelectedSubtypeId = entry.SelectedSubtypeId,
                SelectedTemplateId = entry.SelectedTemplateId,
                SelectedName = entry.SelectedName
            }).ToList(),
            MakePerfectResourceMemories = Current.MakePerfectResourceMemories.Select(entry => new MakePerfectResourceMemoryEntry
            {
                MemoryKey = entry.MemoryKey,
                LifeSkillType = entry.LifeSkillType,
                TargetItemType = entry.TargetItemType,
                TargetTemplateId = entry.TargetTemplateId,
                SelectedSubtypeId = entry.SelectedSubtypeId,
                SelectedTemplateId = entry.SelectedTemplateId,
                SelectedName = entry.SelectedName,
                Wood = entry.Wood,
                Metal = entry.Metal,
                Jade = entry.Jade,
                Fabric = entry.Fabric
            }).ToList(),
            HasLifeSkillAutoMode = Current.HasLifeSkillAutoMode,
            LifeSkillAutoMode = Current.LifeSkillAutoMode,
            HasMakeSubtypeLastSelection = Current.HasMakeSubtypeLastSelection,
            MakeSubtypeLastLifeSkillType = Current.MakeSubtypeLastLifeSkillType,
            MakeSubtypeLastHasSubtypeId = Current.MakeSubtypeLastHasSubtypeId,
            MakeSubtypeLastSubtypeId = Current.MakeSubtypeLastSubtypeId,
            MakeSubtypeLastSubtypeName = Current.MakeSubtypeLastSubtypeName
        };
    }

    private static FilterMemoryEntry CloneFilter(FilterMemoryEntry entry)
    {
        return new FilterMemoryEntry
        {
            Key = entry.Key,
            Signature = entry.Signature,
            Lines = entry.Lines.Select(line => new FilterLineMemory
            {
                LineId = line.LineId,
                Type = line.Type,
                IsActive = line.IsActive,
                ToggleIsAll = line.ToggleIsAll,
                ToggleIndex = line.ToggleIndex,
                Menus = line.Menus.Select(menu => new FilterMenuMemory
                {
                    MenuId = menu.MenuId,
                    IsActive = menu.IsActive,
                    SelectedIndices = new List<int>(menu.SelectedIndices)
                }).ToList()
            }).ToList()
        };
    }
}
