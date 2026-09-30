#nullable disable
#pragma warning disable CS0612

using System;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BetterTaiwuScroll.Frontend;

[HarmonyPatch(typeof(ViewMake), "OnInit")]
internal static class ViewMakeContinuousMakeUiPatch
{
    private static void Postfix(ViewMake __instance)
    {
        ContinuousMakeUiController.GetOrAdd(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(ViewMake), "Refresh")]
internal static class ViewMakeContinuousMakeRefreshPatch
{
    private static void Postfix(ViewMake __instance)
    {
        ContinuousMakeUiController.GetOrAdd(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "RefreshPanel")]
internal static class MakeSubPageMakeContinuousMakeUiPatch
{
    private static void Postfix(MakeSubPageMake __instance)
    {
        var view = MakeSelectMaterialPatch.GetParentView(__instance);
        ContinuousMakeUiController.GetOrAdd(view)?.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(MakeSubPageMake), "Refresh")]
internal static class MakeSubPageMakeContinuousMakeRefreshPatch
{
    private static void Postfix(MakeSubPageMake __instance)
    {
        var view = MakeSelectMaterialPatch.GetParentView(__instance);
        ContinuousMakeUiController.GetOrAdd(view)?.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(Game.Views.Building.BuildingManage.BuildingManageSubPageShop), "Init")]
internal static class BuildingManageSubPageShopContinuousMakeUiTemplatePatch
{
    private static void Postfix(Game.Views.Building.BuildingManage.BuildingManageSubPageShop __instance)
    {
        NativeContinuousMakeUiTemplates.BuildingManageShopTemplate = __instance;
    }
}

internal static class NativeContinuousMakeUiTemplates
{
    internal static Game.Views.Building.BuildingManage.BuildingManageSubPageShop BuildingManageShopTemplate;
    private static bool _requestingBuildingManageTemplate;

    internal static CButton FindArrangementSettingButtonTemplate()
    {
        var button = FindArrangementSettingButtonTemplate(BuildingManageShopTemplate);
        if (button != null)
            return button;

        foreach (var template in Resources.FindObjectsOfTypeAll<Game.Views.Building.BuildingManage.BuildingManageSubPageShop>())
        {
            button = FindArrangementSettingButtonTemplate(template);
            if (button != null)
            {
                BuildingManageShopTemplate = template;
                return button;
            }
        }

        return null;
    }

    private static CButton FindArrangementSettingButtonTemplate(Game.Views.Building.BuildingManage.BuildingManageSubPageShop template)
    {
        if (template == null)
            return null;

        return Traverse.Create(template).Field("buttonArrangementSetting").GetValue<CButton>();
    }

    internal static void RequestArrangementSettingButtonTemplate(Action<CButton> onReady)
    {
        var button = FindArrangementSettingButtonTemplate();
        if (button != null)
        {
            onReady?.Invoke(button);
            return;
        }

        if (_requestingBuildingManageTemplate)
            return;

        _requestingBuildingManageTemplate = true;
        UIElement.BuildingManage.PrepareRes(false, _ =>
        {
            _requestingBuildingManageTemplate = false;
            FindArrangementSettingButtonTemplate();
            onReady?.Invoke(FindArrangementSettingButtonTemplate());
        });
    }

}

internal sealed class ContinuousMakeUiController : MonoBehaviour
{
    private const float ContinuousToggleWidth = 180f;
    private const float ContinuousToggleHeight = 46f;
    private const float ContinuousToggleOffsetY = 70f;
    private const float BatchMakeButtonOffsetY = -70f;
    private const float SettingsButtonOffsetX = -155f;

    private ViewMake _view;
    private CToggle _continuousToggle;
    private CButton _settingsButton;
    private CButton _batchMakeButton;

    internal static bool IsContinuousMakeEnabled => Plugin.EnableContinuousMakeUi
        && ContinuousMakeSettingsStore.Current.BatchMakeStartMode == 0
        && ContinuousMakeSettingsStore.Current.ContinuousMakeEnabled;

    internal static bool IsContinuousMakeEnabledFor(ViewMake view)
    {
        if (!Plugin.EnableContinuousMakeUi || view == null)
            return false;

        var settings = ContinuousMakeSettingsStore.GetFor(view);
        return settings.BatchMakeStartMode == 0 && settings.ContinuousMakeEnabled;
    }

    internal static ContinuousMakeUiController GetOrAdd(ViewMake view)
    {
        if (view == null)
            return null;

        var controller = view.GetComponent<ContinuousMakeUiController>();
        if (controller == null)
            controller = view.gameObject.AddComponent<ContinuousMakeUiController>();

        controller._view = view;
        return controller;
    }

    internal static void RefreshBatchMakeButtonState(MakeSubPageMake page)
    {
        var view = MakeSelectMaterialPatch.GetParentView(page);
        if (view == null)
            return;

        var controller = view.GetComponent<ContinuousMakeUiController>();
        controller?.SyncBatchMakeButtonState(page);
    }

    internal static void RefreshAll()
    {
        foreach (var controller in Resources.FindObjectsOfTypeAll<ContinuousMakeUiController>())
            controller.Refresh();
    }

    internal void Refresh(MakeSubPageMake activeMakePage = null)
    {
        if (_view == null)
            _view = GetComponent<ViewMake>();

        if (!Plugin.EnableContinuousMakeUi)
        {
            SetActive(_continuousToggle, false);
            SetActive(_settingsButton, false);
            SetActive(_batchMakeButton, false);
            return;
        }

        EnsureSettingsButton();
        activeMakePage ??= GetActiveMakePage();
        EnsureContinuousToggle(activeMakePage);
        EnsureBatchMakeButton(activeMakePage);
        SetActive(_settingsButton, true);
        var settings = ContinuousMakeSettingsStore.GetFor(_view);
        var showMakeControls = activeMakePage != null && activeMakePage.gameObject.activeInHierarchy;
        SetActive(_continuousToggle, showMakeControls && settings.BatchMakeStartMode == 0);
        SetActive(_batchMakeButton, showMakeControls && settings.BatchMakeStartMode == 1);
    }

    private void EnsureContinuousToggle(MakeSubPageMake page)
    {
        if (page == null)
            return;

        var confirmButton = Traverse.Create(page).Field("buttonConfirm").GetValue<CButton>();
        var toolSlot = Traverse.Create(page).Field("toolSlot").GetValue<MakeTargetSlot>();
        if (confirmButton == null || toolSlot == null)
            return;

        var sourceToggle = Traverse.Create(toolSlot).Field("toggle").GetValue<CToggle>();
        if (sourceToggle == null)
            return;

        var confirmRect = confirmButton.transform as RectTransform;
        if (confirmRect == null)
            return;

        if (_continuousToggle == null)
        {
            var toggleObj = Instantiate(sourceToggle.gameObject);
            toggleObj.name = "BetterTaiwuScrollContinuousMakeToggle";
            _continuousToggle = toggleObj.GetComponent<CToggle>() ?? toggleObj.AddComponent<CToggle>();
            _continuousToggle.onValueChanged.RemoveAllListeners();
            _continuousToggle.onValueChanged.AddListener(OnContinuousToggleChanged);
            SetToggleLabel(toggleObj, ModLocalization.T("Continuous Crafting"));
            ConfigureContinuousToggleTooltip(toggleObj);
        }

        var toggleRect = _continuousToggle.transform as RectTransform;
        if (toggleRect == null)
            return;

        if (toggleRect.parent != confirmRect.parent)
            toggleRect.SetParent(confirmRect.parent, false);

        toggleRect.anchorMin = confirmRect.anchorMin;
        toggleRect.anchorMax = confirmRect.anchorMax;
        toggleRect.pivot = confirmRect.pivot;
        toggleRect.sizeDelta = new Vector2(ContinuousToggleWidth, ContinuousToggleHeight);
        toggleRect.anchoredPosition = confirmRect.anchoredPosition + new Vector2(0f, ContinuousToggleOffsetY);
        toggleRect.localScale = Vector3.one;
        toggleRect.SetAsLastSibling();

        _continuousToggle.SetIsOnWithoutNotify(ContinuousMakeSettingsStore.GetFor(_view).ContinuousMakeEnabled);
        _continuousToggle.gameObject.SetActive(true);
    }

    private void EnsureBatchMakeButton(MakeSubPageMake page)
    {
        if (page == null)
            return;

        var confirmButton = Traverse.Create(page).Field("buttonConfirm").GetValue<CButton>();
        if (confirmButton == null)
            return;

        var confirmRect = confirmButton.transform as RectTransform;
        if (confirmRect == null)
            return;

        if (_batchMakeButton == null)
        {
            var buttonObj = Instantiate(confirmButton.gameObject);
            buttonObj.name = "BetterTaiwuScrollBatchMakeButton";
            _batchMakeButton = buttonObj.GetComponent<CButton>() ?? buttonObj.GetComponentInChildren<CButton>(true);
            if (_batchMakeButton == null)
            {
                Destroy(buttonObj);
                return;
            }

            _batchMakeButton.ClearAndAddListener(OnBatchMakeButtonClick);
            SetButtonText(buttonObj, ModLocalization.T("Batch Crafting"));
            (buttonObj.GetComponent<ButtonTextOverride>() ?? buttonObj.AddComponent<ButtonTextOverride>()).SetText(ModLocalization.T("Batch Crafting"));
            ConfigureBatchMakeButtonTooltip(buttonObj);
        }

        var buttonRect = _batchMakeButton.transform as RectTransform;
        if (buttonRect == null)
            return;

        if (buttonRect.parent != confirmRect.parent)
            buttonRect.SetParent(confirmRect.parent, false);

        buttonRect.anchorMin = confirmRect.anchorMin;
        buttonRect.anchorMax = confirmRect.anchorMax;
        buttonRect.pivot = confirmRect.pivot;
        buttonRect.sizeDelta = new Vector2(ContinuousToggleWidth, ContinuousToggleHeight);
        buttonRect.anchoredPosition = confirmRect.anchoredPosition + new Vector2(0f, BatchMakeButtonOffsetY);
        buttonRect.localScale = Vector3.one;
        buttonRect.SetAsLastSibling();
        _batchMakeButton.interactable = confirmButton.interactable;
        _batchMakeButton.gameObject.SetActive(true);
    }

    private void SyncBatchMakeButtonState(MakeSubPageMake page)
    {
        if (_view == null || page == null || _batchMakeButton == null)
            return;

        var settings = ContinuousMakeSettingsStore.GetFor(_view);
        if (settings.BatchMakeStartMode != 1 || !_batchMakeButton.gameObject.activeInHierarchy)
            return;

        var confirmButton = Traverse.Create(page).Field("buttonConfirm").GetValue<CButton>();
        if (confirmButton == null)
            return;

        _batchMakeButton.interactable = confirmButton.gameObject.activeInHierarchy
            && confirmButton.interactable;
    }

    private void EnsureSettingsButton()
    {
        if (_view == null || _settingsButton != null)
            return;

        var quickEncyclopedia = Traverse.Create(_view).Field("quickEncyclopedia").GetValue<QuickEncyclopedia>();
        var quickRect = quickEncyclopedia == null ? null : quickEncyclopedia.transform as RectTransform;
        if (quickRect == null || quickRect.parent == null)
            return;

        var nativeTemplate = NativeContinuousMakeUiTemplates.FindArrangementSettingButtonTemplate();
        if (nativeTemplate == null)
        {
            NativeContinuousMakeUiTemplates.RequestArrangementSettingButtonTemplate(_ => EnsureSettingsButton());
            return;
        }

        var buttonObj = Instantiate(nativeTemplate.gameObject, quickRect.parent, false);
        buttonObj.name = "BetterTaiwuScrollContinuousMakeSettingsButton";
        var buttonRect = buttonObj.transform as RectTransform;
        if (buttonRect.parent != quickRect.parent)
            buttonRect.SetParent(quickRect.parent, false);

        buttonRect.anchorMin = quickRect.anchorMin;
        buttonRect.anchorMax = quickRect.anchorMax;
        buttonRect.pivot = quickRect.pivot;
        buttonRect.sizeDelta = new Vector2(132f, 52f);
        buttonRect.anchoredPosition = quickRect.anchoredPosition + new Vector2(SettingsButtonOffsetX, 0f);
        buttonRect.localScale = Vector3.one;
        buttonRect.SetSiblingIndex(Math.Max(quickRect.GetSiblingIndex(), 0));

        _settingsButton = buttonObj.GetComponent<CButton>() ?? buttonObj.GetComponentInChildren<CButton>(true);
        if (_settingsButton == null)
        {
            Destroy(buttonObj);
            return;
        }

        SetButtonInteractable(buttonObj, true);
        _settingsButton.ClearAndAddListener(OpenSettingsPanel);

        SetButtonText(buttonObj, ModLocalization.T("Settings"));
        (buttonObj.GetComponent<ButtonTextOverride>() ?? buttonObj.AddComponent<ButtonTextOverride>()).SetText(ModLocalization.T("Settings"));
        ConfigureSettingsButtonTooltip(buttonObj);
        buttonObj.SetActive(true);
        ContinuousMakeSettingsPanel.Preload();
    }

    private void OpenSettingsPanel()
    {
        ContinuousMakeSettingsPanel.Show(_view);
    }

    private MakeSubPageMake GetActiveMakePage()
    {
        if (_view == null)
            return null;

        var subPages = Traverse.Create(_view).Field("subPages").GetValue<MakeSubPage[]>();
        if (subPages == null)
            return null;

        foreach (var page in subPages)
        {
            if (page is MakeSubPageMake makePage && makePage.gameObject.activeInHierarchy)
                return makePage;
        }

        return null;
    }

    private void OnContinuousToggleChanged(bool isOn)
    {
        var settings = ContinuousMakeSettingsStore.Use(_view);
        settings.ContinuousMakeEnabled = isOn;
        ContinuousMakeSettingsStore.Save(_view);
    }

    private void OnBatchMakeButtonClick()
    {
        var page = GetActiveMakePage();
        if (page == null)
            return;

        var confirmButton = Traverse.Create(page).Field("buttonConfirm").GetValue<CButton>();
        if (confirmButton == null || !confirmButton.gameObject.activeInHierarchy || !confirmButton.interactable)
            return;

        ContinuousMakeExecutionController.TryStartOneShotBatch(page);
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string text, float fontSize, TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var obj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var rect = obj.transform as RectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        rect.localScale = Vector3.one;

        var label = obj.GetComponent<TextMeshProUGUI>();
        CopyFont(label, parent);
        label.SetText(text);
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = new Color(0.82f, 0.86f, 0.80f, 1f);
        label.raycastTarget = false;
        return label;
    }

    private static void CopyFont(TextMeshProUGUI target, Component context)
    {
        var source = context == null ? null : context.GetComponentInParent<ViewMake>(true)?.GetComponentInChildren<TextMeshProUGUI>(true);
        if (source == null)
            return;

        target.font = source.font;
        target.fontSharedMaterial = source.fontSharedMaterial;
    }

    private static void SetToggleLabel(GameObject toggleObj, string text)
    {
        foreach (var label in toggleObj.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label == null || string.IsNullOrWhiteSpace(label.text))
                continue;

            label.SetText(text);
            label.enableAutoSizing = true;
            label.fontSizeMax = 26f;
            label.fontSizeMin = 18f;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return;
        }
    }

    private static void SetButtonText(GameObject buttonObj, string text)
    {
        if (buttonObj == null)
            return;

        var changed = false;
        foreach (var label in buttonObj.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label == null)
                continue;

            label.SetText(text);
            label.enableAutoSizing = true;
            label.fontSizeMax = Math.Min(label.fontSizeMax <= 0f ? 30f : label.fontSizeMax, 30f);
            label.fontSizeMin = Math.Max(label.fontSizeMin, 18f);
            label.overflowMode = TextOverflowModes.Ellipsis;
            changed = true;
        }

        if (!changed && buttonObj.transform is RectTransform rect)
            CreateText(rect, text, 28f, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private static void SetButtonInteractable(GameObject buttonObj, bool interactable)
    {
        foreach (var button in buttonObj.GetComponentsInChildren<CButton>(true))
            button.interactable = interactable;
    }

    private static void ConfigureContinuousToggleTooltip(GameObject toggleObj)
    {
        var tooltips = toggleObj.GetComponentsInChildren<TooltipInvoker>(true);
        foreach (var tooltip in tooltips)
        {
            if (tooltip == null)
                continue;

            tooltip.enabled = true;
            tooltip.Type = TipType.Simple;
            tooltip.IsLanguageKey = false;
            tooltip.NeedRefresh = false;
            tooltip.PresetParam = new[]
            {
                ModLocalization.T("Continuous Crafting"),
                ModLocalization.T("When checked, crafting continues to the next batch per your settings once complete.")
            };
        }
    }

    private static void ConfigureSettingsButtonTooltip(GameObject buttonObj)
    {
        var tooltips = buttonObj.GetComponentsInChildren<TooltipInvoker>(true);
        foreach (var tooltip in tooltips)
        {
            if (tooltip == null)
                continue;

            tooltip.enabled = true;
            tooltip.Type = TipType.Simple;
            tooltip.IsLanguageKey = false;
            tooltip.NeedRefresh = false;
            tooltip.PresetParam = new[]
            {
                ModLocalization.T("Continuous Crafting Settings"),
                ModLocalization.T("Open the continuous-crafting settings panel.")
            };
        }
    }

    private static void ConfigureBatchMakeButtonTooltip(GameObject buttonObj)
    {
        var tooltips = buttonObj.GetComponentsInChildren<TooltipInvoker>(true);
        foreach (var tooltip in tooltips)
        {
            if (tooltip == null)
                continue;

            tooltip.enabled = true;
            tooltip.Type = TipType.Simple;
            tooltip.IsLanguageKey = false;
            tooltip.NeedRefresh = false;
            tooltip.PresetParam = new[]
            {
                ModLocalization.T("Batch Crafting"),
                ModLocalization.T("Batch-craft from the current recipe using your continuous-crafting settings.")
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
