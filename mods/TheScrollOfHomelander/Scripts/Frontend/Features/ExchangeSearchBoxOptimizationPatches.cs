#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FrameWork.UISystem.UIElements;
using Game.Components.ListStyleGeneralScroll.Item;
using Game.Components.SortAndFilter;
using Game.Views.Exchange;
using GameDataExtensions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTaiwuScroll.Frontend;

[HarmonyPatch(typeof(ViewExchangeBase), "RefreshButtons")]
internal static class ViewExchangeBaseSearchBoxRefreshButtonsPatch
{
    private static void Postfix(ViewExchangeBase __instance)
    {
        ExchangeSearchBoxOptimizationSupport.GetOrAddState(__instance)?.Refresh();
    }
}

internal static class ExchangeSearchBoxOptimizationSupport
{
    internal const float SearchWidth = 210f;
    internal const float SearchHeight = 38f;
    internal const float SearchGap = 10f;
    internal const float LegacySearchGapY = 8f;
    internal const string SearchObjectPrefix = "BetterTaiwuScrollExchangeSearchBox";

    private static readonly FieldInfo ExchangeContainerField = AccessTools.Field(typeof(ViewExchangeBase), "exchangeContainer");
    private static readonly FieldInfo FirstToggleGroupLineField = AccessTools.Field(typeof(SortAndFilter), "firstToggleGroupLine");
    private static readonly FieldInfo ToggleRootField = AccessTools.Field(typeof(ToggleGroupLine), "toggleRoot");
    private static readonly ConditionalWeakTable<ItemListScroll, ListPanelBinding> PanelBindings =
        new ConditionalWeakTable<ItemListScroll, ListPanelBinding>();
    private static bool _isApplying;

    private sealed class ListPanelBinding
    {
        internal ExchangeSearchBoxState State;
        internal ExchangePanelSearchBox Panel;
    }

    internal static bool IsApplying => _isApplying;

    internal static ExchangeSearchBoxState GetOrAddState(ViewExchangeBase view)
    {
        if (view == null)
            return null;

        var state = view.GetComponent<ExchangeSearchBoxState>();
        if (state == null)
            state = view.gameObject.AddComponent<ExchangeSearchBoxState>();

        state.Initialize(view);
        return state;
    }

    internal static IReadOnlyList<ITradeableContent> PrepareItemList(
        ItemListScroll list,
        IReadOnlyList<ITradeableContent> source)
    {
        if (_isApplying || list == null)
            return source;

        if (!TryResolvePanel(list, out var state, out var panel))
            return source;

        if (!Plugin.EnableInventorySearchBoxOptimization)
        {
            panel.Hide();
            panel.SetSource(source);
            panel.MarkListRestored();
            return source;
        }

        return panel.PrepareItemList(source);
    }

    internal static void ApplyFilteredList(ItemListScroll list, IReadOnlyList<ITradeableContent> items)
    {
        if (list == null)
            return;

        _isApplying = true;
        try
        {
            list.SetItemList(items);
        }
        finally
        {
            _isApplying = false;
        }
    }

    internal static void RefreshAllActive()
    {
        foreach (var view in Resources.FindObjectsOfTypeAll<ViewExchangeBase>())
        {
            if (view == null || !view.gameObject.scene.IsValid())
                continue;

            GetOrAddState(view)?.Refresh();
        }

        foreach (var state in Resources.FindObjectsOfTypeAll<ExchangeSearchBoxState>())
            state?.RefreshFromSettings();
    }

    internal static void RestoreAll()
    {
        foreach (var state in Resources.FindObjectsOfTypeAll<ExchangeSearchBoxState>())
            state?.Restore();
    }

    internal static ExchangeContainer GetExchangeContainer(ViewExchangeBase view)
    {
        return view == null ? null : ExchangeContainerField?.GetValue(view) as ExchangeContainer;
    }

    private static bool TryResolvePanel(ItemListScroll list, out ExchangeSearchBoxState state, out ExchangePanelSearchBox panel)
    {
        var binding = PanelBindings.GetOrCreateValue(list);
        state = binding.State;
        panel = binding.Panel;
        if (panel != null)
            return true;

        var view = list.GetComponentInParent<ViewExchangeBase>(true);
        if (view != null && TryResolvePanelInView(view, list, out state, out panel))
        {
            binding.State = state;
            binding.Panel = panel;
            return true;
        }

        return false;
    }

    private static bool TryResolvePanelInView(
        ViewExchangeBase view,
        ItemListScroll list,
        out ExchangeSearchBoxState state,
        out ExchangePanelSearchBox panel)
    {
        state = null;
        panel = null;

        var container = GetExchangeContainer(view);
        if (container == null)
            return false;

        if (ReferenceEquals(list, container.selfItemList))
        {
            state = GetOrAddState(view);
            panel = state?.SelfPanel;
            return panel != null;
        }

        if (ReferenceEquals(list, container.targetItemList))
        {
            state = GetOrAddState(view);
            panel = state?.TargetPanel;
            return panel != null;
        }

        return false;
    }

    internal static bool IsItemMatchSearch(ITradeableContent item, string keyword)
    {
        if (item == null || string.IsNullOrWhiteSpace(keyword))
            return true;

        var name = item.GetName();
        return !string.IsNullOrEmpty(name)
               && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static bool TryFindFilterToggleAnchor(
        ItemListScroll list,
        out RectTransform parent,
        out RectTransform anchorToggle)
    {
        parent = null;
        anchorToggle = null;
        if (list == null)
            return false;

        foreach (var owner in list.GetComponentsInChildren<SortAndFilter>(true))
        {
            if (owner == null)
                continue;

            var firstLine = FirstToggleGroupLineField?.GetValue(owner) as ToggleGroupLine;
            var toggleRoot = firstLine == null ? null : ToggleRootField?.GetValue(firstLine) as RectTransform;
            if (toggleRoot == null)
                continue;

            if (!InventorySearchBoxOptimizationSupport.TryFindLastActiveFilterToggleRect(toggleRoot, out anchorToggle))
                continue;

            parent = toggleRoot.parent as RectTransform ?? toggleRoot;
            return parent != null;
        }

        return false;
    }
}

internal sealed class ExchangeSearchBoxState : MonoBehaviour
{
    private ViewExchangeBase _view;

    internal ExchangePanelSearchBox SelfPanel { get; private set; }
    internal ExchangePanelSearchBox TargetPanel { get; private set; }

    internal void Initialize(ViewExchangeBase view)
    {
        if (_view == view)
            return;

        _view = view;
        SelfPanel = new ExchangePanelSearchBox("Self");
        TargetPanel = new ExchangePanelSearchBox("Target");
    }

    internal void Refresh()
    {
        if (_view == null)
            _view = GetComponent<ViewExchangeBase>();

        var container = ExchangeSearchBoxOptimizationSupport.GetExchangeContainer(_view);
        if (container == null)
        {
            SelfPanel?.Hide();
            TargetPanel?.Hide();
            return;
        }

        if (!Plugin.EnableInventorySearchBoxOptimization)
        {
            SelfPanel?.Hide();
            TargetPanel?.Hide();
            return;
        }

        SelfPanel?.Ensure(container.currPage, container.selfItemList);
        TargetPanel?.Ensure(container.targetPage, container.targetItemList);
    }

    internal void RefreshFromSettings()
    {
        if (!Plugin.EnableInventorySearchBoxOptimization)
        {
            SelfPanel?.HideAndRestore();
            TargetPanel?.HideAndRestore();
            return;
        }

        Refresh();
        SelfPanel?.ApplyFilter();
        TargetPanel?.ApplyFilter();
    }

    internal void Restore()
    {
        SelfPanel?.Destroy();
        TargetPanel?.Destroy();
    }

    private void OnDestroy()
    {
        Restore();
    }
}

internal sealed class ExchangePanelSearchBox
{
    private static TMP_InputField _cachedSearchInputTemplate;

    private readonly string _nameSuffix;
    private TMP_InputField _input;
    private RectTransform _inputRect;
    private ItemListScroll _list;
    private IReadOnlyList<ITradeableContent> _source;
    private readonly Dictionary<ItemKey, string> _itemNames = new Dictionary<ItemKey, string>();
    private int _sourceCount = -1;
    private string _searchText = string.Empty;
    private bool _listOverridden;

    internal ExchangePanelSearchBox(string nameSuffix)
    {
        _nameSuffix = nameSuffix;
    }

    internal void Ensure(CToggleGroup pageGroup, ItemListScroll list)
    {
        _list = list;
        if (pageGroup == null || list == null)
        {
            Hide();
            return;
        }

        var useLegacyWarehouseLayout = list.GetComponentInParent<ViewWarehouse>(true) != null;
        RectTransform parent;
        RectTransform anchor;
        if (useLegacyWarehouseLayout)
        {
            var firstToggle = FindFirstVisibleToggle(pageGroup);
            anchor = firstToggle == null ? null : firstToggle.transform as RectTransform;
            parent = anchor == null ? null : anchor.parent as RectTransform;
        }
        else if (!ExchangeSearchBoxOptimizationSupport.TryFindFilterToggleAnchor(list, out parent, out anchor))
        {
            Hide();
            return;
        }

        if (parent == null || anchor == null)
        {
            Hide();
            return;
        }

        if (_input == null)
            _input = CreateSearchInput(parent);

        if (_input == null)
            return;

        if (_input.transform.parent != parent)
            _input.transform.SetParent(parent, false);

        _input.gameObject.SetActive(true);
        _inputRect = _input.transform as RectTransform;

        var layout = _input.GetComponent<LayoutElement>() ?? _input.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        if (useLegacyWarehouseLayout)
            RefreshLegacyWarehouseLayout(parent, anchor);
        else
            RefreshLayout(parent, anchor);
    }

    internal void SetSource(IReadOnlyList<ITradeableContent> source)
    {
        var count = source?.Count ?? 0;
        if (!ReferenceEquals(_source, source) || _sourceCount != count)
            _itemNames.Clear();
        _source = source;
        _sourceCount = count;
    }

    internal IReadOnlyList<ITradeableContent> PrepareItemList(IReadOnlyList<ITradeableContent> source)
    {
        SetSource(source);
        var hasSearch = Plugin.EnableInventorySearchBoxOptimization
                        && !string.IsNullOrWhiteSpace(_searchText);
        _listOverridden = hasSearch;
        return hasSearch ? BuildFilteredList(_searchText.Trim()) : source;
    }

    internal void MarkListRestored()
    {
        _listOverridden = false;
    }

    internal void ApplyFilter()
    {
        if (_list == null || _source == null || ExchangeSearchBoxOptimizationSupport.IsApplying)
            return;

        var hasSearch = Plugin.EnableInventorySearchBoxOptimization && !string.IsNullOrWhiteSpace(_searchText);
        var targetList = hasSearch ? BuildFilteredList(_searchText.Trim()) : _source;

        if (!hasSearch && !_listOverridden)
            return;

        ExchangeSearchBoxOptimizationSupport.ApplyFilteredList(_list, targetList);
        _listOverridden = hasSearch;
    }

    private IReadOnlyList<ITradeableContent> BuildFilteredList(string keyword)
    {
        var filtered = new List<ITradeableContent>(_source?.Count ?? 0);
        if (_source == null)
            return filtered;

        for (var i = 0; i < _source.Count; i++)
        {
            var item = _source[i];
            if (item == null)
                continue;
            if (!_itemNames.TryGetValue(item.Key, out var name))
            {
                name = item.GetName() ?? string.Empty;
                _itemNames[item.Key] = name;
            }
            if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                filtered.Add(item);
        }

        return filtered;
    }

    internal void Hide()
    {
        if (_input != null)
            _input.gameObject.SetActive(false);
    }

    internal void HideAndRestore()
    {
        Hide();
        if (_list != null && _source != null)
            ExchangeSearchBoxOptimizationSupport.ApplyFilteredList(_list, _source);
        _listOverridden = false;
    }

    internal void Destroy()
    {
        if (_input != null)
        {
            _input.onValueChanged.RemoveListener(OnSearchInputChanged);
            _input.onEndEdit.RemoveListener(OnSearchInputChanged);
            DestroyUnityObject(_input.gameObject);
            _input = null;
            _inputRect = null;
        }
    }

    private void RefreshLayout(RectTransform parent, RectTransform anchorToggle)
    {
        if (_inputRect == null || parent == null || anchorToggle == null)
            return;

        if (!InventorySearchBoxOptimizationSupport.PositionSearchBoxAfterToggle(
                _inputRect,
                parent,
                anchorToggle,
                ExchangeSearchBoxOptimizationSupport.SearchWidth,
                ExchangeSearchBoxOptimizationSupport.SearchHeight,
                ExchangeSearchBoxOptimizationSupport.SearchGap))
        {
            Hide();
        }
    }

    private void RefreshLegacyWarehouseLayout(RectTransform parent, RectTransform firstRect)
    {
        if (_inputRect == null || parent == null || firstRect == null)
            return;

        var firstWidth = Mathf.Max(firstRect.rect.width, LayoutUtility.GetPreferredWidth(firstRect));
        var firstHeight = Mathf.Max(firstRect.rect.height, LayoutUtility.GetPreferredHeight(firstRect));
        if (firstWidth <= 0f || firstHeight <= 0f)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            firstWidth = Mathf.Max(firstRect.rect.width, LayoutUtility.GetPreferredWidth(firstRect));
            firstHeight = Mathf.Max(firstRect.rect.height, LayoutUtility.GetPreferredHeight(firstRect));
        }

        var centerX = firstRect.anchoredPosition.x + (0.5f - firstRect.pivot.x) * firstWidth;
        var topY = firstRect.anchoredPosition.y
                   + (1f - firstRect.pivot.y) * firstHeight
                   + ExchangeSearchBoxOptimizationSupport.LegacySearchGapY
                   + ExchangeSearchBoxOptimizationSupport.SearchHeight * 0.5f;

        _inputRect.anchorMin = firstRect.anchorMin;
        _inputRect.anchorMax = firstRect.anchorMax;
        _inputRect.pivot = new Vector2(0.5f, 0.5f);
        _inputRect.sizeDelta = new Vector2(
            ExchangeSearchBoxOptimizationSupport.SearchWidth,
            ExchangeSearchBoxOptimizationSupport.SearchHeight);
        _inputRect.anchoredPosition = new Vector2(centerX, topY);
        _inputRect.localScale = Vector3.one;
        _inputRect.SetAsLastSibling();
        UiLayoutRefreshQueue.Request(parent);
    }

    private CToggle FindFirstVisibleToggle(CToggleGroup pageGroup)
    {
        var toggles = pageGroup.GetAll();
        if (toggles == null)
            return null;

        foreach (var toggle in toggles)
        {
            if (toggle != null && toggle.gameObject.activeSelf)
                return toggle;
        }

        return null;
    }

    private TMP_InputField CreateSearchInput(RectTransform parent)
    {
        var template = FindSearchInputTemplate();
        TMP_InputField input;
        if (template != null)
        {
            input = UnityEngine.Object.Instantiate(template, parent);
            input.gameObject.name = ExchangeSearchBoxOptimizationSupport.SearchObjectPrefix + _nameSuffix;
            NormalizeInput(input);
        }
        else
        {
            input = CreateFallbackInput(parent);
        }

        input.onValueChanged.RemoveAllListeners();
        input.onEndEdit.RemoveAllListeners();
        input.onValueChanged.AddListener(OnSearchInputChanged);
        input.onEndEdit.AddListener(OnSearchInputChanged);
        input.SetTextWithoutNotify(_searchText);
        return input;
    }

    private TMP_InputField CreateFallbackInput(RectTransform parent)
    {
        var root = new GameObject(
            ExchangeSearchBoxOptimizationSupport.SearchObjectPrefix + _nameSuffix,
            typeof(RectTransform),
            typeof(Image),
            typeof(TMP_InputField));
        root.transform.SetParent(parent, false);

        var image = root.GetComponent<Image>();
        image.color = new Color(0.08f, 0.065f, 0.045f, 0.86f);

        var input = root.GetComponent<TMP_InputField>();
        input.targetGraphic = image;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 24;
        input.caretColor = new Color(1f, 0.88f, 0.48f, 1f);
        input.selectionColor = new Color(0.8f, 0.55f, 0.2f, 0.35f);

        var viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(root.transform, false);
        var viewportRect = viewport.transform as RectTransform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(12f, 2f);
        viewportRect.offsetMax = new Vector2(-10f, -2f);

        var placeholder = CreateText(viewportRect, "Placeholder", ModLocalization.T("Enter keyword"), new Color(0.85f, 0.78f, 0.62f, 0.65f));
        var text = CreateText(viewportRect, "Text", string.Empty, new Color(0.98f, 0.95f, 0.84f, 1f));
        text.gameObject.SetActive(true);

        input.textViewport = viewportRect;
        input.placeholder = placeholder;
        input.textComponent = text;
        NormalizeInput(input);
        return input;
    }

    private static TMP_InputField FindSearchInputTemplate()
    {
        if (_cachedSearchInputTemplate != null)
            return _cachedSearchInputTemplate;

        foreach (var input in Resources.FindObjectsOfTypeAll<TMP_InputField>())
        {
            if (input == null)
                continue;

            var inputName = input.gameObject.name ?? string.Empty;
            if (inputName.StartsWith(ExchangeSearchBoxOptimizationSupport.SearchObjectPrefix, StringComparison.Ordinal) ||
                inputName == InventorySearchBoxOptimizationSupport.SearchObjectNameValue)
                continue;

            var placeholderText = (input.placeholder as TMP_Text)?.text ?? string.Empty;
            if (placeholderText.Contains("输入搜索关键字") ||
                placeholderText.Contains("输入关键字") ||
                inputName.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _cachedSearchInputTemplate = input;
                return input;
            }
        }

        return null;
    }

    private static void NormalizeInput(TMP_InputField input)
    {
        if (input == null)
            return;

        input.gameObject.SetActive(true);
        input.interactable = true;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 24;
        input.SetTextWithoutNotify(string.Empty);
        if (input.placeholder is TMP_Text placeholder)
        {
            placeholder.SetText(ModLocalization.T("Enter keyword"));
            placeholder.color = new Color(0.85f, 0.78f, 0.62f, 0.65f);
            placeholder.enableAutoSizing = true;
            placeholder.fontSizeMax = Math.Min(placeholder.fontSizeMax <= 0f ? 22f : placeholder.fontSizeMax, 19f);
            placeholder.fontSizeMin = Math.Max(placeholder.fontSizeMin, 12f);
        }

        if (input.textComponent != null)
        {
            input.textComponent.color = new Color(0.98f, 0.95f, 0.84f, 1f);
            input.textComponent.enableAutoSizing = true;
            input.textComponent.fontSizeMax = Math.Min(input.textComponent.fontSizeMax <= 0f ? 22f : input.textComponent.fontSizeMax, 19f);
            input.textComponent.fontSizeMin = Math.Max(input.textComponent.fontSizeMin, 12f);
        }
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, string text, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        var rect = obj.transform as RectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var label = obj.GetComponent<TextMeshProUGUI>();
        label.SetText(text);
        label.color = color;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableAutoSizing = true;
        label.fontSizeMax = 19f;
        label.fontSizeMin = 12f;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private void OnSearchInputChanged(string text)
    {
        var nextText = text ?? string.Empty;
        if (string.Equals(_searchText, nextText, StringComparison.Ordinal))
            return;

        _searchText = nextText;
        ApplyFilter();
    }

    private static void DestroyUnityObject(UnityEngine.Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            UnityEngine.Object.Destroy(obj);
        else
            UnityEngine.Object.DestroyImmediate(obj);
    }
}
