#nullable disable
#pragma warning disable CS0612

using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using FwUi = FrameWork.UISystem.UIElements;
using SysSetting = Game.Views.SystemSetting;
using UGui = UnityEngine.UI;

namespace BetterTaiwuScroll.Frontend;

internal sealed class PurchaseOptimizationSettingsPanel : MonoBehaviour
{
    private const string PanelName = "BetterTaiwuScrollPurchaseOptimizationSettingsPanel";
    private const string ContentName = "BetterTaiwuScrollPurchaseOptimizationSettingsContent";
    private const string InputBlockerName = "BetterTaiwuScrollPurchaseOptimizationInputBlocker";
    private const float PanelMinWidth = 1220f;
    private const float PanelMinHeight = 1040f;
    private const float ContentWidth = 940f;
    private const float ContentHeight = 820f;
    private const float ContentTopOffset = 150f;
    private const float RowHeight = 52f;
    private const float RowSpacing = 12f;
    private const int PanelSortingOrder = 5000;

    private static readonly string[] GradeNames =
    {
        "神一品",
        "绝二品",
        "超三品",
        "极四品",
        "秘五品",
        "妙六品",
        "上七品",
        "中八品",
        "下九品"
    };

    private static readonly string[] StorageNames = { "行囊", "私库", "公库" };
    private static readonly int[] LoadThresholdPercents = { 50, 60, 70, 80, 85, 90, 95, 100 };
    private static readonly string[] LoadThresholdNames =
        Array.ConvertAll(LoadThresholdPercents, value => value + "%");

    private static PurchaseOptimizationSettingsPanel _current;
    private static bool _requestingArchiveTemplate;
    private static Action _onArchiveTemplateReady;
    private static RectTransform _cachedArchivePanelSource;

    private RectTransform _panelRoot;
    private RectTransform _contentRoot;
    private Action _escHandler;
    private readonly List<Action> _controlRebinds = new List<Action>();
    private bool _syncing;
    private bool _masked;

    internal static void Show(MonoBehaviour owner)
    {
        PurchaseOptimizationSettingsStore.Load();

        if (!NativeContinuousMakeSettingsTemplates.Ready)
        {
            NativeContinuousMakeSettingsTemplates.Request(() => Show(owner));
            return;
        }

        EnsureArchiveTemplate(() => CreateFromRevertArchiveTemplate(UIElement.RevertArchive.UiBase.gameObject));
    }

    internal static void Preload()
    {
        if (!NativeContinuousMakeSettingsTemplates.Ready)
            NativeContinuousMakeSettingsTemplates.Request(null);

        EnsureArchiveTemplate(null);
    }

    private static void EnsureArchiveTemplate(Action onReady)
    {
        if (UIElement.RevertArchive.UiBase != null)
        {
            onReady?.Invoke();
            return;
        }

        if (onReady != null)
            _onArchiveTemplateReady += onReady;

        if (_requestingArchiveTemplate)
            return;

        _requestingArchiveTemplate = true;
        UIElement.RevertArchive.PrepareRes(false, _ =>
        {
            _requestingArchiveTemplate = false;
            var callback = _onArchiveTemplateReady;
            _onArchiveTemplateReady = null;
            callback?.Invoke();
        });
    }

    private static void CreateFromRevertArchiveTemplate(GameObject template)
    {
        if (template == null)
            return;

        if (_current != null)
            _current.Close();

        var sourcePanel = _cachedArchivePanelSource;
        if (sourcePanel == null)
        {
            sourcePanel = FindRevertArchivePanelSource(template);
            _cachedArchivePanelSource = sourcePanel;
        }

        if (sourcePanel == null)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to find purchase settings panel source.");
            return;
        }

        Transform parent = UIManager.Instance.GetLayer(UILayer.LayerVeryTop);
        if (parent == null)
            parent = UIManager.Instance.transform;

        var panelObj = Instantiate(sourcePanel.gameObject, parent, false);
        panelObj.name = PanelName;
        panelObj.SetActive(false);

        var panel = panelObj.AddComponent<PurchaseOptimizationSettingsPanel>();
        panel._panelRoot = panelObj.transform as RectTransform;
        panel.ConfigureClone();
        panel.Build();
        panel.ConfigureInputLayer();
        panel.EnablePanelRendering();

        panelObj.SetActive(true);
        panel.RebindControlEvents();
        panelObj.transform.SetAsLastSibling();
        panel.Mask();
        _current = panel;
    }

    private void ConfigureClone()
    {
        if (_panelRoot != null)
        {
            _panelRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRoot.pivot = new Vector2(0.5f, 0.5f);
            _panelRoot.anchoredPosition = Vector2.zero;
            _panelRoot.localScale = Vector3.one;
            SetMinSize(_panelRoot, PanelMinWidth, PanelMinHeight);
        }

        var view = GetComponent<Game.Views.RecordSelect.ViewRevertArchive>();
        TextMeshProUGUI title = null;
        Component scroll = null;
        CButton confirmBtn = null;

        if (view != null)
        {
            var traverse = Traverse.Create(view);
            title = traverse.Field("revertTitle").GetValue<TextMeshProUGUI>();
            scroll = traverse.Field("scroll").GetValue<Component>();
            confirmBtn = traverse.Field("confirmBtn").GetValue<CButton>();
            view.enabled = false;
        }

        title ??= FindTitleText();
        scroll ??= FindComponentByTypeName("InfinityScroll");
        confirmBtn ??= FindDeep(transform, "EnterGame")?.GetComponent<CButton>();
        if (title != null)
            title.SetText(ModLocalization.T("批量采购设置"));

        if (scroll != null)
            scroll.gameObject.SetActive(false);
        if (confirmBtn != null)
            confirmBtn.gameObject.SetActive(false);

        HideByName(transform, "EnterGame");
        HideArchiveHeaderRow();
        BindCloseButton();
        _contentRoot = CreateContentRoot(scroll == null ? null : scroll.transform as RectTransform);
    }

    private void EnablePanelRendering()
    {
        foreach (var group in GetComponentsInChildren<CanvasGroup>(true))
        {
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
        }

        var rootCanvas = GetComponent<Canvas>();
        if (rootCanvas != null)
        {
            rootCanvas.enabled = true;
            rootCanvas.overrideSorting = true;
            rootCanvas.sortingOrder = 200;
            rootCanvas.worldCamera = UIManager.Instance.UiCamera;
        }

        foreach (var canvas in GetComponentsInChildren<Canvas>(true))
        {
            canvas.enabled = true;
            if (canvas != rootCanvas)
                canvas.worldCamera = UIManager.Instance.UiCamera;
        }

        foreach (var raycaster in GetComponentsInChildren<UGui.GraphicRaycaster>(true))
            raycaster.enabled = true;

        foreach (var graphic in GetComponentsInChildren<UGui.Graphic>(true))
            graphic.enabled = true;
    }

    private static RectTransform FindRevertArchivePanelSource(GameObject template)
    {
        var view = template.GetComponent<Game.Views.RecordSelect.ViewRevertArchive>();
        Transform title = null;
        Transform scroll = null;
        Transform confirm = null;
        Transform close = FindDeep(template.transform, "BtnClose");

        if (view != null)
        {
            var traverse = Traverse.Create(view);
            title = traverse.Field("revertTitle").GetValue<TextMeshProUGUI>()?.transform;
            scroll = traverse.Field("scroll").GetValue<Component>()?.transform;
            confirm = traverse.Field("confirmBtn").GetValue<CButton>()?.transform;
        }

        title ??= FindTitleText(template.transform)?.transform;
        scroll ??= FindComponentByTypeName(template.transform, "InfinityScroll")?.transform;
        confirm ??= FindDeep(template.transform, "EnterGame");

        var panel = CommonAncestor(title, scroll, confirm, close) as RectTransform;
        if (panel == null || panel == template.transform)
            panel = PreferPanelAncestor(title, scroll, confirm, close);

        return panel;
    }

    private static RectTransform PreferPanelAncestor(params Transform[] nodes)
    {
        foreach (var node in nodes)
        {
            var current = node;
            while (current != null)
            {
                var rect = current as RectTransform;
                if (rect != null)
                {
                    var width = Mathf.Abs(rect.rect.width);
                    var height = Mathf.Abs(rect.rect.height);
                    if (width >= 700f && width <= 1800f && height >= 400f && height <= 1200f)
                        return rect;
                }

                current = current.parent;
            }
        }

        return null;
    }

    private static Transform CommonAncestor(params Transform[] nodes)
    {
        var first = Array.Find(nodes, node => node != null);
        if (first == null)
            return null;

        var current = first;
        while (current != null)
        {
            var allInside = true;
            foreach (var node in nodes)
            {
                if (node == null)
                    continue;

                if (!IsAncestorOf(current, node))
                {
                    allInside = false;
                    break;
                }
            }

            if (allInside)
                return current;

            current = current.parent;
        }

        return null;
    }

    private static bool IsAncestorOf(Transform ancestor, Transform node)
    {
        var current = node;
        while (current != null)
        {
            if (current == ancestor)
                return true;
            current = current.parent;
        }

        return false;
    }

    private RectTransform CreateContentRoot(RectTransform oldScrollRect)
    {
        var host = FindContentHost(oldScrollRect);
        if (host == null)
            host = _panelRoot;
        if (host == null)
            return null;

        var old = host.Find(ContentName);
        if (old != null)
            Destroy(old.gameObject);

        var contentObj = new GameObject(ContentName, typeof(RectTransform), typeof(UGui.VerticalLayoutGroup));
        var contentRect = contentObj.transform as RectTransform;
        contentRect.SetParent(host, false);
        contentRect.anchorMin = new Vector2(0.5f, 1f);
        contentRect.anchorMax = new Vector2(0.5f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = new Vector2(0f, -ContentTopOffset);
        contentRect.sizeDelta = new Vector2(ContentWidth, ContentHeight);
        contentRect.localScale = Vector3.one;
        contentRect.SetAsLastSibling();

        var layout = contentObj.GetComponent<UGui.VerticalLayoutGroup>();
        layout.spacing = RowSpacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        return contentRect;
    }

    private void ConfigureInputLayer()
    {
        if (_panelRoot == null)
            return;

        var canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = PanelSortingOrder;
        canvas.worldCamera = UIManager.Instance.UiCamera;

        var raycaster = gameObject.GetComponent<ConchShipGraphicRaycaster>() ?? gameObject.AddComponent<ConchShipGraphicRaycaster>();
        raycaster.TargetCamera = UIManager.Instance.UiCamera;
        raycaster.enabled = true;

        var group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;

        EnsureInputBlocker();
        DisableDecorativeRaycasts();
    }

    private void EnsureInputBlocker()
    {
        var blocker = transform.Find(InputBlockerName) as RectTransform;
        UGui.Image blockerImage;
        if (blocker == null)
        {
            var blockerObj = new GameObject(InputBlockerName, typeof(RectTransform), typeof(UGui.Image));
            blocker = blockerObj.transform as RectTransform;
            blocker.SetParent(_panelRoot, false);
            blockerImage = blockerObj.GetComponent<UGui.Image>();
        }
        else
        {
            blockerImage = blocker.GetComponent<UGui.Image>() ?? blocker.gameObject.AddComponent<UGui.Image>();
        }

        blocker.anchorMin = new Vector2(0.5f, 0.5f);
        blocker.anchorMax = new Vector2(0.5f, 0.5f);
        blocker.pivot = new Vector2(0.5f, 0.5f);
        blocker.anchoredPosition = Vector2.zero;
        blocker.sizeDelta = new Vector2(4096f, 4096f);
        blocker.localScale = Vector3.one;
        blocker.SetAsFirstSibling();

        blockerImage.color = new Color(0f, 0f, 0f, 0f);
        blockerImage.raycastTarget = true;
    }

    private void DisableDecorativeRaycasts()
    {
        foreach (var graphic in GetComponentsInChildren<UGui.Graphic>(true))
        {
            if (graphic == null || graphic.gameObject.name == InputBlockerName)
                continue;

            if (graphic.GetComponentInParent<UGui.Selectable>() != null)
                continue;

            var text = graphic as TextMeshProUGUI;
            if (text != null)
            {
                text.raycastTarget = false;
                continue;
            }

            var rect = graphic.transform as RectTransform;
            if (rect == null)
                continue;

            if (Mathf.Abs(rect.rect.width) >= 360f || Mathf.Abs(rect.rect.height) >= 120f)
                graphic.raycastTarget = false;
        }
    }

    private void Build()
    {
        if (_contentRoot == null)
            return;

        AddDropdownRow("采购的最低品级", GradeNames, () => GradeToIndex(PurchaseOptimizationSettingsStore.Current.LowestPurchaseGrade), OnLowestGradeChanged);
        AddDropdownRow("采购的最高品级", GradeNames, () => GradeToIndex(PurchaseOptimizationSettingsStore.Current.HighestPurchaseGrade), OnHighestGradeChanged);
        AddBoolRow("不采购涨价的物品", () => PurchaseOptimizationSettingsStore.Current.SkipPriceIncreasedItems, value => PurchaseOptimizationSettingsStore.Current.SkipPriceIncreasedItems = value);
        AddBoolRow("不采购原价的物品", () => PurchaseOptimizationSettingsStore.Current.SkipOriginalPriceItems, value => PurchaseOptimizationSettingsStore.Current.SkipOriginalPriceItems = value);
        AddBoolRow("未解锁精纯商店也采购", () => PurchaseOptimizationSettingsStore.Current.IncludeLimitedPurityLockedLevels, value => PurchaseOptimizationSettingsStore.Current.IncludeLimitedPurityLockedLevels = value);
        AddBoolRow("批量采购购买药材引子", () => PurchaseOptimizationSettingsStore.Current.IncludeMedicineMaterials, value => PurchaseOptimizationSettingsStore.Current.IncludeMedicineMaterials = value);
        AddBoolRow("批量采购购买毒物引子", () => PurchaseOptimizationSettingsStore.Current.IncludePoisonMaterials, value => PurchaseOptimizationSettingsStore.Current.IncludePoisonMaterials = value);
        AddBoolRow("过月自动购买商人同道", () => PurchaseOptimizationSettingsStore.Current.AutoPurchaseGroupMerchants, value => PurchaseOptimizationSettingsStore.Current.AutoPurchaseGroupMerchants = value);
        AddDropdownRow("默认保存位置", StorageNames, () => PurchaseOptimizationSettingsStore.Current.AutoPurchaseTargetSource, value => PurchaseOptimizationSettingsStore.Current.AutoPurchaseTargetSource = value);
        AddBoolRow("仅在产业地块过月自动购买", () => PurchaseOptimizationSettingsStore.Current.AutoPurchaseOnlyOnIndustryTile, value => PurchaseOptimizationSettingsStore.Current.AutoPurchaseOnlyOnIndustryTile = value);
        AddBoolRow("行囊负重保护", () => PurchaseOptimizationSettingsStore.Current.AutoPurchaseInventoryLoadProtection, value => PurchaseOptimizationSettingsStore.Current.AutoPurchaseInventoryLoadProtection = value);
        AddDropdownRow("行囊负重阈值（百分比）", LoadThresholdNames, GetLoadThresholdIndex, SetLoadThresholdIndex);
        AddBoolRow("不在产业地块不购入行囊", () => PurchaseOptimizationSettingsStore.Current.AutoPurchaseSkipInventoryOutsideIndustry, value => PurchaseOptimizationSettingsStore.Current.AutoPurchaseSkipInventoryOutsideIndustry = value);

        RefreshValues();
    }

    private void RebindControlEvents()
    {
        foreach (var rebind in _controlRebinds)
            rebind();

        RefreshValues();
    }

    private void AddBoolRow(string label, Func<bool> getter, Action<bool> setter)
    {
        var item = Instantiate(NativeContinuousMakeSettingsTemplates.BoolSettingPrefab, _contentRoot);
        item.gameObject.name = "Setting_" + label;
        PrepareRow(item.gameObject, label);

        var toggle = Traverse.Create(item).Field("toggle").GetValue<FwUi.CToggle>();
        if (toggle == null)
            return;

        void Bind()
        {
            toggle.onValueChanged = new UGui.Toggle.ToggleEvent();
            DisableTooltips(toggle.gameObject);
            toggle.SetIsOnWithoutNotify(getter());
            toggle.onValueChanged.AddListener(value =>
            {
                if (_syncing)
                    return;

                setter(value);
                SaveAndRefresh();
            });
        }

        _controlRebinds.Add(Bind);
        Bind();
    }

    private void AddDropdownRow(string label, IReadOnlyList<string> options, Func<int> getter, Action<int> setter)
    {
        var item = Instantiate(NativeContinuousMakeSettingsTemplates.EnumSettingPrefab, _contentRoot);
        item.gameObject.name = "Setting_" + label;
        PrepareRow(item.gameObject, label);

        var dropdown = Traverse.Create(item).Field("dropdown").GetValue<FwUi.CDropdown>();
        if (dropdown == null)
            return;

        void Bind()
        {
            dropdown.onSelect = new FwUi.CDropdown.DropdownEvent();
            dropdown.onValueChanged = new FwUi.CDropdown.DropdownEvent();
            DisableTooltips(dropdown.gameObject);
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(ModLocalization.Options(options)));
            dropdown.SetValueWithoutNotify(Mathf.Clamp(getter(), 0, options.Count - 1));
            dropdown.onValueChanged.AddListener(index =>
            {
                if (_syncing)
                    return;

                setter(index);
                SaveAndRefresh();
            });
        }

        _controlRebinds.Add(Bind);
        Bind();
    }

    private void PrepareRow(GameObject row, string label)
    {
        row.SetActive(true);
        var rect = row.transform as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, RowHeight);
            rect.localScale = Vector3.one;
        }

        var layoutElement = row.GetComponent<UGui.LayoutElement>() ?? row.AddComponent<UGui.LayoutElement>();
        layoutElement.minWidth = ContentWidth;
        layoutElement.preferredWidth = ContentWidth;
        layoutElement.flexibleWidth = 0f;
        layoutElement.minHeight = RowHeight;
        layoutElement.preferredHeight = RowHeight;
        layoutElement.flexibleHeight = 0f;

        var labelText = Traverse.Create(row.GetComponent<SysSetting.SettingItemBase>()).Field("labelText").GetValue<TextMeshProUGUI>();
        if (labelText != null)
        {
            labelText.SetText(ModLocalization.T(label));
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = 18f;
            labelText.fontSizeMax = 26f;
            labelText.overflowMode = TextOverflowModes.Ellipsis;
        }

        DisableTooltips(row);
    }

    private void RefreshValues()
    {
        _syncing = true;
        PurchaseOptimizationSettingsStore.Current.Normalize();

        foreach (var item in GetComponentsInChildren<SysSetting.BoolSettingItem>(true))
        {
            var label = ModLocalization.RowKey(item);
            var toggle = Traverse.Create(item).Field("toggle").GetValue<FwUi.CToggle>();
            if (toggle == null)
                continue;

            toggle.SetIsOnWithoutNotify(label switch
            {
                "不采购涨价的物品" => PurchaseOptimizationSettingsStore.Current.SkipPriceIncreasedItems,
                "不采购原价的物品" => PurchaseOptimizationSettingsStore.Current.SkipOriginalPriceItems,
                "未解锁精纯商店也采购" => PurchaseOptimizationSettingsStore.Current.IncludeLimitedPurityLockedLevels,
                "批量采购购买药材引子" => PurchaseOptimizationSettingsStore.Current.IncludeMedicineMaterials,
                "批量采购购买毒物引子" => PurchaseOptimizationSettingsStore.Current.IncludePoisonMaterials,
                "过月自动购买商人同道" => PurchaseOptimizationSettingsStore.Current.AutoPurchaseGroupMerchants,
                "仅在产业地块过月自动购买" => PurchaseOptimizationSettingsStore.Current.AutoPurchaseOnlyOnIndustryTile,
                "行囊负重保护" => PurchaseOptimizationSettingsStore.Current.AutoPurchaseInventoryLoadProtection,
                "不在产业地块不购入行囊" => PurchaseOptimizationSettingsStore.Current.AutoPurchaseSkipInventoryOutsideIndustry,
                _ => toggle.isOn
            });
        }

        foreach (var item in GetComponentsInChildren<SysSetting.EnumSettingItem>(true))
        {
            var label = ModLocalization.RowKey(item);
            var dropdown = Traverse.Create(item).Field("dropdown").GetValue<FwUi.CDropdown>();
            if (dropdown == null)
                continue;

            dropdown.SetValueWithoutNotify(label switch
            {
                "采购的最低品级" => GradeToIndex(PurchaseOptimizationSettingsStore.Current.LowestPurchaseGrade),
                "采购的最高品级" => GradeToIndex(PurchaseOptimizationSettingsStore.Current.HighestPurchaseGrade),
                "默认保存位置" => PurchaseOptimizationSettingsStore.Current.AutoPurchaseTargetSource,
                "行囊负重阈值（百分比）" => GetLoadThresholdIndex(),
                _ => dropdown.value
            });
        }

        _syncing = false;
    }

    private void OnLowestGradeChanged(int index)
    {
        var grade = IndexToGrade(index);
        PurchaseOptimizationSettingsStore.Current.LowestPurchaseGrade = grade;
        if (PurchaseOptimizationSettingsStore.Current.HighestPurchaseGrade > grade)
            PurchaseOptimizationSettingsStore.Current.HighestPurchaseGrade = grade;
    }

    private void OnHighestGradeChanged(int index)
    {
        var grade = IndexToGrade(index);
        PurchaseOptimizationSettingsStore.Current.HighestPurchaseGrade = grade;
        if (PurchaseOptimizationSettingsStore.Current.LowestPurchaseGrade < grade)
            PurchaseOptimizationSettingsStore.Current.LowestPurchaseGrade = grade;
    }

    private static int GetLoadThresholdIndex()
    {
        var value = PurchaseOptimizationSettingsStore.Current.AutoPurchaseInventoryLoadThresholdPercent;
        var bestIndex = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < LoadThresholdPercents.Length; i++)
        {
            var distance = Math.Abs(LoadThresholdPercents[i] - value);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }
        return bestIndex;
    }

    private static void SetLoadThresholdIndex(int index)
    {
        index = Mathf.Clamp(index, 0, LoadThresholdPercents.Length - 1);
        PurchaseOptimizationSettingsStore.Current.AutoPurchaseInventoryLoadThresholdPercent =
            LoadThresholdPercents[index];
    }

    private void SaveAndRefresh()
    {
        PurchaseOptimizationSettingsStore.Save();
        RefreshValues();
    }

    private void BindCloseButton()
    {
        var close = FindDeep(transform, "BtnClose");
        if (close == null)
            return;

        var hasButton = false;
        foreach (var button in close.GetComponentsInChildren<CButton>(true))
        {
            button.ClearAndAddListener(Close);
            hasButton = true;
        }
        foreach (var button in close.GetComponentsInChildren<CButtonObsolete>(true))
        {
            button.ClearAndAddListener(Close);
            hasButton = true;
        }

        if (hasButton)
            return;

        foreach (var button in close.GetComponentsInParent<CButton>(true))
            button.ClearAndAddListener(Close);
        foreach (var button in close.GetComponentsInParent<CButtonObsolete>(true))
            button.ClearAndAddListener(Close);
    }

    private void Mask()
    {
        if (_panelRoot == null || _masked)
            return;

        UIManager.Instance.MaskComponent(_panelRoot);
        RegisterEscHandler();
        _masked = true;
    }

    private void Close()
    {
        UnregisterEscHandler();
        if (_masked && _panelRoot != null)
        {
            UIManager.Instance.UnMaskComponent(_panelRoot);
            _masked = false;
        }

        if (_current == this)
            _current = null;

        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        AsyncSettingsSaveQueue.FlushAll();
        UnregisterEscHandler();
        if (_masked && _panelRoot != null && UIManager.Instance != null)
        {
            UIManager.Instance.UnMaskComponent(_panelRoot);
            _masked = false;
        }

        if (_current == this)
            _current = null;
    }

    private void RegisterEscHandler()
    {
        if (UIManager.Instance == null)
            return;

        _escHandler ??= Close;
        UIManager.Instance.SetEscHandler(_escHandler);
    }

    private void UnregisterEscHandler()
    {
        if (UIManager.Instance == null || _escHandler == null)
            return;

        if (UIManager.Instance.CheckEscHandler(_escHandler))
            UIManager.Instance.SetEscHandler(null);
    }

    private TextMeshProUGUI FindTitleText()
    {
        return FindTitleText(transform);
    }

    private Component FindComponentByTypeName(string typeName)
    {
        return FindComponentByTypeName(transform, typeName);
    }

    private static TextMeshProUGUI FindTitleText(Transform root)
    {
        if (root == null)
            return null;

        foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text == null)
                continue;

            var name = text.gameObject.name;
            if (!string.IsNullOrEmpty(name) && name.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0)
                return text;
        }

        return null;
    }

    private static Component FindComponentByTypeName(Transform root, string typeName)
    {
        if (root == null || string.IsNullOrEmpty(typeName))
            return null;

        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null)
                continue;

            var type = component.GetType();
            if (type.Name == typeName || type.FullName == typeName)
                return component;
        }

        return null;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;

        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static void HideByName(Transform root, string name)
    {
        var target = FindDeep(root, name);
        if (target != null)
            target.gameObject.SetActive(false);
    }

    private static readonly HashSet<string> ArchiveHeaderLabels =
        ModLocalization.BuildBilingualLabelSet(new[] { "头像", "名字", "第几世", "第几个", "存档时间", "所在地点" });

    private void HideArchiveHeaderRow()
    {
        var headerTexts = new List<Transform>();
        foreach (var text in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text == null)
                continue;

            if (ArchiveHeaderLabels.Contains(text.text))
                headerTexts.Add(text.transform);
        }

        if (headerTexts.Count == 0)
            return;

        var header = FindHeaderRowAncestor(headerTexts);
        if (header != null)
        {
            header.gameObject.SetActive(false);
            return;
        }

        foreach (var text in headerTexts)
            text.gameObject.SetActive(false);
    }

    private RectTransform FindContentHost(RectTransform oldScrollRect)
    {
        var best = FindPanelGraphicHost();
        if (best != null)
            return best;

        var oldParent = oldScrollRect == null ? null : oldScrollRect.parent as RectTransform;
        return oldParent != null && oldParent != _panelRoot ? oldParent : _panelRoot;
    }

    private RectTransform FindPanelGraphicHost()
    {
        if (_panelRoot == null)
            return null;

        RectTransform best = null;
        var bestScore = float.MinValue;
        foreach (var graphic in _panelRoot.GetComponentsInChildren<UGui.Graphic>(true))
        {
            if (graphic == null || graphic is TextMeshProUGUI)
                continue;

            var rect = graphic.transform as RectTransform;
            if (rect == null || rect == _panelRoot)
                continue;

            var width = Mathf.Abs(rect.rect.width);
            var height = Mathf.Abs(rect.rect.height);
            if (width < 900f || width > 1400f || height < 500f || height > 950f)
                continue;

            var score = width * height - Mathf.Abs(width - PanelMinWidth) * 250f - Mathf.Abs(height - PanelMinHeight) * 250f;
            if (score <= bestScore)
                continue;

            bestScore = score;
            best = rect;
        }

        return best;
    }

    private static Transform FindHeaderRowAncestor(IReadOnlyList<Transform> headerTexts)
    {
        var nodes = new Transform[headerTexts.Count];
        for (var i = 0; i < headerTexts.Count; i++)
            nodes[i] = headerTexts[i];

        var common = CommonAncestor(nodes) as RectTransform;
        var current = common;
        while (current != null)
        {
            var height = Mathf.Abs(current.rect.height);
            var width = Mathf.Abs(current.rect.width);
            if (height > 16f && height <= 90f && width >= 700f)
                return current;

            current = current.parent as RectTransform;
        }

        return null;
    }

    private static void SetMinSize(RectTransform rect, float width, float height)
    {
        if (rect == null)
            return;

        var currentWidth = Mathf.Abs(rect.rect.width);
        var currentHeight = Mathf.Abs(rect.rect.height);
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(currentWidth, width));
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(currentHeight, height));
    }

    private static int GradeToIndex(int grade)
    {
        return Mathf.Clamp(grade, 1, 9) - 1;
    }

    private static int IndexToGrade(int index)
    {
        return Mathf.Clamp(index, 0, 8) + 1;
    }

    private static void DisableTooltips(GameObject obj)
    {
        foreach (var tooltip in obj.GetComponentsInChildren<TooltipInvoker>(true))
            tooltip.enabled = false;
    }
}
