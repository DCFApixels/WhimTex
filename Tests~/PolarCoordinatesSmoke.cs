using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

// Self-contained in-memory GPU comparison against the two original mappings.
public static class PolarCoordinatesSmoke
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
    public static string Main()
    {
        const int size = 33; // Includes the exact center.
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.width = doc.height = size;
        var input = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true) { wrapMode = TextureWrapMode.Repeat };
        var read = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
        var output = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
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
                new[] { typeof(TextureCompositor), typeof(string), typeof(List<ShaderFXParameter>) }, null);
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
            if (modeParameter.controls[0].type != ShaderFXParameterType.Enum || modeParameter.floatValue != 0)
                throw new Exception("Expected To Polar default.");
            var shader = typeof(ShaderFX).GetField("compiledShader", F).GetValue(effects[0]);
            Color[] Render(ShaderFX fx)
            {
                var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", F).Invoke(fx, new[] { context });
                if (material == null || ShaderUtil.ShaderHasError(material.shader)) throw new Exception("Shader failed.");
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error ||
                        message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)
                        throw new Exception(message.message);
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
                            parameters.Find(p => p.name == "_Area").transformValue = area;
                        }
                        var actual = Render(effects[0]);
                        var expected = Render(effects[mode + 1]);
                        for (int i = 0; i < actual.Length; i++) for (int c = 0; c < 4; c++)
                        {
                            float value = actual[i][c];
                            if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value - expected[i][c]) > .0001f)
                                throw new Exception($"Mapping mismatch: mode {mode}, offset {offset}, transform {transformed}, pixel {i}, channel {c}.");
                            checks++;
                        }
                    }
            if (!ReferenceEquals(shader, typeof(ShaderFX).GetField("compiledShader", F).GetValue(effects[0])))
                throw new Exception("Mode changes must not recompile the shader.");
            var catalog = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog");
            int found = 0;
            foreach (var entry in (System.Collections.IEnumerable)catalog.GetMethod("GetEntries", F).Invoke(null, null))
            {
                string path = (string)entry.GetType().GetField("menuPath", F).GetValue(entry);
                if (path == "Distortion/Polar Coordinates") found++;
                if (path.StartsWith("Distortion/Polar Coordinates/")) throw new Exception("Old catalog item remains.");
            }
            if (found != 1) throw new Exception("Expected one unified catalog entry.");
            return $"PASS: {checks} GPU RGBA comparisons, both modes/offsets/transforms, no shader warnings, no mode recompilation, one catalog entry.";
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            RenderTexture.ReleaseTemporary(output);
            foreach (var fx in effects) if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(input); UnityEngine.Object.DestroyImmediate(read);
            UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
