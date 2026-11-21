using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class MorphGuideJsonTests
{
    const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string LegacyFields = @"""fromId"":""A"",""toId"":""B"",""duration"":2,
        ""anchors"":[{""from"":[0.25,0.5],""to"":[0.75,0.25]}],
        ""marcoAnchors"":[{""from"":[0.1,0.3],""to"":[0.2,0.4]}],
        ""nodes"":[{""from"":[0.5,0.2],""to"":[0.7,0.3],""smooth"":true}],
        ""segments"":[{""startNode"":1,""fromEnd"":[0.7,1],""toEnd"":[0.5,0.9],""bezier"":true,
            ""fromOut"":[0.1,0],""toIn"":[0,-0.2],""marks"":[[0.2,0.3]]}]";
    const string GuideFields = @"""guides"":[{
        ""from"":[{""position"":[0,0],""inTangent"":[-0.125,0],""outTangent"":[0.25,0],""smooth"":true},
                  {""position"":[1,0]},{""position"":[0.5,1]}],
        ""to"":[{""position"":[0,0]},{""position"":[1,0]},{""position"":[1,1]},{""position"":[0,1]}],
        ""closed"":true,""boundary"":true,""foreground"":true,""radius"":1e-17,
        ""correction"":{""mode"":""MatchTone"",""width"":0.125,""feather"":0.25,""strength"":0.75},
        ""marks"":[[0.125,0.25],[0.5,0.875]]}]";

    static string Recipe(string fields) =>
        @"{""format"":""whimtex.document"",""version"":3,""document"":{""width"":64,""height"":32},""layers"":[{
          ""id"":""group"",""behaviour"":{""$type"":""MorphGroupLayerBehaviour"",""transitions"":[{" + fields +
        @"}]},""children"":[{""id"":""A"",""behaviour"":{""$type"":""ColorFillLayerBehaviour""}},
                            {""id"":""B"",""behaviour"":{""$type"":""ColorFillLayerBehaviour""}}]}]}";

    static MorphTransition Pair(Layer layer) =>
        ((MorphGroupLayerBehaviour)layer.Behaviour).Find(layer.children[0].Id, layer.children[1].Id);

    static void CheckPair(TestContext t, Layer group, string label, bool hasGuide = true)
    {
        var pair = Pair(group);
        t.True(pair != null, label + " transition bound to these children");
        t.Equal(2f, pair.duration, label + " duration");
        t.Equal(1, pair.anchors.Count, label + " point count");
        t.Equal(new Vector2(.75f,.25f), pair.anchors[0].to, label + " single point");
        t.Equal(new Vector2(.1f,.3f), pair.marcoAnchors[0].from, label + " baked point");
        t.Equal(1, pair.nodes.Count, label + " original shared node count");
        t.Equal(new Vector2(.5f,.2f), pair.nodes[0].from, label + " shared node A");
        t.True(pair.nodes[0].smooth, label + " shared node smooth");
        t.Equal(1, pair.segments.Count, label + " original edge count");
        var edge = pair.segments[0];
        t.True(edge.bezier && edge.startNode == 1 && edge.endNode == 0, label + " original edge references");
        t.Equal(new Vector2(0,-.2f), edge.toIn, label + " original B handle");
        t.Equal(new Vector2(.2f,.3f), edge.marks[0], label + " segment parameter pair");
        t.Equal(hasGuide ? 1 : 0, pair.guides.Count, label + " path count");
        if (!hasGuide) return;
        var guide = pair.guides[0];
        t.Equal(3, guide.from.Count, label + " A knots");
        t.Equal(4, guide.to.Count, label + " B knots");
        t.Equal(new Vector2(.5f,1), guide.from[2].position, label + " last A knot");
        t.Equal(Vector2.up, guide.to[3].position, label + " last B knot");
        t.True(guide.from[0].smooth && !guide.to[0].smooth, label + " independent smooth flags");
        t.Equal(new Vector2(-.125f,0), guide.from[0].inTangent, label + " incoming offset");
        t.Equal(new Vector2(.25f,0), guide.from[0].outTangent, label + " outgoing offset");
        t.True(guide.closed && guide.boundary && guide.foreground, label + " whole-guide settings");
        t.Equal(1e-17f, guide.radius, label + " positive radius stays manual");
        t.Equal(MorphCorrectionMode.MatchTone, guide.correction.mode, label + " correction");
        t.Equal(.125f, guide.correction.width, label + " width");
        t.Equal(.25f, guide.correction.feather, label + " feather");
        t.Equal(.75f, guide.correction.strength, label + " strength");
        t.Equal(2, guide.marks.Count, label + " whole-curve mark count");
        t.Equal(new Vector2(.125f,.25f), guide.marks[0], label + " first length pair");
        t.Equal(new Vector2(.5f,.875f), guide.marks[1], label + " second length pair");
    }

    public static string Run() => TestContext.Run("Morph guide persistence", t =>
    {
        using var old = WhimTexDocumentJson.Read(Recipe(LegacyFields), false);
        CheckPair(t, old.Document.layers[0], "v3 without guides", false);
        using var mixed = WhimTexDocumentJson.Read(Recipe(LegacyFields + "," + GuideFields), false);
        var document = mixed.Document;
        var root = document.layers[0];
        CheckPair(t, root, "authored");
        foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
        {
            var options = new WhimTexJsonWriteOptions { Mode = mode };
            using var read = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document, options).Json, false);
            CheckPair(t, read.Document.layers[0], mode.ToString());
        }

        // This snapshot uses the same tagged model serializer and reader as the TIFF document block.
        var native = (WhimTexDocument)typeof(WhimTexDocumentFile).GetMethod("CreateEditableCopy", Internal)
            .Invoke(null, new object[] { document });
        try
        {
            var restored = Pair(native.layers[0]);
            var edges = (IReadOnlyList<MorphSegment>)typeof(MorphTransition).GetMethod("RasterGuides", Internal)
                .Invoke(restored, new object[] { native.width, native.height });
            bool legacyEdge = false, pathEdge = false;
            foreach (var edge in edges)
            {
                legacyEdge |= !edge.boundary && !edge.foreground;
                pathEdge |= edge.boundary && edge.foreground && edge.correction.mode == MorphCorrectionMode.MatchTone;
            }
            t.True(legacyEdge && pathEdge, "Reopened transition supplies both kinds of guide to the raster preview");
            CheckPair(t, native.layers[0], "native document after preview binding");
        }
        finally { UnityEngine.Object.DestroyImmediate(native); }

        var selection = new List<Layer> { root };
        var api = typeof(WhimTexApi);
        var json = (string)api.GetMethod("WritePortableClipboard", Internal).Invoke(null, new object[] { document, selection });
        using var clipboard = (IDisposable)api.GetMethod("ReadProceduralClipboard", Internal).Invoke(null, new object[] { json, 64, 32 });
        var clipboardDocument = (WhimTexDocument)clipboard.GetType().GetField("Document", Internal).GetValue(clipboard);
        CheckPair(t, clipboardDocument.layers[0], "portable clipboard");

        var captured = (WhimTexDocument)typeof(WhimTexDocument).GetMethod("CaptureLayerClipboard", Internal)
            .Invoke(document, new object[] { selection });
        try
        {
            CheckPair(t, captured.layers[0], "layer clipboard with remapped IDs");
            t.True(captured.layers[0].children[0].Id != root.children[0].Id, "Captured key has its own ID");
        }
        finally { UnityEngine.Object.DestroyImmediate(captured); }

        var copies = (Dictionary<Layer, Layer>)typeof(WhimTexDocument).GetMethod("DuplicateLayersForAgent", Internal)
            .Invoke(document, new object[] { selection, false });
        CheckPair(t, copies[root], "duplicate");
        var copyGuide = Pair(copies[root]).guides[0];
        copyGuide.from[0].smooth = false;
        copyGuide.to.RemoveAt(3);
        copyGuide.marks.Clear();
        CheckPair(t, root, "source after editing duplicate");

        // Feed the actual inspect settings into the API used for set operations.
        var snapshot = ApiJson.Parse(api.GetMethod("Snapshot", Internal).Invoke(null, new object[] { document, null }).ToString());
        ApiJson morphSettings = null;
        foreach (var layer in snapshot["layers"])
            if ((string)layer["id"] == root.Id) morphSettings = layer["settings"]["morphGroup"];
        t.True(morphSettings != null, "inspect returns Morph Group settings");
        var set = api.GetMethod("SetLayer", Internal);
        var parse = set.GetParameters()[2].ParameterType.GetMethod("Parse", new[] { typeof(string) });
        void Patch(string settings)
        {
            var request = parse.Invoke(null, new object[] { "{\"morphGroup\":" + settings + "}" });
            set.Invoke(null, new object[] { old.Document, old.Document.layers[0], request, new Dictionary<string, Layer>() });
        }
        Patch(morphSettings.ToString());
        CheckPair(t, old.Document.layers[0], "inspect to set");
        Patch("{\"visibilityMask\":false}");
        CheckPair(t, old.Document.layers[0], "unrelated settings patch");
        string invalid = GuideFields.Replace("[0.5,0.875]", "[0.5,0.125]");
        bool rejected = false;
        try { Patch("{\"transitions\":[{" + LegacyFields + "," + invalid + "}]}"); }
        catch (TargetInvocationException ex) when (ex.InnerException is WhimTexDocumentException) { rejected = true; }
        t.True(rejected, "Reject marks whose B fractions run backwards");
        CheckPair(t, old.Document.layers[0], "rejected patch preserves both guide formats");
    });
}
