using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class MorphGroupTests
{
    public static string Run() => TestContext.Run("Morph Group timeline, pixels and persistence", Test);

    static void Test(TestContext check)
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = doc.height = 64;
        var morph = new MorphGroupLayerBehaviour();
        var group = new Layer(morph);
        doc.layers.Add(group);
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            check.Near(0, Frame(doc, 2)[32 * 64 + 32].a, .001, "Empty group");
            var a = Shape(-16, Color.red);
            var b = Shape(16, Color.blue);
            var c = Shape(0, Color.green);
            group.children.AddRange(new[] { a, b, c });
            typeof(WhimTexDocument).GetMethod("NormalizeModel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(doc, null);
            check.Equal(MorphInterpolation.Linear, morph.interpolation, "Default raster trajectory");
            check.True(!morph.classicLiao, "Liao uses current matching by default");
            check.True(!morph.preserveGeometry, "Local rigidity defaults off");
            check.True(!morph.preserveStationaryDetails, "Stationary detail preservation defaults off");
            var pair = morph.GetOrCreate(a.Id, b.Id);
            pair.duration = 2;
            pair.anchors.Add(new MorphAnchor { from = new Vector2(.25f,.5f), to = new Vector2(.75f,.5f) });
            foreach (var mode in new[] { MorphMode.Raster, MorphMode.Vector })
            {
                morph.mode = mode;
                AssertPixel(check, Frame(doc, 0), 16, Color.red, mode + " first");
                AssertPixel(check, Frame(doc, 2), 48, Color.blue, mode + " second");
                AssertPixel(check, Frame(doc, 10), 32, Color.green, mode + " holds last");
                var middle = Frame(doc, 1);
                var center = middle[32 * 64 + 32];
                check.True(center.a > .95f && center.r > .1f && center.b > .1f, mode + " transports both endpoints: " + center);
                check.True(middle[32 * 64 + 16].a < .1f && middle[32 * 64 + 48].a < .1f, mode + " avoids a double exposure");
                group.transform.positionF = new Vector2(4, 0);
                check.True(Frame(doc, 1)[32 * 64 + 41].a > .9f, mode + " group transform applies once");
                group.transform.Reset();
            }
            // A tiled key must use raster fallback in Vector mode.
            var originalTiling = c.transform.tiling;
            c.transform.tiling = TransformTilingMode.Repeat;
            morph.mode = MorphMode.Raster;
            var rasterFallback = Frame(doc, 2.5);
            morph.mode = MorphMode.Vector;
            check.True(rasterFallback.AsSpan().SequenceEqual(Frame(doc, 2.5)), "Unsupported vector pair uses the default Liao raster path");
            c.transform.tiling = originalTiling;
            morph.mode = MorphMode.Raster;
            pair.anchors.Clear();
            check.True(Frame(doc, 1)[32 * 64 + 32].a > .95f, "Automatic silhouette alignment");
            // Two controls agree on an upward translation. UV +Y must move toward the upper part of the canvas.
            a.transform.positionF = new Vector2(0, -12);
            b.transform.positionF = new Vector2(0, 20);
            pair.anchors.Add(new MorphAnchor { from = new Vector2(.4f, .3125f), to = new Vector2(.4f, .8125f) });
            pair.anchors.Add(new MorphAnchor { from = new Vector2(.6f, .3125f), to = new Vector2(.6f, .8125f) });
            var lifted = Frame(doc, 1);
            check.True(lifted[36 * 64 + 32].a > .95f, "Paired upward displacement meets at target midpoint");
            check.True(lifted[16 * 64 + 32].a < .05f && lifted[56 * 64 + 32].a < .05f, "Vertical endpoints vacated");
            a.transform.positionF = new Vector2(-16, 0);
            b.transform.positionF = new Vector2(16, 0);
            pair.anchors.Clear();
            pair.anchors.Add(new MorphAnchor { from = new Vector2(.25f,.5f), to = new Vector2(.75f,.5f) });
            morph.mode = MorphMode.Vector;
            b.enabled = false;
            AssertPixel(check, Frame(doc, 1), 32, Color.green, "Disabled key skipped");
            b.enabled = true;
            group.children.Reverse();
            AssertPixel(check, Frame(doc, 0), 32, Color.green, "Reordered keys");
            group.children.Reverse();
            doc.SetPreviewTime(2);
            using (var pixels = new Image(doc.ComposeCanvas()))
                AssertPixel(check, pixels.value.GetPixels(), 16, Color.red, "Default output time remains zero");

            pair.segments.Add(new MorphSegment { fromStart = Vector2.left, fromEnd = Vector2.up,
                toStart = Vector2.zero, toEnd = Vector2.right });
            pair.nodes.Add(new MorphGuideNode { from = Vector2.up, to = Vector2.right });
            pair.segments[0].endNode = 1;
            pair.segments.Add(new MorphSegment { startNode = 1, fromEnd = Vector2.right, toEnd = Vector2.down,
                bezier = true, fromOut = Vector2.left * .1f, toIn = Vector2.up * .2f, marks = new() { new Vector2(.3f,.6f) } });
            var legacy = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Compact }).Json;
            check.True(!legacy.Contains("\"boundary\"") && !legacy.Contains("\"radius\""), "Compact omits default guide influence");
            check.True(!legacy.Contains("\"preserveStationaryDetails\""), "Compact omits disabled stationary detail preservation");
            using (var restored = WhimTexDocumentJson.Read(legacy, false))
            {
                var restoredMorph = (MorphGroupLayerBehaviour)restored.Document.layers[0].Behaviour;
                check.True(!restoredMorph.preserveStationaryDetails, "Omitted stationary detail preservation reads as off");
                var legacyPair = restoredMorph.Find(a.Id, b.Id);
                check.Equal(0f, legacyPair.anchors[0].radius, "Omitted point radius selects Auto");
                foreach (var edge in legacyPair.segments)
                    check.True(edge.radius == 0 && !edge.boundary, "Omitted curve influence selects Auto with Boundary off");
                CheckStationaryPatch(check, restored.Document, a.Id, b.Id);
            }
            void RejectInvalidGuide(string expectedError)
            {
                try { WhimTexDocumentJson.Write(doc); }
                catch (WhimTexDocumentException ex) when (ex.Message.Contains(expectedError)) { return; }
                throw new Exception("Invalid guide was serialized: " + expectedError);
            }
            pair.anchors[0].radius = float.NaN;
            RejectInvalidGuide("Morph anchor radius");
            pair.anchors[0].radius = 0;
            pair.segments[1].radius = 1.01f;
            RejectInvalidGuide("Morph segment radius");
            pair.segments[1].radius = 0;
            pair.anchors[0].radius = 1e-20f;
            using (var tiny = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc,
                new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Compact }).Json, false))
            {
                var stored = (MorphGroupLayerBehaviour)tiny.Document.layers[0].Behaviour;
                check.Equal(1e-20f, stored.Find(a.Id, b.Id).anchors[0].radius, "Small explicit radius survives Compact");
            }
            CheckFlattenedInfluence(check, pair);
            pair.anchors[0].radius = .125f;
            void CheckStoredInfluence(MorphTransition stored, string format)
            {
                check.Equal(.125f, stored.anchors[0].radius, format + " point radius");
                check.True(stored.segments[0].radius == 1 && stored.segments[0].boundary, format + " straight guide settings");
                check.True(stored.segments[1].radius == .25f && stored.segments[1].boundary, format + " cubic guide settings");
            }
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                morph.interpolation = MorphInterpolation.AutomaticCurve;
                morph.classicLiao = true;
                morph.preserveGeometry = true;
                morph.preserveStationaryDetails = true;
                var json = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                using var restored = WhimTexDocumentJson.Read(json, false);
                var copy = (MorphGroupLayerBehaviour)restored.Document.layers[0].Behaviour;
                check.True(copy.classicLiao, "Saved classic Liao setting " + mode);
                check.True(copy.preserveGeometry, "Saved local rigidity " + mode);
                check.True(copy.preserveStationaryDetails, "Saved stationary detail preservation in Vector mode " + mode);
                check.Equal(MorphInterpolation.AutomaticCurve, copy.interpolation, "Saved raster trajectory " + mode);
                check.Equal(MorphMode.Vector, copy.mode, "Saved vector mode " + mode);
                var transition = copy.Find(a.Id,b.Id);
                CheckStoredInfluence(transition, mode.ToString());
                check.Equal(Vector2.left, transition.segments[0].fromStart, "Saved segment start " + mode);
                check.Equal(Vector2.right, transition.segments[0].toEnd, "Saved segment end " + mode);
                check.Equal(transition.segments[0].endNode, transition.segments[1].startNode, "Shared knot reference " + mode);
                check.Equal(Vector2.up, transition.nodes[0].from, "Shared knot position " + mode);
                check.Equal(new Vector2(.3f,.6f), transition.segments[1].marks[0], "Paired mark " + mode);
                check.Equal(Vector2.up * .2f, transition.segments[1].toIn, "Destination handle " + mode);
                check.Near(2, transition.duration, 0, "Saved time");
                check.Equal(pair.anchors[0].to, transition.anchors[0].to, "Saved paired UV");
                check.True(Frame(restored.Document, 1)[32 * 64 + 32].a > .95f, "Reopened frame " + mode);
            }
            morph.mode = MorphMode.Raster;
            var full = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }).Json;
            const string classicField = "\"classicLiao\":";
            check.True(full.Contains(classicField) && !full.Contains("\"rasterMethod\""), "New group omits the retired field");
            var expectedFrame = Frame(doc, 1);
            foreach (var oldMethod in new[] { "LocalContrast", "Liao" })
            {
                var oldDocument = full.Replace(classicField, "\"rasterMethod\":\"" + oldMethod + "\"," + classicField);
                using var restored = WhimTexDocumentJson.Read(oldDocument, false);
                var saved = (MorphGroupLayerBehaviour)restored.Document.layers[0].Behaviour;
                var savedPair = saved.Find(a.Id, b.Id);
                CheckStoredInfluence(savedPair, oldMethod);
                check.Equal(pair.anchors[0].from, savedPair.anchors[0].from, "Imported guide");
                check.Near(pair.duration, savedPair.duration, 0, "Imported duration");
                check.True(expectedFrame.AsSpan().SequenceEqual(Frame(restored.Document, 1)), "Imported group renders with Liao");
                check.True(!WhimTexDocumentJson.Write(restored.Document, new WhimTexJsonWriteOptions
                    { Mode = WhimTexJsonWriteMode.Full }).Json.Contains("\"rasterMethod\""), "Resaving drops the retired selector");
            }
            morph.mode = MorphMode.Vector;
            var clone = typeof(WhimTexDocumentFile).GetMethod("CreateEditableCopy", BindingFlags.Static | BindingFlags.NonPublic);
            var native = (WhimTexDocument)clone.Invoke(null, new object[] { doc });
            try
            {
                var settings = (MorphGroupLayerBehaviour)native.layers[0].Behaviour;
                check.True(settings.classicLiao, "Native container classic Liao setting");
                check.True(settings.preserveGeometry, "Native container local rigidity");
                check.True(settings.preserveStationaryDetails, "Native container stationary detail preservation");
                check.Equal(MorphInterpolation.AutomaticCurve, settings.interpolation, "Native container raster trajectory");
                check.Equal(pair.anchors[0].from, settings.Find(a.Id, b.Id).anchors[0].from, "Native container paired points");
                CheckStoredInfluence(settings.Find(a.Id, b.Id), "Native container");
                check.True(Frame(native, 1)[32 * 64 + 32].a > .95f, "Native container midpoint");
            }
            finally { UnityEngine.Object.DestroyImmediate(native); }
            // Each copied child gets a new ID. Its transitions must still find the corresponding keys.
            var duplicate = typeof(WhimTexDocument).GetMethod("DuplicateLayersForAgent", BindingFlags.Instance | BindingFlags.NonPublic);
            var copies = (Dictionary<Layer,Layer>)duplicate.Invoke(doc, new object[] { new List<Layer>{ group }, false });
            var copyGroup = (MorphGroupLayerBehaviour)copies[group].Behaviour;
            check.True(copyGroup.preserveStationaryDetails, "Duplicate retains stationary detail preservation");
            check.True(copyGroup.Find(copies[a].Id, copies[b].Id)?.anchors.Count == 1, "Duplicated points remapped");
            CheckStoredInfluence(copyGroup.Find(copies[a].Id, copies[b].Id), "Duplicated transition");
            check.True(copyGroup.Find(a.Id, b.Id) == null, "Duplicate independent from originals");
            CheckStationaryGuides(check);
            check.Equal(previous, RenderTexture.active, "Caller render target preserved");
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            UnityEngine.Object.DestroyImmediate(doc);
        }
    }

    static void CheckStationaryGuides(TestContext check)
    {
        const int size = 64;
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = doc.height = size;
        var stripes = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = x % 16 < 8 ? Color.black : Color.white;
            stripes.SetPixels(pixels);
            stripes.Apply();
            var morph = new MorphGroupLayerBehaviour();
            var group = new Layer(morph);
            var a = new Layer(new FileLayerBehaviour { sourceTexture = stripes });
            var b = new Layer(new FileLayerBehaviour { sourceTexture = stripes });
            group.children.AddRange(new[] { a, b });
            doc.layers.Add(group);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(doc, null);
            var pair = morph.GetOrCreate(a.Id, b.Id);
            Vector2 UV(int x, int y) => new Vector2((x + .5f) / size, (y + .5f) / size);
            // Both keys are identical. The linear midpoint lies on the opposite stripe.
            // Automatic Curve can displace the visible midpoint from this chord.
            pair.anchors.Add(new MorphAnchor { from = UV(12, 12), to = UV(28, 12) });
            pair.segments.Add(new MorphSegment { fromStart = UV(36, 36), fromEnd = UV(36, 52),
                toStart = UV(52, 36), toEnd = UV(52, 52) });
            foreach (bool classic in new[] { false, true })
            foreach (MorphInterpolation path in Enum.GetValues(typeof(MorphInterpolation)))
            {
                morph.classicLiao = classic;
                morph.interpolation = path;
                morph.preserveStationaryDetails = false;
                var off = Frame(doc, .5);
                morph.preserveStationaryDetails = true;
                var on = Frame(doc, .5);
                string label = (classic ? "Classic Liao " : "Liao ") + path;
                float pointOff = off[12 * size + 20].r, pointOn = on[12 * size + 20].r;
                float edgeOff = off[44 * size + 44].r, edgeOn = on[44 * size + 44].r;
                string samples = $"point (20,12) off={pointOff:F4} on={pointOn:F4}, edge (44,44) off={edgeOff:F4} on={edgeOn:F4}";
                // Curved trajectories can miss the linear halfway pixel. Compare the
                // same stripe with the option off; black would mean the pin was ignored.
                check.True(pointOn > .5f && pointOn >= pointOff - .1f, $"{label} manual point carries the white stripe: {samples}");
                check.True(edgeOn < .25f, $"{label} manual edge carries the black stripe: {samples}");
                check.True(on[32 * size + 4].r < .25f && on[32 * size + 60].r > .75f,
                    label + " unpinned distant stripes stay in place");
                check.True(!off.AsSpan().SequenceEqual(on), label + " changing the option updates the rendered midpoint");
                morph.preserveStationaryDetails = false;
                check.True(off.AsSpan().SequenceEqual(Frame(doc, .5)), label + " disabling restores the prior midpoint");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(doc);
            UnityEngine.Object.DestroyImmediate(stripes);
        }
    }

    static void CheckStationaryPatch(TestContext check, WhimTexDocument document, string fromId, string toId)
    {
        var layer = document.layers[0];
        var morph = (MorphGroupLayerBehaviour)layer.Behaviour;
        var setLayer = typeof(WhimTexApi).GetMethod("SetLayer", BindingFlags.NonPublic | BindingFlags.Static);
        var parse = setLayer.GetParameters()[2].ParameterType.GetMethod("Parse", new[] { typeof(string) });
        var aliases = new Dictionary<string, Layer>();
        void Patch(string fields)
        {
            var settings = parse.Invoke(null, new object[] { "{\"morphGroup\":{" + fields + "}}" });
            setLayer.Invoke(null, new object[] { document, layer, settings, aliases });
        }
        Patch("\"preserveStationaryDetails\":true");
        check.True(morph.preserveStationaryDetails && morph.mode == MorphMode.Vector, "Agent can enable the inactive group setting");
        Patch("\"classicLiao\":true");
        check.True(morph.preserveStationaryDetails, "Unrelated agent patch retains stationary detail preservation");
        try
        {
            Patch("\"classicLiao\":false,\"preserveStationaryDetails\":\"false\"");
            throw new InvalidOperationException("Agent accepted a string for stationary detail preservation");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is WhimTexDocumentException error &&
            error.Message.Contains("preserveStationaryDetails")) { }
        check.True(morph.preserveStationaryDetails && morph.classicLiao, "Rejected agent patch leaves both fields intact");
        Patch("\"preserveStationaryDetails\":false");
        check.True(!morph.preserveStationaryDetails && morph.classicLiao, "Agent can disable just stationary detail preservation");
        var pair = morph.Find(fromId, toId);
        check.True(pair?.anchors.Count == 1 && pair.segments.Count == 2 && pair.duration == 2, "Agent patches preserve manual guides and timing");
    }

    static void CheckFlattenedInfluence(TestContext check, MorphTransition pair)
    {
        var compile = typeof(MorphTransition).GetMethod("RasterGuides", BindingFlags.Instance | BindingFlags.NonPublic);
        IReadOnlyList<MorphSegment> Pieces() =>
            (IReadOnlyList<MorphSegment>)compile.Invoke(pair, new object[] { 256, 128 });
        void CheckPieces()
        {
            var pieces = Pieces();
            check.True(pieces.Count > 2, "Marked cubic generates multiple chords");
            for (int i = 0; i < pieces.Count; i++)
            {
                var authored = pair.segments[i == 0 ? 0 : 1];
                check.Equal(authored.radius, pieces[i].radius, "Current radius on chord " + i);
                check.Equal(authored.boundary, pieces[i].boundary, "Current boundary on chord " + i);
            }
        }
        CheckPieces();
        pair.segments[0].radius = 1;
        pair.segments[1].radius = .25f;
        CheckPieces();
        pair.segments[1].boundary = true;
        CheckPieces();
        pair.segments[0].boundary = true;
        CheckPieces();
    }

    static Layer Shape(int x, Color color)
    {
        var layer = new Layer(new ShapeLayerBehaviour { fillColor = color });
        layer.transform.scaleF = new Vector2(.25f, .375f);
        layer.transform.positionF = new Vector2(x, 0);
        return layer;
    }

    static void AssertPixel(TestContext check, Color[] pixels, int x, Color expected, string where)
    {
        var c = pixels[32 * 64 + x];
        check.True(Mathf.Abs(c.r-expected.r) + Mathf.Abs(c.g-expected.g) + Mathf.Abs(c.b-expected.b) +
            Mathf.Abs(c.a-expected.a) < .03f, where + ": " + c);
    }

    static Color[] Frame(WhimTexDocument document, double seconds)
    {
        var evaluate = typeof(WhimTexDocument).GetMethod("EvaluateAtTime", BindingFlags.Instance | BindingFlags.NonPublic);
        using var scope = (IDisposable)evaluate.Invoke(document, new object[]{ seconds });
        using var image = new Image(document.ComposeCanvas());
        return image.value.GetPixels();
    }

    readonly struct Image : IDisposable
    {
        internal readonly Texture2D value;
        internal Image(Texture2D image) { value = image; }
        public void Dispose() => UnityEngine.Object.DestroyImmediate(value);
    }
}
