using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class AreaCookieLightingBenchmark
{
    const string Root="Packages/com.dcfapixels.whimtex/";
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    [Serializable] public class Row { public string name; public int width,height; public double medianMs; public double[] samples; }
    [Serializable] public class Report { public string gpu,api,unity; public string timing="Warmed submission + GPU completion through a separate 1x1 target; no full-size CPU readback."; public List<Row> rows=new(); }
    public static string Run()=>TestContext.Run("Area/Cookie opt-in shader variants: unchanged ordinary lights and warmed 8K cost",t=>
    {
        var report=new Report{gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),unity=Application.unityVersion};
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>(); doc.width=doc.height=8192;
        var owned=new List<Object>(); RenderTexture target=null;
        var prior=RenderTexture.active; bool srgb=GL.sRGBWrite;
        var fence=RenderTexture.GetTemporary(1,1,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
        var cpu=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true); owned.Add(cpu);
        Material Make(string library,string keywords)
        {
            string source="Shader \"Hidden/WhimTex/LightingBenchmark/"+Guid.NewGuid().ToString("N")+"\"\n{\nSubShader\n{\nPass\n{\nCull Off ZWrite Off ZTest Always\nCGPROGRAM\n#pragma target 3.5\n#pragma vertex vert_img\n#pragma fragment frag\n"+keywords+
                "#include \"UnityCG.cginc\"\nfloat4 _View;\n"+library+
                "\nfloat4 frag(v2f_img i):SV_Target{float2 uv=_View.xy+i.uv*_View.zw;float3 n=normalize(float3((uv-.5)*.6,1));return WTGlobalLighting(float3(uv*8192,0),float4(.6,.3,.1,.4),n,.15,float3(1,1,1),.5,.4);}\nENDCG\n}\n}\n}\n";
            var shader=ShaderUtil.CreateShaderAsset(source); owned.Add(shader);
            var mat=new Material(shader); owned.Add(mat); return mat;
        }
        void Bind(Material mat)=>typeof(WhimTexDocument).GetMethod("BindLighting",F).Invoke(doc,new object[]{mat});
        Color Draw(Material mat)
        {
            Graphics.Blit(Texture2D.blackTexture,target,mat); Graphics.Blit(target,fence);
            RenderTexture.active=fence; cpu.ReadPixels(new Rect(0,0,1,1),0,0); cpu.Apply(); return cpu.GetPixel(0,0);
        }
        void Resize(int w,int h)
        {
            if(target!=null) RenderTexture.ReleaseTemporary(target);
            target=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
        }
        void Measure(string name,Material mat)
        {
            Bind(mat); Draw(mat); Draw(mat); var samples=new double[7];
            for(int i=0;i<samples.Length;i++){var clock=Stopwatch.StartNew(); Draw(mat); clock.Stop(); samples[i]=clock.Elapsed.TotalMilliseconds;}
            var sorted=(double[])samples.Clone(); Array.Sort(sorted); report.rows.Add(new Row{name=name,width=target.width,height=target.height,medianMs=sorted[3],samples=samples});
        }
        try
        {
            byte[] reference=File.ReadAllBytes(Root+"Tests~/Cases/FX/PointSpotLightingReference.hlsl");
            using(var sha=SHA256.Create()) t.Equal("0AC2CA1A2D8FEE3D6954F35BB07D8B67666C81CBA72337A82FB10B45A908DCCF",BitConverter.ToString(sha.ComputeHash(reference)).Replace("-",""),"Pre-Area reference from 51ef815 is unchanged");
            var before=Make(System.Text.Encoding.UTF8.GetString(reference),"");
            var after=Make("#include \"Packages/com.dcfapixels.whimtex/src/Shaders/DocumentLighting.cginc\"\n","#pragma multi_compile_local __ WHIMTEX_AREA_LIGHTS\n#pragma multi_compile_local __ WHIMTEX_LIGHT_COOKIES\n");
            foreach(var kind in new[]{WhimTexLightType.Directional,WhimTexLightType.Point,WhimTexLightType.Spot})
            {var light=doc.AddLight(kind); light.color=new Color(2,.8f,.3f); light.intensity=.7f; light.position=new Vector3(4300,4000,1600); light.range=10000;}
            before.SetVector("_View",new Vector4(0,0,1,1)); after.SetVector("_View",new Vector4(0,0,1,1));
            Resize(256,256); Bind(before); Draw(before); var a=Read(target); Bind(after); Draw(after); var b=Read(target);
            for(int i=0;i<a.Length;i++) t.Equal(a[i],b[i],"No-feature pixel parity");
            t.True(!after.IsKeywordEnabled("WHIMTEX_AREA_LIGHTS")&&!after.IsKeywordEnabled("WHIMTEX_LIGHT_COOKIES"),"Both rare features compiled out");
            t.True(typeof(WhimTexDocument).GetField("lightCookieAtlas",F).GetValue(doc)==null,"No Cookie resources");
            t.True(typeof(WhimTexDocument).GetField("lightRight",F).GetValue(doc)==null,"No extra Area/Cookie constant arrays");
            Resize(8192,8192); Measure("ordinary-before-8k",before); Measure("ordinary-after-8k",after);
            Measure("ordinary-after-8k-reverse",after); Measure("ordinary-before-8k-reverse",before);
            Resize(1024,768); before.SetVector("_View",new Vector4(.4375f,.453125f,.125f,.09375f)); after.SetVector("_View",new Vector4(.4375f,.453125f,.125f,.09375f));
            Measure("ordinary-before-native-region",before); Measure("ordinary-after-native-region",after);
            var area=doc.AddLight(WhimTexLightType.Area); area.size=new Vector2(1500,750); area.range=10000;
            Measure("rectangle-native-region",after); t.True(after.IsKeywordEnabled("WHIMTEX_AREA_LIGHTS"),"Area enables only its variant");
            area.areaShape=WhimTexAreaLightShape.Disk; Measure("disk-native-region",after); area.enabled=false;
            var cookie=new Texture2D(64,64,TextureFormat.RGBA32,false,true); owned.Add(cookie); var colors=new Color[4096];
            for(int i=0;i<colors.Length;i++) colors[i]=new Color((i%64)/63f,(i/64)/63f,.5f,1); cookie.SetPixels(colors); cookie.Apply();
            doc.lights[2].cookie=cookie; Measure("cookie-native-region",after);
            t.True(!after.IsKeywordEnabled("WHIMTEX_AREA_LIGHTS")&&after.IsKeywordEnabled("WHIMTEX_LIGHT_COOKIES"),"Cookie does not enable Area");
            area.enabled=true; Measure("area-and-cookie-native-region",after);
            area.enabled=false; doc.lights[2].cookie=null; Bind(after); t.True(!after.IsKeywordEnabled("WHIMTEX_AREA_LIGHTS")&&!after.IsKeywordEnabled("WHIMTEX_LIGHT_COOKIES"),"Disabling rare sources restores ordinary variant");
            foreach(var obj in owned) if(obj is Shader shader) foreach(var message in ShaderUtil.GetShaderMessages(shader)) t.True(false,"Shader diagnostic: "+message.message);
            var json=Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json",true);
            string text=(string)json.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{report});
            t.True(text.Contains("ordinary-before-8k"),"Timing rows serialized");
            Directory.CreateDirectory("Temp/WhimTex/area-cookie"); File.WriteAllText("Temp/WhimTex/area-cookie/performance.json",text);
        }
        finally
        {
            if(target!=null) RenderTexture.ReleaseTemporary(target); RenderTexture.ReleaseTemporary(fence); Object.DestroyImmediate(doc);
            for(int i=owned.Count-1;i>=0;i--) if(owned[i]!=null) Object.DestroyImmediate(owned[i]); RenderTexture.active=prior; GL.sRGBWrite=srgb;
        }
    });
    static Color[] Read(RenderTexture rt)
    {
        var prior=RenderTexture.active; var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
        try{RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();return t.GetPixels();}
        finally{Object.DestroyImmediate(t);RenderTexture.active=prior;}
    }
}
