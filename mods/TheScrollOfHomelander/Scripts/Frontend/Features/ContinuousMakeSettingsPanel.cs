#nullable disable
#pragma warning disable CS0612

using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using TMPro;
using UnityEngine;
using FwUi = FrameWork.UISystem.UIElements;
using SysSetting = Game.Views.SystemSetting;
using UGui = UnityEngine.UI;

namespace BetterTaiwuScroll.Frontend;

[Serializable]
internal sealed class ContinuousMakeSettings
{
    internal const int MinBatchMakeSpeed = 1;
    internal const int MaxBatchMakeSpeed = 20;

    public bool ContinuousMakeEnabled = false;
    public bool IncludeInventory = true;
    public bool IncludePrivateStorage = true;
    public bool IncludePublicStorage = true;
    public int HighestMaterialGrade = 1;
    public int LowestMaterialGrade = 9;
    public int ToolGradePriority = 0;
    public bool AllowBareHand = true;
    public bool EnableDurabilityProtection = true;
    public int BatchMakeSpeed = 1;
    public int BatchMakeStartMode = 0;

    internal ContinuousMakeSettings Clone()
    {
        return new ContinuousMakeSettings
        {
            ContinuousMakeEnabled = ContinuousMakeEnabled,
            IncludeInventory = IncludeInventory,
            IncludePrivateStorage = IncludePrivateStorage,
            IncludePublicStorage = IncludePublicStorage,
            HighestMaterialGrade = HighestMaterialGrade,
            LowestMaterialGrade = LowestMaterialGrade,
            ToolGradePriority = ToolGradePriority,
            AllowBareHand = AllowBareHand,
            EnableDurabilityProtection = EnableDurabilityProtection,
            BatchMakeSpeed = BatchMakeSpeed,
            BatchMakeStartMode = BatchMakeStartMode
        };
    }

    internal void Normalize()
    {
        HighestMaterialGrade = Mathf.Clamp(HighestMaterialGrade, 1, 9);
        LowestMaterialGrade = Mathf.Clamp(LowestMaterialGrade, 1, 9);
        if (HighestMaterialGrade > LowestMaterialGrade)
            LowestMaterialGrade = HighestMaterialGrade;

        ToolGradePriority = Mathf.Clamp(ToolGradePriority, 0, 1);
        BatchMakeSpeed = Mathf.Clamp(BatchMakeSpeed, MinBatchMakeSpeed, MaxBatchMakeSpeed);
        BatchMakeStartMode = Mathf.Clamp(BatchMakeStartMode, 0, 1);
    }
}

internal static class ContinuousMakeSettingsStore
{
    private const string LegacyFileName = "ContinuousMakeSettings.json";
    private const string FileNamePrefix = "ContinuousMakeSettings_";
    private const sbyte NoActiveLifeSkillType = sbyte.MinValue;

    private static readonly Dictionary<sbyte, ContinuousMakeSettings> SettingsByLifeSkillType = new();
    private static sbyte _activeLifeSkillType = NoActiveLifeSkillType;
    internal static ContinuousMakeSettings Current { get; private set; } = new ContinuousMakeSettings();

    internal static void Load()
    {
        Current = GetFor(_activeLifeSkillType);
    }

    internal static void Load(ViewMake view)
    {
        Use(view);
    }

    internal static ContinuousMakeSettings Use(ViewMake view)
    {
        return Use(view == null ? NoActiveLifeSkillType : view.CurLifeSkillType);
    }

    internal static ContinuousMakeSettings Use(sbyte lifeSkillType)
    {
        _activeLifeSkillType = lifeSkillType;
        Current = GetFor(lifeSkillType);
        return Current;
    }

    internal static ContinuousMakeSettings GetFor(ViewMake view)
    {
        return GetFor(view == null ? _activeLifeSkillType : view.CurLifeSkillType);
    }

    internal static ContinuousMakeSettings GetFor(sbyte lifeSkillType)
    {
        if (SettingsByLifeSkillType.TryGetValue(lifeSkillType, out var settings))
            return settings;

        settings = LoadFor(lifeSkillType);
        SettingsByLifeSkillType[lifeSkillType] = settings;
        return settings;
    }

    private static ContinuousMakeSettings LoadFor(sbyte lifeSkillType)
    {
        try
        {
            foreach (var path in GetSettingsPathCandidates(lifeSkillType))
            {
                if (!File.Exists(path))
                    continue;

                var settings = JsonUtility.FromJson<ContinuousMakeSettings>(File.ReadAllText(path)) ?? new ContinuousMakeSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to load continuous make settings for life skill "
                + lifeSkillType + ": " + ex);
        }

        var fallback = new ContinuousMakeSettings();
        fallback.Normalize();
        return fallback;
    }

    internal static void Save()
    {
        Save(_activeLifeSkillType);
    }

    internal static void Save(ViewMake view)
    {
        Save(view == null ? _activeLifeSkillType : view.CurLifeSkillType);
    }

    internal static void Save(sbyte lifeSkillType)
    {
        try
        {
            var settings = GetFor(lifeSkillType);
            settings.Normalize();
            if (lifeSkillType == _activeLifeSkillType)
                Current = settings;

            var path = GetSettingsPath(lifeSkillType);
            AsyncSettingsSaveQueue.Enqueue(path, settings.Clone());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save continuous make settings for life skill "
                + lifeSkillType + ": " + ex);
        }
    }

    internal static bool IsSourceAllowed(GameData.Domains.Taiwu.ItemSourceType sourceType)
    {
        return IsSourceAllowed(_activeLifeSkillType, sourceType);
    }

    internal static bool IsSourceAllowed(sbyte lifeSkillType, GameData.Domains.Taiwu.ItemSourceType sourceType)
    {
        var settings = GetFor(lifeSkillType);
        return sourceType switch
        {
            GameData.Domains.Taiwu.ItemSourceType.Inventory => settings.IncludeInventory,
            GameData.Domains.Taiwu.ItemSourceType.Warehouse => settings.IncludePrivateStorage,
            GameData.Domains.Taiwu.ItemSourceType.Treasury => settings.IncludePublicStorage,
            _ => false
        };
    }

    private static string GetSettingsPath(sbyte lifeSkillType)
    {
        if (lifeSkillType == NoActiveLifeSkillType)
            return ModUserDataPaths.GetFilePath(LegacyFileName);

        return ModUserDataPaths.GetFilePath(FileNamePrefix + (int)lifeSkillType + ".json");
    }

    private static IEnumerable<string> GetSettingsPathCandidates(sbyte lifeSkillType)
    {
        var fileName = lifeSkillType == NoActiveLifeSkillType
            ? LegacyFileName
            : FileNamePrefix + (int)lifeSkillType + ".json";
        foreach (var path in ModUserDataPaths.GetFilePathCandidates(fileName))
            yield return path;

        if (lifeSkillType != NoActiveLifeSkillType)
        {
            foreach (var path in ModUserDataPaths.GetFilePathCandidates(LegacyFileName))
                yield return path;
        }
    }
}

internal static class NativeContinuousMakeSettingsTemplates
{
    internal static SysSetting.BoolSettingItem BoolSettingPrefab;
    internal static SysSetting.IntSettingItem IntSettingPrefab;
    internal static SysSetting.EnumSettingItem EnumSettingPrefab;
    internal static SysSetting.SwitchButtonSettingItem SwitchButtonSettingPrefab;

    private static bool _requesting;

    internal static bool Ready => BoolSettingPrefab != null
        && IntSettingPrefab != null
        && EnumSettingPrefab != null
        && SwitchButtonSettingPrefab != null;

    internal static void Request(Action onReady)
    {
        if (TryFind())
        {
            onReady?.Invoke();
            return;
        }

        if (_requesting)
            return;

        _requesting = true;
        UIElement.SystemSetting.PrepareRes(false, go =>
        {
            _requesting = false;
            CacheFrom(go == null ? null : go.GetComponentInChildren<SysSetting.ViewSystemSetting>(true));
            TryFind();
            onReady?.Invoke();
        });
    }

    private static bool TryFind()
    {
        if (Ready)
            return true;

        foreach (var template in Resources.FindObjectsOfTypeAll<SysSetting.ViewSystemSetting>())
        {
            CacheFrom(template);
            if (Ready)
                return true;
        }

        return false;
    }

    private static void CacheFrom(SysSetting.ViewSystemSetting template)
    {
        if (template == null)
            return;

        var traverse = Traverse.Create(template);
        BoolSettingPrefab ??= traverse.Field("boolSettingPrefab").GetValue<SysSetting.BoolSettingItem>();
        IntSettingPrefab ??= traverse.Field("intSettingPrefab").GetValue<SysSetting.IntSettingItem>();
        EnumSettingPrefab ??= traverse.Field("enumSettingPrefab").GetValue<SysSetting.EnumSettingItem>();
        SwitchButtonSettingPrefab ??= traverse.Field("switchButtonSettingPrefab").GetValue<SysSetting.SwitchButtonSettingItem>();
    }
}

internal sealed class ContinuousMakeSettingsPanel : MonoBehaviour
{
    private const string PanelName = "BetterTaiwuScrollContinuousMakeSettingsPanel";
    private const string ContentName = "BetterTaiwuScrollContinuousMakeSettingsContent";
    private const string InputBlockerName = "BetterTaiwuScrollContinuousMakeInputBlocker";
    private const float PanelMinWidth = 1220f;
    private const float PanelMinHeight = 820f;
    private const float ContentWidth = 940f;
    private const float ContentHeight = 520f;
    private const float ContentTopOffset = 138f;
    private const float RowHeight = 48f;
    private const float RowSpacing = 8f;
    private const int PanelSortingOrder = 5000;

    private static readonly string[] GradeNames =
    {
        "神·一品",
        "绝·二品",
        "超·三品",
        "极·四品",
        "秘·五品",
        "奇·六品",
        "上·七品",
        "中·八品",
        "下·九品"
    };

    private static readonly string[] ToolPriorityNames =
    {
        "高品",
        "低品"
    };

    private static readonly string[] BatchMakeStartModeNames =
    {
        "勾选模式",
        "按钮模式"
    };

    private static ContinuousMakeSettingsPanel _current;
    private static bool _requestingArchiveTemplate;
    private static Action _onArchiveTemplateReady;
    private static RectTransform _cachedArchivePanelSource;

    private RectTransform _panelRoot;
    private RectTransform _contentRoot;
    private Action _escHandler;
    private readonly List<Action> _controlRebinds = new List<Action>();
    private sbyte _lifeSkillType;
    private bool _autoRepairMode;
    private bool _autoChickenMode;
    private bool _autoCricketMode;
    private bool _syncing;
    private bool _masked;

    internal static void Show(ViewMake owner)
    {
        var lifeSkillType = owner == null ? sbyte.MinValue : owner.CurLifeSkillType;
        ContinuousMakeSettingsStore.Use(lifeSkillType);

        if (!NativeContinuousMakeSettingsTemplates.Ready)
        {
            NativeContinuousMakeSettingsTemplates.Request(() => Show(owner));
            return;
        }

        EnsureArchiveTemplate(() => CreateFromRevertArchiveTemplate(UIElement.RevertArchive.UiBase.gameObject, lifeSkillType, false, false, false));
    }

    internal static void ShowAutoRepair(MonoBehaviour owner)
    {
        AutoRepairSettingsStore.Load();

        if (!NativeContinuousMakeSettingsTemplates.Ready)
        {
            NativeContinuousMakeSettingsTemplates.Request(() => ShowAutoRepair(owner));
            return;
        }

        EnsureArchiveTemplate(() => CreateFromRevertArchiveTemplate(UIElement.RevertArchive.UiBase.gameObject, sbyte.MinValue, true, false, false));
    }

    internal static void ShowChicken(MonoBehaviour owner)
    {
        AutoChickenCareSettingsStore.Load();
        if (!NativeContinuousMakeSettingsTemplates.Ready)
        {
            NativeContinuousMakeSettingsTemplates.Request(() => ShowChicken(owner));
            return;
        }
        EnsureArchiveTemplate(() => CreateFromRevertArchiveTemplate(UIElement.RevertArchive.UiBase.gameObject, sbyte.MinValue, false, true, false));
    }

    internal static void ShowCricket(MonoBehaviour owner)
    {
        AutoCricketRoomSettingsStore.Load();
        if (!NativeContinuousMakeSettingsTemplates.Ready)
        {
            NativeContinuousMakeSettingsTemplates.Request(() => ShowCricket(owner));
            return;
        }
        EnsureArchiveTemplate(() => CreateFromRevertArchiveTemplate(UIElement.RevertArchive.UiBase.gameObject, sbyte.MinValue, false, false, true));
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

    private static void CreateFromRevertArchiveTemplate(GameObject template, sbyte lifeSkillType, bool autoRepairMode, bool autoChickenMode, bool autoCricketMode)
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
            Debug.LogWarning("[BetterTaiwuScroll] Failed to find revert archive panel source.");
            return;
        }

        Transform parent = UIManager.Instance.GetLayer(UILayer.LayerVeryTop);
        if (parent == null)
            parent = UIManager.Instance.transform;

        var panelObj = Instantiate(sourcePanel.gameObject, parent, false);
        panelObj.name = PanelName;
        panelObj.SetActive(false);

        var panel = panelObj.AddComponent<ContinuousMakeSettingsPanel>();
        panel._lifeSkillType = lifeSkillType;
        panel._autoRepairMode = autoRepairMode;
        panel._autoChickenMode = autoChickenMode;
        panel._autoCricketMode = autoCricketMode;
        panel._panelRoot = panelObj.transform as RectTransform;
        if (!autoRepairMode)
            ContinuousMakeSettingsStore.Use(lifeSkillType);
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

    private ContinuousMakeSettings Settings => ContinuousMakeSettingsStore.GetFor(_lifeSkillType);

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

        if (title == null)
            title = FindTitleText();
        if (scroll == null)
            scroll = FindComponentByTypeName("InfinityScroll");
        if (confirmBtn == null)
            confirmBtn = FindDeep(transform, "EnterGame")?.GetComponent<CButton>();
        if (title != null)
            title.SetText(ModLocalization.T(_autoRepairMode ? "自动修理设置" : _autoChickenMode ? "自动饲养元鸡设置" : _autoCricketMode ? "自动存放促织设置" : "连续制作设置"));

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

        if (_autoRepairMode)
        {
            var autoSettings = AutoRepairSettingsStore.Current;
            AddBoolRow("是否开启自动修理", () => autoSettings.Enabled, value => autoSettings.Enabled = value);
            AddBoolRow("仅使用徒手修理", () => autoSettings.OnlyBareHandRepair, value => autoSettings.OnlyBareHandRepair = value);
            AddBoolRow("允许徒手修理", () => autoSettings.AllowBareHandRepair, value => autoSettings.AllowBareHandRepair = value);
            AddSwitchRow("优先使用工具的品级", ToolPriorityNames, () => autoSettings.ToolGradePriority, value => autoSettings.ToolGradePriority = value);
            AddBoolRow("是否开启耐久保护", () => autoSettings.EnableDurabilityProtection, value => autoSettings.EnableDurabilityProtection = value);
            RefreshValues();
            return;
        }

        if (_autoChickenMode)
        {
            var chickenSettings = AutoChickenCareSettingsStore.Current;
            AddBoolRow("自动饲养元鸡", () => chickenSettings.Enabled, value => chickenSettings.Enabled = value);
            AddIntSliderRow("最低饲养好感度", 0, 100, () => chickenSettings.HappinessThreshold, value => chickenSettings.HappinessThreshold = value, FormatPercent);
            AddBoolRow("优先使用饲槽", () => chickenSettings.UseTrough, value => chickenSettings.UseTrough = value);
            AddBoolRow("允许使用库房", () => chickenSettings.UseWarehouse, value => chickenSettings.UseWarehouse = value);
            AddBoolRow("允许使用行囊", () => chickenSettings.UseInventory, value => chickenSettings.UseInventory = value);
            RefreshValues();
            return;
        }

        if (_autoCricketMode)
        {
            var cricketSettings = AutoCricketRoomSettingsStore.Current;
            AddBoolRow("自动存放促织", () => cricketSettings.Enabled, value => cricketSettings.Enabled = value);
            AddBoolRow("自动取回本次放入的促织", () => cricketSettings.RetrieveAutomaticallyStored, value => cricketSettings.RetrieveAutomaticallyStored = value);
            AddBoolRow("同时取回玩家原先放置的促织", () => cricketSettings.RetrievePlayerStored, value => cricketSettings.RetrievePlayerStored = value);
            RefreshValues();
            return;
        }

        AddBoolRow("是否包括行囊", () => Settings.IncludeInventory, value => Settings.IncludeInventory = value);
        AddBoolRow("是否包括私库", () => Settings.IncludePrivateStorage, value => Settings.IncludePrivateStorage = value);
        AddBoolRow("是否包括公库", () => Settings.IncludePublicStorage, value => Settings.IncludePublicStorage = value);
        AddDropdownRow("允许使用的最高引子品级", GradeNames, () => GradeToIndex(Settings.HighestMaterialGrade), OnHighestGradeChanged);
        AddDropdownRow("允许使用的最低引子品级", GradeNames, () => GradeToIndex(Settings.LowestMaterialGrade), OnLowestGradeChanged);
        AddSwitchRow("优先使用工具的品级", ToolPriorityNames, () => Settings.ToolGradePriority, value => Settings.ToolGradePriority = value);
        AddSwitchRow("批量制作模式", BatchMakeStartModeNames, () => Settings.BatchMakeStartMode, value => Settings.BatchMakeStartMode = value);
        AddBoolRow("是否允许徒手制作", () => Settings.AllowBareHand, value => Settings.AllowBareHand = value);
        AddBoolRow("是否开启耐久保护", () => Settings.EnableDurabilityProtection, value => Settings.EnableDurabilityProtection = value);
        AddIntSliderRow("批量制作速度", ContinuousMakeSettings.MinBatchMakeSpeed, ContinuousMakeSettings.MaxBatchMakeSpeed, () => Settings.BatchMakeSpeed, value => Settings.BatchMakeSpeed = value, FormatBatchMakeSpeed);

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

    private void AddIntSliderRow(string label, int min, int max, Func<int> getter, Action<int> setter, Func<int, string> formatter)
    {
        var item = Instantiate(NativeContinuousMakeSettingsTemplates.IntSettingPrefab, _contentRoot);
        item.gameObject.name = "Setting_" + label;
        PrepareRow(item.gameObject, label);

        var traverse = Traverse.Create(item);
        var slider = traverse.Field("slider").GetValue<FwUi.CSlider>();
        var addBtn = traverse.Field("addBtn").GetValue<FwUi.CButton>();
        var reduceBtn = traverse.Field("reduceBtn").GetValue<FwUi.CButton>();
        var valueText = traverse.Field("valueText").GetValue<TMP_InputField>();

        var value = Mathf.Clamp(getter(), min, max);
        void ApplyValue(int next, bool notify)
        {
            var nextValue = Mathf.Clamp(next, min, max);
            var changed = nextValue != value;
            value = nextValue;
            ApplyIntRowVisual(slider, valueText, addBtn, reduceBtn, min, max, value, formatter);
            if (!notify || !changed)
                return;

            setter(value);
            SaveAndRefresh();
        }

        void Bind()
        {
            if (slider != null)
            {
                slider.onValueChanged = new UGui.Slider.SliderEvent();
                slider.minValue = min;
                slider.maxValue = max;
                slider.wholeNumbers = true;
                DisableTooltips(slider.gameObject);
                slider.onValueChanged.AddListener(rawValue =>
                {
                    if (_syncing)
                        return;

                    ApplyValue(Mathf.RoundToInt(rawValue), true);
                });
            }

            if (addBtn != null)
            {
                addBtn.onClick = new UGui.Button.ButtonClickedEvent();
                addBtn.onClick.AddListener(() =>
                {
                    if (_syncing)
                        return;

                    ApplyValue(value + 1, true);
                });
            }

            if (reduceBtn != null)
            {
                reduceBtn.onClick = new UGui.Button.ButtonClickedEvent();
                reduceBtn.onClick.AddListener(() =>
                {
                    if (_syncing)
                        return;

                    ApplyValue(value - 1, true);
                });
            }

            if (valueText != null)
            {
                valueText.onSubmit.RemoveAllListeners();
                valueText.onEndEdit.RemoveAllListeners();
                valueText.onSubmit.AddListener(input =>
                {
                    if (!_syncing)
                        ApplyValue(ParseIntInput(input, value), true);
                });
                valueText.onEndEdit.AddListener(input =>
                {
                    if (!_syncing)
                        ApplyValue(ParseIntInput(input, value), true);
                });
            }

            value = Mathf.Clamp(getter(), min, max);
            ApplyValue(value, false);
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

    private void AddSwitchRow(string label, IReadOnlyList<string> options, Func<int> getter, Action<int> setter)
    {
        var item = Instantiate(NativeContinuousMakeSettingsTemplates.SwitchButtonSettingPrefab, _contentRoot);
        item.gameObject.name = "Setting_" + label;
        PrepareRow(item.gameObject, label);

        var traverse = Traverse.Create(item);
        var leftBtn = traverse.Field("leftBtn").GetValue<FwUi.CButton>();
        var rightBtn = traverse.Field("rightBtn").GetValue<FwUi.CButton>();
        var valueText = traverse.Field("valueText").GetValue<TextMeshProUGUI>();

        var value = Mathf.Clamp(getter(), 0, options.Count - 1);
        void ApplyValue(int next)
        {
            value = Mathf.Clamp(next, 0, options.Count - 1);
            if (valueText != null)
                valueText.SetText(ModLocalization.T(options[value]));
            if (leftBtn != null)
                leftBtn.interactable = value > 0;
            if (rightBtn != null)
                rightBtn.interactable = value < options.Count - 1;
        }

        void Bind()
        {
            if (leftBtn != null)
                leftBtn.onClick = new UGui.Button.ButtonClickedEvent();
            if (rightBtn != null)
                rightBtn.onClick = new UGui.Button.ButtonClickedEvent();

            value = Mathf.Clamp(getter(), 0, options.Count - 1);
            leftBtn?.onClick.AddListener(() =>
            {
                if (_syncing)
                    return;

                ApplyValue(value - 1);
                setter(value);
                SaveAndRefresh();
            });
            rightBtn?.onClick.AddListener(() =>
            {
                if (_syncing)
                    return;

                ApplyValue(value + 1);
                setter(value);
                SaveAndRefresh();
            });

            ApplyValue(value);
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
        if (_autoRepairMode)
        {
            var autoSettings = AutoRepairSettingsStore.Current;
            autoSettings.Normalize();
            foreach (var item in GetComponentsInChildren<SysSetting.BoolSettingItem>(true))
            {
                var label = ModLocalization.RowKey(item);
                var toggle = Traverse.Create(item).Field("toggle").GetValue<FwUi.CToggle>();
                if (toggle == null)
                    continue;

                toggle.SetIsOnWithoutNotify(label switch
                {
                    "是否开启自动修理" => autoSettings.Enabled,
                    "仅使用徒手修理" => autoSettings.OnlyBareHandRepair,
                    "允许徒手修理" => autoSettings.AllowBareHandRepair,
                    "是否开启耐久保护" => autoSettings.EnableDurabilityProtection,
                    _ => toggle.isOn
                });
            }

            foreach (var item in GetComponentsInChildren<SysSetting.SwitchButtonSettingItem>(true))
            {
                var label = ModLocalization.RowKey(item);
                if (label != "优先使用工具的品级")
                    continue;

                var value = Mathf.Clamp(autoSettings.ToolGradePriority, 0, ToolPriorityNames.Length - 1);
                var traverse = Traverse.Create(item);
                var valueText = traverse.Field("valueText").GetValue<TextMeshProUGUI>();
                var leftBtn = traverse.Field("leftBtn").GetValue<FwUi.CButton>();
                var rightBtn = traverse.Field("rightBtn").GetValue<FwUi.CButton>();
                valueText?.SetText(ModLocalization.T(ToolPriorityNames[value]));
                if (leftBtn != null)
                    leftBtn.interactable = value > 0;
                if (rightBtn != null)
                    rightBtn.interactable = value < ToolPriorityNames.Length - 1;
            }

            _syncing = false;
            return;
        }

        if (_autoChickenMode)
        {
            var chickenSettings = AutoChickenCareSettingsStore.Current;
            chickenSettings.Normalize();
            foreach (var item in GetComponentsInChildren<SysSetting.BoolSettingItem>(true))
            {
                var label = ModLocalization.RowKey(item);
                var toggle = Traverse.Create(item).Field("toggle").GetValue<FwUi.CToggle>();
                if (toggle == null)
                    continue;
                toggle.SetIsOnWithoutNotify(label switch
                {
                    "自动饲养元鸡" => chickenSettings.Enabled,
                    "优先使用饲槽" => chickenSettings.UseTrough,
                    "允许使用库房" => chickenSettings.UseWarehouse,
                    "允许使用行囊" => chickenSettings.UseInventory,
                    _ => toggle.isOn
                });
            }
            RefreshSpecialThreshold(chickenSettings.HappinessThreshold, "最低饲养好感度", 0, 100);
            _syncing = false;
            return;
        }

        if (_autoCricketMode)
        {
            var cricketSettings = AutoCricketRoomSettingsStore.Current;
            foreach (var item in GetComponentsInChildren<SysSetting.BoolSettingItem>(true))
            {
                var label = ModLocalization.RowKey(item);
                var toggle = Traverse.Create(item).Field("toggle").GetValue<FwUi.CToggle>();
                if (toggle == null)
                    continue;
                toggle.SetIsOnWithoutNotify(label switch
                {
                    "自动存放促织" => cricketSettings.Enabled,
                    "自动取回本次放入的促织" => cricketSettings.RetrieveAutomaticallyStored,
                    "同时取回玩家原先放置的促织" => cricketSettings.RetrievePlayerStored,
                    _ => toggle.isOn
                });
            }
            _syncing = false;
            return;
        }

        var settings = Settings;
        settings.Normalize();

        foreach (var item in GetComponentsInChildren<SysSetting.BoolSettingItem>(true))
        {
            var label = ModLocalization.RowKey(item);
            var toggle = Traverse.Create(item).Field("toggle").GetValue<FwUi.CToggle>();
            if (toggle == null)
                continue;

            toggle.SetIsOnWithoutNotify(label switch
            {
                "是否包括行囊" => settings.IncludeInventory,
                "是否包括私库" => settings.IncludePrivateStorage,
                "是否包括公库" => settings.IncludePublicStorage,
                "是否允许徒手制作" => settings.AllowBareHand,
                "是否开启耐久保护" => settings.EnableDurabilityProtection,
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
                "允许使用的最高引子品级" => GradeToIndex(settings.HighestMaterialGrade),
                "允许使用的最低引子品级" => GradeToIndex(settings.LowestMaterialGrade),
                _ => dropdown.value
            });
        }

        foreach (var item in GetComponentsInChildren<SysSetting.SwitchButtonSettingItem>(true))
        {
            var label = ModLocalization.RowKey(item);
            var value = 0;
            IReadOnlyList<string> options = null;
            switch (label)
            {
                case "优先使用工具的品级":
                    value = Mathf.Clamp(settings.ToolGradePriority, 0, ToolPriorityNames.Length - 1);
                    options = ToolPriorityNames;
                    break;
                case "批量制作模式":
                    value = Mathf.Clamp(settings.BatchMakeStartMode, 0, BatchMakeStartModeNames.Length - 1);
                    options = BatchMakeStartModeNames;
                    break;
            }

            if (options == null)
                continue;

            var traverse = Traverse.Create(item);
            var valueText = traverse.Field("valueText").GetValue<TextMeshProUGUI>();
            var leftBtn = traverse.Field("leftBtn").GetValue<FwUi.CButton>();
            var rightBtn = traverse.Field("rightBtn").GetValue<FwUi.CButton>();
            if (valueText != null)
                valueText.SetText(ModLocalization.T(options[value]));
            if (leftBtn != null)
                leftBtn.interactable = value > 0;
            if (rightBtn != null)
                rightBtn.interactable = value < options.Count - 1;
        }

        foreach (var item in GetComponentsInChildren<SysSetting.IntSettingItem>(true))
        {
            var label = ModLocalization.RowKey(item);
            if (label != "批量制作速度")
                continue;

            var traverse = Traverse.Create(item);
            var slider = traverse.Field("slider").GetValue<FwUi.CSlider>();
            var addBtn = traverse.Field("addBtn").GetValue<FwUi.CButton>();
            var reduceBtn = traverse.Field("reduceBtn").GetValue<FwUi.CButton>();
            var valueText = traverse.Field("valueText").GetValue<TMP_InputField>();
            ApplyIntRowVisual(slider, valueText, addBtn, reduceBtn, ContinuousMakeSettings.MinBatchMakeSpeed, ContinuousMakeSettings.MaxBatchMakeSpeed, settings.BatchMakeSpeed, FormatBatchMakeSpeed);
        }

        _syncing = false;
    }

    private void OnHighestGradeChanged(int index)
    {
        var grade = IndexToGrade(index);
        var settings = Settings;
        settings.HighestMaterialGrade = grade;
        if (settings.LowestMaterialGrade < grade)
            settings.LowestMaterialGrade = grade;
    }

    private void OnLowestGradeChanged(int index)
    {
        var grade = IndexToGrade(index);
        var settings = Settings;
        settings.LowestMaterialGrade = grade;
        if (settings.HighestMaterialGrade > grade)
            settings.HighestMaterialGrade = grade;
    }

    private void SaveAndRefresh()
    {
        if (_autoRepairMode)
        {
            AutoRepairSettingsStore.Save();
            RefreshValues();
            return;
        }

        if (_autoChickenMode)
        {
            AutoChickenCareSettingsStore.Save();
            RefreshValues();
            return;
        }

        if (_autoCricketMode)
        {
            AutoCricketRoomSettingsStore.Save();
            RefreshValues();
            return;
        }

        ContinuousMakeSettingsStore.Save(_lifeSkillType);
        RefreshValues();
        ContinuousMakeUiController.RefreshAll();
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
        ModLocalization.BuildBilingualLabelSet(new[] { "头像", "名字", "第几世", "存档时间", "所在地点" });

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
            if (width < 900f || width > 1400f || height < 520f || height > 950f)
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

    private static void ApplyIntRowVisual(
        FwUi.CSlider slider,
        TMP_InputField valueText,
        FwUi.CButton addBtn,
        FwUi.CButton reduceBtn,
        int min,
        int max,
        int value,
        Func<int, string> formatter)
    {
        value = Mathf.Clamp(value, min, max);
        if (slider != null)
            slider.SetValueWithoutNotify(value);
        if (valueText != null)
            valueText.SetTextWithoutNotify(formatter == null ? value.ToString() : formatter(value));
        if (addBtn != null)
            addBtn.interactable = value < max;
        if (reduceBtn != null)
            reduceBtn.interactable = value > min;
    }

    private static int ParseIntInput(string input, int fallback)
    {
        if (string.IsNullOrWhiteSpace(input))
            return fallback;

        var text = input.Trim();
        if (text.EndsWith("x", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(0, text.Length - 1).Trim();
        if (text.EndsWith("倍", StringComparison.Ordinal))
            text = text.Substring(0, text.Length - 1).Trim();

        return int.TryParse(text, out var result) ? result : fallback;
    }

    private static string FormatBatchMakeSpeed(int value)
    {
        return Mathf.Clamp(value, ContinuousMakeSettings.MinBatchMakeSpeed, ContinuousMakeSettings.MaxBatchMakeSpeed) + "x";
    }

    private static string FormatPercent(int value)
    {
        return Mathf.Clamp(value, 0, 100) + "%";
    }

    private void RefreshSpecialThreshold(int value, string labelName, int min, int max)
    {
        foreach (var item in GetComponentsInChildren<SysSetting.IntSettingItem>(true))
        {
            var label = ModLocalization.RowKey(item);
            if (label != labelName)
                continue;

            var traverse = Traverse.Create(item);
            ApplyIntRowVisual(
                traverse.Field("slider").GetValue<FwUi.CSlider>(),
                traverse.Field("valueText").GetValue<TMP_InputField>(),
                traverse.Field("addBtn").GetValue<FwUi.CButton>(),
                traverse.Field("reduceBtn").GetValue<FwUi.CButton>(),
                min,
                max,
                value,
                FormatPercent);
        }
    }

    private static void DisableTooltips(GameObject obj)
    {
        foreach (var tooltip in obj.GetComponentsInChildren<TooltipInvoker>(true))
            tooltip.enabled = false;
    }
}
