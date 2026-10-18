using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Support: Framework/TestApi.cs. Detached memory and TIFF bytes; no files or global Undo.
public static class VectorPersistenceTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Type Serializer = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
    static readonly Type Carrier = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexTiffCarrier", true);
    static object Call(Type type, string name, params object[] args)
    {
        var method = type.GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null ||
                (p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType).IsInstanceOfType(args[i])).All(match => match));
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    static VectorLayerBehaviour Sample()
    {
        var point = new VectorPoint(new Vector2(-.125f, .875f))
        { inTangent = new Vector2(-.21f, .04f), outTangent = new Vector2(.18f, -.09f), tangentMode = VectorTangentMode.Aligned };
        var contour = new VectorContour { closed = false, points = new List<VectorPoint>
        { point, new VectorPoint(new Vector2(.4f, .6f)), new VectorPoint(new Vector2(1.25f, -.2f)) } };
        var shape = new VectorShape
        {
            name = "Vector / 曲线", visible = false, contours = new List<VectorContour> { contour },
            fillMode = VectorFillMode.None, fillColor = new Color(2.5f, -.2f, .7f, .4f),
            gradientStart = new Vector2(.1f, .2f), gradientEnd = new Vector2(.75f, .85f),
            fillRule = VectorFillRule.NonZero, stroke = false, strokeColor = new Color(.3f, 1.5f, .4f, .65f),
            strokeWidth = 7.25f, strokePosition = VectorStrokePosition.Outside, join = VectorStrokeJoin.Bevel,
            cap = VectorStrokeCap.Square, miterLimit = 8.5f, dashPatternRelative = true,
            dashPattern = new List<float> { 3.25f, 1.5f, 2.75f }, dashOffset = -1.25f
        };
        return new VectorLayerBehaviour
        {
            referenceSize = new Vector2(320, 180), edgeMode = VectorEdgeMode.Step,
            feather = 3.75f, featherPosition = VectorFeatherPosition.Outside,
            shapes = new List<VectorShape> { shape },
            brushDraft = new VectorBrushDraft { width = 3, height = 2, shapeId = shape.id,
                coverage = new byte[] { 0, 1, 37, 128, 254, 255 }, tolerance = 2.25f, expansion = -3.5f }
        };
    }
    static ShapeLayerBehaviour SampleShapeStroke() => new ShapeLayerBehaviour
    {
        kind = ShapeLayerBehaviour.ShapeKind.Line, stroke = false,
        strokeJoin = VectorStrokeJoin.Bevel, strokeCap = VectorStrokeCap.Square,
        lineCap = ShapeLayerBehaviour.LineCap.Butt, miterLimit = 7.5f,
        dashPattern = new List<float> { .25f, 2.5f, 1.25f }, dashOffset = -4.25f
    };
    static void SameShapeStroke(TestContext t, ShapeLayerBehaviour expected, ShapeLayerBehaviour actual, string label)
    {
        t.Equal(expected.stroke, actual.stroke, label + " enabled state");
        t.Equal(expected.strokeJoin, actual.strokeJoin, label + " outline join");
        t.Equal(expected.strokeCap, actual.strokeCap, label + " outline cap");
        t.Equal(expected.lineCap, actual.lineCap, label + " independent body cap");
        t.Equal(expected.strokePosition, actual.strokePosition, label + " Shape alignment ordering");
        t.Equal(expected.miterLimit, actual.miterLimit, label + " miter ratio");
        t.Equal(expected.dashOffset, actual.dashOffset, label + " document-pixel dash offset");
        t.True(actual.dashPattern != null && expected.dashPattern.SequenceEqual(actual.dashPattern),
            label + " retains the width-relative pattern, including its odd length");
        t.True(!ReferenceEquals(expected.dashPattern, actual.dashPattern), label + " owns its dash list");
    }
    static void Same(TestContext t, VectorLayerBehaviour expected, VectorLayerBehaviour actual, string label)
    {
        t.Equal(JsonUtility.ToJson(expected), JsonUtility.ToJson(actual), label + " preserves every field, inactive style, tangent and draft byte");
        t.True(!ReferenceEquals(expected, actual) && !ReferenceEquals(expected.shapes[0], actual.shapes[0]) &&
            !ReferenceEquals(expected.brushDraft.coverage, actual.brushDraft.coverage), label + " owns independent model data");
    }
    static WhimTexDocument Decode(byte[] bytes, WhimTexDocumentContainer container, List<Object> owned, TestContext t)
    {
        var result = Call(Serializer, "Deserialize", bytes, container, typeof(WhimTexDocument), null, false);
        foreach (string field in new[] { "SkippedFields", "MissingTypes", "UnresolvedReferences" })
            t.Equal(0, ((IReadOnlyList<string>)result.GetType().GetProperty(field, Flags).GetValue(result)).Count, "No " + field);
        var document = (WhimTexDocument)result.GetType().GetProperty("Model", Flags).GetValue(result);
        owned.Add(document); return document;
    }
    static void Reject(TestContext t, Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (WhimTexDocumentException) { rejected = true; }
        t.True(rejected, message);
    }

    // Entry: VectorPersistenceTests.RunFeatherApi(). Same support as Run; no render or global Undo.
    public static string RunFeatherApi() => TestContext.Run("Vector Feather API: partial patches and atomic invalid-value rejection", t =>
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            document.width = 80; document.height = 64;
            var vector = Sample();
            var layer = new Layer(vector); document.layers.Add(layer);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Flags).Invoke(document, null);
            var aliases = new Dictionary<string, Layer> { { "figure", layer } };
            var jsonObject = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.Linq.JObject", true);
            object Operation(string patch) => Call(jsonObject, "Parse",
                "{\"op\":\"set\",\"layer\":\"@figure\",\"settings\":{\"vector\":" + patch + "}}");
            void Apply(object operation) => Call(typeof(WhimTexApi), "ApplyOperation", document, operation, aliases, false, null, null);
            void Patch(string patch) => Apply(Operation(patch));
            string Full() => WhimTexDocumentJson.Write(document,
                new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }).Json;
            T SnapshotValue<T>(string field)
            {
                object snapshot = Call(typeof(WhimTexDocumentJson), "VectorSnapshot", vector, document);
                object token = snapshot.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(snapshot, new object[] { field });
                t.True(token != null, "Vector API snapshot contains " + field);
                return (T)token.GetType().GetMethod("ToObject", new[] { typeof(Type) }).Invoke(token, new object[] { typeof(T) });
            }
            var referenceSize = vector.referenceSize;
            string originalShape = JsonUtility.ToJson(vector.shapes[0]);
            string originalDraft = JsonUtility.ToJson(vector.brushDraft);
            void SameUnrelated(string label)
            {
                t.True(ReferenceEquals(vector, layer.Behaviour), label + " keeps the behaviour shell");
                t.Equal(referenceSize, vector.referenceSize, label + " keeps referenceSize");
                t.Equal(originalShape, JsonUtility.ToJson(vector.shapes[0]), label + " keeps geometry, IDs and inactive paint");
                t.Equal(originalDraft, JsonUtility.ToJson(vector.brushDraft), label + " keeps the original mask and rebuild settings");
            }

            Patch("{\"feather\":8.25}");
            t.Equal(8.25f, vector.feather, "Width-only patch accepts a fractional reference-pixel width");
            t.Equal(VectorFeatherPosition.Outside, vector.featherPosition, "Width-only patch retains the omitted position");
            t.Equal(VectorEdgeMode.Step, vector.edgeMode, "Width-only patch retains Step");
            t.Equal(8.25f, SnapshotValue<float>("feather"), "Snapshot exposes the stored width even under Step");
            t.Equal("Outside", SnapshotValue<string>("featherPosition"), "Snapshot exposes the named position even under Step");
            SameUnrelated("Width-only patch");

            foreach (VectorFeatherPosition position in Enum.GetValues(typeof(VectorFeatherPosition)))
            {
                Patch("{\"featherPosition\":\"" + position + "\"}");
                t.Equal(position, vector.featherPosition, "Position-only patch accepts " + position);
                t.Equal(8.25f, vector.feather, "Position-only patch retains the omitted width");
                t.Equal(VectorEdgeMode.Step, vector.edgeMode, "Position-only patch retains Step");
                SameUnrelated("Position-only patch " + position);
            }
            foreach (float width in new[] { 0f, .125f, 8192f })
            {
                Patch("{\"feather\":" + width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                t.Equal(width, vector.feather, "Finite width accepted, including exact endpoints: " + width);
                t.Equal(VectorFeatherPosition.Centered, vector.featherPosition, "Width patch retains the selected Centered position");
                SameUnrelated("Width patch " + width);
            }
            Patch("{\"edgeMode\":\"Antialiased\"}");
            t.Equal(8192f, vector.feather, "Leaving Step retains the maximum width");
            t.Equal(VectorFeatherPosition.Centered, vector.featherPosition, "Leaving Step retains the position");
            Patch("{\"edgeMode\":\"Step\"}");
            t.Equal(8192f, vector.feather, "Entering Step does not clear width");
            t.Equal(VectorFeatherPosition.Centered, vector.featherPosition, "Entering Step does not clear position");
            string beforeEmpty = Full(); Patch("{}");
            t.Equal(beforeEmpty, Full(), "Empty partial patch preserves the complete model");

            var invalid = new[]
            {
                (label: "NaN width", fields: "\"feather\":NaN,\"featherPosition\":\"Inside\""),
                (label: "Infinite width", fields: "\"feather\":Infinity,\"featherPosition\":\"Inside\""),
                (label: "Negative width", fields: "\"feather\":-0.125,\"featherPosition\":\"Inside\""),
                (label: "Width above 8192", fields: "\"feather\":8192.125,\"featherPosition\":\"Inside\""),
                (label: "Unknown position", fields: "\"feather\":1.25,\"featherPosition\":\"Unknown\""),
                (label: "Numeric position", fields: "\"feather\":1.25,\"featherPosition\":3")
            };
            foreach (var value in invalid)
            {
                // Parse outside Reject: a parser error must not masquerade as validation of numeric NaN/Infinity.
                object operation = Operation("{\"referenceSize\":[411,217],\"edgeMode\":\"Antialiased\"," + value.fields + "}");
                string before = Full();
                var shapes = vector.shapes; var draft = vector.brushDraft; var coverage = draft.coverage;
                Reject(t, () => Apply(operation), value.label + " is rejected by the API");
                t.Equal(before, Full(), value.label + " rejects the entire Vector patch without changing any serialized field");
                t.True(ReferenceEquals(shapes, vector.shapes) && ReferenceEquals(draft, vector.brushDraft) &&
                    ReferenceEquals(coverage, vector.brushDraft.coverage), value.label + " leaves model references intact");
                SameUnrelated(value.label);
            }
        }
        finally { Object.DestroyImmediate(document); }
    });

    public static string Run() => TestContext.Run("Shape/Vector persistence: JSON modes, fragments, TIFF bytes and Unity snapshot", t =>
    {
        var owned = new List<Object>();
        try
        {
            var document = ScriptableObject.CreateInstance<WhimTexDocument>(); owned.Add(document);
            document.hideFlags = HideFlags.HideAndDontSave; document.width = 8; document.height = 6;
            var vector = Sample(); var layer = new Layer(vector); document.layers.Add(layer);
            var shape = SampleShapeStroke(); document.layers.Add(new Layer(shape));
            var defaultShape = new ShapeLayerBehaviour(); document.layers.Add(new Layer(defaultShape));
            layer.transform.positionF = new Vector2(3, -2); layer.opacity = .65f;
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Flags).Invoke(document, null);
            t.True(!string.IsNullOrEmpty(layer.Id), "Fixture normalizes layer identity before serialization");
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                var options = new WhimTexJsonWriteOptions { Mode = mode };
                var expectedShape = shape; // Inactive source appearance is retained for Boolean operands too.
                string json = WhimTexDocumentJson.Write(document, options).Json;
                t.True(json.Contains("AAElgP7/"), "Original mask bytes use base64 in " + mode);
                using var read = WhimTexDocumentJson.Read(json, false);
                Same(t, vector, (VectorLayerBehaviour)read.Document.layers[0].Behaviour, "JSON " + mode);
                SameShapeStroke(t, expectedShape, (ShapeLayerBehaviour)read.Document.layers[1].Behaviour, "Inactive Shape JSON " + mode);
                SameShapeStroke(t, defaultShape, (ShapeLayerBehaviour)read.Document.layers[2].Behaviour, "Default Shape JSON " + mode);
                t.Equal(layer.Id, read.Document.layers[0].Id, "Layer identity survives " + mode);
                using var fragment = WhimTexDocumentJson.Read(WhimTexDocumentJson.WriteLayers(document, document.layers, options).Json, false);
                Same(t, vector, (VectorLayerBehaviour)fragment.Document.layers[0].Behaviour, "Clipboard fragment " + mode);
                SameShapeStroke(t, expectedShape, (ShapeLayerBehaviour)fragment.Document.layers[1].Behaviour, "Inactive Shape clipboard " + mode);
                SameShapeStroke(t, defaultShape, (ShapeLayerBehaviour)fragment.Document.layers[2].Behaviour, "Default Shape clipboard " + mode);
                shape.stroke = true;
                try
                {
                    using var active = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document, options).Json, false);
                    SameShapeStroke(t, shape, (ShapeLayerBehaviour)active.Document.layers[1].Behaviour, "Active Shape JSON " + mode);
                    using var activeFragment = WhimTexDocumentJson.Read(WhimTexDocumentJson.WriteLayers(document, document.layers, options).Json, false);
                    SameShapeStroke(t, shape, (ShapeLayerBehaviour)activeFragment.Document.layers[1].Behaviour, "Active Shape clipboard " + mode);
                }
                finally { shape.stroke = false; }
            }
            string vectorSnapshot = JsonUtility.ToJson(vector);
            string unitySnapshot = EditorJsonUtility.ToJson(document);
            vector.referenceSize = new Vector2(91, 73);
            vector.feather = 0; vector.featherPosition = VectorFeatherPosition.Inside;
            vector.shapes[0].name = "Changed after snapshot";
            vector.shapes[0].contours[0].points[0].outTangent = Vector2.zero;
            vector.brushDraft.coverage[0] = 255;
            vector.brushDraft.tolerance = 8;
            vector.brushDraft.expansion = 4;
            shape.strokeJoin = VectorStrokeJoin.Miter; shape.strokeCap = VectorStrokeCap.Round;
            shape.lineCap = ShapeLayerBehaviour.LineCap.Square; shape.miterLimit = 12;
            shape.dashPattern[0] = 9; shape.dashOffset = 17;
            t.True(vectorSnapshot != JsonUtility.ToJson(vector), "Fixture changes model values after the Unity snapshot");
            EditorJsonUtility.FromJsonOverwrite(unitySnapshot, document);
            vector = (VectorLayerBehaviour)document.layers[0].Behaviour;
            shape = (ShapeLayerBehaviour)document.layers[1].Behaviour;
            SameShapeStroke(t, SampleShapeStroke(), shape, "Shape Unity snapshot");
            // Overwrite may reuse managed references; restoration promises values, not new identities.
            t.Equal(vectorSnapshot, JsonUtility.ToJson(vector), "Unity Undo serialization substrate restores every field and original draft byte");

            using var container = new WhimTexDocumentContainer();
            byte[] payload = (byte[])Call(Serializer, "Serialize", document, container);
            container.Set(WhimTexDocumentContainer.DocumentBlock, payload);
            var native = Decode(payload, container, owned, t);
            Same(t, vector, (VectorLayerBehaviour)native.layers[0].Behaviour, "Native tagged payload");
            SameShapeStroke(t, shape, (ShapeLayerBehaviour)native.layers[1].Behaviour, "Shape native tagged payload");
            SameShapeStroke(t, defaultShape, (ShapeLayerBehaviour)native.layers[2].Behaviour, "Default Shape native tagged payload");
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true); owned.Add(image);
            image.SetPixels(new[] { Color.clear, Color.red, Color.green, Color.blue }); image.Apply();
            byte[] tiff = (byte[])Call(Carrier, "Write", container, image, (bool?)false);
            object[] args = { tiff, null, null };
            t.True((bool)Call(Carrier, "TryRead", args), "TIFF container extracts: " + args[2]);
            using var restored = WhimTexDocumentContainer.Parse((byte[])args[1]);
            var reopened = Decode(restored.Get(WhimTexDocumentContainer.DocumentBlock), restored, owned, t);
            Same(t, vector, (VectorLayerBehaviour)reopened.layers[0].Behaviour, "TIFF carrier");
            SameShapeStroke(t, shape, (ShapeLayerBehaviour)reopened.layers[1].Behaviour, "Shape TIFF carrier");
            SameShapeStroke(t, defaultShape, (ShapeLayerBehaviour)reopened.layers[2].Behaviour, "Default Shape TIFF carrier");

            string valid = WhimTexDocumentJson.Write(document).Json;
            Reject(t, () => { using var _ = WhimTexDocumentJson.Read(valid.Replace("AAElgP7/", "AA=="), false); }, "Wrong mask length is rejected");
            Reject(t, () => { using var _ = WhimTexDocumentJson.Read(valid.Replace("AAElgP7/", "invalid!"), false); }, "Invalid base64 is rejected");

            var maximum = (VectorLayerBehaviour)document.layers[0].Behaviour;
            maximum.brushDraft.width = maximum.brushDraft.height = 1024;
            maximum.brushDraft.coverage = new byte[1024 * 1024];
            maximum.brushDraft.coverage[0] = 37; maximum.brushDraft.coverage[maximum.brushDraft.coverage.Length - 1] = 251;
            using var largeContainer = new WhimTexDocumentContainer();
            byte[] largePayload = (byte[])Call(Serializer, "Serialize", document, largeContainer);
            var large = Decode(largePayload, largeContainer, owned, t);
            Same(t, maximum, (VectorLayerBehaviour)large.layers[0].Behaviour, "Maximum draft bypasses per-byte graph limit");
            using var largeJson = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json, false);
            Same(t, maximum, (VectorLayerBehaviour)largeJson.Document.layers[0].Behaviour, "Maximum draft JSON");

            maximum.brushDraft = null;
            string withoutDraft = JsonUtility.ToJson(maximum);
            t.Equal(withoutDraft, JsonUtility.ToJson(maximum), "Absent draft has deterministic native serialization");
            string emptySnapshot = EditorJsonUtility.ToJson(document);
            maximum.brushDraft = Sample().brushDraft;
            EditorJsonUtility.FromJsonOverwrite(emptySnapshot, document);
            t.True(((VectorLayerBehaviour)document.layers[0].Behaviour).brushDraft == null,
                "Native restoration retains null rather than creating a phantom empty mask");
            using var emptyJson = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json, false);
            t.True(((VectorLayerBehaviour)emptyJson.Document.layers[0].Behaviour).brushDraft == null,
                "JSON retains absence of procedural source");
        }
        finally { for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); }
    });
}
