using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class ColorPickerModeSmoke
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    const string Preference = "WhimTex.ColorPicker.ColorMode";
    static object Get(object owner, string name) => owner.GetType().GetField(name, F).GetValue(owner);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Close(WhimTexColorPicker picker, bool confirm)
    { if (picker != null) typeof(WhimTexColorPicker).GetMethod("Finish", F).Invoke(picker, new object[] { confirm }); }

    public static string Main()
    {
        if (Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0)
            return "BLOCKED: close the active color picker first.";
        bool hadPreference = EditorPrefs.HasKey(Preference);
        int previous = EditorPrefs.GetInt(Preference);
        var focus = EditorWindow.focusedWindow;
        WhimTexColorPicker picker = null;
        var value = new Color(2.5f, .375f, .125f, .6f);
        int changes = 0;
        Func<WhimTexColorPicker> open = () => (WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open", F).Invoke(null,
            new object[] { value, true, true, WhimTexColorRange.Switchable, null, (Action<Color>)(_ => changes++), null, null });
        try
        {
            EditorPrefs.DeleteKey(Preference);
            picker = open();
            Check(((PopupField<string>)Get(picker, "mode")).index == 2, "Initial mode is HSV");
            Close(picker, true); picker = null;
            foreach (int index in new[] { 0, 1, 2 })
            {
                picker = open();
                ((PopupField<string>)Get(picker, "mode")).index = index;
                Check(EditorPrefs.GetInt(Preference, -1) == index, "Selection persisted immediately");
                Check(((Color)Get(picker, "color")).Equals(value) && changes == 0, "Mode selection does not edit RGBA");
                Close(picker, index != 1); picker = null;
                picker = open();
                Check(((PopupField<string>)Get(picker, "mode")).index == index, "Mode restored after close or cancel");
                picker.CreateGUI();
                Check(((PopupField<string>)Get(picker, "mode")).index == index, "Mode restored after UI rebuild");
                var channels = (Slider[])Get(picker, "channels");
                Check(channels[0].label == (index == 2 ? "H" : "R"), "Correct channel labels");
                Check(((Color)Get(picker, "color")).Equals(value) && changes == 0, "Restoring mode does not edit RGBA");
                Close(picker, true); picker = null;
            }
            foreach (int invalid in new[] { -1, 3, int.MaxValue })
            {
                EditorPrefs.SetInt(Preference, invalid);
                picker = open();
                Check(((PopupField<string>)Get(picker, "mode")).index == 2, "Invalid preference falls back to HSV");
                Close(picker, true); picker = null;
            }
            return "PASS: all three color modes persist across close/cancel/reopen and UI rebuild; default/invalid preference, labels and unchanged HDR/RGBA verified.";
        }
        finally
        {
            Close(picker, true);
            if (hadPreference) EditorPrefs.SetInt(Preference, previous); else EditorPrefs.DeleteKey(Preference);
            if (focus != null) focus.Focus();
        }
    }
}
