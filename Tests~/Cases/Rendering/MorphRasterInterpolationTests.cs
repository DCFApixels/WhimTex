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

    // Two plausible translations share a local patch. Only the three witnesses
    // on its left favour the larger displacement. Insert an edge in either
    // endpoint and the local patch should keep its own better match.
    private static void CheckContourVotes(TestContext context)
    {
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        Texture2D scores = null, neighbours = null, features = null, readback = null;
        RenderTexture target = null;
        Material material = null;
        try
        {
            GL.sRGBWrite = false;
            material = new Material(Shader.Find("Hidden/WhimTex/MorphRaster"));
            int pass = material.FindPass("RotationConsensus");
            context.True(pass >= 0, "Contour consensus pass exists");
            scores = new Texture2D(3 * 64, 3 * 9, TextureFormat.RGBAFloat, false, true);
            neighbours = new Texture2D(3, 3, TextureFormat.RGBAFloat, false, true);
            features = new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true);
            target = RenderTexture.GetTemporary(3, 3, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var table = new Color[scores.width * scores.height];
            Array.Fill(table, new Color(-2, 0, 0, 0));
            scores.SetPixels(table);
            scores.SetPixel(64 + 63, 9, new Color(.2f, 0, 0, 0));
            scores.SetPixel(64 + 31, 9 + 4, new Color(.91f, 0, 0, 0));
            scores.SetPixel(64 + 31, 9 + 7, new Color(.78f, 0, 0, 0));
            scores.Apply();
            var witnesses = new Color[9];
            for (int y = 0; y < 3; y++) witnesses[3*y] = new Color(0, .225f, 0, 1);
            neighbours.SetPixels(witnesses);
            neighbours.Apply();
            material.SetTexture("_RotationScores", scores);
            material.SetTexture("_RotationFeatures", features);
            material.SetVector("_RotationGrid", new Vector4(3, 3, 1f/3, 1f/3));
            material.SetVector("_RasterSize", new Vector4(512, 512, 0, 0));
            var pixels = new Color[32 * 32];
            for (int edge = 0; edge < 3; edge++)
            {
                for (int i = 0; i < pixels.Length; i++)
                {
                    float a = edge == 1 && i % 32 < 10 ? 1 : 0;
                    float b = edge == 2 && i % 32 < 10 ? 1 : 0;
                    pixels[i] = new Color(a, a, b, b);
                }
                features.SetPixels(pixels);
                features.Apply();
                Graphics.Blit(neighbours, target, material, pass);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(1, 1, 1, 1), 0, 0, false);
                var match = readback.GetPixel(0, 0);
                context.True(match.a > .1f, $"Usable consensus match, edge {edge}");
                context.Near(edge == 0 ? .225 : 0, match.g, .001, $"Displacement across contour, edge {edge}");
            }
        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = srgb;
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            if (target != null) RenderTexture.ReleaseTemporary(target);
            if (scores != null) UnityEngine.Object.DestroyImmediate(scores);
            if (neighbours != null) UnityEngine.Object.DestroyImmediate(neighbours);
            if (features != null) UnityEngine.Object.DestroyImmediate(features);
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
        }
    }

    // Feed the visibility decision a known occluded edge and then reverse the
    // endpoints. A change of exposure on an aligned edge should keep a linear fade.
    private static void CheckVisibility(TestContext context)
    {
        const int size = 16;
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        Material material = null;
        Texture2D evidence = null, readback = null;
        RenderTexture target = null;
        try
        {
            GL.sRGBWrite = false;
            material = new Material(Shader.Find("Hidden/WhimTex/MorphRaster"));
            int pass = material.FindPass("VisibilityMask");
            context.True(pass >= 0, "Liao visibility pass exists");
            evidence = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            material.SetVector("_Level", new Vector4(size, size, 1f / size, 1f / size));
            var samples = new Color[size * size];
            float Probe(Func<int, int, Color> evidenceAt)
            {
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = evidenceAt(i % size, i / size);
                evidence.SetPixels(samples);
                evidence.Apply();
                Graphics.Blit(evidence, target, material, pass);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(size / 2, size / 2, 1, 1), 0, 0, false);
                return readback.GetPixel(0, 0).r;
            }
            float Mask(bool reversed, bool aligned, float area) => Probe((x, y) =>
            {
                float a = x < size / 2 ? .6f : .15f;
                float b = aligned ? .1f + .5f * a : 0;
                return reversed ? new Color(b, a, -area, 0) : new Color(a, b, area, 0);
            });
            float forward = Mask(false, false, -1);
            context.True(forward < -.5f, "Visible source wins at an unmatched edge");
            context.Near(-forward, Mask(true, false, -1), .001, "Reversing visibility swaps endpoint weights");
            context.Near(0, Mask(false, true, -1), .001, "Aligned exposed edge keeps linear color weights");
            context.Near(0, Mask(false, false, 0), .001, "Equal endpoint support avoids an arbitrary bias");
            context.Near(0, Probe((x, y) => new Color(.7f, .1f, -1, 0)), .001,
                "Different uniform endpoint values keep a linear fade");

            // The probe is the first column on the right. Weak local stripes
            // supply a mismatch even when the two sides have the same mean.
            float NeighbourVote(int contour, int vote) => Probe((x, y) =>
            {
                bool left = x < size / 2;
                float a = (y & 1) == 0 ? .04f : -.04f;
                if (left && contour == 1) a += .5f;
                float b = left && contour == 2 ? .5f : 0;
                return new Color(a, b, left ? vote : -.25f, left ? vote : 0);
            });
            context.True(NeighbourVote(0, 1) > .1f && NeighbourVote(0, -1) < -.1f,
                "An adjacent vote can change the winner across a continuous surface");
            for (int contour = 1; contour <= 2; contour++)
            {
                float ownSide = NeighbourVote(contour, -1);
                context.True(ownSide < -.15f, $"The probe retains its own vote at contour {contour}");
                context.Near(ownSide, NeighbourVote(contour, 1), .0001,
                    $"Flipping votes beyond endpoint contour {contour} cannot change the mask");
            }
        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = srgb;
            if (target != null) RenderTexture.ReleaseTemporary(target);
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (evidence != null) UnityEngine.Object.DestroyImmediate(evidence);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static void CheckIndependentRigidParts(TestContext context)
    {
        const int w = 64, h = 32;
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        Material material = null;
        Texture2D pins = null, cpu = null;
        RenderTexture models = null;
        try
        {
            GL.sRGBWrite = false;
            material = new Material(Shader.Find("Hidden/WhimTex/MorphRaster"));
            int pass = material.FindPass("RigidGuideModels");
            context.True(pass >= 0, "Local rigid guide pass exists");
            var entries = new List<Color>();
            foreach (int x in new[] {16, 48})
            {
                float slope = x == 16 ? .8f : -.6f;
                foreach (var offset in new[] { new Vector2(-2,-1), new Vector2(2,-1), new Vector2(0,2) })
                {
                    var mid = new Vector2(x + .5f, 16.5f) + offset;
                    var halfMotion = .5f * (new Vector2(1, -2) + slope * new Vector2(-offset.y, offset.x));
                    var a = mid - halfMotion;
                    var b = mid + halfMotion;
                    entries.Add(new Color(a.x/w, a.y/h, b.x/w, b.y/h));
                }
            }
            pins = new Texture2D(entries.Count, 1, TextureFormat.RGBAFloat, false, true);
            pins.SetPixels(entries.ToArray());
            pins.Apply();
            models = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            cpu = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
            material.SetTexture("_AnchorTable", pins);
            material.SetInteger("_AnchorCount", entries.Count);
            material.SetInteger("_AnchorWidth", entries.Count);
            material.SetVector("_Level", new Vector4(w,h,1f/w,1f/h));
            material.SetVector("_RasterSize", new Vector4(w,h,0,0));
            Graphics.Blit(null, models, material, pass);
            RenderTexture.active = models;
            cpu.ReadPixels(new Rect(0,0,w,h),0,0,false);
            foreach (int x in new[] {16,48})
            {
                Color model = cpu.GetPixel(x,16);
                context.True(model.a > .8f, "Three consistent markers support a local part");
                context.Near(x == 16 ? .8 : -.6, model.b, .001, "Independent local rotation on a rectangular canvas");
                context.Near(1, model.r * h, .001, "Local translation X in pixels");
                context.Near(-2, model.g * h, .001, "Local translation Y in pixels");
            }
            context.Near(0, cpu.GetPixel(32,16).a, .0001, "Distinct parts do not impose motion in the unsupported gap");
        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = srgb;
            if (models != null) RenderTexture.ReleaseTemporary(models);
            if (cpu != null) UnityEngine.Object.DestroyImmediate(cpu);
            if (pins != null) UnityEngine.Object.DestroyImmediate(pins);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static void Check(TestContext context)
    {
        CheckContourVotes(context);
        CheckIndependentRigidParts(context);
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
            bool classicLiao = false;
            System.Collections.Generic.List<MorphSegment> segments = null;
            bool preserveGeometry = false, holdStill = false;
            var interpolation = MorphInterpolation.Linear;
            Color[] Frame(IReadOnlyList<MorphAnchor> pairs, float t, bool writeSrgb = false)
            {
                RenderTexture.active = from;
                GL.sRGBWrite = writeSrgb;
                var image = (RenderTexture)render.Invoke(null, new object[] { from, to, pairs, t, flowCache, null, method, interpolation, classicLiao, preserveGeometry, segments, holdStill });
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
            // A shared halfway texture with fractional translation and opposite X/Y stretches.
            Color Stretched(int x, int y, float time)
            {
                float t = time - .5f;
                float u = (x - 31.5f - t * 4.5f) / (1 + t * .24f) + 31.5f;
                float v = (y - 15.5f + t * 2.75f) / (1 - t * .22f) + 15.5f;
                float value = TextureSignal(u, v);
                return new Color(value, value, value, 1);
            }
            Write(from, (x, y) => Stretched(x, y, 0));
            Write(to, (x, y) => Stretched(x, y, 1));
            var midpoint = Frame(null, .5f);
            float stretchError = 0, stretchFade = 0;
            for (int y = 4; y < Height - 4; y++)
                for (int x = 6; x < Width - 6; x++)
                {
                    float truth = Stretched(x, y, .5f).r;
                    stretchError += Mathf.Abs(midpoint[y * Width + x].r - truth);
                    stretchFade += Mathf.Abs(.5f * (Stretched(x, y, 0).r + Stretched(x, y, 1).r) - truth);
                }
            context.True(stretchError < .4f * stretchFade, $"Liao fractional translation and axial scale: {stretchError} vs fade {stretchFade}");
            // Four guides describe a rotating, translating patch. Check the unpinned
            // interior at two non-midpoint times as well as at the halfway frame.
            MorphAnchor TurnGuide(float x, float y)
            {
                var middle = new Vector2((x + .5f) / Width, (y + .5f) / Height);
                var half = .5f * new Vector2((10 + 1.1f * (y - 15)) / Width, (-2 - 1.1f * (x - 30)) / Height);
                return new MorphAnchor { from = middle - half, to = middle + half };
            }
            Color Turning(int x, int y, float time)
            {
                float t = time - .5f, k = 1.1f * t;
                float u = x - 30 - 10 * t, v = y - 15 + 2 * t;
                float value = TextureSignal(30 + (u - k * v) / (1 + k*k), 15 + (k*u + v) / (1 + k*k));
                return new Color(value, value, value, 1);
            }
            var turnGuides = new[] { TurnGuide(22, 9), TurnGuide(38, 9), TurnGuide(22, 21), TurnGuide(38, 21) };
            Write(from, (x, y) => Turning(x, y, 0));
            Write(to, (x, y) => Turning(x, y, 1));
            foreach (float time in new[] { .25f, .5f, .75f })
            {
                var aligned = Frame(turnGuides, time);
                float error = 0, fade = 0;
                for (int y = 11; y <= 18; y++)
                    for (int x = 23; x <= 37; x++)
                    {
                        float truth = Turning(x, y, time).r;
                        error += Mathf.Abs(aligned[y * Width + x].r - truth);
                        fade += Mathf.Abs(Mathf.Lerp(Turning(x, y, 0).r, Turning(x, y, 1).r, time) - truth);
                    }
                context.True(error < .20f * fade, $"Liao guided rotation at {time}: {error} vs fade {fade}");
            }
            var solidA = new Color(-2, 3, 1, 1);
            // Both endpoints are rigid rotations of one texture. Its true midpoint
            // retains the original radius; a straight chord shortens it by cos(angle).
            const float angle = .55f;
            Vector2 Rotate(Vector2 point, float radians) => new Vector2(
                Mathf.Cos(radians)*point.x - Mathf.Sin(radians)*point.y,
                Mathf.Sin(radians)*point.x + Mathf.Cos(radians)*point.y);
            Vector2 pivot = new Vector2((Width - 1)*.5f, (Height - 1)*.5f);
            Vector2 UV(Vector2 p) => new Vector2((p.x+.5f)/Width, (p.y+.5f)/Height);
            Color Rigid(int x, int y, float radians)
            {
                var p = pivot + Rotate(new Vector2(x,y) - pivot, -radians);
                float value = TextureSignal(p.x,p.y);
                return new Color(value,value,value,1);
            }
            var rotationPins = new List<MorphAnchor>();
            foreach (int x in new[] {-15, 0, 15})
                foreach (int y in new[] {-7, 0, 7})
                {
                    var p = new Vector2(x,y);
                    rotationPins.Add(new MorphAnchor { from=UV(pivot+Rotate(p,-angle)), to=UV(pivot+Rotate(p,angle)) });
                }
            Write(from,(x,y)=>Rigid(x,y,-angle));
            Write(to,(x,y)=>Rigid(x,y,angle));
            var chord = Frame(rotationPins,.5f);
            interpolation = MorphInterpolation.AutomaticCurve;
            var arc = Frame(rotationPins,.5f);
            var unguidedArc = Frame(null, .5f);
            classicLiao = true;
            var classicArc = Frame(null, .5f);
            context.True(!classicArc.AsSpan().SequenceEqual(unguidedArc), "Classic and current Liao differ on textured rotation");
            flowCache = Activator.CreateInstance(cacheType, true);
            Frame(null, .5f);
            classicLiao = false;
            context.True(Frame(null, .5f).AsSpan().SequenceEqual(unguidedArc), "Same pair switches to current flow and arc");
            classicLiao = true;
            context.True(Frame(null, .5f).AsSpan().SequenceEqual(classicArc), "Same pair switches back to classic flow and arc");
            foreach (bool classic in new[] {false,true})
            {
                classicLiao = classic;
                var free = classic ? classicArc : unguidedArc;
                preserveGeometry = true;
                var constrained = Frame(null, .5f);
                context.True(!constrained.AsSpan().SequenceEqual(free), "Unguided geometry option rebuilds the cached rotation field");
                context.True(Frame(null, .5f).AsSpan().SequenceEqual(constrained), "Geometry cache reproduces the same frame");
                preserveGeometry = false;
                context.True(Frame(null, .5f).AsSpan().SequenceEqual(free), "Disabling geometry restores the original motion");
            }
            cacheType.GetMethod("Clear", flags | BindingFlags.Instance).Invoke(flowCache, null);
            flowCache = null;
            classicLiao = false;
            double chordError = 0, arcError = 0, unguidedError = 0, rigidFadeError = 0;
            for (int y = 9; y < Height-9; y++)
                for (int x = 18; x < Width-18; x++)
                {
                    int index = y * Width + x;
                    float expected = Rigid(x,y,0).r;
                    chordError += Mathf.Abs(chord[index].r - expected);
                    arcError += Mathf.Abs(arc[index].r - expected);
                    unguidedError += Mathf.Abs(unguidedArc[index].r - expected);
                    float fade = .5f * (Rigid(x, y, -angle).r + Rigid(x, y, angle).r);
                    rigidFadeError += Mathf.Abs(fade - expected);
                }
            context.True(arcError < .9 * chordError, $"Rigid rotation midpoint error: arc {arcError} vs chord {chordError}");
            context.True(unguidedError < .8 * rigidFadeError,
                $"Unguided 63-degree rotation on a rectangular canvas: {unguidedError} vs fade {rigidFadeError}");
            interpolation = MorphInterpolation.Linear;
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
            var segment = new MorphSegment { fromStart = UV(new Vector2(8, 8)), fromEnd = UV(new Vector2(38, 8)),
                toStart = UV(new Vector2(12, 16)), toEnd = UV(new Vector2(42, 16)) };
            segments = new List<MorphSegment> { segment };
            Frame(null, .5f);
            var alongLine = Field();
            foreach (int x in new[] { 12, 20, 28, 38 })
            {
                context.Near(4, alongLine[12 * Width + x].r * Width, .55, "Segment interior X");
                context.Near(8, alongLine[12 * Width + x].g * Height, .55, "Segment interior Y");
            }
            // Identical straight cubics with unequal handle spacing should still match by arc length.
            var path = new MorphTransition();
            var cubic = new MorphSegment { bezier = true, fromStart = segment.fromStart, fromEnd = segment.fromEnd,
                toStart = segment.toStart, toEnd = segment.toEnd, fromOut = new Vector2(.01f,0),
                fromIn = new Vector2(-.30f,0), toOut = new Vector2(.30f,0), toIn = new Vector2(-.01f,0) };
            path.segments.Add(cubic);
            var prepare = typeof(MorphTransition).GetMethod("RasterGuides",BindingFlags.NonPublic|BindingFlags.Instance);
            List<MorphSegment> Pieces() => (List<MorphSegment>)prepare.Invoke(path,new object[]{Width,Height});
            segments = Pieces();
            Frame(null,.5f);
            var byLength = Field();
            context.Near(4,byLength[12*Width+24].r*Width,.55,"Different cubic parameter speeds match by image-space length");
            cubic.boundary = true;
            segments = Pieces();
            Frame(null,.5f);
            context.True(!byLength.AsSpan().SequenceEqual(Field()), "Boundary cuts motion links with the same cached pair");
            cubic.boundary = false;
            cubic.radius = .35f;
            segments = Pieces();
            Frame(null,.5f);
            context.True(!byLength.AsSpan().SequenceEqual(Field()), "Cubic radius changes the cached field");
            cubic.radius = 0;
            segments = Pieces();
            Frame(null,.5f);
            context.True(byLength.AsSpan().SequenceEqual(Field()), "Auto radius restores the original curve");
            cubic.marks.Add(new Vector2(.5f,.5f));
            segments = Pieces();
            Frame(null,.5f);
            context.True(!byLength.AsSpan().SequenceEqual(Field()),"A paired mark changes longitudinal correspondence");
            cubic.marks.Clear();
            segments = Pieces();
            Frame(null,.5f);
            context.True(byLength.AsSpan().SequenceEqual(Field()),"Removing mark restores the unmarked curve exactly");
            segments = new List<MorphSegment>{segment};
            segment.toEnd = UV(new Vector2(42, 24));
            Frame(null, .5f);
            context.True(!alongLine.AsSpan().SequenceEqual(Field()), "Moving one segment end invalidates cached flow");
            segments = null;
            Frame(null, .5f);
            context.True(freeField.AsSpan().SequenceEqual(Field()), "Removing the last segment restores unguided flow");
            var guide = Pair(20, 11, 28, 15);
            var pairList = new[] { guide };
            Frame(pairList, .25f);
            var pinnedField = Field();
            guide.radius = .3f;
            Frame(pairList, .25f);
            var broadPin = Field();
            context.True(!pinnedField.AsSpan().SequenceEqual(broadPin), "A point radius invalidates the cached field");
            context.Near(8, broadPin[13 * Width + 24].r * Width, .55, "Broad pin retains its center");
            guide.radius = 0;
            Frame(pairList, .25f);
            context.True(pinnedField.AsSpan().SequenceEqual(Field()), "Auto restores the original point field");
            var motion = pinnedField[13 * Width + 24];
            context.Near(8, motion.r * Width, .5, "Liao guide reaches X");
            context.Near(4, motion.g * Height, .5, "Liao guide reaches Y");
            context.True(pinnedField[13 * Width + 26].r > freeField[13 * Width + 26].r + .02f,
                "Guide moves an adjacent unconstrained texel");
            holdStill = true;
            Frame(pairList, .5f);
            var held = Field();
            context.Near(8, held[13 * Width + 24].r * Width, .55, "Explicit point overrides zero-motion evidence");
            var heldField = (RenderTexture)fieldInfo.GetValue(flowCache);
            Frame(pairList, .25f);
            context.True(heldField == (RenderTexture)fieldInfo.GetValue(flowCache), "Stationary field reused while scrubbing");
            holdStill = false;
            Frame(pairList, .5f);
            context.True(pinnedField.AsSpan().SequenceEqual(Field()), "Disabling stationary check restores cached-pair behavior");
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

            // Identical texture with a small exposure change offers genuine evidence
            // for zero displacement, even where a moving guide contradicts it.
            method = MorphRasterMethod.Liao;
            holdStill = true;
            interpolation = MorphInterpolation.AutomaticCurve;
            Color Painted(float x, float y, float gain) => Color.Lerp(Color.black, Color.white, TextureSignal(x,y)) * new Color(gain,gain,gain,1);
            Write(from, (x,y) => Painted(x,y,1));
            Write(to, (x,y) => Painted(x,y,1.02f));
            flowCache = Activator.CreateInstance(cacheType, true);
            cubic.marks.Clear();
            segments = Pieces();
            Frame(new[] { Pair(14,21,20,25) }, .25f);
            var texturedHeld = Field();
            var guidedMotion = texturedHeld[23*Width+17];
            context.Near(6, guidedMotion.r*Width, 1, "Point overrides matching textured background X");
            context.Near(4, guidedMotion.g*Height, .8, "Point overrides matching textured background Y");
            context.Near(.75, guidedMotion.a, .001, "Moving point clears identity confidence");
            var onCurve = texturedHeld[12*Width+24];
            context.Near(4, onCurve.r*Width, .8, "Cubic overrides matching textured background X");
            context.Near(8, onCurve.g*Height, .8, "Cubic overrides matching textured background Y");
            context.Near(.75, onCurve.a, .001, "Moving cubic clears identity confidence");
            var fixedDetail = texturedHeld[20*Width+53];
            context.True(fixedDetail.a > 1.20f, "Nearly identical remote patch has strong stationary evidence");
            context.Near(0, fixedDetail.r*Width, .25, "Stationary patch motion X");
            context.Near(0, fixedDetail.g*Height, .25, "Stationary patch motion Y");
            RenderTexture.active = (RenderTexture)cacheType.GetField("arc", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(flowCache);
            cpu.ReadPixels(new Rect(0,0,Width,Height),0,0,false);
            Color bend = cpu.GetPixel(53,20);
            context.Near(0, bend.b*Width, .1, "Remote stationary patch stays fixed along arc X");
            context.Near(0, bend.a*Height, .1, "Remote stationary patch stays fixed along arc Y");

            clearCache.Invoke(flowCache,null);
            segments = null;
            Write(to, (x,y) => Painted(x-4,y-2,1));
            Frame(null,.5f);
            var pan = Field()[16*Width+32];
            context.Near(4, pan.r*Width, .55, "Zero hypothesis preserves small camera translation X");
            context.Near(2, pan.g*Height, .55, "Zero hypothesis preserves small camera translation Y");
            clearCache.Invoke(flowCache,null);
            flowCache = null;
            holdStill = false;
            method = MorphRasterMethod.LocalContrast;
            interpolation = MorphInterpolation.Linear;

            // Exercise the renderer's evidence -> mask -> packed field with a
            // prescribed displacement. Re-solving flow would change the input
            // evidence and would not isolate the visibility decision.
            var pack = assembly.GetType("DCFApixels.WhimTex.MorphRasterRenderer", true).GetMethod("PackVisibility", flags);
            Write(from, (x, y) => new Color(Mathf.Clamp(-.16f * (x - 8), -1, 1) / Width, 0, 0, 1));
            Color[] PackedMask(bool remoteContrastInB)
            {
                Write(to, (x, y) =>
                {
                    float a = x < 8 || x >= 18 ? .6f : .15f;
                    float b = 0;
                    if (x >= Width / 2)
                    {
                        float stripe = ((x / 4) & 1) == 0 ? .9f : -.9f;
                        a = remoteContrastInB ? 0 : stripe;
                        b = remoteContrastInB ? stripe : 0;
                    }
                    return new Color(a, a, b, b);
                });
                var packed = (RenderTexture)pack.Invoke(null, new object[] { from, to });
                try
                {
                    RenderTexture.active = packed;
                    cpu.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                    return cpu.GetPixels();
                }
                finally
                {
                    RenderTexture.active = previous;
                    release.Invoke(null, new object[] { packed });
                }
            }
            var remoteA = PackedMask(false);
            var remoteB = PackedMask(true);
            int row = (Height / 2) * Width;
            for (int x = 4; x <= 20; x++)
                context.Near(remoteA[row + x].b, remoteB[row + x].b, 0,
                    $"Moving distant contrast between endpoints preserves packed visibility at x={x}");
            float localMask = remoteA[row + 8].b;
            context.True(localMask < -.1f && localMask > -.9f, $"Local compression gives a measurable unsaturated vote: {localMask}");
            context.Near(0, remoteA[row + 18].b, .001, "An unmatched edge with equal local footprints remains neutral");
            CheckVisibility(context);

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
