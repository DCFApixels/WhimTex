using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

// In-memory GPU comparisons against independent mappings, current frames, refresh and JSON.
public static class PolarCoordinatesTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string ToReference = @"// @whimtex-effect Distortion/Polar Coordinates/To Polar
// @param float _AngleOffset = 0 [~-180 .. ~180] // Angular offset in degrees.
// @param float _RadialOffset = 0 [~-1 .. ~1] // Radial offset in normalized coordinates.
// @param transform2D _Area

float4 ApplyFX(float2 uv, float4 color)
{
    float2 p = (_Area_ToLocal(uv) - 0.5) * 2.0;
    float radius = length(p);
    // The center has no unique angle. Choose zero without evaluating atan2(0, 0).
    float angle = 0.0;
    if (radius > 0.0) angle = atan2(p.y, p.x) / 6.28318530718;
    // Only the angular coordinate wraps; radius continues beyond the frame.
    float2 sampleUV = float2(frac(angle - _AngleOffset / 360.0), radius - _RadialOffset);
    return SampleInput(sampleUV);
}";
    const string FromReference = @"// @whimtex-effect Distortion/Polar Coordinates/From Polar
// @param float _AngleOffset = 0 [~-180 .. ~180] // Angular offset in degrees.
// @param float _RadialOffset = 0 [~-1 .. ~1] // Radial offset in normalized coordinates.
// @param transform2D _Area

float4 ApplyFX(float2 uv, float4 color)
{
    float angle = (uv.x + _AngleOffset / 360.0) * 6.28318530718;
    float radius = uv.y + _RadialOffset;
    float sine, cosine;
    sincos(angle, sine, cosine);
    float2 localUV = 0.5 + float2(cosine, sine) * radius * 0.5;
    return SampleInput(_Area_ToInput(localUV));
}";
    // Independent two-frame mapping; no historic-name or linked-preset migration.
    const string FramesReference = @"// @whimtex-effect Distortion/Polar Coordinates
// @param hidden float _Amount = 1 [0 .. 1]
// @param enum _Mode = 0 {ToPolar: 0, FromPolar: 1}
// @param float _AngleOffset = 0 [~-180 .. ~180]
// @param float _RadialOffset = 0 [~-1 .. ~1]
// @param transform2D _Input
// @param transform2D _Output
// @param enum _Tiling = 0 {Clamp: 0, Repeat: 1, Mirror: 2, Clip: 3}
float4 ApplyFX(float2 uv, float4 color)
{
    if (_Amount <= 0.0) return color;
    float2 localUV = _Output_ToLocal(uv);
    float2 mappedUV;
    if (_Mode < 0.5)
    {
        float2 p = (localUV - 0.5) * 2.0;
        float radius = length(p);
        float angle = 0.0;
        if (radius > 0.0) angle = atan2(p.y, p.x) / 6.28318530718;
        mappedUV = float2(frac(angle - _AngleOffset / 360.0), radius - _RadialOffset);
    }
    else
    {
        float angle = (localUV.x + _AngleOffset / 360.0) * 6.28318530718;
        float radius = localUV.y + _RadialOffset;
        float sine, cosine;
        sincos(angle, sine, cosine);
        mappedUV = 0.5 + float2(cosine, sine) * radius * 0.5;
    }
    return SampleInput(lerp(uv, _Input_ToInput(mappedUV), _Amount), _Tiling);
}";
    static string ExecuteMain()
    {
        const int size = 33; // Includes the exact center.
        // Package inputs are already imported; all comparison shaders/textures are in memory.
        var doc = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        doc.width = doc.height = size;
        var input = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true) { wrapMode = TextureWrapMode.Clamp });
        var read = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
        var output = WhimTex.Tests.UnityC.FixtureContext.Scope.Temporary(RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var effects = new List<ShaderFX>();
        int checks = 0;
        try
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color(x / 32f, y / 32f, ((x + y) % 7) / 6f, ((x * 3 + y) % 11) / 10f);
            input.SetPixels(pixels); input.Apply();
            var draft = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            foreach (string code in new[] { File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/PolarCoordinates.hlsl"), ToReference, FromReference })
            {
                var fx = (ShaderFX)draft.Invoke(null, new object[] { doc, code, new List<ShaderFXParameter>() });
                effects.Add(fx);
                typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            }
            var context = Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"),
                doc, null, size, size, 1f, true, true, null);
            List<ShaderFXParameter> Parameters(ShaderFX fx) =>
                (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            var modeParameter = Parameters(effects[0]).Find(p => p.name == "_Mode");
            WhimTex.Tests.UnityC.FixtureContext.Context.True(!(modeParameter.controls[0].type != ShaderFXParameterType.Enum || modeParameter.floatValue != 0), "Expected To Polar default.");
            var shader = typeof(ShaderFX).GetField("compiledShader", F).GetValue(effects[0]);
            Color[] Render(ShaderFX fx)
            {
                var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", F).Invoke(fx, new[] { context });
                WhimTex.Tests.UnityC.FixtureContext.Context.True(!(material == null || ShaderUtil.ShaderHasError(material.shader)), "Shader failed.");
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    WhimTex.Tests.UnityC.FixtureContext.Context.True(!(message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error ||
                        message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning), message.message);
                GL.sRGBWrite = false;
                Graphics.Blit(input, output, material);
                RenderTexture.active = output;
                read.ReadPixels(new Rect(0, 0, size, size), 0, 0); read.Apply();
                return read.GetPixels();
            }
            for (int mode = 0; mode < 2; mode++)
                foreach (var offset in new[] { Vector2.zero, new Vector2(37, .2f), new Vector2(-720, -.5f) })
                    for (int transformed = 0; transformed < 2; transformed++)
                    {
                        modeParameter.floatValue = mode;
                        foreach (var fx in effects)
                        {
                            var parameters = Parameters(fx);
                            parameters.Find(p => p.name == "_AngleOffset").floatValue = offset.x;
                            parameters.Find(p => p.name == "_RadialOffset").floatValue = offset.y;
                            var area = ShaderFXTransform.Default;
                            if (transformed != 0)
                            {
                                area.position = new Double2(.4, .6);
                                area.size = new Double2(.7, 1.2);
                                area.rotation = 23;
                            }
                            // The original From Polar used Area as its source frame; that is now Input.
                            parameters.Find(p => p.name == (fx == effects[0] ? "_Output" : "_Area")).transformValue = fx == effects[0] && mode == 1
                                ? ShaderFXTransform.Default : area;
                            if (fx == effects[0]) parameters.Find(p => p.name == "_Input").transformValue = mode == 1
                                ? area : ShaderFXTransform.Default;
                        }
                        var actual = Render(effects[0]);
                        var expected = Render(effects[mode + 1]);
                        for (int i = 0; i < actual.Length; i++) for (int c = 0; c < 4; c++)
                        {
                            float value = actual[i][c];
                            WhimTex.Tests.UnityC.FixtureContext.Context.True(!(float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value - expected[i][c]) > .0001f), $"Mapping mismatch: mode {mode}, offset {offset}, transform {transformed}, pixel {i}, channel {c}.");
                            checks++;
                        }
                    }
            WhimTex.Tests.UnityC.FixtureContext.Context.True(!(!ReferenceEquals(shader, typeof(ShaderFX).GetField("compiledShader", F).GetValue(effects[0]))), "Mode changes must not recompile the shader.");
            var t = WhimTex.Tests.UnityC.FixtureContext.Context;
            void Compare(Color[] expected, Color[] actual, string label)
            {
                for (int i = 0; i < actual.Length; i++) for (int c = 0; c < 4; c++)
                    t.True(!float.IsNaN(actual[i][c]) && !float.IsInfinity(actual[i][c]) &&
                        Math.Abs(actual[i][c] - expected[i][c]) <= .0001f, $"{label}: pixel {i}, channel {c}");
            }
            var translated = ShaderFXTransform.Default;
            translated.position = new Double2(.4, .6); translated.size = new Double2(.7, 1.2); translated.rotation = 23;
            var projective = ShaderFXTransform.Default; projective.storage = TransformStorage.Projective;
            projective.matrix = new ProjectiveMatrix { m00 = .9, m01 = .1, m02 = .04, m10 = -.05, m11 = 1.1, m12 = -.1, m20 = .2, m21 = -.15, m22 = 1 };
            string guid = AssetDatabase.AssetPathToGUID("Packages/com.dcfapixels.whimtex/src/FXPresets/PolarCoordinates.hlsl");
            t.True(!string.IsNullOrEmpty(guid), "Polar Coordinates catalog GUID exists");
            var reload = typeof(ShaderFX).GetMethod("ReloadCatalogSource", F);
            var reference = (ShaderFX)draft.Invoke(null, new object[] { doc, FramesReference, new List<ShaderFXParameter>() });
            effects.Add(reference); typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(reference, null);
            var framePairs = new[] {
                new[] { ShaderFXTransform.Default, translated },
                new[] { translated, projective },
                new[] { projective, translated }
            };
            for (int mode = 0; mode < 2; mode++) foreach (float amount in new[] { .4f, 1f })
                foreach (var pair in framePairs)
                {
                    foreach (var fx in new[] { effects[0], reference })
                    {
                        var p = Parameters(fx);
                        p.Find(x => x.name == "_Mode").floatValue = mode;
                        p.Find(x => x.name == "_Amount").floatValue = amount;
                        p.Find(x => x.name == "_AngleOffset").floatValue = 37;
                        p.Find(x => x.name == "_RadialOffset").floatValue = .2f;
                        p.Find(x => x.name == "_Input").transformValue = pair[0];
                        p.Find(x => x.name == "_Output").transformValue = pair[1];
                    }
                    var expected = Render(reference);
                    Compare(expected, Render(effects[0]), "Independent Input/Output mapping");
                    var parameters = Parameters(effects[0]);
                    var inputFrame = parameters.Find(p => p.name == "_Input");
                    var outputFrame = parameters.Find(p => p.name == "_Output");
                    Layer layer = new ColorFillLayerBehaviour(); layer.fx.Add(effects[0]);
                    doc.layers.Clear(); doc.layers.Add(layer);
                    typeof(WhimTexDocument).GetMethod("NormalizeModel", F).Invoke(doc, null);
                    var exported = WhimTexDocumentJson.Write(doc);
                    t.Equal(0, exported.Warnings.Count, "Current frames serialize without warnings");
                    using (var copy = WhimTexDocumentJson.Read(exported.Json))
                    {
                        t.Equal(0, copy.Warnings.Count, "Current frames reload without warnings");
                        var fx = (ShaderFX)copy.Document.layers[0].fx[0];
                        t.Equal(pair[0], Parameters(fx).Find(p => p.name == "_Input").transformValue, "Saved full Input frame");
                        t.Equal(pair[1], Parameters(fx).Find(p => p.name == "_Output").transformValue, "Saved full Output frame");
                        Compare(expected, Render(fx), "Current JSON render parity");
                    }
                    typeof(ShaderFX).GetField("catalogGuid", F).SetValue(effects[0], guid);
                    reload.Invoke(effects[0], new object[] { true });
                    var next = Parameters(effects[0]);
                    t.True(next.Find(p => p.name == "_Area" || p.name == "_Center") == null, "Only current frame names");
                    t.Equal(inputFrame.id, next.Find(p => p.name == "_Input").id, "Input identity survives current-source refresh");
                    t.Equal(outputFrame.id, next.Find(p => p.name == "_Output").id, "Output identity survives current-source refresh");
                    t.Equal(pair[0], next.Find(p => p.name == "_Input").transformValue, "Input edit survives refresh");
                    t.Equal(pair[1], next.Find(p => p.name == "_Output").transformValue, "Output edit survives refresh");
                    Compare(expected, Render(effects[0]), "Current linked refresh render parity");
                }
            var catalog = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog");
            int found = 0;
            foreach (var entry in (System.Collections.IEnumerable)catalog.GetMethod("GetEntries", F).Invoke(null, null))
            {
                string path = (string)entry.GetType().GetField("menuPath", F).GetValue(entry);
                if (path == "Distortion/Polar Coordinates") found++;
                WhimTex.Tests.UnityC.FixtureContext.Context.True(!(path.StartsWith("Distortion/Polar Coordinates/")), "Old catalog item remains.");
            }
            WhimTex.Tests.UnityC.FixtureContext.Context.True(!(found != 1), "Expected one unified catalog entry.");
            return $"PASS: {checks} original GPU RGBA comparisons, current frame refresh and JSON round-trips, no shader warnings, no mode recompilation, one catalog entry.";
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            WhimTex.Tests.UnityC.FixtureContext.Scope.Release(output);
            foreach (var fx in effects) if (fx != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(fx);
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(input); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(read);
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);
        }
    }

    public static string Main() => WhimTex.Tests.UnityC.FixtureContext.Run("PolarCoordinatesTests.Main", () => { ExecuteMain(); });
}
