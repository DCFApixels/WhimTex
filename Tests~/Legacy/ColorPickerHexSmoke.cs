using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class ColorPickerHexSmoke
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks;
    static object Get(object o, string name) => o.GetType().GetField(name, F).GetValue(o);
    static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    static WhimTexColorPicker Open(bool hdr, bool alpha) => (WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open", F).Invoke(null,
        new object[] { new Color(hdr ? 4 : 1, 0, 0, .3f), hdr, alpha, WhimTexColorRange.Switchable, null, (Action<Color>)(_ => { }), null, null });
    static void Close(WhimTexColorPicker picker) { if (picker != null) typeof(WhimTexColorPicker).GetMethod("Finish", F).Invoke(picker, new object[] { false }); }
    public static string Main()
    {
        if (Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0) return "BLOCKED: close the active color picker first.";
        checks = 0;
        var focus = EditorWindow.focusedWindow;
        WhimTexColorPicker picker = null;
        try
        {
            foreach (bool hdr in new[] { false, true }) foreach (bool alpha in new[] { false, true })
            {
                picker = Open(hdr, alpha);
                var hex = (TextField)Get(picker, "hex");
                var prefix = hex.Q<Label>(className: "whimtex-picker-hex-prefix");
                Check(prefix != null && prefix.text == "#" && hex.IndexOf(prefix) < hex.IndexOf(hex.Q(className: TextField.inputUssClassName)), "Separate prefix precedes input");
                foreach (string value in new[] { "aBcDeF", "#abcdef", " ABCDEF ", "#AbCdEf80", "abcdef00", "#aBcF", "aBc", "#ABC" })
                {
                    float oldAlpha = ((Color)Get(picker, "color")).a;
                    float scale = hdr ? Mathf.Pow(2, (float)Get(picker, "exposure")) : 1;
                    hex.value = value;
                    string digits = value.Trim().TrimStart('#');
                    ColorUtility.TryParseHtmlString("#" + digits, out Color expected);
                    float expectedAlpha = alpha && (digits.Length == 4 || digits.Length == 8) ? expected.a : oldAlpha;
                    expected *= scale; expected.a = expectedAlpha;
                    Color actual = (Color)Get(picker, "color");
                    Check(Mathf.Abs(actual.r - expected.r) + Mathf.Abs(actual.g - expected.g) + Mathf.Abs(actual.b - expected.b) + Mathf.Abs(actual.a - expected.a) < .0001f, "RGBA input " + value);
                    Check(hex.value.Length == 6 && !hex.value.Contains("#") && hex.value == hex.value.ToUpperInvariant(), "Normalized RGB display " + value);
                }
                Color saved = (Color)Get(picker, "color");
                foreach (string bad in new[] { "", "#", "GGGGGG", "12345", "1234567", "123456789" })
                { hex.value = bad; Check((Color)Get(picker, "color") == saved && hex.value.Length == 6, "Invalid input restores display"); }
                Close(picker); picker = null;
            }
            return "PASS: " + checks + " HEX checks (prefix, case, optional #, RGB/RGBA, shorthand, HDR, fixed alpha and invalid input).";
        }
        finally { Close(picker); if (focus != null) focus.Focus(); }
    }
    public static string PasteSetup()
    {
        if (Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0) return "BLOCKED: close the active color picker first.";
        var picker = Open(false, true);
        picker.name = "Color picker HEX paste test";
        return "Temporary HEX test window ready.";
    }
    public static string PasteVerify()
    {
        WhimTexColorPicker picker = null;
        foreach (var p in Resources.FindObjectsOfTypeAll<WhimTexColorPicker>()) if (p.name == "Color picker HEX paste test") picker = p;
        if (picker == null) throw new Exception("Run PasteSetup first");
        string clipboard = GUIUtility.systemCopyBuffer;
        try
        {
            var hex = (TextField)Get(picker, "hex");
            var prefix = hex.Q<Label>(className: "whimtex-picker-hex-prefix");
            var input = hex.Q(className: TextField.inputUssClassName);
            Check(prefix.worldBound.xMax <= input.worldBound.xMin && prefix.resolvedStyle.unityTextAlign == TextAnchor.MiddleRight, "Prefix left of input, right aligned");
            var focused = input.Q<TextElement>();
            focused.Focus(); hex.SelectAll();
            GUIUtility.systemCopyBuffer = "#aBcDeF80";
            using (var e = ExecuteCommandEvent.GetPooled("Paste")) { e.target = focused; focused.SendEvent(e); }
            focused.Blur();
            Color actual = (Color)Get(picker, "color");
            Check(Mathf.Abs(actual.r - 171 / 255f) < .0001f && Mathf.Abs(actual.a - 128 / 255f) < .0001f && hex.value == "ABCDEF", "Clipboard paste commits RGB and alpha: color=" + actual + ", value=" + hex.value + ", text=" + hex.text + ", focused=" + focused.name);
            return "PASS: actual clipboard Paste command, delayed commit, RGB display and prefix layout.";
        }
        finally { GUIUtility.systemCopyBuffer = clipboard; Close(picker); }
    }
}
