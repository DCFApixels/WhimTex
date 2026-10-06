using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests.UnityC;

// Only package-owned members are reflected; all inputs and windows are transient.
public static class SmudgeBrushTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static FixtureScope S => FixtureContext.Scope;
    static WhimTex.Tests.TestContext T => FixtureContext.Context;
    static object Call(object owner, string name, params object[] args) =>
        (owner as Type ?? owner.GetType()).GetMethod(name, Flags).Invoke(owner is Type ? null : owner, args);
    static object Get(object owner, string name) => owner.GetType().GetField(name, Flags).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Flags).SetValue(owner, value);
    static Texture2D Pixels(DrawingLayerBehaviour layer) => (Texture2D)typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Flags).GetValue(layer);
    static TextureCompositor Document(int width = 128, int height = 64)
    {
        var doc = S.Own(ScriptableObject.CreateInstance<TextureCompositor>());
        doc.width = width; doc.height = height;
        return doc;
    }
    static DrawingLayerBehaviour Drawing(TextureCompositor doc, Func<int, int, Color> color, int width = 128, int height = 64)
    {
        var texture = S.Own(new Texture2D(width, height, TextureFormat.RGBAHalf, false, true));
        var values = new Color[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) values[y * width + x] = color(x, y);
        texture.SetPixels(values); texture.Apply();
        var drawing = (DrawingLayerBehaviour)Call(typeof(DrawingLayerBehaviour), "FromMergedTexture", texture);
        doc.layers.Add(drawing);
        Call(doc, "NormalizeModel");
        return drawing;
    }
    static void Begin(TextureCompositor doc, DrawingLayerBehaviour layer, Vector2 uv, float size = 20, RenderTexture sample = null, bool tiled = false)
    {
        Call(doc, "GetPaintTransform", layer.Owner);
        Call(layer, "PrepareStroke", doc.width, doc.height, "Smudge test");
        Call(layer, "BeginStroke", uv);
        Call(layer, "BeginSmudgeStroke", uv, doc.width, doc.height, size, sample, tiled);
    }
    static void Segment(TextureCompositor doc, DrawingLayerBehaviour layer, Vector2 from, Vector2 to,
        float strength = .95f, float flow = 1, float hardness = .8f, Texture selection = null) =>
        Call(layer, "SmudgeSegment", from, to, doc.width, doc.height, hardness, strength, flow, selection);
    static void End(DrawingLayerBehaviour layer)
    {
        Call(layer, "EndStroke"); Call(layer, "SyncSurfaceToTexture");
        foreach (string field in new[] { "smudgeCarry", "smudgeNext", "smudgeBackdrop", "smudgeSample", "smudgeSampleBackdrop" })
            T.True(Get(layer, field) == null, "Owned Smudge buffer released: " + field);
    }
    static double Difference(Color[] a, Color[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) + Math.Abs(a[i].a - b[i].a);
        return sum;
    }
    static Color Split(int x, int y) => x < 48 ? new Color(3, .1f, -.2f, 1) : new Color(.1f, .2f, 2, 1);
    static Vector2 Start => new Vector2(40f / 128, .5f);
    static Vector2 Finish => new Vector2(84f / 128, .5f);

    // Translating along stripes must not erase detail across the stroke. Compare
    // actual stored pixels, not a replacement CPU model of the transport shader.
    public static string Quality() => FixtureContext.RunReport("Smudge feedback and transverse pixel detail", () =>
    {
        var reports = new System.Collections.Generic.List<string>();
        VerifyPaintedPatchFeedback(reports);
        foreach (bool sampled in new[] { false, true })
        foreach (int nativeScale in new[] { 1, 2 })
        foreach (float phase in new[] { 0f, .35f })
        foreach (bool soft in new[] { false, true })
        {
            const int width = 256, height = 128;
            var doc = Document(width, height);
            Color Stripe(int x, int y) => y % 4 < 2 ? Color.white : Color.black;
            var layer = Drawing(doc, sampled ? (Func<int, int, Color>)((x, y) => Color.clear) : Stripe,
                width * nativeScale, height * nativeScale);
            var donor = sampled ? Drawing(doc, Stripe, width, height) : null;
            RenderTexture sample = null;
            if (sampled)
            {
                sample = S.Temporary(RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
                Graphics.Blit(Pixels(donor), sample);
            }
            var start = new Vector2(64f / width, (64 + phase) / height);
            var finish = start + new Vector2(82f / width, 0);
            Begin(doc, layer, start, 32, sample);
            Segment(doc, layer, start, finish, .8f, soft ? .3f : 1, soft ? .55f : 1);
            var source = sampled ? (RenderTexture)Get(layer, "smudgeSample") : (RenderTexture)Get(layer, "paintSurface");
            var read = S.Own(new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true));
            var active = RenderTexture.active;
            try { RenderTexture.active = source; read.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); }
            finally { RenderTexture.active = active; }
            double contrast = 0;
            int scale = sampled ? 1 : nativeScale;
            for (int y = 56 * scale; y < 72 * scale; y++)
            for (int x = 136 * scale; x < 144 * scale; x++)
                contrast += read.GetPixel(x, y).r * (y % 4 < 2 ? 1 : -1);
            contrast /= 64 * scale * scale;
            string label = "sample=" + sampled + " native=" + nativeScale + " phase=" + phase + " soft=" + soft;
            T.Near(1, contrast, .01, "No cumulative transverse blur: " + label);
            reports.Add(label + " contrast=" + contrast.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
            End(layer);
            if (sample != null) S.Release(sample);
        }
        foreach (bool perspective in new[] { false, true })
        {
            var doc = Document(256, 128);
            var layer = Drawing(doc, (x, y) => y % 4 < 2 ? new Color(3, -.25f, .2f, .5f) : new Color(0, -.25f, .2f, .5f), 1024, 512);
            var transform = layer.Owner.transform;
            if (perspective)
                T.True(transform.TrySetMatrix(new ProjectiveMatrix { m00 = 1.2, m11 = 1, m20 = 1, m22 = 1 }), "Set owned projective transform");
            else { transform.rotation = 27; transform.scale = new Double2(.8, 1.1); }
            layer.Owner.transform = transform;
            var from = new Vector2(.25f, .503f); var to = new Vector2(.65f, .503f);
            var original = Pixels(layer).GetPixels();
            Begin(doc, layer, from, 20);
            int beforeWidth = ((RenderTexture)Get(layer, "smudgeCarry")).width;
            Segment(doc, layer, from, to, .8f, .65f, .6f);
            int afterWidth = ((RenderTexture)Get(layer, "smudgeCarry")).width;
            T.True(!perspective || afterWidth > beforeWidth, "Projective carry grows along the path");
            End(layer);
            double error = 0;
            var actual = Pixels(layer).GetPixels();
            for (int y = 240; y < 272; y++) for (int x = 512; x < 640; x++)
                error += Difference(new[] { original[y * 1024 + x] }, new[] { actual[y * 1024 + x] });
            error /= 32 * 128;
            T.Near(0, error, .01, "Native transverse HDR/alpha detail survives " + (perspective ? "perspective growth" : "rotation"));
            reports.Add("perspective=" + perspective + " error=" + error.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
        }
        var wideDoc = Document(2048, 128);
        var wide = Drawing(wideDoc, (x, y) => y % 4 < 2 ? Color.white : Color.black, 2048, 128);
        var wideStart = new Vector2(.35f, .503f);
        Begin(wideDoc, wide, wideStart, 1120);
        T.True(((RenderTexture)Get(wide, "smudgeCarry")).width > 1024, "Large tips are not downsampled to the old 1024 limit");
        Segment(wideDoc, wide, wideStart, new Vector2(.65f, .503f), .8f, 1, 1); End(wide);
        T.Near(1, Pixels(wide).GetPixel(1250, 64).r - Pixels(wide).GetPixel(1250, 66).r, .01, "Large tip preserves transverse stripes");

        Color[][] masks = new Color[2][];
        for (int variant = 0; variant < 2; variant++)
        {
            var doc = Document(); var layer = Drawing(doc, (x, y) => Color.clear);
            var donor = Drawing(doc, (x, y) => Color.red);
            var sample = S.Temporary(RenderTexture.GetTemporary(128, 64, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
            Graphics.Blit(Pixels(donor), sample);
            var start = new Vector2(.5f, (32.2f + variant * .6f) / 64);
            Begin(doc, layer, start, 10, sample);
            Segment(doc, layer, start, start + new Vector2(1.01f / 128, 0), 1, 1, .5f); End(layer);
            masks[variant] = Pixels(layer).GetPixels(); S.Release(sample);
        }
        T.True(Difference(masks[0], masks[1]) > .1, "Fractional positions change the soft mask even within one color-grid pixel");
        return string.Join("; ", reports);
    });

    // Independent pixel-space reference: refresh carried pixels from the painted
    // result, with explicit retention. No production shader or spacing helper is reused.
    static void VerifyPaintedPatchFeedback(System.Collections.Generic.List<string> reports)
    {
        const int width = 128, height = 64, size = 20;
        foreach (bool sampled in new[] { false, true })
        foreach (bool curved in new[] { false, true })
        foreach (float strength in new[] { 1f, .65f })
        foreach (float flow in new[] { 1f, .45f })
        {
            Color Input(int x, int y) => x < 46
                ? new Color(3, -.25f, .2f + .1f * Mathf.Sin(y * .8f), .65f)
                : new Color(.1f, .4f, 2, .3f);
            var doc = Document(width, height);
            var layer = Drawing(doc, sampled ? (Func<int, int, Color>)((x, y) => Color.clear) : Input);
            var donor = sampled ? Drawing(doc, Input) : layer;
            RenderTexture sample = null;
            if (sampled)
            {
                sample = S.Temporary(RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
                Graphics.Blit(Pixels(donor), sample);
            }
            var from = new Vector2(40.25f / width, 32.25f / height);
            var path = curved ? new[] { new Vector2(40.25f, 32.25f), new Vector2(64.25f, 42.25f),
                new Vector2(44.25f, 24.25f), new Vector2(65.25f, 32.25f) } :
                new[] { new Vector2(40.25f, 32.25f), new Vector2(64.25f, 32.25f) };
            Begin(doc, layer, from, size, sample);
            var initialPixels = ReadSurface((RenderTexture)Get(layer, sampled ? "smudgeSample" : "paintSurface")).GetPixels();
            var initialCarry = ReadSurface((RenderTexture)Get(layer, "smudgeCarry")).GetPixels();
            Vector2 firstPoint = Vector2.LerpUnclamped(path[0], path[1], 1.01f / Vector2.Distance(path[0], path[1]));
            Segment(doc, layer, from, new Vector2(firstPoint.x / width, firstPoint.y / height), strength, flow, .4f);
            T.True((bool)typeof(DrawingLayerBehaviour).GetProperty("SmudgeStrokeChanged", Flags).GetValue(layer), "One reference dab was deposited");
            var firstSurface = (RenderTexture)Get(layer, sampled ? "smudgeSample" : "paintSurface");
            var firstPainted = ReadSurface(firstSurface).GetPixels();
            var firstCarry = (RenderTexture)Get(layer, "smudgeCarry");
            var firstCarried = ReadSurface(firstCarry).GetPixels();
            Vector2 actualCenter = (Vector2)Get(layer, "smudgeLastCenter");
            int firstLeft = Mathf.FloorToInt(actualCenter.x * width) - firstCarry.width / 2;
            int firstBottom = Mathf.FloorToInt(actualCenter.y * height) - firstCarry.height / 2;
            double firstError = 0;
            for (int y = 0; y < firstCarry.height; y++) for (int x = 0; x < firstCarry.width; x++)
            {
                int i = y * firstCarry.width + x;
                Color predicted = Color.LerpUnclamped(firstPainted[(firstBottom + y) * width + firstLeft + x], initialCarry[i], strength);
                firstError += Difference(new[] { predicted }, new[] { firstCarried[i] });
            }
            T.Near(0, firstError / firstCarried.Length, .002, "Pickup uses actual post-deposit pixels and the requested retention (one half-float write)");
            var centers = new System.Collections.Generic.List<Vector2>();
            double nextDistance = 1;
            for (int segment = 1; segment < path.Length; segment++)
            {
                Vector2 runtimeFrom = segment == 1 ? firstPoint : path[segment - 1];
                Segment(doc, layer, new Vector2(runtimeFrom.x / width, runtimeFrom.y / height),
                    new Vector2(path[segment].x / width, path[segment].y / height), strength, flow, .4f);
                double distance = Vector2.Distance(path[segment - 1], path[segment]);
                while (nextDistance <= distance + 1e-7)
                {
                    centers.Add(Vector2.LerpUnclamped(path[segment - 1], path[segment], (float)(nextDistance / distance)));
                    nextDistance += 1;
                }
                nextDistance -= distance;
            }
            // Model both legal half-float write roundings without choosing an OS/GPU.
            // This isolates transport correctness from accumulated format quantization.
            var surface = (RenderTexture)Get(layer, sampled ? "smudgeSample" : "paintSurface");
            var actual = ReadSurface(surface).GetPixels();
            double error = double.PositiveInfinity;
            foreach (bool truncate in new[] { false, true })
            {
            var expected = (Color[])initialPixels.Clone();
            var expectedCarry = (Color[])initialPixels.Clone();
            Vector2 previousCenter = path[0];
            foreach (Vector2 center in centers)
            {
                var before = (Color[])expected.Clone();
                var beforeCarry = (Color[])expectedCarry.Clone();
                int dx = Mathf.FloorToInt(center.x) - Mathf.FloorToInt(previousCenter.x);
                int dy = Mathf.FloorToInt(center.y) - Mathf.FloorToInt(previousCenter.y);
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x + .5f, y + .5f), center) / (size * .5f);
                    float t = Mathf.Clamp01((radius - .4f) / .6f);
                    float coverage = (1 - t * t * (3 - 2 * t)) * strength * flow;
                    int sx = x - dx, sy = y - dy;
                    Color source;
                    if (strength == 1)
                    {
                        float px = x - (center.x - path[0].x), py = y - (center.y - path[0].y);
                        int ix = Mathf.FloorToInt(px), iy = Mathf.FloorToInt(py);
                        Color Read(int xx, int yy) => xx < 0 || xx >= width || yy < 0 || yy >= height ? Color.clear : initialPixels[yy * width + xx];
                        source = Color.LerpUnclamped(Color.LerpUnclamped(Read(ix, iy), Read(ix + 1, iy), px - ix),
                            Color.LerpUnclamped(Read(ix, iy + 1), Read(ix + 1, iy + 1), px - ix), py - iy);
                    }
                    else source = sx < 0 || sx >= width || sy < 0 || sy >= height ? Color.clear : beforeCarry[sy * width + sx];
                    expected[y * width + x] = ReferenceHalf(Color.LerpUnclamped(before[y * width + x], source, coverage), truncate);
                    expectedCarry[y * width + x] = ReferenceHalf(Color.LerpUnclamped(expected[y * width + x], source, strength), truncate);
                }
                previousCenter = center;
            }
            error = Math.Min(error, Difference(expected, actual) / expected.Length);
            }
            T.Near(0, error, .002, "Post-deposit feedback matches independent RGBA transport: sample=" + sampled + " curved=" + curved + " strength=" + strength + " flow=" + flow);
            reports.Add("feedback=" + sampled + "/" + curved + "/" + strength + "/" + flow + " error=" + error.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
            var carry = (RenderTexture)Get(layer, "smudgeCarry");
            var carried = ReadSurface(carry).GetPixels();
            if (strength == 1)
                T.Near(0, Difference(initialCarry, carried), 0, "Strength 100 keeps initial HDR/RGBA patch exact on soft/low-Flow strokes and turns");
            End(layer);
            if (sample != null) S.Release(sample);
        }

        var hardDoc = Document(); var hard = Drawing(hardDoc, (x, y) => Color.clear);
        var opaque = Drawing(hardDoc, (x, y) => Color.red);
        var hardSample = S.Temporary(RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
        Graphics.Blit(Pixels(opaque), hardSample);
        var hardStart = new Vector2(40.25f / width, 32.25f / height);
        Begin(hardDoc, hard, hardStart, 20, hardSample);
        Segment(hardDoc, hard, hardStart, hardStart + new Vector2(1f / width, 0), 1, 1, 1);
        var hardPixels = ReadSurface((RenderTexture)Get(hard, "paintSurface")).GetPixels();
        int partial = 0;
        foreach (Color pixel in hardPixels) if (pixel.a > .01f && pixel.a < .99f) partial++;
        T.True(partial > 8, "Fully hard round tip has antialiased boundary pixels");
        End(hard); S.Release(hardSample);
    }

    static Color ReferenceHalf(Color value, bool truncate)
    {
        float Round(float component)
        {
            ushort bits = Mathf.FloatToHalf(component);
            if (truncate && Mathf.Abs(Mathf.HalfToFloat(bits)) > Mathf.Abs(component)) bits--;
            return Mathf.HalfToFloat(bits);
        }
        return new Color(Round(value.r), Round(value.g), Round(value.b), Round(value.a));
    }

    static Texture2D ReadSurface(RenderTexture surface)
    {
        var image = S.Own(new Texture2D(surface.width, surface.height, TextureFormat.RGBAFloat, false, true));
        var active = RenderTexture.active;
        try { RenderTexture.active = surface; image.ReadPixels(new Rect(0, 0, surface.width, surface.height), 0, 0); image.Apply(); }
        finally { RenderTexture.active = active; }
        return image;
    }

    public static string Run() => FixtureContext.Run("Smudge pixels, spacing, masks, HDR, transforms and sampling", () =>
    {
        var doc = Document(); var layer = Drawing(doc, Split);
        var original = Pixels(layer).GetPixels();
        var previous = RenderTexture.active;
        Begin(doc, layer, Start); End(layer);
        T.Near(0, Difference(original, Pixels(layer).GetPixels()), .001, "A click without movement leaves pixels unchanged");
        foreach (var settings in new[] { new Vector2(0, 1), new Vector2(1, 0) })
        {
            Begin(doc, layer, Start); Segment(doc, layer, Start, Finish, settings.x, settings.y); End(layer);
            T.Near(0, Difference(original, Pixels(layer).GetPixels()), .001, "Zero Strength/Flow does not paint");
        }
        Begin(doc, layer, Start); Segment(doc, layer, Start, Finish); End(layer);
        var single = Pixels(layer).GetPixels();
        T.True(Difference(original, single) > 10, "Dragging changes actual Drawing pixels");
        T.True(Pixels(layer).GetPixel(62, 32).r > .4f, "Color is carried in the direction of movement");
        T.True(Pixels(layer).GetPixel(56, 32).r > 1 && Pixels(layer).GetPixel(42, 32).b < 0, "HDR and negative RGB survive");
        T.Near(0, Difference(new[] { original[0], original[32 * 128 + 110] }, new[] { single[0], single[32 * 128 + 110] }), .001, "Pixels outside the brush remain exact");
        T.True(RenderTexture.active == previous, "Smudge restores caller render target");

        var segmented = Drawing(doc, Split);
        Begin(doc, segmented, Start);
        Vector2 last = Start;
        for (int i = 1; i <= 44; i++) { Vector2 next = Vector2.Lerp(Start, Finish, i / 44f); Segment(doc, segmented, last, next); last = next; }
        End(segmented);
        T.True(Difference(single, Pixels(segmented).GetPixels()) / single.Length < .0003, "The same path is independent of pointer-event segmentation");

        var turns = new[] { Start, new Vector2(84f / 128, 24f / 64), new Vector2(60f / 128, 42f / 64) };
        Color[][] turnResults = new Color[2][];
        for (int density = 0; density < 2; density++)
        {
            var turning = Drawing(doc, Split); Begin(doc, turning, turns[0]);
            for (int segment = 1; segment < turns.Length; segment++)
            {
                int pieces = density == 0 ? 1 : 40;
                Vector2 previousPoint = turns[segment - 1];
                for (int piece = 1; piece <= pieces; piece++)
                {
                    Vector2 next = Vector2.Lerp(turns[segment - 1], turns[segment], piece / (float)pieces);
                    Segment(doc, turning, previousPoint, next, 1, .65f, .4f); previousPoint = next;
                }
            }
            End(turning); turnResults[density] = Pixels(turning).GetPixels();
        }
        T.Near(0, Difference(turnResults[0], turnResults[1]) / single.Length, .0003,
            "Soft diagonal stroke and direction reversal do not depend on pointer-event density");

        var gentle = Drawing(doc, Split);
        Begin(doc, gentle, Start); Segment(doc, gentle, Start, Finish, .95f, .1f); End(gentle);
        T.True(Difference(original, Pixels(gentle).GetPixels()) < Difference(original, single), "Lower Flow deposits less color");
        var shortCarry = Drawing(doc, Split);
        Begin(doc, shortCarry, Start); Segment(doc, shortCarry, Start, Finish, .2f); End(shortCarry);
        T.True(Pixels(shortCarry).GetPixel(70, 32).r < Pixels(layer).GetPixel(70, 32).r, "Lower Strength picks up the destination sooner");

        var selection = S.Own(new Texture2D(128, 64, TextureFormat.RGBA32, false, true));
        var mask = new Color[128 * 64];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 128; x++) mask[y * 128 + x] = x < 60 ? Color.white : Color.black;
        selection.SetPixels(mask); selection.Apply(); selection.filterMode = FilterMode.Point;
        var masked = Drawing(doc, Split);
        Begin(doc, masked, Start); Segment(doc, masked, Start, Finish, .95f, 1, .8f, selection); End(masked);
        T.True(Pixels(masked).GetPixel(54, 32).r > .2f, "Selection permits a deposit inside");
        T.Near(original[32 * 128 + 72].r, Pixels(masked).GetPixel(72, 32).r, .001, "Selection blocks deposits outside");

        var alphaDoc = Document();
        var alpha = Drawing(alphaDoc, (x, y) => x < 48 ? new Color(4, 0, 0, .5f) : new Color(0, 9, 0, 0));
        Begin(alphaDoc, alpha, Start); Segment(alphaDoc, alpha, Start, Finish, 1); End(alpha);
        var carried = Pixels(alpha).GetPixel(72, 32);
        T.True(carried.a > .1f && carried.a <= .501f && carried.r > 3.9f && Math.Abs(carried.g) < .01,
            "Transparent RGB does not produce colored fringes; carried alpha and HDR stay intact");

        var nativeDoc = Document(128, 64);
        var native = Drawing(nativeDoc, (x, y) => Split(x / 2, y / 2), 256, 128);
        var transform = native.Owner.transform; transform.position = new Vector2(8, 0); native.Owner.transform = transform;
        Begin(nativeDoc, native, Start); Segment(nativeDoc, native, Start, Finish); End(native);
        T.True(Pixels(native).width == 256 && Pixels(native).height == 128, "Native Drawing resolution is preserved");
        T.True(Pixels(native).GetPixel(120, 64).r > .2f, "Translated native-resolution Drawing can be smudged");

        var tiledDoc = Document(); var tiled = Drawing(tiledDoc, (x, y) => x > 112 ? Color.red : Color.blue);
        var edge = new Vector2(.96f, .5f); Begin(tiledDoc, tiled, edge, 16, tiled: true);
        Segment(tiledDoc, tiled, edge, new Vector2(1.12f, .5f), 1); End(tiled);
        T.True(Pixels(tiled).GetPixel(7, 32).r > .2f, "Tiled stroke carries color across the canvas edge");
        var wideDoc = Document(128, 32); var wide = Drawing(wideDoc, (x, y) => x < 48 ? Color.red : Color.blue, 128, 32);
        Begin(wideDoc, wide, Start, 96, tiled: true); Segment(wideDoc, wide, Start, Finish, 1, .3f); End(wide);
        T.True(Pixels(wide).GetPixel(60, 0).r > .01f && Pixels(wide).GetPixel(60, 31).r > .01f,
            "A large Tiled tip covers the short canvas axis without missing its seam");
        var smallDoc = Document(32, 32); var largeTip = Drawing(smallDoc, (x, y) => Color.red, 32, 32);
        Begin(smallDoc, largeTip, new Vector2(.25f, .5f), 64);
        T.Near(64, (float)Get(largeTip, "smudgeDiameter"), 0, "Tip diameter matches the control even when larger than the canvas");
        Segment(smallDoc, largeTip, new Vector2(.25f, .5f), new Vector2(.9f, .5f), 1, 1, 1); End(largeTip);
        T.True(Pixels(largeTip).GetPixel(1, 31).a < .1f, "Large round tip includes pixels beyond the small-canvas-sized circle");

        foreach (string mode in new[] { "RenderLayerAndBelow", "RenderCanvasAtSize" })
        {
            var sampleDoc = Document(); var donor = Drawing(sampleDoc, Split); var empty = Drawing(sampleDoc, (x, y) => Color.clear);
            sampleDoc.layers.Remove(empty.Owner); sampleDoc.layers.Insert(0, empty.Owner);
            sampleDoc.layers.Insert(0, new ColorFillLayerBehaviour { color = Color.green });
            var donorBefore = Pixels(donor).GetPixels();
            var sample = S.Temporary((RenderTexture)(mode == "RenderLayerAndBelow"
                ? Call(sampleDoc, mode, empty.Owner, 128, 64) : Call(sampleDoc, mode, 128, 64)));
            Begin(sampleDoc, empty, Start, 20, sample); Segment(sampleDoc, empty, Start, Finish, 1); End(empty);
            var output = Pixels(empty).GetPixel(68, 32);
            T.True(mode == "RenderLayerAndBelow" ? output.r > 1 : output.g > .9f && output.r < .01f,
                "Below ignores upper layers; All includes them, writing only the empty Drawing: " + mode);
            T.Near(0, Difference(donorBefore, Pixels(donor).GetPixels()), 0, "Sampling never edits donor layers");
            S.Release(sample);
        }
        var composite = S.Own(doc.ComposeCanvas());
        T.True(composite != null && composite.width == 128, "Smudged layers compose normally");
        var thumbnail = layer.GetPreviewTexture(32);
        T.True(thumbnail == Pixels(layer), "Drawing thumbnail sees the edited pixels");
        var groupDoc = Document(); var grouped = Drawing(groupDoc, Split);
        Layer group = new GroupLayerBehaviour(); group.children.Add(grouped);
        groupDoc.layers.Clear(); groupDoc.layers.Add(group);
        var groupTransform = group.transform; groupTransform.rotation = 12; groupTransform.scale = new Vector2(.85f, .85f); group.transform = groupTransform;
        Call(groupDoc, "NormalizeModel");
        Color[] Compose(TextureCompositor model) { var image = S.Own(model.ComposeCanvas()); try { return image.GetPixels(); } finally { S.Destroy(image); } }
        var groupBefore = Compose(groupDoc);
        Begin(groupDoc, grouped, Start); Segment(groupDoc, grouped, Start, Finish); End(grouped);
        var groupAfter = Compose(groupDoc);
        T.True(Difference(groupBefore, groupAfter) > 1, "Smudge follows rotated/scaled parent transforms and group composition");
        var target = new BlurLayerBehaviour { radius = 0, colorRange = LayerColorRange.HDR, inputMode = EffectInputMode.Specific, TargetLayerId = grouped.Id };
        group.children.Insert(0, target); Call(groupDoc, "NormalizeModel");
        var targetResult = S.Temporary((RenderTexture)Call(groupDoc, "RenderLayerPreview", target.Owner, 128));
        var targetPixels = S.Own(new Texture2D(targetResult.width, targetResult.height, TextureFormat.RGBAFloat, false, true));
        RenderTexture active = RenderTexture.active;
        try { RenderTexture.active = targetResult; targetPixels.ReadPixels(new Rect(0, 0, targetResult.width, targetResult.height), 0, 0); }
        finally { RenderTexture.active = active; }
        T.True(Difference(groupBefore, targetPixels.GetPixels()) > 1, "Target input sees the smudged Drawing");
        T.True(Difference(groupAfter, targetPixels.GetPixels()) / groupAfter.Length < .001, "Target input matches the smudged group render");
        S.Release(targetResult); group.children.Remove(target.Owner);
        grouped.clippingMask = true;
        Layer clipBase = new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Ellipse };
        group.children.Add(clipBase); Call(groupDoc, "NormalizeModel");
        var clipped = Compose(groupDoc);
        T.True(clipped[0].a < .01f && clipped[32 * 128 + 64].a > .9f, "Clipping still masks the smudged Drawing");
        var copy = S.Own((TextureCompositor)Call(typeof(WhimTexDocumentFile), "CreateEditableCopy", alphaDoc));
        var restored = (DrawingLayerBehaviour)copy.layers[0].Behaviour;
        T.Near(carried.r, Pixels(restored).GetPixel(72, 32).r, .005, "Smudged HDR pixels survive document roundtrip");
        var exportImage = S.Own(alphaDoc.ComposeCanvas());
        var encode = typeof(TextureCompositorWindow).GetMethod("EncodeExportTexture", Flags);
        object Format(string name) => Enum.Parse(encode.GetParameters()[1].ParameterType, name);
        var png = (byte[])encode.Invoke(null, new[] { (object)exportImage, Format("Png") });
        var decoded = S.Own(new Texture2D(1, 1, TextureFormat.RGBA32, false));
        T.True(decoded.LoadImage(png), "Actual smudged composition exports as readable PNG");
        T.Near(carried.a, decoded.GetPixel(72, 32).a, .01, "PNG export preserves carried alpha");
        var exr = (byte[])encode.Invoke(null, new[] { (object)exportImage, Format("Exr") });
        T.True(exr.Length > 100 && BitConverter.ToUInt32(exr, 0) == 20000630, "Actual smudged HDR composition exports as EXR");
    });

    static TextureCompositorWindow Window(out TextureCompositor doc)
    {
        var window = S.Own(ScriptableObject.CreateInstance<TextureCompositorWindow>());
        doc = S.Own((TextureCompositor)Get(window, "compositor")); doc.width = 128; doc.height = 64;
        return window;
    }

    [Serializable] public class Reply { public bool success; public string error; }
    static string Q(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    public static string Api() => FixtureContext.Run("Smudge API validation and Undo/Redo", () =>
    {
        var window = Window(out var doc); var layer = Drawing(doc, Split);
        string session = (string)typeof(TextureCompositorWindow).GetProperty("AgentSessionId", Flags).GetValue(window);
        string Operation(string extra = "") => "{\"op\":\"smudgeStroke\",\"layer\":" + Q(layer.Id) + ",\"points\":[[40,32],[84,32]],\"size\":20,\"strength\":0.95" + extra + "}";
        Reply Batch(string op, bool dry = false)
        {
            string inspection = WhimTexApi.LiveJson("{\"apiVersion\":1,\"op\":\"inspect\",\"sessionId\":" + Q(session) + "}");
            var inspected = JsonUtility.FromJson<Reply>(inspection);
            T.True(inspected.success, "Inspect owned session");
            var jsonType = typeof(WhimTexApi).GetMethod("PaintRepair", Flags).GetParameters()[2].ParameterType;
            object token = jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { inspection });
            foreach (string key in new[] { "document", "revision" })
                token = token.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(token, new object[] { key });
            return JsonUtility.FromJson<Reply>(WhimTexApi.AssistantExecuteJson("{\"apiVersion\":1,\"sessionId\":" + Q(session) +
                ",\"expectedRevision\":" + Q(token.ToString()) + ",\"dryRun\":" + (dry ? "true" : "false") + ",\"operations\":[" + op + "]}"));
        }
        var before = Pixels(layer).GetPixels();
        T.True(Batch(Operation(), true).success, "Smudge dry run is accepted");
        T.Near(0, Difference(before, Pixels(layer).GetPixels()), 0, "Dry run does not paint");
        T.True(!Batch(Operation(",\"flow\":2")).success, "Invalid Flow is rejected");
        var longPath = new string[500];
        for (int i = 0; i < longPath.Length; i++) longPath[i] = i % 2 == 0 ? "[-128,0]" : "[256,128]";
        var oversized = Batch("{\"op\":\"smudgeStroke\",\"layer\":" + Q(layer.Id) + ",\"points\":[" + string.Join(",", longPath) + "],\"size\":512}", true);
        T.True(!oversized.success && oversized.error.Contains("budget"), "Oversized tip work is rejected during dry run");
        var originalTransform = layer.Owner.transform;
        try
        {
            var tiny = originalTransform; tiny.scale = new Double2(.001, .001); layer.Owner.transform = tiny;
            var nativeBudget = Batch(Operation(), true);
            T.True(!nativeBudget.success && nativeBudget.error.Contains("budget"), "Dry run rejects excessive native footprint before allocation");
        }
        finally { layer.Owner.transform = originalTransform; }
        T.Near(0, Difference(before, Pixels(layer).GetPixels()), 0, "Invalid operation preserves pixels");
        var result = Batch(Operation()); T.True(result.success, "Smudge API succeeds: " + result.error);
        DrawingLayerBehaviour Current() => (DrawingLayerBehaviour)((Layer)Call(doc, "FindLayer", layer.Id)).Behaviour;
        var after = Pixels(Current()).GetPixels();
        T.True(Difference(before, after) > 10, "Smudge API actually moves pixels");
        Undo.PerformUndo();
        T.True(Difference(before, Pixels(Current()).GetPixels()) < .01, "One Undo restores the stroke");
        Undo.PerformRedo();
        T.True(Difference(after, Pixels(Current()).GetPixels()) < .01, "Redo restores the stroke");
    });

    public static string StartUi(string id)
    {
        var job = AsyncFixture.Create(id);
        job.Worker = ExecuteUi(job, id);
        return job.Read();
    }

    sealed class IconCapture : ImmediateModeElement
    {
        internal Button button;
        internal EditorWindow owner;
        internal TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
        bool done;
        protected override void ImmediateRepaint()
        {
            if (done) return;
            done = true;
            Texture2D image = null;
            try
            {
                float scale = EditorGUIUtility.pixelsPerPoint;
                int height = RenderTexture.active != null ? RenderTexture.active.height : Mathf.RoundToInt(owner.position.height * scale);
                Rect rect = button.worldBound;
                int w = Mathf.RoundToInt(rect.width * scale), h = Mathf.RoundToInt(rect.height * scale);
                image = new Texture2D(w, h, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(Mathf.RoundToInt(rect.x * scale), height - Mathf.RoundToInt(rect.yMax * scale), w, h), 0, 0);
                image.Apply();
                completion.TrySetResult(Convert.ToBase64String(image.EncodeToPNG()));
            }
            catch (Exception error) { completion.TrySetException(error); }
            finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
        }
    }

    static async Task ExecuteUi(AsyncFixture job, string id)
    {
        string icon = null;
        await FixtureContext.RunAsync(job, async cancellation =>
        {
            var window = Window(out var doc); var layer = Drawing(doc, Split);
            window.ShowUtility(); window.position = new Rect(100, 100, 1100, 700);
            await Task.Delay(250, cancellation);
            Call(window, "SelectOnlyLayer", layer.Id);
            var button = window.rootVisualElement.Q<Button>("smudgeBrushTool");
            T.True(button != null && button.childCount == 1, "Smudge button has its own finger icon");
            PublicInput.Click(button); Call(window, "UpdateToolkitCanvasPresentation");
            await Task.Delay(100, cancellation);
            var row = window.rootVisualElement.Q("smudgeBrushSettings");
            T.True(row != null && !row.ClassListContains("whimtex-tool-options--hidden"), "Smudge settings row is active");
            var settings = Get(window, "paintSettings"); var size = row.Q<FloatField>("smudgeSize");
            float blurBefore = (float)Get(settings, "blurSize");
            size.value = 24;
            T.Near(24, (float)Get(settings, "smudgeSize"), 0, "Size field edits Smudge size");
            T.Near(blurBefore, (float)Get(settings, "blurSize"), 0, "Smudge settings do not overwrite Blur settings");
            T.True(size.Q(className: "unity-base-field__input").worldBound.width > 30, "Compact Size field has usable input width");
            T.True(row.Q<Slider>("smudgeHardness") != null && row.Q<Toggle>("smudgePressure") != null && row.Q<EnumField>("smudgeSampleMode") != null,
                "Hardness, Pressure and source controls exist");
            T.True(Call(typeof(TextureCompositorWindow), "ParseCanvasTool", "SmudgeBrush").ToString() == "SmudgeBrush", "Smudge is a persistent base tool");
            var capture = new IconCapture { button = button, owner = window };
            capture.style.height = 1; window.rootVisualElement.Add(capture); window.Repaint();
            try
            {
                if (await Task.WhenAny(capture.completion.Task, Task.Delay(3000, cancellation)) != capture.completion.Task)
                    throw new TimeoutException("Owned icon capture did not repaint.");
                icon = await capture.completion.Task;
                T.True(!string.IsNullOrEmpty(icon), "Actual tool icon captured with public UI APIs");
            }
            finally { capture.RemoveFromHierarchy(); }
            var canvas = (VisualElement)Get(window, "toolkitCanvas");
            var imageRect = (Rect)canvas.GetType().GetProperty("ImageRect").GetValue(canvas);
            Vector2 View(Vector2 uv) => (Vector2)Call(canvas, "ToView", new Vector2(imageRect.x + uv.x * imageRect.width, imageRect.y + (1 - uv.y) * imageRect.height));
            var original = Pixels(layer).GetPixels();
            Set(window, "temporaryDocumentDirty", false);
            T.True((bool)Call(window, "TryBeginCanvasStroke", View(Start), false), "Canvas begins a Smudge stroke");
            Call(window, "FinishPaintingStroke");
            T.True(Difference(original, Pixels(layer).GetPixels()) < .001, "Canvas click without dragging does not paint");
            T.True(!(bool)Get(window, "temporaryDocumentDirty"), "A click does not mark the document dirty");
            T.True((bool)Call(window, "TryBeginCanvasStroke", View(Start), false), "Canvas starts another Smudge stroke");
            Call(window, "PaintTowardsLayerPoint", Finish); Call(window, "FinishPaintingStroke");
            T.True(Difference(original, Pixels(layer).GetPixels()) > 10, "Canvas movement uses Smudge, not the ordinary Brush");
            double pressureDifference = Difference(original, Pixels(layer).GetPixels());
            var light = Drawing(doc, Split); Call(window, "SelectOnlyLayer", light.Id);
            Set(settings, "smudgePressure", true); Set(window, "paintingPressure", .2f);
            T.True((bool)Call(window, "TryBeginCanvasStroke", View(Start), false), "Begin low-pressure stroke");
            Call(window, "PaintTowardsLayerPoint", Finish); Call(window, "FinishPaintingStroke");
            T.True(Difference(original, Pixels(light).GetPixels()) < pressureDifference, "Pressure reduces Flow, not color retention");
            S.CloseWindow(window);
        });
        string reply = job.Read();
        if (icon != null && JsonUtility.FromJson<WhimTex.Tests.TestResult>(reply).status == "passed")
            SessionState.SetString("WhimTex.Tests.UnityC." + id, reply.Substring(0, reply.Length - 1) +
                ",\"artifacts\":[{\"name\":\"smudge-icon.png\",\"encoding\":\"base64\",\"content\":\"" + icon + "\"}]}" );
    }
    public static string Poll(string id) => AsyncFixture.Poll(id);
    public static Task<string> Cancel(string id) => AsyncFixture.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncFixture.Cleanup(id);

    public static string Capture()
    {
        string before = null, after = null;
        string detailBefore = null, detailAfter = null;
        string reply = FixtureContext.Diagnostic("Smudge visual sample", () =>
        {
            var doc = Document(256, 128);
            var layer = Drawing(doc, (x, y) => x < 96 ? new Color(.8f, .15f, .05f, 1) : new Color(.03f, .15f, .7f, 1), 256, 128);
            before = Convert.ToBase64String(Pixels(layer).EncodeToPNG());
            var start = new Vector2(84f / 256, .5f); Begin(doc, layer, start, 40);
            Segment(doc, layer, start, new Vector2(185f / 256, .63f), .97f, .65f); End(layer);
            var image = S.Own(doc.ComposeCanvas());
            after = Convert.ToBase64String(image.EncodeToPNG());
            var detailDoc = Document(256, 128);
            var detail = Drawing(detailDoc, (x, y) => y % 4 < 2 ? (x < 96 ? Color.red : Color.blue) : Color.black, 256, 128);
            detailBefore = Convert.ToBase64String(Pixels(detail).EncodeToPNG());
            var detailStart = new Vector2(64f / 256, 64.35f / 128);
            Begin(detailDoc, detail, detailStart, 32);
            Segment(detailDoc, detail, detailStart, detailStart + new Vector2(82f / 256, 0), .8f, 1, 1); End(detail);
            detailAfter = Convert.ToBase64String(Pixels(detail).EncodeToPNG());
            return "Actual curved color-boundary and fractional striped strokes; visual inspection is separate from regression verdicts.";
        });
        // Ephemeral Unity serialization can omit nested artifact types. Preserve
        // the actual images explicitly, after all fixture cleanup has completed.
        var result = JsonUtility.FromJson<WhimTex.Tests.TestResult>(reply);
        string json = result.ToJson();
        return before == null || after == null ? json : json.Substring(0, json.Length - 1) +
            ",\"artifacts\":[{\"name\":\"before.png\",\"encoding\":\"base64\",\"content\":\"" + before +
            "\"},{\"name\":\"after.png\",\"encoding\":\"base64\",\"content\":\"" + after +
            "\"},{\"name\":\"detail-before.png\",\"encoding\":\"base64\",\"content\":\"" + detailBefore +
            "\"},{\"name\":\"detail-after.png\",\"encoding\":\"base64\",\"content\":\"" + detailAfter + "\"}]}";
    }

    // Opt-in timing, not a regression threshold. One-pixel readback drains queued GPU
    // work; setup and final storage synchronization are outside the stroke measurement.
    public static string Benchmark()
    {
        var measurements = new System.Collections.Generic.List<string>();
        return FixtureContext.Diagnostic("Smudge interactive timing", () =>
        {
            var drain = S.Own(new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true));
            void Flush(RenderTexture surface)
            {
                var active = RenderTexture.active;
                try { RenderTexture.active = surface; drain.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false); }
                finally { RenderTexture.active = active; }
            }
            foreach (int resolution in new[] { 2048, 4096 })
            foreach (int size in new[] { 32, 128, 512 })
            foreach (bool sampled in new[] { false, true })
            {
                var doc = Document(resolution, resolution);
                var layer = Drawing(doc, (x, y) => x < resolution * .375f
                    ? new Color(3, .1f + y / (float)resolution, -.2f, .7f)
                    : new Color(.1f, .2f, 2, .3f), resolution, resolution);
                RenderTexture sample = null;
                if (sampled)
                {
                    sample = S.Temporary(RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
                    Graphics.Blit(Pixels(layer), sample);
                }
                var start = new Vector2(.35f, .5f);
                Begin(doc, layer, start, size, sample);
                var surface = (RenderTexture)Get(layer, "paintSurface");
                Flush(surface);
                var clock = new System.Diagnostics.Stopwatch();
                const int events = 100;
                double queued = 0, completed = 0, canvas = 0;
                for (int trial = 0; trial < 4; trial++)
                {
                    Vector2 last = start;
                    clock.Restart();
                    for (int i = 1; i <= events; i++)
                    {
                        Vector2 next = start + new Vector2(i * 10f / resolution, .025f * Mathf.Sin(i * .08f));
                        Segment(doc, layer, last, next, .95f, .65f); last = next;
                    }
                    double submitted = clock.Elapsed.TotalMilliseconds;
                    Flush(surface);
                    double drained = clock.Elapsed.TotalMilliseconds;
                    clock.Restart();
                    var preview = S.Temporary((RenderTexture)Call(doc, "RenderCanvas", 512));
                    Flush(preview);
                    double rendered = clock.Elapsed.TotalMilliseconds;
                    S.Release(preview);
                    if (trial > 0) { queued += submitted / 3; completed += drained / 3; canvas += rendered / 3; }
                }
                measurements.Add(resolution + "px tip=" + size + " sample=" + sampled +
                    " events=" + events + " enqueueMs=" + queued.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                    " completedMs=" + completed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                    " canvasMs=" + canvas.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                Call(layer, "EndStroke");
                if (sample != null) S.Release(sample);
                S.Destroy(doc); S.Destroy(Pixels(layer));
            }
            return string.Join("; ", measurements);
        });
    }

    public static string Precision()
    {
        var artifacts = new System.Collections.Generic.List<string>();
        string result = FixtureContext.Diagnostic("Smudge precision snapshot", () =>
        {
            foreach (bool sampled in new[] { false, true })
            foreach (bool tiled in new[] { false, true })
            {
                var doc = Document();
                var layer = Drawing(doc, (x, y) => new Color(x / 31f - .7f, Mathf.Sin(y * .25f) * 2,
                    x < 48 ? 3 : .1f, (x + y) % 17 / 16f));
                RenderTexture sample = null;
                if (sampled)
                {
                    sample = S.Temporary(RenderTexture.GetTemporary(128, 64, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
                    Graphics.Blit(Pixels(layer), sample);
                }
                Begin(doc, layer, Start, 24, sample, tiled);
                Vector2 last = Start;
                for (int i = 1; i <= 20; i++)
                {
                    Vector2 next = Start + new Vector2(i * .042f, Mathf.Sin(i * .3f) * .22f);
                    Segment(doc, layer, last, next, .83f, .62f, .55f); last = next;
                }
                End(layer);
                artifacts.Add("{\"name\":\"pixels-" + sampled + "-" + tiled + ".rgba16f\",\"encoding\":\"base64\",\"content\":\"" +
                    Convert.ToBase64String(Pixels(layer).GetRawTextureData()) + "\"}");
                if (sample != null) S.Release(sample);
            }
            return "Actual native HDR output for a before/after comparison; not an automatically regenerated golden.";
        });
        var json = JsonUtility.FromJson<WhimTex.Tests.TestResult>(result).ToJson();
        return json.Substring(0, json.Length - 1) + ",\"artifacts\":[" + string.Join(",", artifacts) + "]}";
    }
}
