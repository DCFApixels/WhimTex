// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
// Unity Pipeline run_script, entry TilingTests.Main. Transient documents only.
using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class TilingTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("TilingTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteMain();
    });

    static int checks;
    static void Check(bool ok, string message) { context.True(ok, message); }
    static Color[] Render(TextureCompositor doc)
    {
        var previous = RenderTexture.active;
        RenderTexture rt = null; Texture2D pixels = null;
        try
        {
            rt = (RenderTexture)typeof(TextureCompositor).GetMethod("RenderCanvas", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(doc, new object[] { 64 });
            Check(RenderTexture.active == previous, "Leaked active render target");
            RenderTexture.active = rt;
            pixels = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
            return pixels.GetPixels();
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
    private static void ExecuteMain()
    {
        Check((int)TransformTilingMode.Source == 3 && (int)TransformTilingMode.Clip == 0 &&
            (int)TransformTilingMode.Repeat == 1 && (int)TransformTilingMode.Mirror == 2, "Serialized IDs changed");
        foreach (LayerBehaviour behaviour in new LayerBehaviour[] { new NoiseLayerBehaviour(), new GradientLayerBehaviour(),
            new ShapeLayerBehaviour(), new ColorFillLayerBehaviour(), new DrawingLayerBehaviour() })
        {
            var doc = ScriptableObject.CreateInstance<TextureCompositor>();
            try
            {
                doc.width = doc.height = 64;
                Layer layer = behaviour; doc.layers.Add(layer);
                var t = TextureTransform.Default; t.scale = new Vector2(.4f, .4f);
                t.tiling = TransformTilingMode.Clamp; layer.transform = t;
                var clamp = Render(doc);
                t.tiling = TransformTilingMode.Unbounded; layer.transform = t;
                var free = Render(doc);
                if (behaviour is NoiseLayerBehaviour)
                {
                    Check(Mathf.Abs(clamp[65].r - clamp[70].r) < .005f, "Clamp corner did not extend edge");
                    Check(Mathf.Abs(free[65].r - free[70].r) > .001f, "Unbounded noise did not continue");
                    Check(free[65].a > .99f, "Unbounded noise was clipped");
                }
                if (behaviour is ColorFillLayerBehaviour) Check(free[65].a > .99f, "Unbounded fill was clipped");
                if (behaviour is DrawingLayerBehaviour) Check(free[65].a < .01f, "Raster fallback did not clip");
                t.tiling = TransformTilingMode.Clip; layer.transform = t;
                var clip = Render(doc);
                Check(clip[65].a < .01f, "Clip failed after Unbounded: " + behaviour.GetType().Name);
            }
            finally { UnityEngine.Object.DestroyImmediate(doc); }
        }
        return;
    }
}

