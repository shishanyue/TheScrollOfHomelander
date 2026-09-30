#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Config;
using FrameWork.UISystem.UIElements;
using Game.Views;
using Game.Views.Bottom;
using Game.Views.WorldStatePanel;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterTaiwuScroll.Frontend;

[Serializable]
public sealed class WorldStateIconVisibilitySettings
{
    public int Version = 1;
    public List<int> HiddenTemplateIds = new List<int>();

    internal WorldStateIconVisibilitySettings Clone()
    {
        return new WorldStateIconVisibilitySettings
        {
            Version = Version,
            HiddenTemplateIds = new List<int>(HiddenTemplateIds)
        };
    }

    internal void Normalize()
    {
        Version = 1;
        HiddenTemplateIds ??= new List<int>();
        var unique = new HashSet<int>();
        for (var i = 0; i < HiddenTemplateIds.Count; i++)
        {
            var templateId = HiddenTemplateIds[i];
            if (templateId >= 0 && templateId <= sbyte.MaxValue)
                unique.Add(templateId);
        }

        HiddenTemplateIds = new List<int>(unique);
        HiddenTemplateIds.Sort();
    }
}

internal static class WorldStateIconVisibilitySettingsStore
{
    private const string FileName = "WorldStateIconVisibilitySettings.json";
    private static readonly HashSet<int> HiddenTemplateIds = new HashSet<int>();

    internal static WorldStateIconVisibilitySettings Current { get; private set; } =
        new WorldStateIconVisibilitySettings();

    internal static int Revision { get; private set; }

    internal static int HiddenCount => HiddenTemplateIds.Count;

    internal static void Load()
    {
        Current = new WorldStateIconVisibilitySettings();
        try
        {
            foreach (var path in ModUserDataPaths.GetFilePathCandidates(FileName))
            {
                if (!File.Exists(path))
                    continue;

                Current = JsonUtility.FromJson<WorldStateIconVisibilitySettings>(File.ReadAllText(path))
                    ?? new WorldStateIconVisibilitySettings();
                break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to load world state icon settings: " + ex);
            Current = new WorldStateIconVisibilitySettings();
        }

        Current.Normalize();
        RebuildLookup();
        Revision++;
    }

    internal static bool IsHidden(int templateId)
    {
        return HiddenTemplateIds.Contains(templateId);
    }

    internal static bool Toggle(int templateId)
    {
        if (templateId < 0 || templateId > sbyte.MaxValue)
            return false;

        var hidden = !HiddenTemplateIds.Contains(templateId);
        if (hidden)
            HiddenTemplateIds.Add(templateId);
        else
            HiddenTemplateIds.Remove(templateId);

        Current.HiddenTemplateIds = new List<int>(HiddenTemplateIds);
        Current.HiddenTemplateIds.Sort();
        Revision++;
        Save();
        return hidden;
    }

    private static void Save()
    {
        try
        {
            Current.Normalize();
            AsyncSettingsSaveQueue.Enqueue(ModUserDataPaths.GetFilePath(FileName), Current.Clone());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to save world state icon settings: " + ex);
        }
    }

    private static void RebuildLookup()
    {
        HiddenTemplateIds.Clear();
        for (var i = 0; i < Current.HiddenTemplateIds.Count; i++)
            HiddenTemplateIds.Add(Current.HiddenTemplateIds[i]);
    }
}

internal static class WorldStateIconVisibilityController
{
    private const string ButtonName = "BetterTaiwuScroll_WorldStateVisibilityButton";
    private const string ButtonLabelName = "BetterTaiwuScroll_Label";
    private const string WorldStateKeyPrefix = "WorldState_";
    private const float VisibilityButtonWidth = 62f;
    private const float VisibilityButtonMinHeight = 30f;
    private const float VisibilityButtonGap = 8f;

    private static readonly Color ButtonNormalColor = new Color(0.105f, 0.14f, 0.145f, 0.96f);
    private static readonly Color ButtonHighlightedColor = new Color(0.16f, 0.22f, 0.22f, 1f);
    private static readonly Color ButtonPressedColor = new Color(0.075f, 0.1f, 0.105f, 1f);
    private static readonly Color ButtonDisabledColor = new Color(0.08f, 0.09f, 0.09f, 0.55f);

    private static readonly FieldInfo WorldStateObjectsField =
        AccessTools.Field(typeof(ViewWorldState), "_worldStateObjects");
    private static readonly MethodInfo RefreshWorldStateMethod =
        AccessTools.Method(typeof(ViewWorldState), "RefreshWorldState");
    private static readonly MethodInfo RefreshWorldStatePanelMethod =
        AccessTools.Method(typeof(ViewWorldStatePanel), "RefreshWorldState");
    private static readonly List<object> HiddenStateObjects = new List<object>();
    private static readonly List<WorldStateVisibilityButtonMarker> Buttons =
        new List<WorldStateVisibilityButtonMarker>();

    private static ViewWorldState _mapView;
    private static FieldInfo _shouldActiveField;
    private static Type _stateDataType;
    private static int _cachedDictionaryCount = -1;
    private static int _cachedSettingsRevision = -1;
    private static bool _suppress;

    internal static void Initialize()
    {
        _suppress = false;
        HiddenStateObjects.Clear();
        _cachedDictionaryCount = -1;
        _cachedSettingsRevision = -1;
    }

    internal static void RegisterMapView(ViewWorldState view)
    {
        if (view == null)
            return;

        _mapView = view;
        RebuildHiddenStateCache(view);
    }

    internal static void UnregisterMapView(ViewWorldState view)
    {
        if (_mapView != view)
            return;

        _mapView = null;
        HiddenStateObjects.Clear();
        _cachedDictionaryCount = -1;
    }

    internal static void AfterMapRefresh(ViewWorldState view)
    {
        if (view == null)
            return;

        _mapView = view;
        RebuildHiddenStateCache(view);
        ApplyHiddenStateCache();
    }

    internal static void BeforeMapLateUpdate(ViewWorldState view)
    {
        if (_suppress
            || !Plugin.EnableWorldStateIconVisibility
            || WorldStateIconVisibilitySettingsStore.HiddenCount == 0
            || view == null)
        {
            return;
        }

        _mapView = view;
        var dictionary = GetWorldStateDictionary(view);
        if (dictionary == null)
            return;

        if (_cachedDictionaryCount != dictionary.Count
            || _cachedSettingsRevision != WorldStateIconVisibilitySettingsStore.Revision)
        {
            RebuildHiddenStateCache(view, dictionary);
        }

        ApplyHiddenStateCache();
    }

    internal static void BindButton(WorldStateItem worldStateItem, WorldStatePanelItem panelItem)
    {
        if (worldStateItem == null || panelItem == null || panelItem.jumpButton == null)
            return;

        var parent = panelItem.jumpButton.transform.parent as RectTransform;
        if (parent == null)
            return;

        var existing = parent.Find(ButtonName);
        var marker = existing == null ? null : existing.GetComponent<WorldStateVisibilityButtonMarker>();
        if (!Plugin.EnableWorldStateIconVisibility)
        {
            if (marker != null)
                marker.RefreshVisibility();
            return;
        }

        if (marker == null)
            marker = CreateButton(panelItem, parent);

        marker?.Bind(worldStateItem.TemplateId, panelItem.jumpButton, panelItem.desc);
    }

    internal static void Toggle(int templateId)
    {
        if (!Plugin.EnableWorldStateIconVisibility)
            return;

        WorldStateIconVisibilitySettingsStore.Toggle(templateId);
        RefreshButtons();
        RefreshMapView();
    }

    internal static void OnSettingChanged()
    {
        RefreshButtons();
        RefreshActivePanels();
        RefreshMapView();
    }

    internal static void RegisterButton(WorldStateVisibilityButtonMarker marker)
    {
        if (marker != null && !Buttons.Contains(marker))
            Buttons.Add(marker);
    }

    internal static void UnregisterButton(WorldStateVisibilityButtonMarker marker)
    {
        Buttons.Remove(marker);
    }

    internal static void DisposeAll()
    {
        _suppress = true;
        RefreshMapView();

        var copy = Buttons.ToArray();
        Buttons.Clear();
        for (var i = 0; i < copy.Length; i++)
        {
            if (copy[i] != null && copy[i].gameObject != null)
                UnityEngine.Object.Destroy(copy[i].gameObject);
        }

        _mapView = null;
        HiddenStateObjects.Clear();
        _cachedDictionaryCount = -1;
        _cachedSettingsRevision = -1;
    }

    private static WorldStateVisibilityButtonMarker CreateButton(
        WorldStatePanelItem panelItem,
        RectTransform parent)
    {
        var sourceButton = panelItem.jumpButton;
        var sourceRect = sourceButton.transform as RectTransform;
        if (sourceRect == null)
            return null;

        var buttonObject = new GameObject(
            ButtonName,
            typeof(RectTransform),
            typeof(CImage),
            typeof(CButton),
            typeof(LayoutElement),
            typeof(Outline));
        buttonObject.layer = sourceButton.gameObject.layer;
        var rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        CopyRect(sourceRect, rect);
        PositionBeforeSource(sourceRect, rect);
        buttonObject.GetComponent<LayoutElement>().ignoreLayout = true;

        var image = buttonObject.GetComponent<CImage>();
        ConfigureButtonImage(image);

        var outline = buttonObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.42f, 0.46f, 0.41f, 0.9f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        var button = buttonObject.GetComponent<CButton>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = ButtonNormalColor;
        colors.highlightedColor = ButtonHighlightedColor;
        colors.selectedColor = ButtonHighlightedColor;
        colors.pressedColor = ButtonPressedColor;
        colors.disabledColor = ButtonDisabledColor;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.navigation = sourceButton.navigation;

        var labelObject = new GameObject(ButtonLabelName, typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.layer = buttonObject.layer;
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(2f, 1f);
        labelRect.offsetMax = new Vector2(-2f, -1f);

        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10f;
        label.fontSizeMax = 16f;
        label.raycastTarget = false;
        label.overflowMode = TextOverflowModes.Ellipsis;

        var marker = buttonObject.AddComponent<WorldStateVisibilityButtonMarker>();
        marker.Initialize(button, label, rect);
        RegisterButton(marker);
        return marker;
    }

    private static void CopyRect(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.localScale = source.localScale;
        target.localRotation = source.localRotation;
        target.anchoredPosition = source.anchoredPosition;
    }

    internal static void PositionBeforeSource(RectTransform source, RectTransform target)
    {
        var sourceWidth = GetRectDimension(source, horizontal: true, 38f);
        var sourceHeight = GetRectDimension(source, horizontal: false, VisibilityButtonMinHeight);
        target.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, VisibilityButtonWidth);
        target.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            Mathf.Max(VisibilityButtonMinHeight, sourceHeight));

        var sourceLeft = source.anchoredPosition.x - sourceWidth * source.pivot.x;
        var targetX = sourceLeft
            - VisibilityButtonGap
            - VisibilityButtonWidth * (1f - target.pivot.x);
        target.anchoredPosition = new Vector2(targetX, source.anchoredPosition.y);
        target.SetSiblingIndex(source.GetSiblingIndex());
    }

    private static float GetRectDimension(RectTransform rect, bool horizontal, float fallback)
    {
        var value = horizontal ? Mathf.Abs(rect.rect.width) : Mathf.Abs(rect.rect.height);
        if (value < 8f)
            value = horizontal ? Mathf.Abs(rect.sizeDelta.x) : Mathf.Abs(rect.sizeDelta.y);
        return value < 8f ? fallback : value;
    }

    private static void ConfigureButtonImage(CImage target)
    {
        target.sprite = null;
        target.overrideSprite = null;
        target.type = Image.Type.Simple;
        target.preserveAspect = false;
        target.color = ButtonNormalColor;
        target.material = null;
        target.raycastTarget = true;
    }

    private static void RefreshButtons()
    {
        for (var i = Buttons.Count - 1; i >= 0; i--)
        {
            var marker = Buttons[i];
            if (marker == null)
            {
                Buttons.RemoveAt(i);
                continue;
            }

            marker.RefreshVisibility();
        }
    }

    private static void RefreshActivePanels()
    {
        if (RefreshWorldStatePanelMethod == null)
            return;

        foreach (var panel in Resources.FindObjectsOfTypeAll<ViewWorldStatePanel>())
        {
            if (panel == null || panel.gameObject == null || !panel.gameObject.activeInHierarchy)
                continue;

            try
            {
                RefreshWorldStatePanelMethod.Invoke(panel, null);
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogWarning("[BetterTaiwuScroll] Failed to refresh world state panel buttons: "
                    + (ex.InnerException ?? ex));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BetterTaiwuScroll] Failed to refresh world state panel buttons: " + ex);
            }
        }
    }

    private static void RefreshMapView()
    {
        var view = _mapView;
        if (view == null || RefreshWorldStateMethod == null)
            return;

        try
        {
            RefreshWorldStateMethod.Invoke(view, null);
        }
        catch (TargetInvocationException ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to refresh world state icons: "
                + (ex.InnerException ?? ex));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BetterTaiwuScroll] Failed to refresh world state icons: " + ex);
        }
    }

    private static IDictionary GetWorldStateDictionary(ViewWorldState view)
    {
        return WorldStateObjectsField?.GetValue(view) as IDictionary;
    }

    private static void RebuildHiddenStateCache(ViewWorldState view, IDictionary dictionary = null)
    {
        HiddenStateObjects.Clear();
        _cachedSettingsRevision = WorldStateIconVisibilitySettingsStore.Revision;
        dictionary ??= GetWorldStateDictionary(view);
        _cachedDictionaryCount = dictionary?.Count ?? -1;

        if (_suppress
            || !Plugin.EnableWorldStateIconVisibility
            || WorldStateIconVisibilitySettingsStore.HiddenCount == 0
            || dictionary == null)
        {
            return;
        }

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Value == null
                || entry.Key is not string key
                || !TryGetTemplateId(key, out var templateId)
                || !WorldStateIconVisibilitySettingsStore.IsHidden(templateId))
            {
                continue;
            }

            EnsureStateFields(entry.Value);
            if (_shouldActiveField != null)
                HiddenStateObjects.Add(entry.Value);
        }
    }

    private static void ApplyHiddenStateCache()
    {
        if (_suppress
            || !Plugin.EnableWorldStateIconVisibility
            || _shouldActiveField == null)
        {
            return;
        }

        for (var i = 0; i < HiddenStateObjects.Count; i++)
        {
            var state = HiddenStateObjects[i];
            if (state != null && (bool)_shouldActiveField.GetValue(state))
                _shouldActiveField.SetValue(state, false);
        }
    }

    private static void EnsureStateFields(object stateData)
    {
        var type = stateData.GetType();
        if (_stateDataType == type)
            return;

        _stateDataType = type;
        _shouldActiveField = AccessTools.Field(type, "ShouldActive");
    }

    private static bool TryGetTemplateId(string key, out int templateId)
    {
        templateId = -1;
        if (string.IsNullOrEmpty(key)
            || !key.StartsWith(WorldStateKeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var index = WorldStateKeyPrefix.Length;
        if (index >= key.Length || key[index] < '0' || key[index] > '9')
            return false;

        var value = 0;
        while (index < key.Length)
        {
            var character = key[index];
            if (character == '_')
                break;
            if (character < '0' || character > '9')
                return false;

            value = value * 10 + character - '0';
            index++;
        }

        templateId = value;
        return true;
    }
}

internal sealed class WorldStateVisibilityButtonMarker : MonoBehaviour
{
    private static readonly Color HideColor = new Color(0.92f, 0.86f, 0.7f, 1f);
    private static readonly Color RestoreColor = new Color(0.38f, 0.78f, 0.94f, 1f);

    private CButton _button;
    private TextMeshProUGUI _label;
    private RectTransform _rect;
    private int _templateId = -1;

    internal void Initialize(CButton button, TextMeshProUGUI label, RectTransform rect)
    {
        _button = button;
        _label = label;
        _rect = rect;
    }

    internal void Bind(int templateId, CButton sourceButton, TextMeshProUGUI sourceText)
    {
        _templateId = templateId;
        if (_button == null)
            _button = GetComponent<CButton>();
        if (_label == null)
            _label = transform.Find("BetterTaiwuScroll_Label")?.GetComponent<TextMeshProUGUI>();
        if (_rect == null)
            _rect = transform as RectTransform;

        if (_rect != null && sourceButton != null)
        {
            var sourceRect = sourceButton.transform as RectTransform;
            if (sourceRect != null)
                WorldStateIconVisibilityController.PositionBeforeSource(sourceRect, _rect);
        }

        if (_label != null && sourceText != null)
        {
            _label.font = sourceText.font;
            _label.fontSharedMaterial = sourceText.fontSharedMaterial;
        }

        _button?.ClearAndAddListener(OnClick);
        RefreshVisibility();
    }

    internal void RefreshVisibility()
    {
        var enabled = Plugin.EnableWorldStateIconVisibility && _templateId >= 0;
        if (gameObject.activeSelf != enabled)
            gameObject.SetActive(enabled);
        if (!enabled || _label == null)
            return;

        var hidden = WorldStateIconVisibilitySettingsStore.IsHidden(_templateId);
        _label.SetText(ModLocalization.T(hidden ? "恢复" : "隐藏"));
        _label.color = hidden ? RestoreColor : HideColor;
    }

    private void OnClick()
    {
        WorldStateIconVisibilityController.Toggle(_templateId);
    }

    private void OnDestroy()
    {
        WorldStateIconVisibilityController.UnregisterButton(this);
    }
}

[HarmonyPatch(typeof(ViewWorldState), "OnInit")]
internal static class ViewWorldStateIconVisibilityOnInitPatch
{
    private static void Postfix(ViewWorldState __instance)
    {
        WorldStateIconVisibilityController.RegisterMapView(__instance);
    }
}

[HarmonyPatch(typeof(ViewWorldState), "OnDestroy")]
internal static class ViewWorldStateIconVisibilityOnDestroyPatch
{
    private static void Postfix(ViewWorldState __instance)
    {
        WorldStateIconVisibilityController.UnregisterMapView(__instance);
    }
}

[HarmonyPatch(typeof(ViewWorldState), "RefreshWorldState")]
internal static class ViewWorldStateIconVisibilityRefreshPatch
{
    private static void Postfix(ViewWorldState __instance)
    {
        WorldStateIconVisibilityController.AfterMapRefresh(__instance);
    }
}

[HarmonyPatch(typeof(ViewWorldState), "LateUpdate")]
internal static class ViewWorldStateIconVisibilityLateUpdatePatch
{
    private static void Prefix(ViewWorldState __instance)
    {
        WorldStateIconVisibilityController.BeforeMapLateUpdate(__instance);
    }
}

[HarmonyPatch(typeof(ViewWorldStatePanel), "SetData",
    new Type[] { typeof(WorldStateItem), typeof(WorldStatePanelItem) })]
internal static class ViewWorldStatePanelIconVisibilitySetDataPatch
{
    private static void Postfix(WorldStateItem worldStateItem, WorldStatePanelItem panelItem)
    {
        WorldStateIconVisibilityController.BindButton(worldStateItem, panelItem);
    }
}
