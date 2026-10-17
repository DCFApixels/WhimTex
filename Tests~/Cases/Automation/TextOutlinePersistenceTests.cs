using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Entry: TextOutlinePersistenceTests.Run (or Api / Native / UnitySnapshot separately).
// Support: Tests~/Framework/TestApi.cs, Tests~/Framework/Unity/ApiJson.cs.
// Idle graphics-capable Editor and one preferred installed static outline font required.
// Public ExecuteJson calls ALWAYS use dryRun:true. Successful commits and atomic
// single-operation failures use the shared API dispatcher on owned detached documents.
// TIFF bytes stay in memory; no windows, files, assets, imports or global Undo calls.
// UnitySnapshot checks pure model data before/after conversion, not UI Undo/Redo or batch rollback.
// Public EditorJsonUtility snapshots are not an oracle for transient Unity object references.
// API/native TIFF cases cover FX identity/data; snapshot fixture FX remain Scope-owned for cleanup.
// Reflection targets package code and canonical public Newtonsoft APIs, never Unity internals.
public static class TextOutlinePersistenceTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Assembly Package = typeof(WhimTexDocument).Assembly;
    static readonly Type Serializer = Package.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
    static readonly Type Carrier = Package.GetType("DCFApixels.WhimTex.WhimTexTiffCarrier", true);
    static readonly Type ObjectID = Package.GetType("DCFApixels.WhimTex.UnityObjectID", true);
    static readonly Type JsonObject = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.Linq.JObject", true);

    static object Invoke(Type type, object instance, string name, params object[] args)
    {
        var method = type.GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null ||
                (p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType).IsInstanceOfType(args[i])).All(v => v));
        try { return method.Invoke(instance, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    static object Call(Type type, string name, params object[] args) => Invoke(type, null, name, args);
    // Box the package's existing adapter, not a second identity wrapper or a narrowed ID.
    static object Id(Object value) => Call(ObjectID, "FromObject", value);
    static void SameNative(TestContext t, object expected, Object actual, string label)
    {
        object actualId = Id(actual);
        t.True(actual != null && expected.Equals(actualId), label +
            "; expected UnityObjectID=" + expected + "; actual UnityObjectID=" + actualId +
            "; actual native object alive=" + (actual != null));
    }
    static void Normalize(WhimTexDocument doc) => Invoke(typeof(WhimTexDocument), doc, "NormalizeModel");
    static object Json(string value) => JsonObject.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { value });
    static void Apply(WhimTexDocument doc, Layer layer, bool execute) => Call(typeof(WhimTexApi), "ApplyOperation", doc,
        Json("{\"op\":\"convertToVector\",\"layer\":\"" + layer.Id + "\"}"), new Dictionary<string, Layer>(), execute, null, null);

    sealed class Scope : IDisposable
    {
        readonly FaceInfo face = FontEngine.GetFaceInfo();
        readonly RenderTexture target = RenderTexture.active;
        readonly bool srgb = GL.sRGBWrite;
        readonly List<object> owned = new List<object>();
        readonly HashSet<object> ownedIds = new HashSet<object>();
        readonly TestContext t;
        internal Scope(TestContext t) { this.t = t; }
        internal T Own<T>(T value) where T : Object
        {
            if (value != null)
            {
                object id = Id(value);
                if (ownedIds.Add(id)) owned.Add(id);
            }
            return value;
        }
        public void Dispose()
        {
            var errors = new List<Exception>();
            for (int i = owned.Count - 1; i >= 0; i--)
                try
                {
                    var value = (Object)Invoke(ObjectID, owned[i], "Resolve");
                    if (value != null) Object.DestroyImmediate(value);
                }
                catch (Exception error) { errors.Add(error); }
            try
            {
                RenderTexture.active = target; GL.sRGBWrite = srgb;
                if (string.IsNullOrEmpty(face.familyName))
                {
                    if (!string.IsNullOrEmpty(FontEngine.GetFaceInfo().familyName))
                        t.Equal(FontEngineError.Success, FontEngine.UnloadFontFace(), "Restore initially unloaded native face through public API");
                    t.True(string.IsNullOrEmpty(FontEngine.GetFaceInfo().familyName), "No native face left loaded");
                }
                else
                {
                    t.Equal(FontEngineError.Success, FontEngine.LoadFontFace(face.familyName, face.styleName, face.pointSize),
                        "Restore caller's native font face through public family/style loader");
                    var restored = FontEngine.GetFaceInfo();
                    t.Equal(face.familyName, restored.familyName, "Native face family restored");
                    t.Equal(face.styleName, restored.styleName, "Native face style restored");
                    t.Near(face.pointSize, restored.pointSize, 0, "Native face point size restored");
                }
            }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException("Text outline test cleanup failed.", errors);
        }
    }

    static string Family()
    {
        string[] names = Font.GetOSInstalledFontNames() ?? Array.Empty<string>();
        foreach (string preferred in new[] { "Arial", "Liberation Sans", "DejaVu Sans", "Helvetica", "Noto Sans" })
            if (names.Any(n => string.Equals(n, preferred, StringComparison.OrdinalIgnoreCase))) return preferred;
        throw new NotSupportedException("Prerequisite: install a supported static Arial, Liberation Sans, DejaVu Sans, Helvetica or Noto Sans face. No font was substituted.");
    }
    static TextLayerBehaviour Text(string family) => new TextLayerBehaviour
    {
        text = "O B é\nText", fontFamily = family, fontSize = 32, maxFontSize = 48,
        edgeMode = TextEdgeMode.Antialiased, casing = TextCasing.SmallCaps,
        spacing = new TextSpacing { character = .08f, word = .12f, line = .15f, paragraph = .2f },
        characterHorizontalScale = 1.2f, layoutOffset = new Vector2(180.25f, -12.5f),
        color = new Color(1.5f, .2f, .7f, .65f)
    };
    static WhimTexDocument Fixture(Scope scope, string family, out Layer operand)
    {
        var doc = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = 320; doc.height = 192;
        var group = new Layer(new BooleanGroupLayerBehaviour { referenceSize = new Vector2(320, 192) }) { layerName = "Owned text Boolean" };
        group.transform.positionF = new Vector2(7, -3); group.transform.rotation = 11;
        operand = new Layer(Text(family)) { layerName = "Retain Text shell", opacity = .37f,
            blendMode = BlendMode.Multiply, clippingMask = true, booleanOperation = VectorBooleanOperation.Subtract };
        operand.transform.positionF = new Vector2(13.25f, -4.5f);
        operand.transform.scaleF = new Vector2(-.8f, 1.3f); operand.transform.rotation = -17;
        operand.transform.tiling = TransformTilingMode.Clip;
        var fx = scope.Own(ScriptableObject.CreateInstance<ShaderFX>());
        fx.hideFlags = HideFlags.HideAndDontSave;
        typeof(ShaderFX).GetField("code", Flags).SetValue(fx, "float4 ApplyFX(float2 uv, float4 color) { return color; }");
        typeof(ShaderFX).GetField("active", Flags).SetValue(fx, false);
        operand.fx.Add(fx);
        group.children.Add(operand);
        group.children.Add(new Layer(new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Rectangle }));
        doc.layers.Add(group); Normalize(doc); return doc;
    }
    static string Source(Layer layer) => JsonUtility.ToJson(layer);
    static void FailUnchanged(TestContext t, WhimTexDocument doc, Layer layer, bool execute)
    {
        string source = Source(layer), document = EditorJsonUtility.ToJson(doc);
        var original = layer.Behaviour; object fxId = Id(layer.fx[0]);
        Exception failure = null;
        try { Apply(doc, layer, execute); } catch (NotSupportedException error) { failure = error; }
        t.True(failure != null && failure.Message.IndexOf("font", StringComparison.OrdinalIgnoreCase) >= 0,
            "Missing original font fails explicitly, execute=" + execute);
        t.True(ReferenceEquals(original, layer.Behaviour), "Failed conversion keeps managed source identity");
        SameNative(t, fxId, layer.fx[0], "Failed conversion keeps native FX identity");
        t.Equal(source, Source(layer), "Failed conversion preserves all serialized layer/source fields");
        t.Equal(document, EditorJsonUtility.ToJson(doc), "Failed conversion preserves the complete document model");
    }
    static void Point(TestContext t, ProjectiveMatrix before, Vector2 p, ProjectiveMatrix after, Vector2 q, string label)
    {
        var a = before.Point(new Double2(p.x, p.y)); var b = after.Point(new Double2(q.x, q.y));
        t.Near(a.x, b.x, 2e-6, label + " X"); t.Near(a.y, b.y, 2e-6, label + " Y");
    }

    static void ApiBody(TestContext t, Scope scope, string family)
    {
        var doc = Fixture(scope, family, out var layer);
        var source = (TextLayerBehaviour)layer.Behaviour;
        Invoke(typeof(TextLayerBehaviour), source, "PrepareSavedAppearance", doc.width, doc.height);
        string sourceData = JsonUtility.ToJson(source), shell = Source(layer);
        var curves = TextToVector.Convert(source, doc);
        t.Equal(sourceData, JsonUtility.ToJson(source), "Public converter leaves full Text settings and saved appearance unchanged");
        t.Equal(shell, Source(layer), "Detached converter leaves wrapper, transform, FX and ID unchanged");
        t.Equal(1, curves.shapes.Count, "One compound vector");
        t.Equal(VectorFillRule.NonZero, curves.shapes[0].fillRule, "Font winding retained");
        t.True(curves.shapes[0].contours.Count > 2, "Fixture contains multiple glyph contours and holes");

        // Simulate reopening a saved document on a machine lacking its original font.
        // Keep real saved PNG bytes; only relabel this OWNED fixture's family/key.
        source.fontFamily = "WhimTex-Outline-Missing-" + Guid.NewGuid().ToString("N");
        typeof(TextLayerBehaviour).GetField("fallbackFont", Flags).SetValue(source, source.fontFamily);
        typeof(TextLayerBehaviour).GetField("fallbackKey", Flags).SetValue(source,
            Invoke(typeof(TextLayerBehaviour), source, "LayoutKey", doc.width, doc.height));
        t.True((bool)typeof(TextLayerBehaviour).GetProperty("HasSavedAppearance", Flags).GetValue(source), "Failure fixture has a matching saved raster appearance");
        FailUnchanged(t, doc, layer, false); FailUnchanged(t, doc, layer, true);
        JsonUtility.FromJsonOverwrite(sourceData, source);

        var parentMatrix = doc.layers[0].transform.ToMatrix(doc.width, doc.height);
        var before = parentMatrix * layer.transform.ToMatrix(doc.width, doc.height);
        string id = layer.Id; object effectId = Id(layer.fx[0]);
        Apply(doc, layer, true);
        t.True(layer.Behaviour is VectorLayerBehaviour && ReferenceEquals(doc.layers[0].children[0], layer), "API replaces only behaviour on the existing child");
        t.Equal(id, layer.Id, "API keeps ID"); t.Equal("Retain Text shell", layer.layerName, "API keeps name");
        t.Equal(.37f, layer.opacity, "API keeps opacity"); t.Equal(BlendMode.Multiply, layer.blendMode, "API keeps blend mode");
        t.True(layer.clippingMask, "API keeps clipping flag");
        SameNative(t, effectId, layer.fx[0], "API keeps original native FX");
        t.Equal(VectorBooleanOperation.Subtract, layer.booleanOperation, "API keeps Boolean operation");
        t.Equal(sourceData, JsonUtility.ToJson(source), "Successful API replacement leaves detached original Text data intact");
        t.Equal(TransformTilingMode.Unbounded, layer.transform.tiling, "Text conversion changes Clip to Unbounded");
        var fitted = (VectorLayerBehaviour)layer.Behaviour;
        var after = parentMatrix * layer.transform.ToMatrix(doc.width, doc.height);
        t.Equal(curves.shapes[0].contours.Count, fitted.shapes[0].contours.Count, "FitFrame retains every contour");
        for (int c = 0; c < curves.shapes[0].contours.Count; c++)
        {
            var a = curves.shapes[0].contours[c]; var b = fitted.shapes[0].contours[c];
            t.Equal(a.points.Count, b.points.Count, "FitFrame retains anchor count");
            for (int p = 0; p < a.points.Count; p++)
            {
                Point(t, before, a.points[p].position, after, b.points[p].position, "Canvas anchor");
                Point(t, before, a.points[p].position + a.points[p].inTangent, after, b.points[p].position + b.points[p].inTangent, "Canvas incoming handle");
                Point(t, before, a.points[p].position + a.points[p].outTangent, after, b.points[p].position + b.points[p].outTangent, "Canvas outgoing handle");
            }
        }

        // The destination is only a unique validation token: no public write request is made.
        string path = "Assets/WhimTexTextOutlineDryRun-" + Guid.NewGuid().ToString("N") + ".tiff";
        foreach (bool missing in new[] { false, true })
        {
            var request = new ApiJson { ["apiVersion"] = 1, ["assetPath"] = path, ["create"] = true,
                ["width"] = 96, ["height"] = 64, ["dryRun"] = true };
            request["operations"] = ApiJson.FromObject(new object[] {
                new { op = "add", type = "text", @as = "source", settings = new { text = new { text = "O B", fontFamily = missing ? "WhimTex-Missing-" + Guid.NewGuid().ToString("N") : family, fontSize = 24 } } },
                new { op = "convertToVector", layer = "@source" },
                new { op = "set", layer = "@source", settings = new { vector = new { edgeMode = "Step" } } }
            });
            var response = ApiJson.Parse(WhimTexApi.ExecuteJson(request.ToString()));
            t.Equal(!missing, (bool)response["success"], "Public dry-run conversion result: " + response);
            t.True(!(bool)response["applied"] && !(bool)response["saved"], "Public dry-run never applies or saves");
            if (missing) t.Equal(1, (int)response["failedOperation"], "Missing font fails at conversion, not setup");
            else t.Equal(3, (int)response["operationCount"], "Vector-only patch proves conversion ran in preflight");
            t.True(!File.Exists(path) && !File.Exists(path + ".meta"), "Dry-run creates no TIFF or meta");
        }
    }

    static IEnumerable<Layer> Tree(IEnumerable<Layer> layers)
    {
        foreach (var layer in layers)
        { yield return layer; if (layer.children != null) foreach (var child in Tree(layer.children)) yield return child; }
    }
    static WhimTexDocument Decode(TestContext t, Scope scope, WhimTexDocumentContainer container)
    {
        var result = Call(Serializer, "Deserialize", container.Get(WhimTexDocumentContainer.DocumentBlock), container, typeof(WhimTexDocument), null, false);
        var doc = scope.Own((WhimTexDocument)result.GetType().GetProperty("Model", Flags).GetValue(result));
        foreach (var layer in Tree(doc.layers)) foreach (var fx in layer.fx) if (fx != null) scope.Own(fx);
        foreach (string field in new[] { "SkippedFields", "MissingTypes", "UnresolvedReferences" })
            t.Equal(0, ((IReadOnlyList<string>)result.GetType().GetProperty(field, Flags).GetValue(result)).Count, "Native " + field);
        Normalize(doc); return doc;
    }
    static void Same(TestContext t, Layer a, Layer b)
    {
        t.Equal(a.Id, b.Id, "Stored ID"); t.Equal(a.layerName, b.layerName, "Stored name");
        t.Equal(a.enabled, b.enabled, "Stored visibility"); t.Equal(a.transform, b.transform, "Stored transform");
        t.Equal(a.opacity, b.opacity, "Stored opacity"); t.Equal(a.blendMode, b.blendMode, "Stored blend");
        t.Equal(a.clippingMask, b.clippingMask, "Stored clipping flag"); t.Equal(a.booleanOperation, b.booleanOperation, "Stored Boolean operation");
        t.Equal(a.Behaviour.GetType(), b.Behaviour.GetType(), "Stored source type");
        t.Equal(JsonUtility.ToJson(a.Behaviour), JsonUtility.ToJson(b.Behaviour), "All source settings/curves/IDs/saved raster bytes roundtrip");
        t.Equal(a.fx.Count, b.fx.Count, "Stored FX count");
        for (int i = 0; i < a.fx.Count; i++)
        {
            t.Equal(JsonUtility.ToJson(a.fx[i]), JsonUtility.ToJson(b.fx[i]), "Stored inactive FX data");
            object sourceId = Id(a.fx[i]), readId = Id(b.fx[i]);
            t.True(a.fx[i] != null && b.fx[i] != null && !sourceId.Equals(readId),
                "Native read owns independent FX; source UnityObjectID=" + sourceId + "; read UnityObjectID=" + readId);
        }
        t.Equal(a.children?.Count ?? 0, b.children?.Count ?? 0, "Stored child order/count");
        if (a.children != null) for (int i = 0; i < a.children.Count; i++) Same(t, a.children[i], b.children[i]);
    }
    static void NativeBody(TestContext t, Scope scope, string family)
    {
        var doc = Fixture(scope, family, out var converted); Apply(doc, converted, true);
        var editable = new Layer(Text(family)) { layerName = "Still editable", booleanOperation = VectorBooleanOperation.Xor };
        var text = (TextLayerBehaviour)editable.Behaviour;
        text.layoutMode = TextLayoutMode.Frame; text.overflow = TextOverflowMode.Ellipsis;
        text.frameSize = new Vector2(144, 80); text.layoutOffset = new Vector2(3, -4);
        text.autoSize = true; editable.transform.positionF = new Vector2(-9, 6);
        doc.layers[0].children.Insert(0, editable); Normalize(doc);
        Invoke(typeof(TextLayerBehaviour), text, "PrepareSavedAppearance", doc.width, doc.height);
        string original = EditorJsonUtility.ToJson(doc);
        using var container = new WhimTexDocumentContainer();
        container.Set(WhimTexDocumentContainer.DocumentBlock, (byte[])Call(Serializer, "Serialize", doc, container));
        var native = Decode(t, scope, container); Same(t, doc.layers[0], native.layers[0]);
        var image = scope.Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
        image.SetPixels(new[] { Color.clear, Color.red, Color.green, Color.blue }); image.Apply();
        byte[] tiff = (byte[])Call(Carrier, "Write", container, image, (bool?)false);
        object[] args = { tiff, null, null };
        t.True((bool)Call(Carrier, "TryRead", args), "In-memory TIFF carrier extracts model payload: " + args[2]);
        using var parsed = WhimTexDocumentContainer.Parse((byte[])args[1]);
        var reopened = Decode(t, scope, parsed); Same(t, doc.layers[0], reopened.layers[0]);
        t.Equal(original, EditorJsonUtility.ToJson(doc), "TIFF roundtrip never changes the source document");
        var reopenedVector = (VectorLayerBehaviour)reopened.layers[0].children[1].Behaviour;
        var originalVector = (VectorLayerBehaviour)converted.Behaviour;
        reopenedVector.shapes[0].contours[0].points[0].position += Vector2.one;
        t.True(reopenedVector.shapes[0].contours[0].points[0].position != originalVector.shapes[0].contours[0].points[0].position,
            "Reopened curves do not share editable anchors with the source");
        ((TextLayerBehaviour)reopened.layers[0].children[0].Behaviour).text = "Changed after read";
        t.Equal("O B é\nText", text.text, "Reopened Text edits cannot change source wording");
    }
    static void SnapshotBody(TestContext t, Scope scope, string family)
    {
        var doc = Fixture(scope, family, out var layer); string id = layer.Id;
        // Remove transient references before either capture; Fixture already registered FX with Scope.
        layer.fx.Clear();
        Invoke(typeof(TextLayerBehaviour), layer.Behaviour, "PrepareSavedAppearance", doc.width, doc.height);
        string before = EditorJsonUtility.ToJson(doc), source = JsonUtility.ToJson(layer.Behaviour);
        var originalSource = layer.Behaviour;
        var transform = layer.transform;
        Apply(doc, layer, true);
        t.Equal(source, JsonUtility.ToJson(originalSource), "Conversion preserves detached original Text model");
        string after = EditorJsonUtility.ToJson(doc), vector = JsonUtility.ToJson(layer.Behaviour);
        var fitted = layer.transform;
        EditorJsonUtility.FromJsonOverwrite(before, doc);
        layer = doc.layers[0].children[0];
        t.True(layer.Behaviour is TextLayerBehaviour, "Model snapshot before conversion restores editable Text behaviour");
        t.Equal(id, layer.Id, "Model snapshot before conversion restores ID");
        t.Equal(transform, layer.transform, "Model snapshot before conversion restores original transform/tiling");
        t.Equal(source, JsonUtility.ToJson(layer.Behaviour), "Model snapshot before conversion restores all text and saved appearance fields");
        SnapshotShell(t, layer, "Model snapshot before conversion");
        EditorJsonUtility.FromJsonOverwrite(after, doc);
        layer = doc.layers[0].children[0];
        t.True(layer.Behaviour is VectorLayerBehaviour, "Model snapshot after conversion restores Vector behaviour");
        t.Equal(id, layer.Id, "Model snapshot after conversion restores ID");
        t.Equal(fitted, layer.transform, "Model snapshot after conversion restores fitted transform/tiling");
        t.Equal(vector, JsonUtility.ToJson(layer.Behaviour), "Model snapshot after conversion restores exact curves and geometry IDs");
        SnapshotShell(t, layer, "Model snapshot after conversion");
    }
    static void SnapshotShell(TestContext t, Layer layer, string phase)
    {
        t.Equal("Retain Text shell", layer.layerName, phase + " retains layer name");
        t.Equal(.37f, layer.opacity, phase + " retains opacity");
        t.Equal(BlendMode.Multiply, layer.blendMode, phase + " retains blending");
        t.Equal(VectorBooleanOperation.Subtract, layer.booleanOperation, phase + " retains Boolean operation");
        t.True(layer.enabled && layer.clippingMask, phase + " retains visibility and clipping");
        t.Equal(0, layer.fx.Count, phase + " retains empty FX list");
    }
    static string RunPart(string name, Action<TestContext, Scope, string> body) => TestContext.Run(name, t =>
    {
        var scope = new Scope(t); Exception failure = null;
        try { body(t, scope, Family()); } catch (Exception error) { failure = error; }
        try { scope.Dispose(); } catch (Exception cleanup) { throw failure == null ? cleanup : new AggregateException(failure, cleanup); }
        if (failure != null) throw failure;
    });
    public static string Api() => RunPart("Text conversion API: public dry-run and detached atomic source preservation", ApiBody);
    public static string Native() => RunPart("Text/Vector TIFF model and carrier roundtrip in memory", NativeBody);
    public static string UnitySnapshot() => RunPart("Model snapshot before/after conversion", SnapshotBody);
    public static string Run() => RunPart("Text conversion API, in-memory TIFF and model snapshots before/after conversion", (t, scope, family) =>
    { ApiBody(t, scope, family); NativeBody(t, scope, family); SnapshotBody(t, scope, family); });
}
