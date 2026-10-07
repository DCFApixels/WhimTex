using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class InputTilingTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const int W = 13, H = 9;
    // Original 0.12.5 input addressing, kept independently of the current sampling helper.
    const string AddressReference = @"// @whimtex-effect Distortion/Displacement Map
// @param enum _InputEdge = Clamp {Clamp: 0, Repeat: 1, Mirror: 2, Transparent: 3}
// @param float _StrengthX = 5.2
// @param float _StrengthY = -4.4
// @param texture2D _DisplacementMap = self
float2 AddressInputUV(float2 uv, float mode, float2 texelSize, out float inside)
{
    inside = 1.0;
    if (mode < 0.5) return clamp(uv, texelSize * 0.5, 1.0 - texelSize * 0.5);
    if (mode < 1.5) return frac(uv);
    if (mode < 2.5) return 1.0 - abs(frac(uv * 0.5) * 2.0 - 1.0);
    inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    return clamp(uv, texelSize * 0.5, 1.0 - texelSize * 0.5);
}
float4 ApplyFX(float2 uv, float4 color)
{
    float2 displacement = (tex2D(_DisplacementMap, uv).rg - 0.5) * 2.0 * float2(_StrengthX, _StrengthY);
    float inside;
    float2 sampleUV = AddressInputUV(uv + displacement * _CanvasSize.zw, _InputEdge, _MainTex_TexelSize.xy, inside);
    return SampleInput(sampleUV) * inside;
}";

    public static string Run() => TestContext.Run("FX input tiling: sampling, UI, persistence and render paths", t =>
    {
        using (var fixture = new MigrationD()) using (var h = new Harness(t))
        {
            h.CheckSampling();
            h.CheckPresets();
            h.CheckRenamedTiling();
            h.CheckFileCompatibility();
        }
    });

    sealed class Harness : IDisposable
    {
        readonly TestContext t;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly TextureCompositor doc;
        readonly Texture2D input, read, map;
        readonly Color[] pixels;
        readonly RenderTexture output;
        readonly RenderTexture previous = RenderTexture.active;
        readonly bool srgb = GL.sRGBWrite;
        readonly object context;
        readonly Assembly assembly = typeof(ShaderFX).Assembly;
        readonly FieldInfo parameters = typeof(ShaderFX).GetField("parameters", F);
        readonly MethodInfo getMaterial = typeof(ShaderFX).GetMethod("GetMaterial", F);
        readonly MethodInfo notify = typeof(ShaderFX).GetMethod("NotifyValuesChanged", F);
        readonly MethodInfo writer;
        readonly Dictionary<string, ShaderFX> presets = new Dictionary<string, ShaderFX>();

        public Harness(TestContext tests)
        {
            t = tests;
            doc = Own(ScriptableObject.CreateInstance<TextureCompositor>()); doc.width = W; doc.height = H;
            input = Own(new Texture2D(7, 5, TextureFormat.RGBAFloat, false, true)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 0 });
            pixels = new Color[35];
            for (int y = 0; y < 5; y++) for (int x = 0; x < 7; x++)
                pixels[y * 7 + x] = new Color((x + 1) / 8f, (y + 1) / 6f, ((x * 3 + y) % 7) / 6f, ((x + y * 2) % 5 + 1) / 6f);
            input.SetPixels(pixels); input.Apply();
            read = Own(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true));
            map = Own(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
            map.SetPixel(0, 0, new Color(.8f, .25f, .4f, 1)); map.Apply();
            output = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true),
                doc, null, W, H, 1f, true, true, null);
            writer = assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter", true).GetMethod("BuildSource", F);
        }

        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        ShaderFXParameter P(ShaderFX fx, string name) => ((List<ShaderFXParameter>)parameters.GetValue(fx)).Find(p => p.name == name);
        void Set(ShaderFX fx, string name, float value) { P(fx, name).floatValue = value; notify.Invoke(fx, null); }
        ShaderFX Create(string source)
        {
            var create = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(TextureCompositor), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            var fx = Own((ShaderFX)create.Invoke(null, new object[] { doc, source, new List<ShaderFXParameter>() }));
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            return fx;
        }
        Material Material(ShaderFX fx)
        {
            var material = (Material)getMaterial.Invoke(fx, new[] { context });
            t.True(material != null && !ShaderUtil.ShaderHasError(material.shader), "FX compiles");
            foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                t.True(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error &&
                    message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning,
                    message.file + ":" + message.line + ": " + message.message);
            return material;
        }
        Color[] Read(RenderTexture texture)
        {
            RenderTexture.active = texture; read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply(); return read.GetPixels();
        }
        Color[] Render(ShaderFX fx, FilterMode filter = FilterMode.Bilinear)
        {
            input.filterMode = filter;
            var material = Material(fx); material.SetFloat("_WhimTex_InputFilter", filter == FilterMode.Point ? 0 : 1);
            if (P(fx, "_DisplacementMap") != null) material.SetTexture("_DisplacementMap", map);
            GL.sRGBWrite = false; Graphics.Blit(input, output, material); return Read(output);
        }
        void Same(Color[] expected, Color[] actual, string label, float tolerance = .004f)
        {
            t.Equal(expected.Length, actual.Length, label + " size");
            double worst = 0;
            for (int i = 0; i < expected.Length; i++) for (int c = 0; c < 4; c++)
            {
                t.True(!float.IsNaN(actual[i][c]) && !float.IsInfinity(actual[i][c]), label + " finite");
                worst = Math.Max(worst, Math.Abs(actual[i][c] - expected[i][c]));
            }
            t.Near(0, worst, tolerance, label);
        }

        Color Sample(Vector2 uv, int tiling, FilterMode filter)
        {
            if (tiling == 3 && (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1)) return Color.clear;
            int Address(int index, int count)
            {
                if (tiling == 1) return (index % count + count) % count;
                if (tiling == 2) { int p = (index % (count * 2) + count * 2) % (count * 2); return p < count ? p : count * 2 - p - 1; }
                return Mathf.Clamp(index, 0, count - 1);
            }
            Color At(int x, int y) => pixels[Address(y, 5) * 7 + Address(x, 7)];
            if (filter == FilterMode.Point) return At(Mathf.FloorToInt(uv.x * 7), Mathf.FloorToInt(uv.y * 5));
            float px = uv.x * 7 - .5f, py = uv.y * 5 - .5f;
            int x0 = Mathf.FloorToInt(px), y0 = Mathf.FloorToInt(py); float wx = px - x0, wy = py - y0;
            return Color.LerpUnclamped(Color.LerpUnclamped(At(x0, y0), At(x0 + 1, y0), wx),
                Color.LerpUnclamped(At(x0, y0 + 1), At(x0 + 1, y0 + 1), wx), wy);
        }

        public void CheckSampling()
        {
            var probe = Create("// @param float2 _Coordinate\n// @param float _InputTiling = 0\n" +
                "float4 ApplyFX(float2 uv, float4 color) { return SampleInput(_Coordinate, _InputTiling); }");
            var shader = Material(probe).shader;
            var coordinates = new[] { new Vector2(-2.17f, 3.23f), new Vector2(-.013f, .371f),
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(1.013f, -.371f), new Vector2(.999f, .999f), new Vector2(.001f, .001f),
                new Vector2(.413f, .673f), new Vector2(2, -1), new Vector2(-1, 2) };
            foreach (var filter in new[] { FilterMode.Point, FilterMode.Bilinear, FilterMode.Trilinear })
            foreach (int tiling in new[] { 0, 1, 2, 3 }) foreach (var uv in coordinates)
            {
                P(probe, "_Coordinate").vectorValue = uv; Set(probe, "_InputTiling", tiling);
                var actual = Render(probe, filter); var expected = Sample(uv, tiling, filter);
                float tolerance = tiling == 1 && filter != FilterMode.Point ? .00003f : .004f;
                foreach (var pixel in actual) for (int c = 0; c < 4; c++)
                    t.Near(expected[c], pixel[c], tolerance, $"CPU sampler: {tiling}, {filter}, {uv}, channel {c}");
            }
            t.Equal(shader, Material(probe).shader, "Tiling changes do not compile another shader");
            t.Equal(TextureWrapMode.Clamp, input.wrapMode, "Sampling never mutates the input texture's wrap mode");

            P(probe, "_Coordinate").vectorValue = new Vector2(.413f, .673f); Set(probe, "_InputTiling", 1);
            foreach (var filter in new[] { FilterMode.Point, FilterMode.Bilinear, FilterMode.Trilinear })
            {
                var current = RenderTexture.GetTemporary(7, 5, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                try
                {
                    input.filterMode = FilterMode.Point; GL.sRGBWrite = false; Graphics.Blit(input, current);
                    current.wrapMode = TextureWrapMode.Clamp; current.filterMode = filter;
                    Layer layer = new ColorFillLayerBehaviour(); layer.fx.Add(probe);
                    object[] args = { current, context, filter, int.MaxValue };
                    typeof(Layer).GetMethod("ApplyFx", F).Invoke(layer, args); current = (RenderTexture)args[0];
                    t.Near(filter == FilterMode.Point ? 0 : 1, Material(probe).GetFloat("_WhimTex_InputFilter"), 0,
                        "Real FX pipeline uploads the current input filter");
                    var expected = Sample(new Vector2(.413f, .673f), 1, filter);
                    foreach (var pixel in Read(current)) for (int c = 0; c < 4; c++)
                        t.Near(expected[c], pixel[c], .0005, "Actual layer sampler retains Point/Bilinear behavior through FP16 FinishStage");
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(current); }
            }
        }

        public void CheckPresets()
        {
            var host = Own(ScriptableObject.CreateInstance<InputTilingTestWindow>());
            host.titleContent = new GUIContent("WhimTex input tiling test"); host.ShowUtility();
            var paths = new[] { "Transform/UV Transform", "Distortion/Spherize", "Distortion/Twirl",
                "Distortion/Radial Shear", "Distortion/Polar Coordinates", "Distortion/Displacement Map" };
            var catalog = assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog", true);
            foreach (var entry in (IEnumerable)catalog.GetMethod("GetEntries", F).Invoke(null, null))
            {
                string path = (string)entry.GetType().GetField("menuPath", F).GetValue(entry);
                if (Array.IndexOf(paths, path) < 0) continue;
                presets.Add(path, Own((ShaderFX)typeof(ShaderFX).GetMethod("FromCatalog", F).Invoke(null, new object[] { doc, entry })));
            }
            t.Equal(6, presets.Count, "All transform/distortion presets found");
            var source = Create("float4 ApplyFX(float2 uv, float4 color) { return float4(uv, frac(uv.x * 5 + uv.y * 3), 1); }");
            foreach (var pair in presets)
            {
                var fx = pair.Value; string name = pair.Key;
                string parameter = name.StartsWith("Transform/") ? "_InputTiling" : "_Tiling";
                var p = P(fx, parameter); t.True(p != null, "Input tiling is declared: " + name);
                t.Equal(ShaderFXParameterType.Enum, p.controls[0].type, "Tiling uses the shared enum field");
                t.Near(name.StartsWith("Transform/") ? 3 : 0, p.floatValue, 0, "Previous default preserved: " + name);
                t.Equal("Clamp,Repeat,Mirror,Clip", string.Join(",", p.controls[0].optionNames), "Uniform tiling choices");
                var viewType = assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView", true);
                var view = (VisualElement)Activator.CreateInstance(viewType, F, null, new object[] { fx, false }, null);
                host.rootVisualElement.Add(view);
                try
                {
                    t.True(view.panel != null, "UI test uses an attached Editor panel");
                    var fields = view.Query<DropdownField>().ToList();
                    string label = name.StartsWith("Transform/") ? "Input Tiling" : "Tiling";
                    var field = fields.Find(item => item.label == label);
                    t.True(field != null, "One standard tiling dropdown: " + name);
                    t.Equal(1, fields.FindAll(item => item.label == label).Count, "No duplicate addressing field");
                    for (var parent = field.parent; parent != view; parent = parent.parent)
                        t.True(!(parent is Foldout), "Tiling remains outside transform foldouts");
                    field.value = "Mirror";
                    t.Near(2, P(fx, parameter).floatValue, 0, "Attached UI field updates the actual stored value");
                    t.Equal(0, view.Query<Toggle>().ToList().FindAll(item => item.label == "Repeat Filtering").Count,
                        "File compatibility filtering is not exposed as another user control");
                }
                finally { view.RemoveFromHierarchy(); view.Clear(); }
                if (name.EndsWith("Displacement Map"))
                {
                    Set(fx, "_StrengthX", 5.2f); Set(fx, "_StrengthY", -4.4f);
                    t.Near(1, P(fx, "_RepeatFiltering").floatValue, 0, "New displacement uses filtered repeat seams");
                }
                if (name.EndsWith("Radial Shear")) P(fx, "_Offset").vectorValue = new Vector2(.27f, -.23f);
                if (name.StartsWith("Transform/"))
                {
                    var frame = ShaderFXTransform.Default; frame.position = new Double2(.7, .4); frame.size = new Double2(.5, .7);
                    P(fx, "_Area").transformValue = frame;
                }
                var shader = Material(fx).shader;
                foreach (int tiling in new[] { 0, 1, 2, 3 })
                {
                    Set(fx, parameter, tiling);
                    string exported = (string)writer.Invoke(null, new object[] { fx, "Tests/Input Tiling" });
                    var copy = Create(exported);
                    t.Near(tiling, P(copy, parameter).floatValue, 0, "HLSL preset retains tiling: " + name);
                    Same(Render(fx), Render(copy), "HLSL render parity: " + name, .0001f);
                    Layer child = new ColorFillLayerBehaviour(); child.fx.Add(source); child.fx.Add(fx);
                    doc.layers.Clear(); doc.layers.Add(child); var baseline = Composite();
                    child.fx.Remove(fx); Layer group = new GroupLayerBehaviour(); group.children.Add(child); group.fx.Add(fx);
                    doc.layers.Clear(); doc.layers.Add(group); Same(baseline, Composite(), "Group: " + name);
                    var thumbnail = (RenderTexture)typeof(TextureCompositor).GetMethod("RenderAgentLayerPreview", F).Invoke(doc, new object[] { group, W });
                    try { Same(baseline, Read(thumbnail), "Thumbnail: " + name, .008f); }
                    finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(thumbnail); }
                    var image = (Texture2D)typeof(TextureCompositor).GetMethod("RenderPsdGroupContent", F).Invoke(doc, new object[] { group });
                    try
                    {
                        var display = (Color[])baseline.Clone(); for (int i = 0; i < display.Length; i++) display[i] = display[i].gamma;
                        Same(display, image.GetPixels(), "Layered export: " + name, .008f);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                    group.clippingMask = true; doc.layers.Add(new ColorFillLayerBehaviour { color = Color.white });
                    var overWhite = (Color[])baseline.Clone();
                    for (int i = 0; i < overWhite.Length; i++)
                    {
                        float alpha = Mathf.Clamp01(baseline[i].a);
                        // Standard blending uses sRGB color and preserves the opaque clipping base.
                        for (int c = 0; c < 3; c++) overWhite[i][c] = Mathf.GammaToLinearSpace(
                            Mathf.Lerp(1, Mathf.LinearToGammaSpace(baseline[i][c]), alpha));
                        overWhite[i].a = 1;
                    }
                    Same(overWhite, Composite(), "Clipped group: " + name, .008f);
                    group.clippingMask = false; doc.layers.RemoveAt(1); group.enabled = false;
                    Layer target = new BlurLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = group.Id, radius = 0 };
                    doc.layers.Insert(0, target); Same(baseline, Composite(), "Target: " + name, .008f);
                    doc.layers.Clear(); group.enabled = true; doc.layers.Add(group);
                    var json = WhimTexDocumentJson.Write(doc); t.Equal(0, json.Warnings.Count, "JSON writes without warnings");
                    using (var restored = WhimTexDocumentJson.Read(json.Json))
                    {
                        t.Equal(0, restored.Warnings.Count, "JSON loads without warnings");
                        var restoredFx = (ShaderFX)restored.Document.layers[0].fx[0];
                        t.Near(tiling, P(restoredFx, parameter).floatValue, 0, "JSON retains tiling");
                        var rendered = restored.Document.ComposeCanvas();
                        try { Same(baseline, rendered.GetPixels(), "JSON render: " + name, .008f); }
                        finally { UnityEngine.Object.DestroyImmediate(rendered); }
                    }
                }
                t.Equal(shader, Material(fx).shader, "Tiling edits do not compile a shader: " + name);
            }
        }

        Color[] Composite()
        {
            var active = RenderTexture.active; bool write = GL.sRGBWrite;
            var image = doc.ComposeCanvas();
            try { t.True(active == RenderTexture.active && write == GL.sRGBWrite, "Composite restores caller GPU state"); return image.GetPixels(); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }

        public void CheckRenamedTiling()
        {
            var codeProperty = typeof(ShaderFX).GetProperty("Code", F);
            var guidField = typeof(ShaderFX).GetField("catalogGuid", F);
            var reload = typeof(ShaderFX).GetMethod("ReloadCatalogSource", F);
            foreach (var pair in presets)
            {
                if (pair.Key.StartsWith("Transform/")) continue;
                string oldName = pair.Key.EndsWith("Displacement Map") ? "_InputEdge" : "_InputTiling";
                string marker = "// @formerlyserializedas(" + oldName + ")";
                string source = (string)codeProperty.GetValue(pair.Value);
                t.True(source.Contains(marker), "Current preset declares its exact former tiling name: " + pair.Key);
                // A removed unrelated field makes counts differ, so positional rename inference cannot pass this test.
                string previousSource = source.Replace(marker, "").Replace("_Tiling", oldName) +
                    "\n// @param hidden float _RemovedRenameProbe = 0\n";
                foreach (int tiling in new[] { 0, 1, 2, 3 })
                {
                    var fx = Create(previousSource);
                    Set(fx, oldName, tiling);
                    string id = P(fx, oldName).id;
                    int previousCount = ((List<ShaderFXParameter>)parameters.GetValue(fx)).Count;
                    var expected = Render(fx);
                    guidField.SetValue(fx, guidField.GetValue(pair.Value));
                    reload.Invoke(fx, new object[] { true });
                    t.True(previousCount != ((List<ShaderFXParameter>)parameters.GetValue(fx)).Count,
                        "Rename must not depend on parameter positions");
                    var renamed = P(fx, "_Tiling");
                    t.True(renamed != null && P(fx, oldName) == null, "Old declaration becomes the current tiling field");
                    t.Near(tiling, renamed.floatValue, 0, "Former name preserves saved tiling: " + pair.Key);
                    t.Equal(id, renamed.id, "Former name preserves parameter identity");
                    Same(expected, Render(fx), "Former name preserves rendering: " + pair.Key, .0001f);
                    string exported = (string)writer.Invoke(null, new object[] { fx, "Tests/Renamed Tiling" });
                    t.True(exported.Contains(marker), "Preset export retains the rename marker");
                    var copy = Create(exported);
                    t.Near(tiling, P(copy, "_Tiling").floatValue, 0, "Exported preset preserves renamed tiling");
                    Same(expected, Render(copy), "Renamed preset export rendering", .0001f);
                }
            }
        }

        public void CheckFileCompatibility()
        {
            var fx = Create(AddressReference);
            var expected = new Color[4][];
            for (int tiling = 0; tiling < 4; tiling++) { Set(fx, "_InputEdge", tiling); expected[tiling] = Render(fx); }
            string parameterId = P(fx, "_InputEdge").id;
            Layer layer = new ColorFillLayerBehaviour(); layer.fx.Add(fx); doc.layers.Clear(); doc.layers.Add(layer);
            typeof(TextureCompositor).GetMethod("NormalizeModel", F).Invoke(doc, null);
            var oldJson = WhimTexDocumentJson.Write(doc);
            using (var detached = WhimTexDocumentJson.Read(oldJson.Json))
            {
                t.Equal(0, detached.Warnings.Count, "Original independent HLSL still loads");
                t.True(P((ShaderFX)detached.Document.layers[0].fx[0], "_RepeatFiltering") == null, "Detached code is not rewritten");
            }
            typeof(ShaderFX).GetField("catalogGuid", F).SetValue(fx, "7d755646c7a839e478c67bb36a2189f8");
            var reload = typeof(ShaderFX).GetMethod("ReloadCatalogSource", F); reload.Invoke(fx, new object[] { true });
            t.Near(0, P(fx, "_RepeatFiltering").floatValue, 0, "Linked files retain their previous repeat filtering");
            t.Equal(parameterId, P(fx, "_Tiling").id, "Saved addressing parameter identity preserved");
            t.Near(3, P(fx, "_Tiling").floatValue, 0, "Saved non-default addressing value survives the 0.12.5 upgrade");
            for (int tiling = 0; tiling < 4; tiling++)
            {
                Set(fx, "_Tiling", tiling); Same(expected[tiling], Render(fx), "Original addressing parity: " + tiling, .0001f);
            }
            var json = WhimTexDocumentJson.Write(doc);
            using (var restored = WhimTexDocumentJson.Read(json.Json))
            {
                t.Equal(0, restored.Warnings.Count, "Converted filtering loads without warnings");
                var copy = (ShaderFX)restored.Document.layers[0].fx[0];
                t.Near(0, P(copy, "_RepeatFiltering").floatValue, 0, "JSON preserves original filtering");
                Same(Render(fx), Render(copy), "Converted JSON rendering", .0001f);
            }
            var preset = Create((string)writer.Invoke(null, new object[] { fx, "Tests/Saved Input Tiling" }));
            t.Near(0, P(preset, "_RepeatFiltering").floatValue, 0, "HLSL export preserves original filtering");
            Same(Render(fx), Render(preset), "Converted HLSL rendering", .0001f);
            Set(fx, "_RepeatFiltering", 1); reload.Invoke(fx, new object[] { true });
            t.Near(1, P(fx, "_RepeatFiltering").floatValue, 0, "Repeat catalog refresh does not reapply conversion");
            t.Near(0, P(presets["Distortion/Displacement Map"], "_MapWrap").floatValue, 0, "Input tiling leaves map wrap independent");
        }

        public void Dispose()
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(output);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null)
            {
                if (owned[i] is EditorWindow window) { window.DiscardChanges(); window.Close(); }
                if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            }
        }
    }
}

public sealed class InputTilingTestWindow : EditorWindow { }
