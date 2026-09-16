using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class GradientFXSmoke
{
    public static string Main()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
        var assembly = typeof(ShaderFX).Assembly;
        var metadata = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata");
        List<ShaderFXParameter> Parse(string text) => (List<ShaderFXParameter>)metadata.GetMethod("Parse", flags).Invoke(null, new object[] { text, false, null });
        void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        const string code = "// @param gradient _Ramp // Color mapping\n// @param gradient _Ramp // Linked gradient\nfloat4 ApplyFX(float2 uv, float4 color) { return _Ramp_Sample(uv.x * 2 - 0.5); }";
        var parsed = Parse(code);
        Check(parsed.Count == 1 && parsed[0].type == ShaderFXParameterType.Gradient, "Gradient parsing");
        Check(parsed[0].gradientValue.Evaluate(0) == Color.black && parsed[0].gradientValue.Evaluate(1) == Color.white, "Opaque black-white default");
        Check(Parse("// @param gradient _Ramp\n// @param gradient _Ramp")[0].controls.Count == 2, "Linked gradient controls");
        foreach (var bad in new[] { "// @param gradient _Ramp = 1", "// @param gradient _Ramp [0 .. 1]", "// @param gradient _Ramp\n// @param float _Ramp" })
        {
            bool rejected = false;
            try { Parse(bad); } catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            Check(rejected, "Accepted invalid declaration: " + bad);
        }
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        document.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        VisualElement view = null;
        RenderTexture target = null;
        Texture2D readback = null;
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", flags).Invoke(null, new object[] { document, code, new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", flags).Invoke(fx, null);
            var values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", flags).GetValue(fx);
            var context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"), document, null, 8, 2, 1f, true, true);
            Material Get() => (Material)typeof(ShaderFX).GetMethod("GetMaterial", flags).Invoke(fx, new[] { context });
            var material = Get();
            var shader = material.shader;
            var bindings = (IDictionary)typeof(ShaderFX).GetField("gradientBindings", flags).GetValue(fx);
            var enumerator = bindings.Values.GetEnumerator(); enumerator.MoveNext();
            object binding = enumerator.Current;
            var lut = (WhimTexGradientTexture)binding.GetType().GetField("lut", flags).GetValue(binding);
            int property = (int)binding.GetType().GetField("propertyId", flags).GetValue(binding);
            var texture = (Texture2D)material.GetTexture(property);
            Check(texture.width == 512 && texture.height == 2 && texture.mipmapCount == 1 && texture.format == TextureFormat.RGBAHalf, "LUT format");
            for (int i = 0; i < 30; i++) Check(Get() == material, "Material cache");
            Check(lut.BakeCount == 1 && Get().GetTexture(property) == texture, "LUT rebuilt without changes");
            var gradient = values[0].gradientValue;
            gradient.ColorSpace = ColorSpace.Linear;
            gradient.Mode = WhimTexGradientMode.Linear;
            gradient.Smoothness = 0;
            gradient.SetKeys(new[] { new GradientColorKey(new Color(4, 0, 0), 0), new GradientColorKey(new Color(0, 0, 2), 1) },
                new[] { new GradientAlphaKey(.25f, 0), new GradientAlphaKey(.75f, 1) });
            Get();
            Check(lut.BakeCount == 2 && material.shader == shader && material.GetTexture(property) == texture, "Edit should upload, not recreate/recompile");
            target = new RenderTexture(8, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            GL.sRGBWrite = false;
            Graphics.Blit(Texture2D.whiteTexture, target, material);
            RenderTexture.active = target;
            readback = new Texture2D(8, 2, TextureFormat.RGBAFloat, false, true);
            readback.ReadPixels(new Rect(0, 0, 8, 2), 0, 0); readback.Apply();
            for (int x = 0; x < 8; x++)
            {
                Color expected = gradient.Evaluate(Mathf.Clamp01((x + .5f) / 8 * 2 - .5f));
                Color actual = readback.GetPixel(x, 0);
                Check(Mathf.Abs(expected.r - actual.r) < .01f && Mathf.Abs(expected.b - actual.b) < .01f && Mathf.Abs(expected.a - actual.a) < .01f, "GPU sample/HDR/alpha/clamp at " + x + ": " + actual);
            }
            gradient.Mode = WhimTexGradientMode.Fixed;
            Get();
            Check(texture.filterMode == FilterMode.Point && lut.BakeCount == 3, "Fixed filtering");
            var copy = (ShaderFXParameter)typeof(ShaderFXParameter).GetMethod("Copy", flags).Invoke(values[0], null);
            copy.gradientValue.Smoothness = .8f;
            Check(gradient.Smoothness == 0 && !ReferenceEquals(copy.gradientValue, gradient), "Independent copied gradient");
            var roundtrip = JsonUtility.FromJson<ShaderFXParameter>(JsonUtility.ToJson(values[0]));
            Check(roundtrip.gradientValue.Equals(gradient), "Serialized gradient");
            var next = Parse(code);
            metadata.GetMethod("PreserveValues", flags).Invoke(null, new object[] { next, values });
            Check(next[0].gradientValue.Equals(gradient) && !ReferenceEquals(next[0].gradientValue, gradient), "Apply preserves independent keys");
            string exported = (string)assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter").GetMethod("BuildSource", flags).Invoke(null, new object[] { fx, "Test/Gradient" });
            Check(exported.Contains("// @param gradient _Ramp // Color mapping") && !exported.Contains("gradient _Ramp ="), "Defaultless export and tooltip");
            Check(Parse(exported)[0].gradientValue.Evaluate(0) == Color.black, "HLSL defaults remain black-white");
            var viewType = assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView");
            view = (VisualElement)Activator.CreateInstance(viewType, flags, null, new object[] { fx }, null);
            var fields = view.Query<WhimTexGradientValueField>().ToList();
            Check(fields.Count == 2 && fields[0].tooltip == "Color mapping", "Generated gradient fields and tooltip");
            var edited = gradient.Clone(); edited.Smoothness = .7f;
            viewType.GetMethod("Change", flags).Invoke(view, new object[] { values[0].id, (Action<ShaderFXParameter>)(p => p.gradientValue = edited.Clone()) });
            Check(values[0].gradientValue.Smoothness == .7f && fields[1].value.Smoothness == .7f && !ReferenceEquals(edited, values[0].gradientValue), "Linked gradient UI change with independent data");
            Check(Get().shader == shader, "UI edit recompiled shader");
            typeof(ShaderFX).GetMethod("ReleaseMaterial", flags).Invoke(fx, null);
            Check(texture == null && bindings.Count == 0, "Released GPU cache");
            Check(Get() != null, "Lazy restore");
            return "PASS: declaration validation, black-white defaults, linked controls, HDR/alpha GPU sampling, clamping, LUT reuse/invalidation, Fixed filtering, independent copy, serialization, Apply preservation, export and disposal.";
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (view != null)
                foreach (var field in view.Query<WhimTexGradientValueField>().ToList())
                {
                    var preview = (Texture2D)typeof(WhimTexGradientValueField).GetField("preview", flags).GetValue(field);
                    if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
                }
            if (fx != null) { Undo.ClearUndo(fx); UnityEngine.Object.DestroyImmediate(fx); }
            UnityEngine.Object.DestroyImmediate(document);
        }
    }
}
