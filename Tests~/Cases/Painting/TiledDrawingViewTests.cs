using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using WhimTex.Tests;
using Object=UnityEngine.Object;

public static class TiledDrawingViewTests
{
    public static string Run()=>TestContext.Run("Sparse view filters and outside frame",t=>
    {
        var frame=new TiledPaintFrame(256,256,512,512,ProjectiveMatrix.Scale(2,2));
        var shaders=new TiledPaintShaders(Shader.Find("Hidden/WhimTex/PaintBrush"),Shader.Find("Hidden/WhimTex/TiledPaintCommit"));
        using var surface=new TiledPaintSurface(frame,shaders);
        var seed=new Texture2D(256,256,TextureFormat.RGBAHalf,false,true);
        var view=new Material(Shader.Find("Hidden/WhimTex/TiledPaintView"));
        var referenceShader=ShaderUtil.CreateShaderAsset(ReferenceSource);
        var transform=new Material(referenceShader);
        var textures=new List<Texture2D>();var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        var full=RenderTexture.GetTemporary(1280,1280,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
        var actual=RenderTexture.GetTemporary(513,379,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var expected=RenderTexture.GetTemporary(513,379,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        try
        {
            var imports=new List<TiledPaintImportTile>();
            for(int ty=-1;ty<=1;ty++)for(int tx=-1;tx<=1;tx++)
            {
                if(tx==1&&ty==1)continue;
                var texture=new Texture2D(256,256,TextureFormat.RGBAHalf,false,true);textures.Add(texture);
                var values=new Color[256*256];
                for(int y=0;y<256;y++)for(int x=0;x<256;x++)
                {float a=.2f+(x%11)*.06f;values[y*256+x]=new Color((tx+.7f+x/256f)*a,(ty+2f-y/384f)*a,-.3f*a,a);}
                texture.SetPixels(values);texture.Apply(false,false);
                imports.Add(new TiledPaintImportTile(new Vector2Int(tx,ty),texture,new RectInt(0,0,256,256)));
            }
            using(surface.ImportTiles(imports)){}
            Verify("initial");
            var mask=new Texture2D(256,256,TextureFormat.RGBAHalf,false,true);textures.Add(mask);
            var maskPixels=new Color[256*256];var inkPixels=new Color[maskPixels.Length];
            for(int i=0;i<maskPixels.Length;i++)
            {float m=(i%256)/255f;maskPixels[i]=new Color(m,m,m,m);inkPixels[i]=new Color(.9f,1.8f,-.3f,.6f);}
            mask.SetPixels(maskPixels);mask.Apply(false,false);seed.SetPixels(inkPixels);seed.Apply(false,false);
            var batch=new TileInk(seed);
            var stroke=new TiledStrokeSettings(opacity:.65f,writeChannels:5,selection:mask,selectionWrap:true);
            surface.Begin(stroke);surface.Append(batch);Verify("pending mask/channels");
            surface.Cancel();Verify("cancel");
            surface.Begin(stroke);surface.Append(batch);
            using var delta=surface.Commit();Verify("commit");
            surface.Restore(delta.Before);Verify("undo");
            surface.Restore(delta.After);Verify("redo");
            foreach(var error in ShaderUtil.GetShaderMessages(view.shader))t.True(error.severity.ToString()!="Error",error.message);

            void Verify(string state)
            {
                surface.CopyRegion(new RectInt(-512,-512,1280,1280),full,true);
                foreach(bool clip in new[]{false,true})
                foreach(var filter in new[]{FilterMode.Point,FilterMode.Bilinear})
                foreach(var matrix in new[]{new ProjectiveMatrix {m00=.29,m01=.03,m02=.31,m10=-.04,m11=.32,m12=.34,m22=1},
                    new ProjectiveMatrix {m00=.27,m01=.02,m02=.35,m10=-.03,m11=.28,m12=.34,m20=.08,m21=-.04,m22=1},
                    new ProjectiveMatrix {m00=8,m11=8,m02=-7.5,m12=-.3,m22=1}})
                {
                    long memory=surface.AllocatedBytes;
                    var active=RenderTexture.active;bool write=GL.sRGBWrite;
                    surface.Render(actual,view,matrix,filter,clip,null,false);
                    t.Equal(memory,surface.AllocatedBytes,state+" render GPU allocations");
                    t.True(RenderTexture.active==active&&GL.sRGBWrite==write,state+" render state");
                    matrix.TryInverse(out var inverse);
                    transform.SetVector("_ViewInverse0",new Vector4((float)inverse.m00,(float)inverse.m01,(float)inverse.m02,0));
                    transform.SetVector("_ViewInverse1",new Vector4((float)inverse.m10,(float)inverse.m11,(float)inverse.m12,0));
                    transform.SetVector("_ViewInverse2",new Vector4((float)inverse.m20,(float)inverse.m21,(float)inverse.m22,0));
                    transform.SetFloat("_Point",filter==FilterMode.Point?1:0); transform.SetFloat("_Clip",clip?1:0); transform.SetTexture("_Full",full);
                    RenderTexture.active=expected; GL.sRGBWrite=false;GL.PushMatrix();
                    try {GL.LoadOrtho();transform.SetPass(0);Graphics.DrawProceduralNow(MeshTopology.Triangles,3);}
                    finally {GL.PopMatrix();}
                    Compare(t,expected,actual,state+" "+filter+" clip="+clip+" affine/projective/zoom");
                }
            }
        }
        finally
        {
            RenderTexture.active=previous;GL.sRGBWrite=srgb;Object.DestroyImmediate(seed);Object.DestroyImmediate(view);Object.DestroyImmediate(transform);Object.DestroyImmediate(referenceShader);
            foreach(var texture in textures)Object.DestroyImmediate(texture);
            RenderTexture.ReleaseTemporary(full);RenderTexture.ReleaseTemporary(actual);RenderTexture.ReleaseTemporary(expected);
        }
    });
    sealed class TileInk : ITiledPaintRasterBatch
    {
        readonly Texture texture;
        internal TileInk(Texture texture)=>this.texture=texture;
        public int Count=>1;
        public Rect Bounds(int stamp)=>new Rect(-1,-1,3,3);
        public void Record(CommandBuffer commands,IReadOnlyList<TiledPaintPage> pages,TiledPaintFrame frame)
        {
            foreach(var page in pages)foreach(var tile in page.Tiles)
                commands.CopyTexture(texture,0,0,0,0,256,256,page.Texture,0,0,tile.Origin.x,tile.Origin.y);
        }
    }
    // Dense oracle uses the same original coordinate frame. Expanding that frame's
    // matrix first would round nearest-pixel boundaries differently from the editor.
    const string ReferenceSource = @"Shader ""Hidden/WhimTex/OwnedDenseViewOracle"" { SubShader { Pass {
     ZTest Always ZWrite Off Cull Off Blend Off
     HLSLPROGRAM
     #pragma target 5.0
     #pragma vertex Vert
     #pragma fragment Frag
     #include ""UnityCG.cginc""
     Texture2D<float4> _Full; float3 _ViewInverse0,_ViewInverse1,_ViewInverse2; float _Point, _Clip;
     struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
     V Vert(uint id:SV_VertexID) { V o; o.uv=float2(id==2?2:0,id==1?2:0); o.pos=mul(UNITY_MATRIX_VP,float4(o.uv,0,1)); return o; }
     float4 Fetch(int2 at) { if(_Clip>.5)at=clamp(at,0,255);if(any(at < -512)||any(at >= 768))return 0;
      float4 c=_Full.Load(int3(at+512,0)); if(c.a>.00001)c.rgb=clamp(c.rgb/c.a,-65504,65504);else c.rgb=0;return c; }
     float4 Frag(V i):SV_Target { float3 p=float3(i.uv,1);float w=dot(_ViewInverse2,p);if(w<1e-8)return 0;
      float2 at=float2(dot(_ViewInverse0,p),dot(_ViewInverse1,p))/w*256;
      if(_Clip>.5&&(any(at<0)||any(at>256)))return 0;
      if(_Point>.5)return Fetch((int2)floor(at));at-=.5;int2 a=(int2)floor(at);float2 t=frac(at);
      return lerp(lerp(Fetch(a),Fetch(a+int2(1,0)),t.x),lerp(Fetch(a+int2(0,1)),Fetch(a+1),t.x),t.y); }
     ENDHLSL
    } } }";

    static void Compare(TestContext t,RenderTexture expected,RenderTexture actual,string label)
    {
        var a=Read(expected);var b=Read(actual);
        try
        {
            var left=a.GetRawTextureData<float>();var right=b.GetRawTextureData<float>();
            int errors=0;double maximum=0;int first=-1;
            for(int i=0;i<left.Length;i++)
            {double difference=Math.Abs((double)left[i]-right[i]);maximum=Math.Max(maximum,difference);
                if(difference>0.0001*Math.Max(1,Math.Abs(left[i]))) {errors++;if(first<0)first=i;}}
            t.True(errors==0,label+": differences="+errors+", max="+maximum+", first="+first+(first<0?"":", expected="+left[first]+", actual="+right[first]));
        }
        finally {Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
    }
    static Texture2D Read(RenderTexture source)
    {var before=RenderTexture.active;var texture=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
        try {RenderTexture.active=source;texture.ReadPixels(new Rect(0,0,source.width,source.height),0,0,false);return texture;}
        finally {RenderTexture.active=before;}}
}
