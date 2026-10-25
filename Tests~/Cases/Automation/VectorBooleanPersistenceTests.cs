using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Entry: VectorBooleanPersistenceTests.Run()
// Support: Framework/TestApi.cs, Framework/Unity/ApiJson.cs.
// Detached documents/FX and in-memory native payload only. No assets, files, windows,
// rendering, shader compilation or global Undo. Reflection targets package code only.
public static class VectorBooleanPersistenceTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Type Serializer = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
    static readonly Type JsonObject = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.Linq.JObject", true);

    static object Json(string value) => JsonObject.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { value });
    static object Invoke(Type type, object instance, string name, params object[] args)
    {
        var method = type.GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null ||
                (p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType).IsInstanceOfType(args[i])).All(v => v));
        try { return method.Invoke(instance, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    static object Call(Type type, string name, params object[] args) => Invoke(type, null, name, args);
    static void Normalize(WhimTexDocument document) => Invoke(typeof(WhimTexDocument), document, "NormalizeModel");
    static ProjectiveMatrix CanvasMatrix(WhimTexDocument document, Layer layer) =>
        ((TextureTransform)Invoke(typeof(WhimTexDocument), document, "GetCanvasTransform", layer)).ToMatrix(document.width, document.height);
    static void SameMatrix(TestContext t, ProjectiveMatrix expected, ProjectiveMatrix actual, string label)
    {
        foreach (var field in typeof(ProjectiveMatrix).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (field.FieldType == typeof(double))
                t.Near((double)field.GetValue(expected), (double)field.GetValue(actual), 1e-10, label + " " + field.Name);
    }
    static ApiJson Snapshot(WhimTexDocument document) => ApiJson.FromObject(Call(typeof(WhimTexApi), "Snapshot", document, null));
    static Layer Apply(WhimTexDocument document, Dictionary<string, Layer> aliases, string operation, bool execute = false) =>
        (Layer)Call(typeof(WhimTexApi), "ApplyOperation", document, Json(operation), aliases, execute, null, null);
    static string Full(WhimTexDocument document) => WhimTexDocumentJson.Write(document,
        new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }).Json;

    static WhimTexDocument NewDocument(List<Object> owned)
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave; document.width = 96; document.height = 80;
        owned.Add(document); return document;
    }
    static IEnumerable<Layer> Tree(IEnumerable<Layer> layers)
    {
        foreach (var layer in layers)
        {
            yield return layer;
            if (layer.children != null) foreach (var child in Tree(layer.children)) yield return child;
        }
    }
    static VectorContour Rectangle(float min, float max) => new VectorContour
    {
        closed = true,
        points = new List<VectorPoint> { new VectorPoint(new Vector2(min, min)), new VectorPoint(new Vector2(max, min)),
            new VectorPoint(new Vector2(max, max)), new VectorPoint(new Vector2(min, max)) }
    };
    static VectorShape StyledFigure(string name) => new VectorShape
    {
        name = name, fillMode = VectorFillMode.None, fillColor = new Color(1.75f, -.25f, .3f, .4f),
        gradientStart = new Vector2(-.25f, .125f), gradientEnd = new Vector2(.75f, 1.25f),
        fillRule = VectorFillRule.EvenOdd, stroke = false, strokeColor = new Color(.1f, 1.4f, .7f, .6f),
        strokeWidth = 7.25f, strokePosition = VectorStrokePosition.Outside, join = VectorStrokeJoin.Bevel,
        cap = VectorStrokeCap.Round, miterLimit = 6.5f, dashPatternRelative = true,
        dashPattern = new List<float> { .1f, 2f, 1.5f }, dashOffset = -2.25f
    };
    static ShaderFX Effect(List<Object> owned)
    {
        var effect = ScriptableObject.CreateInstance<ShaderFX>(); owned.Add(effect);
        effect.hideFlags = HideFlags.HideAndDontSave; effect.name = "Retained child appearance";
        typeof(ShaderFX).GetField("code", Flags).SetValue(effect, "float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return color; }");
        typeof(ShaderFX).GetField("active", Flags).SetValue(effect, false);
        return effect;
    }
    static WhimTexDocument Fixture(List<Object> owned)
    {
        var document = NewDocument(owned);
        var common = StyledFigure("Common style"); common.fillMode = VectorFillMode.AngularGradient; common.stroke = true;
        var group = new Layer(new BooleanGroupLayerBehaviour { style = common,
            referenceSize = new Vector2(96, 80), edgeMode = VectorEdgeMode.Step, appearance = BooleanGroupAppearance.Layers }) { layerName = "Boolean root", opacity = .8f };
        group.transform.positionF = new Vector2(3, -2);
        var shape = new Layer(new ShapeLayerBehaviour
        {
            kind = ShapeLayerBehaviour.ShapeKind.Rectangle, fill = false, fillMode = VectorFillMode.RadialGradient,
            fillColor = new Color(2, .3f, -.2f, .25f), gradientStart = new Vector2(-.5f, .125f), gradientEnd = new Vector2(.8f, 1.25f),
            stroke = false, strokeWidth = 9, strokeColor = new Color(.6f, .3f, 1.2f, .7f),
            strokeJoin = VectorStrokeJoin.Bevel, strokeCap = VectorStrokeCap.Round,
            miterLimit = 7, dashPattern = new List<float> { .1f, 2, 3 }, dashOffset = -3
        }) { layerName = "Base style retained", opacity = .35f, blendMode = BlendMode.Multiply };
        shape.transform.positionF = new Vector2(-4, 2);
        var figure = StyledFigure("Compound child");
        figure.contours.Add(Rectangle(0, 1)); figure.contours.Add(Rectangle(.25f, .75f));
        var vector = new Layer(new VectorLayerBehaviour
        {
            referenceSize = new Vector2(96, 80), shapes = new List<VectorShape> { figure }, edgeMode = VectorEdgeMode.Step,
            brushDraft = new VectorBrushDraft { width = 2, height = 2, coverage = new byte[] { 0, 37, 128, 255 },
                shapeId = figure.id, tolerance = 1.25f, expansion = -2, frame = new Rect(-.25f, .125f, 1.5f, .75f) }
        }) { layerName = "Vector subtract", booleanOperation = VectorBooleanOperation.Subtract, opacity = .45f, clippingMask = true };
        vector.transform.positionF = new Vector2(6, 4); vector.fx.Add(Effect(owned));
        var nested = new Layer(new BooleanGroupLayerBehaviour { style = StyledFigure("Nested retained style") })
            { layerName = "Nested XOR", booleanOperation = VectorBooleanOperation.Xor };
        nested.children.Add(new Layer(new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Ellipse }));
        group.children.Add(nested); group.children.Add(vector); group.children.Add(shape);
        document.layers.Add(group);
        Normalize(document); // Establish the geometry frame before forming the roundtrip baseline.
        ((BooleanGroupLayerBehaviour)group.Behaviour).Validate();
        return document;
    }

    static string ShapeStyle(ShapeLayerBehaviour shape) => Call(typeof(WhimTexApi), "ShapeSnapshot", shape).ToString();
    static void SameLayer(TestContext t, Layer expected, Layer actual, string label)
    {
        t.Equal(expected.Id, actual.Id, label + " layer ID");
        t.Equal(expected.layerName, actual.layerName, label + " name");
        t.Equal(expected.enabled, actual.enabled, label + " visibility");
        t.Equal(expected.booleanOperation, actual.booleanOperation, label + " operation");
        t.Equal(expected.opacity, actual.opacity, label + " retained opacity");
        t.Equal(expected.blendMode, actual.blendMode, label + " retained blending");
        t.Equal(expected.clippingMask, actual.clippingMask, label + " retained clipping");
        t.Equal(expected.transform, actual.transform, label + " fitted transform");
        t.Equal(expected.Behaviour.GetType(), actual.Behaviour.GetType(), label + " behavior type");
        if (expected.Behaviour is BooleanGroupLayerBehaviour a)
        {
            var b = (BooleanGroupLayerBehaviour)actual.Behaviour;
            b.Validate();
            t.True((bool)typeof(Layer).GetProperty("IsGroup",Flags).GetValue(actual), label + " group shell");
            t.Equal(GroupCompositing.Isolated, actual.compositing, label + " isolated");
            t.Equal(a.referenceSize, b.referenceSize, label + " reference size");
            t.Equal(a.edgeMode, b.edgeMode, label + " AA mode");
            t.Equal(a.appearance, b.appearance, label + " retained appearance source");
            t.Equal(JsonUtility.ToJson(a.style), JsonUtility.ToJson(b.style), label + " complete common style");
            t.True(!ReferenceEquals(a.style, b.style) && !ReferenceEquals(a.style.gradient, b.style.gradient), label + " independent common style");
        }
        if (expected.Behaviour is ShapeLayerBehaviour sa)
            t.Equal(ShapeStyle(sa), ShapeStyle((ShapeLayerBehaviour)actual.Behaviour), label + " complete inactive Shape style");
        if (expected.Behaviour is VectorLayerBehaviour va)
        {
            var vb = (VectorLayerBehaviour)actual.Behaviour;
            t.Equal(1, vb.shapes.Count, label + " one figure");
            t.Equal(2, vb.shapes[0].contours.Count, label + " compound contours stay together");
            t.Equal(JsonUtility.ToJson(va), JsonUtility.ToJson(vb), label + " vector style, geometry, frame and mask");
            t.True(!ReferenceEquals(va.shapes[0], vb.shapes[0]) && !ReferenceEquals(va.brushDraft.coverage, vb.brushDraft.coverage), label + " independent source data");
        }
        t.Equal(expected.fx.Count, actual.fx.Count, label + " retained FX count");
        for (int i = 0; i < expected.fx.Count; i++)
        {
            var af = (ShaderFX)expected.fx[i]; var bf = (ShaderFX)actual.fx[i];
            t.Equal(af.Active, bf.Active, label + " retained FX enabled state");
            t.Equal(((string)typeof(ShaderFX).GetField("code", Flags).GetValue(af)).TrimEnd(),
                ((string)typeof(ShaderFX).GetField("code", Flags).GetValue(bf)).TrimEnd(), label + " retained FX source (canonical final newline)");
            t.True(!ReferenceEquals(af, bf), label + " independent FX");
        }
        int children = expected.children?.Count ?? 0;
        t.Equal(children, actual.children?.Count ?? 0, label + " child count/order");
        for (int i = 0; i < children; i++) SameLayer(t, expected.children[i], actual.children[i], label + "/" + i);
    }
    static void Reject(TestContext t, Action action, string message)
    {
        Exception failure = null;
        try { action(); }
        catch (Exception error) when (error is WhimTexDocumentException || error is ArgumentException ||
            error is InvalidOperationException || error.GetType().Name == "WhimTexApiException") { failure = error; }
        t.True(failure != null, message);
    }
    static void RejectUnchanged(TestContext t, WhimTexDocument document, Action action, string label)
    {
        string before = Full(document);
        Reject(t, action, label);
        t.Equal(before, Full(document), label + " leaves model unchanged");
    }

    static void JsonAndNative(TestContext t, List<Object> owned)
    {
        var document = Fixture(owned);
        var operand = document.layers[0].children[1];
        foreach (VectorBooleanOperation operation in Enum.GetValues(typeof(VectorBooleanOperation)))
        foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
        {
            operand.booleanOperation = operation;
            var options = new WhimTexJsonWriteOptions { Mode = mode };
            using var read = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document, options).Json, false);
            SameLayer(t, document.layers[0], read.Document.layers[0], operation + " JSON " + mode);
            using var fragment = WhimTexDocumentJson.Read(WhimTexDocumentJson.WriteLayers(document, document.layers, options).Json, false);
            SameLayer(t, document.layers[0], fragment.Document.layers[0], operation + " fragment " + mode);
        }
        using var container = new WhimTexDocumentContainer();
        byte[] bytes = (byte[])Call(Serializer, "Serialize", document, container);
        var result = Call(Serializer, "Deserialize", bytes, container, typeof(WhimTexDocument), null, false);
        var restored = (WhimTexDocument)result.GetType().GetProperty("Model", Flags).GetValue(result);
        owned.Add(restored);
        foreach (var layer in Tree(restored.layers)) foreach (var effect in layer.fx) if (effect != null) owned.Add(effect);
        foreach (string field in new[] { "SkippedFields", "MissingTypes", "UnresolvedReferences" })
            t.Equal(0, ((IReadOnlyList<string>)result.GetType().GetProperty(field, Flags).GetValue(result)).Count, "Native " + field);
        Normalize(restored);
        SameLayer(t, document.layers[0], restored.layers[0], "Native tagged payload");
        using var fromNative = WhimTexDocumentJson.Read(Full(restored), false);
        SameLayer(t, document.layers[0], fromNative.Document.layers[0], "Native to JSON");

        var json = ApiJson.Parse(Full(document));
        json["layers"][0]["compositing"] = "PassThrough";
        Reject(t, () => { using var _ = WhimTexDocumentJson.Read(json.ToString(), false); }, "JSON rejects PassThrough");
        json = ApiJson.Parse(Full(document)); json["layers"][0]["children"][1]["booleanOperation"] = "InvalidOperation";
        Reject(t, () => { using var _ = WhimTexDocumentJson.Read(json.ToString(), false); }, "JSON rejects unknown operation");
        json = ApiJson.Parse(Full(document));
        json["layers"][0]["children"][1]["behaviour"]["shapes"][0]["contours"][0]["closed"] = false;
        Reject(t, () => { using var _ = WhimTexDocumentJson.Read(json.ToString(), false); }, "JSON rejects mixed closed/open operand");
        json = ApiJson.Parse(Full(document));
        json["layers"][0]["children"][1]["behaviour"] = ApiJson.Parse("{\"$type\":\"ColorFillLayerBehaviour\"}");
        Reject(t, () => { using var _ = WhimTexDocumentJson.Read(json.ToString(), false); }, "JSON rejects raster operand");
    }

    static void Defaults(TestContext t)
    {
        const string minimal = "{\"format\":\"whimtex.document\",\"version\":3,\"layers\":[{\"id\":\"default-group\",\"behaviour\":{\"$type\":\"BooleanGroupLayerBehaviour\"},\"children\":[{\"id\":\"base\",\"behaviour\":{\"$type\":\"ShapeLayerBehaviour\"}}]}]}";
        using var read = WhimTexDocumentJson.Read(minimal, false);
        var layer = read.Document.layers[0]; var group = (BooleanGroupLayerBehaviour)layer.Behaviour;
        t.True((bool)typeof(Layer).GetProperty("IsGroup",Flags).GetValue(layer), "Omitted group flag derives from behavior");
        t.Equal(GroupCompositing.Isolated, layer.compositing, "Omitted compositing is Isolated");
        t.Equal(VectorBooleanOperation.Union, layer.children[0].booleanOperation, "Omitted operation is Union");
        t.Equal(VectorFillMode.Solid, group.style.fillMode, "Default common fill");
        t.Equal(0, group.style.contours.Count, "Default style has no operand geometry");
        t.Equal(new Vector2(256, 256), group.referenceSize, "Document default reference size");
        t.Equal(default(VectorEdgeMode), group.edgeMode, "Default AA");
        var description = ApiJson.Parse(WhimTexApi.Describe());
        t.True((bool)description["success"], "describe succeeds with detached Boolean defaults");
        t.True(description["layerTypes"].Any(v => (string)v == "booleanGroup"), "booleanGroup discoverable");
        t.Equal("Union,Subtract,Intersect,Xor,Combine", string.Join(",", description["booleanOperations"].Select(v => (string)v)), "Operation discovery");
        t.Equal(BooleanGroupAppearance.Group, group.appearance, "Default common appearance");
        t.Equal("Solid", (string)description["booleanGroupDefaults"]["style"]["fillMode"], "Style defaults discoverable");
        t.Equal(0, description["booleanGroupDefaults"]["style"]["contours"].Count, "Defaults do not invent geometry");
        var copy = new BooleanGroupLayerBehaviour();
        Call(typeof(WhimTexDocumentJson), "PatchBooleanGroup", copy, Json(description["booleanGroupDefaults"].ToString()), read.Document);
        t.Equal(group.style.fillMode, copy.style.fillMode, "Default snapshot can be replayed");
        var shell = new Layer(copy); // Must not fail because detached validation acquired an incidental owner.
        t.True(ReferenceEquals(shell.Behaviour, copy), "Detached snapshot/patch remains bindable");
    }

    static void Api(TestContext t, List<Object> owned, bool execute)
    {
        var document = NewDocument(owned); var aliases = new Dictionary<string, Layer>(StringComparer.Ordinal);
        Layer Op(string operation) => Apply(document, aliases, operation, execute);
        var group = Op("{\"op\":\"add\",\"type\":\"booleanGroup\",\"as\":\"g\",\"transform\":{\"position\":[8,-5]},\"settings\":{\"booleanGroup\":{\"edgeMode\":\"Step\"}}}");
        t.Equal(new Vector2(96, 80), ((BooleanGroupLayerBehaviour)group.Behaviour).referenceSize, "API reference follows canvas");
        var shape = Op("{\"op\":\"add\",\"type\":\"shape\",\"as\":\"s\",\"parent\":\"@g\",\"settings\":{\"shape\":{\"stroke\":false,\"strokeWidth\":9,\"strokeCap\":\"Round\",\"dashPattern\":[0.1,2]},\"opacity\":0.3}}");
        var vector = Op("{\"op\":\"add\",\"type\":\"vector\",\"as\":\"v\",\"settings\":{\"vector\":{\"shapes\":[{\"id\":\"one\",\"contours\":[{\"closed\":true,\"points\":[{\"position\":[0,0]},{\"position\":[1,0]},{\"position\":[1,1]},{\"position\":[0,1]}]}]}]}}}");
        string vectorBefore = JsonUtility.ToJson(vector.Behaviour);
        var transform = CanvasMatrix(document, vector);
        Op("{\"op\":\"move\",\"layer\":\"@v\",\"parent\":\"@g\",\"index\":0}");
        t.True(ReferenceEquals(group.children[0], vector) && ReferenceEquals(group.children[1], shape), "API insertion order is top to bottom");
        SameMatrix(t, transform, CanvasMatrix(document, vector), "Moving into group preserves canvas transform");
        var nested = Op("{\"op\":\"add\",\"type\":\"booleanGroup\",\"as\":\"nested\",\"parent\":\"@g\",\"settings\":{\"booleanOperation\":\"Subtract\"}}");
        var nestedShape = Op("{\"op\":\"add\",\"type\":\"shape\",\"parent\":\"@nested\"}");
        t.True(group.children.Contains(nested) && nested.children.Contains(nestedShape), "API nested Boolean operands");
        Op("{\"op\":\"set\",\"layer\":\"@g\",\"settings\":{\"booleanGroup\":{\"style\":{\"fillMode\":\"LinearGradient\",\"stroke\":true,\"strokeWidth\":4,\"join\":\"Round\"}}}}");
        t.Equal(VectorFillMode.LinearGradient, ((BooleanGroupLayerBehaviour)group.Behaviour).style.fillMode, "API updates common fill");
        t.Equal(4f, ((BooleanGroupLayerBehaviour)group.Behaviour).style.strokeWidth, "API updates common stroke");
        Op("{\"op\":\"set\",\"layer\":\"@g\",\"settings\":{\"booleanGroup\":{\"appearance\":\"Layers\"}}}");
        t.Equal(BooleanGroupAppearance.Layers, ((BooleanGroupLayerBehaviour)group.Behaviour).appearance, "API selects retained child appearance");
        foreach (var operation in Enum.GetNames(typeof(VectorBooleanOperation)))
        {
            Op("{\"op\":\"set\",\"layer\":\"@v\",\"settings\":{\"booleanOperation\":\"" + operation + "\"}}");
            Call(typeof(WhimTexApi), "ValidateTargets", document, null);
            var snapshot = Snapshot(document);
            var entry = snapshot["layers"].Single(v => (string)v["id"] == vector.Id);
            t.Equal(operation, (string)entry["settings"]["booleanOperation"], "Snapshot operation " + operation);
            t.Equal(group.Id, (string)entry["parent"], "Snapshot parent");
            var gs = snapshot["layers"].Single(v => (string)v["id"] == group.Id);
            t.Equal("booleanGroup", (string)gs["type"], "Snapshot API type");
            t.Equal("Step", (string)gs["settings"]["booleanGroup"]["edgeMode"], "Snapshot common settings");
            t.Equal("Layers", (string)gs["settings"]["booleanGroup"]["appearance"], "Snapshot appearance choice");
            t.Equal("LinearGradient", (string)gs["settings"]["booleanGroup"]["style"]["fillMode"], "Snapshot common fill");
        }
        RejectUnchanged(t, document, () => Op("{\"op\":\"add\",\"type\":\"color\",\"parent\":\"@g\"}"), "Reject raster insertion before mutation");
        const string open = "{\"shapes\":[{\"contours\":[{\"closed\":false,\"points\":[{\"position\":[0,0]},{\"position\":[1,1]}]}]}]}";
        RejectUnchanged(t, document, () => Op("{\"op\":\"add\",\"type\":\"vector\",\"parent\":\"@g\",\"settings\":{\"vector\":" + open + "}}"), "Reject open insertion before mutation");
        Op("{\"op\":\"add\",\"type\":\"color\",\"as\":\"raster\"}");
        Op("{\"op\":\"add\",\"type\":\"vector\",\"as\":\"open\",\"settings\":{\"vector\":" + open + "}}");
        foreach (string alias in new[] { "raster", "open" })
            RejectUnchanged(t, document, () => Op("{\"op\":\"move\",\"layer\":\"@" + alias + "\",\"parent\":\"@g\"}"), "Reject moving " + alias + " before mutation");
        RejectUnchanged(t, document, () => Op("{\"op\":\"set\",\"layer\":\"@v\",\"settings\":{\"vector\":{\"shapes\":[{},{}]}}}"), "New API authoring rejects multiple figures");
        RejectUnchanged(t, document, () => Op("{\"op\":\"set\",\"layer\":\"@g\",\"settings\":{\"booleanGroup\":{\"style\":{\"contours\":[{}]}}}}"), "Reject geometry in result style");
        var commonBefore = JsonUtility.ToJson(((BooleanGroupLayerBehaviour)group.Behaviour).style);
        Op("{\"op\":\"set\",\"layer\":\"@s\",\"settings\":{\"shape\":{\"fillColor\":[0.2,0.5,0.8,1]}}}");
        t.Equal(commonBefore, JsonUtility.ToJson(((BooleanGroupLayerBehaviour)group.Behaviour).style), "No continuous inheritance from child style");
        t.Equal(vectorBefore, JsonUtility.ToJson(vector.Behaviour), "Group operations preserve source figure/style");
        Op("{\"op\":\"move\",\"layer\":\"@v\",\"index\":0}");
        t.True(document.layers.Contains(vector) && !group.children.Contains(vector), "Extraction restores independent layer");
        t.Equal(vectorBefore, JsonUtility.ToJson(vector.Behaviour), "Extraction retains source style");
        SameMatrix(t, transform, CanvasMatrix(document, vector), "Extraction preserves canvas transform");
        var retained = (ShapeLayerBehaviour)shape.Behaviour;
        t.Equal(9f, retained.strokeWidth, "Inactive child stroke width retained");
        t.True(!retained.stroke && retained.strokeCap == VectorStrokeCap.Round &&
            retained.dashPattern.SequenceEqual(new[] { .1f, 2f }), "Inactive child stroke gate, cap and dash pattern retained");
        t.Equal(.3f, shape.opacity, "Child opacity retained separately from common style");
        Call(typeof(WhimTexApi), "ValidateTargets", document, null);
    }

    public static string Run() => TestContext.Run("Boolean JSON/native persistence, API operations, defaults and retained appearance", t =>
    {
        var owned = new List<Object>();
        try { JsonAndNative(t, owned); Defaults(t); Api(t, owned, false); Api(t, owned, true); }
        finally
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        }
    });
}
