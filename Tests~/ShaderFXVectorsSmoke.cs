using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class ShaderFXVectorsSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        var assembly=typeof(ShaderFX).Assembly;
        var parse=assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata").GetMethod("Parse",F);
        List<ShaderFXParameter> Parse(string code) => (List<ShaderFXParameter>)parse.Invoke(null,new object[]{code,false,null});
        int checks=0;
        void Check(bool ok,string text) { if(!ok)throw new Exception(text); checks++; }
        string code="// @param float2 _Offset = (0.2, 0.3)\n// @param float3 _Vector = (0.4, 0.5, 0.6)\n// @param normal _Normal = (0, 0, 4)\nfloat4 ApplyFX(float2 uv,float4 color){return float4(_Offset.x,_Vector.y,_Normal.z,1);}";
        var values=Parse(code);
        Check(values.Count==3,"count");
        Check(values[0].type==ShaderFXParameterType.Vector2 && values[1].type==ShaderFXParameterType.Vector3,"types");
        Check(values[2].vectorValue==new Vector4(0,0,1,0),"normalization");
        Check(Parse("// @param normal _N")[0].vectorValue.z==1,"normal default");
        Check(Parse("// @param normal _N = (0,0,0)")[0].vectorValue.z==1,"zero fallback");
        foreach(var bad in new[]{"float2 _A = (1,2,3)","float3 _A = (1,2)","normal _A = (1,2,3,4)","float3 _A = (1,2,3) [0 .. 1]","float7 _A"})
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
            fx=(ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft",F).Invoke(null,new object[]{doc,code,new List<ShaderFXParameter>()});
            typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(fx,null);
            Layer layer=new ColorFillLayerBehaviour(); layer.modifiers.Add(fx); doc.layers.Add(layer);
            var image=doc.Compose();
            try { var c=image.GetPixel(8,8); Check(Mathf.Abs(c.r-.2f)<.005 && Mathf.Abs(c.g-.5f)<.005 && Mathf.Abs(c.b-1)<.005,"GPU uniforms"); }
            finally{UnityEngine.Object.DestroyImmediate(image);}
            string preset=(string)assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter").GetMethod("BuildSource",F).Invoke(null,new object[]{fx,"Test/Vectors"});
            var restored=Parse(preset);
            Check(restored.Count==3 && restored[0].type==values[0].type && restored[2].vectorValue==values[2].vectorValue,"preset roundtrip");
            var ui=(VisualElement)Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView"),F,null,new object[]{fx},null);
            Check(ui.Q<Vector2Field>()!=null && ui.Query<Vector3Field>().ToList().Count==2 && ui.Q<Button>()!=null,"parameter UI");
        }
        finally{if(fx!=null)UnityEngine.Object.DestroyImmediate(fx);UnityEngine.Object.DestroyImmediate(doc);}
        return "PASS: "+checks+" parser, defaults, normalization, GPU, preset roundtrip and UI checks.";
    }
}
