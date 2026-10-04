using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

// Own temporary preference key and unshown settings window; user preferences stay intact.
public static class HealingStrokeColorSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
        var assembly = typeof(TextureCompositor).Assembly;
        var settings = assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettings", true);
        var property = settings.GetProperty("HealingStrokeColor", F);
        var cached = settings.GetField("healingStrokeColor", F);
        object previous = cached.GetValue(null);
        string key = "DCFApixels.WhimTex.Test.HealingStrokeColor." + Guid.NewGuid().ToString("N");
        EditorWindow window = null;
        int checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
        try
        {
            cached.SetValue(null, null);
            Check(((Color)property.GetValue(null)).Equals(new Color(.2f, .75f, 1f, .4f)), "Default changed.");
            cached.SetValue(null, previous);
            var save = settings.GetMethod("Save", F); var load = settings.GetMethod("Load", F);
            foreach (var color in new[] { new Color(.9f, .1f, .4f, .25f), Color.clear,
                new Color(-1, 2, float.NaN, float.PositiveInfinity) })
            {
                object[] args = { key, null, color, true }; save.Invoke(null, args);
                Color stored = (Color)args[1]; Color restored = (Color)load.Invoke(null, new object[] { key, true });
                Check(stored.Equals(restored), "RGBA persistence differs.");
                Check(EditorPrefs.GetString(key).Length == 9, "Expected RGBA storage.");
                for (int i = 0; i < 4; i++) Check(!float.IsNaN(restored[i]) && restored[i] >= 0 && restored[i] <= 1, "Invalid channel.");
                Color opaque = (Color)load.Invoke(null, new object[] { key, false });
                Check(opaque.a == 1, "Other colors must still load opaque.");
            }
            object[] opaqueArgs = { key, null, new Color(.1f, .2f, .3f, .1f), false }; save.Invoke(null, opaqueArgs);
            Check(((Color)opaqueArgs[1]).a == 1 && EditorPrefs.GetString(key).Length == 7, "Existing RGB storage changed.");
            var windowType = assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettingsWindow", true);
            window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
            windowType.GetMethod("CreateGUI", F).Invoke(window, null);
            var field = window.rootVisualElement.Q<ColorField>("healingStrokeColor");
            Check(field != null && field.showAlpha && !field.hdr, "Missing RGBA settings control.");
            Check(field.value.Equals((Color)property.GetValue(null)), "UI and preference differ.");
            return "PASS HealingStrokeColorSmoke: " + checks + " checks; default, RGBA persistence, validation, existing RGB settings and UI field.";
        }
        finally
        {
            cached.SetValue(null, previous);
            EditorPrefs.DeleteKey(key);
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
        }
    }
}
