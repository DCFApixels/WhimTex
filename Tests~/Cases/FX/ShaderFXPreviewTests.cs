using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

public static class ShaderFXPreviewTests
{
    const BindingFlags F = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Assembly A = typeof(ShaderFX).Assembly;
    static Type Type(string name) => A.GetType("DCFApixels.WhimTex." + name, true);

    public static string Run() => TestContext.Run("FX preview metadata, signatures, export and GPU", test =>
    {
        ExplicitBindings(test);
        BundledPreviews(test);
        const string code = @"
// @param bool _Enabled = true
// @group(Preview)
// @param preview(ShowValue) float _Amount = 0.25 [-1 .. 1]
// @param preview(ShowUV) float2 _Offset = (0.1, 0.2)
// @if _Enabled == 1
// @header(Input)
// @preview(ShowInput)
// @endif
// @endgroup
#ifdef WHIMTEX_PREVIEW
float4 ShowValue(float2 uv, float value) { return float4(value, _Amount, uv.y, 1); }
float4 ShowUV(float2 uv) { return float4(uv, _Offset.x, 1); }
float4 ShowInput(float2 uv) { return SampleInput(uv); }
#endif
float4 ApplyFX(float2 uv, float4 color) { return color; }
";
        var parse = Type("ShaderFXMetadata").GetMethod("ParseWithPreviews", F);
        object[] args = { code, false, null, null };
        var parameters = (List<ShaderFXParameter>)parse.Invoke(null, args);
        var previews = (List<ShaderFXParameterControl>)args[3];
        test.Equal(3, parameters.Count, "Preview is not a shader parameter");
        test.Equal(1, previews.Count, "Standalone preview parsed");
        test.Equal("ShowValue", parameters[1].controls[0].previewFunction, "Inline preview parsed");
        test.Equal("_Enabled", previews[0].visibleIfParameter, "Standalone conditions retained");
        test.Equal("Input", previews[0].headers[0], "Standalone header retained");
        test.True(previews[0].inGroup, "Standalone group retained");

        var call = Type("ShaderFXPreviewFunction").GetMethod("Call", F);
        string Call(string source, ShaderFXParameter parameter, ShaderFXParameterType kind) =>
            (string)call.Invoke(null, new object[] { source, "P", parameter, kind, null, null });
        string UV(string kind) => "float4 P(float2 uv, " + kind + " value) { return 1; }";
        void Reject(string source, ShaderFXParameter parameter, ShaderFXParameterType kind, string label)
        {
            bool rejected = false;
            try { Call(source, parameter, kind); }
            catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            test.True(rejected, label);
        }
        test.Equal("P(input.uv)", Call("float4 P(float2 anything) { return 1; }", null, 0), "UV-only standalone");
        foreach (ShaderFXParameterType kind in Enum.GetValues(typeof(ShaderFXParameterType)))
        {
            string valueType = (string)Type("ShaderFXPreviewFunction").GetMethod("ValueType", F).Invoke(null, new object[] { kind });
            var parameter = new ShaderFXParameter { name = "_V", type = kind };
            test.True(Call(UV(valueType), parameter, kind).StartsWith("P(input.uv, "), "Typed value: " + kind);
            test.Equal(Call(UV(valueType), parameter, kind), (string)call.Invoke(null, new object[] {
                UV(valueType), "P", parameter, kind, new[] { "_V" }, new[] { parameter } }), "Explicit typed argument: " + kind);
            test.Equal("P(input.uv)", Call("float4 P(float2 uv) { return 1; }", parameter, kind), "Optional value: " + kind);
        }
        Reject("float4 P(float value, float2 uv) { return 1; }", parameters[1], ShaderFXParameterType.Float, "UV must be first");
        Reject(UV("float3"), parameters[1], ShaderFXParameterType.Float, "Reject implicit value conversion");
        Reject(UV("float"), null, 0, "Standalone has no parameter value");
        Reject("float4 P(float2 uv, float v) {return 1;} float4 P(float2 uv) {return 1;}", parameters[1], 0, "Reject overloaded entry");
        foreach (string invalid in new[] { "// @preview()", "// @preview(A,)", "// @param preview(A) preview(B) float _A",
            "// @param preview(A, B.x) float _A", "// @if _Missing == 1\n// @preview(P)\n// @endif" })
        {
            bool rejected = false;
            try { parse.Invoke(null, new object[] { invalid, false, null, null }); }
            catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            test.True(rejected, "Invalid preview metadata rejected: " + invalid);
        }

        ShaderFX effect = null;
        Shader main = null, previewShader = null;
        Material material = null;
        Texture2D input = null, pixels = null;
        RenderTexture target = null;
        var active = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { document, code, parameters });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(effect, null);
            main = (Shader)typeof(ShaderFX).GetProperty("PreviewCompiledShader", F).GetValue(effect);
            test.True(main != null && main.isSupported, "Production FX compiles without preview-only functions");
            string exported = (string)Type("ShaderFXPresetWriter").GetMethod("BuildSource", F)
                .Invoke(null, new object[] { effect, "Tests/Preview" });
            object[] exportedArgs = { exported, true, null, null };
            var exportedValues = (List<ShaderFXParameter>)parse.Invoke(null, exportedArgs);
            test.Equal(1, ((List<ShaderFXParameterControl>)exportedArgs[3]).Count, "Preset has one standalone preview");
            test.Equal("ShowValue", exportedValues[1].controls[0].previewFunction, "Preset retains inline preview");
            test.Equal("_Enabled", ((List<ShaderFXParameterControl>)exportedArgs[3])[0].visibleIfParameter, "Preset retains preview condition");
            var build = Type("ShaderFXSourceBuilder").GetMethod("BuildPreview", F);
            var declared = (IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(effect);
            var amount = declared[1];
            string source = (string)build.Invoke(null, new object[] { effect, amount.controls[0], amount });
            previewShader = ShaderUtil.CreateShaderAsset(source, true);
            previewShader.hideFlags = HideFlags.HideAndDontSave;
            material = new Material(previewShader) { hideFlags = HideFlags.HideAndDontSave };
            ShaderUtil.CompilePass(material, 0, true);
            foreach (var message in ShaderUtil.GetShaderMessages(previewShader))
                test.True(message.severity.ToString() != "Error", message.message);
            test.True(previewShader.isSupported, "Preview shader supported");
            target = new RenderTexture(64, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            material.SetFloat("_Amount", .25f);
            material.SetVector("_WhimTex_PreviewRange", new Vector4(0, 1, 1, 0));
            GL.sRGBWrite = false;
            Graphics.Blit(Texture2D.whiteTexture, target, material);
            pixels = new Texture2D(64, 16, TextureFormat.RGBAFloat, false, true);
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 64, 16), 0, 0); pixels.Apply();
            foreach (int x in new[] { 0, 31, 63 })
            {
                Color pixel = pixels.GetPixel(x, 8);
                test.Near((x + .5) / 64.0, pixel.r, .0001, "Slider candidate value at pixel " + x);
                test.Near(.25, pixel.g, .0001, "Real uniform remains unchanged");
            }
            string directory = "Temp/WhimTex/fx-preview";
            Directory.CreateDirectory(directory);
            var display = new Texture2D(64, 16, TextureFormat.RGBA32, false);
            try { display.SetPixels(pixels.GetPixels()); display.Apply(); File.WriteAllBytes(directory + "/slider.png", display.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(display); }
            material.SetVector("_WhimTex_PreviewRange", Vector4.zero);
            Graphics.Blit(Texture2D.whiteTexture, target, material);
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 64, 16), 0, 0); pixels.Apply();
            test.Near(.25, pixels.GetPixel(63, 8).r, .0001, "Non-slider preview receives the selected value");
            test.Near(.25, amount.floatValue, 0, "Preview did not change stored parameter");
            UnityEngine.Object.DestroyImmediate(material); material = null;
            UnityEngine.Object.DestroyImmediate(previewShader); previewShader = null;
            source = (string)build.Invoke(null, new object[] { effect, previews[0], null });
            previewShader = ShaderUtil.CreateShaderAsset(source, true);
            previewShader.hideFlags = HideFlags.HideAndDontSave;
            material = new Material(previewShader) { hideFlags = HideFlags.HideAndDontSave };
            ShaderUtil.CompilePass(material, 0, true);
            input = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            input.SetPixel(0, 0, new Color(.2f, .4f, .6f, .8f)); input.Apply();
            Graphics.Blit(input, target, material);
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 64, 16), 0, 0); pixels.Apply();
            var sampled = pixels.GetPixel(31, 8);
            test.Near(.2, sampled.r, .0001, "Standalone reads its input image");
            test.Near(.8, sampled.a, .0001, "Standalone preserves input alpha");
        }
        finally
        {
            RenderTexture.active = active; GL.sRGBWrite = srgb;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            foreach (UnityEngine.Object owned in new UnityEngine.Object[] { pixels, input, material, previewShader, effect, main, document })
                if (owned != null) UnityEngine.Object.DestroyImmediate(owned);
        }
    });

    static void ExplicitBindings(TestContext t)
    {
        const string code = @"
// @param preview(Pair, _Other, _Amount, _Tint) float _Amount = 0.25 [0 .. 1]
// @param hidden float _Other = 0.75
// @param color _Tint = (1, 0, 0, 1)
// @param preview(RangeTint, _Range, _Tint) float2 _Range = (0.1, 0.9) [0 .. 1]
// @preview(Pair, _Amount, _Other, _Tint)
#ifdef WHIMTEX_PREVIEW
float4 Pair(float2 uv, float first, float second, float4 tint) { return float4(first, second, tint.r, 1); }
float4 RangeTint(float2 uv, float2 range, float4 tint) { return float4(range, tint.r, 1); }
#endif
float4 ApplyFX(float2 uv, float4 color) { return color; }
";
        var parse = Type("ShaderFXMetadata").GetMethod("ParseWithPreviews", F);
        object[] args = { code, false, null, null };
        var declarations = (List<ShaderFXParameter>)parse.Invoke(null, args);
        var standalone = ((List<ShaderFXParameterControl>)args[3])[0];
        t.Equal("_Other,_Amount,_Tint", string.Join(",", declarations[0].controls[0].previewParameters), "Ordered forward and hidden references are retained");
        var clone = (ShaderFXParameter)typeof(ShaderFXParameter).GetMethod("Copy", F).Invoke(declarations[0], null);
        clone.controls[0].previewParameters[0] = "_Tint";
        t.Equal("_Other", declarations[0].controls[0].previewParameters[0], "Copy owns its preview argument array");
        var call = Type("ShaderFXPreviewFunction").GetMethod("Call", F);
        void Reject(string signature, string[] names, string message)
        {
            bool rejected = false;
            try { call.Invoke(null, new object[] { signature, "P", declarations[0], ShaderFXParameterType.Float, names, declarations }); }
            catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            t.True(rejected, message);
        }
        Reject("float4 P(float2 uv, float2 a) {return 1;}", new[] { "_Other" }, "Explicit arguments reject implicit conversions");
        Reject("float4 P(float2 uv, float4 a, float b) {return 1;}", new[] { "_Other", "_Tint" }, "Explicit argument order is strict");
        Reject("float4 P(float2 uv) {return 1;}", new[] { "_Other" }, "Explicit argument count is strict");
        Reject("float4 P(float2 uv, float a) {return 1;}", new[] { "_Missing" }, "Only declared @param names are accepted");
        Reject("float4 P(float a, float2 uv) {return 1;}", new[] { "_Other" }, "Explicit UV is first");
        t.Equal("P(input.uv, _Other, _Other)", (string)call.Invoke(null, new object[] {
            "float4 P(float2 uv, float a, float b) {return 1;}", "P", null, ShaderFXParameterType.Float,
            new[] { "_Other", "_Other" }, declarations }), "Repeated names are positional, not deduplicated");

        ShaderFX effect = null; Shader main = null;
        object cache = null;
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        var target = new RenderTexture(64, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(64, 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { document, code, declarations });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(effect, null);
            main = (Shader)typeof(ShaderFX).GetProperty("PreviewCompiledShader", F).GetValue(effect);
            t.True(main != null && main.isSupported, "Explicit preview metadata leaves production shader usable");
            var values = (IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(effect);
            string exported = (string)Type("ShaderFXPresetWriter").GetMethod("BuildSource", F).Invoke(null, new object[] { effect, "Tests/Explicit Preview" });
            object[] restoredArgs = { exported, true, null, null };
            var restored = (List<ShaderFXParameter>)parse.Invoke(null, restoredArgs);
            t.Equal("_Other,_Amount,_Tint", string.Join(",", restored[0].controls[0].previewParameters), "Preset retains inline binding order");
            t.Equal("_Amount,_Other,_Tint", string.Join(",", ((List<ShaderFXParameterControl>)restoredArgs[3])[0].previewParameters), "Preset retains standalone binding order");
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(effect), effect);
            values = (IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(effect);
            t.Equal("_Other,_Amount,_Tint", string.Join(",", values[0].controls[0].previewParameters), "Native roundtrip retains argument names");
            Layer layer = new ColorFillLayerBehaviour();
            typeof(Layer).GetMethod("AssignNewId", F).Invoke(layer, null);
            layer.fx.Add(effect); document.layers.Add(layer);
            using (var json = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json))
            {
                var restoredValues = (IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(json.Document.layers[0].fx[0]);
                t.Equal("_Other,_Amount,_Tint", string.Join(",", restoredValues[0].controls[0].previewParameters), "Portable JSON rebuilds explicit bindings from code");
            }
            document.layers.Clear();
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(effect, null);
            values = (IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters", F).GetValue(effect);
            var cacheType = Type("ShaderFXPreviewCache");
            cache = cacheType.GetMethod("Acquire", F).Invoke(null, new object[] { effect });
            object Program(ShaderFXParameterControl control, ShaderFXParameter parameter) =>
                cacheType.GetMethod("GetProgram", F).Invoke(cache, new object[] { control, parameter });
            var inline = Program(values[0].controls[0], values[0]);
            var separate = Program(standalone, null);
            t.True(!ReferenceEquals(inline, separate), "Different explicit argument expressions get separate cached programs");
            var identical = new ShaderFXParameterControl { previewFunction = "Pair", previewParameters = new[] { "_Amount", "_Other", "_Tint" } };
            t.True(ReferenceEquals(separate, Program(identical, null)), "Identical explicit calls share their compiled program");
            var pair = Program(values[3].controls[0], values[3]);
            t.True(pair.GetType().GetField("material", F).GetValue(pair) is Material, "Explicit float2 and float4 arguments compile together");
            var beforeShader = (Shader)typeof(ShaderFX).GetProperty("PreviewCompiledShader", F).GetValue(effect);
            t.True(beforeShader != null && beforeShader.isSupported, "Production FX remains usable after roundtrips");
            var objectId = Type("UnityObjectID").GetMethod("FromObject", F);
            var beforeInvalid = objectId.Invoke(null, new object[] { beforeShader });
            var bad = Program(new ShaderFXParameterControl { previewFunction = "Pair", previewParameters = new[] { "_Missing", "_Amount", "_Tint" } }, null);
            t.True(((string)bad.GetType().GetField("error", F).GetValue(bad)).Contains("_Missing"), "Unresolved names produce a local preview diagnostic");
            t.Equal(beforeInvalid, objectId.Invoke(null, new[] { typeof(ShaderFX).GetProperty("PreviewCompiledShader", F).GetValue(effect) }), "Invalid preview does not replace the production shader");
            t.True(target.Create(), "Explicit preview target created"); GL.sRGBWrite = false;
            Color Render(object program, ulong stamp, int x)
            {
                var material = (Material)program.GetType().GetField("material", F).GetValue(program);
                t.True(material != null, "Explicit preview program compiles");
                cacheType.GetMethod("Bind", F).Invoke(cache, new object[] { material, new Vector2(64, 1), 1f, stamp });
                material.SetVector("_WhimTex_PreviewRange", new Vector4(0, 1, 1, 0));
                Graphics.Blit(Texture2D.whiteTexture, target, material); RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 64, 1), 0, 0); readback.Apply(); return readback.GetPixel(x, 0);
            }
            foreach (int x in new[] { 0, 31, 63 })
            {
                Color pixel = Render(inline, 1, x);
                t.Near(.75, pixel.r, .0001, "Other explicit argument uses its actual value");
                t.Near((x + .5) / 64.0, pixel.g, .0001, "Own explicit argument retains slider candidate semantics");
                t.Near(1, pixel.b, .0001, "Explicit color reaches the function");
            }
            Color current = Render(separate, 1, 63);
            t.Near(.25, current.r, .0001, "Standalone receives actual first argument, not a candidate");
            t.Near(.75, current.g, .0001, "Standalone receives actual second argument");
            values[1].floatValue = .4f;
            t.Near(.4, Render(separate, 2, 63).g, .0001, "Changed referenced values update without recompiling");
        }
        finally
        {
            RenderTexture.active = active; GL.sRGBWrite = srgb;
            if (cache != null) Type("ShaderFXPreviewCache").GetMethod("Release", F).Invoke(cache, null);
            document.layers.Clear(); target.Release();
            foreach (var owned in new UnityEngine.Object[] { readback, target, effect, main, document })
                if (owned != null) UnityEngine.Object.DestroyImmediate(owned);
        }
    }

    static void BundledPreviews(TestContext test)
    {
        int previewCount = 0;
        var create = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
            new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null);
        var previousTarget = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        var correctionTarget = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var correctionPixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            test.True(correctionTarget.Create(), "Correction preview float target created");
            foreach (string preset in new[] { "HueSaturation" })
            {
                string code = File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/" + preset + ".hlsl");
                var values = (List<ShaderFXParameter>)Type("ShaderFXMetadata").GetMethod("Parse", F)
                    .Invoke(null, new object[] { code, true, null });
                var effect = (ShaderFX)create.Invoke(null, new object[] { null, code, values });
                Shader production = null;
                try
                {
                    typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(effect, null);
                    production = (Shader)typeof(ShaderFX).GetProperty("PreviewCompiledShader", F).GetValue(effect);
                    test.True(production != null && production.isSupported, preset + " production shader compiles");
                    foreach (var value in values)
                        foreach (var control in value.controls)
                        {
                            if (string.IsNullOrEmpty(control.previewFunction)) continue;
                            previewCount++;
                            Shader shader = null;
                            Material material = null;
                            try
                            {
                                string source = (string)Type("ShaderFXSourceBuilder").GetMethod("BuildPreview", F)
                                    .Invoke(null, new object[] { effect, control, value });
                                shader = ShaderUtil.CreateShaderAsset(source, true);
                                shader.hideFlags = HideFlags.HideAndDontSave;
                                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                                ShaderUtil.CompilePass(material, 0, true);
                                foreach (var diagnostic in ShaderUtil.GetShaderMessages(shader))
                                    test.True(diagnostic.severity.ToString() != "Error", preset + ": " + control.previewFunction + ": " + diagnostic.message);
                                test.True(shader.isSupported && material.passCount == 1, preset + ": " + control.previewFunction);
                                if (value.name.EndsWith("Saturation", StringComparison.Ordinal) ||
                                    value.name.EndsWith("Lightness", StringComparison.Ordinal))
                                {
                                    foreach (var p in values)
                                    {
                                        if (p.type == ShaderFXParameterType.Vector2) material.SetVector(p.name, p.vectorValue);
                                        else material.SetFloat(p.name, p.floatValue);
                                    }
                                    float sourceHue = 0;
                                    if (value.name.StartsWith("_Range", StringComparison.Ordinal))
                                    {
                                        var range = values.Find(p => p.name == value.name.Substring(0, 7)).vectorValue;
                                        sourceHue = Mathf.Repeat(range.x + Mathf.Repeat(range.y - range.x, 360) * .5f, 360);
                                    }
                                    foreach (float candidate in value.name.EndsWith("Lightness", StringComparison.Ordinal) ?
                                        new[] { -100f, 0, 50, 100 } : new[] { 0f, .5f, 1, 2 })
                                    {
                                        material.SetFloat(value.name, candidate);
                                        material.SetVector("_WhimTex_PreviewRange", Vector4.zero);
                                        Color expected = Color.HSVToRGB(sourceHue / 360, 1, 1);
                                        if (value.name.EndsWith("Saturation", StringComparison.Ordinal))
                                        {
                                            float gray = .2126f * expected.r + .7152f * expected.g + .0722f * expected.b;
                                            for (int c = 0; c < 3; c++) expected[c] = Mathf.Max(0, gray + (expected[c] - gray) * candidate);
                                        }
                                        else expected = candidate < 0 ? expected * (1 + candidate / 100) :
                                            Color.LerpUnclamped(expected, Color.white, candidate / 100);
                                        expected.a = 1;
                                        // Explicit extended sRGB decoding also covers preview values above white.
                                        for (int c = 0; c < 3; c++)
                                            expected[c] = expected[c] <= .04045f ? expected[c] / 12.92f :
                                                (float)Math.Pow((expected[c] + .055) / 1.055, 2.4);
                                        GL.sRGBWrite = false; Graphics.Blit(Texture2D.whiteTexture, correctionTarget, material);
                                        RenderTexture.active = correctionTarget;
                                        correctionPixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); correctionPixel.Apply();
                                        Color actual = correctionPixel.GetPixel(0, 0);
                                        for (int c = 0; c < 4; c++) test.Near(expected[c], actual[c], .0005,
                                            value.name + " preview uses actual correction at " + candidate + ", channel " + c);
                                    }
                                }
                            }
                            finally
                            {
                                if (material != null) UnityEngine.Object.DestroyImmediate(material);
                                if (shader != null) UnityEngine.Object.DestroyImmediate(shader);
                            }
                        }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(effect);
                    if (production != null) UnityEngine.Object.DestroyImmediate(production);
                }
            }
            test.Equal(20, previewCount, "Unified HSV previews cover Master and ranges; Feather has no preview");
        }
        finally
        {
            RenderTexture.active = previousTarget; GL.sRGBWrite = previousSrgb;
            correctionTarget.Release(); UnityEngine.Object.DestroyImmediate(correctionTarget);
            UnityEngine.Object.DestroyImmediate(correctionPixel);
        }

        const string lutCode = "float4 ApplyFX(float2 uv, float4 color) { float h = uv.x * 3 - 1; return float4(WhimTexHue(h), WhimTexHueLinear(h).r); }";
        var lutFX = (ShaderFX)create.Invoke(null, new object[] { null, lutCode, new List<ShaderFXParameter>() });
        Shader lutShader = null;
        Material lutMaterial = null;
        RenderTexture target = null;
        Texture2D pixels = null;
        var active = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(lutFX, null);
            lutShader = (Shader)typeof(ShaderFX).GetProperty("PreviewCompiledShader", F).GetValue(lutFX);
            var lut = Shader.GetGlobalTexture("_WhimTexHueGradient") as Texture2D;
            test.True(lut != null && lut.width == 1536 && lut.height == 1, "Shared Hue LUT is globally bound");
            test.Equal(TextureFormat.RGBAFloat, lut.format, "LUT retains float precision");
            test.Equal(TextureWrapMode.Repeat, lut.wrapMode, "Hue wraps at the seam");
            lutMaterial = new Material(lutShader) { hideFlags = HideFlags.HideAndDontSave };
            test.True(!lutMaterial.HasProperty("_WhimTexHueGradient"), "Global sampler is not shadowed by a material property");
            target = new RenderTexture(1024, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create(); GL.sRGBWrite = false;
            Graphics.Blit(Texture2D.whiteTexture, target, lutMaterial);
            pixels = new Texture2D(1024, 1, TextureFormat.RGBAFloat, false, true);
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1024, 1), 0, 0); pixels.Apply();
            for (int x = 0; x < 1024; x += 17)
            {
                float hue = Mathf.Repeat((x + .5f) / 1024f * 3 - 1, 1);
                Color expected = Color.HSVToRGB(hue, 1, 1), actual = pixels.GetPixel(x, 0);
                test.Near(expected.r, actual.r, .0002, "Hue LUT red and wrap");
                test.Near(expected.g, actual.g, .0002, "Hue LUT green and wrap");
                test.Near(expected.b, actual.b, .0002, "Hue LUT blue and wrap");
                test.Near(expected.linear.r, actual.a, .0002, "Linear hue helper decodes color");
            }
        }
        finally
        {
            RenderTexture.active = active; GL.sRGBWrite = srgb;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            foreach (UnityEngine.Object owned in new UnityEngine.Object[] { pixels, lutMaterial, lutFX, lutShader })
                if (owned != null) UnityEngine.Object.DestroyImmediate(owned);
        }
    }
}
