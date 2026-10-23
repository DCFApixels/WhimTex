using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public sealed class ShaderFXPreviewUpdatesWindow : EditorWindow { }

public static class ShaderFXPreviewUpdatesTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type T(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static string Key(string id) => "WhimTex.Tests.PreviewUpdates." + Guid.Parse(id).ToString("D");
    static IReadOnlyList<ShaderFXParameter> Values(object effect) => (IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(effect);
    static void Notify(object effect) => typeof(ShaderFX).GetMethod("NotifyValuesChanged", F).Invoke(effect, null);
    static int Count(object target, string property) => (int)target.GetType().GetProperty(property, F).GetValue(target);
    static List<Image> Images(object[] s) => (List<Image>)s[9];
    static int[] Draws(object[] s) => Images(s).ConvertAll(image => Count(image, "DrawCount")).ToArray();

    public static string Start(string id)
    {
        if (AppDomain.CurrentDomain.GetData(Key(id)) != null) return TestContext.Result("failed", 0, "Duplicate preview update fixture").ToJson();
        var s = new object[13]; AppDomain.CurrentDomain.SetData(Key(id), s); s[7] = 0;
        try
        {
            const string code = @"
// @param preview(P) float _Amount = 0.2 [0 .. 1]
// @param preview(V, _Range, _Amount) float2 _Range = (0.1, 0.8) [0 .. 1]
// @param preview(C, _Tint, _Amount) color _Tint = (1, 0, 0, 1)
// @preview(U, _Amount)
float4 P(float2 uv, float value) { return float4(value, _Amount, 0, 1); }
float4 V(float2 uv, float2 value, float amount) { return float4(value.x, amount, value.y, 1); }
float4 C(float2 uv, float4 value, float amount) { return float4(value.r, amount, 0, 1); }
float4 U(float2 uv, float amount) { return float4(uv.x, amount, 0, 1); }
float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return color; }
";
            var values = (List<ShaderFXParameter>)T("ShaderFXMetadata").GetMethod("Parse", F).Invoke(null, new object[] { code, false, null });
            var effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null).Invoke(null, new object[] { null, code, values });
            s[1] = effect; s[2] = EditorWindow.focusedWindow;
            var window = ScriptableObject.CreateInstance<ShaderFXPreviewUpdatesWindow>(); s[0] = window;
            window.titleContent = new GUIContent("FX Preview Updates");
            window.rootVisualElement.Add((VisualElement)Activator.CreateInstance(T("ShaderFXParameterView"), F, null, new object[] { effect }, null));
            window.ShowUtility(); window.position = new Rect(100, 100, 480, 260);
            s[4] = window.rootVisualElement.schedule.Execute(() => Warm(s)).StartingIn(800);
        }
        catch (Exception e) { s[3] = TestContext.Result("failed", 0, "Preview update setup", e.ToString()).ToJson(); Release(s); }
        return Poll(id);
    }
    static bool Check(object[] s, string message, Action<TestContext> body)
    {
        var r = JsonUtility.FromJson<TestResult>(TestContext.Run(message, body));
        r.checks += (int)s[7]; s[7] = r.checks;
        if (r.status == "passed") return true;
        s[3] = r.ToJson(); Release(s); return false;
    }
    static void Polling(object[] s, bool resume)
    {
        foreach (var image in Images(s))
        {
            var item = (IVisualElementScheduledItem)image.GetType().GetField("update", F).GetValue(image);
            if (resume) item.Resume(); else item.Pause();
        }
    }
    static void Warm(object[] s)
    {
        if (!Check(s, "Warm preview and pause fallback", t =>
        {
            s[9] = ((EditorWindow)s[0]).rootVisualElement.Query<Image>(className: "whimtex-fx-preview").ToList();
            t.Equal(4, Images(s).Count, "Scalar, interval, compact and standalone previews");
            foreach (var image in Images(s)) t.True(image.image is RenderTexture, "Visible preview initialized");
            s[8] = Images(s)[0].GetType().GetField("cache", F).GetValue(Images(s)[0]);
            Polling(s, false); s[10] = Draws(s); s[11] = Count(s[8], "BindingCount"); s[12] = Count(s[8], "FingerprintCount");
            Undo.IncrementCurrentGroup(); Undo.RecordObject((ShaderFX)s[1], "Owned preview event edit");
            for (int i = 0; i < 32; i++) { Values(s[1])[0].floatValue = .75f * (i + 1) / 32; Notify(s[1]); }
            Undo.FlushUndoRecordObjects();
            CompareDraws(t, s, 0, "Events enqueue rather than drawing in the value callback");
            t.Equal((int)s[12], Count(s[8], "FingerprintCount"), "Burst does not fingerprint every value change");
        })) return;
        s[6] = 1; s[5] = (EditorApplication.CallbackFunction)(() => Frame(s));
        EditorApplication.update += (EditorApplication.CallbackFunction)s[5];
    }
    static void CompareDraws(TestContext t, object[] s, int delta, string message)
    {
        var before = (int[])s[10]; var after = Draws(s);
        for (int i = 0; i < after.Length; i++) t.Equal(before[i] + delta, after[i], message + " / " + i);
    }
    static void Frame(object[] s)
    {
        int phase = (int)s[6]; if (phase == 6) return;
        if (!Check(s, "Preview update frame " + phase, t =>
        {
            switch (phase)
            {
                case 1:
                    CompareDraws(t, s, 1, "32 changes produce one update on the next Editor tick with fallback paused");
                    t.Equal((int)s[11] + 1, Count(s[8], "BindingCount"), "All previews share one rebind");
                    t.Equal((int)s[12] + 1, Count(s[8], "FingerprintCount"), "One fresh fingerprint for the burst");
                    Pixel(t, Images(s), .75f);
                    var counts = Draws(s);
                    foreach (var image in Images(s)) for (int i = 0; i < 10; i++) image.GetType().GetMethod("UpdatePreview", F).Invoke(image, null);
                    for (int i = 0; i < counts.Length; i++) t.Equal(counts[i], Count(Images(s)[i], "DrawCount"), "Polling cannot redraw again in the same Editor tick");
                    s[10] = Draws(s); Undo.PerformUndo();
                    break;
                case 2:
                    CompareDraws(t, s, 1, "Undo updates without the periodic poll"); Pixel(t, Images(s), .2f);
                    s[10] = Draws(s); Undo.PerformRedo();
                    break;
                case 3:
                    CompareDraws(t, s, 1, "Redo updates without the periodic poll"); Pixel(t, Images(s), .75f);
                    s[10] = Draws(s); Notify(s[1]);
                    break;
                case 4:
                    CompareDraws(t, s, 0, "Unchanged notification does not redraw");
                    Values(s[1])[0].floatValue = .6f;
                    break;
                case 5:
                    CompareDraws(t, s, 0, "Direct mutation without notification waits for fallback");
                    Polling(s, true);
                    s[4] = ((EditorWindow)s[0]).rootVisualElement.schedule.Execute(() => Fallback(s)).StartingIn(250);
                    break;
                case 7:
                    CompareDraws(t, s, 0, "Pending update does not redraw detached surfaces");
                    t.Equal(0, Count(s[8], "ProgramCount"), "Last detach releases shared programs");
                    foreach (var image in Images(s)) t.True(image.image == null, "Detached image released");
                    break;
            }
        })) return;
        if (phase == 7)
        { s[3] = TestContext.Result("passed", (int)s[7], "Event-driven previews, coalescing, Undo/Redo, fallback and detach").ToJson(); Release(s); }
        else s[6] = phase + 1;
    }
    static void Fallback(object[] s)
    {
        if (!Check(s, "Periodic fallback and queued detach", t =>
        {
            CompareDraws(t, s, 1, "Fallback catches direct edits once, then suppresses unchanged redraws");
            Pixel(t, Images(s), .6f); s[10] = Draws(s);
            Values(s[1])[0].floatValue = .9f; Notify(s[1]);
            ((EditorWindow)s[0]).rootVisualElement.Clear();
        })) return;
        s[6] = 7;
    }
    static void Pixel(TestContext t, List<Image> images, float expected)
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            target.Create(); GL.sRGBWrite = false;
            foreach (var image in images)
            {
                Graphics.Blit(image.image, target); RenderTexture.active = target;
                pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); pixel.Apply();
                t.Near(expected, pixel.GetPixel(0, 0).g, .012, "Latest uniform reached each preview");
            }
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixel); }
    }
    public static string Poll(string id) => AppDomain.CurrentDomain.GetData(Key(id)) is object[] s
        ? (string)s[3] ?? TestContext.Result("running", 0, "Waiting for Editor preview update").ToJson() : TestContext.Result("failed", 0, "Missing owned state").ToJson();
    public static string Cleanup(string id)
    {
        if (AppDomain.CurrentDomain.GetData(Key(id)) is not object[] s) return TestContext.Result("failed", 0, "Missing owned state").ToJson();
        Release(s); AppDomain.CurrentDomain.SetData(Key(id), null); return TestContext.Result("passed", 1, "Preview update fixture released").ToJson();
    }
    static void Release(object[] s)
    {
        var errors = new System.Collections.Generic.List<Exception>();
        void ReleaseOwned(Action action)
        {
            try { action(); }
            catch (Exception error) { errors.Add(error); }
        }
        ReleaseOwned(() =>
        {
            (s[4] as IVisualElementScheduledItem)?.Pause(); s[4] = null;
        });
        ReleaseOwned(() =>
        {
            if (s[5] is EditorApplication.CallbackFunction tick) EditorApplication.update -= tick; s[5] = null;
        });
        ReleaseOwned(() =>
        {
            if (s[0] is EditorWindow window && window != null)
            {
                try { if (window.rootVisualElement.panel != null) window.Close(); }
                finally { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
            }
            s[0] = null;
        });
        ReleaseOwned(() =>
        {
            if (s[1] is ShaderFX effect && effect != null)
            {
                try { Undo.ClearUndo(effect); }
                finally { UnityEngine.Object.DestroyImmediate(effect); }
            }
            s[1] = null;
        });
        ReleaseOwned(() =>
        {
            if (s[2] is EditorWindow focus && focus != null) focus.Focus(); s[2] = null;
        });
        if (errors.Count != 0)
        {
            var failure = new AggregateException("Owned FX fixture cleanup unfinished; retry cleanup before another run", errors);
            s[3] = TestContext.Result("failed", 0, "Cleanup requires recovery; prior result: " + s[3], failure.ToString()).ToJson();
            throw failure;
        }
    }
}
