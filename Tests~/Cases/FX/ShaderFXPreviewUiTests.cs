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
        var state = new object[9];
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
float4 U(float2 uv) { float2 pixel = floor(uv * _CanvasSize.xy); return float4(fmod(pixel.x + pixel.y, 2), uv.y, 0, 1); }
float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return color; }
";
            var assembly = typeof(ShaderFX).Assembly;
            var document = ScriptableObject.CreateInstance<WhimTexDocument>();
            state[7] = document; document.hideFlags = HideFlags.HideAndDontSave;
            document.width = 1024; document.height = 512;
            var metadata = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata", true);
            var parameters = (List<ShaderFXParameter>)metadata.GetMethod("Parse", F).Invoke(null, new object[] { code, false, null });
            var effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { document, code, parameters });
            state[1] = effect;
            state[2] = EditorWindow.focusedWindow;
            var window = ScriptableObject.CreateInstance<ShaderFXPreviewTestWindow>();
            state[0] = window;
            window.titleContent = new GUIContent("FX Preview Test");
            assembly.GetType("DCFApixels.WhimTex.WhimTexUI", true).GetMethod("ApplyWindowStyles", F)
                .Invoke(null, new object[] { window.rootVisualElement });
            var viewType = assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView", true);
            var view = (VisualElement)Activator.CreateInstance(viewType, F, null, new object[] { effect, false, document, null, -1 }, null);
            window.rootVisualElement.Add(view);
            window.rootVisualElement.Add(new Slider("Native reference", 0, 1) { name = "nativeSliderReference", showInputField = true });
            window.ShowUtility();
            window.position = new Rect(100, 100, 480, 650);
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
                CheckStandalone(test, root, (WhimTexDocument)state[7]);
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
            window.position = new Rect(window.position.x, window.position.y, 340, 650);
            state[4] = window.rootVisualElement.schedule.Execute(() => CheckUpdated(state)).StartingIn(400);
            pending = true;
        }
        finally { if (!pending) Release(state); }
    }

    static void CheckUpdated(object[] state)
    {
        bool pending = false;
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
                CheckStandalone(test, root, (WhimTexDocument)state[7]);
            });
            var result = JsonUtility.FromJson<TestResult>(second);
            result.checks += (int)state[6]; state[6] = result.checks;
            if (result.status != "passed") { state[3] = result.ToJson(); return; }
            state[8] = 0;
            ScheduleAspect(state);
            pending = true;
        }
        finally { if (!pending) Release(state); }
    }

    static readonly Vector3Int[] AspectCases = {
        new Vector3Int(800, 512, 512), new Vector3Int(800, 512, 2048),
        new Vector3Int(800, 4096, 1024), new Vector3Int(180, 2048, 512),
        new Vector3Int(180, 256, 4096), new Vector3Int(800, 8192, 256),
        new Vector3Int(800, 64, 64), new Vector3Int(180, 64, 128),
        new Vector3Int(180, 256, 256), new Vector3Int(800, 128, 64),
        new Vector3Int(800, 768, 64), new Vector3Int(180, 768, 64)
    };

    static void ScheduleAspect(object[] state)
    {
        var config = AspectCases[(int)state[8]];
        var window = (EditorWindow)state[0];
        var document = (WhimTexDocument)state[7];
        document.width = config.y; document.height = config.z;
        window.position = new Rect(window.position.x, window.position.y, config.x, 650);
        state[4] = window.rootVisualElement.schedule.Execute(() => CheckAspect(state)).StartingIn(400);
    }

    static void CheckAspect(object[] state)
    {
        bool pending = false;
        try
        {
            var result = JsonUtility.FromJson<TestResult>(TestContext.Run("Standalone preview canvas aspect and panel limits", test =>
            {
                var root = ((EditorWindow)state[0]).rootVisualElement;
                CheckStandalone(test, root, (WhimTexDocument)state[7]);
                var cache = state[5];
                test.Equal(4, (int)cache.GetType().GetProperty("ProgramCount", F).GetValue(cache), "Resizing reuses compiled programs");
                if ((int)state[8] == AspectCases.Length - 1)
                {
                    var previews = root.Query<Image>(className: "whimtex-fx-preview").ToList();
                    root.Clear();
                    test.Equal(0, (int)cache.GetType().GetProperty("ProgramCount", F).GetValue(cache), "Last detach releases shared programs");
                    foreach (var preview in previews) test.True(preview.image == null, "Detach releases each surface image");
                }
            }));
            result.checks += (int)state[6]; state[6] = result.checks;
            if (result.status != "passed" || (int)state[8] == AspectCases.Length - 1) { state[3] = result.ToJson(); return; }
            state[8] = (int)state[8] + 1; ScheduleAspect(state); pending = true;
        }
        finally { if (!pending) Release(state); }
    }

    static void CheckStandalone(TestContext test, VisualElement root, WhimTexDocument document)
    {
        var preview = root.Q<Image>(className: "whimtex-fx-preview--standalone");
        var style = preview.resolvedStyle;
        float aspect = document.width / (float)document.height;
        test.True(preview.layout.height <= 128.01f, "Standalone height is capped at 128 logical pixels");
        float available = preview.parent.contentRect.width - style.marginLeft - style.marginRight;
        test.True(preview.layout.width <= available + .1f, "Standalone fits available inspector width including margins");
        test.Near(preview.parent.worldBound.center.x, preview.worldBound.center.x, .6, "Capped preview stays centered");
        if (aspect >= 1)
            test.Near(preview.contentRect.width / aspect, preview.contentRect.height, .6, "Image content preserves canvas aspect within layout pixel rounding");
        else
            test.Near(preview.contentRect.height * aspect, preview.contentRect.width, .6, "Image content preserves canvas aspect within layout pixel rounding");
        test.True(Mathf.Abs(preview.layout.width - available) < .6f || Mathf.Abs(preview.layout.height - 128) < .6f,
            "Preview uses available width or reaches its height cap");
        var texture = preview.image as RenderTexture;
        test.True(texture != null && texture.IsCreated(), "Standalone image renders after resize");
        bool magnified = preview.contentRect.width * EditorGUIUtility.pixelsPerPoint > document.width ||
            preview.contentRect.height * EditorGUIUtility.pixelsPerPoint > document.height;
        test.Equal(magnified ? FilterMode.Point : FilterMode.Bilinear, texture.filterMode, "Only canvas magnification uses Point");
        if (magnified)
        {
            test.Equal(document.width, texture.width, "Magnified preview renders one texel per canvas pixel");
            test.Equal(document.height, texture.height, "Magnified preview retains native canvas height");
            if (document.width <= 128 && document.height <= 128) CheckPointPixels(test, texture);
        }
        else test.True(texture.width <= 1024 && texture.height <= 256, "Reduced standalone GPU allocation remains bounded");
        test.Near(texture.height * aspect, texture.width, Mathf.Max(1.1f, aspect), "GPU aspect follows canvas within integer texel rounding");
    }

    static void CheckPointPixels(TestContext test, RenderTexture preview)
    {
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        int width = preview.width * 3 + 1, height = preview.height * 3 + 1;
        var enlarged = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        try
        {
            enlarged.Create(); GL.sRGBWrite = false;
            Graphics.Blit(preview, enlarged);
            RenderTexture.active = enlarged; pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            bool black = false, white = false, exact = true;
            foreach (var pixel in pixels.GetPixels32())
            {
                black |= pixel.r == 0; white |= pixel.r == 255;
                exact &= pixel.r == 0 || pixel.r == 255;
            }
            test.True(black && white && exact, "Noninteger Point enlargement preserves checker texels without blended edges");
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            enlarged.Release(); UnityEngine.Object.DestroyImmediate(enlarged); UnityEngine.Object.DestroyImmediate(pixels);
        }
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
        var errors = new System.Collections.Generic.List<Exception>();
        void ReleaseOwned(Action action)
        {
            try { action(); }
            catch (Exception error) { errors.Add(error); }
        }
        ReleaseOwned(() =>
        {
            (state[4] as IVisualElementScheduledItem)?.Pause(); state[4] = null;
        });
        ReleaseOwned(() =>
        {
            if (state[0] is EditorWindow window && window != null)
            {
                try { if (window.rootVisualElement.panel != null) window.Close(); }
                finally { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
            }
            state[0] = null;
        });
        ReleaseOwned(() =>
        {
            if (state[1] is ShaderFX effect && effect != null)
            {
                try { Undo.ClearUndo(effect); }
                finally { UnityEngine.Object.DestroyImmediate(effect); }
            }
            state[1] = null;
        });
        ReleaseOwned(() =>
        {
            if (state[7] is WhimTexDocument document && document != null) UnityEngine.Object.DestroyImmediate(document);
            state[7] = null;
        });
        ReleaseOwned(() =>
        {
            if (state[2] is EditorWindow focus && focus != null) focus.Focus(); state[2] = null;
        });
        if (errors.Count != 0)
        {
            var failure = new AggregateException("Owned FX fixture cleanup unfinished; retry cleanup before another run", errors);
            state[3] = TestContext.Result("failed", 0, "Cleanup requires recovery; prior result: " + state[3], failure.ToString()).ToJson();
            throw failure;
        }
    }
}
