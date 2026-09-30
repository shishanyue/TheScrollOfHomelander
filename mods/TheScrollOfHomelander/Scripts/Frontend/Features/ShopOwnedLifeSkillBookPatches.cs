#nullable disable

using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Components.ListStyleGeneralScroll.Item;
using Game.Views.Exchange;
using GameData.Domains.Item;
using GameData.Domains.Item.Display;
using GameData.Domains.Taiwu.Display;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BetterTaiwuScroll.Frontend;

[HarmonyPatch(typeof(ViewExchangeBase), "OnTargetItemRender",
    new Type[] { typeof(ITradeableContent), typeof(RowItemLine) })]
internal static class ViewShopOwnedLifeSkillBookRenderPatch
{
    private static void Postfix(
        ViewExchangeBase __instance,
        ITradeableContent itemData,
        RowItemLine rowItemLine)
    {
        if (__instance is ViewShop shop)
            ShopOwnedLifeSkillBookSupport.Apply(shop, itemData, rowItemLine);
    }
}

[HarmonyPatch(typeof(ViewShop), "Refresh")]
internal static class ViewShopOwnedLifeSkillBookRefreshPatch
{
    private static void Prefix(ViewShop __instance)
    {
        ShopOwnedLifeSkillBookSupport.Invalidate(__instance);
    }
}

[HarmonyPatch(typeof(ViewShop), "RefreshTargetItems", new Type[] { typeof(int) })]
internal static class ViewShopOwnedLifeSkillBookRefreshTargetItemsPatch
{
    private static void Prefix(ViewShop __instance)
    {
        ShopOwnedLifeSkillBookSupport.Invalidate(__instance);
    }
}

internal static class ShopOwnedLifeSkillBookSupport
{
    private const short LifeSkillBookSubType = 1000;
    private static readonly FieldInfo ExchangeContainerField =
        AccessTools.Field(typeof(ViewExchangeBase), "exchangeContainer");

    internal static void Apply(ViewShop shop, ITradeableContent itemData, RowItemLine rowItemLine)
    {
        var main = rowItemLine?.RowItemMain ?? rowItemLine?.GetComponentInChildren<RowItemMain>(true);
        if (main == null)
            return;

        var marker = main.GetComponent<ShopOwnedLifeSkillBookMarker>();
        if (!Plugin.EnableLifeSkillBookOwnedMarker || !IsLifeSkillBook(itemData))
        {
            marker?.SetVisible(false);
            return;
        }

        var cache = GetOrAddCache(shop);
        var isOwned = cache != null && cache.Contains(itemData.Key.TemplateId);
        if (!isOwned)
        {
            marker?.SetVisible(false);
            return;
        }

        marker ??= main.gameObject.AddComponent<ShopOwnedLifeSkillBookMarker>();
        marker.Initialize(main);
        marker.SetVisible(true);
    }

    internal static void Invalidate(ViewShop shop)
    {
        shop?.GetComponent<ShopOwnedLifeSkillBookCache>()?.Invalidate();
    }

    internal static void RefreshAllActive()
    {
        foreach (var shop in Resources.FindObjectsOfTypeAll<ViewShop>())
        {
            if (shop == null || shop.gameObject == null || !shop.gameObject.activeInHierarchy)
                continue;

            Invalidate(shop);
            var container = ExchangeContainerField?.GetValue(shop) as ExchangeContainer;
            container?.targetItemList?.ReRender();
        }
    }

    internal static void DisposeAll()
    {
        foreach (var marker in Resources.FindObjectsOfTypeAll<ShopOwnedLifeSkillBookMarker>())
            marker?.Remove();

        foreach (var cache in Resources.FindObjectsOfTypeAll<ShopOwnedLifeSkillBookCache>())
        {
            if (cache != null)
                UnityEngine.Object.Destroy(cache);
        }
    }

    internal static bool IsLifeSkillBook(ITradeableContent itemData)
    {
        if (itemData == null || itemData.IsResource || itemData.Key.ItemType != ItemType.SkillBook)
            return false;

        try
        {
            return ItemTemplateHelper.GetItemSubType(itemData.Key.ItemType, itemData.Key.TemplateId)
                == LifeSkillBookSubType;
        }
        catch
        {
            return false;
        }
    }

    private static ShopOwnedLifeSkillBookCache GetOrAddCache(ViewShop shop)
    {
        if (shop == null)
            return null;

        return shop.GetComponent<ShopOwnedLifeSkillBookCache>()
            ?? shop.gameObject.AddComponent<ShopOwnedLifeSkillBookCache>();
    }
}

internal sealed class ShopOwnedLifeSkillBookCache : MonoBehaviour
{
    private static readonly FieldInfo DisplayDataField = AccessTools.Field(typeof(ViewShop), "_displayData");
    private readonly HashSet<short> _ownedTemplateIds = new HashSet<short>();
    private bool _dirty = true;

    internal void Invalidate()
    {
        _dirty = true;
    }

    internal bool Contains(short templateId)
    {
        EnsureCurrent();
        return _ownedTemplateIds.Contains(templateId);
    }

    private void EnsureCurrent()
    {
        if (!_dirty)
            return;

        _dirty = false;
        _ownedTemplateIds.Clear();
        var shop = GetComponent<ViewShop>();
        var displayData = shop == null ? null : DisplayDataField?.GetValue(shop) as ShopDisplayData;
        AddOwnedBooks(displayData?.TaiwuInventoryItemDisplayDataList);
        AddOwnedBooks(displayData?.TaiwuWarehouseItemDisplayDataList);
        AddOwnedBooks(displayData?.TaiwuTreasuryItemDisplayDataList);
    }

    private void AddOwnedBooks(IEnumerable<ItemDisplayData> items)
    {
        if (items == null)
            return;

        foreach (var item in items)
        {
            if (item != null && item.Amount > 0 && ShopOwnedLifeSkillBookSupport.IsLifeSkillBook(item))
                _ownedTemplateIds.Add(item.Key.TemplateId);
        }
    }
}

internal sealed class ShopOwnedLifeSkillBookMarker : MonoBehaviour
{
    private const string LabelObjectName = "BetterTaiwuScrollOwnedLifeSkillBookLabel";
    private TextMeshProUGUI _label;

    internal void Initialize(RowItemMain main)
    {
        if (_label != null || main?.ItemBack == null)
            return;

        var existing = main.ItemBack.transform.Find(LabelObjectName);
        if (existing != null)
            _label = existing.GetComponent<TextMeshProUGUI>();

        if (_label == null)
        {
            var labelObject = new GameObject(LabelObjectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = labelObject.GetComponent<RectTransform>();
            rect.SetParent(main.ItemBack.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(2f, 2f);
            rect.sizeDelta = new Vector2(50f, 20f);

            _label = labelObject.GetComponent<TextMeshProUGUI>();
            var fontSource = Array.Find(
                main.GetComponentsInChildren<TextMeshProUGUI>(true),
                text => text != null && text != _label && text.font != null);
            if (fontSource != null)
                _label.font = fontSource.font;
            _label.SetText(ModLocalization.T("已拥有"));
            _label.alignment = TextAlignmentOptions.BottomLeft;
            _label.fontStyle = FontStyles.Bold;
            _label.enableAutoSizing = true;
            _label.fontSizeMin = 10f;
            _label.fontSizeMax = 16f;
            _label.color = new Color32(75, 170, 255, 255);
            _label.outlineColor = new Color32(5, 20, 40, 255);
            _label.outlineWidth = 0.18f;
            _label.raycastTarget = false;
        }

        _label.transform.SetAsLastSibling();
    }

    internal void SetVisible(bool visible)
    {
        if (_label != null && _label.gameObject.activeSelf != visible)
            _label.gameObject.SetActive(visible);
    }

    internal void Remove()
    {
        if (_label != null)
            Destroy(_label.gameObject);
        Destroy(this);
    }
}
