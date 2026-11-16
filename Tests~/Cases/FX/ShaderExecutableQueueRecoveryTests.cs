using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

public static class ShaderExecutableQueueRecoveryTests
{
    const BindingFlags F=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly Type Cache=typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex.ShaderExecutableCache",true);
    static object Get(object o,string n) {var t=o as Type??o.GetType();return t.GetField(n,F)?.GetValue(o is Type?null:o)??t.GetProperty(n,F)?.GetValue(o is Type?null:o);}
    static void Set(object o,string n,object v) {var t=o as Type??o.GetType();var f=t.GetField(n,F);if(f!=null)f.SetValue(o is Type?null:o,v);else t.GetProperty(n,F).SetValue(o is Type?null:o,v);}
    static object Call(object o,string n,params object[] a)=>(o as Type??o.GetType()).GetMethod(n,F).Invoke(o is Type?null:o,a);
    static int Active=>(int)Get(Cache,"ActiveCount");
    static IList Entries=>(IList)Get(Cache,"entries");
    static string Source()=>"Shader \"Hidden/WhimTex/Recovery_"+Guid.NewGuid().ToString("N")+"\" { SubShader { Cull Off ZWrite Off ZTest Always Pass { HLSLPROGRAM\n#pragma vertex vert\n#pragma fragment frag\n#include \"UnityCG.cginc\"\nfloat4 vert(float4 p:POSITION):SV_POSITION{return UnityObjectToClipPos(p);}\nfloat4 frag():SV_Target{return float4(.25,.5,.75,1);}\nENDHLSL\n} } }";
    public static async Task<object> Run()
    {
        var t=new TestContext();var owned=new List<object>();Shader borrowed=null;Material variant=null,draw=null;
        RenderTexture target=null;Texture2D read=null;var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try
        {
            double deadline=EditorApplication.timeSinceStartup+20;
            while(Active!=0||ShaderUtil.anythingCompiling)
            {if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Wait for existing shader work");await Task.Delay(50);}
            object Native() {var e=Call(Cache,"Acquire",Source(),false,false,null,0,false);owned.Add(e);return e;}
            // Recreate the observed cancelled state deterministically: tracked as submitted,
            // but no native work exists and IsPassCompiled remains false. No user entries change.
            void Cancelled(object e,Shader s)
            {
                Set(e,"shader",s);Set(e,"compilerMaterial",new Material(s){hideFlags=HideFlags.HideAndDontSave});
                Set(e,"started",true);Set(e,"ready",false);Set(Cache,"ActiveCount",Active+1);
            }
            var native=Native();var nativeShader=ShaderUtil.CreateShaderAsset((string)Get(native,"source"),false);
            nativeShader.hideFlags=HideFlags.HideAndDontSave;Cancelled(native,nativeShader);
            Call(Cache,"ReleaseIdleOrphans",false);
            t.True(Entries.Contains(native)&&Active==1,"Owned pending entry is retained");
            Call(native,"Release");
            borrowed=ShaderUtil.CreateShaderAsset(Source(),false);borrowed.hideFlags=HideFlags.HideAndDontSave;
            variant=new Material(borrowed){hideFlags=HideFlags.HideAndDontSave};
            var priorityType=typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex.ShaderGraphPreparation+Priority",true);
            var priority=Activator.CreateInstance(priorityType,F,null,new object[]{false},null);
            var vm=Call(Cache,"AcquireVariant",variant,priority);owned.Add(vm);Cancelled(vm,borrowed);
            ((IList)Get(vm,"requests")).Remove(priority);Call(vm,"Release");
            t.Equal(2,Active,"Both slots occupied by abandoned passes");
            Call(Cache,"ReleaseIdleOrphans",true);
            t.True(Entries.Contains(native)&&Entries.Contains(vm)&&Active==2,"Actual native activity protects pending slots");
            var next=Native();Call(Cache,"Update");
            t.True(!Entries.Contains(native)&&!Entries.Contains(vm),"Idle orphan entries are removed from unified queue");
            t.True(nativeShader==null&&borrowed!=null,"Owned shader released; borrowed shader preserved");
            t.True((bool)Get(next,"started"),"New request starts immediately after recovery");
            deadline=EditorApplication.timeSinceStartup+10;
            while(!(bool)Call(Cache,"IsReady",next))
            {if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Recovered shader compilation");await Task.Delay(20);}
            t.Equal(0,Active,"Recovered queue drains without slot leak");
            draw=(Material)Call(next,"CreateMaterial");target=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();
            read=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);GL.sRGBWrite=false;
            Graphics.Blit(Texture2D.whiteTexture,target,draw);RenderTexture.active=target;read.ReadPixels(new Rect(0,0,1,1),0,0);read.Apply();
            var pixel=read.GetPixel(0,0);t.Near(.25,pixel.r,1e-6,"Recovered red");t.Near(.5,pixel.g,1e-6,"Recovered green");t.Near(.75,pixel.b,1e-6,"Recovered blue");
            return TestContext.Result("passed",t.Checks,"Cancelled orphan recovery, live-owner/busy guards, native/SVM variant ownership and next shader render.");
        }
        catch(Exception error){return TestContext.Result("failed",t.Checks,"Queue recovery",error.ToString());}
        finally
        {
            RenderTexture.active=previous;GL.sRGBWrite=srgb;
            foreach(var e in owned)if(Entries.Contains(e))Call(Cache,"Remove",e);
            foreach(var o in new UnityEngine.Object[]{draw,variant,borrowed,target,read})if(o!=null)UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
