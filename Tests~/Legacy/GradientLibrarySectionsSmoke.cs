using System;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class GradientLibrarySectionsSmoke
{
    const string HistoryKey = "DCFApixels.WhimTex.Gradient.HistoryExpanded";
    const string PresetsKey = "DCFApixels.WhimTex.Gradient.PresetsExpanded";
    const string PickerKey = "DCFApixels.WhimTex.ColorPicker.HistoryExpanded";
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    public static string Main()
    {
        if (Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>().Length != 0 ||
            Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0)
            return "BLOCKED: close the active picker/gradient window first.";
        var focus = EditorWindow.focusedWindow;
        bool hadHistory = EditorPrefs.HasKey(HistoryKey), hadPresets = EditorPrefs.HasKey(PresetsKey);
        bool oldHistory = EditorPrefs.GetBool(HistoryKey), oldPresets = EditorPrefs.GetBool(PresetsKey);
        bool hadPicker = EditorPrefs.HasKey(PickerKey), oldPicker = EditorPrefs.GetBool(PickerKey);
        WhimTexGradientWindow window = null;
        WhimTexColorPicker picker = null;
        try
        {
            EditorPrefs.DeleteKey(HistoryKey);
            EditorPrefs.DeleteKey(PresetsKey);
            for (int pass = 0; pass < 4; pass++)
            {
                // A live panel is required for Foldout value-change events.
                window = ScriptableObject.CreateInstance<WhimTexGradientWindow>();
                window.ShowUtility();
                window.CreateGUI();
                var history = window.rootVisualElement.Q<Foldout>("gradientColorHistory");
                var presets = window.rootVisualElement.Q<Foldout>("gradientPresets");
                Check(history != null && presets != null, "Both sections are foldouts");
                Check(history.value == (pass == 0 || pass == 2), "History restored independently");
                Check(presets.value == (pass < 2), "Presets restored independently");
                Check(history.text == "History" && presets.text == "Presets", "Section labels");
                Check(history.ClassListContains("whimtex-color-library-section") &&
                    presets.ClassListContains("whimtex-color-library-section"), "Shared header styling");
                Check(history.contentContainer.Q<ScrollView>() != null &&
                    presets.contentContainer.Q<ScrollView>() != null, "Palettes inside collapsible content");
                var refresh = presets.Q<Button>("gradientPresetsRefresh");
                Check(refresh != null && !presets.contentContainer.Contains(refresh), "Refresh remains in header");
                using (var e = NavigationSubmitEvent.GetPooled()) refresh.SendEvent(e);
                Check(presets.value == (pass < 2), "Refresh does not toggle Presets");
                history.value = pass == 1;
                presets.value = pass == 0;
                Check(EditorPrefs.GetBool(HistoryKey, true) == history.value &&
                    EditorPrefs.GetBool(PresetsKey, true) == presets.value, "Preferences saved immediately");
                UnityEngine.Object.DestroyImmediate(window);
                window = null;
            }
            EditorPrefs.DeleteKey(PickerKey);
            for (int pass = 0; pass < 3; pass++)
            {
                picker = ScriptableObject.CreateInstance<WhimTexColorPicker>();
                picker.ShowUtility();
                picker.CreateGUI();
                var history = picker.rootVisualElement.Q<Foldout>(className: "whimtex-picker-heading");
                Check(history.value == (pass != 1), "Picker History restored");
                Check(history.ClassListContains("whimtex-color-library-section"), "Picker shares header style");
                history.value = pass == 1;
                Check(EditorPrefs.GetBool(PickerKey, true) == history.value, "Picker preference saved");
                Check(!EditorPrefs.GetBool(HistoryKey, true), "Picker does not change gradient preference");
                UnityEngine.Object.DestroyImmediate(picker);
                picker = null;
            }
            return "PASS: gradient and picker foldouts, shared style, independent persistent states, header refresh, and collapsible content.";
        }
        finally
        {
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            if (picker != null) UnityEngine.Object.DestroyImmediate(picker);
            if (hadHistory) EditorPrefs.SetBool(HistoryKey, oldHistory); else EditorPrefs.DeleteKey(HistoryKey);
            if (hadPresets) EditorPrefs.SetBool(PresetsKey, oldPresets); else EditorPrefs.DeleteKey(PresetsKey);
            if (hadPicker) EditorPrefs.SetBool(PickerKey, oldPicker); else EditorPrefs.DeleteKey(PickerKey);
            if (focus != null) focus.Focus();
        }
    }
}
