using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object=UnityEngine.Object;
using Edges=DCFApixels.WhimTex.MakeSeamlessLayerBehaviour.PoissonEdges;

public static class SeamlessEmptyEdgesSmoke
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int checks;
    static void Check(bool b,string message){checks++;if(!b)throw new Exception(message);}
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,F).Invoke(o,args);
    static Color[] Read(RenderTexture rt)
    {
        var previous=RenderTexture.active;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
        try{RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);return t.GetPixels();}
        finally{RenderTexture.active=previous;Object.DestroyImmediate(t);}
    }
    public static string Main()
    {
        checks=0;var assembly=typeof(TextureCompositor).Assembly;
        var doc=ScriptableObject.CreateInstance<TextureCompositor>();doc.width=32;doc.height=24;
        var t=new Texture2D(32,24,TextureFormat.RGBAFloat,false,true);var p=new Color[32*24];
        for(int i=0;i<p.Length;i++)p[i]=new Color(.3f+Mathf.Sin(i*.3f),.5f,.7f,i%5==0?0:.8f);
        t.SetPixels(p);t.Apply(false,false);
        var layer=new MakeSeamlessLayerBehaviour{colorRange=LayerColorRange.HDR};
        var source=new FileLayerBehaviour{sourceTexture=t,colorRange=LayerColorRange.HDR};doc.layers.Add(layer);doc.layers.Add(source);
        var cache=(IDisposable)Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.EffectRenderCache"),true);
        var window=ScriptableObject.CreateInstance<EditorWindow>();var focus=EditorWindow.focusedWindow;
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();
        Color[] Render(string method,params object[] args)
        {var rt=(RenderTexture)Call(doc,method,args);try{return Read(rt);}finally{RenderTexture.ReleaseTemporary(rt);}}
        void Same(Color[] a,Color[] b,string message,float tolerance=.003f)
        {for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)Check(Math.Abs(a[i][c]-b[i][c])<tolerance,$"{message}: {i}/{c} {a[i][c]} vs {b[i][c]}");}
        try
        {
            Call(doc,"NormalizeModel");layer.TargetLayerId=source.Id;window.Show();
            var bindings=Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings"),true);
            Action<string,Action> apply=(label,change)=>{Undo.RegisterCompleteObjectUndo(doc,label);change();Call(bindings,"Refresh",true);};
            typeof(MakeSeamlessLayerEditorWindow).GetMethod("BuildFields",F).Invoke(null,new object[]{window.rootVisualElement,layer,doc,apply,bindings,new Action<VisualElement,TargetedLayerBehaviour>((r,l)=>{})});
            foreach(string field in new[]{"poissonEdges","mirrorPoissonEdges","offsetPoissonEdges","quiltingEdges","quiltingPoissonEdges"})
            {
                var member=typeof(MakeSeamlessLayerBehaviour).GetField(field);member.SetValue(layer,Edges.AllEdges);Call(bindings,"Refresh",true);
                void Click(string side){var b=window.rootVisualElement.Q<Button>(field+"-"+side);Check(b.enabledInHierarchy,"Every pair remains enabled");Call(b.clickable,"Invoke",new object[]{null});}
                void State(Edges expected)
                {
                    Check((Edges)member.GetValue(layer)==expected,"Pair toggle state "+field);
                    foreach(string side in new[]{"top","bottom","left","right"})
                    {
                        var b=window.rootVisualElement.Q<Button>(field+"-"+side);bool y=side=="top"||side=="bottom";
                        bool selected=expected==Edges.AllEdges||expected==(y?Edges.TopAndBottom:Edges.LeftAndRight);
                        Check(b.enabledSelf,"No disabled tint");Check(b.ClassListContains("whimtex-seamless-edge--selected")==selected,"Selected state");
                    }
                }
                Click("left");State(Edges.TopAndBottom);Click("bottom");State(Edges.None);
                Click("right");State(Edges.LeftAndRight);Click("top");State(Edges.AllEdges);
                member.SetValue(layer,Edges.None);Call(bindings,"Refresh",true);
            }
            // Full JSON includes inactive selectors; optimized clipboard intentionally omits them.
            // The transient GPU fixture is not an asset; exclude it only during this settings check.
            string json;
            source.sourceTexture=null;
            try { json=WhimTexDocumentJson.Write(doc,new WhimTexJsonWriteOptions {Mode=WhimTexJsonWriteMode.Full}).Json; }
            finally { source.sourceTexture=t; }
            using(var clip=WhimTexDocumentJson.Read(json))
            {
                var decoded=clip.Document;
                var saved=(MakeSeamlessLayerBehaviour)decoded.layers[0].Behaviour;
                Check(saved.poissonEdges==Edges.None&&saved.mirrorPoissonEdges==Edges.None&&saved.offsetPoissonEdges==Edges.None&&saved.quiltingEdges==Edges.None&&saved.quiltingPoissonEdges==Edges.None,"Full JSON retains all None selectors");
            }
            layer.horizontal=MakeSeamlessLayerBehaviour.HorizontalDirection.Off;layer.vertical=MakeSeamlessLayerBehaviour.VerticalDirection.Off;
            layer.leftEdge=layer.rightEdge=layer.topEdge=layer.bottomEdge=false;
            layer.mirrorSeamCorrection=layer.offsetSeamCorrection=layer.quiltingSeamCorrection=true;
            layer.enabled=false;var expected=Render("RenderAllLayers",32,24);layer.enabled=true;source.enabled=false;
            foreach(var mode in new[]{MakeSeamlessLayerBehaviour.SeamlessMode.Mirror,MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend,MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson,MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting})
            {
                layer.mode=mode;
                Same(Render("RenderAllLayers",32,24),expected,"Empty effect export bypass "+mode);
                Same(Render("RenderCachedPreview",32,cache,false,null),expected,"Empty effect cached bypass");
                Same(Render("RenderThumbnailLayer",layer.Owner,32,cache),p,"Empty effect thumbnail bypass");
            }
            foreach(string type in new[]{"ScreenedSeamless","PatchQuiltingSeamless"})
            {
                var core=assembly.GetType("DCFApixels.WhimTex."+type);var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
                var rt=(RenderTexture)core.GetMethod(type=="ScreenedSeamless"?"RenderConfigured":"Render",F).Invoke(null,type=="ScreenedSeamless"?
                    new object[]{t,32,24,Edges.None,.05f}:new object[]{t,32,24,Edges.None,.2f,50f,MakeSeamlessLayerBehaviour.QuiltingQuality.Normal,0,MakeSeamlessLayerBehaviour.QuiltingChannels.Linked,Vector4.one});
                try{Same(Read(rt),p,"Core bypass preserves hidden RGB and HDR",1e-6f);Check(RenderTexture.active==previous&&GL.sRGBWrite==srgb,"Caller state");}
                finally{RenderTexture.ReleaseTemporary(rt);}
            }
            return $"Empty paired edges: {checks} checks passed (five selectors, portable roundtrip, four methods, cache/thumbnail/export, core bypass and caller state).";
        }
        catch (TargetInvocationException e) { throw new Exception(e.GetBaseException().ToString()); }
        finally{window.Close();if(focus!=null)focus.Focus();cache.Dispose();Undo.RevertAllDownToGroup(undo);Object.DestroyImmediate(doc);Object.DestroyImmediate(t);}
    }
}
