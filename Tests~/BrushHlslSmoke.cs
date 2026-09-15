using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class BrushHlslSmoke
{
    public static string Main()
    {
        var assembly=typeof(WhimTexApi).Assembly;
        var stat=BindingFlags.Static|BindingFlags.NonPublic;
        var instance=BindingFlags.Instance|BindingFlags.NonPublic;
        var parser=typeof(WhimTexApi).GetMethod("ReadBrushClipboard",stat);
        var programType=assembly.GetType("DCFApixels.WhimTex.BrushTipProgram");
        var program=Activator.CreateInstance(programType,true);
        var bake=programType.GetMethod("Bake",instance);
        string source=(string)programType.GetField("DefaultSource",stat).GetRawConstantValue();
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        string Quote(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n")+"\"";
        void Reject(string json)
        {
            try{parser.Invoke(null,new object[]{json,null});}
            catch(TargetInvocationException){checks++;return;}
            throw new Exception("Invalid brush JSON accepted");
        }
        Texture2D first=null,second=null;
        object settings=null;
        object restored=null;
        try
        {
            foreach(string kind in new[]{"Standard","Texture","HLSL"})
            {
                string extra=kind=="Texture"?",\"url\":\"https://example.com/tip.png\"":kind=="HLSL"?",\"code\":"+Quote(source):"";
                string json="{\"format\":\"whimtex.brush\",\"version\":1,\"source\":"+Quote(kind=="Texture"?"Standard":kind)+extra+"}";
                var args=new object[]{json,null};
                var parsed=parser.Invoke(null,args);
                Check(parsed!=null,kind+" parsed");
                Check(kind!="Texture" || (string)args[1]=="https://example.com/tip.png","URL preserved");
            }
            Reject("{\"format\":\"whimtex.brush\",\"version\":1,\"source\":\"Texture\",\"url\":\"https://example.com/a.png\"}");
            Reject("{\"format\":\"whimtex.brush\",\"version\":1,\"source\":\"Standard\",\"url\":\"file:///x.png\"}");
            Reject("{\"format\":\"whimtex.brush\",\"version\":1,\"source\":\"Standard\",\"settings\":{\"flow\":2}}");
            Reject("{\"format\":\"whimtex.brush\",\"version\":1,\"source\":\"Standard\",\"settings\":{\"typo\":1}}");
            var values=new List<ShaderFXParameter>();
            first=(Texture2D)bake.Invoke(program,new object[]{source,values,128});
            Check(first.width==128 && first.height==128,"Baked resolution");
            Check(first.GetPixel(64,64).a<.01f,"Ring center transparent");
            Check(first.GetPixel(105,64).a>.9f,"Ring visible");
            var shader=programType.GetField("shader",instance).GetValue(program);
            values.Add(new ShaderFXParameter{name="_Thickness",type=ShaderFXParameterType.Float,floatValue=.03f});
            second=(Texture2D)bake.Invoke(program,new object[]{source,values,128});
            Check(ReferenceEquals(shader,programType.GetField("shader",instance).GetValue(program)),"Parameter update reuses shader");
            Check(second.GetPixel(99,64).a<first.GetPixel(99,64).a,"Parameter affects cached pixels");
            string root="{\"format\":\"whimtex.brush\",\"version\":1,\"source\":\"HLSL\",\"code\":"+Quote(source)+",\"settings\":{\"tipChannel\":\"Luminance\",\"tipSdf\":true}}";
            settings=parser.Invoke(null,new object[]{root,null});
            var settingsType=settings.GetType();
            var dynamics=settingsType.GetField("dynamics").GetValue(settings);
            var dt=dynamics.GetType();
            var apply=settingsType.GetMethod("ApplyHlsl",instance);
            apply.Invoke(settings,new object[]{source,values,64});
            Check((bool)dt.GetField("tipSdf").GetValue(dynamics),"HLSL preserves SDF");
            Check(dt.GetField("tipChannel").GetValue(dynamics).ToString()=="Luminance","HLSL preserves channel");
            var before=(Texture2D)dt.GetField("tip").GetValue(dynamics);
            try{apply.Invoke(settings,new object[]{source+"\ninvalid syntax",values,64});throw new Exception("Invalid shader accepted");}
            catch(TargetInvocationException){checks++;}
            Check(ReferenceEquals(dt.GetField("tip").GetValue(dynamics),before),"Failed shader keeps tip");
            string saved=JsonUtility.ToJson(settings);
            restored=JsonUtility.FromJson(saved,settingsType);
            var restoredDynamics=settingsType.GetField("dynamics").GetValue(restored);
            dt.GetField("tip").SetValue(restoredDynamics,null);
            Check((bool)settingsType.GetMethod("TryRestoreBrushTip",instance).Invoke(restored,null),"Restore HLSL after serialization");
            var restoredTip=(Texture2D)dt.GetField("tip").GetValue(restoredDynamics);
            Check(restoredTip!=before && restoredTip.width==64,"Restored independent tip");
            Check(restoredTip.GetPixel(50,32)==before.GetPixel(50,32),"Restored same pixels");
            Check((bool)dt.GetField("tipSdf").GetValue(restoredDynamics),"Restore retains SDF");
            var library=assembly.GetType("DCFApixels.WhimTex.BrushPresetLibrary");
            object preset=library.GetMethod("Capture",stat).Invoke(null,new[]{settings});
            string presetJson=JsonUtility.ToJson(preset);
            Check(presetJson.Contains("BrushTip(float2 uv)") && presetJson.Contains("_Thickness"),"Preset keeps source and parameters");
            string exported=(string)programType.GetMethod("Export",stat).Invoke(null,new object[]{source,values});
            Check(exported.StartsWith("// @whimtex-brush Shapes/Ring"),"Export header first");
            Check(exported.Contains("= 0.03"),"Export current values");
            var previousTarget=RenderTexture.active;
            bool previousSrgb=GL.sRGBWrite;
            foreach(string file in System.IO.Directory.GetFiles("Packages/com.dcfapixels.whimtex/Documentation~/Examples/Brushes","*.json"))
            {
                var example=parser.Invoke(null,new object[]{System.IO.File.ReadAllText(file),null});
                var exampleDynamics=settingsType.GetField("dynamics").GetValue(example);
                Check(example!=null,"Example parsed: "+file);
                if(dt.GetField("source").GetValue(exampleDynamics).ToString()!="HLSL")continue;
                try
                {
                    apply.Invoke(example,new object[]{dt.GetField("hlslCode").GetValue(exampleDynamics),dt.GetField("hlslParameters").GetValue(exampleDynamics),64});
                    Check(dt.GetField("tip").GetValue(exampleDynamics)!=null,"Example shader baked");
                }
                finally{settingsType.GetMethod("ReleasePresetTip",instance).Invoke(example,null);}
            }
            Check(RenderTexture.active==previousTarget && GL.sRGBWrite==previousSrgb,"Baking preserves render state");
            return "Brush HLSL/clipboard checks: "+checks;
        }
        finally
        {
            if(first!=null)UnityEngine.Object.DestroyImmediate(first);
            if(second!=null)UnityEngine.Object.DestroyImmediate(second);
            ((IDisposable)program).Dispose();
            if(settings!=null)settings.GetType().GetMethod("ReleasePresetTip",instance).Invoke(settings,null);
            if(restored!=null)restored.GetType().GetMethod("ReleasePresetTip",instance).Invoke(restored,null);
        }
    }
}
