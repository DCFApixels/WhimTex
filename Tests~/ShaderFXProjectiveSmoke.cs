using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class ShaderFXProjectiveSmoke
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    [Serializable] public class Holder { public ShaderFXTransform transform; }
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name,F).Invoke(target,args);
    static TextureTransform Layer(ShaderFXTransform t, Vector2 size) => (TextureTransform)Call(t,"ToLayerTransform",size);
    static ShaderFXTransform Edit(ShaderFXTransform t,string name, params object[] args) { object box=t; Call(box,name,args); return (ShaderFXTransform)box; }
    public static string Main()
    {
        checks=0;
        var assembly=typeof(ShaderFX).Assembly;
        var dimensions=new Vector2(64,32);
        var old=JsonUtility.FromJson<Holder>("{\"transform\":{\"position\":{\"x\":0.25,\"y\":0.75},\"size\":{\"x\":0.5,\"y\":2},\"rotation\":27}}").transform;
        Check(old.storage==TransformStorage.TRS && old.position.x==.25 && old.size.y==2 && old.rotation==27,"Legacy serialized transform");
        var rng=new System.Random(43);
        for(int i=0;i<300;i++)
        {
            var t=ShaderFXTransform.Default;
            t.position=new Double2(rng.NextDouble(),rng.NextDouble());
            t.size=new Double2(.1+rng.NextDouble()*2,.1+rng.NextDouble()*2); t.rotation=rng.NextDouble()*360;
            var m=Layer(t,dimensions).ToMatrix(64,32);
            double c=Math.Cos(t.rotation*Math.PI/180),s=Math.Sin(t.rotation*Math.PI/180);
            var uv=new Double2(rng.NextDouble(),rng.NextDouble());
            var expected=new Double2(t.position.x+c*t.size.x*(uv.x-.5)-s*t.size.y*(uv.y-.5)/2,
                t.position.y+s*t.size.x*(uv.x-.5)*2+c*t.size.y*(uv.y-.5));
            var p=m.Point(uv);
            Check(Math.Abs(p.x-expected.x)+Math.Abs(p.y-expected.y)<1e-12,"TRS equivalence");
        }
        var matrix=new ProjectiveMatrix { m00=.72,m01=.22,m02=.05,m10=.08,m11=.9,m12=.03,m20=.24,m21=.12,m22=1 };
        var area=ShaderFXTransform.Default;
        area.storage=TransformStorage.Projective; area.matrix=matrix;
        var copy=JsonUtility.FromJson<Holder>(JsonUtility.ToJson(new Holder{transform=area})).transform;
        Check(copy.matrix.Equals(matrix) && copy.storage==TransformStorage.Projective,"Projective serialization");
        var converted=(ShaderFXTransform)typeof(ShaderFXTransform).GetMethod("FromLayerTransform",F).Invoke(null,new object[]{Layer(area,dimensions),dimensions});
        Check(converted.matrix.Equals(matrix),"Canvas roundtrip preserves matrix");
        var moved=Edit(area,"EditPosition",new Double2(.7,.6),dimensions);
        Check(moved.storage==TransformStorage.Projective && moved.matrix.m20==matrix.m20 && moved.matrix.m21==matrix.m21,"Position preserves perspective");
        var rotated=Edit(area,"EditRotation",42d,dimensions);
        Check(rotated.storage==TransformStorage.Projective && rotated.matrix.ValidUnitQuad(),"Rotation preserves projective mode");
        var scaled=Edit(area,"EditSize",new Double2(.9,1.2),dimensions);
        Check(scaled.storage==TransformStorage.Projective && scaled.matrix.ValidUnitQuad(),"Scale preserves projective mode");
        object invalid=area;
        Check(!(bool)Call(invalid,"TrySetMatrix",default(ProjectiveMatrix)) && ((ShaderFXTransform)invalid).matrix.Equals(matrix),"Reject singular matrix atomically");
        // The shared canvas gesture implementation must retain projective results for FX.
        var manipType=typeof(TextureCompositorWindow).GetNestedType("PreviewTransformManipulator",F);
        var manip=Activator.CreateInstance(manipType,new object[]{null});
        void Set(string name,object value) => manipType.GetField(name,F).SetValue(manip,value);
        Set("original",TextureTransform.Default); Set("size",dimensions);
        foreach(var gesture in new[]{(0,false,false),(1,false,false),(0,true,true),(0,false,true)})
        {
            Set("handle",gesture.Item1);Set("lastAlt",gesture.Item3);
            object[] args={new Vector2(3,2),new Vector2(3,2),gesture.Item2,true,null};
            Check((bool)Call(manip,"UpdateProjective",args),"Corner/skew/perspective gesture");
            var result=(TextureTransform)args[4];
            Check(result.storage==TransformStorage.Projective && result.matrix.ValidUnitQuad(),"Valid gesture matrix");
            var fxResult=(ShaderFXTransform)typeof(ShaderFXTransform).GetMethod("FromLayerTransform",F).Invoke(null,new object[]{result,dimensions});
            Check(fxResult.matrix.Equals(result.matrix),"FX keeps gesture deformation");
        }
        var doc=ScriptableObject.CreateInstance<TextureCompositor>(); doc.width=64;doc.height=32;
        ShaderFX fx=null;
        var output=RenderTexture.GetTemporary(64,32,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var readback=new Texture2D(64,32,TextureFormat.RGBAFloat,false,true);
        var active=RenderTexture.active;
        Shader legacy=null;
        try
        {
            string code="// @param transform2D _Area\nfloat4 ApplyFX(float2 uv, float4 color) { float2 p=_Area_ToLocal(uv); float2 q=_Area_ToInput(p); return float4(p, length(q-uv),1); }";
            fx=(ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft",F).Invoke(null,new object[]{doc,code,new List<ShaderFXParameter>()});
            Call(fx,"ApplyAgentDraft");
            var parameters=(List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters",F).GetValue(fx);
            var field=typeof(ShaderFX).GetField("compiledShader",F);
            var shader=(Shader)field.GetValue(fx);
            object context=Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"),doc,null,64,32,1f,true,true,null);
            Material Render()
            {
                var material=(Material)Call(fx,"GetMaterial",context);
                Graphics.Blit(Texture2D.whiteTexture,output,material);
                RenderTexture.active=output; readback.ReadPixels(new Rect(0,0,64,32),0,0);readback.Apply();
                return material;
            }
            foreach(var transform in new[]{area,moved,rotated,scaled})
            {
                parameters[0].transformValue=transform;
                Render();
                transform.matrix.TryInverse(out var inv);
                for(int y=0;y<32;y++) for(int x=0;x<64;x++)
                {
                    var expected=inv.Point(new Double2((x+.5)/64,(y+.5)/32));
                    var actual=readback.GetPixel(x,y);
                    Check(Math.Abs(actual.r-expected.x)<.0001 && Math.Abs(actual.g-expected.y)<.0001 && actual.b<.0001,"GPU inverse/forward");
                }
                Check(ReferenceEquals(field.GetValue(fx),shader),"Value changes do not recompile");
            }
            parameters[0].transformValue=area;
            var writer=assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter");
            string preset=(string)writer.GetMethod("BuildSource",F).Invoke(null,new object[]{fx,"Tests/Projective"});
            Check(preset.Contains("matrix("),"Preset writes matrix");
            object[] parseArgs={preset,true,null};
            var parsed=(List<ShaderFXParameter>)assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata").GetMethod("Parse",F).Invoke(null,parseArgs);
            Check(parsed[0].transformValue.matrix.Equals(matrix),"Preset preserves exact matrix");
            parameters[0].transformValue=new ShaderFXTransform { position=new Double2(.123456789123,.6),size=new Double2(.834567891234,1.1),rotation=23.123456789123 };
            string trsPreset=(string)writer.GetMethod("BuildSource",F).Invoke(null,new object[]{fx,"Tests/TRS"});
            var trsParsed=(List<ShaderFXParameter>)assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata").GetMethod("Parse",F).Invoke(null,new object[]{trsPreset,true,null});
            Check(trsParsed[0].transformValue.position==parameters[0].transformValue.position &&
                trsParsed[0].transformValue.size==parameters[0].transformValue.size &&
                trsParsed[0].transformValue.rotation==parameters[0].transformValue.rotation,"Preset preserves double TRS");
            parameters[0].transformValue=area;
            var editor=Editor.CreateEditor(fx);
            try { Check(editor.CreateInspectorGUI()!=null,"Inspector builds"); }
            finally { UnityEngine.Object.DestroyImmediate(editor); }

            // Stored shaders made by the previous wrapper continue working without Apply.
            string source=(string)typeof(ShaderFX).GetField("appliedSource",F).GetValue(fx);
            string prefix="_WhimTex_Area_"+parameters[0].id+"_";
            foreach(string direction in new[]{"ToLocal","ToInput"})
            {
                string current=$"float2 _Area_{direction}(float2 uv) {{ float3 p = float3(uv, 1.0); float w = dot({prefix}{direction}Row2.xyz, p); w = w < 0.0 ? min(w, -1e-7) : max(w, 1e-7); return float2(dot({prefix}{direction}Row0.xyz, p), dot({prefix}{direction}Row1.xyz, p)) / w; }}";
                string previous=$"float2 _Area_{direction}(float2 uv) {{ float3 p = float3(uv, 1.0); return float2(dot({prefix}{direction}Row0.xyz, p), dot({prefix}{direction}Row1.xyz, p)); }}";
                Check(source.Contains(current),"Locate helper");
                source=source.Replace(current,previous).Replace($"float4 {prefix}{direction}Row2;","");
            }
            legacy=ShaderUtil.CreateShaderAsset(source,true);legacy.hideFlags=HideFlags.HideAndDontSave;
            Call(fx,"ReleaseMaterial");
            field.SetValue(fx,legacy);
            typeof(ShaderFX).GetField("appliedSource",F).SetValue(fx,source);
            typeof(ShaderFX).GetField("code",F).SetValue(fx,code+"\n// pending edit");
            var upgraded=Render();
            Check(upgraded.shader!=legacy,"Old wrapper upgraded transiently");
            matrix.TryInverse(out var inverse);
            var center=inverse.Point(new Double2(32.5/64,16.5/32));var color=readback.GetPixel(32,16);
            Check(Math.Abs(color.r-center.x)+Math.Abs(color.g-center.y)<.0001,"Old wrapper handles perspective");
            Check(Render()==upgraded && Render().shader==upgraded.shader,"Upgrade cached");
            Check(((string)typeof(ShaderFX).GetField("code",F).GetValue(fx)).EndsWith("// pending edit"),"Pending code preserved");
            Call(fx,"ReleaseMaterial");field.SetValue(fx,shader);
            return "FX projective checks passed: "+checks;
        }
        finally
        {
            RenderTexture.active=active;
            if(fx!=null)UnityEngine.Object.DestroyImmediate(fx);
            if(legacy!=null)UnityEngine.Object.DestroyImmediate(legacy);
            UnityEngine.Object.DestroyImmediate(readback);RenderTexture.ReleaseTemporary(output);
            UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
