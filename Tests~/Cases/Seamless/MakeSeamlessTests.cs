using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

// Independent port: complete original body, assertion inputs and finally cleanup retained.
public static class MakeSeamlessTests
{
    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("MakeSeamlessTests", Body);
    static void Body()
    {
        // Opt-in only AFTER the user manually compiles. Transient objects; no imports or asset writes.
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(DCFApixels.WhimTex.TextureCompositor);
        var document = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>());
        document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        document.width = 33; document.height = 25;
        var texture = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new UnityEngine.Texture2D(33,25,UnityEngine.TextureFormat.RGBAFloat,false,true));
        texture.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        var source = new DCFApixels.WhimTex.FileLayerBehaviour {
            sourceTexture = texture, colorRange = DCFApixels.WhimTex.LayerColorRange.HDR };
        var effect = new DCFApixels.WhimTex.MakeSeamlessLayerBehaviour {
            colorRange = DCFApixels.WhimTex.LayerColorRange.HDR };
        document.layers.Add(effect); document.layers.Add(source);
        type.GetMethod("NormalizeModel",flags).Invoke(document,null);
        int checks = 0;
        void Same(UnityEngine.Color a, UnityEngine.Color b, string message)
        {
            for(int c=0;c<4;c++)
            {
                WhimTex.Tests.UnityC.FixtureContext.Context.True(!(float.IsNaN(a[c]) || System.Math.Abs(a[c]-b[c])>.003f), message);
                checks++;
            }
        }
        UnityEngine.Color[] Render()
        {
            var rt = (UnityEngine.RenderTexture)type.GetMethod("RenderLayerPreview",flags).Invoke(document,new object[]{effect.Owner,33});
            var previous = UnityEngine.RenderTexture.active;
            var read = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new UnityEngine.Texture2D(33,25,UnityEngine.TextureFormat.RGBAFloat,false,true));
            try
            {
                UnityEngine.RenderTexture.active = rt;
                read.ReadPixels(new UnityEngine.Rect(0,0,33,25),0,0,false);
                return read.GetPixels();
            }
            finally { UnityEngine.RenderTexture.active = previous; WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(read); }
        }
        try
        {
            var input = new UnityEngine.Color[33*25];
            for(int y=0;y<25;y++) for(int x=0;x<33;x++)
                input[y*33+x] = new UnityEngine.Color(x/8f,y/12f,.3f,.2f + ((x+y)%5)*.2f);
            texture.SetPixels(input); texture.Apply(false,false);
            foreach(var h in new[]{DCFApixels.WhimTex.MakeSeamlessLayerBehaviour.HorizontalDirection.Off,
                DCFApixels.WhimTex.MakeSeamlessLayerBehaviour.HorizontalDirection.LeftToRight,
                DCFApixels.WhimTex.MakeSeamlessLayerBehaviour.HorizontalDirection.RightToLeft})
            foreach(var v in new[]{DCFApixels.WhimTex.MakeSeamlessLayerBehaviour.VerticalDirection.Off,
                DCFApixels.WhimTex.MakeSeamlessLayerBehaviour.VerticalDirection.BottomToTop,
                DCFApixels.WhimTex.MakeSeamlessLayerBehaviour.VerticalDirection.TopToBottom})
            foreach(float width in new[]{.001f,.2f,.5f}) foreach(float falloff in new[]{.25f,1f,4f})
            {
                effect.horizontal=h; effect.vertical=v; effect.blendWidth=width; effect.falloff=falloff;
                var pixels=Render();
                if((int)h!=0) for(int y=0;y<25;y++) Same(pixels[y*33],pixels[y*33+32],"Horizontal seam");
                if((int)v!=0) for(int x=0;x<33;x++) Same(pixels[x],pixels[24*33+x],"Vertical seam");
                if((int)h==0 && (int)v==0) for(int i=0;i<input.Length;i++) Same(input[i],pixels[i],"Bypass");
            }
            var before=Render(); source.enabled=false; var hidden=Render();
            for(int i=0;i<before.Length;i++) Same(before[i],hidden[i],"Hidden input");
            var json=UnityEngine.JsonUtility.ToJson(document);
            var copy=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>());
            try
            {
                UnityEngine.JsonUtility.FromJsonOverwrite(json,copy);
                var roundtrip=copy.layers[0]?.Behaviour as DCFApixels.WhimTex.MakeSeamlessLayerBehaviour;
                WhimTex.Tests.UnityC.FixtureContext.Context.True(!(roundtrip==null || roundtrip.horizontal!=effect.horizontal || roundtrip.falloff!=effect.falloff), "Seamless settings serialization");
            }
            finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(copy); }
            return;
        }
        finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(document); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(texture); }
        
    }
}
