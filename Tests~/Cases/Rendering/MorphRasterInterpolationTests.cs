using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

// Editor case with Tests~/Framework/TestApi.cs. All input and output pixels are linear.
public static class MorphRasterInterpolationTests
{
    private const int Width = 64, Height = 32;
    private static readonly Color Hidden = new Color(8, 64, -2, 0);
    private static readonly Color First = new Color(4, -2, .5f, .25f);
    private static readonly Color Second = new Color(-2, 1, 3, .75f);
    private static readonly Color Mixed = new Color(-.5f, .25f, 2.375f, .5f);

    public static string Run() => TestContext.Run(nameof(MorphRasterInterpolationTests), Check);

    private static MorphAnchor Pair(int ax, int ay, int bx, int by) => new MorphAnchor
    {
        from = new Vector2((ax + .5f) / Width, (ay + .5f) / Height),
        to = new Vector2((bx + .5f) / Width, (by + .5f) / Height)
    };

    private static Color Box(int x, int y, int left, int bottom, Color ink, int width = 8) =>
        x >= left && x < left + width && y >= bottom && y < bottom + 8 ? ink : Hidden;

    private static void Check(TestContext context)
    {
        var assembly = typeof(MorphAnchor).Assembly;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var render = assembly.GetType("DCFApixels.WhimTex.MorphRasterRenderer", true).GetMethod("Render", flags);
        var release = assembly.GetType("DCFApixels.WhimTex.ProfiledRenderTexture", true).GetMethod("ReleaseTemporary", flags);
        RenderTexture previous = RenderTexture.active, from = null, to = null;
        bool srgb = GL.sRGBWrite;
        Texture2D cpu = null;
        object flowCache = null;
        var cacheType = assembly.GetType("DCFApixels.WhimTex.MorphRasterRenderer+FlowCache", true);
        try
        {
            from = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            to = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            from.filterMode = FilterMode.Point;
            from.wrapMode = TextureWrapMode.Repeat;
            to.filterMode = FilterMode.Bilinear;
            cpu = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };

            void Write(RenderTexture target, Func<int, int, Color> color)
            {
                int w = target.width, h = target.height;
                var pixels = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) pixels[y * w + x] = color(x, y);
                cpu.SetPixels(pixels);
                cpu.Apply(false, false);
                GL.sRGBWrite = false;
                Graphics.Blit(cpu, target);
            }

            var method = MorphRasterMethod.LocalContrast;
            Color[] Frame(IReadOnlyList<MorphAnchor> pairs, float t, bool writeSrgb = false)
            {
                RenderTexture.active = from;
                GL.sRGBWrite = writeSrgb;
                var image = (RenderTexture)render.Invoke(null, new object[] { from, to, pairs, t, flowCache, null, method });
                try
                {
                    context.True(RenderTexture.active == from && GL.sRGBWrite == writeSrgb, "Restore caller render state");
                    context.True(image != from && image != to && image.width == from.width && image.height == from.height,
                        "Return an independently owned full-size image");
                    context.True(image.format == RenderTextureFormat.ARGBFloat && !image.sRGB, "Preserve float HDR input precision");
                    RenderTexture.active = image;
                    cpu.ReadPixels(new Rect(0, 0, from.width, from.height), 0, 0, false);
                    return cpu.GetPixels();
                }
                finally
                {
                    RenderTexture.active = from;
                    release.Invoke(null, new object[] { image });
                }
            }

            void Pixel(Color[] image, int x, int y, Color expected, string label)
            {
                Color actual = image[y * Width + x];
                for (int channel = 0; channel < 4; channel++)
                    context.Near(expected[channel], actual[channel], .0002, label + $" ({x},{y}) channel {channel}");
            }

            // Two independently moving textured bands on a fully opaque canvas. The
            // analytic halfway image is known and the mean image position is stationary.
            float TextureSignal(float x, float y) => .47f + .19f * Mathf.Sin(.43f*x + .22f*y)
                + .16f * Mathf.Cos(.12f*x - .39f*y) + .13f * Mathf.Sin(.27f*x - .18f*y);
            Color Band(int x, int y, float time)
            {
                float dx = y < Height/2 ? 8 : -8;
                float v = TextureSignal(x - dx*time, y);
                return new Color(v, v, v, 1);
            }
            Write(from, (x, y) => Band(x, y, 0));
            Write(to, (x, y) => Band(x, y, 1));
            foreach (float time in new[] { .25f, .5f, .75f })
            {
                var estimated = Frame(null, time);
                for (int k = 0; k < 2; k++)
                {
                    float flowError = 0, fadeError = 0;
                    for (int y = 4 + k*16; y < 12 + k*16; y++)
                        for (int x = 10; x < Width - 10; x++)
                        {
                            float truth = Band(x, y, time).r;
                            flowError += Mathf.Abs(estimated[y*Width+x].r - truth);
                            fadeError += Mathf.Abs(Mathf.Lerp(Band(x,y,0).r, Band(x,y,1).r, time) - truth);
                        }
                    context.True(flowError < fadeError * .6f, $"Opaque band {k} at {time}: flow error {flowError} vs fade {fadeError}");
                }
            }
            var automatic = Frame(null, .5f);
            var pin = Pair(30, 6, 30, 6);
            var adjusted = Frame(new[] { pin }, .5f);
            Pixel(adjusted, 30, 6, Color.Lerp(Band(30,6,0), Band(30,6,1), .5f), "User pin overrides local dense motion");
            float remoteChange = 0, movingContrast = 0;
            for (int x = 8; x < Width-8; x++)
            {
                int i = 26 * Width + x;
                remoteChange += Mathf.Abs(adjusted[i].r - automatic[i].r);
                movingContrast += Mathf.Abs(automatic[i].r - Color.Lerp(Band(x,26,0), Band(x,26,1), .5f).r);
            }
            context.True(remoteChange < movingContrast * .15f, $"Distant automatic motion survives pin: {remoteChange} vs {movingContrast}");

            Write(from, (x, y) => Box(x, y, 8, 4, First));
            Write(to, (x, y) => Box(x, y, 32, 16, Second));
            var manual = new[] { Pair(8, 4, 32, 16) };
            var start = Frame(manual, 0, true);
            Pixel(start, 8, 4, First, "First endpoint");
            Pixel(start, 32, 16, Hidden, "First endpoint hidden RGB");
            var end = Frame(null, 1);
            Pixel(end, 32, 16, Second, "Last endpoint");
            Pixel(end, 8, 4, Hidden, "Last endpoint hidden RGB");
            foreach (var pairs in new IReadOnlyList<MorphAnchor>[] { null, Array.Empty<MorphAnchor>(), manual })
            {
                var mid = Frame(pairs, .5f, true);
                Pixel(mid, 20, 10, Mixed, "Both translated leading edges coincide");
                Pixel(mid, 27, 17, Mixed, "Both translated trailing edges coincide");
                Pixel(mid, 19, 10, Color.clear, "Transparent background");
                Pixel(mid, 8, 4, Color.clear, "Vacated first silhouette");
                Pixel(mid, 32, 16, Color.clear, "Vacated last silhouette");
            }
            Pixel(Frame(null, .25f), 14, 7, new Color(1, -.5f, 1.75f, .375f), "Quarter-time warp and alpha weighting");

            Write(to, (x, y) => Box(x, y, 4, 8, Second));
            Pixel(Frame(null, .5f), 6, 6, Mixed, "Reused endpoint texture supplies its current silhouette");
            Write(to, (x, y) => Box(x, y, 32, 16, Second, 16));
            Pixel(Frame(null, .5f), 8, 4, new Color(4, -2, .5f, .125f), "Different occupied areas crossfade");
            Write(from, (x, y) => x == 9 && y == 5 ? First : Hidden);
            Write(to, (x, y) => x == 33 && y == 17 ? Second : Hidden);
            Pixel(Frame(null, .5f), 9, 5, new Color(4, -2, .5f, .125f), "Sparse subsampled masks crossfade");

            Write(from, (x, y) => x == Width - 1 && y == 6 ? First : Hidden);
            Write(to, (x, y) => Hidden);
            Pixel(Frame(null, .5f), Width - 1, 6, new Color(4, -2, .5f, .125f), "Empty endpoint crossfades");
            var fractional = Frame(new[] { Pair(12, 6, 13, 6) }, .5f);
            Pixel(fractional, Width - 1, 6, new Color(4, -2, .5f, .0625f), "Premultiply before bilinear filtering");
            Pixel(fractional, 0, 6, Color.clear, "Outside samples ignore repeat wrap");

            Write(from, (x, y) => new Color(2 * x, 0, 0, 1));
            Write(to, (x, y) => new Color(0, 2 * x, 0, 1));
            Pixel(Frame(null, .5f), 11, 3, new Color(11, 11, 0, 1), "Fully covered masks crossfade");
            var moving = Pair(8, 16, 24, 16);
            var pinned = Pair(32, 8, 32, 8);
            var anchored = Frame(new[] { moving, pinned }, .5f);
            Pixel(anchored, 16, 16, new Color(8, 24, 0, 1), "Paired interpolation point");
            Pixel(anchored, 32, 8, new Color(32, 32, 0, 1), "Stationary pin");
            var betweenPins = anchored[4 * Width + 20];
            context.True(betweenPins.r > 15.9f && betweenPins.r < 19.9f && betweenPins.g > 20.1f,
                "Between distinct guides both the manual correction and the automatic motion contribute");
            var crossing = Frame(new[] { moving, Pair(24, 16, 8, 16) }, .5f);
            Pixel(crossing, 16, 16, new Color(16, 16, 0, 1), "Coincident trajectories average their displacements");

            var many = new List<MorphAnchor>();
            for (int i = 0; i < 256; i++) many.Add(pinned);
            many.Add(moving);
            Pixel(Frame(many, .5f), 16, 16, new Color(8, 24, 0, 1), "Anchor beyond uniform-array limits");
            moving.to = Pair(8, 16, 32, 16).to;
            Pixel(Frame(many, .5f), 20, 16, new Color(8, 32, 0, 1), "In-place edits update the GPU anchor table");

            // A flat opaque pair supplies no automatic evidence. A guide should
            // deform the dense field itself and be respected after in-place edits.
            method = MorphRasterMethod.Liao;
            Color Translated(int x, int y, float time)
            {
                float signal = TextureSignal(x - 5*time, y - 2*time);
                return new Color(signal,signal,signal,1);
            }
            Write(from, (x, y) => Translated(x, y, 0));
            Write(to, (x, y) => Translated(x, y, 1));
            var quarter = Frame(null, .25f);
            (from, to) = (to, from);
            var backward = Frame(null, .75f);
            (from, to) = (to, from);
            float alignedError = 0, fadeErrorLiao = 0, reversalError = 0;
            for (int y = 5; y < Height - 5; y++)
                for (int x = 8; x < Width - 8; x++)
                {
                    int i = y * Width + x;
                    float target = Translated(x, y, .25f).r;
                    alignedError += Mathf.Abs(quarter[i].r - target);
                    reversalError += Mathf.Abs(quarter[i].r - backward[i].r);
                    fadeErrorLiao += Mathf.Abs(Mathf.Lerp(Translated(x,y,0).r, Translated(x,y,1).r, .25f) - target);
                }
            context.True(alignedError < .5f * fadeErrorLiao, $"Liao automatic translation: {alignedError} vs fade {fadeErrorLiao}");
            context.True(reversalError / ((Width - 16) * (Height - 10)) < .008f, "Liao reversing endpoints and time preserves the frame");
            var solidA = new Color(-2, 3, 1, 1);
            var solidB = new Color(4, 1, -1, 1);
            Write(from, (x, y) => solidA);
            Write(to, (x, y) => solidB);
            flowCache = Activator.CreateInstance(cacheType, true);
            var fieldInfo = cacheType.GetField("field", BindingFlags.NonPublic | BindingFlags.Instance);
            var clearCache = cacheType.GetMethod("Clear", BindingFlags.NonPublic | BindingFlags.Instance);
            Color[] Field()
            {
                RenderTexture.active = (RenderTexture)fieldInfo.GetValue(flowCache);
                cpu.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                return cpu.GetPixels();
            }
            Pixel(Frame(null, 0), 17, 9, solidA, "Liao first HDR endpoint");
            Pixel(Frame(null, 1), 17, 9, solidB, "Liao last HDR endpoint");
            Pixel(Frame(null, .5f), 17, 9, Color.Lerp(solidA, solidB, .5f), "Liao opaque midpoint");
            var freeField = Field();
            var guide = Pair(20, 11, 28, 15);
            var pairList = new[] { guide };
            Frame(pairList, .25f);
            var pinnedField = Field();
            var motion = pinnedField[13 * Width + 24];
            context.Near(8, motion.r * Width, .5, "Liao guide reaches X");
            context.Near(4, motion.g * Height, .5, "Liao guide reaches Y");
            context.True(pinnedField[13 * Width + 26].r > freeField[13 * Width + 26].r + .02f,
                "Guide moves an adjacent unconstrained texel");
            var retainedLiao = (RenderTexture)fieldInfo.GetValue(flowCache);
            Frame(pairList, .75f);
            context.True(retainedLiao == (RenderTexture)fieldInfo.GetValue(flowCache), "Liao scrubbing reuses field");
            guide.from = Pair(28, 15, 20, 11).from;
            guide.to = Pair(28, 15, 20, 11).to;
            Frame(pairList, .5f);
            var reversed = Field();
            var reverseMotion = reversed[13 * Width + 24];
            context.Near(-8, reverseMotion.r * Width, .5, "Edited guide rebuilds Liao X");
            context.Near(-4, reverseMotion.g * Height, .5, "Edited guide rebuilds Liao Y");
            for (int y = 0; y < Height - 1; y++)
                for (int x = 0; x < Width - 1; x++)
                {
                    int i = y * Width + x;
                    var dx = reversed[i + 1] - reversed[i];
                    var dy = reversed[i + Width] - reversed[i];
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        float k = sign * .5f;
                        float area = (1 + k * dx.r * Width) * (1 + k * dy.g * Height)
                            - k * k * dx.g * Height * dy.r * Width;
                        if (!(area > 0)) throw new Exception($"Folded Liao map {sign} at {x},{y}: {area}");
                    }
                }
            method = MorphRasterMethod.LocalContrast;
            Frame(null, .5f);
            context.Near(0, Field()[13 * Width + 24].r, .001, "Switch method with same content stamp");
            clearCache.Invoke(flowCache, null);
            flowCache = null;

            // Exercise the full-resolution refinement above the pyramid's working size.
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(from);
            RenderTexture.ReleaseTemporary(to);
            UnityEngine.Object.DestroyImmediate(cpu);
            const int largeW = 384, largeH = 192;
            cpu = new Texture2D(largeW, largeH, TextureFormat.RGBAFloat, false, true);
            from = RenderTexture.GetTemporary(largeW, largeH, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            to = RenderTexture.GetTemporary(largeW, largeH, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Color Detail(int x, int y, float time)
            {
                float dx = y < largeH/2 ? 18 : -18;
                float sx = (x - dx*time)/3f;
                float v = TextureSignal(sx, y/3f) + .08f * Mathf.Cos(1.1f*sx - .45f*y);
                return new Color(v,v,v,1);
            }
            Write(from,(x,y) => Detail(x,y,0));
            Write(to,(x,y) => Detail(x,y,1));
            flowCache = Activator.CreateInstance(cacheType, true);
            var detailed = Frame(null,.5f);
            float autoError = 0, crossfadeError = 0;
            for (int y = 0; y < largeH; y++)
                for (int x = 0; x < largeW; x++)
                {
                    Color sample = detailed[y*largeW+x];
                    if (sample.a < .999f) throw new Exception($"Opaque frame lost coverage at {x},{y}: {sample.a}");
                    if (x < 20 || x >= largeW-20 || y%96 < 10 || y%96 > 85) continue;
                    float truth = Detail(x,y,.5f).r;
                    autoError += Mathf.Abs(sample.r - truth);
                    crossfadeError += Mathf.Abs(.5f*(Detail(x,y,0).r+Detail(x,y,1).r) - truth);
                }
            var retained = (RenderTexture)cacheType.GetField("field", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(flowCache);
            RenderTexture.active = retained;
            cpu.ReadPixels(new Rect(0,0,retained.width,retained.height),0,0,false);
            var d1 = cpu.GetPixel(retained.width/2, retained.height/4);
            var d2 = cpu.GetPixel(retained.width/2, retained.height*3/4);
            context.True(autoError < .6f*crossfadeError,
                $"Full-resolution graphic: flow {autoError} vs fade {crossfadeError}, center motions {d1.r*largeW},{d1.g*largeH} and {d2.r*largeW},{d2.g*largeH}");
            Frame(null,.75f);
            context.True(retained == (RenderTexture)cacheType.GetField("field", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(flowCache),
                "Scrubbing a static pair reuses the estimated field");

        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = srgb;
            if (flowCache != null) cacheType.GetMethod("Clear", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(flowCache,null);
            if (cpu != null) UnityEngine.Object.DestroyImmediate(cpu);
            if (from != null) RenderTexture.ReleaseTemporary(from);
            if (to != null) RenderTexture.ReleaseTemporary(to);
        }
    }
}
