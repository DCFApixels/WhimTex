using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

public static class ShaderFXCurveSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var doc = ScriptableObject.CreateInstance<TextureCompositor>(); doc.width = doc.height = 16;
        var effects = new List<ShaderFX>();
        var utility = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.WhimTexCurveTexture");
        var lut = Activator.CreateInstance(utility, true);
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        ShaderFX Create(string code)
        {
            var fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F).Invoke(null, new object[] { doc, code, new List<ShaderFXParameter>() });
            effects.Add(fx); typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null); return fx;
        }
        List<ShaderFXParameter> Params(ShaderFX fx) => (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
        Texture2D Bake(AnimationCurve c) => (Texture2D)utility.GetMethod("GetTexture", F).Invoke(lut, new object[] { c });
        int Count() => (int)utility.GetProperty("BakeCount", F).GetValue(lut);
        try
        {
            var fx = Create("// @param curve _Profile\nfloat4 ApplyFX(float2 uv,float4 color){return float4(_Profile_Sample(-1),_Profile_Sample(0.5),_Profile_Sample(2),1);}");
            var p = Params(fx)[0];
            Check(p.type == ShaderFXParameterType.Curve && Mathf.Abs(p.curveValue.Evaluate(.25f) - .25f) < 1e-6f, "default");
            Layer layer = new ColorFillLayerBehaviour(); doc.layers.Add(layer); layer.modifiers.Add(fx);
            var shader = typeof(ShaderFX).GetField("compiledShader", F).GetValue(fx);
            var image = doc.Compose();
            try { var c = image.GetPixel(8,8); Check(Mathf.Abs(c.r) < .005f && Mathf.Abs(c.g-.5f)<.005f && Mathf.Abs(c.b-1)<.005f, "GPU sampling/clamp " + c); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            p.curveValue = AnimationCurve.Linear(0, .2f, 1, .6f);
            image = doc.Compose();
            try { var c=image.GetPixel(8,8); Check(Mathf.Abs(c.r-.2f)<.005f && Mathf.Abs(c.g-.4f)<.005f && Mathf.Abs(c.b-.6f)<.005f, "live update " + c); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            Check(ReferenceEquals(shader,typeof(ShaderFX).GetField("compiledShader",F).GetValue(fx)), "no recompile");
            var c1 = new AnimationCurve(new Keyframe(0,-2,0,4,.2f,.4f){weightedMode=WeightedMode.Both},new Keyframe(1,3,2,0,.3f,.6f){weightedMode=WeightedMode.Both});
            var tex = Bake(c1);
            Check(tex.width==512 && tex.height==2 && tex.format==TextureFormat.RFloat, "LUT format");
            Bake(c1); Bake(new AnimationCurve(c1.keys));
            Check(Count()==1,"unchanged curve cache");
            var key=c1[0]; key.value=-3; c1.MoveKey(0,key); Bake(c1);
            Check(Count()==2 && tex.GetPixel(0,0).r == -3 && tex.GetPixel(511,1).r==3,"mutation + unbounded Y");
            p.curveValue=c1;
            var copy=(ShaderFXParameter)typeof(ShaderFXParameter).GetMethod("Copy",F).Invoke(p,null);
            key=copy.curveValue[0];key.value=10;copy.curveValue.MoveKey(0,key);
            Check(p.curveValue[0].value==-3,"independent copy");
            var writer=typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter");
            string source=(string)writer.GetMethod("BuildSource",F).Invoke(null,new object[]{fx,"Tests/Curve"});
            var loaded=Create(source);
            for(int i=0;i<=100;i++) Check(Mathf.Abs(Params(loaded)[0].curveValue.Evaluate(i/100f)-c1.Evaluate(i/100f))<1e-5,"preset keys/weights");
            string json=JsonUtility.ToJson(p);
            var restored=JsonUtility.FromJson<ShaderFXParameter>(json);
            Check(restored.curveValue.Equals(c1),"serialization");
            var step=new AnimationCurve(new Keyframe(0,0,0,float.PositiveInfinity),new Keyframe(1,1,float.PositiveInfinity,0));
            string text=(string)utility.GetMethod("Format",F).Invoke(null,new object[]{step});
            var roundtrip=(AnimationCurve)utility.GetMethod("Parse",F).Invoke(null,new object[]{text});
            Check(roundtrip.Evaluate(.5f)==0,"step tangent roundtrip");
            Bake(new AnimationCurve()); Check(Count()==3 && tex.GetPixel(250,0).r==0,"empty curve");
            var reader=typeof(WhimTexApi).GetMethod("ReadLiveFxParameters",F);
            var tokenType=reader.GetParameters()[0].ParameterType;
            var payload=tokenType.GetMethod("Parse",new[]{typeof(string)}).Invoke(null,new object[]{"[{\"name\":\"_Profile\",\"type\":\"Curve\",\"value\":\""+text+"\"}]" });
            var api=(List<ShaderFXParameter>)typeof(WhimTexApi).GetMethod("ReadLiveFxParameters",F).Invoke(null,new object[]{payload,doc});
            Check(api[0].curveValue.Evaluate(.5f)==0,"API curve input");
            var viewType=typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView");
            var view=(VisualElement)Activator.CreateInstance(viewType,F,null,new object[]{fx},null);
            Check(view.Q<CurveField>() != null,"standard curve field");
            var metadata=typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata");
            var repeated=(List<ShaderFXParameter>)metadata.GetMethod("Parse",F).Invoke(null,new object[]{"// @param curve _P = keys((0,2,0,0,0,0,0))\n// @param curve _P",false,null});
            Check(repeated.Count==1 && repeated[0].curveValue.Evaluate(.5f)==2,"duplicate default retained");
            foreach (string preset in new[] { "linear", "easeInOut", "one" })
            {
                var parsed = (List<ShaderFXParameter>)metadata.GetMethod("Parse", F).Invoke(null,
                    new object[] { "// @param curve _P = " + preset, false, null });
                var expected = preset == "one" ? AnimationCurve.Linear(0,1,1,1) :
                    preset == "linear" ? AnimationCurve.Linear(0,0,1,1) : AnimationCurve.EaseInOut(0,0,1,1);
                for (int i=0;i<=20;i++)
                    Check(Mathf.Abs(parsed[0].curveValue.Evaluate(i/20f)-expected.Evaluate(i/20f))<1e-6f,"named default "+preset);
            }
            foreach (string preset in new[] { "easeIn", "easeOut" })
            {
                var parsed = (List<ShaderFXParameter>)metadata.GetMethod("Parse", F).Invoke(null,
                    new object[] { "// @param curve _P = " + preset, false, null });
                var curve = parsed[0].curveValue;
                for (int i=0;i<=20;i++)
                {
                    float t=i/20f;
                    float expected=preset=="easeIn" ? t*t : 1-(1-t)*(1-t);
                    Check(Mathf.Abs(curve.Evaluate(t)-expected)<1e-6f,"named default "+preset);
                }
                string saved=(string)utility.GetMethod("Format",F).Invoke(null,new object[]{curve});
                var restoredCurve=(AnimationCurve)utility.GetMethod("Parse",F).Invoke(null,new object[]{saved});
                Check(restoredCurve.Equals(curve),"easing preset roundtrip");
            }
            foreach(string bad in new[]{"keys((0,0))","keys((0,0,0,0,0,0,4))","keys((0,NaN,0,0,0,0,0))"})
            {
                bool rejected=false;
                try { utility.GetMethod("Parse",F).Invoke(null,new object[]{bad}); } catch(TargetInvocationException e) { rejected=e.InnerException is FormatException; }
                Check(rejected,"bad keys rejected");
            }
            return "PASS: "+checks+" curve parsing, GPU sampling, cache, copy, serialization and preset checks.";
        }
        finally
        {
            ((IDisposable)lut).Dispose();
            foreach(var fx in effects) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
