// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class WhimTexGradientContractTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("WhimTexGradientContractTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteMain();
    });

    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static int checks;
    static void Check(bool value, string reason) { context.True(value, reason); }
    static void Near(float a, float b, float tolerance = .00002f) => Check(Mathf.Abs(a-b) <= tolerance, a + " != " + b);
    static float Neutral(float value, WhimTexGradientMode mode, ColorSpace space)
    {
        if (mode == WhimTexGradientMode.Classic)
            return space == ColorSpace.Gamma ? value : Mathf.GammaToLinearSpace(value);
        float linear = mode == WhimTexGradientMode.Perceptual ? value*value*value : value;
        return space == ColorSpace.Linear ? linear : Mathf.LinearToGammaSpace(linear);
    }
    private static void ExecuteMain()
    {
        checks = 0;
        var g = new WhimTexGradient();
        Check(typeof(WhimTexGradient).GetField("transition", Flags) == null, "No serialized transition");
        Check(typeof(WhimTexGradient).GetProperty("Transition", Flags) == null, "No transition property");
        Check(!JsonUtility.ToJson(g).Contains("transition"), "No transition output");
        foreach (float midpoint in new[] {.01f, .1f, .5f, .72700745f, .9437751f, .99f})
        foreach (float smooth in new[] {0f, .5f, 1f})
        foreach (var mode in new[] {WhimTexGradientMode.Classic, WhimTexGradientMode.Linear, WhimTexGradientMode.Perceptual})
        foreach (var space in new[] {ColorSpace.Linear, ColorSpace.Gamma})
        {
            g = new WhimTexGradient { Mode = mode, ColorSpace = space, Smoothness = smooth };
            g.SetKeys(new[] {new GradientColorKey(Color.white, .1f), new GradientColorKey(Color.white, .4f), new GradientColorKey(Color.black, .95f)},
                new[] {new GradientAlphaKey(1, .05f), new GradientAlphaKey(0, .85f)});
            g.SetMidpoint(false, 1, midpoint); g.SetMidpoint(true, 0, midpoint);
            float colorTime = Mathf.Lerp(.4f, .95f, midpoint), alphaTime = Mathf.Lerp(.05f, .85f, midpoint);
            Near(g.Evaluate(colorTime).r, Neutral(.5f, mode, space), .0001f);
            Near(g.Evaluate(alphaTime).a, .5f, .0001f);
            Color previous = g.Evaluate(0);
            for (int i = 0; i <= 2000; i++)
            {
                float t = i / 2000f; Color c = g.Evaluate(t);
                Check(c.r >= -.00001f && c.r <= 1.00001f && c.a >= 0 && c.a <= 1, "Bounds");
                Check(c.r <= previous.r + .000002f && c.a <= previous.a + .000002f, "Monotone");
                if (smooth == 0)
                {
                    float u = t <= colorTime ? .5f*Mathf.InverseLerp(.4f, colorTime, t) :
                        .5f+.5f*Mathf.InverseLerp(colorTime, .95f, t);
                    Near(c.r, Neutral(1-u, mode, space));
                }
                previous = c;
            }
            g.Mode = WhimTexGradientMode.Fixed;
            for (int i = 0; i <= 200; i++) Near(g.Evaluate(i/200f).r, i/200f <= .4f ? 1 : 0, 0);
        }
        g = new WhimTexGradient();
        g.SetMidpoint(false, 0, .72700745f);
        float m = g.GetMidpoint(false, 0), e = .0001f, center = g.Evaluate(m).r;
        Near((center-g.Evaluate(m-e).r)/e, (g.Evaluate(m+e).r-center)/e, .005f);
        Near(g.Evaluate(.00001f).r, 0, .000001f);
        Near(g.Evaluate(.99999f).r, 1, .000001f);
        foreach (var wrap in new[] {WhimTexGradientWrapMode.Repeat, WhimTexGradientWrapMode.Mirror})
        {
            g.WrapMode = wrap;
            Near(g.Evaluate(1.25f).r, g.Evaluate(wrap == WhimTexGradientWrapMode.Repeat ? .25f : .75f).r);
        }
        g.WrapMode = WhimTexGradientWrapMode.Clamp;
        Check(g.Clone().Equals(g) && g.Clone().GetHashCode() == g.GetHashCode(), "Clone");
        Check(JsonUtility.FromJson<WhimTexGradient>(JsonUtility.ToJson(g)).Equals(g), "JSON roundtrip");
        var clipboard = typeof(WhimTexGradient).Assembly.GetType("DCFApixels.WhimTex.WhimTexGradientClipboard");
        string json = (string)clipboard.GetMethod("Write", Flags).Invoke(null, new object[] {g});
        Check(!json.Contains("transition"), "Clipboard still writes transition");
        Check(((WhimTexGradient)clipboard.GetMethod("Read", Flags).Invoke(null, new object[] {json})).Equals(g), "Clipboard/API roundtrip");
        string raw = JsonUtility.ToJson(g);
        void RejectClipboard(string input, string reason)
        {
            object[] args = {input, null};
            Check(!(bool)clipboard.GetMethod("TryRead", Flags).Invoke(null, args), reason);
            Check(args[1] == null, "Rejected clipboard has no gradient");
        }
        RejectClipboard(raw, "Raw Unity JSON is not gradient clipboard JSON");
        foreach (var value in new[] {g, new WhimTexGradient()})
        {
            var document = ScriptableObject.CreateInstance<TextureCompositor>();
            try
            {
                document.layers.Add(new GradientLayerBehaviour {gradient = value.Clone()});
                typeof(TextureCompositor).GetMethod("NormalizeModel", Flags).Invoke(document, null);
                string compact = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions {Mode = WhimTexJsonWriteMode.Compact}).Json;
                Check(!compact.Contains("transition"), "Compact document retains retired transition");
                using var restored = WhimTexDocumentJson.Read(compact);
                Check(((GradientLayerBehaviour)restored.Document.layers[0].Behaviour).gradient.Equals(value), "Compact document gradient roundtrip");
            }
            finally { UnityEngine.Object.DestroyImmediate(document); }
        }
        foreach (string value in new[] {"0","1","2","3","4","5","999","null","\"Standard\"","\"Soft\"","\"Soft2\"","\"Soft3\"","\"Rational\"","\"Rounded\""})
        {
            string portable = json.Replace("\"mode\":", "\"transition\":" + value + ",\"mode\":");
            RejectClipboard(portable, "Retired transition rejected: " + value);
        }
        var single = new WhimTexGradient();
        single.SetKeys(new[] {new GradientColorKey(new Color(-2, 4, .3f), .4f)}, new[] {new GradientAlphaKey(.3f, .7f)});
        Near(single.Evaluate(.9f).r, -2); Near(single.Evaluate(.1f).g, 4); Near(single.Evaluate(.5f).a, .3f);
        single.SetKeys(new[] {new GradientColorKey(Color.black, .5f), new GradientColorKey(Color.white, .500002f)},
            new[] {new GradientAlphaKey(0, .5f), new GradientAlphaKey(1, .500002f)});
        single.SetMidpoint(false, 0, .01f);
        for (int i = 0; i <= 20; i++)
        {
            var c = single.Evaluate(.5f + .000002f*i/20);
            Check(c.r >= 0 && c.r <= 1 && c.a >= 0 && c.a <= 1, "Narrow interval finite/bounded");
        }
        var serializer = typeof(WhimTexGradient).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
        using (var container = new WhimTexDocumentContainer())
        {
            var bytes = (byte[])serializer.GetMethod("Serialize", Flags).Invoke(null, new object[] {g, container});
            var readResult = serializer.GetMethod("Deserialize", Flags).Invoke(null, new object[] {bytes, container, typeof(WhimTexGradient), null, false});
            var copy = (WhimTexGradient)readResult.GetType().GetProperty("Model", Flags).GetValue(readResult);
            Check(copy.Equals(g), "Document serializer roundtrip");
        }
        using (var lut = new WhimTexGradientTexture())
        {
            var tex = lut.GetTexture(g, ColorSpace.Gamma); int count = lut.BakeCount;
            Color before = tex.GetPixel(170, 0); var old = g.Clone(); uint revision = g.Revision;
            g.Smoothness = .5f;
            Check(!old.Equals(g) && old.GetHashCode() != g.GetHashCode() && revision != g.Revision, "Identity/invalidation");
            Check(lut.GetTexture(g, ColorSpace.Gamma) == tex && lut.BakeCount == count+1, "Rebake existing LUT");
            Check(!before.Equals(tex.GetPixel(170, 0)), "LUT pixels changed");
            lut.GetTexture(g, ColorSpace.Gamma); Check(lut.BakeCount == count+1, "Stable LUT cache");
        }
        g.Evaluate(.3f);
        long allocated = GC.GetAllocatedBytesForCurrentThread(); float sink = 0;
        for (int i = 0; i < 10000; i++) sink += g.Evaluate(i/10000f).r;
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated && sink > 0, "Hot-path allocation");
        var editor = ScriptableObject.CreateInstance<WhimTexGradientWindow>();
        var previousFocus = EditorWindow.focusedWindow;
        try
        {
            editor.Show();
            editor.CreateGUI();
            Check(typeof(WhimTexGradientWindow).GetField("transition", Flags) == null, "No Transition UI field");
            bool foundTransition = false;
            editor.rootVisualElement.Query<EnumField>().ForEach(f => foundTransition |= f.label == "Transition");
            Check(!foundTransition, "No Transition element");
            var smoothField = (Slider)typeof(WhimTexGradientWindow).GetField("smoothness", Flags).GetValue(editor);
            Undo.IncrementCurrentGroup();
            smoothField.value = 25;
            var edited = (WhimTexGradient)typeof(WhimTexGradientWindow).GetField("gradient", Flags).GetValue(editor);
            Near(edited.Smoothness, .25f);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            edited = (WhimTexGradient)typeof(WhimTexGradientWindow).GetField("gradient", Flags).GetValue(editor);
            Near(edited.Smoothness, 1);
            Undo.PerformRedo();
            edited = (WhimTexGradient)typeof(WhimTexGradientWindow).GetField("gradient", Flags).GetValue(editor);
            Near(edited.Smoothness, .25f);
            edited.Mode = WhimTexGradientMode.Fixed;
            typeof(WhimTexGradientWindow).GetMethod("Refresh", Flags).Invoke(editor, null);
            Check(!smoothField.enabledSelf, "Fixed disables Smoothness");
        }
        finally { Undo.ClearUndo(editor); Undo.IncrementCurrentGroup(); editor.Close(); if (previousFocus != null) previousFocus.Focus(); }
        return;
    }
}

