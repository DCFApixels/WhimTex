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
            check.Equal(MorphRasterMethod.LocalContrast, morph.rasterMethod, "Legacy raster default");
            check.Equal(MorphInterpolation.Linear, morph.interpolation, "Default raster trajectory");
            check.True(!morph.classicLiao, "Liao uses current matching by default");
            check.True(!morph.preserveGeometry, "Local rigidity defaults off");
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
            morph.mode = MorphMode.Raster;
            morph.rasterMethod = MorphRasterMethod.Liao;
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
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                morph.interpolation = MorphInterpolation.AutomaticCurve;
                morph.classicLiao = true;
                morph.preserveGeometry = true;
                var json = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                using var restored = WhimTexDocumentJson.Read(json, false);
                var copy = (MorphGroupLayerBehaviour)restored.Document.layers[0].Behaviour;
                check.Equal(MorphRasterMethod.Liao, copy.rasterMethod, "Saved raster method " + mode);
                check.True(copy.classicLiao, "Saved classic Liao setting " + mode);
                check.True(copy.preserveGeometry, "Saved local rigidity " + mode);
                check.Equal(MorphInterpolation.AutomaticCurve, copy.interpolation, "Saved raster trajectory " + mode);
                check.Equal(MorphMode.Vector, copy.mode, "Saved vector mode " + mode);
                var transition = copy.Find(a.Id,b.Id);
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
            var clone = typeof(WhimTexDocumentFile).GetMethod("CreateEditableCopy", BindingFlags.Static | BindingFlags.NonPublic);
            var native = (WhimTexDocument)clone.Invoke(null, new object[] { doc });
            try
            {
                var settings = (MorphGroupLayerBehaviour)native.layers[0].Behaviour;
                check.Equal(MorphRasterMethod.Liao, settings.rasterMethod, "Native container raster method");
                check.True(settings.classicLiao, "Native container classic Liao setting");
                check.True(settings.preserveGeometry, "Native container local rigidity");
                check.Equal(MorphInterpolation.AutomaticCurve, settings.interpolation, "Native container raster trajectory");
                check.Equal(pair.anchors[0].from, settings.Find(a.Id, b.Id).anchors[0].from, "Native container paired points");
                check.True(Frame(native, 1)[32 * 64 + 32].a > .95f, "Native container midpoint");
            }
            finally { UnityEngine.Object.DestroyImmediate(native); }
            // Each copied child gets a new ID. Its transitions must still find the corresponding keys.
            var duplicate = typeof(WhimTexDocument).GetMethod("DuplicateLayersForAgent", BindingFlags.Instance | BindingFlags.NonPublic);
            var copies = (Dictionary<Layer,Layer>)duplicate.Invoke(doc, new object[] { new List<Layer>{ group }, false });
            var copyGroup = (MorphGroupLayerBehaviour)copies[group].Behaviour;
            check.True(copyGroup.Find(copies[a].Id, copies[b].Id)?.anchors.Count == 1, "Duplicated points remapped");
            check.True(copyGroup.Find(a.Id, b.Id) == null, "Duplicate independent from originals");
            check.Equal(previous, RenderTexture.active, "Caller render target preserved");
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            UnityEngine.Object.DestroyImmediate(doc);
        }
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
