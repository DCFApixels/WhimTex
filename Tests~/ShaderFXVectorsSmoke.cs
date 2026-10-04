using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class ShaderFXVectorsSmoke
{
    // Bind the package's JSON assembly explicitly; other Editor integrations can
    // expose an embedded copy with the same public type names.
    static object ParseJson(string json)
    {
        var reference = Array.Find(typeof(WhimTexApi).Assembly.GetReferencedAssemblies(), a => a.Name == "Newtonsoft.Json");
        return Assembly.Load(reference).GetType("Newtonsoft.Json.Linq.JObject", true)
            .GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });
    }
    static object At(object node, string key) => node.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(node, new object[] { key });
    static bool Flag(object node, string key) => bool.Parse(At(node, key).ToString());
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        var assembly=typeof(ShaderFX).Assembly;
        var parse=assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata").GetMethod("Parse",F);
        List<ShaderFXParameter> Parse(string code) => (List<ShaderFXParameter>)parse.Invoke(null,new object[]{code,false,null});
        int checks=0;
        void Check(bool ok,string text) { if(!ok)throw new Exception(text); checks++; }
        string code="// @param float2 _Offset = (0.2, 0.3)\n// @param float3 _Vector = (0.4, 0.5, 0.6)\n// @param normal _Normal = (0, 0, 4)\n// @param point _Point = (0.2, 0.37)\nfloat4 ApplyFX(float2 uv,float4 color){return float4(_Offset.x,_Vector.y,_Normal.z,_Point.y);}";
        var values=Parse(code);
        Check(values.Count==4,"count");
        Check(values[0].type==ShaderFXParameterType.Vector2 && values[1].type==ShaderFXParameterType.Vector3,"types");
        Check(values[2].vectorValue==new Vector4(0,0,1,0),"normalization");
        Check(values[3].type==ShaderFXParameterType.Point && values[3].vectorValue.x==.2f && values[3].vectorValue.y==.37f,"point parsing");
        Check(Parse("// @param point _P")[0].vectorValue == new Vector4(.5f,.5f,0,0),"point default");
        Check(Parse("// @param normal _N")[0].vectorValue.z==1,"normal default");
        Check(Parse("// @param normal _N = (0,0,0)")[0].vectorValue.z==1,"zero fallback");
        foreach(var bad in new[]{"float2 _A = (1,2,3)","float3 _A = (1,2)","normal _A = (1,2,3,4)","float3 _A = (1,2,3) [0 .. 1]","float7 _A","point _P = (1.1, 0.5)","point _P = (0.5, 0.5) [0 .. 1]"})
        {
            bool rejected=false;
            try{Parse("// @param "+bad);}catch(TargetInvocationException e) when(e.InnerException is FormatException){rejected=true;}
            Check(rejected,"invalid "+bad);
        }
        var project=typeof(TextureCompositorWindow).GetMethod("ProjectNormal",F);
        foreach(bool back in new[]{false,true})
        foreach(var xy in new[]{Vector2.zero,new Vector2(.3f,.4f),Vector2.right,new Vector2(10,-20)})
        {
            var n=(Vector3)project.Invoke(null,new object[]{xy,back});
            Check(Mathf.Abs(n.magnitude-1)<1e-5 && (back ? n.z<0 : n.z>0),"unit hemisphere");
            var expected=Vector2.ClampMagnitude(xy,1);
            Check(Vector2.Distance(new Vector2(n.x,n.y),expected)<1e-5,"projection clamp");
        }
        var doc=ScriptableObject.CreateInstance<TextureCompositor>(); doc.width=doc.height=16;
        ShaderFX fx=null;
        try
        {
            fx=(ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null, new[] { typeof(DCFApixels.WhimTex.TextureCompositor), typeof(string), typeof(List<DCFApixels.WhimTex.ShaderFXParameter>) }, null).Invoke(null,new object[]{doc,code,new List<ShaderFXParameter>()});
            typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(fx,null);
            Layer layer=new ColorFillLayerBehaviour(); layer.modifiers.Add(fx); doc.layers.Add(layer);
            var image=doc.ComposeCanvas();
            try { var c=image.GetPixel(8,8); Check(Mathf.Abs(c.r-.2f)<.005 && Mathf.Abs(c.g-.5f)<.005 && Mathf.Abs(c.b-1)<.005 && Mathf.Abs(c.a-.37f)<.005,"GPU uniforms"); }
            finally{UnityEngine.Object.DestroyImmediate(image);}
            string preset=(string)assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter").GetMethod("BuildSource",F).Invoke(null,new object[]{fx,"Test/Vectors"});
            var restored=Parse(preset);
            Check(restored.Count==4 && restored[0].type==values[0].type && restored[2].vectorValue==values[2].vectorValue && restored[3].type==ShaderFXParameterType.Point && restored[3].vectorValue==values[3].vectorValue,"preset roundtrip");
            var ui=(VisualElement)Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView"),F,null,new object[]{fx},null);
            Check(ui.Q<Vector2Field>()!=null && ui.Query<Vector3Field>().ToList().Count==2 && ui.Q<Button>()!=null,"parameter UI");
        }
        finally{if(fx!=null)UnityEngine.Object.DestroyImmediate(fx);UnityEngine.Object.DestroyImmediate(doc);}
        var compileResult = ParseJson(WhimTexApi.CompileFXPreset("Packages/com.dcfapixels.whimtex/src/FXPresets/Halftone.hlsl"));
        Check(Flag(compileResult, "success"), "FX compile diagnostic request: " + compileResult.ToString());
        Check(Flag(compileResult, "compiled"), "Halftone preset compilation: " + compileResult.ToString());
        Check(At(compileResult, "diagnostics").GetType().FullName == "Newtonsoft.Json.Linq.JArray" &&
            At(compileResult, "warnings").GetType().FullName == "Newtonsoft.Json.Linq.JArray" &&
            At(compileResult, "errors").GetType().FullName == "Newtonsoft.Json.Linq.JArray",
            "FX compile diagnostic response shape");
        var rawCompile = ParseJson(WhimTexApi.CompileFX(source:
            "// @param color _Tint = (1, 1, 1, 1)\nfloat4 ApplyFX(float2 uv, float4 color) { return color * _Tint; }"));
        Check(Flag(rawCompile, "success") && Flag(rawCompile, "compiled"), "Raw FX source compilation: " + rawCompile.ToString());
        var relativeIncludeCompile = ParseJson(WhimTexApi.CompileFX(
            source: "#include \"./Dither.cginc\"\nfloat4 ApplyFX(float2 uv, float4 color) { return color * DitherThreshold(uv * _CanvasSize.xy, 1); }",
            includeBasePath: "Packages/com.dcfapixels.whimtex/src/Shaders"));
        Check(Flag(relativeIncludeCompile, "success") && Flag(relativeIncludeCompile, "compiled"),
            "Raw FX relative include compilation: " + relativeIncludeCompile.ToString());
        return "PASS: "+checks+" parser, defaults, normalization, GPU, preset roundtrip, UI, raw FX and relative-include compile checks.";
    }
}
