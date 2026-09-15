using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class GradientPresetsSmoke
{
    static readonly Assembly Assembly = typeof(WhimTexGradient).Assembly;
    static object Call(string type, string method, params object[] args) =>
        Assembly.GetType("DCFApixels.WhimTex." + type).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static WhimTexGradient Read(string text) => (WhimTexGradient)Call("WhimTexGradientClipboard", "Read", text);
    public static string Main()
    {
        string guide = File.ReadAllText("Packages/com.dcfapixels.whimtex/Documentation~/AI/README.md");
        foreach (Match example in Regex.Matches(guide, @"```json\s*\n([\s\S]*?)\n```"))
            if (example.Groups[1].Value.Contains("whimtex.gradient")) Read(example.Groups[1].Value);
        var g = new WhimTexGradient();
        g.SetKeys(new[] {new GradientColorKey(new Color(512, 2, .5f, 1), 0), new GradientColorKey(Color.blue, 1)},
            new[] {new GradientAlphaKey(.25f, 0), new GradientAlphaKey(.9f, 1)});
        g.Smoothness = .37f; g.ColorSpace = ColorSpace.Linear;
        g.SetMidpoint(false, 0, .3f); g.SetMidpoint(true, 0, .7f);
        foreach (WhimTexGradientMode mode in Enum.GetValues(typeof(WhimTexGradientMode)))
        {
            g.Mode = mode;
            string json = (string)Call("WhimTexGradientClipboard", "Write", g);
            Check(g.Equals(Read(json)), "Roundtrip " + mode);
            Check(g.Equals(Read("```json\n" + json + "\n```")), "Fenced JSON");
            Check(g.Equals(Read("WhimTex.Gradient/1\n" + JsonUtility.ToJson(g))), "Legacy Copy");
        }
        var copy = Read((string)Call("WhimTexGradientClipboard", "Write", g));
        copy.SetMidpoint(false, 0, .8f);
        Check(g.GetMidpoint(false, 0) == .3f, "Shared key memory");
        const string stops = "[{\"time\":0,\"color\":[1,0,0,1]},{\"time\":1,\"color\":[0,0,1,0]}]";
        Check(Read(stops).ColorKeys.Length == 2, "Bare stops");
        Check(Read("{\"colors\":" + stops + "}").ColorKeys.Length == 2, "Bare object");
        foreach (string invalid in new[] {"{}", "[]", "{\"colors\":[],\"colors\":[]}",
            "{\"colors\":" + stops + ",\"unknown\":1}",
            "{\"format\":\"whimtex.gradient\",\"version\":2,\"gradient\":{\"colors\":" + stops + "}}",
            "[{\"time\":0,\"color\":[99999,0,0,1]}]", stops + " trailing"})
        {
            object[] args = { invalid, null };
            Check(!(bool)Call("WhimTexGradientClipboard", "TryRead", args), "Accepted invalid JSON: " + invalid);
        }
        const string pref = "DCFApixels.WhimTex.PresetsFolder";
        bool existed = EditorPrefs.HasKey(pref); string previous = EditorPrefs.GetString(pref);
        string temporary = Path.Combine(Path.GetTempPath(), "WhimTexGradientSmoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            EditorPrefs.SetString(pref, temporary);
            string path = (string)Call("WhimTexGradientPresets", "Save", g);
            Check(Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _), "GUID filename");
            Check(g.Equals(Read(File.ReadAllText(path))), "Stored HDR preset");
            object[] args = { null };
            Check(((IList)Call("WhimTexGradientPresets", "Read", args)).Count == 1 && args[0] == null, "Preset read");
            var window = ScriptableObject.CreateInstance<WhimTexGradientWindow>();
            try
            {
                window.CreateGUI();
                Check(window.rootVisualElement.Q(className: "whimtex-gradient-preset") != null, "Preset swatch missing");
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(WhimTexGradientWindow).GetMethod("UsePreset", flags).Invoke(window, new object[] {g});
                var applied = (WhimTexGradient)typeof(WhimTexGradientWindow).GetField("gradient", flags).GetValue(window);
                Check(applied.Equals(g) && !ReferenceEquals(applied, g), "Apply must clone preset");
                typeof(WhimTexGradientWindow).GetMethod("RefreshPresets", flags).Invoke(window, null);
                var previews = (IList)typeof(WhimTexGradientWindow).GetField("presetPreviews", flags).GetValue(window);
                Check(previews.Count == 1 && ((Texture2D)previews[0]).height == 2, "Cached preview refresh");
            }
            finally { Undo.ClearUndo(window); UnityEngine.Object.DestroyImmediate(window); }
            Call("WhimTexGradientPresets", "Remove", path);
            Check(!File.Exists(path) && Directory.GetFiles(Path.Combine(temporary, "Gradients", ".trash")).Length == 1, "Recoverable removal");
            Check(((IList)Call("WhimTexGradientPresets", "Read", new object[] {null})).Count == 0, "Trash excluded");
        }
        finally
        {
            if (existed) EditorPrefs.SetString(pref, previous); else EditorPrefs.DeleteKey(pref);
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
        return "PASS: all interpolation modes, HDR, alpha/midpoints, independent copy, legacy/fenced/bare JSON, invalid JSON, GUID presets, recoverable removal; user preferences restored.";
    }
}
