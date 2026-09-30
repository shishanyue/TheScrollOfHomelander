#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components.Item;
using Game.Components.ListStyleGeneralScroll.Item;
using Game.Components.SortAndFilter;
using Game.Views.CharacterMenu;
using GameData.Domains.Character.Display;
using GameData.Domains.Item.Display;
using GameDataExtensions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTaiwuScroll.Frontend;

[HarmonyPatch(typeof(ViewCharacterMenuItems), "Awake")]
internal static class InventorySearchBoxAwakePatch
{
    private static void Postfix(ViewCharacterMenuItems __instance)
    {
        InventorySearchBoxOptimizationSupport.GetOrAddState(__instance);
    }
}

[HarmonyPatch(typeof(ViewCharacterMenuItems), "Refresh")]
internal static class InventorySearchBoxViewRefreshPatch
{
    private static void Postfix(ViewCharacterMenuItems __instance)
    {
        InventorySearchBoxOptimizationSupport.ApplyFilter(__instance);
    }
}

[HarmonyPatch(typeof(MultiplyItemListScroll), "RefreshItems")]
internal static class InventorySearchBoxMultiplyRefreshPatch
{
    private static void Postfix(MultiplyItemListScroll __instance)
    {
        var view = __instance == null ? null : __instance.GetComponentInParent<ViewCharacterMenuItems>(true);
        if (view != null)
            InventorySearchBoxOptimizationSupport.ApplyFilter(view);
    }
}

internal static class InventorySearchBoxOptimizationSupport
{
    private const float SearchWidth = 210f;
    private const float SearchHeight = 38f;
    private const float SearchGap = 10f;
    private const float SearchMinWidth = 120f;
    private const string SearchObjectName = "BetterTaiwuScrollInventorySearchBox";

    private static readonly AccessTools.FieldRef<ViewCharacterMenuItems, ItemListScroll> ItemListScrollRef =
        AccessTools.FieldRefAccess<ViewCharacterMenuItems, ItemListScroll>("itemListScroll");

    private static readonly AccessTools.FieldRef<ViewCharacterMenuItems, MultiplyItemListScroll> MultiplyItemListScrollRef =
        AccessTools.FieldRefAccess<ViewCharacterMenuItems, MultiplyItemListScroll>("multiplyItemListScroll");

    private static readonly AccessTools.FieldRef<ViewCharacterMenuItems, CharacterItemsDisplayData> CharacterItemsDisplayDataRef =
        AccessTools.FieldRefAccess<ViewCharacterMenuItems, CharacterItemsDisplayData>("_characterItemsDisplayData");

    private static readonly AccessTools.FieldRef<ViewCharacterMenuItems, GameObject> ItemListScrollListStyleRef =
        AccessTools.FieldRefAccess<ViewCharacterMenuItems, GameObject>("itemListScrollListStyle");

    private static readonly AccessTools.FieldRef<ViewCharacterMenuItems, GameObject> ItemListScrollCardStyleRef =
        AccessTools.FieldRefAccess<ViewCharacterMenuItems, GameObject>("itemListScrollCardStyle");

    internal static InventorySearchBoxState GetOrAddState(ViewCharacterMenuItems view)
    {
        if (view == null)
            return null;

        var state = view.GetComponent<InventorySearchBoxState>();
        if (state == null)
            state = view.gameObject.AddComponent<InventorySearchBoxState>();

        state.Initialize(view);
        return state;
    }

    internal static void EnsureToggleLineSearchBox(ToggleGroupLine line)
    {
        var owner = line == null ? null : line.GetComponentInParent<SortAndFilter>(true);
        var view = owner == null ? null : owner.GetComponentInParent<ViewCharacterMenuItems>(true);
        if (view == null || !IsMainInventoryListOwner(view, owner) || !IsFirstToggleGroupLine(owner, line))
            return;

        var state = GetOrAddState(view);
        if (state == null)
            return;

        if (!Plugin.EnableInventorySearchBoxOptimization)
        {
            state.HideSearchBox();
            state.ApplyFilter();
            return;
        }

        var toggleRoot = Traverse.Create(line).Field("toggleRoot").GetValue<RectTransform>();
        if (toggleRoot == null)
            return;

        state.EnsureSearchBoxNearToggleRoot(toggleRoot);
    }

    internal static void ApplyFilter(ViewCharacterMenuItems view)
    {
        var state = view == null ? null : view.GetComponent<InventorySearchBoxState>();
        state?.ApplyFilter();
    }

    internal static ItemListScroll GetItemListScroll(ViewCharacterMenuItems view)
    {
        return view == null ? null : ItemListScrollRef(view);
    }

    internal static MultiplyItemListScroll GetMultiplyItemListScroll(ViewCharacterMenuItems view)
    {
        return view == null ? null : MultiplyItemListScrollRef(view);
    }

    internal static List<ItemDisplayData> GetInventoryItems(ViewCharacterMenuItems view)
    {
        var data = view == null ? null : CharacterItemsDisplayDataRef(view);
        return data?.InventoryItems;
    }

    internal static void SyncInventoryListMode(ViewCharacterMenuItems view, ItemListScroll itemListScroll = null)
    {
        if (view == null)
            return;

        itemListScroll ??= GetItemListScroll(view);
        if (itemListScroll == null)
            return;

        var isCardMode = itemListScroll.IsCardMode;
        var listStyle = ItemListScrollListStyleRef(view);
        var cardStyle = ItemListScrollCardStyleRef(view);
        SetActive(listStyle, !isCardMode);
        SetActive(cardStyle, isCardMode);

        if (ContainerDefaultCardModePatch.ShouldUseDefaultCardMode(itemListScroll))
        {
            ContainerDefaultCardModePatch.SyncModeVisibility(itemListScroll);
            ContainerDefaultCardModePatch.SyncToggleState(itemListScroll);
        }
    }

    private static void SetActive(GameObject obj, bool active)
    {
        if (obj != null && obj.activeSelf != active)
            obj.SetActive(active);
    }

    private static bool IsMainInventoryListOwner(ViewCharacterMenuItems view, SortAndFilter owner)
    {
        var itemListScroll = GetItemListScroll(view);
        return itemListScroll != null
               && owner != null
               && owner.transform.IsChildOf(itemListScroll.transform);
    }

    private static bool IsFirstToggleGroupLine(SortAndFilter owner, ToggleGroupLine line)
    {
        if (owner == null || line == null)
            return false;

        var firstToggleGroupLine = Traverse.Create(owner).Field("firstToggleGroupLine").GetValue<ToggleGroupLine>();
        return firstToggleGroupLine == line;
    }

    internal static void RefreshAllActive()
    {
        foreach (var line in Resources.FindObjectsOfTypeAll<ToggleGroupLine>())
        {
            if (line == null || !line.gameObject.scene.IsValid())
                continue;

            EnsureToggleLineSearchBox(line);
        }

        foreach (var state in Resources.FindObjectsOfTypeAll<InventorySearchBoxState>())
            state?.RefreshFromSettings();
    }

    internal static void RestoreAll()
    {
        foreach (var state in Resources.FindObjectsOfTypeAll<InventorySearchBoxState>())
            state?.Restore();
    }

    internal static string SearchObjectNameValue => SearchObjectName;
    internal static float SearchWidthValue => SearchWidth;
    internal static float SearchHeightValue => SearchHeight;
    internal static float SearchGapValue => SearchGap;

    internal static bool TryFindLastActiveFilterToggleRect(RectTransform toggleRoot, out RectTransform rect)
    {
        rect = null;
        if (toggleRoot == null)
            return false;

        for (var i = 0; i < toggleRoot.childCount; i++)
        {
            var child = toggleRoot.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy)
                continue;

            var childRect = child as RectTransform;
            if (childRect == null || child.GetComponent<FilterToggle>() == null)
                continue;

            rect = childRect;
        }

        return rect != null;
    }

    internal static bool PositionSearchBoxAfterToggle(
        RectTransform inputRect,
        RectTransform parent,
        RectTransform anchorToggle,
        float preferredWidth,
        float preferredHeight,
        float gap)
    {
        if (inputRect == null || parent == null || anchorToggle == null)
            return false;

        if (parent.rect.width <= 0f || parent.rect.height <= 0f || anchorToggle.rect.width <= 0f)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
        }

        var corners = new Vector3[4];
        anchorToggle.GetWorldCorners(corners);
        var rightMiddle = (corners[2] + corners[3]) * 0.5f;
        var center = (corners[0] + corners[2]) * 0.5f;
        var rightLocal = (Vector2)parent.InverseTransformPoint(rightMiddle);
        var centerLocal = (Vector2)parent.InverseTransformPoint(center);

        var left = rightLocal.x + gap;
        var availableWidth = parent.rect.xMax - left - gap;
        var width = availableWidth > 0f
            ? Mathf.Clamp(availableWidth, Math.Min(SearchMinWidth, preferredWidth), preferredWidth)
            : preferredWidth;

        var anchor = new Vector2(0f, 0.5f);
        inputRect.anchorMin = anchor;
        inputRect.anchorMax = anchor;
        inputRect.pivot = new Vector2(0f, 0.5f);
        inputRect.anchoredPosition = LocalPointToAnchoredPosition(parent, new Vector2(left, centerLocal.y), anchor);
        inputRect.sizeDelta = new Vector2(width, preferredHeight);
        inputRect.localScale = Vector3.one;
        inputRect.SetAsLastSibling();
        UiLayoutRefreshQueue.Request(parent);
        return true;
    }

    private static Vector2 LocalPointToAnchoredPosition(RectTransform parent, Vector2 localPoint, Vector2 anchor)
    {
        var anchorReference = new Vector2(
            (anchor.x - parent.pivot.x) * parent.rect.width,
            (anchor.y - parent.pivot.y) * parent.rect.height);
        return localPoint - anchorReference;
    }
}

internal sealed class InventorySearchBoxState : MonoBehaviour
{
    private static TMP_InputField _cachedSearchInputTemplate;

    private ViewCharacterMenuItems _view;
    private TMP_InputField _input;
    private RectTransform _inputRect;
    private string _searchText = string.Empty;
    private bool _isApplying;
    private bool _listOverridden;
    private readonly Dictionary<ItemKey, string> _itemNames = new Dictionary<ItemKey, string>();
    private object _lastSource;
    private int _lastSourceCount = -1;

    internal void Initialize(ViewCharacterMenuItems view)
    {
        if (_view == view)
            return;

        _view = view;
    }

    internal void EnsureSearchBoxNearToggleRoot(RectTransform toggleRoot)
    {
        if (toggleRoot == null)
            return;

        if (!InventorySearchBoxOptimizationSupport.TryFindLastActiveFilterToggleRect(toggleRoot, out var anchorToggle))
        {
            HideSearchBox();
            return;
        }

        var parent = toggleRoot.parent as RectTransform ?? toggleRoot;
        if (_input == null)
            _input = CreateSearchInput(parent);

        if (_input == null)
            return;

        if (_input.transform.parent != parent)
            _input.transform.SetParent(parent, false);

        _input.gameObject.SetActive(true);
        _inputRect = _input.transform as RectTransform;
        var layoutElement = _input.GetComponent<LayoutElement>() ?? _input.gameObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;
        RefreshSearchBoxLayout(parent, anchorToggle);
    }

    private void RefreshSearchBoxLayout(RectTransform parent, RectTransform anchorToggle)
    {
        if (_inputRect == null || parent == null || anchorToggle == null)
            return;

        if (!InventorySearchBoxOptimizationSupport.PositionSearchBoxAfterToggle(
                _inputRect,
                parent,
                anchorToggle,
                InventorySearchBoxOptimizationSupport.SearchWidthValue,
                InventorySearchBoxOptimizationSupport.SearchHeightValue,
                InventorySearchBoxOptimizationSupport.SearchGapValue))
        {
            HideSearchBox();
        }
    }

    private static float CalculateToggleLineWidth(RectTransform toggleRoot)
    {
        var activeCount = CountActiveFilterToggles(toggleRoot);
        if (activeCount <= 0)
            return 0f;

        var grid = toggleRoot.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            return grid.padding.left
                   + activeCount * grid.cellSize.x
                   + Math.Max(activeCount - 1, 0) * grid.spacing.x;
        }

        var horizontal = toggleRoot.GetComponent<HorizontalLayoutGroup>();
        if (horizontal != null)
        {
            var width = (float)horizontal.padding.left;
            var seen = 0;
            foreach (var rect in EnumerateActiveFilterToggleRects(toggleRoot))
            {
                width += Mathf.Max(GetPreferredWidth(rect), rect.rect.width);
                seen++;
            }

            width += Math.Max(seen - 1, 0) * horizontal.spacing;
            return width;
        }

        var fallbackWidth = 0f;
        var fallbackCount = 0;
        foreach (var rect in EnumerateActiveFilterToggleRects(toggleRoot))
        {
            fallbackWidth += Mathf.Max(GetPreferredWidth(rect), rect.rect.width);
            fallbackCount++;
        }

        return fallbackWidth + Math.Max(fallbackCount - 1, 0) * SimplifiedFilterToggleVisual.SimplifiedSpacing;
    }

    private static int CountActiveFilterToggles(RectTransform toggleRoot)
    {
        var count = 0;
        foreach (var _ in EnumerateActiveFilterToggleRects(toggleRoot))
            count++;

        return count;
    }

    private static IEnumerable<RectTransform> EnumerateActiveFilterToggleRects(RectTransform toggleRoot)
    {
        for (var i = 0; i < toggleRoot.childCount; i++)
        {
            var child = toggleRoot.GetChild(i);
            if (child == null || !child.gameObject.activeSelf)
                continue;

            var rect = child as RectTransform;
            if (rect == null || child.GetComponent<FilterToggle>() == null)
                continue;

            yield return rect;
        }
    }

    private static float GetPreferredWidth(RectTransform rect)
    {
        if (rect == null)
            return 0f;

        var layout = rect.GetComponent<LayoutElement>();
        if (layout != null && layout.preferredWidth > 0f)
            return layout.preferredWidth;

        return LayoutUtility.GetPreferredWidth(rect);
    }

    internal void HideSearchBox()
    {
        if (_input != null)
            _input.gameObject.SetActive(false);
    }

    internal void RefreshFromSettings()
    {
        if (!Plugin.EnableInventorySearchBoxOptimization)
        {
            HideSearchBox();
            ApplyFilter();
        }
        else if (_input != null)
        {
            _input.gameObject.SetActive(true);
            ApplyFilter();
        }
    }

    internal void Restore()
    {
        if (_input != null)
        {
            _input.onValueChanged.RemoveListener(OnSearchInputChanged);
            _input.onEndEdit.RemoveListener(OnSearchInputChanged);
            DestroyUnityObject(_input.gameObject);
            _input = null;
            _inputRect = null;
        }

        _searchText = string.Empty;
        _listOverridden = false;
        ApplyFilter();
    }

    internal void ApplyFilter()
    {
        if (_isApplying || _view == null)
            return;

        var itemListScroll = InventorySearchBoxOptimizationSupport.GetItemListScroll(_view);
        var multiplyItemListScroll = InventorySearchBoxOptimizationSupport.GetMultiplyItemListScroll(_view);
        if (itemListScroll == null || multiplyItemListScroll == null)
            return;

        if (multiplyItemListScroll.IsMultiItemSelect || multiplyItemListScroll.IsMultiplyLock)
            return;

        var source = InventorySearchBoxOptimizationSupport.GetInventoryItems(_view);
        if (source == null)
            return;
        if (!ReferenceEquals(_lastSource, source) || _lastSourceCount != source.Count)
        {
            _lastSource = source;
            _lastSourceCount = source.Count;
            _itemNames.Clear();
        }

        var hasSearch = Plugin.EnableInventorySearchBoxOptimization && !string.IsNullOrWhiteSpace(_searchText);
        if (!hasSearch && !_listOverridden)
        {
            InventorySearchBoxOptimizationSupport.SyncInventoryListMode(_view, itemListScroll);
            return;
        }

        IReadOnlyList<ITradeableContent> targetList;
        if (!hasSearch)
        {
            targetList = source;
        }
        else
        {
            var keyword = _searchText.Trim();
            var filtered = new List<ITradeableContent>(source.Count);
            for (var i = 0; i < source.Count; i++)
            {
                var item = source[i];
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
            targetList = filtered;
        }

        _isApplying = true;
        try
        {
            itemListScroll.SetItemList(targetList);
            _listOverridden = hasSearch;
            InventorySearchBoxOptimizationSupport.SyncInventoryListMode(_view, itemListScroll);
        }
        finally
        {
            _isApplying = false;
        }
    }

    private TMP_InputField CreateSearchInput(RectTransform parent)
    {
        var template = FindSearchInputTemplate();
        TMP_InputField input;
        if (template != null)
        {
            input = Instantiate(template, parent);
            input.gameObject.name = InventorySearchBoxOptimizationSupport.SearchObjectNameValue;
            NormalizeClonedInput(input);
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

    private static TMP_InputField FindSearchInputTemplate()
    {
        if (_cachedSearchInputTemplate != null)
            return _cachedSearchInputTemplate;

        foreach (var input in Resources.FindObjectsOfTypeAll<TMP_InputField>())
        {
            if (input == null || input.gameObject.name == InventorySearchBoxOptimizationSupport.SearchObjectNameValue)
                continue;

            var placeholderText = (input.placeholder as TMP_Text)?.text ?? string.Empty;
            var inputName = input.gameObject.name ?? string.Empty;
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

    private static void NormalizeClonedInput(TMP_InputField input)
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

        var rect = input.transform as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(
                InventorySearchBoxOptimizationSupport.SearchWidthValue,
                InventorySearchBoxOptimizationSupport.SearchHeightValue);
        }
    }

    private static TMP_InputField CreateFallbackInput(RectTransform parent)
    {
        var root = new GameObject(
            InventorySearchBoxOptimizationSupport.SearchObjectNameValue,
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
        NormalizeClonedInput(input);
        return input;
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

    private void OnDestroy()
    {
        if (_input != null)
        {
            _input.onValueChanged.RemoveListener(OnSearchInputChanged);
            _input.onEndEdit.RemoveListener(OnSearchInputChanged);
        }
    }

    private static void DestroyUnityObject(UnityEngine.Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}
