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

public static class DistortionControlsTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const int W = 33, H = 25;

    public static string Run() => TestContext.Run("Distortion header controls", t =>
    {
        using (var fixture = new MigrationD()) Execute(t);
    });

    static void Execute(TestContext t)
    {
        var owned = new List<UnityEngine.Object>();
        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        var doc = Own(ScriptableObject.CreateInstance<TextureCompositor>());
        doc.width = W; doc.height = H;
        var input = Own(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0 });
        var map = Own(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
        map.SetPixel(0, 0, new Color(.8f, .25f, .4f, 1)); map.Apply();
        var read = Own(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true));
        var pixels = new Color[W * H];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            pixels[y * W + x] = new Color((x + .5f) / W, (y + .5f) / H, ((x * 3 + y) % 7) / 6f, ((x + y * 2) % 11) / 10f);
        input.SetPixels(pixels); input.Apply();
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var output = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var assembly = typeof(ShaderFX).Assembly;
        var parametersField = typeof(ShaderFX).GetField("parameters", F);
        var shaderField = typeof(ShaderFX).GetField("compiledShader", F);
        var getMaterial = typeof(ShaderFX).GetMethod("GetMaterial", F);
        var notify = typeof(ShaderFX).GetMethod("NotifyValuesChanged", F);
        var draft = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
            new[] { typeof(TextureCompositor), typeof(string), typeof(List<ShaderFXParameter>) }, null);
        var apply = typeof(ShaderFX).GetMethod("ApplyAgentDraft", F);
        var viewType = assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView", true);
        var refresh = viewType.GetMethod("Refresh", F);
        var writer = assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter", true).GetMethod("BuildSource", F);
        var renderContext = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true),
            doc, null, W, H, 1f, true, true, null);
        ShaderFXParameter P(ShaderFX fx, string name) => ((List<ShaderFXParameter>)parametersField.GetValue(fx)).Find(p => p.name == name);
        void Set(ShaderFX fx, string name, float value) { P(fx, name).floatValue = value; notify.Invoke(fx, null); }
        ShaderFX Create(string code)
        {
            var fx = Own((ShaderFX)draft.Invoke(null, new object[] { doc, code, new List<ShaderFXParameter>() }));
            apply.Invoke(fx, null); return fx;
        }
        void Same(Color[] expected, Color[] actual, string label, float tolerance = .002f)
        {
            t.Equal(expected.Length, actual.Length, label + " dimensions");
            double worst = 0;
            for (int i = 0; i < expected.Length; i++) for (int c = 0; c < 4; c++)
            {
                t.True(!float.IsNaN(actual[i][c]) && !float.IsInfinity(actual[i][c]), label + " finite RGBA");
                worst = Math.Max(worst, Math.Abs(expected[i][c] - actual[i][c]));
            }
            t.Near(0, worst, tolerance, label);
        }
        Color[] Render(ShaderFX fx)
        {
            var material = (Material)getMaterial.Invoke(fx, new[] { renderContext });
            t.True(material != null && !ShaderUtil.ShaderHasError(material.shader), "Shader compiles");
            if (P(fx, "_DisplacementMap") != null)
            {
                material.SetTexture("_DisplacementMap", map);
                material.SetTexture("_StrengthMask", map);
            }
            GL.sRGBWrite = false; Graphics.Blit(input, output, material);
            RenderTexture.active = output; read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply();
            return read.GetPixels();
        }
        Color[] Composite()
        {
            var active = RenderTexture.active; bool write = GL.sRGBWrite;
            var image = doc.ComposeCanvas();
            try
            {
                t.True(RenderTexture.active == active && GL.sRGBWrite == write, "Composite restores caller GPU state");
                return image.GetPixels();
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        try
        {
            var bindings = new Dictionary<string, string> { { "Spherize", "_Strength" }, { "Twirl", "_Angle" },
                { "Radial Shear", "_Strength" }, { "Polar Coordinates", "_Amount" }, { "Displacement Map", "_Amount" } };
            var effects = new Dictionary<string, ShaderFX>();
            var defaults = new Dictionary<string, float>();
            var catalog = assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog", true);
            foreach (var entry in (IEnumerable)catalog.GetMethod("GetEntries", F).Invoke(null, null))
            {
                string path = (string)entry.GetType().GetField("menuPath", F).GetValue(entry);
                if (!path.StartsWith("Distortion/", StringComparison.Ordinal)) continue;
                string name = path.Substring("Distortion/".Length);
                t.True(bindings.ContainsKey(name), "Every built-in distortion must declare a tested strength binding: " + name);
                var fx = Own((ShaderFX)typeof(ShaderFX).GetMethod("FromCatalog", F).Invoke(null, new object[] { doc, entry }));
                effects.Add(name, fx);
                string controlName = bindings[name];
                string code = (string)typeof(ShaderFX).GetProperty("Code", F).GetValue(fx);
                t.Equal("// @control(" + controlName + ")", code.Split('\n')[1].TrimEnd('\r'), "Header directive follows catalog marker");
                var parameter = P(fx, controlName);
                t.Equal(1, parameter.controls.Count, "Header points to one scalar control");
                t.Equal(ShaderFXParameterType.Float, parameter.controls[0].type, "Header is a numeric strength control");
                defaults.Add(name, parameter.floatValue);
                var header = (VisualElement)Activator.CreateInstance(viewType, F, null, new object[] { fx, true }, null);
                try
                {
                    t.Equal(1, header.Query<FloatField>().ToList().Count, "Actual FX header has one numeric field");
                    t.True(!string.IsNullOrEmpty(header.tooltip), "Header explains control units and zero behavior");
                    foreach (float value in new[] { 0f, defaults[name] * .4f, defaults[name] })
                    {
                        Set(fx, controlName, value); refresh.Invoke(header, null);
                        t.Near(value, header.Q<FloatField>().value, 0, "Header follows the shared parameter value");
                    }
                }
                finally { header.Clear(); }
                Set(fx, controlName, defaults[name] * .4f);
                string exported = (string)writer.Invoke(null, new object[] { fx, "Tests/Distortion Control" });
                var copy = Create(exported);
                t.Equal("// @control(" + controlName + ")", exported.Split('\n')[1].TrimEnd('\r'), "Preset export retains header binding");
                t.Near(P(fx, controlName).floatValue, P(copy, controlName).floatValue, 0, "Preset export stores the edited default");
                Same(Render(fx), Render(copy), "Preset export render parity: " + name);
                Set(fx, controlName, 0);
                Same(pixels, Render(fx), "Zero distortion is identity with default ancillary settings: " + name);
            }
            t.Equal(5, effects.Count, "All five built-in distortion presets covered");

            var displacement = effects["Displacement Map"];
            Set(displacement, "_MapChannel", 1); Set(displacement, "_Direction", 0);
            Set(displacement, "_ViewAngle", 0); Set(displacement, "_ViewElevation", 60);
            var constantOffset = Create("// @param float2 _Offset\nfloat4 ApplyFX(float2 uv, float4 color) { return SampleInput(uv + _Offset * _CanvasSize.zw); }");
            foreach (int mode in new[] { 0, 1, 2 })
            {
                Set(displacement, "_Mode", mode); Set(displacement, "_Tiling", 0); Set(displacement, "_MaskSource", 0);
                Set(displacement, "_StrengthX", 5.2f); Set(displacement, "_StrengthY", -4.4f);
                Set(displacement, "_Strength", 5.2f); Set(displacement, "_Depth", 6.8f); Set(displacement, "_Amount", 1);
                // A constant height field intersects at (1-height) of the virtual ray depth.
                float dx = mode == 2 ? -6.8f * .2f / Mathf.Tan(60 * Mathf.Deg2Rad) : .6f * 5.2f;
                float dy = mode == 0 ? -.5f * -4.4f : 0;
                P(constantOffset, "_Offset").vectorValue = new Vector2(dx, dy);
                Same(Render(constantOffset), Render(displacement), "Unscaled displacement matches an independent constant-field offset: mode " + mode);
                foreach (int edge in new[] { 0, 1, 2, 3 }) foreach (int mask in new[] { 0, 1, 2, 3 })
                {
                    Set(displacement, "_Tiling", edge); Set(displacement, "_MaskSource", mask);
                    foreach (float amount in new[] { .4f, 1f, 2.5f })
                    {
                        Set(displacement, "_Amount", amount);
                        Set(displacement, "_StrengthX", 5.2f); Set(displacement, "_StrengthY", -4.4f);
                        Set(displacement, "_Strength", 5.2f); Set(displacement, "_Depth", 6.8f);
                        var actual = Render(displacement);
                        Set(displacement, "_Amount", 1);
                        Set(displacement, "_StrengthX", 5.2f * amount); Set(displacement, "_StrengthY", -4.4f * amount);
                        Set(displacement, "_Strength", 5.2f * amount); Set(displacement, "_Depth", 6.8f * amount);
                        Same(Render(displacement), actual, $"Amount scales configured displacement: mode {mode}, edge {edge}, mask {mask}, amount {amount}");
                    }
                    Set(displacement, "_Amount", 0);
                    Same(pixels, Render(displacement), "Zero Amount bypasses edge addressing, masks and parallax");
                }
            }

            var polar = effects["Polar Coordinates"];
            var inputParameter = P(polar, "_Input");
            t.Equal(ShaderFXParameterType.Transform2D, inputParameter.type, "Input is a complete source frame");
            t.Equal(ShaderFXTransform.Default, inputParameter.transformValue, "Default input is neutral");
            t.True(P(polar, "_Center") == null, "The provisional point is replaced, not kept as a second source control");
            var body = (VisualElement)Activator.CreateInstance(viewType, F, null, new object[] { polar, false }, null);
            try
            {
                var folds = body.Query<Foldout>().ToList();
                t.Equal(2, folds.Count, "Two frame controls");
                t.True(folds.Exists(field => field.text == "Input") && folds.Exists(field => field.text == "Output"), "Input and output frames use shared transform UI");
                t.True(folds.TrueForAll(field => !field.value), "Both frames start collapsed");
                t.Equal(2, body.Query<Button>("editFXTransform").ToList().Count, "Each frame has Edit on Canvas");
                t.Equal(2, body.Query<Button>("resetFXTransform").ToList().Count, "Each frame has reset");
                t.Equal(0, body.Query<Button>("editFXPoint").ToList().Count, "No obsolete point handle");
            }
            finally { body.Clear(); }
            var coordinateInput = new Color[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                coordinateInput[y * W + x] = new Color((x + .5f) / W, (y + .5f) / H, 0, 1);
            input.SetPixels(coordinateInput); input.Apply();
            var translated = ShaderFXTransform.Default; translated.position = new Double2(.27, .68);
            var outside = ShaderFXTransform.Default; outside.position = new Double2(1.2, -.25);
            var rotated = ShaderFXTransform.Default; rotated.position = new Double2(.4, .65);
            rotated.size = new Double2(.7, 1.3); rotated.rotation = -31;
            var mirrored = ShaderFXTransform.Default; mirrored.size = new Double2(-.8, .6); mirrored.rotation = 17;
            var projective = ShaderFXTransform.Default; projective.storage = TransformStorage.Projective;
            projective.matrix = new ProjectiveMatrix { m00 = .9, m01 = .1, m02 = .04, m10 = -.05, m11 = 1.1, m12 = -.1, m20 = .2, m21 = -.15, m22 = 1 };
            object[] Rows(ShaderFXTransform frame)
            {
                object[] rows = { new Vector2(W, H), null, null, null, null, null, null };
                typeof(ShaderFXTransform).GetMethod("GetRows", F).Invoke(frame, rows); return rows;
            }
            Vector2 TransformPoint(Vector2 point, object[] rows, int first)
            {
                var a = (Vector4)rows[first]; var b = (Vector4)rows[first + 1]; var c = (Vector4)rows[first + 2];
                float w = c.x * point.x + c.y * point.y + c.z;
                return new Vector2((a.x * point.x + a.y * point.y + a.z) / w, (b.x * point.x + b.y * point.y + b.z) / w);
            }
            object polarShader = shaderField.GetValue(polar);
            foreach (var sourceFrame in new[] { ShaderFXTransform.Default, translated, outside, rotated, mirrored, projective })
            foreach (bool transformed in new[] { false, true })
            {
                inputParameter.transformValue = sourceFrame;
                var area = ShaderFXTransform.Default;
                if (transformed)
                {
                    area.position = new Double2(.4, .6); area.size = new Double2(.7, 1.2); area.rotation = 23;
                }
                P(polar, "_Output").transformValue = area;
                var outputRows = Rows(area); var inputRows = Rows(sourceFrame);
                foreach (int mode in new[] { 0, 1 })
                {
                    Set(polar, "_Mode", mode); Set(polar, "_AngleOffset", 37); Set(polar, "_RadialOffset", .2f);
                    foreach (float amount in new[] { 0f, .4f, 1f })
                    {
                        Set(polar, "_Amount", amount); var actual = Render(polar);
                        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                        {
                            var uv = new Vector2((x + .5f) / W, (y + .5f) / H);
                            var local = TransformPoint(uv, outputRows, 1);
                            Vector2 mapped;
                            if (mode == 0)
                            {
                                var p = (local - Vector2.one * .5f) * 2;
                                float angle = p.sqrMagnitude > 0 ? Mathf.Atan2(p.y, p.x) / (2 * Mathf.PI) : 0;
                                mapped = new Vector2(Mathf.Repeat(angle - 37f / 360, 1), p.magnitude - .2f);
                            }
                            else
                            {
                                float angle = (local.x + 37f / 360) * 2 * Mathf.PI, radius = local.y + .2f;
                                mapped = Vector2.one * .5f + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * .5f;
                            }
                            mapped = TransformPoint(mapped, inputRows, 4);
                            var sample = Vector2.Lerp(uv, mapped, amount);
                            // Bilinear sampling may quantize weights; keep tolerance well below a texel.
                            const float samplingTolerance = .0003f;
                            t.True(!float.IsNaN(actual[y * W + x].r) && !float.IsInfinity(actual[y * W + x].r) &&
                                !float.IsNaN(actual[y * W + x].g) && !float.IsInfinity(actual[y * W + x].g), "Polar frame sampling stays finite");
                            // The center has no unique angle. Input rotation/projective mapping can
                            // propagate tiny CPU/GPU angle differences into both source coordinates.
                            if (mode != 0 || amount == 0 || (local - Vector2.one * .5f).sqrMagnitude > 1e-8f)
                            {
                                t.Near(Mathf.Clamp(sample.x, .5f / W, 1 - .5f / W), actual[y * W + x].r, samplingTolerance,
                                    $"Polar sampling X: input {sourceFrame.position}, size {sourceFrame.size}, output transform {transformed}, mode {mode}, amount {amount}, pixel {x},{y}");
                                t.Near(Mathf.Clamp(sample.y, .5f / H, 1 - .5f / H), actual[y * W + x].g, samplingTolerance,
                                    $"Polar sampling Y: input {sourceFrame.position}, size {sourceFrame.size}, output transform {transformed}, mode {mode}, amount {amount}, pixel {x},{y}");
                            }
                        }
                    }
                }
            }
            t.True(ReferenceEquals(polarShader, shaderField.GetValue(polar)), "Editing either frame or mode does not recompile the shader");
            var polarCopy = Create((string)writer.Invoke(null, new object[] { polar, "Tests/Polar Frames" }));
            t.Equal(inputParameter.transformValue, P(polarCopy, "_Input").transformValue, "Preset export preserves the full input frame");
            t.Equal(P(polar, "_Output").transformValue, P(polarCopy, "_Output").transformValue, "Preset export preserves the output frame");
            Same(Render(polar), Render(polarCopy), "Polar frame preset export render parity");

            var source = Create("float4 ApplyFX(float2 uv, float4 color) { return float4(uv, frac(uv.x * 5 + uv.y * 3), 1); }");
            foreach (var pair in effects)
            {
                var fx = pair.Value;
                Set(fx, bindings[pair.Key], defaults[pair.Key] * .4f);
                if (pair.Key == "Displacement Map")
                {
                    Set(fx, "_Mode", 0); Set(fx, "_Tiling", 0); Set(fx, "_MaskSource", 0);
                    Set(fx, "_StrengthX", 4.3f); Set(fx, "_StrengthY", 3.7f);
                }
                Layer child = new ColorFillLayerBehaviour(); child.fx.Add(source); child.fx.Add(fx);
                doc.layers.Clear(); doc.layers.Add(child); var baseline = Composite();
                child.fx.Remove(fx); Layer group = new GroupLayerBehaviour(); group.children.Add(child); group.fx.Add(fx);
                doc.layers.Clear(); doc.layers.Add(group);
                Same(baseline, Composite(), "Group input matches ordinary layer: " + pair.Key);
                var thumbnail = (RenderTexture)typeof(TextureCompositor).GetMethod("RenderAgentLayerPreview", F).Invoke(doc, new object[] { group, W });
                try
                {
                    RenderTexture.active = thumbnail; read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply();
                    Same(baseline, read.GetPixels(), "Group thumbnail: " + pair.Key, .008f);
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(thumbnail); }
                var exported = (Texture2D)typeof(TextureCompositor).GetMethod("RenderPsdGroupContent", F).Invoke(doc, new object[] { group });
                try
                {
                    var display = (Color[])baseline.Clone(); for (int i = 0; i < display.Length; i++) display[i] = display[i].gamma;
                    Same(display, exported.GetPixels(), "Layered group export: " + pair.Key, .008f);
                }
                finally { UnityEngine.Object.DestroyImmediate(exported); }
                group.clippingMask = true; doc.layers.Add(new ColorFillLayerBehaviour { color = Color.white });
                Same(baseline, Composite(), "Clipped group: " + pair.Key, .008f);
                group.clippingMask = false; doc.layers.RemoveAt(1); group.enabled = false;
                Layer target = new BlurLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = group.Id, radius = 0 };
                doc.layers.Insert(0, target); Same(baseline, Composite(), "Target input: " + pair.Key, .008f);
                doc.layers.Clear(); group.enabled = true; doc.layers.Add(group);
                var json = WhimTexDocumentJson.Write(doc);
                t.Equal(0, json.Warnings.Count, "JSON write preserves distortion");
                using (var restored = WhimTexDocumentJson.Read(json.Json))
                {
                    t.Equal(0, restored.Warnings.Count, "JSON read has no warnings");
                    var restoredFx = (ShaderFX)restored.Document.layers[0].fx[0];
                    t.Near(P(fx, bindings[pair.Key]).floatValue, P(restoredFx, bindings[pair.Key]).floatValue, 0, "Header value survives JSON");
                    if (pair.Key == "Polar Coordinates")
                    {
                        t.Equal(inputParameter.transformValue, P(restoredFx, "_Input").transformValue, "Input frame survives JSON");
                        t.Equal(P(polar, "_Output").transformValue, P(restoredFx, "_Output").transformValue, "Output frame survives JSON");
                    }
                    var image = restored.Document.ComposeCanvas();
                    try { Same(baseline, image.GetPixels(), "JSON render parity: " + pair.Key, .008f); }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                }
                foreach (var message in ShaderUtil.GetShaderMessages((Shader)shaderField.GetValue(fx)))
                    t.True(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error &&
                        message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning, message.message);
            }
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(output);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
        }
    }
}
