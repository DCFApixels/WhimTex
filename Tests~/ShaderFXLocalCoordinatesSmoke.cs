using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ShaderFXLocalCoordinatesSmoke
{
    public static string Main()
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        var doc=ScriptableObject.CreateInstance<TextureCompositor>();
        doc.width=doc.height=64;
        ShaderFX fx=null;
        int checks=0;
        try
        {
            string code="float4 ApplyFX(float2 uv, float4 color) { float2 p=LayerToLocal(uv)-0.5; color.a*=step(dot(p,p),0.04); return color; }";
            fx=(ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft",F).Invoke(null,new object[]{doc,code,new List<ShaderFXParameter>()});
            typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(fx,null);
            Layer group=new GroupLayerBehaviour(), layer=new ColorFillLayerBehaviour();
            doc.layers.Add(group); group.children.Add(layer); layer.modifiers.Add(fx);
            group.transform.position=new Double2(9,-4);
            group.transform.rotation=17;
            layer.transform.scale=new Double2(.65,.8);
            foreach(var tiling in new[]{TransformTilingMode.Clip,TransformTilingMode.Unbounded})
            foreach(bool projective in new[]{false,true})
            {
                layer.transform.tiling=tiling;
                if(projective)
                {
                    var m=group.transform.ToMatrix(64,64); m.m20=.2; m.m21=.1;
                    if(!group.transform.TrySetMatrix(m)) throw new Exception("Invalid test transform");
                }
                var world=(TextureTransform)typeof(TextureCompositor).GetMethod("GetCanvasTransform",F).Invoke(doc,new object[]{layer});
                world.ToMatrix(64,64).TryInverse(out var inverse);
                var texture=doc.Compose();
                try
                {
                    for(int y=0;y<64;y++) for(int x=0;x<64;x++)
                    {
                        var p=inverse.Point(new Double2((x+.5)/64,(y+.5)/64));
                        double d=(p.x-.5)*(p.x-.5)+(p.y-.5)*(p.y-.5);
                        if(Math.Abs(d-.04)<.001) continue;
                        float expected=d<.04?1:0;
                        if(Math.Abs(texture.GetPixel(x,y).a-expected)>.01) throw new Exception("FX shape detached from layer: "+tiling+" "+projective+" "+x+","+y);
                        checks++;
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
            }
            return "PASS: "+checks+" local FX pixels, nested transforms, perspective, Clip and Unbounded.";
        }
        finally
        {
            if(fx!=null) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
