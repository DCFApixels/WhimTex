using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using DCFApixels.WhimTex;

// Opt-in eval after manual compilation, with an active graphics device. Temporary objects only.
var sessionType = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.LiveOutputSession", true);
var constructor = sessionType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Texture2D) }, null);
var publish = sessionType.GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic);
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
}
void Near(Color actual, Color expected, string label)
{
    Check(Mathf.Abs(actual.r - expected.r) < .015f && Mathf.Abs(actual.g - expected.g) < .015f &&
        Mathf.Abs(actual.b - expected.b) < .015f && Mathf.Abs(actual.a - expected.a) < .015f, label + ": " + actual);
}
void Fill(Texture2D texture, Color value)
{
    var colors = new Color[texture.width * texture.height];
    for (int i = 0; i < colors.Length; i++) colors[i] = value;
    texture.SetPixels(colors);
    texture.Apply(true, false);
}
Color ReadGpu(Texture source)
{
    var previous = RenderTexture.active;
    bool previousSrgb = GL.sRGBWrite;
    var rt = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
    var pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
    try
    {
        GL.sRGBWrite = false;
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;
        pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
        return pixel.GetPixel(0, 0);
    }
    finally
    {
        RenderTexture.active = previous;
        GL.sRGBWrite = previousSrgb;
        RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.DestroyImmediate(pixel);
    }
}

RenderTexture initialActive = RenderTexture.active;
bool initialSrgb = GL.sRGBWrite;
try
{
    foreach (TextureFormat format in new[] { TextureFormat.RGBAHalf, TextureFormat.RGBAFloat, TextureFormat.RGBA32 })
    foreach (bool mips in new[] { false, true })
    {
        Texture2D target = new Texture2D(8, 8, format, mips, format != TextureFormat.RGBA32);
        RenderTexture source = RenderTexture.GetTemporary(4, 4, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        IDisposable session = null;
        try
        {
            var saved = new Color(.1f, .2f, .3f, .6f);
            var edited = new Color(format == TextureFormat.RGBA32 ? .4f : 3f, .25f, .125f, .35f);
            Fill(target, saved);
            Color savedGpu = ReadGpu(target);
            Color savedCpu = target.GetPixel(0, 0);
            bool dirtyBefore = EditorUtility.IsDirty(target);
            var identity = target;
            session = (IDisposable)constructor.Invoke(new object[] { target });
            for (int iteration = 0; iteration < 3; iteration++)
            {
                if (iteration == 1)
                {
                    RenderTexture.active = initialActive;
                    RenderTexture.ReleaseTemporary(source);
                    source = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                }
                RenderTexture.active = source;
                GL.Clear(false, true, edited);
                GL.sRGBWrite = true;
                publish.Invoke(session, new object[] { source });
                Check(RenderTexture.active == source && GL.sRGBWrite, "Render state preserved");
                Check(ReferenceEquals(identity, target), "Texture identity preserved");
                Check(target.width == 8 && target.height == 8, "Saved dimensions preserved");
                Near(target.GetPixel(0, 0), savedCpu, "CPU pixels unchanged by GPU publishing");
                var expected = edited;
                if (target.isDataSRGB && !GraphicsFormatUtility.IsSRGBFormat(target.graphicsFormat))
                    expected = new Color(Mathf.LinearToGammaSpace(edited.r), Mathf.LinearToGammaSpace(edited.g), Mathf.LinearToGammaSpace(edited.b), edited.a);
                Near(ReadGpu(target), expected, "GPU shows edited HDR/color/alpha");
            }
            Check(EditorUtility.IsDirty(target) == dirtyBefore, "Publishing does not dirty the texture");
            session.Dispose();
            Near(ReadGpu(target), savedGpu, "Stop restores original GPU image");
            Near(target.GetPixel(0, 0), savedCpu, "Stop preserves original CPU pixels");
            Check(EditorUtility.IsDirty(target) == dirtyBefore, "Restoration does not dirty the texture");
            session.Dispose();
            session = null;

            // A later saved image becomes the restoration source, not a stale session snapshot.
            Fill(target, Color.green);
            session = (IDisposable)constructor.Invoke(new object[] { target });
            publish.Invoke(session, new object[] { source });
            session.Dispose();
            session = null;
            Near(ReadGpu(target), Color.green, "New session restores the latest saved image");
        }
        finally
        {
            session?.Dispose();
            RenderTexture.active = initialActive;
            GL.sRGBWrite = initialSrgb;
            RenderTexture.ReleaseTemporary(source);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
    return $"Live output: {checks} checks passed.";
}
finally
{
    RenderTexture.active = initialActive;
    GL.sRGBWrite = initialSrgb;
}
