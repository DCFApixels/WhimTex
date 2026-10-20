using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public sealed class ShaderFXIntervalTestWindow : EditorWindow { }

public static class ShaderFXIntervalTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    const string Code = @"
// @param preview(P) float2 _Range = (0.2, 0.8) [0 .. 1]
// @param float2 _Offset = (3, -2)
float4 P(float2 uv, float2 value) { return float4(value.x, value.y, uv.x, 1); }
float4 ApplyFX(float2 uv, float4 color) { return color; }
";
    static Type T(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static List<ShaderFXParameter> Parse(string source) => (List<ShaderFXParameter>)T("ShaderFXMetadata").GetMethod("Parse", F).Invoke(null, new object[] { source, false, null });
    static string Key(string id) => "WhimTex.Tests.Interval." + Guid.Parse(id).ToString("D");
    static ShaderFX Create(List<ShaderFXParameter> values) => (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
        new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null).Invoke(null, new object[] { null, Code, values });
    static IReadOnlyList<ShaderFXParameter> Values(object effect) => (IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(effect);

    static void Metadata(TestContext t)
    {
        var values = Parse(Code);
        var p = values[0];
        t.Equal(ShaderFXParameterType.Vector2, p.type, "Interval is stored as float2");
        t.True(p.hasMinimum && p.hasMaximum && !p.softMinimum && !p.softMaximum, "Hard interval limits");
        t.Equal(new Vector4(.2f, .8f, 0, 0), p.vectorValue, "Explicit endpoints");
        t.Equal(new Vector4(-2, 3, 0, 0), Parse("// @param float2 _R [-2 .. 3]")[0].vectorValue, "Default is the whole interval");
        t.Equal(new Vector4(3, -2, 0, 0), values[1].vectorValue, "Unbounded vector keeps component order");
        foreach (string declaration in new[] { "float2 _R = (.8, .2) [0 .. 1]", "float2 _R = (-1, .5) [0 .. 1]",
            "float2 _R = (0, 2) [0 .. 1]", "float2 _R [0 .. ~1]", "float2 _R [ .. 1]", "float2 _R [1 .. 1]", "point _R [0 .. 1]", "float3 _R [0 .. 1]" })
        {
            bool rejected = false;
            try { Parse("// @param " + declaration); }
            catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            t.True(rejected, "Reject invalid interval: " + declaration);
        }
        var saved = Parse(Code); saved[0].vectorValue = new Vector4(-2, 5, 0, 0);
        T("ShaderFXMetadata").GetMethod("PreserveValues", F).Invoke(null, new object[] { values, saved });
        t.Equal(new Vector4(0, 1, 0, 0), values[0].vectorValue, "Changed hard limits clamp retained endpoints");
        var api = typeof(WhimTexApi).GetMethod("ReadFxParameterValue", F);
        var tokenType = api.GetParameters()[1].ParameterType;
        var parseJson = tokenType.GetMethod("Parse", new[] { typeof(string) });
        foreach (string json in new[] { "[0.3,0.7]", "[0.9,0.1]", "[-1,0.5]", "[0.5,2]" })
        {
            bool accepted = true;
            try { api.Invoke(null, new object[] { p, parseJson.Invoke(null, new object[] { json }), null }); }
            catch (TargetInvocationException) { accepted = false; }
            t.Equal(json == "[0.3,0.7]", accepted, "API validates ordered bounded endpoints: " + json);
        }
        ShaderFX effect = null;
        try
        {
            effect = Create(Parse(Code));
            Values(effect)[0].vectorValue = new Vector4(.3f, .7f, 0, 0);
            string source = (string)T("ShaderFXPresetWriter").GetMethod("BuildSource", F).Invoke(null, new object[] { effect, "Tests/Interval" });
            var roundtrip = Parse(source)[0];
            t.Equal(new Vector4(.3f, .7f, 0, 0), roundtrip.vectorValue, "Preset exports current endpoints");
            t.True(roundtrip.hasMinimum && roundtrip.hasMaximum && roundtrip.controls[0].previewFunction == "P", "Preset exports limits and preview");
        }
        finally { if (effect != null) UnityEngine.Object.DestroyImmediate(effect); }
    }

    public static string Start(string runId)
    {
        if (AppDomain.CurrentDomain.GetData(Key(runId)) != null) return TestContext.Result("failed", 0, "Duplicate interval test").ToJson();
        var state = new object[7]; AppDomain.CurrentDomain.SetData(Key(runId), state);
        state[3] = TestContext.Run("Interval metadata and API", Metadata);
        var result = JsonUtility.FromJson<TestResult>((string)state[3]);
        if (result.status != "passed") return (string)state[3];
        state[6] = result.checks; state[3] = null;
        try
        {
            state[2] = EditorWindow.focusedWindow;
            var effect = Create(Parse(Code)); state[1] = effect;
            var window = ScriptableObject.CreateInstance<ShaderFXIntervalTestWindow>(); state[0] = window;
            window.titleContent = new GUIContent("FX Interval Test");
            var view = (VisualElement)Activator.CreateInstance(T("ShaderFXParameterView"), F, null, new object[] { effect }, null);
            window.rootVisualElement.Add(view); state[5] = view;
            window.rootVisualElement.Add(new Slider("Native reference", 0, 1) { name = "nativeSliderReference", showInputField = true });
            window.ShowUtility(); window.position = new Rect(100, 100, 540, 180);
            state[4] = window.rootVisualElement.schedule.Execute(() => Check(state, false)).StartingIn(800);
        }
        catch (Exception e) { state[3] = TestContext.Result("failed", 0, "Interval UI setup", e.ToString()).ToJson(); Release(state); }
        return Poll(runId);
    }

    static void Check(object[] state, bool narrow)
    {
        string json = TestContext.Run("Native MinMaxSlider and preview", t =>
        {
            var root = (VisualElement)state[5];
            var slider = root.Q<MinMaxSlider>();
            t.True(slider != null, "Bounded float2 uses the native MinMaxSlider");
            t.True(root.Q<Vector2Field>() != null, "Unbounded float2 keeps Vector2Field");
            var field = slider.GetFirstAncestorOfType<BaseField<Vector2>>();
            var numbers = field.Query<FloatField>().ToList();
            t.Equal(2, numbers.Count, "Separate X/Y numeric fields");
            var track = slider.Q(className: MinMaxSlider.trackerUssClassName);
            var image = track.Q<Image>();
            t.True(image != null && image.image is RenderTexture && !image.ClassListContains("whimtex-fx-preview--error"), "Preview renders inside native track: " + image?.tooltip);
            t.Equal(PickingMode.Ignore, image.pickingMode, "Preview does not intercept input");
            t.Near(20, slider.layout.height, .1, "Fixed row height");
            t.Near(10, track.layout.height, .1, "Fixed track height");
            t.True(track.worldBound.width > 28, "Track remains usable at this window width");
            var thumbs = slider.Query<VisualElement>(className: "whimtex-fx-preview-thumb").ToList();
            t.Equal(2, thumbs.Count, "Two triangle markers on native handles");
            for (int i = 0; i < 2; i++)
            {
                t.Near(8, thumbs[i].layout.width, .1, "Fixed half-triangle width");
                t.Near(12, thumbs[i].layout.height, .1, "Fixed enlarged half-triangle height");
                t.True(thumbs[i].ClassListContains("whimtex-fx-preview-thumb--half"), "Interval uses half triangles only");
                t.Near(track.worldBound.yMax, thumbs[i].worldBound.center.y, .6, "Half inside/outside the track");
                float endpoint = Mathf.Lerp(track.worldBound.xMin, track.worldBound.xMax, slider.value[i]);
                t.Near(endpoint, i == 0 ? thumbs[i].worldBound.xMax : thumbs[i].worldBound.xMin, 1.1, "Vertical edge follows endpoint, half extends outside the interval");
                t.True(thumbs[i].parent.worldBound.Contains(thumbs[i].worldBound.center), "Visible half matches native hit area");
            }
            var nativeNumber = ((EditorWindow)state[0]).rootVisualElement.Q<Slider>("nativeSliderReference").Q<TextField>();
            t.Near(nativeNumber.layout.width, numbers[0].layout.width, .1, "Lower field uses the native Slider numeric width");
            t.Near(nativeNumber.layout.width, numbers[1].layout.width, .1, "Upper field uses the native Slider numeric width");
            t.True(string.IsNullOrEmpty(numbers[0].label) && string.IsNullOrEmpty(numbers[1].label), "Endpoint numeric fields have no labels");
            CheckPixel(t, image, narrow ? .4f : .2f, narrow ? .6f : .8f);
            if (!narrow)
            {
                Undo.IncrementCurrentGroup();
                slider.value = new Vector2(.3f, .7f); Undo.FlushUndoRecordObjects();
                t.Equal(new Vector4(.3f, .7f, 0, 0), Values(state[1])[0].vectorValue, "Native slider updates FX pair");
                Undo.PerformUndo(); t.Equal(new Vector4(.2f, .8f, 0, 0), Values(state[1])[0].vectorValue, "Undo restores both endpoints");
                Undo.PerformRedo(); t.Equal(new Vector4(.3f, .7f, 0, 0), Values(state[1])[0].vectorValue, "Redo restores both endpoints");
                // Undo can rebuild the view; continue through its current controls, not detached ones.
                root.GetType().GetMethod("Refresh", F).Invoke(root, null);
                slider = root.Q<MinMaxSlider>();
                field = slider.GetFirstAncestorOfType<BaseField<Vector2>>();
                numbers = field.Query<FloatField>().ToList();
                numbers[0].value = .9f; t.Near(.7, Values(state[1])[0].vectorValue.x, .0001, "X cannot cross Y");
                numbers[1].value = -1; t.Near(.7, Values(state[1])[0].vectorValue.y, .0001, "Y cannot cross X");
                string geometry = " before slider=" + slider.value + " field=" + field.value + " attached=" + (slider.panel != null);
                slider.value = new Vector2(.4f, .6f);
                t.Equal(new Vector4(.4f, .6f, 0, 0), Values(state[1])[0].vectorValue, "Editing after Undo/Redo uses current controls" + geometry + " after slider=" + slider.value + " field=" + field.value);
                numbers[0].value = float.NaN; t.Near(.4, Values(state[1])[0].vectorValue.x, .0001, "Invalid numeric input rejected");
            }
        });
        var result = JsonUtility.FromJson<TestResult>(json); result.checks += (int)state[6];
        if (result.status != "passed") { state[3] = result.ToJson(); Release(state); return; }
        state[6] = result.checks;
        CheckLimits(state, narrow, 0);
    }

    static readonly Vector2[] EdgeValues = { new(0, .6f), new(.4f, 1), new(0, 1), new(0, 0), new(1, 1) };

    static void CheckLimits(object[] state, bool narrow, int index)
    {
        var root = (VisualElement)state[5];
        var slider = root.Q<MinMaxSlider>();
        slider.SetValueWithoutNotify(EdgeValues[index]);
        state[4] = root.schedule.Execute(() =>
        {
            string json = TestContext.Run("MinMax preview endpoint clipping", t =>
            {
                var thumbs = slider.Query<VisualElement>(className: "whimtex-fx-preview-thumb").ToList();
                t.Equal(2, thumbs.Count, "Both endpoint markers exist at " + slider.value);
                foreach (var thumb in thumbs)
                {
                    t.True(thumb.visible && thumb.resolvedStyle.display != DisplayStyle.None, "Endpoint marker is visible");
                    foreach (float position in new[] { .1f, .5f, .9f })
                    {
                        var point = new Vector2(Mathf.Lerp(thumb.worldBound.xMin, thumb.worldBound.xMax, position), thumb.worldBound.center.y);
                        var picked = thumb.panel.Pick(point);
                        t.True(picked != null && (picked == thumb.parent || thumb.parent.Contains(picked)),
                            "Endpoint " + slider.value + " is clipped or covered at " + point +
                            "; picked=" + picked?.name + " / " + (picked == null ? "none" : string.Join(" ", picked.GetClasses())));
                    }
                }
            });
            var result = JsonUtility.FromJson<TestResult>(json); result.checks += (int)state[6];
            if (result.status != "passed") { state[3] = result.ToJson(); Release(state); return; }
            state[6] = result.checks;
            if (index + 1 < EdgeValues.Length) { CheckLimits(state, narrow, index + 1); return; }
            slider.SetValueWithoutNotify(new Vector2(.4f, .6f));
            if (narrow) { state[3] = result.ToJson(); Release(state); return; }
            CheckNarrow(state);
        }).StartingIn(100);
    }

    static void CheckNarrow(object[] state)
    {
        var window = (EditorWindow)state[0]; window.position = new Rect(100, 100, 360, 180);
        state[4] = window.rootVisualElement.schedule.Execute(() => Check(state, true)).StartingIn(500);
    }

    static void CheckPixel(TestContext t, Image image, float x, float y)
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var linear = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            linear.Create(); GL.sRGBWrite = false; Graphics.Blit(image.image, linear); RenderTexture.active = linear;
            pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); pixel.Apply();
            t.Near(x, pixel.GetPixel(0, 0).r, .012, "Preview receives actual X, not candidate UV");
            t.Near(y, pixel.GetPixel(0, 0).g, .012, "Preview receives actual Y");
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; linear.Release(); UnityEngine.Object.DestroyImmediate(linear); UnityEngine.Object.DestroyImmediate(pixel); }
    }
    public static string Poll(string id) => AppDomain.CurrentDomain.GetData(Key(id)) is object[] state
        ? (string)state[3] ?? TestContext.Result("running", 0, "Waiting for interval UI").ToJson() : TestContext.Result("failed", 0, "Missing interval state").ToJson();
    public static string Cleanup(string id)
    {
        if (AppDomain.CurrentDomain.GetData(Key(id)) is not object[] state) return TestContext.Result("failed", 0, "Missing owned state").ToJson();
        Release(state); AppDomain.CurrentDomain.SetData(Key(id), null); return TestContext.Result("passed", 1, "Interval fixture released").ToJson();
    }
    static void Release(object[] state)
    {
        (state[4] as IVisualElementScheduledItem)?.Pause(); state[4] = null;
        if (state[0] is EditorWindow window && window != null) { window.Close(); if (window != null) UnityEngine.Object.DestroyImmediate(window); } state[0] = null;
        if (state[1] is ShaderFX effect && effect != null) { Undo.ClearUndo(effect); UnityEngine.Object.DestroyImmediate(effect); } state[1] = null;
        if (state[2] is EditorWindow focus && focus != null) focus.Focus(); state[2] = null;
    }
}
