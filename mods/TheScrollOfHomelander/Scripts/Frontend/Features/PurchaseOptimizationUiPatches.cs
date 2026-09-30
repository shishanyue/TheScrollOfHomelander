#nullable disable
#pragma warning disable CS0612

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FrameWork;
using HarmonyLib;
using GameData.Domains.Global;
using GameData.Domains.Item;
using GameData.Domains.Item.Display;
using GameData.Domains.Taiwu.ExchangeSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ExchangeContainerView = Game.Views.Exchange.ExchangeContainer;
using ExchangeViewBase = Game.Views.Exchange.ViewExchangeBase;
using ShopView = Game.Views.Exchange.ViewShop;

namespace BetterTaiwuScroll.Frontend;

[Serializable]
public sealed class PurchaseOptimizationSettings
{
    public int LowestPurchaseGrade = 9;
    public int HighestPurchaseGrade = 1;
    public bool SkipPriceIncreasedItems = true;
    public bool SkipOriginalPriceItems = false;
    public bool IncludeLimitedPurityLockedLevels = false;
    public bool IncludeMedicineMaterials = true;
    public bool IncludePoisonMaterials = false;
    public bool AutoPurchaseGroupMerchants = false;
    public int AutoPurchaseTargetSource = 1;
    public bool AutoPurchaseOnlyOnIndustryTile = false;
    public bool AutoPurchaseInventoryLoadProtection = true;
    public int AutoPurchaseInventoryLoadThresholdPercent = 90;
    public bool AutoPurchaseSkipInventoryOutsideIndustry = true;

    internal PurchaseOptimizationSettings Clone()
    {
        return (PurchaseOptimizationSettings)MemberwiseClone();
    }

    internal void Normalize()
    {
        HighestPurchaseGrade = Mathf.Clamp(HighestPurchaseGrade, 1, 9);
        LowestPurchaseGrade = Mathf.Clamp(LowestPurchaseGrade, 1, 9);
        if (HighestPurchaseGrade > LowestPurchaseGrade)
            LowestPurchaseGrade = HighestPurchaseGrade;
        AutoPurchaseTargetSource = Mathf.Clamp(AutoPurchaseTargetSource, 0, 2);
        AutoPurchaseInventoryLoadThresholdPercent = Mathf.Clamp(
            AutoPurchaseInventoryLoadThresholdPercent,
            10,
            100);
    }
}

internal static class PurchaseOptimizationSettingsStore
{
    private const string FileName = BetterTaiwuScroll.Shared.ModProtocol.PurchaseSettings;

    internal static PurchaseOptimizationSettings Current { get; private set; } = new PurchaseOptimizationSettings();

    internal static void Load()
    {
        try
        {
            foreach (var path in GetSettingsPathCandidates())
            {
                if (!File.Exists(path))
                    continue;

                var json = File.ReadAllText(path);
                Current = JsonUtility.FromJson<PurchaseOptimizationSettings>(json) ?? new PurchaseOptimizationSettings();
                if (!json.Contains("\"IncludeMedicineMaterials\""))
                    Current.IncludeMedicineMaterials = true;
                if (!json.Contains("\"IncludePoisonMaterials\""))
                    Current.IncludePoisonMaterials = false;
                if (!json.Contains("\"AutoPurchaseTargetSource\""))
                    Current.AutoPurchaseTargetSource = 1;
                if (!json.Contains("\"AutoPurchaseInventoryLoadProtection\""))
                    Current.AutoPurchaseInventoryLoadProtection = true;
                if (!json.Contains("\"AutoPurchaseInventoryLoadThresholdPercent\""))
                    Current.AutoPurchaseInventoryLoadThresholdPercent = 90;
                if (!json.Contains("\"AutoPurchaseSkipInventoryOutsideIndustry\""))
                    Current.AutoPurchaseSkipInventoryOutsideIndustry = true;
                break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to load purchase optimization settings: " + ex);
            Current = new PurchaseOptimizationSettings();
        }

        Current.Normalize();
    }

    internal static void Save()
    {
        try
        {
            Current.Normalize();
            var path = GetSettingsPath();
            AsyncSettingsSaveQueue.Enqueue(path, Current.Clone());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save purchase optimization settings: " + ex);
        }
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
}

[HarmonyPatch(typeof(ShopView), "OnInit")]
internal static class ViewShopPurchaseOptimizationOnInitPatch
{
    private static void Postfix(ShopView __instance)
    {
        ShopStockPageSupport.Apply(__instance);
        PurchaseOptimizationUiController.GetOrAdd(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(ExchangeViewBase), "RefreshButtons")]
internal static class ViewExchangeBasePurchaseOptimizationRefreshButtonsPatch
{
    private static void Postfix(ExchangeViewBase __instance)
    {
        if (__instance is ShopView shop)
        {
            ShopStockPageSupport.Apply(shop);
            PurchaseOptimizationUiController.GetOrAdd(shop)?.Refresh();
        }
    }
}

[HarmonyPatch(typeof(ExchangeViewBase), "RefreshSelfItems", new Type[] { typeof(int) })]
internal static class ViewExchangeBaseShopStockItemSourcePatch
{
    private const int StockPageIndex = 3;

    private static void Postfix(ExchangeViewBase __instance, int index)
    {
        if (index != StockPageIndex || __instance is not ShopView || !Plugin.EnableShopStockPage)
            return;

        __instance.Exchange?.SetItemSource((sbyte)ItemSourceType.Stock);
        ShopStockPageSupport.SyncStockToggleState(__instance);
    }
}

internal static class ShopStockPageSupport
{
    private const int StockPageIndex = 3;

    internal static void RefreshAllActive()
    {
        foreach (var shop in Resources.FindObjectsOfTypeAll<ShopView>())
            Apply(shop);
    }

    internal static void Apply(ShopView view)
    {
        if (view == null)
            return;

        var container = Traverse.Create(view).Field("exchangeContainer").GetValue<ExchangeContainerView>();
        var currPage = container?.currPage;
        var stockToggle = currPage?.Get(StockPageIndex);
        if (stockToggle == null)
            return;

        var shouldShow = Plugin.EnableShopStockPage && IsStockUnlocked();
        stockToggle.gameObject.SetActive(shouldShow);
        if (!shouldShow && currPage.GetActiveIndex() == StockPageIndex)
            currPage.Set(0);

        RefreshOriginalDisabledSourceTooltips(currPage);
        if (shouldShow && !stockToggle.interactable && currPage.GetActiveIndex() == StockPageIndex && view.Exchange != null)
            currPage.Set(0);
        else if (shouldShow && currPage.GetActiveIndex() == StockPageIndex)
            ForceStockToggleSelected(currPage, stockToggle);
    }

    internal static void SyncStockToggleState(ExchangeViewBase view)
    {
        var container = Traverse.Create(view).Field("exchangeContainer").GetValue<ExchangeContainerView>();
        var currPage = container?.currPage;
        var stockToggle = currPage?.Get(StockPageIndex);
        if (stockToggle == null || !stockToggle.gameObject.activeSelf || !stockToggle.interactable)
            return;

        ForceStockToggleSelected(currPage, stockToggle);
    }

    private static void ForceStockToggleSelected(CToggleGroup currPage, CToggle stockToggle)
    {
        if (currPage.GetActiveIndex() != StockPageIndex && stockToggle.isOn)
            stockToggle.SetIsOnWithoutNotify(false);

        currPage.SetWithoutNotify(StockPageIndex);
    }

    private static void RefreshOriginalDisabledSourceTooltips(CToggleGroup currPage)
    {
        if (currPage == null)
            return;

        for (var i = 1; i <= StockPageIndex; i++)
        {
            var toggle = currPage.Get(i);
            if (toggle == null)
                continue;

            var tooltip = toggle.GetComponent<TooltipInvoker>();
            if (tooltip != null)
                tooltip.enabled = toggle.gameObject.activeSelf && !toggle.interactable;
        }
    }

    private static bool IsStockUnlocked()
    {
        try
        {
            return SingletonObject.getInstance<FunctionLockManager>().IsFunctionUnlock(10);
        }
        catch
        {
            return true;
        }
    }
}

internal sealed class PurchaseOptimizationUiController : MonoBehaviour
{
    private const float ButtonScale = 0.8f;
    private const float ButtonWidth = 132f * ButtonScale;
    private const float ButtonHeight = 46f * ButtonScale;
    private const float SettingsOffsetX = -18f;
    private const float SettingsOffsetY = 80f * ButtonScale;
    private const float BatchButtonGap = 8f;
    private const int MaterialItemType = 5;
    private const short MedicineMaterialSubType = 505;
    private const short PoisonMaterialSubType = 506;
    private static readonly FieldInfo RefreshGoodsField = AccessTools.Field(typeof(ShopView), "refreshGoods");

    private ShopView _view;
    private CButton _settingsButton;
    private CButton _batchButton;
    private Coroutine _batchButtonLayoutCoroutine;

    internal static PurchaseOptimizationUiController GetOrAdd(ShopView view)
    {
        if (view == null)
            return null;

        var controller = view.GetComponent<PurchaseOptimizationUiController>();
        if (controller == null)
            controller = view.gameObject.AddComponent<PurchaseOptimizationUiController>();

        controller._view = view;
        return controller;
    }

    internal static void RefreshAll()
    {
        foreach (var controller in Resources.FindObjectsOfTypeAll<PurchaseOptimizationUiController>())
            controller.Refresh();
    }

    internal void Refresh()
    {
        if (_view == null)
            _view = GetComponent<ShopView>();

        if (!Plugin.EnableBulkPurchaseUi)
        {
            SetActive(_settingsButton, false);
            SetActive(_batchButton, false);
            return;
        }

        EnsureButtons();
        SetActive(_settingsButton, true);
        SetActive(_batchButton, PositionBatchButton());
        QueueBatchButtonLayoutRefresh();
    }

    private void EnsureButtons()
    {
        if (_view == null || (_settingsButton != null && _batchButton != null))
            return;

        var container = Traverse.Create(_view).Field("exchangeContainer").GetValue<ExchangeContainerView>();
        var buyBackToggle = container?.targetPage?.Get(7);
        var buyBackRect = buyBackToggle == null ? null : buyBackToggle.transform as RectTransform;
        var batchAnchorRect = GetBatchButtonAnchorRect(container);
        var defaultAnchor = buyBackRect ?? batchAnchorRect;
        if (defaultAnchor == null || defaultAnchor.parent == null)
            return;

        var template = NativeContinuousMakeUiTemplates.FindArrangementSettingButtonTemplate();
        if (template == null)
        {
            NativeContinuousMakeUiTemplates.RequestArrangementSettingButtonTemplate(_ => EnsureButtons());
            return;
        }

        if (_settingsButton == null && buyBackRect != null)
            _settingsButton = CreateButton(template, buyBackRect, "BetterTaiwuScrollPurchaseSettingsButton", "设置", SettingsOffsetX, SettingsOffsetY, OpenSettingsPanel);
        if (_batchButton == null)
            _batchButton = CreateButton(template, defaultAnchor, "BetterTaiwuScrollBulkPurchaseButton", "批量采购", 0f, 0f, OnBatchPurchaseClicked);

        PositionBatchButton(container);
        PurchaseOptimizationSettingsPanel.Preload();
    }

    private bool PositionBatchButton()
    {
        var container = _view == null ? null : Traverse.Create(_view).Field("exchangeContainer").GetValue<ExchangeContainerView>();
        return PositionBatchButton(container);
    }

    private bool PositionBatchButton(ExchangeContainerView container)
    {
        var buttonRect = _batchButton == null ? null : _batchButton.transform as RectTransform;
        var targetRect = GetBatchButtonAnchorRect(container);
        if (buttonRect == null || targetRect == null || targetRect.parent == null)
            return false;

        if (buttonRect.parent != targetRect.parent)
            buttonRect.SetParent(targetRect.parent, false);

        buttonRect.anchorMin = targetRect.anchorMin;
        buttonRect.anchorMax = targetRect.anchorMax;
        buttonRect.pivot = targetRect.pivot;
        buttonRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        buttonRect.localScale = Vector3.one;
        ConfigureBatchButtonLayoutElement(buttonRect.gameObject);
        buttonRect.SetSiblingIndex(Math.Max(targetRect.GetSiblingIndex(), 0));
        if (TryRefreshParentLayout(targetRect.parent as RectTransform))
            return true;

        buttonRect.anchoredPosition = GetLeftSidePosition(targetRect, buttonRect.pivot);
        return true;
    }

    private void QueueBatchButtonLayoutRefresh()
    {
        if (!isActiveAndEnabled || _batchButton == null)
            return;

        if (_batchButtonLayoutCoroutine != null)
            return;

        _batchButtonLayoutCoroutine = StartCoroutine(RefreshBatchButtonLayoutAfterNativeLayout());
    }

    private IEnumerator RefreshBatchButtonLayoutAfterNativeLayout()
    {
        yield return null;
        _batchButtonLayoutCoroutine = null;
        if (isActiveAndEnabled && _view != null && Plugin.EnableBulkPurchaseUi)
            PositionBatchButton();
    }

    private void OnDisable()
    {
        if (_batchButtonLayoutCoroutine != null)
            StopCoroutine(_batchButtonLayoutCoroutine);
        _batchButtonLayoutCoroutine = null;
    }

    private RectTransform GetBatchButtonAnchorRect(ExchangeContainerView container)
    {
        return GetButtonRect(GetRefreshGoodsButton()) ?? GetButtonRect(container?.putTargetAll);
    }

    private CButton GetRefreshGoodsButton()
    {
        var button = RefreshGoodsField?.GetValue(_view) as CButton;
        if (button == null || button.gameObject == null || !button.gameObject.activeSelf)
            return null;

        return button;
    }

    private static RectTransform GetButtonRect(CButton button)
    {
        return button == null ? null : button.transform as RectTransform;
    }

    private static void ConfigureBatchButtonLayoutElement(GameObject buttonObj)
    {
        var layout = buttonObj.GetComponent<LayoutElement>() ?? buttonObj.AddComponent<LayoutElement>();
        layout.ignoreLayout = false;
        layout.minWidth = ButtonWidth;
        layout.preferredWidth = ButtonWidth;
        layout.flexibleWidth = 0f;
        layout.minHeight = ButtonHeight;
        layout.preferredHeight = ButtonHeight;
        layout.flexibleHeight = 0f;
    }

    private static bool TryRefreshParentLayout(RectTransform parentRect)
    {
        if (parentRect == null || parentRect.GetComponent<LayoutGroup>() == null)
            return false;

        LayoutRebuilder.MarkLayoutForRebuild(parentRect);
        return true;
    }

    private static Vector2 GetLeftSidePosition(RectTransform targetRect, Vector2 pivot)
    {
        var targetWidth = Mathf.Abs(targetRect.rect.width);
        if (targetWidth < 1f)
            targetWidth = Mathf.Abs(targetRect.sizeDelta.x);
        if (targetWidth < 1f)
            targetWidth = ButtonWidth;

        var targetHeight = Mathf.Abs(targetRect.rect.height);
        if (targetHeight < 1f)
            targetHeight = Mathf.Abs(targetRect.sizeDelta.y);
        if (targetHeight < 1f)
            targetHeight = ButtonHeight;

        var targetLeft = targetRect.anchoredPosition.x - targetRect.pivot.x * targetWidth;
        var targetCenterY = targetRect.anchoredPosition.y + (0.5f - targetRect.pivot.y) * targetHeight;
        var x = targetLeft - BatchButtonGap - (1f - pivot.x) * ButtonWidth;
        var y = targetCenterY - (0.5f - pivot.y) * ButtonHeight;
        return new Vector2(x, y);
    }

    private CButton CreateButton(CButton template, RectTransform anchor, string name, string text, float offsetX, float offsetY, Action onClick)
    {
        var buttonObj = Instantiate(template.gameObject, anchor.parent, false);
        buttonObj.name = name;

        var rect = buttonObj.transform as RectTransform;
        rect.anchorMin = anchor.anchorMin;
        rect.anchorMax = anchor.anchorMax;
        rect.pivot = anchor.pivot;
        rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        rect.anchoredPosition = anchor.anchoredPosition + new Vector2(offsetX, offsetY);
        rect.localScale = Vector3.one;
        rect.SetSiblingIndex(Math.Max(anchor.GetSiblingIndex(), 0) + 1);

        var button = buttonObj.GetComponent<CButton>() ?? buttonObj.GetComponentInChildren<CButton>(true);
        if (button == null)
        {
            Destroy(buttonObj);
            return null;
        }

        foreach (var childButton in buttonObj.GetComponentsInChildren<CButton>(true))
            childButton.interactable = true;

        button.ClearAndAddListener(onClick);
        SetButtonText(buttonObj, ModLocalization.T(text));
        (buttonObj.GetComponent<ButtonTextOverride>() ?? buttonObj.AddComponent<ButtonTextOverride>()).SetText(ModLocalization.T(text));
        ConfigureTooltip(buttonObj, text);
        buttonObj.SetActive(true);
        return button;
    }

    private void OpenSettingsPanel()
    {
        PurchaseOptimizationSettingsPanel.Show(_view);
    }

    private void OnBatchPurchaseClicked()
    {
        RunBulkPurchase();
    }

    private void RunBulkPurchase()
    {
        var exchange = _view?.Exchange as ShopExchange;
        if (exchange == null)
            return;

        PurchaseOptimizationSettingsStore.Load();
        var settings = PurchaseOptimizationSettingsStore.Current;
        var selectedCount = 0;

        for (var page = 0; page <= 6; page++)
        {
            if (!exchange.IsPageShow(page))
                continue;

            var remainingLimitedCount = GetRemainingLimitedCount(exchange, page, settings.IncludeLimitedPurityLockedLevels);
            if (remainingLimitedCount == 0)
                continue;

            var items = _view.GetTargetTradeableList(page);
            if (items == null)
                continue;

            foreach (var item in items)
            {
                var count = GetSelectableMaterialCount(exchange, item, page, settings, ref remainingLimitedCount);
                if (count <= 0)
                    continue;

                exchange.SelectTargetItem(item, count);
                selectedCount += count;
            }
        }

        Traverse.Create(_view).Method("Refresh").GetValue();
        Debug.Log($"[BetterTaiwuScroll] Bulk purchase selected {selectedCount} material items.");
    }

    private static int GetSelectableMaterialCount(ShopExchange exchange, ITradeableContent item, int page, PurchaseOptimizationSettings settings, ref int remainingLimitedCount)
    {
        if (item == null || item.Amount <= 0)
            return 0;

        if (!ShopExchange.IsShopItem(item) || item.ItemSourceType - 10 != page)
            return 0;

        if (item.UsingType != ItemDisplayData.ItemUsingType.Invalid || item.ItemSourceType == 0)
            return 0;

        if (item.RealKey.ItemType != MaterialItemType)
            return 0;

        if (!IsMaterialSubTypeAllowed(item, settings))
            return 0;

        var grade = GetMaterialConfigGrade(item);
        var highestRawGrade = DisplayGradeToRaw(settings.HighestPurchaseGrade);
        var lowestRawGrade = DisplayGradeToRaw(settings.LowestPurchaseGrade);
        if (grade > highestRawGrade || grade < lowestRawGrade)
            return 0;

        var priceChangePercent = exchange.GetPriceChangePercentValue(item, true);
        if (settings.SkipPriceIncreasedItems && priceChangePercent > 0)
            return 0;
        if (settings.SkipOriginalPriceItems && priceChangePercent == 0)
            return 0;

        var remaining = item.Amount - GetSelectedTargetAmount(exchange, item);
        if (remaining <= 0)
            return 0;

        if (remainingLimitedCount < 0)
            return remaining;

        var count = Mathf.Min(remaining, remainingLimitedCount);
        remainingLimitedCount -= count;
        return count;
    }

    private static bool IsMaterialSubTypeAllowed(ITradeableContent item, PurchaseOptimizationSettings settings)
    {
        var subType = GetMaterialSubType(item);
        return subType switch
        {
            MedicineMaterialSubType => settings.IncludeMedicineMaterials,
            PoisonMaterialSubType => settings.IncludePoisonMaterials,
            _ => true,
        };
    }

    private static short GetMaterialSubType(ITradeableContent item)
    {
        if (item == null)
            return 0;

        try
        {
            return Config.Material.Instance[item.RealKey.TemplateId].ItemSubType;
        }
        catch
        {
            return 0;
        }
    }

    private static sbyte GetMaterialConfigGrade(ITradeableContent item)
    {
        if (item == null)
            return 0;

        try
        {
            return Config.Material.Instance[item.RealKey.TemplateId].Grade;
        }
        catch
        {
            return item.Grade;
        }
    }

    private static int DisplayGradeToRaw(int displayGrade)
    {
        return 9 - Mathf.Clamp(displayGrade, 1, 9);
    }

    private static int GetRemainingLimitedCount(ShopExchange exchange, int page, bool includeLimitedLevels)
    {
        if (exchange.MinDebtLevel >= page)
            return -1;

        if (!includeLimitedLevels)
            return 0;

        var overFavorData = exchange.TradeArguments?.OverFavorData;
        var levelDataArray = overFavorData?.MerchantOverFavorLevelDataArray;
        if (levelDataArray == null || !levelDataArray.CheckIndex(page))
            return 0;

        var levelData = levelDataArray[page];
        if (levelData == null)
            return 0;

        var buyCount = levelData.BuyCount;
        if (buyCount == short.MaxValue)
            return -1;

        return Mathf.Max(0, buyCount);
    }

    private static int GetSelectedTargetAmount(ShopExchange exchange, ITradeableContent item)
    {
        var selected = 0;
        var targetContentList = exchange.TargetContentList;
        for (var i = 0; i < targetContentList.Count; i++)
        {
            var selectedItem = targetContentList[i];
            if (selectedItem == null)
                continue;

            if (selectedItem.RealKey.Equals(item.RealKey) &&
                selectedItem.CharacterId == item.CharacterId &&
                selectedItem.ItemSourceType == item.ItemSourceType)
            {
                selected += selectedItem.Amount;
            }
        }

        return selected;
    }

    private static void SetButtonText(GameObject buttonObj, string text)
    {
        if (buttonObj == null)
            return;

        foreach (var label in buttonObj.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label == null)
                continue;

                label.SetText(text);
                label.enableAutoSizing = true;
                label.fontSizeMax = Math.Min(label.fontSizeMax <= 0f ? 24f : label.fontSizeMax, 24f);
                label.fontSizeMin = Math.Max(label.fontSizeMin, 14f);
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

    private static void ConfigureTooltip(GameObject buttonObj, string text)
    {
        foreach (var tooltip in buttonObj.GetComponentsInChildren<TooltipInvoker>(true))
        {
            if (tooltip == null)
                continue;

            tooltip.enabled = true;
            tooltip.Type = TipType.Simple;
            tooltip.IsLanguageKey = false;
            tooltip.NeedRefresh = false;
            tooltip.PresetParam = new[]
            {
                ModLocalization.T(text),
                ModLocalization.T(text == "设置" ? "打开批量采购设置。" : "按照设置把符合条件的商品加入买入列表。")
            };
        }
    }

    private static void SetActive(Component component, bool active)
    {
        if (component != null)
            component.gameObject.SetActive(active);
    }

    private sealed class ButtonTextOverride : MonoBehaviour
    {
        private string _text;
        private TMP_Text[] _labels;

        internal void SetText(string text)
        {
            if (_text == text && _labels != null)
                return;
            _text = text ?? string.Empty;
            _labels ??= GetComponentsInChildren<TMP_Text>(true);
            Apply();
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_text))
                return;

            Apply();
        }

        private void Apply()
        {
            _labels ??= GetComponentsInChildren<TMP_Text>(true);
            foreach (var label in _labels)
            {
                if (label != null && label.text != _text)
                    label.SetText(_text);
            }
        }
    }
}
