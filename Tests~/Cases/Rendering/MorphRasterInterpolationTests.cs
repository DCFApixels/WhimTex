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

    public static string Run()
    {
        string measured = null;
        var json = TestContext.Run(nameof(MorphRasterInterpolationTests), context => Check(context, value => measured = value));
        if (measured == null) return json;
        var result = JsonUtility.FromJson<TestResult>(json);
        result.message += "\n" + measured;
        return result.ToJson();
    }

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
        Texture2D evidence = null, readback = null, coarseMask = null;
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
            float Conflicting(float validity) => Probe((x,y) =>
            {
                float detail = ((x + y) & 1) == 0 ? .055f : -.055f;
                return new Color(.3f + detail, .3f - detail, -1, validity);
            });
            context.True(Mathf.Abs(Conflicting(0)) < .5f * Mathf.Abs(forward),
                "Conflicting nearby texture weakens the vote from compression");
            context.True(Conflicting(1) > .9f, "Verified endpoint coverage survives conflicting texture");
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

            // Identical coarse ownership, different fine structure. An aligned
            // edge in both keys should need less correction than a one-sided edge.
            coarseMask = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            material.SetTexture("_Flow", Texture2D.blackTexture);
            material.SetTexture("_VisibilityFeatures", evidence);
            material.SetVector("_MaskGrid", Vector4.one);
            int pack = material.FindPass("PackVisibility");
            float Refined(bool bothKeys, float validity)
            {
                coarseMask.SetPixel(0, 0, new Color(-.7f, .3f, .3f, validity));
                coarseMask.Apply();
                for (int i = 0; i < samples.Length; i++)
                {
                    float a = .3f + .15f * Mathf.Sign(i % size - size / 2);
                    if (i % size == size / 2) a = .3f;
                    float b = bothKeys ? a : .3f;
                    samples[i] = new Color(a,a,b,b);
                }
                evidence.SetPixels(samples);
                evidence.Apply();
                Graphics.Blit(coarseMask, target, material, pack);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(size / 2, size / 2, 1, 1), 0, 0, false);
                return readback.GetPixel(0, 0).b;
            }
            context.True(Mathf.Abs(Refined(true, 0)) < .02f, "Fine detail aligned in both keys suppresses the coarse vote");
            context.Near(-.7f, Refined(false, 0), .004, "A one-sided edge keeps its supported vote");
            context.Near(-.7f, Refined(true, -1), .004, "Endpoint coverage takes precedence over fine detail");
        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = srgb;
            if (target != null) RenderTexture.ReleaseTemporary(target);
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (coarseMask != null) UnityEngine.Object.DestroyImmediate(coarseMask);
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
            foreach (int count in new[] { 1, 8 })
            {
                // Directed correspondences with known full motion d(r) = (1,-2) + .8 perp(r).
                Color Endpoint(float x)
                {
                    var mid = new Vector2(16.5f + x,16.5f);
                    var half = .5f * new Vector2(1,-2 + .8f*x);
                    return new Color((mid.x-half.x)/w,(mid.y-half.y)/h,(mid.x+half.x)/w,(mid.y+half.y)/h);
                }
                var table = new Color[3 * (count + 1)];
                for (int i = 0; i < count; i++)
                {
                    table[3*i] = Endpoint(-5 + 10f*i*i/(count*count));
                    table[3*i+1] = Endpoint(-5 + 10f*(i+1)*(i+1)/(count*count));
                    table[3*i+2] = new Color(i == 0 ? count : 1,.3f,0,1);
                }
                // A coincident legacy edge contradicts the curve. Even in a mixed
                // document it must contribute only to the ordinary motion solver.
                var left = Endpoint(-5); var right = Endpoint(5);
                table[3*count] = new Color(left.b,left.a,left.r,left.g);
                table[3*count+1] = new Color(right.b,right.a,right.r,right.g);
                table[3*count+2] = new Color(1,.3f,0,0);
                UnityEngine.Object.DestroyImmediate(pins);
                pins = new Texture2D(table.Length,1,TextureFormat.RGBAFloat,false,true);
                pins.SetPixels(table);
                pins.Apply();
                material.SetTexture("_AnchorTable",pins);
                material.SetInteger("_AnchorWidth",table.Length);
                material.SetInteger("_AnchorCount",0);
                material.SetInteger("_SegmentCount",count+1);
                material.SetInteger("_RigidHasPaths",1);
                Graphics.Blit(null,models,material,pass);
                RenderTexture.active = models;
                cpu.ReadPixels(new Rect(0,0,w,h),0,0,false);
                Color fitted = cpu.GetPixel(16,16);
                context.Near(1, fitted.r * h, .002, "Path local translation X");
                context.Near(-2, fitted.g * h, .002, "Path local translation Y");
                context.Near(.8, fitted.b, .002, "Path tangent determines signed halfway rotation");
                context.True(fitted.a > .98f, "Single directed curve supplies local rotation support");
            }
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

    private static void Check(TestContext context, Action<string> report)
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

            bool classicLiao = false;
            System.Collections.Generic.List<MorphSegment> segments = null;
            bool preserveGeometry = false, holdStill = false, visibilityMask = true;
            var maskBlend = MorphMaskBlend.Original;
            var interpolation = MorphInterpolation.Linear;
            var guided = render.DeclaringType.GetMethod("RenderGuided", flags);
            bool useAuthoredGuides = false;
            Color[] Frame(IReadOnlyList<MorphAnchor> pairs, float t, bool writeSrgb = false)
            {
                RenderTexture.active = from;
                GL.sRGBWrite = writeSrgb;
                object[] args = { from, to, pairs, t, flowCache, null, interpolation, classicLiao, preserveGeometry, segments, holdStill, visibilityMask, false, maskBlend };
                var method = render;
                if (useAuthoredGuides)
                {
                    var withRaster = new object[args.Length + 1];
                    withRaster[0] = true;
                    args.CopyTo(withRaster, 1);
                    args = withRaster;
                    method = guided;
                }
                var image = (RenderTexture)method.Invoke(null, args);
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

            float TextureSignal(float x, float y) => .47f + .19f * Mathf.Sin(.43f*x + .22f*y)
                + .16f * Mathf.Cos(.12f*x - .39f*y) + .13f * Mathf.Sin(.27f*x - .18f*y);
            // Run the measured comparison first so the receipt keeps the numbers even
            // if a later, independent rendering regression fails.
            {
                Color Pattern(int x, int y)
                {
                    float phase = x * (2 * Mathf.PI / 16);
                    float s = Mathf.Cos(phase + .05f*y), c = Mathf.Sin(phase - .08f*y);
                    return new Color(.5f+.38f*s,.45f+.31f*c,.52f+.2f*s*c,.86f+.09f*s);
                }
                Write(from, Pattern);
                Write(to, Pattern);
                var transition = new MorphTransition();
                var guidePath = new MorphGuidePath { radius = .16f, marks = new() { new Vector2(.31f,.31f), new Vector2(.72f,.72f) } };
                var canvas = new Vector2(Width,Height);
                foreach (bool source in new[] {true,false})
                {
                    var side = source ? guidePath.from : guidePath.to;
                    Vector2 Pos(int x, int y) => new Vector2(x + (source ? .5f : 16.5f), y+.5f) / canvas;
                    side.Add(new MorphPathKnot { position = Pos(8,5), outTangent = new Vector2(6,-3)/canvas });
                    side.Add(new MorphPathKnot { position = Pos(20,15), inTangent = new Vector2(-3,-6)/canvas,
                        outTangent = new Vector2(3,6)/canvas, smooth = true });
                    side.Add(new MorphPathKnot { position = Pos(39,23), inTangent = new Vector2(-6,4)/canvas });
                }
                transition.guides.Add(guidePath);
                // The crossing stays still. Its competition with the moving guide
                // exposes accidental extra votes from newly inserted knots.
                var fixedStart = new Vector2(32.5f,8.5f) / canvas;
                var fixedEnd = new Vector2(32.5f,24.5f) / canvas;
                transition.segments.Add(new MorphSegment { fromStart = fixedStart, toStart = fixedStart,
                    fromEnd = fixedEnd, toEnd = fixedEnd, radius = .16f });
                const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
                var preparePath = typeof(MorphTransition).GetMethod("RasterGuides",instance);
                var cutSource = typeof(MorphGuidePath).GetMethod("Split",instance);
                List<MorphSegment> PreparePath() => (List<MorphSegment>)preparePath.Invoke(transition,new object[]{Width,Height});
                string[] untouched = guidePath.to.ConvertAll(k => JsonUtility.ToJson(k)).ToArray();
                var marks = guidePath.marks.ToArray();
                flowCache = Activator.CreateInstance(cacheType,true);
                var clear = cacheType.GetMethod("Clear",instance);
                useAuthoredGuides = true;
                preserveGeometry = holdStill = true;
                interpolation = MorphInterpolation.AutomaticCurve;
                var unguided = Frame(null,.5f);
                segments = PreparePath();
                var baselineChords = segments.ToArray();
                float[] phases = {.25f,.5f,.75f};
                var baselineFrames = new Color[phases.Length][];
                for (int k = 0; k < phases.Length; k++) baselineFrames[k] = Frame(null,phases[k]);
                float effect = 0;
                for (int i = 0; i < unguided.Length; i++)
                    effect = Mathf.Max(effect,Mathf.Abs(unguided[i].r-baselineFrames[1][i].r));
                // Work from the end of each original span. This creates cuts at
                // 1/6, 2/6, 3/6, 4/6 and 5/6 on both original cubics of A.
                for (int span = 1; span >= 0; span--)
                    for (int parts = 6; parts > 1; parts--)
                        context.True((bool)cutSource.Invoke(guidePath,new object[]{true,span,(parts-1f)/parts}), "Cut source span " + span);
                context.Equal(12,guidePath.from.Count-1,"Ten source-only cuts produce twelve spans");
                context.True(untouched.AsSpan().SequenceEqual(guidePath.to.ConvertAll(k => JsonUtility.ToJson(k)).ToArray()) &&
                    marks.AsSpan().SequenceEqual(guidePath.marks.ToArray()), "B and whole-path marks stay identical");
                string[] measurements = new string[2];
                bool withinTolerance = true;
                for (int run = 0; run < measurements.Length; run++)
                {
                    string stage = run == 0 ? "live" : "JSON reload";
                    if (run == 1)
                    {
                        transition = JsonUtility.FromJson<MorphTransition>(JsonUtility.ToJson(transition));
                        context.True(!ReferenceEquals(transition.guides[0],guidePath) && transition.guides[0].from.Count == 13,
                            "Measure the deserialized twelve-span path with fresh length and chord tables");
                    }
                    clear.Invoke(flowCache,null);
                    segments = PreparePath();
                    bool identical = segments.Count == baselineChords.Length;
                    for (int i = 0; identical && i < segments.Count; i++)
                    {
                        var before = baselineChords[i]; var after = segments[i];
                        identical = before.fromStart.Equals(after.fromStart) && before.toStart.Equals(after.toStart) &&
                            before.fromEnd.Equals(after.fromEnd) && before.toEnd.Equals(after.toEnd);
                    }
                    double sum = 0, squared = 0, maximum = 0;
                    int worstPixel = 0, worstChannel = 0, worstFrame = 0;
                    for (int k = 0; k < phases.Length; k++)
                    {
                        var actual = Frame(null,phases[k]);
                        for (int i = 0; i < actual.Length; i++)
                            for (int channel = 0; channel < 4; channel++)
                            {
                                double error = Math.Abs((double)actual[i][channel] - baselineFrames[k][i][channel]);
                                sum += error;
                                squared += error*error;
                                if (error > maximum)
                                {
                                    maximum = error;
                                    worstPixel = i; worstChannel = channel; worstFrame = k;
                                }
                            }
                    }
                    int count = 4 * Width * Height * phases.Length;
                    double mae = sum / count, rms = Math.Sqrt(squared / count);
                    char component = "RGBA"[worstChannel];
                    measurements[run] = FormattableString.Invariant($"GPU A 2->12 spans ({stage}), B 2 unchanged, {Width}x{Height} RGBAFloat, t=0.25/0.5/0.75, n={count}: maxAbs={maximum:G9}, MAE={mae:G9}, RMSE={rms:G9}, worst=({worstPixel%Width},{worstPixel/Width})/{component} at t={phases[worstFrame]}, guide delta vs unguided={effect:G9}, identical paired chords={identical} ({baselineChords.Length}/{segments.Count})");
                    withinTolerance &= identical && maximum < .1 && mae < .003 && rms < .01;
                }
                string measurement = string.Join("\n", measurements);
                report(measurement);
                context.True(effect > .15f, "The original guide visibly changes the midpoint. " + measurement);
                context.True(withinTolerance, "Subdivision changed solver chords or exceeded GPU pixel tolerance. " + measurement);
                clear.Invoke(flowCache,null);
                flowCache = null;
                segments = null;
                useAuthoredGuides = preserveGeometry = holdStill = false;
                interpolation = MorphInterpolation.Linear;
            }
            CheckContourVotes(context);
            CheckIndependentRigidParts(context);
            CheckPathAtlas(context);
            Write(from, (x, y) => Box(x, y, 8, 4, First));
            Write(to, (x, y) => Box(x, y, 32, 16, Second));
            var manual = new[] { Pair(8, 4, 32, 16) };
            var start = Frame(manual, 0, true);
            Pixel(start, 8, 4, First, "First endpoint");
            Pixel(start, 32, 16, Hidden, "First endpoint hidden RGB");
            var end = Frame(null, 1);
            Pixel(end, 32, 16, Second, "Last endpoint");
            Pixel(end, 8, 4, Hidden, "Last endpoint hidden RGB");
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
            var manyPins = new List<MorphAnchor>();
            for (int i = 0; i < 256; i++) manyPins.Add(Pair(8, 5, 8, 5));
            manyPins.Add(Pair(38, 24, 46, 24));
            Frame(manyPins, .5f);
            context.Near(8, Field()[24 * Width + 42].r * Width, .6, "Guide beyond the first 256 entries reaches the flow field");
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
            // Two paths compete on exactly the same halfway line. One has eight source
            // spans, one destination span and seven marks forcing eight GPU chords.
            // Making eight independent authored curves instead must change their total vote.
            {
                var transition = new MorphTransition();
                MorphPathKnot KnotAt(float x, float y) => new MorphPathKnot { position = UV(new Vector2(x,y)) };
                var up = new MorphGuidePath { to = new() { KnotAt(8,16), KnotAt(40,16) } };
                for (int i = 0; i <= 8; i++)
                {
                    up.from.Add(KnotAt(8+4*i,8));
                    if (i > 0 && i < 8) up.marks.Add(new Vector2(i/8f,i/8f));
                }
                var down = new MorphGuidePath { from = new() { KnotAt(8,16),KnotAt(40,16) }, to = new() { KnotAt(8,8),KnotAt(40,8) } };
                transition.guides.Add(up);
                transition.guides.Add(down);
                float Vote()
                {
                    segments = (List<MorphSegment>)prepare.Invoke(transition,new object[] {Width,Height});
                    Frame(null,.5f);
                    return Field()[12*Width+24].g*Height;
                }
                context.Near(0,Vote(),.45,"Equal and opposite guides cancel despite 8:1 source-span count");
                transition.guides.Remove(up);
                for (int i = 0; i < 8; i++)
                    transition.guides.Add(new MorphGuidePath { from = new() {KnotAt(8,8),KnotAt(40,8)},
                        to = new() {KnotAt(8,16),KnotAt(40,16)} });
                context.Near(8*7/9f,Vote(),.65,"Eight independent guides have eight votes against one");
            }
            {
                var legacy = new MorphTransition { nodes = new() {
                    new MorphGuideNode { from = segment.fromStart, to = segment.toStart },
                    new MorphGuideNode { from = segment.fromEnd, to = segment.toEnd } },
                    segments = new() { new MorphSegment { startNode = 1, endNode = 2 } } };
                var replacement = new MorphGuidePath { from = new() {
                    new MorphPathKnot { position = segment.fromStart }, new MorphPathKnot { position = segment.fromEnd } },
                    to = new() { new MorphPathKnot { position = segment.toStart }, new MorphPathKnot { position = segment.toEnd } } };
                var boundMaterial = (Material)render.DeclaringType.GetField("material",flags).GetValue(null);
                void AssertLocalPass(IReadOnlyList<MorphAnchor> pins, bool active, bool paths)
                {
                    segments = (List<MorphSegment>)prepare.Invoke(legacy,new object[] {Width,Height});
                    Frame(pins,.5f);
                    context.Equal(active ? 1 : 0,boundMaterial.GetInteger("_HasRigidGuides"),"Legacy/path local rigidity gate");
                    if (active) context.Equal(paths ? 1 : 0,boundMaterial.GetInteger("_RigidHasPaths"),"Local path observations enabled");
                }
                preserveGeometry = true;
                AssertLocalPass(null,false,false);
                var onePin = new[] { Pair(4,8,8,16) };
                AssertLocalPass(onePin,false,false);
                AssertLocalPass(new[] { onePin[0], Pair(12,20,16,28) },true,false);
                AssertLocalPass(null,false,false);
                var oldEdge = segments[0];
                legacy.segments.Clear();
                legacy.guides.Add(replacement);
                AssertLocalPass(null,true,true);
                var spanField = typeof(MorphSegment).GetField("chordCount",BindingFlags.NonPublic|BindingFlags.Instance);
                context.True(segments.Count == 1 && segments[0].fromStart.Equals(oldEdge.fromStart) &&
                    segments[0].fromEnd.Equals(oldEdge.fromEnd) && segments[0].toStart.Equals(oldEdge.toStart) &&
                    segments[0].toEnd.Equals(oldEdge.toEnd) && spanField.GetValue(oldEdge).Equals(spanField.GetValue(segments[0])),
                    "Same cached pair, UVs and grouping distinguish a path from a legacy segment");
                legacy.guides.Clear();
                legacy.segments.Add(new MorphSegment { startNode = 1, endNode = 2 });
                AssertLocalPass(null,false,false);
                preserveGeometry = false;
            }
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
            clearCache.Invoke(flowCache,null);
            visibilityMask = false;
            var unmasked = Frame(pairList,.25f);
            var motionWithoutMask = Field();
            visibilityMask = true;
            Frame(pairList,.25f);
            var packedWithMask = Field();
            for (int i = 0; i < motionWithoutMask.Length; i++)
                if (packedWithMask[i].r != motionWithoutMask[i].r || packedWithMask[i].g != motionWithoutMask[i].g)
                    throw new InvalidOperationException("Toggling visibility changed the motion field at " + i);
            visibilityMask = false;
            context.True(unmasked.AsSpan().SequenceEqual(Frame(pairList,.25f)), "Disabling the cached mask restores the exact unmasked frame");
            visibilityMask = true;
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
            clearCache.Invoke(flowCache, null);
            flowCache = null;

            // Identical texture with a small exposure change offers genuine evidence
            // for zero displacement, even where a moving guide contradicts it.
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
            Frame(new[] { Pair(20,11,24,13) }, .5f);
            var evidenceField = cacheType.GetField("layerEvidence", BindingFlags.Instance | BindingFlags.NonPublic);
            RenderTexture.active = (RenderTexture)evidenceField.GetValue(flowCache);
            cpu.ReadPixels(new Rect(0,0,1,1),0,0,false);
            context.Near(0, cpu.GetPixel(0,0).r, .001, "A guide following a camera pan cannot establish a fixed backdrop");
            RenderTexture.active = from;
            clearCache.Invoke(flowCache,null);
            flowCache = null;
            // A rigid object passes in front of a stationary gridded backdrop.
            // At these phases its silhouette and the visible cyan marks are known exactly.
            Color Crossing(int x, int y, float t)
            {
                int localX = x - 16 - Mathf.RoundToInt(24 * t);
                if (Mathf.Abs(localX) <= 5 && y >= 10 && y < 22)
                    return localX < -3 || y == 21 ? Color.yellow : Color.red;
                if (x % 11 == 2 && (y % 9 == 1 || y % 9 == 2)) return Color.cyan;
                if (x == 28 || x == 29) return Color.white;
                return x % 7 == 0 || y % 7 == 0 ? Color.gray : Color.black;
            }
            Write(from, (x,y) => Crossing(x,y,0));
            Write(to, (x,y) => Crossing(x,y,1));
            var objectGuides = new[] { Pair(14,13,38,13), Pair(18,19,42,19) };
            flowCache = Activator.CreateInstance(cacheType, true);
            foreach (float t in new[] { .25f, .5f, .75f })
            {
                var frame = Frame(objectGuides,t);
                int paintedHeight = 0, falseMarks = 0, missingMarks = 0;
                int centerX = 16 + Mathf.RoundToInt(24 * t);
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        var sample = frame[y*Width+x];
                        var truth = Crossing(x,y,t);
                        if (x == centerX && sample.r - sample.b > .5f) paintedHeight++;
                        bool cyan = sample.g - sample.r > .45f && sample.b - sample.r > .45f;
                        if (cyan && truth != Color.cyan) falseMarks++;
                        if (!cyan && truth == Color.cyan) missingMarks++;
                        if (truth == Color.white) context.True(sample.r > .95f && sample.g > .95f && sample.b > .95f,
                            $"Visible stripe stays white at {t}, {x},{y}");
                    }
                context.Equal(12, paintedHeight, $"Moving silhouette height at {t}");
                context.Equal(0, falseMarks, $"No duplicate background marks at {t}");
                context.Equal(0, missingMarks, $"Every visible background mark remains at {t}");
            }
            clearCache.Invoke(flowCache,null);
            var turnGuidesAcrossStripe = new[] { Pair(12,15,40,11), Pair(20,15,40,19) };
            Color TurningKey(int x, int y, float time)
            {
                float dx = x - Mathf.Lerp(16,40,time), dy = y - 15;
                float angle = Mathf.PI * .5f * time;
                float u = dx * Mathf.Cos(angle) + dy * Mathf.Sin(angle);
                float v = dy * Mathf.Cos(angle) - dx * Mathf.Sin(angle);
                if (Mathf.Abs(u) < 7.25f && Mathf.Abs(v) < 4.25f)
                    return u < -3 ? Color.yellow : Color.red;
                if (x % 11 == 2 && y % 8 == 2) return Color.cyan;
                if (x == 28) return Color.white;
                return x % 7 == 0 || y % 7 == 0 ? Color.gray : Color.black;
            }
            Write(from, (x,y) => TurningKey(x,y,0));
            Write(to, (x,y) => TurningKey(x,y,1));
            foreach (float t in new[] { .25f, .5f, .75f })
            {
                var frame = Frame(turnGuidesAcrossStripe,t);
                int intersection = 0, union = 0;
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        var sample = frame[y*Width+x];
                        var truth = TurningKey(x,y,t);
                        bool inside = truth.r - truth.b > .5f;
                        bool painted = sample.r - sample.b > .5f;
                        if (inside && painted) intersection++;
                        if (inside || painted) union++;
                        if (Mathf.Abs(y - 15) >= 10 && (truth == Color.white || truth == Color.cyan))
                            context.True((sample - truth).maxColorComponent < .02f &&
                                (truth - sample).maxColorComponent < .02f, $"Turning object leaves fixed background at {x},{y}, {t}");
                    }
                context.True(intersection > .85f * union, $"Rotating silhouette at {t}: {intersection}/{union}");
            }
            clearCache.Invoke(flowCache,null);
            interpolation = MorphInterpolation.Linear;
            Frame(turnGuidesAcrossStripe,.5f);
            context.True(evidenceField.GetValue(flowCache) == null, "Linear point paths keep their authored chords");
            clearCache.Invoke(flowCache,null);
            flowCache = null;
            holdStill = false;
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

            // Isolate color blending from correspondence: the same textured surface changes
            // exposure and contrast. Force a known visibility preference on its cached field.
            flowCache = Activator.CreateInstance(cacheType, true);
            interpolation = MorphInterpolation.Linear;
            classicLiao = preserveGeometry = holdStill = false;
            segments = null;
            visibilityMask = true;
            Color Tone(float mean, float detail) => new Color(mean + detail, mean + detail, mean + detail, 1);
            Write(from, (x, y) => Tone(.25f, ((x + y) % 2 == 0 ? 1 : -1) * .04f));
            Write(to, (x, y) => Tone(.65f, ((x + y) % 2 == 0 ? 1 : -1) * .10f));
            Frame(null, .5f);
            var toneField = (RenderTexture)fieldInfo.GetValue(flowCache);
            Write(toneField, (x,y) => new Color(0, 0, .6f, 1));
            var original = Frame(null, .5f);
            maskBlend = MorphMaskBlend.SeparateTone;
            var split = Frame(null, .5f);
            maskBlend = MorphMaskBlend.NormalizeDetail;
            var normalized = Frame(null, .5f);
            const int tonePixel = Width * 16 + 32;
            float Mean(Color[] pixels) => .5f * (pixels[tonePixel].r + pixels[tonePixel + 1].r);
            float Amplitude(Color[] pixels) => .5f * (pixels[tonePixel].r - pixels[tonePixel + 1].r);
            context.True(Mean(original) > .58f, "Original mask transfers the brighter endpoint early");
            context.Near(.45f, Mean(split), .008, "Base tone follows timeline on a matched surface");
            context.Near(.45f, Mean(normalized), .008, "Normalization preserves base tone");
            context.True(Amplitude(normalized) < Amplitude(split) - .008f, "Separated detail can also follow the target contrast");
            context.Near(.07f, Amplitude(normalized), .008, "Target contrast is halfway between endpoint contrasts");
            visibilityMask = false;
            var withoutMask = Frame(null, .5f);
            maskBlend = MorphMaskBlend.Original;
            context.True(withoutMask.AsSpan().SequenceEqual(Frame(null,.5f)), "Mask off bypasses both tone modes");
            visibilityMask = true;
            clearCache.Invoke(flowCache, null);
            // A paired point on a differently lit surface. Force a known visibility vote at zero
            // motion to distinguish localized tone repair from a global change of the blend.
            var correctionPoint = Pair(16, 10, 16, 10);
            var authored = new[] { correctionPoint };
            maskBlend = MorphMaskBlend.Original;
            Frame(authored, .5f);
            Write((RenderTexture)fieldInfo.GetValue(flowCache), (x,y) => new Color(0, 0, .7f, 1));
            var control = Frame(authored, .5f);
            useAuthoredGuides = true;
            context.True(control.AsSpan().SequenceEqual(Frame(authored, .5f)), "No correction preserves the baseline");
            correctionPoint.correction = MorphGuideCorrection.Create(MorphCorrectionMode.MatchTone);
            correctionPoint.correction.width = .3f;
            var repaired = Frame(authored, .5f);
            int center = 10 * Width + 16;
            context.True(repaired[center].r < control[center].r - .05f, "Match Tone corrects biased lighting");
            Pixel(repaired, 52, 25, control[25 * Width + 52], "Outside correction");
            correctionPoint.correction.strength = 0;
            context.True(control.AsSpan().SequenceEqual(Frame(authored, .5f)), "Zero strength bypasses correction");
            correctionPoint.correction = MorphGuideCorrection.Create(MorphCorrectionMode.LocalChoice);
            for (int i = 0; i <= 1; i++)
            {
                useAuthoredGuides = false;
                var key = Frame(authored, i);
                useAuthoredGuides = true;
                context.True(key.AsSpan().SequenceEqual(Frame(authored, i)), "Authored correction preserves key " + i);
            }
            // A one-key-only bright speck is less coherent with its neighbours. Prefer the other key locally.
            clearCache.Invoke(flowCache, null);
            Write(from, (x,y) => Tone(.2f, 0));
            Write(to, (x,y) => Tone(x == 16 && y == 10 ? .95f : .2f, 0));
            useAuthoredGuides = false;
            Frame(authored, .5f);
            Write((RenderTexture)fieldInfo.GetValue(flowCache), (x,y) => new Color(0,0,0,1));
            control = Frame(authored, .5f);
            useAuthoredGuides = true;
            var chosen = Frame(authored, .5f);
            context.True(chosen[center].r < control[center].r - .02f, "Local Choice follows neighbourhood support");
            Pixel(chosen, 52, 25, control[25 * Width + 52], "Local Choice outside radius");
            // A closed contour must preserve an identical translucent image, including the anti-aliased matte.
            var corners = new[] { new Vector2(.2f,.2f), new Vector2(.7f,.2f), new Vector2(.55f,.8f) };
            segments = new List<MorphSegment>();
            for (int i = 0; i < corners.Length; i++) segments.Add(new MorphSegment {
                fromStart = corners[i], toStart = corners[i], fromEnd = corners[(i+1)%3], toEnd = corners[(i+1)%3], foreground = true });
            clearCache.Invoke(flowCache, null);
            Color SameSurface(int x, int y) => new Color(.2f + .17f * Mathf.Sin(.32f*x),.3f,.7f,.6f);
            Write(from, SameSurface);
            Write(to, SameSurface);
            var layers = Frame(null,.5f);
            for (int i=0;i<layers.Length;i++)
                if (Mathf.Abs(layers[i].a-.6f)>.003f || Mathf.Abs(layers[i].b-.7f)>.003f ||
                    Mathf.Abs(layers[i].r-SameSurface(i%Width,i/Width).r)>.003f)
                    throw new Exception("Foreground contour changed identical endpoints at " + i + ": " + layers[i]);
            var contour = new MorphGuidePath { closed = true, foreground = true,
                correction = MorphGuideCorrection.Create(MorphCorrectionMode.MatchTone) };
            foreach (var corner in corners)
            {
                contour.from.Add(new MorphPathKnot { position = corner });
                contour.to.Add(new MorphPathKnot { position = corner });
            }
            var outline = new MorphTransition { guides = new() { contour } };
            var splitPath = typeof(MorphGuidePath).GetMethod("Split",BindingFlags.NonPublic|BindingFlags.Instance);
            context.True((bool)splitPath.Invoke(contour,new object[]{false,2,.37f}), "Subdivide destination closing span");
            segments = (List<MorphSegment>)prepare.Invoke(outline,new object[] {Width,Height});
            var closedFrame = Frame(null,.5f);
            for (int i = 0; i < layers.Length; i++)
                if (((Vector4)(layers[i]-closedFrame[i])).magnitude > .006f)
                    throw new Exception("Path foreground/correction differs at the subdivided seam or matte at " + i);
            useAuthoredGuides = false;
            segments = null;
            clearCache.Invoke(flowCache, null);
            // The same local vote can be a thin outline or part of a broad occlusion.
            // Also place a horizontal stripe where the opaque source samples past the left edge.
            Write(from, (x,y) => Tone(.2f, 0));
            Write(to, (x,y) => Tone(x < 48 ? .8f : .7f, 0));
            Frame(null, .5f);
            toneField = (RenderTexture)fieldInfo.GetValue(flowCache);
            Write(toneField, (x,y) => new Color(8f / Width, 0, x >= 40 || x == 24 || y == 12 ? .7f : 0, 1));
            var untrimmed = Frame(null, .5f);
            foreach (var mode in new[] { MorphMaskBlend.SeparateTone, MorphMaskBlend.NormalizeDetail })
            {
                maskBlend = mode;
                var trimmed = Frame(null, .5f);
                context.True(untrimmed[16 * Width + 24].r > .7f, "Control has a visible rim");
                context.Near(.5, trimmed[16 * Width + 24].r, .003, "Remove isolated contour bias in " + mode);
                context.Near(.5, trimmed[12 * Width + 1].r, .003, "Handle clamped opaque source in " + mode);
                context.Near(untrimmed[16 * Width + 41].r, trimmed[16 * Width + 41].r, .001,
                    "Retain a supported ownership step in " + mode);
            }
            maskBlend = MorphMaskBlend.Original;
            clearCache.Invoke(flowCache, null);
            flowCache = null;


            // Render a full-size image from a lower-resolution motion field.
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
                float sx = (x - 18*time)/3f;
                float v = TextureSignal(sx, y/3f) + .08f * Mathf.Cos(1.1f*sx - .45f*y);
                return new Color(v,v,v,1);
            }
            Write(from,(x,y) => Detail(x,y,0));
            Write(to,(x,y) => Detail(x,y,1));
            flowCache = Activator.CreateInstance(cacheType, true);
            var panGuides = new List<MorphAnchor>();
            foreach (var uv in new[] { new Vector2(.2f,.25f), new Vector2(.6f,.25f), new Vector2(.45f,.75f) })
                panGuides.Add(new MorphAnchor { from = uv, to = uv + new Vector2(18f/largeW,0) });
            var detailed = Frame(panGuides,.5f);
            float autoError = 0, crossfadeError = 0;
            for (int y = 0; y < largeH; y++)
                for (int x = 0; x < largeW; x++)
                {
                    Color sample = detailed[y*largeW+x];
                    if (sample.a < .999f) throw new Exception($"Opaque frame lost coverage at {x},{y}: {sample.a}");
                    if (x < 20 || x >= largeW-20 || y < 10 || y >= largeH-10) continue;
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
            Frame(panGuides,.75f);
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

    private static void CheckPathAtlas(TestContext context)
    {
        var guide = new MorphGuidePath { closed = true, foreground = true,
            correction = MorphGuideCorrection.Create(MorphCorrectionMode.LocalChoice) };
        guide.correction.width = .125f;
        guide.correction.strength = .75f;
        foreach (var a in new[] { new Vector2(.125f,.25f), new Vector2(.625f,.25f),
            new Vector2(.625f,.75f), new Vector2(.125f,.75f) })
        {
            guide.from.Add(new MorphPathKnot { position = a });
            guide.to.Add(new MorphPathKnot { position = a + new Vector2(.25f,0) });
        }
        var transition = new MorphTransition { guides = new() {guide} };
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var compile = typeof(MorphTransition).GetMethod("RasterGuides",flags);
        var type = typeof(MorphTransition).Assembly.GetType("DCFApixels.WhimTex.MorphCorrectionAtlas",true);
        using var atlas = (IDisposable)Activator.CreateInstance(type,true);
        Texture2D Prepare()
        {
            var edges = compile.Invoke(transition,new object[] {96,32});
            type.GetMethod("Prepare",flags).Invoke(atlas,new object[] {null,edges,96,32});
            return (Texture2D)type.GetProperty("Texture",flags).GetValue(atlas);
        }
        var texture = Prepare();
        Color left = texture.GetPixelBilinear(.25f,.5f), right = texture.GetPixelBilinear(.75f,.5f);
        context.True(left.r > .99f && left.g < .01f && right.g > .99f && right.r < .01f,
            "Closed path produces separate endpoint mattes on a rectangular canvas");
        context.Near(.75,left.b,.008,"Local correction covers the halfway closing edge exactly once");
        context.Near(0,texture.GetPixelBilinear(.5f,.5f).b,.008,"Correction is localized to the outline");
        var before = texture.GetPixels32();
        var split = typeof(MorphGuidePath).GetMethod("Split",flags);
        context.True((bool)split.Invoke(guide,new object[] {true,3,.31f}), "Independent closing-span split");
        var after = Prepare().GetPixels32();
        for (int i = 0; i < before.Length; i++)
            if (Math.Abs(before[i].r-after[i].r) > 1 || Math.Abs(before[i].g-after[i].g) > 1 ||
                Math.Abs(before[i].b-after[i].b) > 1)
                throw new Exception("Subdividing a closing edge changed the matte or correction at " + i);
    }
}
