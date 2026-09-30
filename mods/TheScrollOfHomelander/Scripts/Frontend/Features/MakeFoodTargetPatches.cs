#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using Config;
using Game.Views.Make;
using GameData.Domains.Item.Display;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BetterTaiwuScroll.Frontend;

internal static class MakeFoodTargetSupport
{
    internal const short CombinedFoodTargetSubType = 799;

    internal const short MeatFoodGroup = 701;
    internal const short VegetableFoodGroup = 700;

    internal static bool IsFoodGroupSubType(short itemSubType)
    {
        return itemSubType == MeatFoodGroup || itemSubType == VegetableFoodGroup;
    }

    internal static bool CanMakeCombinedFood(ItemDisplayData material)
    {
        if (material == null || material.RealKey.ItemType != 5)
            return false;

        var config = Config.Material.Instance[material.RealKey.TemplateId];
        if (config?.CraftableItemTypes == null || config.RequiredLifeSkillType != 14)
            return false;

        foreach (var typeId in config.CraftableItemTypes)
        {
            if (IsFoodMakeType(MakeItemType.Instance[typeId]))
                return true;
        }

        return false;
    }

    internal static bool IsFoodMakeType(MakeItemTypeItem type)
    {
        if (type == null || !IsFoodGroupSubType(type.ItemSubType)
            || type.MakeItemSubTypes == null || type.MakeItemSubTypes.Count == 0)
            return false;

        foreach (var subType in type.MakeItemSubTypes)
        {
            if (MakeItemSubType.Instance[subType]?.Result.ItemType != 7)
                return false;
        }
        return true;
    }

    /// <summary>
    /// True while the synthetic 荤素 entry is the selected target. Only then may the Mod
    /// answer the material filters on behalf of both food groups.
    /// </summary>
    internal static bool CombinedTargetActive { get; private set; }

    internal static void SetCombinedTargetActive(bool active)
    {
        CombinedTargetActive = active;
    }

    /// <summary>The food group this catalyst is cooked in (701 荤 / 700 素), or -1.</summary>
    internal static short ResolveFoodGroupFor(ItemDisplayData material)
    {
        if (material == null || material.RealKey.ItemType != 5)
            return -1;

        var config = Config.Material.Instance[material.RealKey.TemplateId];
        var craftable = config?.CraftableItemTypes;
        if (craftable == null)
            return -1;

        foreach (var typeId in craftable)
        {
            var type = MakeItemType.Instance[typeId];
            if (!IsFoodMakeType(type))
                continue;

            return type.ItemSubType;
        }

        return -1;
    }

    /// <summary>
    /// Remembers the group the combined target currently publishes, so the material filters
    /// can keep admitting the other group's catalysts while it stays selected.
    /// </summary>
    internal static void PublishFoodGroup(MakeSubPageMake page, short foodGroup)
    {
        if (!IsFoodGroupSubType(foodGroup))
            return;

        CombinedFoodGroup = foodGroup;
        if (page != null)
            _groupByPage[page] = foodGroup;
    }

    internal static short CombinedFoodGroup { get; private set; } = MeatFoodGroup;

    internal static void ForgetPage(MakeSubPageMake page)
    {
        if (page != null)
            _groupByPage.Remove(page);
    }

    private static readonly Dictionary<MakeSubPageMake, short> _groupByPage = new();

    private const string LargeIconFileName = "combined_food.png";
    private const string SmallIconFileName = "combined_food_small.png";

    private static Texture2D _largeTexture;
    private static Texture2D _smallTexture;
    private static Sprite _largeSprite;
    private static Sprite _smallSprite;
    private static bool _missingIconLogged;

    internal static bool IsCombinedFoodTarget(short itemSubType)
    {
        return itemSubType == CombinedFoodTargetSubType;
    }

    internal static bool IsFoodRandomSubType(short itemSubType)
    {
        return IsFoodGroupSubType(itemSubType);
    }

    internal static void InstallCombinedTarget(MakeSubPageMake page)
    {
        if (page == null)
            return;

        var traverse = Traverse.Create(page);
        var subtypeList = traverse.Field("_canMakeItemSubTypeList").GetValue<List<short>>();
        if (subtypeList != null)
        {
            subtypeList.RemoveAll(IsCombinedFoodTarget);
            subtypeList.Insert(0, CombinedFoodTargetSubType);
        }

        var targetList = traverse.Field("_canMakeItemSubTypeTargetList").GetValue<List<ItemDisplayData>>();
        if (targetList == null)
            return;

        ItemDisplayData combinedTarget = null;
        for (var index = targetList.Count - 1; index >= 0; index--)
        {
            var target = targetList[index];
            if (target != null
                && MakeSubPageMakeHelper.CheckIsRandomMake(target)
                && IsCombinedFoodTarget(target.RealKey.TemplateId))
            {
                combinedTarget ??= target;
                targetList.RemoveAt(index);
            }
        }

        if (combinedTarget == null)
        {
            combinedTarget = new ItemDisplayData(
                new ItemKey(-1, byte.MaxValue, CombinedFoodTargetSubType, -1), 0)
            {
                Interactable = true
            };
        }

        targetList.Insert(0, combinedTarget);
        EnsureCombinedFoodMakeTypeItem(traverse, targetList.Count);
    }

    internal static Sprite GetLargeSprite()
    {
        if (_largeSprite != null)
            return _largeSprite;

        _largeSprite = LoadSprite(LargeIconFileName, out _largeTexture);
        return _largeSprite;
    }

    internal static Sprite GetSmallSprite()
    {
        if (_smallSprite != null)
            return _smallSprite;

        _smallSprite = LoadSprite(SmallIconFileName, out _smallTexture);
        return _smallSprite;
    }

    internal static void Dispose()
    {
        if (_largeSprite != null)
            UnityEngine.Object.Destroy(_largeSprite);
        if (_smallSprite != null)
            UnityEngine.Object.Destroy(_smallSprite);
        if (_largeTexture != null)
            UnityEngine.Object.Destroy(_largeTexture);
        if (_smallTexture != null)
            UnityEngine.Object.Destroy(_smallTexture);

        _largeSprite = null;
        _smallSprite = null;
        _largeTexture = null;
        _smallTexture = null;
        _missingIconLogged = false;
    }

    private static void EnsureCombinedFoodMakeTypeItem(Traverse pageTraverse, int requiredCount)
    {
        var itemList = pageTraverse.Field("makeTypeItemList").GetValue<List<MakeTypeItem>>();
        if (itemList == null || itemList.Count == 0)
            return;

        MakeTypeItem combinedItem = null;
        for (var index = itemList.Count - 1; index >= 0; index--)
        {
            var item = itemList[index];
            if (item != null && item.gameObject.name == "BetterTaiwuScrollCombinedFoodMakeType")
            {
                combinedItem = item;
                itemList.RemoveAt(index);
            }
        }

        var template = itemList.Find(item => item != null);
        if (template == null || template.transform.parent == null)
            return;

        if (combinedItem == null)
        {
            var existingTransform = template.transform.parent.Find("BetterTaiwuScrollCombinedFoodMakeType");
            if (existingTransform != null)
                combinedItem = existingTransform.GetComponent<MakeTypeItem>();
        }

        if (combinedItem == null && itemList.Count < requiredCount)
        {
            var cloneObject = UnityEngine.Object.Instantiate(
                template.gameObject,
                template.transform.parent,
                false);
            cloneObject.name = "BetterTaiwuScrollCombinedFoodMakeType";
            combinedItem = cloneObject.GetComponent<MakeTypeItem>();
            if (combinedItem == null)
            {
                UnityEngine.Object.Destroy(cloneObject);
                return;
            }
        }

        if (combinedItem == null)
            return;

        PositionCloneAtBeginning(itemList, combinedItem);
        itemList.Insert(0, combinedItem);
    }

    private static void PositionCloneAtBeginning(List<MakeTypeItem> itemList, MakeTypeItem clone)
    {
        var cloneRect = clone.transform as RectTransform;
        var parent = cloneRect == null ? null : cloneRect.parent as RectTransform;
        if (cloneRect == null || parent == null)
            return;

        RectTransform firstRect = null;
        RectTransform secondRect = null;
        for (var index = 0; index < itemList.Count; index++)
        {
            var rect = itemList[index] == null ? null : itemList[index].transform as RectTransform;
            if (rect == null || rect == cloneRect)
                continue;

            if (firstRect == null)
                firstRect = rect;
            else
            {
                secondRect = rect;
                break;
            }
        }

        if (firstRect == null)
            return;

        cloneRect.SetSiblingIndex(firstRect.GetSiblingIndex());
        cloneRect.anchorMin = firstRect.anchorMin;
        cloneRect.anchorMax = firstRect.anchorMax;
        cloneRect.pivot = firstRect.pivot;
        cloneRect.sizeDelta = firstRect.sizeDelta;
        cloneRect.localScale = Vector3.one;

        if (parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null)
            return;

        var step = secondRect == null
            ? new Vector2(firstRect.rect.width + 8f, 0f)
            : secondRect.anchoredPosition - firstRect.anchoredPosition;
        cloneRect.anchoredPosition = firstRect.anchoredPosition - step;
    }

    private static Sprite LoadSprite(string fileName, out Texture2D texture)
    {
        texture = null;
        string selectedPath = null;
        foreach (var path in GetIconPathCandidates(fileName))
        {
            if (!File.Exists(path))
                continue;

            selectedPath = path;
            break;
        }

        if (string.IsNullOrEmpty(selectedPath))
        {
            LogMissingIcon(fileName);
            return null;
        }

        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(selectedPath)))
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
                LogMissingIcon(fileName);
                return null;
            }

            texture.name = "BetterTaiwuScroll_" + Path.GetFileNameWithoutExtension(fileName);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = texture.name;
            return sprite;
        }
        catch (Exception ex)
        {
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
            texture = null;
            Debug.LogWarning("[BetterTaiwuScroll] Failed to load combined food icon: " + ex.Message);
            return null;
        }
    }

    private static IEnumerable<string> GetIconPathCandidates(string fileName)
    {
        if (!string.IsNullOrEmpty(Plugin.ModDirectory))
            yield return Path.Combine(Path.GetFullPath(Plugin.ModDirectory), "Assets", "Make", fileName);

        var assemblyPath = typeof(Plugin).Assembly.Location;
        if (!string.IsNullOrEmpty(assemblyPath))
        {
            var assemblyDirectory = Path.GetDirectoryName(assemblyPath);
            if (!string.IsNullOrEmpty(assemblyDirectory))
            {
                var modRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", ".."));
                yield return Path.Combine(modRoot, "Assets", "Make", fileName);
            }
        }

        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaiwuStudio",
            "Taiwu Studio",
            "data",
            "mods",
            "TheScrollOfHomelander");
        yield return Path.Combine(appDataRoot, "Assets", "Make", fileName);
    }

    private static void LogMissingIcon(string fileName)
    {
        if (_missingIconLogged)
            return;

        _missingIconLogged = true;
        Debug.LogWarning("[BetterTaiwuScroll] Combined food icon is missing: Assets/Make/" + fileName);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "Init")]
internal static class MakeSubPageMakeInitFoodTargetPatch
{
    private static void Postfix(MakeSubPageMake __instance, ViewMake parentView)
    {
        if (__instance == null || parentView == null || parentView.CurLifeSkillType != 14
            || !Plugin.IsEnabledForLifeSkill(parentView.CurLifeSkillType))
            return;

        MakeFoodTargetSupport.InstallCombinedTarget(__instance);
    }
}

[HarmonyPatch(typeof(MakeSubPageMakeHelper), nameof(MakeSubPageMakeHelper.GetRandomMakeIcon))]
internal static class MakeSubPageMakeHelperFoodTargetIconPatch
{
    private static void Postfix(short tempId, ref string __result)
    {
        if (MakeFoodTargetSupport.IsCombinedFoodTarget(tempId))
            __result = "ui9_icon_maketype_all";
    }
}

[HarmonyPatch(typeof(MakeSubPageMakeHelper), nameof(MakeSubPageMakeHelper.GetSmallRandomMakeIcon))]
internal static class MakeSubPageMakeHelperSmallFoodTargetIconPatch
{
    private static void Postfix(short tempId, ref string __result)
    {
        if (MakeFoodTargetSupport.IsCombinedFoodTarget(tempId))
        {
            __result = MakeFoodTargetSupport.GetSmallSprite() != null
                ? string.Empty
                : "ui9_icon_maketype_small_meatfood";
        }
    }
}

[HarmonyPatch(typeof(MakeTypeItem), nameof(MakeTypeItem.Init))]
internal static class MakeTypeItemCombinedFoodIconPatch
{
    private static bool Prefix(
        MakeTypeItem __instance,
        ItemDisplayData data,
        bool isSelected,
        Action onClick)
    {
        if (__instance == null
            || data == null
            || !MakeSubPageMakeHelper.CheckIsRandomMake(data)
            || !MakeFoodTargetSupport.IsCombinedFoodTarget(data.RealKey.TemplateId))
            return true;

        var traverse = Traverse.Create(__instance);
        var button = traverse.Field("button").GetValue<CButton>();
        var image = traverse.Field("image").GetValue<CImage>();
        var selected = traverse.Field("selected").GetValue<GameObject>();
        var tip = traverse.Field("tip").GetValue<TooltipInvoker>();
        var sprite = MakeFoodTargetSupport.GetSmallSprite();

        if (image != null && sprite != null)
        {
            image.sprite = sprite;
            image.preserveAspect = true;
        }

        if (tip != null)
            tip.PresetParam = new[] { "荤素" };

        button?.ClearAndAddListener(onClick);
        selected?.SetActive(isSelected);

        return false;
    }
}

[HarmonyPatch(typeof(MakeTargetSlot), nameof(MakeTargetSlot.SetRandomIcon), new[] { typeof(string) })]
internal static class MakeTargetSlotCombinedFoodIconPatch
{
    private static bool Prefix(MakeTargetSlot __instance)
    {
        var data = __instance?.ItemData;
        if (data == null
            || !MakeSubPageMakeHelper.CheckIsRandomMake(data)
            || !MakeFoodTargetSupport.IsCombinedFoodTarget(data.RealKey.TemplateId))
            return true;

        var sprite = MakeFoodTargetSupport.GetLargeSprite();
        var itemBack = Traverse.Create(__instance)
            .Field("itemBack")
            .GetValue<Game.Components.Item.ItemBack>();
        if (sprite == null || itemBack == null)
            return true;

        itemBack.SetIcon(sprite);
        itemBack.SetBack(-1);
        return false;
    }
}

[HarmonyPatch(typeof(MakeTargetSlot), nameof(MakeTargetSlot.Refresh), new[] { typeof(bool) })]
internal static class MakeTargetSlotCombinedFoodNamePatch
{
    private static void Postfix(MakeTargetSlot __instance)
    {
        var data = __instance?.ItemData;
        if (data == null
            || !MakeSubPageMakeHelper.CheckIsRandomMake(data)
            || !MakeFoodTargetSupport.IsCombinedFoodTarget(data.RealKey.TemplateId))
            return;

        var textName = Traverse.Create(__instance)
            .Field("textName")
            .GetValue<TextMeshProUGUI>();
        textName?.SetText(ModLocalization.T("Meat and Vegetables"));
    }
}

[HarmonyPatch(typeof(MakeSubPageMakeHelper), nameof(MakeSubPageMakeHelper.GetRandomMakeTypeName))]
internal static class MakeSubPageMakeHelperFoodTargetNamePatch
{
    private static void Postfix(short tempId, ref string __result)
    {
        if (MakeFoodTargetSupport.IsCombinedFoodTarget(tempId))
            __result = "荤素";
    }
}

[HarmonyPatch(typeof(MakeSubPageMakeHelper), nameof(MakeSubPageMakeHelper.CheckCanMakeTargetRandomType))]
internal static class MakeSubPageMakeHelperFoodTargetMaterialPatch
{
    private static bool Prefix(short itemSubType, ItemDisplayData materialData, ref bool __result)
    {
        // Vanilla groups need no help unless the combined 荤素 target is the active target:
        // the panel publishes one concrete group while the player may click a catalyst of the
        // other group, which vanilla would reject as 类型不符.
        if (MakeFoodTargetSupport.IsCombinedFoodTarget(itemSubType))
        {
            __result = MakeFoodTargetSupport.CanMakeCombinedFood(materialData);
            return false;
        }

        if (!MakeFoodTargetSupport.IsFoodGroupSubType(itemSubType)
            || !MakeFoodTargetSupport.CombinedTargetActive)
            return true;

        __result = MakeFoodTargetSupport.CanMakeCombinedFood(materialData);
        return false;
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "RefreshMakeType")]
internal static class MakeSubPageMakeRefreshFoodTargetMakeTypePatch
{
    /// <summary>
    /// The combined 荤素 target only needs one thing vanilla cannot do: while it is selected,
    /// publish the food group the current catalyst actually belongs to (701 荤 / 700 素), so
    /// vanilla's own algorithm finds the make item type and builds the subtype list from it.
    /// The Mod deliberately does not pick the subtype: the game decides it, and the craft
    /// result follows the submitted subtype that vanilla leaves in place.
    /// </summary>
    private static bool Prefix(MakeSubPageMake __instance)
    {
        if (__instance == null)
            return true;

        var traverse = Traverse.Create(__instance);
        var targetSlot = traverse.Field("targetSlot").GetValue<MakeTargetSlot>();
        var materialSlot = traverse.Field("materialSlot").GetValue<MakeTargetSlot>();
        var targetData = targetSlot?.ItemData;
        if (!MakeSubPageMakeHelper.CheckIsRandomMake(targetData)
            || !MakeFoodTargetSupport.IsCombinedFoodTarget(targetData.RealKey.TemplateId)
            || materialSlot == null
            || !materialSlot.IsValid)
            return true;

        var material = materialSlot.ItemData;
        if (material == null)
            return true;

        // Publish the group this catalyst belongs to before vanilla runs, otherwise vanilla
        // finds no matching make item type and cancels the catalyst.
        var group = MakeFoodTargetSupport.ResolveFoodGroupFor(material);
        if (group >= 0)
        {
            MakeFoodTargetSupport.PublishFoodGroup(__instance, group);
            traverse.Field("_currentSelectRandomMakeItemSubType").SetValue(group);
        }

        // Let the game compute the make item type and the subtype list.
        return true;
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "SelectTarget")]
internal static class MakeSubPageMakeSelectTargetFoodGroupPatch
{
    private static void Prefix(MakeSubPageMake __instance, ItemDisplayData itemData)
    {
        // SelectTarget refreshes materials before assigning this field. Publish the new
        // category first, including when switching back to 荤 or 素.
        if (__instance == null || !MakeSubPageMakeHelper.CheckIsRandomMake(itemData))
            return;

        Traverse.Create(__instance).Field("_currentSelectRandomMakeItemSubType").SetValue(itemData.Key.TemplateId);

        // Only the synthetic 荤素 entry makes the Mod answer for both food groups; every
        // vanilla target keeps the game's own rules untouched.
        MakeFoodTargetSupport.SetCombinedTargetActive(
            itemData.Key.TemplateId == MakeFoodTargetSupport.CombinedFoodTargetSubType);
        if (!MakeFoodTargetSupport.CombinedTargetActive)
            MakeFoodTargetSupport.ForgetPage(__instance);
    }
}
