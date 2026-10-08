using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class BoolFXTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    private static string BodyRun()
    {
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var assembly = typeof(ShaderFX).Assembly;
        var parse = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata").GetMethod("Parse", hidden);
        foreach (string value in new[] { "false", "true", "0", "1" })
        {
            var parsed = (List<ShaderFXParameter>)parse.Invoke(null, new object[] { "// @param bool _Flag = " + value, false, null });
            T.True(!(parsed[0].type != ShaderFXParameterType.Bool || parsed[0].floatValue != (value == "true" || value == "1" ? 1f : 0f)), "Bool default parsing failed");
        }
        foreach (string invalid in new[] { " = 0.5", " = true [0 .. 1]", " = yes" })
        {
            bool rejected = false;
            try { parse.Invoke(null, new object[] { "// @param bool _Flag" + invalid, false, null }); }
            catch (TargetInvocationException error) when (error.InnerException is FormatException) { rejected = true; }
            T.True(!(!rejected), "Invalid bool accepted");
        }
        var document = Scope.OwnObject(ScriptableObject.CreateInstance<TextureCompositor>());
        document.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var output = RenderTexture.GetTemporary(4, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var pixels = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
        try
        {
            string source = "// @param bool _Flag = false\nfloat4 ApplyFX(float2 uv, float4 color) { return float4(_Flag, 0, 0, 1); }";
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", hidden, null, new[] { typeof(DCFApixels.WhimTex.TextureCompositor), typeof(string), typeof(List<DCFApixels.WhimTex.ShaderFXParameter>) }, null).Invoke(null, new object[] { document, source, new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", hidden).Invoke(fx, null);
            var list = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", hidden).GetValue(fx);
            var shaderField = typeof(ShaderFX).GetField("compiledShader", hidden);
            var shader = shaderField.GetValue(fx);
            var context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"), document, null, 4, 4, 1f, true, true, null);
            Color Render()
            {
                var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", hidden).Invoke(fx, new[] { context });
                GL.sRGBWrite = false;
                Graphics.Blit(Texture2D.whiteTexture, output, material);
                RenderTexture.active = output;
                pixels.ReadPixels(new Rect(0, 0, 4, 4), 0, 0); pixels.Apply();
                return pixels.GetPixel(1, 1);
            }
            var rgb = Render();
            T.True(!(Mathf.Abs(rgb.r) > .001f || Mathf.Abs(rgb.a - 1) > .001f), "False GPU result wrong");
            var viewType = assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView");
            var view = (VisualElement)Activator.CreateInstance(viewType, hidden, null, new object[] { fx }, null);
            var toggle = view.Q<Toggle>();
            T.True(!(toggle == null || toggle.value), "Toggle not created/default incorrect");
            viewType.GetMethod("Change", hidden).Invoke(view, new object[] { list[0].id,
                (Action<ShaderFXParameter>)(p => p.floatValue = 1f) });
            T.True(!(!toggle.value), "Toggle did not refresh from model");
            var rgba = Render();
            T.True(!(Mathf.Abs(rgba.r - 1f) > .001f || Mathf.Abs(rgba.a - 1f) > .001f), "True GPU result wrong: " + rgba);
            T.True(!(!ReferenceEquals(shader, shaderField.GetValue(fx))), "Toggle recompiled shader");
            var writer = assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter");
            string declaration = (string)writer.GetMethod("Declaration", hidden).Invoke(null, new object[] { list[0] });
            T.True(!(declaration != "// @param bool _Flag = true"), "Export lost bool value");
            var copy = JsonUtility.FromJson<ShaderFXParameter>(JsonUtility.ToJson(list[0]));
            T.True(!(copy.type != ShaderFXParameterType.Bool || copy.floatValue != 1f), "Serialization failed");
            return null;
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            RenderTexture.ReleaseTemporary(output);
            UnityEngine.Object.DestroyImmediate(pixels);
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(document);
        }
    }
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}
