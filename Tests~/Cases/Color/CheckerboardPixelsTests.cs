using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class CheckerboardPixelsTests
{
    public static string Start(string id) => AsyncD.Start(id, Run);
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);

    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static async Task Run(TestContext t, CancellationToken token)
    {
        var tileType = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexCheckerboard", true);
        foreach (string name in new[] { "Preview", "Thumbnail", "Canvas", "Mask" })
        {
            var prop = tileType.GetProperty(name, Static);
            var tile = (Texture2D)prop.GetValue(null);
            t.True(tile == (Texture2D)prop.GetValue(null) && tile.width == 2 && tile.height == 2 &&
                tile.mipmapCount == 1 && tile.wrapMode == TextureWrapMode.Repeat && tile.filterMode == FilterMode.Point, name);
        }
        var drawPreview = (Action<MeshGenerationContext>)tileType.GetMethod("DrawPreview",Static)
            .CreateDelegate(typeof(Action<MeshGenerationContext>));
        var drawStrip = typeof(WhimTexColorPicker).GetMethod("DrawColorStrip",Static);
        var curves = typeof(WhimTexWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexGradientCurvesWindow", true);
        var drawMarker = curves.GetMethod("DrawAlphaStripMarker",Static);
        var window = ScriptableObject.CreateInstance<EditorWindow>();
        try
        {
            window.ShowUtility();
            window.position = new Rect(80, 80, 300, 220);
            var root = window.rootVisualElement;
            var actual = Row(root);
            var reference = Row(root);
            int mode = 0;
            Func<float,Color> ramp = x => new Color(1-x,.25f,x,x);
            Color Gray(float v) => new Color(v,v,v,1);
            actual.generateVisualContent += ctx =>
            {
                if (mode == 0) drawPreview(ctx);
                if (mode == 1) drawStrip.Invoke(null,new object[] {ctx,actual.contentRect, 8,
                    new Vector2(4,3), .45f,.75f,ramp,default(Color)});
                if (mode == 2) drawMarker.Invoke(null,new object[] {ctx,20f,0f,1f});
            };
            reference.generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                if (mode == 2)
                {
                    // Interior of the two columns of the six-point alpha checker, below the tip.
                    Fill(p,14,31,6,5,Gray(.20f)); Fill(p,20,31,6,5,Gray(.42f));
                    Fill(p,14,36,6,5,Gray(.42f)); Fill(p,20,36,6,5,Gray(.20f));
                    return;
                }
                for (int y=0; y<48; y+=8)
                for (int x=0; x<64; x+=8)
                {
                    bool even = ((x/8 + y/8) & 1) == 0;
                    if (mode == 0) Fill(p,x,y,8,8,Gray(even ? .28f : .42f));
                    else
                    {
                        Color Blend(float u)
                        {
                            var c = ramp(u);
                            return Color.Lerp(Gray(even ? .45f : .75f),new Color(c.r,c.g,c.b),c.a);
                        }
                        // Original ramp interpolated quantized vertex tints across each cell.
                        var mesh = ctx.Allocate(4,6);
                        void Corner(int dx,int dy)
                        {
                            mesh.SetNextVertex(new Vertex { position = new Vector3(x+dx,y+dy,Vertex.nearZ),
                                tint = Blend((x+dx)/64f) });
                        }
                        Corner(0,0); Corner(8,0); Corner(8,8); Corner(0,8);
                        mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
                        mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);
                    }
                }
            };
            for (mode=0; mode<3; mode++)
            {
                actual.MarkDirtyRepaint(); reference.MarkDirtyRepaint();
                window.Repaint();
                await AsyncD.Tick(root,token); await AsyncD.Tick(root,token);
                var a = Capture(actual); var b = Capture(reference);
                t.True(a.width == b.width && a.height == b.height, "Equal sample grids");
                Color32 Pixel((Color32[] pixels,int width,int height) image, float x, float y)
                    => image.pixels[(image.height-1-(int)(y*image.height/48))*image.width+(int)(x*image.width/64)];
                void Compare(float x,float y,int tolerance)
                {
                    var c = Pixel(a,x,y); var expected = Pixel(b,x,y);
                    t.True(Math.Abs(c.r-expected.r)<=tolerance && Math.Abs(c.g-expected.g)<=tolerance &&
                        Math.Abs(c.b-expected.b)<=tolerance && Math.Abs(c.a-expected.a)<=tolerance,
                        $"mode={mode}, ({x},{y}) expected {expected}, got {c}");
                }
                if (mode == 2)
                {
                    foreach (int y in new[] {33,38}) foreach (int x in new[] {17,23}) Compare(x,y,2);
                    t.True(Pixel(a,17,33).r != Pixel(a,23,33).r, "Two marker shades remain visible");
                }
                else
                {
                    for (int y=4; y<48; y+=8) for (int x=4; x<64; x+=8) Compare(x,y,2);
                    t.True(Pixel(a,4,4).r != Pixel(a,4,12).r, "Alternating rows are visible");
                }
            }
        }
        finally { window.Close(); }
    }

    static VisualElement Row(VisualElement root)
    {
        var e = new VisualElement();
        e.style.width = 64; e.style.height = 48;
        root.Add(e);
        return e;
    }
    static void Fill(Painter2D p, float x, float y, float w, float h, Color col)
    {
        p.fillColor = col; p.BeginPath(); p.MoveTo(new Vector2(x,y)); p.LineTo(new Vector2(x+w,y));
        p.LineTo(new Vector2(x+w,y+h)); p.LineTo(new Vector2(x,y+h)); p.ClosePath(); p.Fill();
    }
    static (Color32[] pixels,int width,int height) Capture(VisualElement e)
    {
        // Public capture API in 6000.7. No desktop or other editor window is captured.
        var method = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.VisualElementCaptureExtensions",true)
            .GetMethod("CaptureToRenderTexture",BindingFlags.Public | BindingFlags.Static);
        var previous = RenderTexture.active;
        RenderTexture rt = null;
        Texture2D copy = null;
        try
        {
            rt = (RenderTexture)method.Invoke(null,new object[] { e });
            RenderTexture.active = rt;
            copy = new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false,true);
            copy.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);
            return (copy.GetPixels32(),rt.width,rt.height);
        }
        finally
        {
            RenderTexture.active = previous;
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        }
    }
}
