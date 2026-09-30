#nullable disable
#pragma warning disable CS0612

using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Components.ListStyleGeneralScroll.Item;
using Game.Components.SortAndFilter;
using Game.Views.Exchange;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTaiwuScroll.Frontend;

[HarmonyPatch(typeof(ViewExchangeBase), "RefreshButtons")]
internal static class ViewExchangeBaseFilterCategorySyncRefreshButtonsPatch
{
    private static void Postfix(ViewExchangeBase __instance)
    {
        ExchangeFilterCategorySyncButton.GetOrAdd(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(SortAndFilter), "OnFirstToggleGroupChanged")]
internal static class SortAndFilterFirstToggleGroupCategorySyncPatch
{
    private static void Postfix(SortAndFilter __instance)
    {
        ExchangeFilterCategorySyncSupport.OnFirstToggleGroupChanged(__instance);
    }
}

internal static class ExchangeFilterCategorySyncSupport
{
    private static readonly FieldInfo FirstToggleGroupIndexField = AccessTools.Field(typeof(SortAndFilter), "_firstToggleGroupIndex");
    private static readonly FieldInfo FirstToggleGroupLineField = AccessTools.Field(typeof(SortAndFilter), "firstToggleGroupLine");
    private static readonly FieldInfo ExchangeContainerField = AccessTools.Field(typeof(ViewExchangeBase), "exchangeContainer");
    private static readonly FieldInfo ItemListSortAndFilterField = AccessTools.Field(typeof(ItemListScroll), "sortAndFilter");
    private static bool _runtimeEnabled = true;
    private static bool _isSyncing;

    internal static bool IsEnabled => Plugin.EnableExchangeFilterCategorySync && _runtimeEnabled;

    internal static void OnSettingChanged()
    {
        _runtimeEnabled = Plugin.EnableExchangeFilterCategorySync;
        RefreshAllButtons();
    }

    internal static void ToggleRuntimeEnabled()
    {
        _runtimeEnabled = !_runtimeEnabled;
        RefreshAllButtons();
    }

    internal static void RefreshAllButtons()
    {
        foreach (var controller in Resources.FindObjectsOfTypeAll<ExchangeFilterCategorySyncButton>())
            controller.Refresh();
    }

    internal static void OnFirstToggleGroupChanged(SortAndFilter source)
    {
        if (!IsEnabled || _isSyncing || source == null)
            return;

        if (!TryGetFirstToggleGroupState(source, out _, out var toggleKey))
            return;

        if (!TryFindExchangePair(source, out var view, out var destinationList, out var selfSort, out var targetSort, out var destination))
            return;

        if (!TryGetFirstToggleGroupLineId(destination, out var destinationLineId))
            return;

        try
        {
            _isSyncing = true;
            var toggleIndex = toggleKey.IsAll ? -1 : toggleKey.Index;

            // ViewShop drives its two panels through ItemListScroll.SortAndFilterController.
            // Updating the raw SortAndFilter view can leave the controller state at "All",
            // which is why merchant right-to-left sync looked selected only after another refresh.
            destinationList.SortAndFilterController.SetToggleIsOnWithoutNotify(destinationLineId, toggleIndex);
            destination.Config?.OnFilterChanged?.Invoke(destinationLineId);
            FilterMemoryController.TrySave(destination);
            RefreshDestinationAfterSyncedFilter(view, destination);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to sync exchange first toggle category: " + ex);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private static bool TryGetFirstToggleGroupState(SortAndFilter source, out int lineId, out Game.Components.SortAndFilter.ToggleKey toggleKey)
    {
        lineId = -1;
        toggleKey = Game.Components.SortAndFilter.ToggleKey.AllKey;
        if (source == null)
            return false;

        var firstToggleGroupIndex = GetFirstToggleGroupIndex(source);
        if (firstToggleGroupIndex < 0 || source.Config?.LineConfigs == null || firstToggleGroupIndex >= source.Config.LineConfigs.Count)
            return false;

        lineId = source.Config.LineConfigs[firstToggleGroupIndex].Id;
        var firstToggleGroupLine = GetFirstToggleGroupLine(source);
        if (firstToggleGroupLine == null || !firstToggleGroupLine.gameObject.activeInHierarchy)
            return false;

        toggleKey = firstToggleGroupLine.GetLineState().ToggleGroupState;
        return lineId >= 0;
    }

    private static bool TryGetFirstToggleGroupLineId(SortAndFilter source, out int lineId)
    {
        lineId = -1;
        if (source == null)
            return false;

        var firstToggleGroupIndex = GetFirstToggleGroupIndex(source);
        if (firstToggleGroupIndex < 0 || source.Config?.LineConfigs == null || firstToggleGroupIndex >= source.Config.LineConfigs.Count)
            return false;

        var firstToggleGroupLine = GetFirstToggleGroupLine(source);
        if (firstToggleGroupLine == null || !firstToggleGroupLine.gameObject.activeInHierarchy)
            return false;

        lineId = source.Config.LineConfigs[firstToggleGroupIndex].Id;
        return lineId >= 0;
    }

    private static bool TryFindExchangePair(
        SortAndFilter source,
        out ViewExchangeBase view,
        out ItemListScroll destinationList,
        out SortAndFilter selfSort,
        out SortAndFilter targetSort,
        out SortAndFilter destination)
    {
        view = null;
        destinationList = null;
        selfSort = null;
        targetSort = null;
        destination = null;

        if (source == null)
            return false;

        var ownerView = source.GetComponentInParent<ViewExchangeBase>(true);
        if (TryResolveExchangePairInView(source, ownerView, out view, out destinationList,
                out selfSort, out targetSort, out destination))
            return true;

        foreach (var candidate in Resources.FindObjectsOfTypeAll<ViewExchangeBase>())
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
                continue;

            if (TryResolveExchangePairInView(source, candidate, out view, out destinationList,
                    out selfSort, out targetSort, out destination))
                return true;
        }

        return false;
    }

    private static bool TryResolveExchangePairInView(
        SortAndFilter source,
        ViewExchangeBase candidate,
        out ViewExchangeBase view,
        out ItemListScroll destinationList,
        out SortAndFilter selfSort,
        out SortAndFilter targetSort,
        out SortAndFilter destination)
    {
        view = null;
        destinationList = null;
        selfSort = null;
        targetSort = null;
        destination = null;

        if (source == null || candidate == null || !candidate.gameObject.activeInHierarchy)
            return false;

        if (!TryGetPanelSortAndFilters(candidate, out var candidateSelfSort, out var candidateTargetSort,
                out var selfList, out var targetList))
            return false;

        if (ReferenceEquals(source, candidateSelfSort))
        {
            view = candidate;
            selfSort = candidateSelfSort;
            targetSort = candidateTargetSort;
            destination = candidateTargetSort;
            destinationList = targetList;
            return destination != null && destinationList?.SortAndFilterController != null;
        }

        if (ReferenceEquals(source, candidateTargetSort))
        {
            view = candidate;
            selfSort = candidateSelfSort;
            targetSort = candidateTargetSort;
            destination = candidateSelfSort;
            destinationList = selfList;
            return destination != null && destinationList?.SortAndFilterController != null;
        }

        return false;
    }

    private static bool TryGetPanelSortAndFilters(
        ViewExchangeBase view,
        out SortAndFilter selfSort,
        out SortAndFilter targetSort,
        out ItemListScroll selfList,
        out ItemListScroll targetList)
    {
        selfSort = null;
        targetSort = null;
        selfList = null;
        targetList = null;

        var container = GetExchangeContainer(view);
        if (container == null)
            return false;

        selfList = container.selfItemList;
        targetList = container.targetItemList;
        selfSort = GetSortAndFilter(selfList);
        targetSort = GetSortAndFilter(targetList);
        return selfSort != null && targetSort != null;
    }

    private static SortAndFilter GetSortAndFilter(ItemListScroll list)
    {
        return list == null ? null : ItemListSortAndFilterField?.GetValue(list) as SortAndFilter;
    }

    private static int GetFirstToggleGroupIndex(SortAndFilter source)
    {
        return source == null || FirstToggleGroupIndexField == null
            ? -1
            : (int)FirstToggleGroupIndexField.GetValue(source);
    }

    private static Game.Components.SortAndFilter.ToggleGroupLine GetFirstToggleGroupLine(SortAndFilter source)
    {
        return source == null ? null : FirstToggleGroupLineField?.GetValue(source) as Game.Components.SortAndFilter.ToggleGroupLine;
    }

    private static ExchangeContainer GetExchangeContainer(ViewExchangeBase view)
    {
        return view == null ? null : ExchangeContainerField?.GetValue(view) as ExchangeContainer;
    }

    private static void RefreshDestinationAfterSyncedFilter(ViewExchangeBase view, SortAndFilter destination)
    {
        if (view == null || destination == null)
            return;

        Traverse.Create(view).Method("RefreshPutButtons").GetValue();
    }
}

internal sealed class ExchangeFilterCategorySyncButton : MonoBehaviour
{
    private const float ButtonOffsetX = -120f;
    private const float MerchantCloseOffsetX = -120f;
    private const float MerchantFallbackRight = -220f;
    private const float MerchantFallbackTop = -82f;
    private static readonly Vector2 ButtonSize = new Vector2(132f, 52f);
    private static readonly Color ActiveRed = new Color(0.58f, 0.08f, 0.06f, 1f);

    private ViewExchangeBase _view;
    private CButton _button;
    private readonly List<Graphic> _coloredGraphics = new();
    private readonly List<Color> _originalColors = new();

    internal static ExchangeFilterCategorySyncButton GetOrAdd(ViewExchangeBase view)
    {
        if (view == null)
            return null;

        var controller = view.GetComponent<ExchangeFilterCategorySyncButton>();
        if (controller == null)
            controller = view.gameObject.AddComponent<ExchangeFilterCategorySyncButton>();

        controller._view = view;
        return controller;
    }

    internal void Refresh()
    {
        if (_view == null)
            _view = GetComponent<ViewExchangeBase>();

        if (!Plugin.EnableExchangeFilterCategorySync)
        {
            SetVisible(false);
            return;
        }

        EnsureButton();
        SetVisible(_button != null);
        ApplyVisualState();
    }

    private void EnsureButton()
    {
        if (_button != null || _view == null)
            return;

        var container = Traverse.Create(_view).Field("exchangeContainer").GetValue<ExchangeContainer>();
        var template = NativeContinuousMakeUiTemplates.FindArrangementSettingButtonTemplate();
        if (template == null)
        {
            NativeContinuousMakeUiTemplates.RequestArrangementSettingButtonTemplate(_ => Refresh());
            return;
        }

        if (!TryGetButtonParentAndLayout(out var parent, out var anchorMin, out var anchorMax, out var pivot,
                out var sizeDelta, out var anchoredPosition))
            return;

        if (parent == null)
            return;

        var obj = Instantiate(template.gameObject, parent, false);
        obj.name = "BetterTaiwuScrollExchangeFilterCategorySyncButton";
        var rect = obj.transform as RectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = ButtonSize;
        rect.anchoredPosition = anchoredPosition;
        rect.localScale = Vector3.one;
        rect.SetAsLastSibling();

        _button = obj.GetComponent<CButton>() ?? obj.GetComponentInChildren<CButton>(true);
        if (_button == null)
        {
            Destroy(obj);
            return;
        }

        _button.interactable = true;
        _button.ClearAndAddListener(ExchangeFilterCategorySyncSupport.ToggleRuntimeEnabled);
        SetButtonText(obj, ModLocalization.T("Sync"));
        (obj.GetComponent<ButtonTextOverride>() ?? obj.AddComponent<ButtonTextOverride>()).SetText(ModLocalization.T("Sync"));
        ConfigureTooltip(obj);
        CaptureColoredGraphics(obj);
        obj.SetActive(true);
    }

    private bool TryGetButtonParentAndLayout(out Transform parent, out Vector2 anchorMin, out Vector2 anchorMax,
        out Vector2 pivot, out Vector2 sizeDelta, out Vector2 anchoredPosition)
    {
        parent = null;
        anchorMin = new Vector2(1f, 1f);
        anchorMax = new Vector2(1f, 1f);
        pivot = new Vector2(0.5f, 0.5f);
        sizeDelta = new Vector2(58f, 58f);
        anchoredPosition = new Vector2(MerchantFallbackRight, MerchantFallbackTop);

        var container = Traverse.Create(_view).Field("exchangeContainer").GetValue<ExchangeContainer>();
        var quick = FindCurrentQuickEncyclopedia();
        var quickRect = quick == null ? null : quick.transform as RectTransform;
        if (quickRect != null && quickRect.parent != null)
        {
            parent = quickRect.parent;
            anchorMin = quickRect.anchorMin;
            anchorMax = quickRect.anchorMax;
            pivot = quickRect.pivot;
            sizeDelta = ButtonSize;
            anchoredPosition = quickRect.anchoredPosition + new Vector2(ButtonOffsetX, 0f);
            return true;
        }

        var closeRect = container?.hide == null ? null : container.hide.transform as RectTransform;
        if (closeRect != null && closeRect.parent != null)
        {
            parent = closeRect.parent;
            anchorMin = closeRect.anchorMin;
            anchorMax = closeRect.anchorMax;
            pivot = closeRect.pivot;
            sizeDelta = ButtonSize;
            anchoredPosition = closeRect.anchoredPosition + new Vector2(MerchantCloseOffsetX, 0f);
            return true;
        }

        var viewRect = _view.transform as RectTransform;
        if (viewRect != null)
        {
            parent = viewRect;
            return true;
        }

        return false;
    }

    private Game.Components.Common.QuickEncyclopedia FindCurrentQuickEncyclopedia()
    {
        if (_view == null)
            return null;

        foreach (var quick in _view.GetComponentsInChildren<Game.Components.Common.QuickEncyclopedia>(true))
        {
            if (quick != null && !IsOurSyncButton(quick.gameObject))
                return quick;
        }

        return null;
    }

    private static bool IsOurSyncButton(GameObject obj)
    {
        return obj != null && obj.name.StartsWith("BetterTaiwuScrollExchangeFilterCategorySyncButton", StringComparison.Ordinal);
    }

    private void ConfigureTooltip(GameObject obj)
    {
        foreach (var tooltip in obj.GetComponentsInChildren<TooltipInvoker>(true))
        {
            if (tooltip == null)
                continue;

            tooltip.enabled = true;
            tooltip.Type = TipType.Simple;
            tooltip.IsLanguageKey = false;
            tooltip.NeedRefresh = false;
            tooltip.PresetParam = new[]
            {
                ModLocalization.T("Sync Both Sides"),
                ModLocalization.T("When enabled, item-category filters stay in sync on both sides.")
            };
        }
    }

    private void CaptureColoredGraphics(GameObject obj)
    {
        if (_coloredGraphics.Count > 0 || obj == null)
            return;

        foreach (var graphic in obj.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic == null)
                continue;

            _coloredGraphics.Add(graphic);
            _originalColors.Add(graphic.color);
        }
    }

    private void ApplyVisualState()
    {
        var active = ExchangeFilterCategorySyncSupport.IsEnabled;
        for (var i = 0; i < _coloredGraphics.Count; i++)
        {
            var graphic = _coloredGraphics[i];
            if (graphic == null)
                continue;

            graphic.color = active ? ActiveRed : _originalColors[i];
        }
    }

    private void SetVisible(bool visible)
    {
        if (_button != null)
            _button.gameObject.SetActive(visible);
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
            label.fontSizeMax = Math.Min(label.fontSizeMax <= 0f ? 30f : label.fontSizeMax, 30f);
            label.fontSizeMin = Math.Max(label.fontSizeMin, 18f);
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
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
