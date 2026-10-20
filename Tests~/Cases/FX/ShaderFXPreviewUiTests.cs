using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public sealed class ShaderFXPreviewTestWindow : EditorWindow { }

public static class ShaderFXPreviewUiTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static string Key(string runId) => "WhimTex.Tests.FXPreviewUI." + Guid.Parse(runId).ToString("D");

    public static string Start(string runId)
    {
        if (AppDomain.CurrentDomain.GetData(Key(runId)) != null)
            return TestContext.Result("failed", 0, "Duplicate preview test", "Owned state was not replaced").ToJson();
        var state = new object[7];
        AppDomain.CurrentDomain.SetData(Key(runId), state);
        try
        {
            const string code = @"
// @param preview(P) float _Hard = 14.8 [-180 .. 180]
// @param preview(P) float _Soft = 1.02 [0 .. ~4]
// @param preview(C) color _Tint = (1, 0.5, 0, 1)
// @param preview(U) float2 _Offset = (0.1, 0.2)
// @preview(U)
float4 P(float2 uv, float value) { return float4(value, _Hard, uv.y, 1); }
float4 C(float2 uv, float4 value) { return value; }
float4 U(float2 uv) { return float4(uv, 0, 1); }
float4 ApplyFX(float2 uv, float4 color) { return color; }
";
            var assembly = typeof(ShaderFX).Assembly;
            var metadata = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata", true);
            var parameters = (List<ShaderFXParameter>)metadata.GetMethod("Parse", F).Invoke(null, new object[] { code, false, null });
            var effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { null, code, parameters });
            state[1] = effect;
            state[2] = EditorWindow.focusedWindow;
            var window = ScriptableObject.CreateInstance<ShaderFXPreviewTestWindow>();
            state[0] = window;
            window.titleContent = new GUIContent("FX Preview Test");
            assembly.GetType("DCFApixels.WhimTex.WhimTexUI", true).GetMethod("ApplyWindowStyles", F)
                .Invoke(null, new object[] { window.rootVisualElement });
            var viewType = assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView", true);
            var view = (VisualElement)Activator.CreateInstance(viewType, F, null, new object[] { effect }, null);
            window.rootVisualElement.Add(view);
            window.rootVisualElement.Add(new Slider("Native reference", 0, 1) { name = "nativeSliderReference", showInputField = true });
            window.ShowUtility();
            window.position = new Rect(100, 100, 480, 340);
            state[4] = window.rootVisualElement.schedule.Execute(() => Check(state)).StartingIn(1000);
            return Poll(runId);
        }
        catch (Exception error)
        {
            state[3] = TestContext.Result("failed", 0, "Preview UI setup", error.ToString()).ToJson();
            Release(state); return (string)state[3];
        }
    }

    static void Check(object[] state)
    {
        bool pending = false;
        try
        {
            string first = TestContext.Run("FX preview attached UI layout and render", test =>
            {
                var window = (EditorWindow)state[0];
                var root = window.rootVisualElement;
                var previews = root.Query<Image>(className: "whimtex-fx-preview").ToList();
                test.Equal(5, previews.Count, "Two sliders, two right-side previews and one standalone");
                int tracks = 0, fields = 0, standalone = 0;
                foreach (var preview in previews)
                {
                    test.Equal(PickingMode.Ignore, preview.pickingMode, "Preview never intercepts editing");
                    test.True(preview.image is RenderTexture rt && rt.IsCreated(), "Visible preview has a rendered image: " + preview.name + ": " + preview.tooltip);
                    test.True(preview.worldBound.width > 0 && preview.worldBound.height > 0, "Preview layout has area");
                    if (preview.ClassListContains("whimtex-fx-preview--slider"))
                    {
                        tracks++;
                        test.True(preview.parent.ClassListContains(Slider.trackerUssClassName), "Embedded in the real track");
                        var slider = preview.GetFirstAncestorOfType<Slider>();
                        var thumb = slider.Q(className: Slider.draggerUssClassName);
                        test.True(thumb != null, "Native thumb retained");
                        var marker = thumb.Q(className: "whimtex-fx-preview-thumb");
                        test.True(marker != null, "Triangle decorates the native thumb");
                        test.Equal(PickingMode.Ignore, marker.pickingMode, "Triangle leaves native input untouched");
                        test.Near(20, slider.layout.height, .1, "Hard and soft sliders have the same fixed row height");
                        test.Near(10, preview.parent.layout.height, .1, "Texture cannot expand the track");
                        test.Near(12, marker.layout.width, .1, "Fixed triangle width");
                        test.Near(10, marker.layout.height, .1, "Fixed triangle height");
                        test.Near(preview.parent.worldBound.yMax, marker.worldBound.center.y, .6,
                            "Triangle is half inside and half outside; slider=" + slider.layout + "; track=" + preview.parent.layout +
                            "; thumb=" + thumb.layout + "; thumb parent=" + thumb.parent.layout + "; top=" + thumb.resolvedStyle.top +
                            "; position=" + thumb.resolvedStyle.position + "; translate=" + thumb.resolvedStyle.translate);
                    }
                    else if (preview.ClassListContains("whimtex-fx-preview--field"))
                    {
                        fields++;
                        test.True(preview.parent.ClassListContains("whimtex-fx-preview-row"), "Right-side fallback row");
                        test.True(preview.worldBound.xMin >= preview.parent[0].worldBound.xMax, "Fallback is right of the control");
                    }
                    else standalone++;
                }
                test.Equal(2, tracks, "Hard and soft Slider both embed");
                test.Equal(2, fields, "Other fields keep their native UI");
                test.Equal(1, standalone, "Standalone preview occupies its own row");
                CheckSliderColumns(test, root);
                var type = previews[0].GetType();
                var cache = type.GetField("cache", F).GetValue(previews[0]);
                var cacheType = cache.GetType();
                int Count(string name) => (int)cacheType.GetProperty(name, F).GetValue(cache);
                foreach (var preview in previews)
                    test.True(ReferenceEquals(cache, type.GetField("cache", F).GetValue(preview)), "One cache per FX, not per control");
                test.Equal(4, Count("ProgramCount"), "UV-only callback shares its compiled program across controls");
                test.Equal(1, Count("BindingCount"), "All five surfaces share one parameter preparation");
                var update = type.GetMethod("UpdatePreview", F);
                var drawCount = type.GetProperty("DrawCount", F);
                int fingerprints = Count("FingerprintCount");
                var counts = previews.ConvertAll(preview => (int)drawCount.GetValue(preview));
                for (int repeat = 0; repeat < 20; repeat++)
                    for (int i = 0; i < previews.Count; i++) update.Invoke(previews[i], null);
                test.True(Count("FingerprintCount") - fingerprints <= 1, "100 polling calls perform at most one fingerprint in an Editor update");
                for (int i = 0; i < previews.Count; i++)
                    test.Equal(counts[i], (int)drawCount.GetValue(previews[i]), "Unchanged preview does not redraw");
                state[5] = cache;
            });
            var result = JsonUtility.FromJson<TestResult>(first);
            if (result.status != "passed") { state[3] = first; return; }
            state[6] = result.checks;
            var parameters = (IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(state[1]);
            parameters[0].floatValue = .75f;
            parameters[1].floatValue = 12345.678f;
            var window = (EditorWindow)state[0];
            window.position = new Rect(window.position.x, window.position.y, 340, 340);
            state[4] = window.rootVisualElement.schedule.Execute(() => CheckUpdated(state)).StartingIn(400);
            pending = true;
        }
        finally { if (!pending) Release(state); }
    }

    static void CheckUpdated(object[] state)
    {
        try
        {
            string second = TestContext.Run("FX preview update, narrow layout and resource release", test =>
            {
                var root = ((EditorWindow)state[0]).rootVisualElement;
                var previews = root.Query<Image>(className: "whimtex-fx-preview").ToList();
                var type = previews[0].GetType();
                var cache = state[5];
                CheckSliderColumns(test, root);
                test.Equal(4, (int)cache.GetType().GetProperty("ProgramCount", F).GetValue(cache), "Value/width edits do not compile shaders");
                test.Equal(2, (int)cache.GetType().GetProperty("BindingCount", F).GetValue(cache), "One shared rebind after the value edit");
                foreach (var preview in previews)
                {
                    test.True((int)type.GetProperty("DrawCount", F).GetValue(preview) >= 2, "Changed value redraws each surface");
                    if (!preview.ClassListContains("whimtex-fx-preview--slider")) continue;
                    var slider = preview.GetFirstAncestorOfType<Slider>();
                    var marker = slider.Q(className: "whimtex-fx-preview-thumb");
                    test.Near(20, slider.layout.height, .1, "Narrow Slider keeps its row height");
                    test.Near(10, preview.parent.layout.height, .1, "Narrow track keeps its height");
                    test.Near(12, marker.layout.width, .1, "Narrow marker keeps its width");
                    test.Near(preview.parent.worldBound.yMax, marker.worldBound.center.y, .6, "Narrow marker still straddles the edge");
                    CheckPixel(test, preview);
                }
                root.Clear();
                test.Equal(0, (int)cache.GetType().GetProperty("ProgramCount", F).GetValue(cache), "Last detach releases shared shader programs");
                foreach (var preview in previews) test.True(preview.image == null, "Detach releases each surface image");
            });
            var result = JsonUtility.FromJson<TestResult>(second);
            result.checks += (int)state[6]; state[3] = result.ToJson();
        }
        finally { Release(state); }
    }

    static void CheckSliderColumns(TestContext test, VisualElement root)
    {
        var sliders = root.Query<Slider>(className: "whimtex-fx-preview-slider").ToList();
        test.Equal(2, sliders.Count, "Compare hard and soft preview slider columns");
        Rect? firstTrack = null, firstNumber = null;
        string firstGeometry = null;
        foreach (var slider in sliders)
        {
            var track = slider.Q(className: Slider.trackerUssClassName).worldBound;
            VisualElement number = slider.showInputField ? slider.Q<TextField>() : slider.parent.Q<FloatField>(className: "whimtex-soft-range-number");
            test.True(number != null, "Numeric field exists for both range types");
            var nativeNumber = root.Q<Slider>("nativeSliderReference").Q<TextField>();
            var nativeStyle = nativeNumber.resolvedStyle;
            string nativeGeometry = " width=" + nativeStyle.width + " height=" + nativeStyle.height +
                " minWidth=" + nativeStyle.minWidth + " maxWidth=" + nativeStyle.maxWidth + " basis=" + nativeStyle.flexBasis +
                " grow=" + nativeStyle.flexGrow + " shrink=" + nativeStyle.flexShrink +
                " margins=" + nativeStyle.marginLeft + "," + nativeStyle.marginRight + "," + nativeStyle.marginTop + "," + nativeStyle.marginBottom;
            test.Near(nativeNumber.layout.width, number.layout.width, .1, "Numeric width matches an unmodified native Slider:" + nativeGeometry);
            test.Near(nativeNumber.layout.height, number.layout.height, .1, "Numeric height matches an unmodified native Slider");
            test.Near(nativeNumber.resolvedStyle.marginLeft, number.resolvedStyle.marginLeft, .1, "Native numeric left spacing");
            test.Near(nativeNumber.resolvedStyle.marginRight, number.resolvedStyle.marginRight, .1, "Native numeric right spacing");
            var numberRect = number.Q(className: "unity-base-field__input").worldBound;
            var drag = slider.Q(className: "unity-base-slider__drag-container");
            string geometry = "; slider=" + slider.worldBound + "; slider margins=" + slider.resolvedStyle.marginLeft + "," + slider.resolvedStyle.marginRight +
                "; number=" + number.worldBound + "; number margins=" + number.resolvedStyle.marginLeft + "," + number.resolvedStyle.marginRight +
                "; drag=" + drag.worldBound + "; drag margins=" + drag.resolvedStyle.marginLeft + "," + drag.resolvedStyle.marginRight;
            if (firstTrack.HasValue)
            {
                string detail = "; hard track=" + firstTrack + "; soft track=" + track + "; hard number=" + firstNumber + "; soft number=" + numberRect + "; hard geometry=" + firstGeometry + "; soft geometry=" + geometry;
                test.Near(firstTrack.Value.xMin, track.xMin, .1, "Track starts align" + detail);
                test.Near(firstTrack.Value.xMax, track.xMax, .1, "Track ends align" + detail);
                test.Near(firstNumber.Value.xMin, numberRect.xMin, .1, "Numeric inputs start in the same column" + detail);
                test.Near(firstNumber.Value.xMax, numberRect.xMax, .1, "Numeric inputs end in the same column" + detail);
            }
            else { firstTrack = track; firstNumber = numberRect; firstGeometry = geometry; }
        }
    }

    static void CheckPixel(TestContext test, Image preview)
    {
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var linear = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var pixels = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            linear.Create(); GL.sRGBWrite = false;
            Graphics.Blit(preview.image, linear);
            RenderTexture.active = linear; pixels.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); pixels.Apply();
            test.Near(.75, pixels.GetPixel(0, 0).g, .012, "Shared binding refresh reaches both independently ranged slider shaders");
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            linear.Release(); UnityEngine.Object.DestroyImmediate(linear); UnityEngine.Object.DestroyImmediate(pixels);
        }
    }

    public static string Poll(string runId)
    {
        var state = AppDomain.CurrentDomain.GetData(Key(runId)) as object[];
        return state == null ? TestContext.Result("failed", 0, "Missing test state", "Domain reload or unknown run").ToJson()
            : (string)state[3] ?? TestContext.Result("running", 0, "Waiting for preview UI layout").ToJson();
    }
    public static string Cleanup(string runId)
    {
        var state = AppDomain.CurrentDomain.GetData(Key(runId)) as object[];
        if (state == null) return TestContext.Result("failed", 0, "Missing cleanup state", "Cannot prove ownership").ToJson();
        Release(state); AppDomain.CurrentDomain.SetData(Key(runId), null);
        return TestContext.Result("passed", 1, "Owned preview UI released").ToJson();
    }
    static void Release(object[] state)
    {
        (state[4] as IVisualElementScheduledItem)?.Pause(); state[4] = null;
        if (state[0] is EditorWindow window && window != null)
        { window.Close(); if (window != null) UnityEngine.Object.DestroyImmediate(window); }
        state[0] = null;
        if (state[1] is ShaderFX effect && effect != null) UnityEngine.Object.DestroyImmediate(effect);
        state[1] = null;
        if (state[2] is EditorWindow previousFocus && previousFocus != null) previousFocus.Focus();
        state[2] = null;
    }
}
