using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class ShaderFXTypedParametersTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type Type(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, string name, params object[] args) => type.GetMethods(F).Single(m => m.Name == name && m.GetParameters().Length == args.Length && m.GetParameters().Select((p,i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(x => x)).Invoke(null, args);
    static List<ShaderFXParameter> Parse(string code) => (List<ShaderFXParameter>)Call(Type("ShaderFXMetadata"), "Parse", code, false, null);
    static double Number(ShaderFXParameter p, int i = 0) => (double)typeof(ShaderFXParameter).GetMethod("GetNumber", F).Invoke(p, new object[] { i });
    static void Set(ShaderFXParameter p, int i, double value) => typeof(ShaderFXParameter).GetMethod("SetNumber", F).Invoke(p, new object[] { i, value });

    public static string Run() => TestContext.Run("Typed shader controls, GPU values, UI, API and persistence", t =>
    {
        var code = new StringBuilder(); var checks = new StringBuilder("bool valid = true;\n");
        int sequence = 0;
        foreach (string field in new[] { "toggle", "color" })
            foreach (string scalar in new[] { "bool", "int", "uint", "half", "float" })
                for (int count = 1; count <= 4; count++)
                {
                    string type = scalar + (count == 1 ? "" : count.ToString());
                    string name = "_V" + sequence++;
                    var components = Enumerable.Range(0, count).Select(i => scalar == "bool" ? i % 2 == 0 ? "true" : "false" : (i % 2 == 0 ? "1" : "0")).ToArray();
                    string value = count == 1 ? components[0] : "(" + string.Join(",", components) + ")";
                    string declaration = "// @param " + (count > 1 ? "rgba " : "") + field + "(" + type + ") " + name + " = " + value + "\n";
                    code.Append(declaration);
                    var parsed = Parse(declaration)[0];
                    t.Equal(type, parsed.valueType, "Declared type");
                    t.Equal(count > 1 || field == "color", parsed.controls[0].rgba, "Channel label mode");
                    var restored = JsonUtility.FromJson<ShaderFXParameter>(JsonUtility.ToJson(parsed));
                    for (int i = 0; i < count; i++)
                    {
                        t.Equal(i % 2 == 0 ? 1d : 0d, Number(restored, i), "Exact numeric roundtrip");
                        string access = name + (count == 1 ? "" : "." + "xyzw"[i]);
                        checks.Append("valid = valid && (" + access + " == " + components[i] + ");\n");
                    }
                    string preview = "float4 Preview(float2 uv, " + type + " value) { return 0; }";
                    Call(Type("ShaderFXPreviewFunction"), "Call", preview, "Preview", parsed, parsed.controls[0].type, null, null);
                    t.True(true, "Preview accepts the declared numeric type");
                }
        code.Append("// @param enum(uint) _Enum = Last { First: 2147483648, Last: 4294967295 }\n" +
            "// @param color(int4) _Signed = (-2147483648,2147483647,2147483646,-123456789)\n" +
            "// @param color(uint4) _Unsigned = (0,4294967295,4294967294,2147483648)\n" +
            "// @param seed(uint) _Seed = 4294967295 [4294967294 .. 4294967295]\n" +
            "// @param color(float4) _Hdr = (4.5, 0.125, -2, 0.25)\n" +
            "// @param toggle(bool4) _Gate = (true, false, true, false)\n" +
            "// @enableif _Gate.r == true\n// @param float _Amount = 1\n// @endenableif\n");
        checks.Append("valid = valid && (_Enum == 4294967295u) && (_Seed == 4294967295u);\n");
        checks.Append("valid = valid && all(_Signed == int4(-2147483647 - 1,2147483647,2147483646,-123456789)) && all(_Unsigned == uint4(0u,4294967295u,4294967294u,2147483648u));\n");
        code.Append("float4 ApplyFX(PinnedUV uv, float4 color) { " + checks + " return float4(valid ? 1 : 0, _Hdr.r, _Hdr.g, 1); }");
        var parameters = Parse(code.ToString());
        var seed = parameters.Find(p => p.name == "_Seed");
        for (int i = 0; i < 64; i++)
        {
            double value = (double)Call(Type("ShaderFXSeed"), "NextValue", seed.controls[0]);
            t.True(value == 4294967294d || value == 4294967295d, "Full-width unsigned seed generation");
        }
        foreach (string invalid in new[] { "// @param bool _Old", "// @param seed float _Old", "// @param toggle(double) _Unsupported",
            "// @param rgba float _Scalar", "// @param toggle(uint2) _Bad = (1,2)", "// @param enum(int2) _Bad { A:0 }",
            "// @param color(uint) _Bad = -1", "// @param color(int) _Bad = 2147483648", "// @param color(bool2) _Bad = (0,0.5)",
            "// @param toggle(float2) _Flags\n// @if _Flags == 1\n// @param float _Bad\n// @endif",
            "// @param toggle(float2) _Flags\n// @if _Flags.z == 1\n// @param float _Bad\n// @endif" })
        {
            bool rejected = false;
            try { Parse(invalid); } catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            t.True(rejected, "Reject invalid declaration: " + invalid);
        }
        CheckChannelPresets(t);
        var document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX effect = null; Material material = null; Texture2D pixels = null; RenderTexture output = null;
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            effect = (ShaderFX)Call(typeof(ShaderFX), "CreateAgentDraft", document, code.ToString(), new List<ShaderFXParameter>());
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(effect, null);
            var values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(effect);
            var context = Activator.CreateInstance(Type("LayerRenderContext"), document, null, 4, 4, 1f, true, true, null);
            material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", F).Invoke(effect, new[] { context });
            output = RenderTexture.GetTemporary(4, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            pixels = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            GL.sRGBWrite = false; Graphics.Blit(Texture2D.whiteTexture, output, material);
            RenderTexture.active = output; pixels.ReadPixels(new Rect(0,0,4,4),0,0); pixels.Apply();
            Color result = pixels.GetPixel(1,1);
            t.Near(1, result.r, .001, "All scalar/vector types and unsigned endpoints reach GPU exactly");
            t.Near(Math.Pow((4.5 + .055) / 1.055, 2.4), result.g, .001, "Encoded HDR color preserves existing linear shader conversion"); t.Near(Math.Pow((.125 + .055) / 1.055, 2.4), result.b, .001, "Fractional color conversion preserved");
            string exported = (string)Call(Type("ShaderFXPresetWriter"), "BuildSource", effect, "Test/Typed");
            var exportedValues = Parse(exported);
            foreach (var p in values)
            {
                var copy = exportedValues.Find(x => x.name == p.name);
                t.Equal(p.valueType, copy.valueType, "Preset export retains explicit type");
                for (int i = 0; i < (int)typeof(ShaderFXParameter).GetProperty("Components", F).GetValue(p); i++) t.Equal(Number(p,i), Number(copy,i), "Preset exact value");
            }
            var view = (VisualElement)Activator.CreateInstance(Type("ShaderFXParameterView"), F, null, new object[] { effect }, null);
            t.True(view.Query<Toggle>().ToList().Count >= 50, "Independent vector toggles exist");
            t.True(view.Query<Toggle>().ToList().Any(x => x.label == "R") && view.Query<Toggle>().ToList().Any(x => x.label == "A"), "RGBA toggle labels");
            foreach (int count in new[] { 1,2,3,4 })
                t.True(view.Query<WhimTexColorField>().ToList().Any(x => (int)typeof(WhimTexColorField).GetProperty("EditableComponents", F).GetValue(x) == count), "Picker component mode " + count);
            var target = values.Find(x => x.name == "_Seed");
            var exactColor = values.Find(x => x.name == "_Unsigned");
            typeof(ShaderFXParameter).GetMethod("SetNumericColor", F).Invoke(exactColor, new object[] { new Color(0,4294967295f,4294967294f,2147483648f) });
            t.Equal(4294967294d, Number(exactColor,2), "Unchanged picker components retain integers beyond float precision");
            Undo.RegisterCompleteObjectUndo(effect, "Typed parameter test"); Set(target, 0, 4294967294d); Undo.FlushUndoRecordObjects();
            Undo.PerformUndo(); values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(effect);
            t.Equal(4294967295d, Number(values.Find(x => x.name == "_Seed")), "Undo retains full uint");
            Undo.PerformRedo(); values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(effect);
            t.Equal(4294967294d, Number(values.Find(x => x.name == "_Seed")), "Redo retains full uint");
            var read = (ShaderFXParameter)Call(typeof(WhimTexApi), "ReadFxParameterValue", values.Find(x => x.name == "_Gate"), typeof(WhimTexApi).GetMethod("ReadFxParameterValue", F).GetParameters()[1].ParameterType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { "[false,true,false,true]" }), document);
            t.Equal(0d, Number(read,0), "Boolean API input"); t.Equal(1d, Number(read,1), "Boolean vector API input");
            var snapshot = Call(typeof(WhimTexApi), "TypedFxValue", read);
            var item = snapshot.GetType().GetProperty("Item", new[] { typeof(int) }).GetValue(snapshot, new object[] { 1 });
            t.Equal("Boolean", item.GetType().GetProperty("Type").GetValue(item).ToString(), "Boolean API output is typed");
            var layer = new Layer(new ColorFillLayerBehaviour()); document.layers.Add(layer); layer.fx.Add(effect);
            typeof(Layer).GetMethod("AssignNewId", F).Invoke(layer, null);
            var saved = WhimTexDocumentJson.Write(document);
            using var loaded = WhimTexDocumentJson.Read(saved.Json);
            var restoredEffect = (ShaderFX)loaded.Document.layers[0].fx[0];
            var restoredParameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(restoredEffect);
            t.Equal(4294967294d, Number(restoredParameters.Find(x => x.name == "_Seed")), "Document roundtrip preserves uint");
            t.Equal(4.5, Number(restoredParameters.Find(x => x.name == "_Hdr")), "Document roundtrip preserves HDR");
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (output != null) RenderTexture.ReleaseTemporary(output);
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (effect != null) { Undo.ClearUndo(effect); UnityEngine.Object.DestroyImmediate(effect); }
            Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document);
        }
    });

    static void CheckChannelPresets(TestContext t)
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
        var source = new Texture2D(4,4,TextureFormat.RGBAFloat,false,true);
        var mask = new Texture2D(4,4,TextureFormat.RGBAFloat,false,true);
        var read = new Texture2D(4,4,TextureFormat.RGBAFloat,false,true);
        var input = Enumerable.Range(0,16).Select(i => new Color((i%4-1)*.5f,.2f+i*.04f,2.5f,i/15f)).ToArray();
        source.SetPixels(input); source.Apply(); mask.SetPixels(Enumerable.Repeat(new Color(.25f,.25f,.25f,1),16).ToArray()); mask.Apply();
        var target = RenderTexture.GetTemporary(4,4,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            var context = Activator.CreateInstance(Type("LayerRenderContext"), document, null, 4,4,1f,true,true,null);
            foreach (string name in new[] { "Mask", "Step" })
            {
                string code = System.IO.File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/"+name+".hlsl");
                var effect = (ShaderFX)Call(typeof(ShaderFX), "CreateAgentDraft", document,code,new List<ShaderFXParameter>());
                try
                {
                    typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(effect,null);
                    var values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters",F).GetValue(effect);
                    var channels = values.Find(p=>p.name=="_Channels");
                    t.True(channels.valueType=="float4" && channels.controls[0].rgba, name+" uses vector channel switches");
                    if(name=="Mask") { var p=values.Find(x=>x.name=="_Mask"); p.textureSource=ShaderFXTextureSource.Texture; p.textureValue=mask; }
                    for(int mode=0;mode<2;mode++) for(int bits=0;bits<16;bits++)
                    {
                        for(int c=0;c<4;c++) Set(channels,c,(bits>>c)&1);
                        if(name=="Step") Set(values.Find(p=>p.name=="_Mode"),0,mode);
                        else Set(values.Find(p=>p.name=="_Invert"),0,mode);
                        var material=(Material)typeof(ShaderFX).GetMethod("GetMaterial",F).Invoke(effect,new[]{context});
                        GL.sRGBWrite=false; Graphics.Blit(source,target,material); RenderTexture.active=target;
                        read.ReadPixels(new Rect(0,0,4,4),0,0); read.Apply(); var pixels=read.GetPixels();
                        for(int i=0;i<16;i++) for(int c=0;c<4;c++)
                        {
                            float expected=input[i][c];
                            if(((bits>>c)&1)!=0)
                            {
                                if(name=="Mask") expected*=mode==0?.25f:.75f;
                                else if(mode==0) expected=expected>=.5f?1:0;
                                else { float u=Mathf.Clamp01((expected-.375f)/.25f); expected=u*u*(3-2*u); }
                            }
                            t.Near(expected,pixels[i][c],.00002,name+" preserves channel result, mode="+mode+", mask="+bits+", component="+c);
                        }
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(effect); }
            }
        }
        finally
        {
            RenderTexture.active=previous; GL.sRGBWrite=srgb; RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(mask); UnityEngine.Object.DestroyImmediate(read); UnityEngine.Object.DestroyImmediate(document);
        }
    }
}
