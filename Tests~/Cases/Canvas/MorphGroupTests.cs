using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class MorphGroupTests
{
    public static string Run()
    {
        string measurements = null;
        var json = TestContext.Run("Morph Group timeline, pixels and persistence",
            check => Test(check, value => measurements = value));
        if (measurements == null) return json;
        var result = JsonUtility.FromJson<TestResult>(json);
        result.message += "\n" + measurements;
        return result.ToJson();
    }

    static void Test(TestContext check, Action<string> report)
    {
        CheckGuideSplits(check);
        CheckGuideEditGuards(check);
        CheckIndependentPaths(check);
        CheckPathRigidFit(check);
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
                check.True(restoredMorph.visibilityMask, "Documents without a visibility option open with the mask enabled");
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
            check.True(!legacy.Contains("\"correction\"") && !legacy.Contains("\"foreground\""),
                "Legacy compact guides retain no correction or foreground");
            pair.segments[0].foreground = true;
            RejectInvalidGuide("foreground contour must close");
            pair.segments[0].foreground = false;
            pair.anchors[0].correction = MorphGuideCorrection.Create(MorphCorrectionMode.LocalChoice);
            pair.anchors[0].correction.feather = -.2f;
            RejectInvalidGuide("Invalid morph guide correction");
            pair.anchors[0].correction.feather = .35f;
            pair.segments[1].correction = MorphGuideCorrection.Create(MorphCorrectionMode.MatchTone);
            pair.segments[1].correction.width = .125f;
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
            var guide = new MorphGuidePath { closed = true, foreground = true, boundary = true, radius = .23f,
                correction = MorphGuideCorrection.Create(MorphCorrectionMode.MatchTone), marks = new() { new Vector2(.3f,.7f) },
                from = new() { Knot(.1f,.1f), Knot(.5f,.2f), Knot(.4f,.5f) },
                to = new() { Knot(.3f,.1f), Knot(.45f,.1f), Knot(.7f,.2f), Knot(.6f,.5f) } };
            guide.to[1].smooth = true;
            guide.to[1].outTangent = new Vector2(.1f,.02f);
            pair.guides.Add(guide);
            void CheckStoredInfluence(MorphTransition stored, string format)
            {
                var path = stored.guides[0];
                check.True(path.from.Count == 3 && path.to.Count == 4 && path.closed && path.foreground && path.boundary &&
                    path.radius == .23f && path.correction.mode == MorphCorrectionMode.MatchTone &&
                    path.marks[0].Equals(guide.marks[0]) && path.to[1].smooth &&
                    path.to[1].outTangent.Equals(guide.to[1].outTangent), format + " independent guide path");
                check.Equal(MorphCorrectionMode.LocalChoice, stored.anchors[0].correction.mode, format + " local choice");
                check.Equal(.35f, stored.anchors[0].correction.feather, format + " point feather");
                check.Equal(MorphCorrectionMode.MatchTone, stored.segments[1].correction.mode, format + " seam correction");
                check.Equal(.125f, stored.segments[1].correction.width, format + " seam width");
                check.Equal(.125f, stored.anchors[0].radius, format + " point radius");
                check.True(stored.segments[0].radius == 1 && stored.segments[0].boundary, format + " straight guide settings");
                check.True(stored.segments[1].radius == .25f && stored.segments[1].boundary, format + " cubic guide settings");
            }
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                morph.interpolation = MorphInterpolation.AutomaticCurve;
                morph.classicLiao = true;
                morph.matching = MorphMatching.MARCO;
                pair.marcoAnchors = new() { new MorphAnchor { from = Vector2.zero, to = Vector2.one, radius = .19f } };
                morph.preserveGeometry = true;
                morph.preserveStationaryDetails = true;
                morph.visibilityMask = false;
                morph.maskBlend = MorphMaskBlend.NormalizeDetail;
                var json = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                using var restored = WhimTexDocumentJson.Read(json, false);
                var copy = (MorphGroupLayerBehaviour)restored.Document.layers[0].Behaviour;
                check.Equal(MorphMatching.MARCO, copy.matching, "Saved raster matching " + mode);
                check.Equal(Vector2.one, copy.Find(a.Id,b.Id).marcoAnchors[0].to, "Saved semantic guide " + mode);
                check.True(!copy.visibilityMask, "Saved disabled visibility mask " + mode);
                check.Equal(MorphMaskBlend.NormalizeDetail, copy.maskBlend, "Saved tone and contrast setting " + mode);
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
            CheckStationaryGuides(check, report);
            check.Equal(previous, RenderTexture.active, "Caller render target preserved");
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            UnityEngine.Object.DestroyImmediate(doc);
        }
    }

    // The external test runner accesses the package's internal editing API.
    static bool GuideEdit(MorphTransition pair, string name, params object[] args) =>
        (bool)typeof(MorphTransition).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
            null, Type.GetTypeArray(args), null).Invoke(pair, args);

    static void CheckGuideSplits(TestContext check)
    {
        // The curves are (t, t²) and (s, 1-s²/2). Their x coordinates independently reveal both cut parameters.
        var size = new Vector2(256, 1024);
        var marks = new[] { new Vector2(.13f, .12f), new Vector2(.25f, .45f),
            new Vector2(.75f, .85f), new Vector2(.9f, .95f) };
        double Arc(double t, double curvature)
        {
            double k = 2 * curvature * size.y / size.x, z = k * t;
            return t * Math.Sqrt(1 + z*z) + Math.Log(z + Math.Sqrt(1 + z*z)) / k;
        }
        float DestinationAt(float t)
        {
            double fraction = (Arc(t, 1) - Arc(marks[1].x, 1)) / (Arc(marks[2].x, 1) - Arc(marks[1].x, 1));
            double low = marks[1].y, high = marks[2].y;
            double distance = Arc(low, .5) + fraction * (Arc(high, .5) - Arc(low, .5));
            for (int i = 0; i < 32; i++)
            {
                double mid = (low + high) / 2;
                if (Arc(mid, .5) < distance) low = mid;
                else high = mid;
            }
            return (float)((low + high) / 2);
        }
        foreach (bool onMark in new[] { false, true })
        foreach (bool from in new[] { false, true })
        {
            float cutA = onMark ? marks[1].x : .5f;
            float cutB = onMark ? marks[1].y : DestinationAt(cutA);
            var pair = new MorphTransition();
            var start = new MorphGuideNode { from = Vector2.zero, to = Vector2.up, smooth = true };
            pair.nodes.Add(start);
            var edge = new MorphSegment { startNode = 1, fromEnd = Vector2.one, toEnd = new Vector2(1, .5f),
                bezier = true, fromOut = Vector2.right / 3, fromIn = new Vector2(-1, -2) / 3,
                toOut = Vector2.right / 3, toIn = new Vector2(-1, 1) / 3, marks = new List<Vector2>(marks),
                radius = .37f, boundary = true, foreground = true,
                correction = new MorphGuideCorrection { mode = MorphCorrectionMode.MatchTone, width = .21f, feather = .34f, strength = .56f } };
            var returning = new MorphSegment { fromStart = edge.fromEnd, toStart = edge.toEnd, endNode = 1, foreground = true };
            pair.segments.Add(edge);
            pair.segments.Add(returning);
            string original = JsonUtility.ToJson(pair);
            float clicked = from ? cutA : cutB;
            check.True(GuideEdit(pair, "CanSplitGuide", 0, from, clicked, size), "Both panes accept the split");
            check.Equal(original, JsonUtility.ToJson(pair), "Split preflight preserves the whole transition");
            check.True(GuideEdit(pair, "SplitGuide", 0, from, clicked, size), "Commit split");
            var left = pair.segments[0]; var right = pair.segments[1];
            check.True(pair.nodes.Count == 2 && pair.segments.Count == 3 && left == edge && pair.segments[2] == returning &&
                left.startNode == 1 && right.endNode == 0 && left.endNode == 2 && right.startNode == 2,
                "Split retains outer references, inline ends, edge order and neighboring identities");
            var joint = pair.nodes[1];
            check.True(joint.smooth && pair.nodes[0] == start && start.smooth, "New cubic joint and original node settings");
            check.Near(cutA, joint.from.x, .0005, "Source pixel arc correspondence");
            check.Near(cutB, joint.to.x, .0005, "Destination pixel arc correspondence");
            check.Near(clicked, from ? joint.from.x : joint.to.x, .000001, "Clicked parameter is unchanged");
            var cut = new Vector2(joint.from.x, joint.to.x);
            int leftMarks = 0, rightMarks = 0;
            foreach (var mark in marks)
            {
                if (onMark && mark.Equals(marks[1])) continue;
                bool before = mark.x < cut.x;
                var local = before ? left.marks[leftMarks++] : right.marks[rightMarks++];
                var restored = before ? local * cut : cut + local * (Vector2.one - cut);
                check.True((restored - mark).magnitude < .000001f, "Keep both parameters and order of each surviving mark");
            }
            check.True(leftMarks == left.marks.Count && rightMarks == right.marks.Count, "Exact cut mark becomes just the shared knot");
            foreach (var half in new[] { left, right })
            {
                check.True(half.bezier && half.foreground && half.boundary && half.radius == .37f &&
                    half.correction.mode == MorphCorrectionMode.MatchTone && half.correction.width == .21f &&
                    half.correction.feather == .34f && half.correction.strength == .56f, "Both halves retain authored influence");
                bool first = half == left;
                for (int i = 0; i <= 16; i++)
                {
                    float u = i / 16f;
                    float t = first ? u * cut.x : Mathf.Lerp(cut.x, 1, u);
                    float s = first ? u * cut.y : Mathf.Lerp(cut.y, 1, u);
                    Vector2 a = first ? start.from : joint.from, b = first ? joint.from : Vector2.one;
                    Vector2 c = first ? start.to : joint.to, d = first ? joint.to : new Vector2(1, .5f);
                    var source = VectorGeometry.EvaluateCubic(a, a + half.fromOut, b + half.fromIn, b, u);
                    var target = VectorGeometry.EvaluateCubic(c, c + half.toOut, d + half.toIn, d, u);
                    check.True((source - new Vector2(t, t*t)).magnitude < .000002f &&
                        (target - new Vector2(s, 1 - .5f*s*s)).magnitude < .000002f, "Both cubic restrictions preserve the full curve");
                }
            }
            typeof(MorphTransition).GetMethod("ValidateGuides", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pair, null);
        }
    }

    static void CheckGuideEditGuards(TestContext check)
    {
        var pair = new MorphTransition();
        var line = new MorphSegment { fromEnd = Vector2.right, toEnd = Vector2.up, marks = null };
        pair.segments.Add(line);
        bool Continue(int index, bool end) => GuideEdit(pair, "CanContinueGuide", index, end);
        bool Capacity(int edges, int nodes) => GuideEdit(pair, "CanAddGuides", edges, nodes);
        void Reject(int index, float t, string why)
        {
            string before = JsonUtility.ToJson(pair);
            check.True(!GuideEdit(pair, "CanSplitGuide", index, true, t) &&
                !GuideEdit(pair, "SplitGuide", index, true, t), why);
            check.Equal(before, JsonUtility.ToJson(pair), why + " preserves the transition");
        }
        Reject(-1, .5f, "Missing edge");
        foreach (float t in new[] { float.NaN, float.NegativeInfinity, -1f, 0f, 1e-8f, 1f, 2f })
            Reject(0, t, "Invalid or collapsed cut");
        line.toEnd = new Vector2(float.PositiveInfinity, 0);
        Reject(0, .5f, "Nonfinite opposite pane");
        line.toEnd = Vector2.up;
        line.endNode = 2;
        Reject(0, .5f, "Dangling node");
        check.True(!Continue(0, false), "Continuation rejects an invalid node reference");
        line.endNode = 0;
        line.toIn = new Vector2(float.NaN, 0);
        Reject(0, .5f, "Nonfinite handle");
        line.toIn = Vector2.zero;
        line.marks = new() { new Vector2(.25f, .6f), new Vector2(.75f, .4f) };
        Reject(0, .5f, "Crossed marks");
        line.marks[0] = new Vector2(.25f, .5f);
        line.marks[1] = new Vector2(.75f, .50000006f);
        Reject(0, .5f, "No representable destination parameter between adjacent marks");
        line.marks = new() { new Vector2(.5f, .5f) };
        line.bezier = true;
        line.toEnd = 3 * Vector2.up;
        line.toIn = -4 * Vector2.up;
        Reject(0, .5f, "Cubic returns to the destination endpoint at the cut");
        line.marks = null;
        line.bezier = false;
        line.toEnd = Vector2.up;
        line.toIn = Vector2.zero;
        check.True(Continue(0, false) && Continue(0, true) && !Continue(-1, false), "Both ends of an isolated inline guide can continue");
        check.True(Capacity(999999, 1000000) && !Capacity(1000000, 0) && !Capacity(0, int.MaxValue) && !Capacity(-1, 0),
            "Whole-path deltas are checked against available serialized list capacity without overflow");
        var next = new MorphSegment { fromStart = line.fromEnd, toStart = line.toEnd, fromEnd = Vector2.one, toEnd = Vector2.one };
        var draft = new[] { next };
        check.True(GuideEdit(pair, "CanAppendGuidePath", (object)draft) && pair.nodes.Count == 0 && pair.segments.Count == 1,
            "Dry-run a continuation that will bind inline endpoints");
        next.toEnd = next.toStart;
        check.True(!GuideEdit(pair, "CanAppendGuidePath", (object)draft), "Reject a path collapsed on just one pane");
        next.toEnd = Vector2.one;
        next.bezier = true;
        next.toOut = next.toStart = new Vector2(float.MaxValue, 0);
        check.True(!GuideEdit(pair, "CanAppendGuidePath", (object)draft), "Reject an overflowing absolute control point");
        next.bezier = false;
        next.toStart = line.toEnd;
        next.toOut = Vector2.zero;
        line.marks = new() { new Vector2(.5f, .25f) };
        check.True(GuideEdit(pair, "SplitGuide", 0, false, .625f), "Reverse-pane split on a marked straight guide");
        var joint = pair.nodes[0];
        check.True(!joint.smooth && joint.from == .75f * Vector2.right && joint.to == .625f * Vector2.up &&
            !pair.segments[0].bezier && !pair.segments[1].bezier, "Straight guide uses the containing mark interval on each pane");
        check.True(!Continue(0, true) && !Continue(1, false) && Continue(0, false) && Continue(1, true), "Only degree-one ends can continue");
        pair.segments.Add(next);
        check.True(!Continue(1, true) && !Continue(2, false), "An inline shared pair blocks continuation in either direction");
        next.fromStart = joint.from; next.toStart = Vector2.left;
        check.True(Continue(2, false), "Coincidence on a single pane is independent");
        next.toStart = joint.to;
        check.True(!Continue(2, false), "An inline branch meeting a referenced knot cannot continue from the branch point");

        var stationary = new MorphTransition();
        stationary.segments.Add(new MorphSegment { fromEnd = Vector2.right, fromOut = Vector2.right, fromIn = Vector2.left,
            toEnd = Vector2.up, bezier = true, marks = null });
        check.True(GuideEdit(stationary, "SplitGuide", 0, true, .5f) && !stationary.nodes[0].smooth,
            "A valid cubic cut with a vanishing source tangent keeps a shared corner");

        MorphSegment Foreground(Vector2 a, Vector2 b) => new MorphSegment {
            fromStart = a, toStart = a, fromEnd = b, toEnd = b, foreground = true };
        var loops = new MorphTransition();
        var mid = Vector2.right * .5f;
        loops.segments.AddRange(new[] { Foreground(Vector2.zero, Vector2.right), Foreground(Vector2.right, Vector2.zero),
            Foreground(mid, Vector2.up), Foreground(Vector2.up, mid) });
        check.True(!GuideEdit(loops, "SplitGuide", 0, true, .5f) && loops.nodes.Count == 0 && loops.segments.Count == 4,
            "A split cannot turn two closed foreground contours into a four-way joint");
    }

    static MorphPathKnot Knot(float x, float y) => new MorphPathKnot { position = new Vector2(x, y) };

    static object PathCall(MorphGuidePath path, string name, params object[] args) =>
        typeof(MorphGuidePath).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(path, args);

    static void CheckIndependentPaths(TestContext check)
    {
        var size = new Vector2(240, 80);
        var path = new MorphGuidePath { from = new() { Knot(0,0), Knot(.5f,0), Knot(.5f,1) },
            to = new() { Knot(.2f,.2f), Knot(.2f,.9f) }, marks = new() { new Vector2(.6f,.25f) } };
        Vector2 At(bool from, float f) => (Vector2)PathCall(path, "AtLength", from, f, size);
        float Fraction(bool from, int span, float t) => (float)PathCall(path, "LengthAt", from, span, t, size);
        List<MorphSegment> Compile()
        {
            var list = new List<MorphSegment>();
            PathCall(path, "AppendLinear", size, list);
            return list;
        }
        void Reject(string message)
        {
            try { PathCall(path, "Validate"); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { return; }
            throw new Exception("Accepted invalid path: " + message);
        }
        check.True(ReferenceEquals(path.from, PathCall(path,"Side",true)), "Side returns the authored list");
        check.Near(.6, Fraction(true, 1, 0), 1e-5, "Rectangular canvas measures the 120px and 80px spans");
        check.Near(.6*.15625, Fraction(true, 0, .25f), .00008, "Arc lookup includes nonlinear cubic speed");
        check.Near(.5, At(true, .8f).y, .0001, "Fraction of the complete source side");
        var pieces = Compile();
        check.True(pieces.Exists(e => Vector2.Scale(e.fromEnd - new Vector2(.5f,0), size).magnitude < .001f &&
            Vector2.Scale(e.toEnd - new Vector2(.2f,.375f), size).magnitude < .001f), "A mark pairs exact whole-side positions");
        var groupCount = typeof(MorphSegment).GetField("chordCount", BindingFlags.NonPublic | BindingFlags.Instance);
        check.Equal(pieces.Count, (int)groupCount.GetValue(pieces[0]), "One contribution across all marks and spans");
        check.True(ReferenceEquals(pieces[0], Compile()[0]), "Unchanged geometry reuses its chords");
        path.radius = .4f;
        path.boundary = true;
        path.correction = MorphGuideCorrection.Create(MorphCorrectionMode.LocalChoice);
        check.True(Compile().TrueForAll(e => e.radius == .4f && e.boundary && e.correction.mode == MorphCorrectionMode.LocalChoice),
            "Settings reach every cached chord");
        path.marks[0] = new Vector2(.6f,.75f);
        check.True(Compile().Exists(e => Mathf.Abs(e.toEnd.y - .725f) < .0001f), "In-place mark edit rebuilds correspondence");
        path.to[0].outTangent = new Vector2(.35f,.12f);
        var before = At(false,.4f);
        var oldEnd = (Vector2)PathCall(path,"Evaluate",false,0,.37f);
        check.True((bool)PathCall(path,"Split",false,0,.37f), "Split destination cubic independently");
        check.True(path.from.Count == 3 && path.to.Count == 3 && path.to[1].smooth && path.to[1].position == oldEnd,
            "Shape-preserving split inserts one tangent knot on B");
        check.True(Vector2.Scale(At(false,.4f)-before,size).magnitude < .025f &&
            path.marks[0].Equals(new Vector2(.6f,.75f)), "Arc landmark stays put after asymmetric subdivision");
        path.foreground = true;
        Reject("open foreground");
        path.closed = true;
        PathCall(path,"Validate");
        var first = path.from[0];
        check.True(At(true,1).Equals(first.position) && Fraction(true,2,1) == 1, "Closing span reaches seam at length one");
        check.True((bool)PathCall(path,"Split",true,2,.63f) && ReferenceEquals(path.from[0], first),
            "Closing-span cut appends before the original seam");
        pieces = Compile();
        check.True(pieces[0].fromStart.Equals(pieces[^1].fromEnd) && pieces[0].toStart.Equals(pieces[^1].toEnd) &&
            pieces.TrueForAll(e => e.foreground), "Both foreground outlines close across independent seams");
        path.marks.Add(new Vector2(.8f,.7f));
        Reject("crossed destination marks");
        path.marks.Clear();
        path.radius = float.NaN;
        Reject("nonfinite radius");
        path.radius = 0;
        path.from[0].inTangent.x = float.NaN;
        Reject("nonfinite incoming handle on seam");
        path.from[0].inTangent = Vector2.zero;
        path.to.Add(Knot(path.to[0].position.x, path.to[0].position.y));
        Reject("zero-length closing span");
    }

    static void CheckPathRigidFit(TestContext check)
    {
        var size = new Vector2(96, 32);
        var path = new MorphGuidePath { from = new() { Knot(0,.25f), Knot(16/96f,.25f) },
            to = new() { Knot(.5f,4/32f), Knot(.5f,20/32f) } };
        var pin = new MorphAnchor { from = new Vector2(8/96f,20/32f), to = new Vector2(36/96f,12/32f) };
        var fit = typeof(MorphTransition).Assembly.GetType("DCFApixels.WhimTex.MorphRasterRenderer")
            .GetMethod("TryFitRigidLayer", BindingFlags.Static | BindingFlags.NonPublic);
        void AssertFit(bool reverse, int count)
        {
            var chords = new List<MorphSegment>();
            PathCall(path,"AppendLinear",size,chords);
            check.Equal(count, chords.Count, "Independent quadrature test chord count");
            object[] args = { new[] { pin }, chords, size, true, null, null, null };
            check.True((bool)fit.Invoke(null,args), "Rigid fit accepts a point and an oriented path");
            var mean = (Vector4)args[4];
            var expected = reverse ? new Vector4(41.5f,11.5f,7.5f,13.5f) : new Vector4(7.5f,13.5f,41.5f,11.5f);
            check.True((mean - expected).magnitude < .001f, "Each path has unit mass regardless of chord lengths or count");
            check.Near(reverse ? -Math.PI/2 : Math.PI/2, (float)args[6], .0001, "Rotation direction in rectangular pixel metric");
        }
        AssertFit(false, 1);
        for (int i=1; i<8; i++)
        {
            float f = i*i/64f;
            path.marks.Add(new Vector2(f,f));
        }
        AssertFit(false, 8);
        var legacy = new MorphTransition { nodes = new() {
            new MorphGuideNode { from = path.from[0].position, to = path.to[0].position },
            new MorphGuideNode { from = path.from[1].position, to = path.to[1].position } },
            segments = new() { new MorphSegment { startNode = 1, endNode = 2, marks = new List<Vector2>(path.marks) } } };
        var prepare = typeof(MorphTransition).GetMethod("RasterGuides", BindingFlags.NonPublic | BindingFlags.Instance);
        var oldChords = (List<MorphSegment>)prepare.Invoke(legacy,new object[]{(int)size.x,(int)size.y});
        check.Equal(8,oldChords.Count,"Eight legacy chords share node endpoints");
        var newChords = new List<MorphSegment>();
        PathCall(path,"AppendLinear",size,newChords);
        foreach (bool withPoint in new[] {false,true})
        foreach (bool withPath in new[] {false,true})
        {
            var manual = new List<MorphAnchor>();
            if (withPoint) manual.Add(pin);
            var asOldPoints = new List<MorphAnchor>(manual);
            var mixed = new List<MorphSegment>(oldChords);
            foreach (var e in oldChords)
            {
                asOldPoints.Add(new MorphAnchor { from = e.fromStart, to = e.toStart });
                asOldPoints.Add(new MorphAnchor { from = e.fromEnd, to = e.toEnd });
            }
            if (withPath) mixed.AddRange(newChords);
            object[] reference = { asOldPoints, withPath ? newChords : null, size, true, null, null, null };
            object[] measured = { manual, mixed, size, true, null, null, null };
            check.True((bool)fit.Invoke(null,reference) && (bool)fit.Invoke(null,measured),"Reference rigid transform exists");
            for (int i = 4; i <= 6; i++)
                check.Equal(reference[i],measured[i],"Legacy endpoints retain their historical mass, region and rotation");
        }
        (path.from,path.to) = (path.to,path.from);
        (pin.from,pin.to) = (pin.to,pin.from);
        AssertFit(true, 8);
    }

    static void CheckStationaryGuides(TestContext check, Action<string> report)
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
            pair.anchors.Clear();
            pair.segments.Clear();
            morph.classicLiao = false;
            morph.preserveGeometry = true;
            morph.preserveStationaryDetails = true;
            morph.interpolation = MorphInterpolation.Linear;
            var unguided = Frame(doc, .5);
            var guide = new MorphGuidePath();
            foreach (int x in new[] {36, 52})
            {
                var side = x == 36 ? guide.from : guide.to;
                side.Add(new MorphPathKnot { position = UV(x,36), outTangent = new Vector2(6,4) / size });
                side.Add(new MorphPathKnot { position = UV(x,52), inTangent = new Vector2(-6,-4) / size });
            }
            pair.guides.Add(guide);
            var initial = Frame(doc, .5);
            var reference = Chords(guide);
            int center = 44 * size + 44;
            check.True(unguided[center].r > .75f && initial[center].r < .25f, "Normal document render transports the stripe with a path alone");
            foreach (float cut in new[] {.31f,.56f,.77f})
                check.True((bool)PathCall(guide,"Split",true,0,cut), "Split one source cubic in place");
            var live = Measure(doc, guide, "live");
            var clone = typeof(WhimTexDocumentFile).GetMethod("CreateEditableCopy", BindingFlags.NonPublic | BindingFlags.Static);
            var cold = (WhimTexDocument)clone.Invoke(null, new object[] { doc });
            Texture2D copiedStripes = null;
            try
            {
                var root = cold.layers[0];
                copiedStripes = ((FileLayerBehaviour)root.children[0].Behaviour).sourceTexture;
                var restored = ((MorphGroupLayerBehaviour)root.Behaviour).Find(a.Id, b.Id).guides[0];
                check.True(!ReferenceEquals(guide, restored) && restored.from.Count == 5, "Native model reload retains the three cuts");
                var reopened = Measure(cold, restored, "native reload");
                string measured = live.message + "\n" + reopened.message;
                report(measured);
                check.True(live.ok && reopened.ok, measured);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cold);
                if (copiedStripes != null && copiedStripes != stripes) UnityEngine.Object.DestroyImmediate(copiedStripes);
            }

            List<MorphSegment> Chords(MorphGuidePath path)
            {
                var result = new List<MorphSegment>();
                PathCall(path, "AppendLinear", new Vector2(size,size), result);
                return result;
            }

            (bool ok, string message) Measure(WhimTexDocument target, MorphGuidePath path, string phase)
            {
                var pixelsAfter = Frame(target, .5);
                double sum = 0;
                float largest = 0;
                for (int i = 0; i < initial.Length; i++)
                {
                    float error = Mathf.Abs(initial[i].r - pixelsAfter[i].r);
                    largest = Mathf.Max(largest,error);
                    sum += error;
                }
                var chords = Chords(path);
                bool identical = reference.Count == chords.Count;
                for (int i = 0; identical && i < chords.Count; i++)
                    identical = reference[i].fromStart.Equals(chords[i].fromStart) && reference[i].fromEnd.Equals(chords[i].fromEnd) &&
                        reference[i].toStart.Equals(chords[i].toStart) && reference[i].toEnd.Equals(chords[i].toEnd);
                double mean = sum / initial.Length;
                return (identical && largest < .1f && mean < .003, FormattableString.Invariant(
                    $"Stationary guide after A-only subdivision ({phase}): maximum {largest:G9}, mean {mean:G9}, identical paired chords={identical} ({reference.Count}/{chords.Count})"));
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
        Patch("\"visibilityMask\":true");
        check.True(morph.visibilityMask && morph.classicLiao, "Agent can enable the stored mask independently");
        Patch("\"visibilityMask\":false");
        check.True(!morph.visibilityMask && morph.classicLiao, "Agent can disable only the mask");
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
