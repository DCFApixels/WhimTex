using System.Threading.Tasks;
using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class ColorPickerHexTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks;
    static object Get(object o, string name) => o.GetType().GetField(name, F).GetValue(o);
    static void Check(bool value, string name) { T.True(value, name); }
    static WhimTexColorPicker Open(bool hdr, bool alpha) => Scope.OwnWindow((WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open", F).Invoke(null,
        new object[] { new Color(hdr ? 4 : 1, 0, 0, .3f), hdr, alpha, WhimTexColorRange.Switchable, null, (Action<Color>)(_ => { }), null, null }));
    static void Close(WhimTexColorPicker picker) { if (picker != null) typeof(WhimTexColorPicker).GetMethod("Finish", F).Invoke(picker, new object[] { false }); }
    private static string BodyRun()
    {
        T.True(!(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0), "Close the borrowed active picker before this case");
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
            return null;
        }
        finally { Close(picker); if (focus != null) focus.Focus(); }
    }
    private static string PasteSetup()
    {
        T.True(!(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0), "Close the borrowed active picker before this case");
        var picker = Open(false, true);
        picker.name = (Scope.Tag + "-hex-paste");
        return null;
    }
    private static string PasteVerify()
    {
        WhimTexColorPicker picker = null;
        foreach (var p in Resources.FindObjectsOfTypeAll<WhimTexColorPicker>()) if (p.name == (Scope.Tag + "-hex-paste")) picker = p;
        T.True(!(picker == null), "Run PasteSetup first");
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
            return null;
        }
        finally { GUIUtility.systemCopyBuffer = clipboard; Close(picker); }
    }

private static async Task<string> BodyLayoutScenario() {
PasteSetup(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); PasteVerify();
return null;
}

public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyLayoutScenario(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
