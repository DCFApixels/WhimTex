using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class EdgeOutlineTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    const int W = 32, H = 24;

    public static string Run() => TestContext.Run("Edge Outline", t =>
    {
        using (var fixture = new MigrationD()) Execute(t);
    });

    static void Execute(TestContext t)
    {
        var owned = new List<UnityEngine.Object>();
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var output = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        doc.width = W; doc.height = H;
        var input = Own(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat });
        var read = Own(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true));
        try
        {
            var catalog = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog", true);
            object selected = null;
            foreach (var entry in (IEnumerable)catalog.GetMethod("GetEntries", Hidden).Invoke(null, null))
                if ((string)entry.GetType().GetField("menuPath", Hidden).GetValue(entry) == "Stylization/Edge Outline")
                    selected = entry;
            t.True(selected != null, "Preset is automatically discovered in the FX catalog");
            var fx = Own((ShaderFX)typeof(ShaderFX).GetMethod("FromCatalog", Hidden).Invoke(null, new[] { (object)doc, selected }));
            var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", Hidden).GetValue(fx);
            t.Equal(12, parameters.Count, "Shared controls plus method-specific strength, hue sensitivity and FX opacity");
            ShaderFXParameter P(string name) => parameters.Find(p => p.name == name);
            t.True(P("_Opacity").controls[0].hidden && !P("_Opacity").controls[0].inGroup, "Opacity is reserved for the FX block header");
            t.Near(1, P("_Opacity").floatValue, 0, "Default opacity preserves the full FX result");
            t.Near(0, P("_Opacity").minimum, 0, "Opacity minimum");
            t.Near(1, P("_Opacity").maximum, 0, "Opacity maximum");
            t.Equal(ShaderFXParameterType.Enum, P("_Method").controls[0].type, "Method is a dropdown");
            t.Near(0, P("_Method").floatValue, 0, "Boundary remains the default method");
            t.Equal("Scharr", P("_Method").controls[0].optionNames[1], "Scharr is the second method");
            t.Equal("_Method", P("_Strength").controls[0].visibleIfParameter, "Strength is conditional on method");
            t.Near(1, P("_Strength").controls[0].visibleIfValue, 0, "Strength appears only for Scharr");
            t.True(!P("_Strength").controls[0].visibleIfNotEqual, "Strength is hidden for Boundary");
            foreach (string name in new[] { "_Detection", "_Shape", "_Thickness", "_Softness", "_OutlineColor", "_Output" })
                t.True(string.IsNullOrEmpty(P(name).controls[0].visibleIfParameter), "Common control stays available in both methods: " + name);
            t.Equal(ShaderFXParameterType.Enum, P("_Detection").controls[0].type, "Detection is a dropdown");
            t.Equal("Hue", P("_Detection").controls[0].optionNames[2], "Hue is the third detection mode");
            t.Near(2, P("_Detection").controls[0].optionValues[2], 0, "Existing detection values remain unchanged");
            t.Near(0, P("_Detection").floatValue, 0, "Color is the default detection mode");
            t.Near(2, P("_Thickness").floatValue, 0, "Default total line width");
            t.Near(15, P("_HueThreshold").floatValue, 0, "Default hue threshold in degrees");
            t.Near(.1f, P("_MinSaturation").floatValue, 0, "Default minimum saturation");
            void Group(string title, string header, params string[] members)
            {
                var definition = P(header).controls[0];
                t.True(definition.hidden, "Linked header has no duplicate body row: " + header);
                foreach (string name in members)
                {
                    var control = P(name).controls[0];
                    t.True(control.inGroup, "Parameter belongs to a group: " + name);
                    t.Equal(definition.groupId, control.groupId, "Contiguous group membership: " + name);
                    t.Equal(title, control.groupTitle, "Group title: " + name);
                    t.Equal(header, control.groupHeaderParameter, "Linked group header: " + name);
                }
            }
            Group("Detection", "_Detection", "_Detection", "_Method", "_Threshold", "_HueThreshold", "_MinSaturation", "_Strength");
            Group("Contour", "_Shape", "_Shape", "_Thickness", "_Softness");
            Group("Output", "_Output", "_Output", "_OutlineColor");
            t.Equal("Thickness (px)", P("_Thickness").controls[0].label, "Width unit is visible in the label");
            t.Equal("Softness (px)", P("_Softness").controls[0].label, "Softness unit is visible in the label");
            t.Equal("Hue Threshold (°)", P("_HueThreshold").controls[0].label, "Hue unit is visible in the label");
            t.Equal("Color", P("_OutlineColor").controls[0].label, "Output group gives context to the short color label");
            foreach (string name in new[] { "_Threshold", "_HueThreshold", "_MinSaturation" })
            {
                var control = P(name).controls[0];
                t.Equal("_Detection", control.visibleIfParameter, "Sensitivity visibility uses the shared detection dropdown");
                t.Near(2, control.visibleIfValue, 0, "Sensitivity visibility targets Hue");
                t.Equal(name == "_Threshold", control.visibleIfNotEqual, "Only the selected detection sensitivity is visible");
            }
            var shader = (Shader)typeof(ShaderFX).GetField("compiledShader", Hidden).GetValue(fx);
            t.True(shader != null && !ShaderUtil.ShaderHasError(shader), "Preset compiles");
            var contextType = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
            object renderContext = Activator.CreateInstance(contextType, doc, null, W, H, 1f, true, true, null);
            void Settings(int detection = 0, int shape = 0, float thickness = 2, float threshold = .1f,
                float softness = 0, int mode = 1, float opacity = 1, float hueThreshold = 15, float minSaturation = .1f,
                int method = 0, float strength = 1, float fxOpacity = 1)
            {
                P("_Opacity").floatValue = fxOpacity;
                P("_Method").floatValue = method; P("_Strength").floatValue = strength;
                P("_Detection").floatValue = detection; P("_Shape").floatValue = shape;
                P("_Thickness").floatValue = thickness; P("_Threshold").floatValue = threshold;
                P("_Softness").floatValue = softness; P("_Output").floatValue = mode;
                P("_OutlineColor").colorValue = new Color(0, 0, 0, opacity);
                P("_HueThreshold").floatValue = hueThreshold; P("_MinSaturation").floatValue = minSaturation;
                typeof(ShaderFX).GetMethod("NotifyValuesChanged", Hidden).Invoke(fx, null);
            }
            Color[] Render(Color[] pixels)
            {
                input.SetPixels(pixels); input.Apply();
                var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", Hidden).Invoke(fx, new[] { renderContext });
                GL.sRGBWrite = false; Graphics.Blit(input, output, material);
                RenderTexture.active = output; read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply();
                t.True(!ShaderUtil.ShaderHasError(shader), "GPU shader has no errors after rendering");
                return read.GetPixels();
            }
            Color[] Image(Func<int, int, Color> sample)
            {
                var pixels = new Color[W * H];
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) pixels[y * W + x] = sample(x, y);
                return pixels;
            }
            void Same(Color[] expected, Color[] actual, string label, float tolerance = .002f)
            {
                t.Equal(expected.Length, actual.Length, label + " dimensions");
                double worst = 0;
                for (int i = 0; i < expected.Length; i++) for (int c = 0; c < 4; c++)
                {
                    if (float.IsNaN(actual[i][c]) || float.IsInfinity(actual[i][c]))
                        throw new InvalidOperationException(label + " has nonfinite pixels");
                    worst = Math.Max(worst, Math.Abs(actual[i][c] - expected[i][c]));
                }
                t.Near(0, worst, tolerance, label);
            }
            Color[] Verify(Color[] pixels, int detection, int shape, float thickness, float softness = 0,
                int mode = 1, float opacity = 1, float threshold = .1f, float hueThreshold = 15, float minSaturation = .1f,
                int method = 0, float strength = 1, float fxOpacity = 1)
            {
                Settings(detection, shape, thickness, threshold, softness, mode, opacity, hueThreshold, minSaturation, method, strength, fxOpacity);
                var actual = Render(pixels);
                Same(Reference(pixels, detection, shape, thickness, threshold, softness, mode, opacity, hueThreshold, minSaturation, method, strength, fxOpacity), actual,
                    $"All-pixel reference: method {method}, detection {detection}, shape {shape}, thickness {thickness}, softness {softness}, output {mode}");
                return actual;
            }

            var uniform = Image((x, y) => new Color(.4f, .6f, .8f, .37f));
            var isoluminant = Image((x, y) => x < W / 2 ? Color.red : new Color(0, .2126f / .7152f, 0, 1));
            var corner = Image((x, y) => x >= 15 && x <= 16 && y >= 11 && y <= 12 ? Color.white : new Color(.2f, .5f, .8f, 1));
            var diagonal = Image((x, y) => x > y + 4 ? Color.white : new Color(.2f, .5f, .8f, .4f));
            var gradient = Image((x, y) => new Color(x / (float)(W - 1), .3f, .7f, 1));
            var hiddenRgb = Image((x, y) => x < W / 2 ? new Color(.4f, .6f, .8f, 1) : new Color(x % 2, y % 2, 1, 0));
            var onePixel = Image((x, y) => x == 15 && y == 11 ? Color.white : new Color(.2f, .5f, .8f, 1));
            var visual = new List<Color[]> { isoluminant, corner, diagonal, onePixel };
            foreach (int detection in new[] { 0, 1, 2 })
            {
                foreach (int shape in new[] { 0, 1, 2 })
                {
                    Verify(uniform, detection, shape, 32, threshold: 0);
                    Verify(gradient, detection, shape, 32);
                    Verify(hiddenRgb, detection, shape, 12);
                    foreach (float thickness in new[] { .5f, 2f, 9f, 20f, 32f })
                        Verify(corner, detection, shape, thickness);
                    Verify(diagonal, detection, shape, 12, softness: 4);
                    Verify(onePixel, detection, shape, 20);
                    Verify(isoluminant, detection, shape, 5);
                    visual.Add(Verify(corner, detection, shape, 12, mode: 0));
                }
            }
            var detected = Verify(isoluminant, 0, 0, 2);
            var ignored = Verify(isoluminant, 1, 0, 2);
            t.True(detected[12 * W + 15].a > .99f && ignored[12 * W + 15].a == 0,
                "Color detects an equal-luminance chromatic boundary that Luminance ignores");
            t.True(Verify(isoluminant, 2, 0, 2)[12 * W + 15].a > .99f,
                "Hue also detects equal-luminance colors");

            // Build colors in display HSV, independently of the shader's HSV conversion.
            Color Hsv(float degrees, float saturation = 1, float value = 1)
                => Color.HSVToRGB(degrees / 360, saturation, value, true).linear;
            Color[] Pair(Color first, Color second) => Image((x, y) => x < W / 2 ? first : second);
            void HueBoundary(Color first, Color second, bool expected, string label,
                float hueThreshold = 15, float minSaturation = .1f)
            {
                var pixels = Pair(first, second);
                var actual = Verify(pixels, 2, 0, 2, hueThreshold: hueThreshold, minSaturation: minSaturation);
                t.Near(expected ? 1 : 0, actual[12 * W + 15].a, .002, label);
                visual.Add(Verify(pixels, 2, 0, 2, mode: 0, hueThreshold: hueThreshold, minSaturation: minSaturation));
            }
            HueBoundary(Hsv(120, 1, .25f), Hsv(120, 1, 1), false, "Same hue ignores brightness changes");
            HueBoundary(Hsv(120, .2f), Hsv(120, 1), false, "Same hue ignores saturation changes");
            HueBoundary(Hsv(359), Hsv(1), false, "Hue wraps across red without a false large difference", hueThreshold: 3);
            HueBoundary(Hsv(359), Hsv(1), true, "Wrapped two-degree hue change can be selected", hueThreshold: 1);
            HueBoundary(Hsv(0), Hsv(180), true, "Opposite hues reach the 180-degree endpoint", hueThreshold: 180);
            HueBoundary(Hsv(120), Hsv(150), false, "Hue threshold can suppress a real color transition", hueThreshold: 40);
            HueBoundary(Hsv(120), Hsv(150), true, "Hue threshold selects a stronger color transition", hueThreshold: 20);
            HueBoundary(Hsv(120, .09f), Hsv(240), false, "Either low-saturation endpoint suppresses the boundary");
            HueBoundary(Hsv(120, .11f), Hsv(240), true, "Saturated endpoints remain detectable");
            HueBoundary(Hsv(120, .09f), Hsv(240), true, "Lowering minimum saturation restores muted color boundaries", minSaturation: .05f);
            HueBoundary(Color.gray, Color.blue, false, "Gray has no meaningful hue even with zero saturation cutoff", minSaturation: 0);
            HueBoundary(Color.black, Color.white, false, "Black and white never seed hue boundaries", minSaturation: 0);
            HueBoundary(new Color(1e-8f, 0, 0, 1), new Color(0, 1e-8f, 0, 1), false,
                "Numerically near-black colors do not create hue noise", minSaturation: 0);
            HueBoundary(Hsv(210), Hsv(210), false, "Zero hue threshold still excludes identical hues", hueThreshold: 0);
            HueBoundary(new Color(-1, 1, 0, 1), new Color(1, -1, 0, 1), true,
                "Negative channels use the HSV preset's nonnegative convention");
            HueBoundary(new Color(0, 4, 0, 1), new Color(4, 0, 0, 1), true, "HDR hues remain finite and detectable");
            foreach (int shape in new[] { 0, 1, 2 })
            {
                var hueFeature = Image((x, y) => x >= 15 && x <= 16 && y >= 11 && y <= 12 ? Hsv(240) : Hsv(120));
                Verify(hueFeature, 2, shape, 32, softness: 4);
                var hueDiagonal = Image((x, y) => x > y + 4 ? Hsv(240) : Hsv(120));
                Verify(hueDiagonal, 2, shape, 12);
                Verify(hueDiagonal, 2, shape, 5, mode: 0, opacity: .5f);
            }
            var overlay = Verify(diagonal, 0, 0, 5, mode: 0, opacity: .5f);
            for (int i = 0; i < overlay.Length; i++) t.Near(diagonal[i].a, overlay[i].a, .0001, "Overlay preserves source alpha");
            Verify(diagonal, 0, 0, 0, mode: 0);
            Verify(diagonal, 0, 0, 0);
            Verify(diagonal, 0, 0, 5, mode: 0, opacity: 0);
            Verify(diagonal, 0, 0, 5, threshold: 2);

            // Independent 3x3 derivative and global weighted-spread oracle, including subpixel width.
            foreach (int detection in new[] { 0, 1, 2 }) foreach (int shape in new[] { 0, 1, 2 })
            {
                Verify(uniform, detection, shape, 32, threshold: 0, method: 1);
                Verify(hiddenRgb, detection, shape, 12, method: 1);
                Verify(gradient, detection, shape, 12, threshold: 0, hueThreshold: 0, method: 1);
                foreach (float thickness in new[] { .5f, 2f, 9f, 32f })
                    Verify(corner, detection, shape, thickness, method: 1);
                Verify(onePixel, detection, shape, 20, method: 1);
                Verify(diagonal, detection, shape, 12, softness: 4, method: 1);
                Verify(isoluminant, detection, shape, 5, method: 1);
                var hueFeature = Image((x, y) => x >= 15 && x <= 16 && y >= 11 && y <= 12 ? Hsv(240) : Hsv(120));
                Verify(hueFeature, detection, shape, 12, method: 1);
            }
            foreach (var pair in new[] {
                Pair(Hsv(359), Hsv(1)), Pair(Hsv(120, .09f), Hsv(240)),
                Pair(Hsv(120, .2f), Hsv(120, 1)), Pair(Hsv(120, 1, .25f), Hsv(120, 1, 1)),
                Pair(Color.gray, Color.blue), Pair(Color.black, Color.white),
                Pair(new Color(0, 4, 0, 1), new Color(4, 0, 0, 1)) })
                Verify(pair, 2, 0, 5, hueThreshold: 3, method: 1);
            var weakStep = Pair(Color.black, new Color(.25f, .25f, .25f, 1));
            var weakBoundary = Verify(weakStep, 0, 0, 2);
            var weakScharr = Verify(weakStep, 0, 0, 2, method: 1);
            t.Near(1, weakBoundary[12 * W + 15].a, .002, "Boundary gives uniform opacity above threshold");
            t.Near(.25, weakScharr[12 * W + 15].a, .002, "Scharr retains the strength of a weak transition");
            t.Near(.5, Verify(weakStep, 0, 0, 2, method: 1, strength: 2)[12 * W + 15].a, .002,
                "Strength amplifies Scharr opacity without changing its footprint");
            t.Near(0, Verify(weakStep, 0, 0, 2, method: 1, threshold: .3f)[12 * W + 15].a, .002,
                "Shared threshold rejects weaker Scharr gradients");
            Verify(weakStep, 0, 0, 2, strength: 0);
            Verify(weakStep, 0, 0, 2, method: 1, strength: 0);
            Verify(weakStep, 0, 0, 2, method: 1, strength: 0, mode: 0);
            Verify(diagonal, 0, 0, 5, method: 1, mode: 0, opacity: .5f);
            Verify(diagonal, 0, 0, 5, method: 1, mode: 0, opacity: 0);
            Verify(diagonal, 0, 0, 0, method: 1);
            Verify(diagonal, 0, 0, 0, method: 1, mode: 0);
            Verify(diagonal, 0, 0, 5, method: 1, threshold: 2);
            foreach (var pixels in new[] { weakStep, diagonal, onePixel, gradient, isoluminant })
            {
                visual.Add(Verify(pixels, 0, 0, 4, mode: 0));
                visual.Add(Verify(pixels, 0, 0, 4, mode: 0, method: 1));
            }
            foreach (int method in new[] { 0, 1 }) foreach (int mode in new[] { 0, 1 })
            {
                var full = Verify(diagonal, 0, 0, 5, method: method, mode: mode, opacity: .4f);
                foreach (float amount in new[] { 0f, .35f, 1f })
                {
                    var actual = Verify(diagonal, 0, 0, 5, method: method, mode: mode, opacity: .4f, fxOpacity: amount);
                    var expected = new Color[diagonal.Length];
                    for (int i = 0; i < expected.Length; i++) expected[i] = Color.Lerp(diagonal[i], full[i], amount);
                    Same(expected, actual, "FX opacity blends the complete result independently of tint alpha");
                }
                foreach (var pixels in new[] { hiddenRgb, uniform })
                    Same(pixels, Verify(pixels, 0, 0, 5, method: method, mode: mode, fxOpacity: 0),
                        "Zero FX opacity preserves every input channel, including invisible RGB");
            }
            var viewType = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView", true);
            var view = (VisualElement)Activator.CreateInstance(viewType, Hidden, null, new object[] { fx }, null);
            var effectControl = (VisualElement)Activator.CreateInstance(viewType, Hidden, null, new object[] { fx, true }, null);
            try
            {
                t.Equal(3, view.childCount, "Hidden FX opacity does not add a body row");
                t.Equal(1, effectControl.Query<FloatField>().ToList().Count, "FX header exposes one opacity field");
                t.True(effectControl.Q<VisualElement>(className: "whimtex-fx-effect-control-handle").GetType().Name == "LayerActionIcon",
                    "Opacity uses the shared alpha drag handle");
                t.Equal(P("_Opacity").controls[0].tooltip, effectControl.tooltip, "FX header explains opacity behavior");
                foreach (float amount in new[] { 0f, .35f, 1f })
                {
                    Settings(fxOpacity: amount);
                    viewType.GetMethod("Refresh", Hidden).Invoke(effectControl, null);
                    t.Near(amount, effectControl.Q<FloatField>().value, 0, "FX header reflects the current opacity");
                }
                var groups = view.Query<VisualElement>(className: "whimtex-fx-parameter-group").ToList();
                t.Equal(3, groups.Count, "Actual UI has three compact groups");
                string[] titles = { "Detection", "Contour", "Output" };
                string[] headers = { "_Detection", "_Shape", "_Output" };
                for (int i = 0; i < groups.Count; i++)
                {
                    var header = groups[i].Q<VisualElement>(className: "whimtex-fx-parameter-group-header");
                    t.Equal(titles[i], header.Q<Label>(className: "whimtex-fx-parameter-group-title").text, "Actual group order/title");
                    var dropdowns = header.Query<DropdownField>(className: "whimtex-fx-group-header-field").ToList();
                    t.Equal(1, dropdowns.Count, "One dropdown per group header");
                    t.Equal(string.Empty, dropdowns[0].label, "Group title replaces the dropdown label");
                    t.Equal(P(headers[i]).controls[0].tooltip, dropdowns[0].tooltip, "Header retains its parameter tooltip");
                }
                t.Equal(4, view.Query<DropdownField>().ToList().Count, "Three linked selectors and Method, without duplicate dropdowns");
                t.Equal("Thickness (px)", groups[1].Query<Slider>().ToList()[0].label, "Actual width label includes pixels");
                t.Equal("Softness (px)", groups[1].Query<Slider>().ToList()[1].label, "Actual softness label includes pixels");
                var conditionalRows = view.Query<VisualElement>(className: "whimtex-fx-conditional-parameter").ToList();
                t.Equal(4, conditionalRows.Count, "Only detection sensitivity and Scharr strength are conditional");
                foreach (int method in new[] { 0, 1 }) foreach (int detection in new[] { 0, 1, 2 })
                {
                    Settings(detection, method: method, strength: 1.7f);
                    viewType.GetMethod("Refresh", Hidden).Invoke(view, null);
                    t.Equal(new[] { "Color", "Luminance", "Hue" }[detection],
                        groups[0].Q<DropdownField>(className: "whimtex-fx-group-header-field").value,
                        "Linked detection header refreshes without rebuilding the groups");
                    bool[] visible = { detection != 2, detection == 2, detection == 2, method == 1 };
                    for (int i = 0; i < conditionalRows.Count; i++)
                        t.Equal(visible[i] ? DisplayStyle.Flex : DisplayStyle.None, conditionalRows[i].style.display.value,
                            $"Actual parameter row visibility: method {method}, detection {detection}, row {i}");
                    t.Near(1.7f, P("_Strength").floatValue, 0, "Refreshing hidden UI preserves strength");
                }
            }
            finally { effectControl.Clear(); view.Clear(); }
            t.True(ReferenceEquals(shader, typeof(ShaderFX).GetField("compiledShader", Hidden).GetValue(fx)),
                "Mode/shape/line edits reuse the compiled shader");
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                t.True(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error &&
                    message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning, message.message);

            // Ordinary layer, group, clipping, Target input, thumbnail and layered-export paths.
            Settings(detection: 2, thickness: 4, mode: 0, method: 1, strength: 3);
            var draft = typeof(ShaderFX).GetMethod("CreateAgentDraft", Hidden, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            var source = Own((ShaderFX)draft.Invoke(null, new object[] { doc,
                "float4 ApplyFX(float2 uv, float4 color) { return uv.x < .5 ? float4(0,0,1,1) : float4(1,0,0,1); }",
                new List<ShaderFXParameter>() }));
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", Hidden).Invoke(source, null);
            Layer child = new ColorFillLayerBehaviour(); child.fx.Add(source); child.fx.Add(fx); doc.layers.Add(child);
            Color[] Composite()
            {
                var activeBefore = RenderTexture.active; bool srgbBefore = GL.sRGBWrite;
                var image = doc.ComposeCanvas();
                try
                {
                    t.True(RenderTexture.active == activeBefore && GL.sRGBWrite == srgbBefore, "Composite preserves caller render state");
                    return image.GetPixels();
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            var baseline = Composite();
            t.True(baseline[12 * W + 15].r < .01f && baseline[12 * W + 3].b > .7f, "Layer FX preserves source and overlays boundary");
            child.fx.Remove(fx);
            Layer group = new GroupLayerBehaviour(); group.children.Add(child); group.fx.Add(fx);
            doc.layers.Clear(); doc.layers.Add(group);
            Same(baseline, Composite(), "Group input equals ordinary layer input", .005f);
            var thumbnail = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderAgentLayerPreview", Hidden).Invoke(doc, new object[] { group, W });
            try
            {
                RenderTexture.active = thumbnail; read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply();
                Same(baseline, read.GetPixels(), "Group thumbnail (linear input)", .008f);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(thumbnail); }
            thumbnail = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderAgentLayerPreview", Hidden).Invoke(doc, new object[] { group, W / 2 });
            try
            {
                var reduced = Own(new Texture2D(W / 2, H / 2, TextureFormat.RGBAFloat, false, true));
                RenderTexture.active = thumbnail; reduced.ReadPixels(new Rect(0, 0, W / 2, H / 2), 0, 0); reduced.Apply();
                t.True(reduced.GetPixel(7, 6).r < .01f && reduced.GetPixel(8, 6).r < .01f,
                    "Reduced thumbnail keeps total thickness in canvas pixels");
                t.True(reduced.GetPixel(6, 6).b > .7f && reduced.GetPixel(9, 6).r > .99f,
                    "Reduced thumbnail does not widen the contour in document units");
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(thumbnail); }
            var exported = (Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdGroupContent", Hidden).Invoke(doc, new object[] { group });
            try
            {
                var display = (Color[])baseline.Clone();
                for (int i = 0; i < display.Length; i++) display[i] = display[i].gamma;
                Same(display, exported.GetPixels(), "Layered group export", .008f);
            }
            finally { UnityEngine.Object.DestroyImmediate(exported); }
            group.clippingMask = true; doc.layers.Add(new ColorFillLayerBehaviour { color = Color.white });
            Same(baseline, Composite(), "Clipped group over an opaque base", .005f);
            group.clippingMask = false; doc.layers.RemoveAt(1); group.enabled = false;
            var target = new BlurLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = group.Id, radius = 0 };
            Layer targetLayer = target; doc.layers.Insert(0, targetLayer);
            Same(baseline, Composite(), "Disabled group supplies Target input with its FX", .005f);
            doc.layers.Clear(); group.enabled = true; doc.layers.Add(group);

            // Portable JSON with a procedural input requires no imported assets or file saves.
            foreach (int method in new[] { 0, 1 }) foreach (int detection in new[] { 0, 1, 2 })
            {
                Settings(detection, shape: 2, thickness: 9, softness: 2, mode: 1, hueThreshold: 25, minSaturation: .35f,
                    method: method, strength: 1.7f, fxOpacity: .65f);
                var before = Composite();
                var json = WhimTexDocumentJson.Write(doc);
                t.Equal(0, json.Warnings.Count, "JSON write preserves all content");
                using (var restored = WhimTexDocumentJson.Read(json.Json))
                {
                    t.Equal(0, restored.Warnings.Count, "JSON read compiles the preset without warnings");
                    var restoredParameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", Hidden)
                        .GetValue(restored.Document.layers[0].fx[0]);
                    t.Near(detection, restoredParameters.Find(p => p.name == "_Detection").floatValue, 0, "Detection survives JSON");
                    t.Near(method, restoredParameters.Find(p => p.name == "_Method").floatValue, 0, "Method survives JSON");
                    t.Near(.65f, restoredParameters.Find(p => p.name == "_Opacity").floatValue, 0, "FX opacity survives JSON");
                    var restoredHeader = (VisualElement)Activator.CreateInstance(viewType, Hidden, null,
                        new object[] { restored.Document.layers[0].fx[0], true }, null);
                    try { t.Near(.65f, restoredHeader.Q<FloatField>().value, 0, "FX header binding survives JSON"); }
                    finally { restoredHeader.Clear(); }
                    t.Near(1.7f, restoredParameters.Find(p => p.name == "_Strength").floatValue, 0, "Strength survives JSON even when hidden");
                    t.Near(25, restoredParameters.Find(p => p.name == "_HueThreshold").floatValue, 0, "Hue threshold survives JSON even when hidden");
                    t.Near(.35f, restoredParameters.Find(p => p.name == "_MinSaturation").floatValue, 0, "Minimum saturation survives JSON even when hidden");
                    var image = restored.Document.ComposeCanvas();
                    try { Same(before, image.GetPixels(), "JSON render parity", .005f); }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                }
            }

            // Save a contact sheet outside Assets and the package for actual visual inspection.
            const int scale = 4, columns = 5;
            int rows = (visual.Count + columns - 1) / columns;
            var sheet = Own(new Texture2D(W * scale * columns, H * scale * rows, TextureFormat.RGBA32, false, true));
            var sheetPixels = new Color[sheet.width * sheet.height];
            for (int n = 0; n < visual.Count; n++)
                for (int y = 0; y < H * scale; y++) for (int x = 0; x < W * scale; x++)
                    sheetPixels[((rows - 1 - n / columns) * H * scale + y) * sheet.width + n % columns * W * scale + x] =
                        visual[n][y / scale * W + x / scale].gamma;
            sheet.SetPixels(sheetPixels); sheet.Apply();
            string folder = MigrationD.ProjectPath("Temp/WhimTex/edge-outline-tests/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, "comparison.png"), sheet.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(output);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
        }
    }

    // Global brute-force oracle: binary segments or matrix-convolved pixel seeds,
    // followed by a full weighted spread. No shader local search or early-exit logic is reused.
    static Color[] Reference(Color[] pixels, int detection, int shape, float thickness, float threshold,
        float softness, int output, float opacity, float hueThreshold, float minSaturation, int method, float strength, float fxOpacity)
    {
        var segments = new List<Vector4>();
        var weights = new List<double>();
        Vector2 Hue(Color color)
        {
            var display = new Color(Mathf.Max(color.r, 0), Mathf.Max(color.g, 0), Mathf.Max(color.b, 0), 1).gamma;
            float chroma = Mathf.Max(display.r, Mathf.Max(display.g, display.b)) - Mathf.Min(display.r, Mathf.Min(display.g, display.b));
            if (chroma <= 1e-5) return Vector2.zero;
            Color.RGBToHSV(display, out float hue, out float saturation, out _);
            return new Vector2(hue, saturation);
        }
        bool HasHue(Vector2 hue) => hue.y > 1e-5 && hue.y >= Mathf.Clamp01(minSaturation);
        bool Boundary(Color a, Color b)
        {
            if (Math.Min(a.a, b.a) <= 1e-5) return false;
            if (detection == 2)
            {
                Color Display(Color color) => new Color(Mathf.Max(color.r, 0), Mathf.Max(color.g, 0), Mathf.Max(color.b, 0), 1).gamma;
                var first = Display(a); var second = Display(b);
                float Chroma(Color color) => Mathf.Max(color.r, Mathf.Max(color.g, color.b)) - Mathf.Min(color.r, Mathf.Min(color.g, color.b));
                if (Chroma(first) <= 1e-5 || Chroma(second) <= 1e-5) return false;
                Color.RGBToHSV(first, out float ha, out float sa, out _);
                Color.RGBToHSV(second, out float hb, out float sb, out _);
                if (Math.Min(sa, sb) <= 1e-5 || Math.Min(sa, sb) < Mathf.Clamp01(minSaturation)) return false;
                return Math.Abs(Mathf.DeltaAngle(ha * 360, hb * 360)) >= Math.Max(Mathf.Clamp(hueThreshold, 0, 180), .001);
            }
            double dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
            double delta = detection == 0 ? Math.Max(Math.Abs(dr), Math.Max(Math.Abs(dg), Math.Abs(db)))
                : Math.Abs(.2126 * dr + .7152 * dg + .0722 * db);
            return delta >= Math.Max(threshold, 1e-5);
        }
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            int i = y * W + x;
            if (method == 1)
            {
                Color center = pixels[i]; var centerHue = Hue(center);
                if (center.a <= 1e-5 || (detection == 2 && !HasHue(centerHue))) continue;
                int[] kernelX = { -3, 0, 3, -10, 0, 10, -3, 0, 3 };
                int[] kernelY = { -3, -10, -3, 0, 0, 0, 3, 10, 3 };
                var dx = Vector3.zero; var dy = Vector3.zero;
                for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
                {
                    Color neighbor = pixels[Mathf.Clamp(y + oy, 0, H - 1) * W + Mathf.Clamp(x + ox, 0, W - 1)];
                    if (neighbor.a <= 1e-5) continue;
                    Vector3 signal;
                    if (detection == 2)
                    {
                        var hue = Hue(neighbor); if (!HasHue(hue)) continue;
                        float degrees = Mathf.DeltaAngle(centerHue.x * 360, hue.x * 360);
                        if (degrees >= 180) degrees -= 360;
                        signal = new Vector3(degrees / 180, 0, 0);
                    }
                    else if (detection == 1)
                        signal = new Vector3(.2126f * (neighbor.r - center.r) + .7152f * (neighbor.g - center.g) + .0722f * (neighbor.b - center.b), 0, 0);
                    else signal = new Vector3(neighbor.r - center.r, neighbor.g - center.g, neighbor.b - center.b);
                    int k = (oy + 1) * 3 + ox + 1;
                    dx += signal * kernelX[k]; dy += signal * kernelY[k];
                }
                dx /= 16; dy /= 16;
                double magnitude = 0;
                for (int c = 0; c < 3; c++) magnitude = Math.Max(magnitude, Math.Sqrt(dx[c] * dx[c] + dy[c] * dy[c]));
                double cutoff = detection == 2 ? Math.Max(Mathf.Clamp(hueThreshold, 0, 180), .001) / 180 : Math.Max(threshold, 1e-5);
                if (magnitude >= cutoff)
                {
                    segments.Add(new Vector4(x, y, 0, 0));
                    weights.Add(Math.Min(magnitude * Math.Max(strength, 0), 1));
                }
                continue;
            }
            if (x + 1 < W && Boundary(pixels[i], pixels[i + 1])) segments.Add(new Vector4(x + .5f, y, 0, .5f));
            if (y + 1 < H && Boundary(pixels[i], pixels[i + W])) segments.Add(new Vector4(x, y + .5f, .5f, 0));
        }
        var result = new Color[pixels.Length];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            int i = y * W + x;
            double nearest = double.PositiveInfinity;
            double weighted = 0;
            for (int n = 0; n < segments.Count; n++)
            {
                var segment = segments[n];
                double dx = Math.Max(Math.Abs(x - segment.x) - segment.z, 0);
                double dy = Math.Max(Math.Abs(y - segment.y) - segment.w, 0);
                double d = shape == 0 ? Math.Sqrt(dx * dx + dy * dy) : shape == 1 ? Math.Max(dx, dy) : dx + dy;
                nearest = Math.Min(nearest, d);
                if (method == 1)
                    weighted = Math.Max(weighted, Math.Max(0, Math.Min(1,
                        (thickness * .5 - .5 - d) / Math.Max(softness, 1) + .5)) * weights[n]);
            }
            float mask = thickness <= 0 || pixels[i].a <= 1e-5 ? 0 :
                (method == 1 ? (float)weighted : Mathf.Clamp01((float)((thickness * .5 - nearest) / Math.Max(softness, 1) + .5))) * opacity;
            if (output == 1) result[i] = new Color(0, 0, 0, pixels[i].a * mask);
            else { result[i] = Color.Lerp(pixels[i], Color.black, mask); result[i].a = pixels[i].a; }
            result[i] = Color.Lerp(pixels[i], result[i], fxOpacity);
        }
        return result;
    }
}
