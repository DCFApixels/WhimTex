using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public class SoftRangeSmokeWindow : EditorWindow { }

public static class SoftRangeSmoke
{
    public static string Main()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
        var assembly=typeof(ShaderFX).Assembly;
        var metadata=assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata");
        List<ShaderFXParameter> Parse(string text)=>(List<ShaderFXParameter>)metadata.GetMethod("Parse",flags).Invoke(null,new object[]{text,false,null});
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        const string code="// @param float _Strength = 1 [0 .. 2]\n// @param float _Strength [~0 .. ~2] // Soft control\nfloat4 ApplyFX(float2 uv,float4 color){return _Strength;}";
        var parsed=Parse(code);
        Check(parsed.Count==1 && !parsed[0].controls[0].softMaximum && parsed[0].controls[1].softMaximum,"Per-control range metadata");
        Check(Parse("// @param float _X = 5 [~0 .. ~2]")[0].floatValue==5,"Out-of-range default");
        Check(Parse("// @param float _X [~-2 .. ~2]")[0].floatValue==0,"Optional default");
        foreach(var bad in new[]{"// @param float _X [~0 ..]","// @param float _X [.. ~2]","// @param float _X [~2 .. 2]","// @param float _X [3 .. ~2]","// @param bool _X [~0 .. ~2]","// @param float _X [0 .. ~Infinity]", "// @param float _X ~[0 .. 2]", "// @param float _X [~ .. 2]"})
        {
            bool rejected=false;try{Parse(bad);}catch(TargetInvocationException e)when(e.InnerException is FormatException){rejected=true;}
            Check(rejected,"Accepted invalid soft range: "+bad);
        }
        ShaderFX fx=null; SoftRangeSmokeWindow window=null;
        var owner=ScriptableObject.CreateInstance<TextureCompositor>();owner.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            fx=(ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft",flags).Invoke(null,new object[]{owner,code,new List<ShaderFXParameter>()});
            typeof(ShaderFX).GetMethod("ApplyAgentDraft",flags).Invoke(fx,null);
            var values=(List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters",flags).GetValue(fx);
            var view=(VisualElement)Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView"),flags,null,new object[]{fx},null);
            window=ScriptableObject.CreateInstance<SoftRangeSmokeWindow>();window.titleContent=new GUIContent("Soft range test");
            window.rootVisualElement.Add(view);window.ShowUtility();
            var soft=view.Q<VisualElement>(className:"whimtex-soft-range");
            var number=soft.Q<FloatField>();var slider=soft.Q<Slider>();
            var fieldType=soft.GetType();
            foreach(var lowerSoft in new[]{false,true})
            foreach(var upperSoft in new[]{false,true})
            {
                var side=(BaseField<float>)Activator.CreateInstance(fieldType,flags,null,new object[]{"Sides",0f,2f,lowerSoft,upperSoft},null);
                window.rootVisualElement.Add(side);
                var input=side.Q<FloatField>();
                input.value=-3;
                Check(side.value==(lowerSoft?-3:0) && input.value==side.value,"Independent lower boundary");
                input.value=5;
                Check(side.value==(upperSoft?5:2) && input.value==side.value,"Independent upper boundary");
                side.RemoveFromHierarchy();
                var declaration=Parse("// @param float _X ["+(lowerSoft?"~":"")+"0 .. "+(upperSoft?"~":"")+"2]")[0];
                Check(declaration.softMinimum==lowerSoft && declaration.softMaximum==upperSoft,"Boundary parsing");
                var clamp=typeof(ShaderFXParameter).GetMethod("Clamp",flags);
                Check((float)clamp.Invoke(declaration,new object[]{-3f})==(lowerSoft?-3:0) && (float)clamp.Invoke(declaration,new object[]{5f})==(upperSoft?5:2),"Per-boundary clamp");
            }
            number.value=5;
            Check(values[0].floatValue==5 && number.value==5 && slider.value==2,"Numeric input above range");
            number.value=-3;
            Check(values[0].floatValue==-3 && number.value==-3 && slider.value==0,"Numeric input below range");
            slider.value=1.5f;
            Check(values[0].floatValue==1.5f && number.value==1.5f,"Slider after outside input");
            var hard=view.Q<Slider>(); hard.value=.5f;hard.value=5;
            Check(values[0].floatValue==2,"Hard range still clamps");
            number.value=5;
            var context=Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"),owner,null,4,4,1f,true,true);
            var material=(Material)typeof(ShaderFX).GetMethod("GetMaterial",flags).Invoke(fx,new[]{context});
            Check(material.GetFloat("_Strength")==5,"GPU receives outside value");
            var writer=assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter");
            string exported=(string)writer.GetMethod("BuildSource",flags).Invoke(null,new object[]{fx,"Test/Soft"});
            Check(exported.Contains("_Strength [~0 .. ~2] // Soft control"),"Export lost tilde without initializer");
            var roundtrip=Parse(exported);Check(roundtrip[0].floatValue==5 && roundtrip[0].controls[1].softMaximum,"Export value/controls roundtrip");
            var copy=JsonUtility.FromJson<ShaderFXParameter>(JsonUtility.ToJson(values[0]));Check(copy.controls[1].softMaximum,"Serialization");
            var brushType=assembly.GetType("DCFApixels.WhimTex.BrushTipProgram");
            const string brush="// @whimtex-brush Test/Soft\n// @param float _Strength = 5 [~0 .. ~2]\nfloat4 BrushTip(float2 uv){return float4(_Strength,0,0,1);}";
            var brushValues=(List<ShaderFXParameter>)brushType.GetMethod("Parse",flags).Invoke(null,new object[]{brush,null});
            Check((float)typeof(ShaderFXParameter).GetMethod("Clamp",flags).Invoke(brushValues[0],new object[]{5f})==5,"Brush GPU clamp");
            string brushExport=(string)brushType.GetMethod("Export",flags).Invoke(null,new object[]{brush,brushValues});
            Check(brushExport.Contains("= 5 [~0 .. ~2]"),"Brush export");
            return "PASS: parsing/validation, attached UI typing and dragging, linked hard/soft controls, shader upload, FX/brush export and serialization.";
        }
        finally
        {
            if(window!=null)window.Close();
            if(fx!=null){Undo.ClearUndo(fx);UnityEngine.Object.DestroyImmediate(fx);}
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }
}
