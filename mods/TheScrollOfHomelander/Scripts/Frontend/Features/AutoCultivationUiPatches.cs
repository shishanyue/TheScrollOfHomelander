#nullable disable

using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BetterTaiwuScroll.Frontend;

[HarmonyPatch(typeof(Game.Views.Building.BuildingManage.BuildingManageSubPageChickenCoop), "Awake")]
internal static class ChickenCoopAutoCareAwakePatch
{
    private static void Postfix(Game.Views.Building.BuildingManage.BuildingManageSubPageChickenCoop __instance)
    {
        ChickenCoopAutoCareUiController.GetOrAdd(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(Game.Views.Building.BuildingManage.BuildingManageSubPageChickenCoop), "Refresh")]
internal static class ChickenCoopAutoCareRefreshPatch
{
    private static void Postfix(Game.Views.Building.BuildingManage.BuildingManageSubPageChickenCoop __instance)
    {
        ChickenCoopAutoCareUiController.GetOrAdd(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(Game.Views.Building.BuildingManage.BuildingManageSubPageChickenCoop), nameof(Game.Views.Building.BuildingManage.BuildingManageSubPageChickenCoop.Init))]
internal static class ChickenCoopAutoCareInitPatch
{
    private static void Postfix(Game.Views.Building.BuildingManage.BuildingManageSubPageChickenCoop __instance)
    {
        ChickenCoopAutoCareUiController.GetOrAdd(__instance)?.Refresh();
    }
}

internal sealed class ChickenCoopAutoCareUiController : MonoBehaviour
{
    private const string ButtonName = "BetterTaiwuScrollAutoChickenCareSettingsButton";
    private static readonly FieldInfo EntranceButtonGroupField = AccessTools.Field(typeof(Game.Views.Building.BuildingManage.ViewBuildingManage), "_btnGroup");

    private Game.Views.Building.BuildingManage.BuildingManageSubPageChickenCoop _page;
    private Game.Views.Building.BuildingManage.ViewBuildingManage _buildingView;
    private CButton _settingsButton;
    private Coroutine _layoutRoutine;

    internal static ChickenCoopAutoCareUiController GetOrAdd(Game.Views.Building.BuildingManage.BuildingManageSubPageChickenCoop page)
    {
        if (page == null)
            return null;
        var controller = page.GetComponent<ChickenCoopAutoCareUiController>();
        if (controller == null)
            controller = page.gameObject.AddComponent<ChickenCoopAutoCareUiController>();
        controller._page = page;
        return controller;
    }

    internal void Refresh()
    {
        if (!Plugin.EnableAutoChickenCare)
        {
            if (_settingsButton != null)
                _settingsButton.gameObject.SetActive(false);
            return;
        }

        EnsureButton();
        if (_settingsButton == null)
            return;

        _settingsButton.gameObject.SetActive(true);
        ScheduleLayoutRefresh();
    }

    private void OnDisable()
    {
        if (_layoutRoutine != null)
        {
            StopCoroutine(_layoutRoutine);
            _layoutRoutine = null;
        }
        if (_settingsButton != null)
            _settingsButton.gameObject.SetActive(false);
    }

    private void EnsureButton()
    {
        var trough = FindTroughButton();
        var troughRect = trough?.transform as RectTransform;
        if (troughRect == null || troughRect.parent == null)
            return;

        if (_settingsButton == null)
        {
            var existing = troughRect.parent.Find(ButtonName);
            if (existing != null)
                _settingsButton = existing.GetComponent<CButton>() ?? existing.GetComponentInChildren<CButton>(true);

            if (_settingsButton == null)
            {
                var obj = Instantiate(trough.gameObject, troughRect.parent, false);
                obj.name = ButtonName;
                _settingsButton = obj.GetComponent<CButton>() ?? obj.GetComponentInChildren<CButton>(true);
                if (_settingsButton == null)
                {
                    Destroy(obj);
                    return;
                }
                _settingsButton.ClearAndAddListener(() => ContinuousMakeSettingsPanel.ShowChicken(_page));
                SetButtonText(obj, "设置");
                DisableTooltips(obj);
            }
        }

        ConfigureButton(_settingsButton.gameObject, troughRect);
        _settingsButton.interactable = true;
    }

    private static readonly System.Collections.Generic.HashSet<string> TroughLabels =
        ModLocalization.BuildBilingualLabelSet(new[] { "饲槽" });

    private CButton FindTroughButton()
    {
        if (_buildingView == null)
            _buildingView = _page.GetComponentInParent<Game.Views.Building.BuildingManage.ViewBuildingManage>();
        var buttons = _buildingView == null ? null : EntranceButtonGroupField?.GetValue(_buildingView) as IEnumerable;
        if (buttons == null)
            return null;

        foreach (var entry in buttons)
        {
            var button = entry as CButton;
            if (button == null || button == _settingsButton || !button.gameObject.activeSelf)
                continue;
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label != null && TroughLabels.Contains(label.text?.Trim() ?? string.Empty))
                    return button;
            }
        }

        return null;
    }

    private static void ConfigureButton(GameObject obj, RectTransform troughRect)
    {
        var buttonRect = obj.transform as RectTransform;
        if (buttonRect == null)
            return;
        buttonRect.SetParent(troughRect.parent, false);
        buttonRect.anchorMin = troughRect.anchorMin;
        buttonRect.anchorMax = troughRect.anchorMax;
        buttonRect.pivot = troughRect.pivot;
        buttonRect.sizeDelta = troughRect.sizeDelta;
        buttonRect.localScale = Vector3.one;
        buttonRect.SetSiblingIndex(Math.Min(troughRect.GetSiblingIndex() + 1, troughRect.parent.childCount - 1));
    }

    private void ScheduleLayoutRefresh()
    {
        if (_layoutRoutine != null || !isActiveAndEnabled)
            return;
        _layoutRoutine = StartCoroutine(RefreshLayoutNextFrame());
    }

    private IEnumerator RefreshLayoutNextFrame()
    {
        yield return null;
        _layoutRoutine = null;
        if (_page != null && _settingsButton != null)
            EnsureButton();
    }

    private static void SetButtonText(GameObject obj, string text)
    {
        foreach (var label in obj.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label == null)
                continue;
            label.SetText(ModLocalization.T(text));
            label.raycastTarget = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 18f;
            label.fontSizeMax = 30f;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    private static void DisableTooltips(GameObject obj)
    {
        foreach (var tooltip in obj.GetComponentsInChildren<TooltipInvoker>(true))
            tooltip.enabled = false;
    }
}

[HarmonyPatch(typeof(Game.Views.Building.ViewCricketCollection), "Awake")]
internal static class CricketRoomAutoCareAwakePatch
{
    private static void Postfix(Game.Views.Building.ViewCricketCollection __instance)
    {
        CricketRoomAutoCareUiController.GetOrAdd(__instance)?.Refresh();
    }
}

[HarmonyPatch(typeof(Game.Views.Building.ViewCricketCollection), "OnEnable")]
internal static class CricketRoomAutoCareEnablePatch
{
    private static void Postfix(Game.Views.Building.ViewCricketCollection __instance)
    {
        CricketRoomAutoCareUiController.GetOrAdd(__instance)?.Refresh();
    }
}

internal sealed class CricketRoomAutoCareUiController : MonoBehaviour
{
    private const string ButtonName = "BetterTaiwuScrollAutoCricketRoomSettingsButton";
    private const float ButtonWidth = 132f;
    private const float ButtonHeight = 52f;
    private const float ButtonGap = 12f;
    private static readonly FieldInfo AutoButtonField = AccessTools.Field(typeof(Game.Views.Building.ViewCricketCollection), "btnAutoCricket");

    private Game.Views.Building.ViewCricketCollection _view;
    private CButton _settingsButton;
    private Vector2 _basePosition;
    private bool _basePositionCaptured;
    private Coroutine _layoutRoutine;

    internal static CricketRoomAutoCareUiController GetOrAdd(Game.Views.Building.ViewCricketCollection view)
    {
        if (view == null)
            return null;
        var controller = view.GetComponent<CricketRoomAutoCareUiController>();
        if (controller == null)
            controller = view.gameObject.AddComponent<CricketRoomAutoCareUiController>();
        controller._view = view;
        return controller;
    }

    internal void Refresh()
    {
        if (!Plugin.EnableAutoCricketRoom)
        {
            if (_settingsButton != null)
                _settingsButton.gameObject.SetActive(false);
            return;
        }

        var autoButton = AutoButtonField?.GetValue(_view) as CButton;
        var autoRect = autoButton?.transform as RectTransform;
        if (autoRect == null || autoRect.parent == null)
            return;

        if (_settingsButton == null)
        {
            var existing = autoRect.parent.Find(ButtonName);
            if (existing != null)
                _settingsButton = existing.GetComponent<CButton>() ?? existing.GetComponentInChildren<CButton>(true);
            if (_settingsButton == null)
            {
                var obj = Instantiate(autoButton.gameObject, autoRect.parent, false);
                obj.name = ButtonName;
                _settingsButton = obj.GetComponent<CButton>() ?? obj.GetComponentInChildren<CButton>(true);
                if (_settingsButton == null)
                {
                    Destroy(obj);
                    return;
                }
                _settingsButton.ClearAndAddListener(() => ContinuousMakeSettingsPanel.ShowCricket(_view));
                SetButtonText(obj, "设置");
                DisableTooltips(obj);
            }
        }

        ConfigureButton(_settingsButton.gameObject, autoRect);
        _settingsButton.gameObject.SetActive(true);
        ScheduleLayoutRefresh();
    }

    private void OnDisable()
    {
        if (_layoutRoutine != null)
        {
            StopCoroutine(_layoutRoutine);
            _layoutRoutine = null;
        }
    }

    private void ConfigureButton(GameObject obj, RectTransform autoRect)
    {
        if (!_basePositionCaptured)
        {
            _basePosition = autoRect.anchoredPosition;
            _basePositionCaptured = true;
        }
        var buttonRect = obj.transform as RectTransform;
        if (buttonRect == null)
            return;
        buttonRect.SetParent(autoRect.parent, false);
        buttonRect.anchorMin = autoRect.anchorMin;
        buttonRect.anchorMax = autoRect.anchorMax;
        buttonRect.pivot = autoRect.pivot;
        buttonRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        var autoWidth = Mathf.Max(Mathf.Abs(autoRect.rect.width), Mathf.Abs(autoRect.sizeDelta.x));
        if (autoWidth < 1f)
            autoWidth = ButtonWidth;
        autoRect.anchoredPosition = _basePosition - new Vector2((ButtonGap + ButtonWidth) * 0.5f, 0f);
        buttonRect.anchoredPosition = _basePosition + new Vector2((autoWidth + ButtonGap) * 0.5f, 0f);
        buttonRect.localScale = Vector3.one;
        buttonRect.SetAsLastSibling();
        _settingsButton.interactable = true;
    }

    private void ScheduleLayoutRefresh()
    {
        if (_layoutRoutine != null || !isActiveAndEnabled)
            return;
        _layoutRoutine = StartCoroutine(RefreshLayoutNextFrame());
    }

    private IEnumerator RefreshLayoutNextFrame()
    {
        yield return null;
        _layoutRoutine = null;
        if (_view == null || _settingsButton == null || !isActiveAndEnabled)
            yield break;

        var autoButton = AutoButtonField?.GetValue(_view) as CButton;
        var autoRect = autoButton?.transform as RectTransform;
        if (autoRect != null && autoRect.parent != null)
            ConfigureButton(_settingsButton.gameObject, autoRect);
    }

    private static void SetButtonText(GameObject obj, string text)
    {
        foreach (var label in obj.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label == null)
                continue;
            label.SetText(ModLocalization.T(text));
            label.raycastTarget = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 18f;
            label.fontSizeMax = 30f;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    private static void DisableTooltips(GameObject obj)
    {
        foreach (var tooltip in obj.GetComponentsInChildren<TooltipInvoker>(true))
            tooltip.enabled = false;
    }
}
