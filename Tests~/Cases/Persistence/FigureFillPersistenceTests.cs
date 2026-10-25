using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Support: Framework/TestApi.cs. Detached models, Unity snapshots and in-memory TIFF bytes only.
public static class FigureFillPersistenceTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Type Serializer = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
    static readonly Type Carrier = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexTiffCarrier", true);
    static object JsonObject(string json) => Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true)
        .GetMethod("Parse", new[]{typeof(string)}).Invoke(null,new object[]{json});
    static object Call(Type type, string name, params object[] args)
    {
        var method = type.GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null ||
                (p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType).IsInstanceOfType(args[i])).All(match => match));
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    static WhimTexGradient Palette(int index)
    {
        var modes = new[] {
            (color: WhimTexGradientColorMode.Gamma, interpolation: WhimTexGradientInterpolation.Fixed),
            (color: WhimTexGradientColorMode.Gamma, interpolation: WhimTexGradientInterpolation.Balanced),
            (color: WhimTexGradientColorMode.Linear, interpolation: WhimTexGradientInterpolation.Balanced),
            (color: WhimTexGradientColorMode.Perceptual, interpolation: WhimTexGradientInterpolation.Balanced)
        };
        var gradient = new WhimTexGradient
        {
            ColorMode = modes[index % modes.Length].color, Interpolation = modes[index % modes.Length].interpolation, WrapMode = (WhimTexGradientWrapMode)(index % 3),
            ColorSpace = index % 2 == 0 ? ColorSpace.Gamma : ColorSpace.Linear, Smoothness = .37f
        };
        gradient.SetKeys(new[] {
            new GradientColorKey(new Color(-.25f, 2.5f, .1f), 0),
            new GradientColorKey(new Color(.4f, .8f, 3.5f), .35f),
            new GradientColorKey(new Color(1.5f, .2f, -.1f), 1)
        }, new[] { new GradientAlphaKey(.2f, 0), new GradientAlphaKey(.7f, .6f), new GradientAlphaKey(.9f, 1) });
        gradient.SetMidpoint(false, 0, .27f); gradient.SetMidpoint(false, 1, .68f);
        gradient.SetMidpoint(true, 0, .33f); gradient.SetMidpoint(true, 1, .72f);
        return gradient;
    }
    static void SameFill(TestContext t, VectorFillMode mode, Color color, WhimTexGradient gradient,
        Vector2 start, Vector2 end, float repetitions, VectorFillMode actualMode, Color actualColor, WhimTexGradient actualGradient,
        Vector2 actualStart, Vector2 actualEnd, float actualRepetitions, string label, bool independent = true)
    {
        t.Equal(mode, actualMode, label + " mode"); t.Equal(color, actualColor, label + " inactive solid color");
        t.Equal(start, actualStart, label + " start"); t.Equal(end, actualEnd, label + " end");
        t.Equal(repetitions, actualRepetitions, label + " retained angular repetitions");
        t.Equal(JsonUtility.ToJson(gradient), JsonUtility.ToJson(actualGradient),
            label + " HDR/alpha stops, midpoints, mode, wrap, smoothness and color space");
        if (independent) t.True(!ReferenceEquals(gradient, actualGradient), label + " owns its palette");
    }
    static void Same(TestContext t, WhimTexDocument expected, WhimTexDocument actual, string label, bool independent = true)
    {
        t.Equal(expected.layers.Count, actual.layers.Count, label + " layer count");
        for (int i = 0; i < expected.layers.Count; i++)
        {
            if (expected.layers[i].Behaviour is ShapeLayerBehaviour aShape)
            {
                var bShape = (ShapeLayerBehaviour)actual.layers[i].Behaviour;
                t.Equal(aShape.fill, bShape.fill, label + " Shape gate " + i);
                SameFill(t, aShape.fillMode, aShape.fillColor, aShape.gradient, aShape.gradientStart, aShape.gradientEnd, aShape.gradientRepetitions,
                    bShape.fillMode, bShape.fillColor, bShape.gradient, bShape.gradientStart, bShape.gradientEnd, bShape.gradientRepetitions,
                    label + " Shape " + i, independent);
                t.Equal(aShape.stroke, bShape.stroke, label + " Shape stroke gate " + i);
                SameFill(t, aShape.strokeMode, aShape.strokeColor, aShape.strokeGradient, aShape.strokeGradientStart, aShape.strokeGradientEnd, aShape.strokeGradientRepetitions,
                    bShape.strokeMode, bShape.strokeColor, bShape.strokeGradient, bShape.strokeGradientStart, bShape.strokeGradientEnd, bShape.strokeGradientRepetitions,
                    label + " Shape stroke " + i, independent);
                t.True(!ReferenceEquals(bShape.gradient, bShape.strokeGradient), label + " independent Shape fill/stroke palettes");
                continue;
            }
            if (expected.layers[i].Behaviour is BooleanGroupLayerBehaviour aGroup)
            {
                var bGroup = (BooleanGroupLayerBehaviour)actual.layers[i].Behaviour;
                t.Equal(aGroup.referenceSize, bGroup.referenceSize, label + " Boolean Group reference metric " + i);
                t.Equal(aGroup.appearance, bGroup.appearance, label + " Boolean Group appearance " + i);
                SameFigure(t, aGroup.style, bGroup.style, label + " Boolean Group " + i, independent);
                continue;
            }
            var av = (VectorLayerBehaviour)expected.layers[i].Behaviour;
            var bv = (VectorLayerBehaviour)actual.layers[i].Behaviour;
            t.Equal(av.referenceSize, bv.referenceSize, label + " Vector reference metric " + i);
            t.Equal(1, av.shapes.Count, label + " source Vector has one figure " + i);
            t.Equal(1, bv.shapes.Count, label + " restored Vector has one figure " + i);
            SameFigure(t, av.shapes[0], bv.shapes[0], label + " Vector " + i, independent);
        }
    }
    static void SameFigure(TestContext t, VectorShape a, VectorShape b, string label, bool independent = true)
    {
        t.Equal(a.visible, b.visible, label + " visibility");
        SameFill(t, a.fillMode, a.fillColor, a.gradient, a.gradientStart, a.gradientEnd, a.gradientRepetitions,
            b.fillMode, b.fillColor, b.gradient, b.gradientStart, b.gradientEnd, b.gradientRepetitions, label + " fill", independent);
        t.Equal(a.stroke, b.stroke, label + " stroke gate");
        SameFill(t, a.strokeMode, a.strokeColor, a.strokeGradient, a.strokeGradientStart, a.strokeGradientEnd, a.strokeGradientRepetitions,
            b.strokeMode, b.strokeColor, b.strokeGradient, b.strokeGradientStart, b.strokeGradientEnd, b.strokeGradientRepetitions, label + " stroke", independent);
        t.True(!ReferenceEquals(b.gradient, b.strokeGradient), label + " independent fill/stroke palettes");
    }
    static WhimTexDocument Decode(byte[] bytes, WhimTexDocumentContainer container, List<Object> owned, TestContext t)
    {
        var result = Call(Serializer, "Deserialize", bytes, container, typeof(WhimTexDocument), null, false);
        foreach (string field in new[] { "SkippedFields", "MissingTypes", "UnresolvedReferences" })
            t.Equal(0, ((IReadOnlyList<string>)result.GetType().GetProperty(field, Flags).GetValue(result)).Count, "No " + field);
        var document = (WhimTexDocument)result.GetType().GetProperty("Model", Flags).GetValue(result);
        owned.Add(document); return document;
    }
    static void Reject(TestContext t, Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (WhimTexDocumentException) { rejected = true; }
        t.True(rejected, label);
    }
    static void RejectRepetitions(TestContext t, Action action, string label, string field = "gradientRepetitions")
    {
        bool rejected = false;
        try { action(); }
        catch (Exception error) when (error is WhimTexDocumentException || error is InvalidOperationException ||
            error.GetType().Name == "WhimTexApiException")
        {
            rejected = true;
            t.True(error.Message.Contains(field), label + " identifies the field");
        }
        t.True(rejected, label);
    }
    public static string Run() => TestContext.Run("Independent figure fills/strokes: JSON/API/defaults, native payload, TIFF and Unity snapshots", t =>
    {
        var owned = new List<Object>();
        try
        {
            t.Equal(3, (int)GradientLayerBehaviour.GradientType.Angular, "Angular retains its native Gradient enum value");
            t.Equal(6, (int)VectorFillMode.AngularGradient, "Angular retains its native figure enum value");
            var angularDocument = ScriptableObject.CreateInstance<WhimTexDocument>(); owned.Add(angularDocument);
            angularDocument.hideFlags = HideFlags.HideAndDontSave;
            var angular = new GradientLayerBehaviour { gradientType = GradientLayerBehaviour.GradientType.Angular,
                circularStartAngle = 37, circularRepetitions = 2.5f, circularWrapMode = GradientLayerBehaviour.WrapMode.PingPong,
                gradient = Palette(2) };
            angularDocument.layers.Add(new Layer(angular));
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Flags).Invoke(angularDocument, null);
            void SameAngular(WhimTexDocument copy)
            {
                var actual = (GradientLayerBehaviour)copy.layers[0].Behaviour;
                t.Equal(GradientLayerBehaviour.GradientType.Angular, actual.gradientType, "Angular layer type survives storage");
                t.Equal(angular.circularStartAngle, actual.circularStartAngle, "Angular start angle survives storage");
                t.Equal(angular.circularRepetitions, actual.circularRepetitions, "Angular repetitions survive storage");
                t.Equal(angular.circularWrapMode, actual.circularWrapMode, "Angular wrapping survives storage");
                t.True(actual.gradient.Equals(angular.gradient), "Angular HDR palette survives storage");
            }
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                string json = WhimTexDocumentJson.Write(angularDocument, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                t.True(json.Contains("\"Angular\""), "JSON uses the canonical Angular type name");
                using var restored = WhimTexDocumentJson.Read(json, false);
                SameAngular(restored.Document);
            }
            using (var angularContainer = new WhimTexDocumentContainer())
            {
                byte[] bytes = (byte[])Call(Serializer, "Serialize", angularDocument, angularContainer);
                SameAngular(Decode(bytes, angularContainer, owned, t));
            }
            t.Equal("Linear,Radial,Angular,Square,Freeform", string.Join(",", Enum.GetNames(typeof(GradientLayerBehaviour.GradientType))), "Canonical gradient type set");
            t.Equal(GradientLayerBehaviour.GradientType.Linear, new GradientLayerBehaviour().gradientType, "Linear is the layer default");
            foreach (GradientLayerBehaviour.GradientType kind in Enum.GetValues(typeof(GradientLayerBehaviour.GradientType)))
            {
                angular.gradientType = kind;
                foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
                {
                    string json = WhimTexDocumentJson.Write(angularDocument, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                    using var copy = WhimTexDocumentJson.Read(json, false);
                    t.Equal(kind, ((GradientLayerBehaviour)copy.Document.layers[0].Behaviour).gradientType, "Gradient type JSON roundtrip " + kind + "/" + mode);
                }
                using var typeContainer = new WhimTexDocumentContainer();
                byte[] bytes = (byte[])Call(Serializer, "Serialize", angularDocument, typeContainer);
                t.Equal(kind, ((GradientLayerBehaviour)Decode(bytes, typeContainer, owned, t).layers[0].Behaviour).gradientType, "Gradient type native roundtrip " + kind);
            }
            angular.gradientType = GradientLayerBehaviour.GradientType.Linear;
            string current = WhimTexDocumentJson.Write(angularDocument, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }).Json;
            string missingType = System.Text.RegularExpressions.Regex.Replace(current, @"""gradientType""\s*:\s*""Linear""\s*,", "");
            t.True(missingType != current, "Missing-type fixture removes the explicit type");
            Reject(t, () => { using var rejected = WhimTexDocumentJson.Read(missingType, false); }, "Missing type cannot reinterpret the former compact default");
            foreach (string removed in new[] { "Vertical", "Horizontal", "Diamond" })
            {
                string invalid = System.Text.RegularExpressions.Regex.Replace(current, @"(""gradientType""\s*:\s*)""Linear""", "$1\"" + removed + "\"");
                t.True(invalid != current, "Retired type test changes the type field");
                Reject(t, () => { using var rejected = WhimTexDocumentJson.Read(invalid, false); }, "Removed type rejected: " + removed);
            }
            var text = new TextLayerBehaviour();
            Call(typeof(WhimTexApi), "SetText", text, JsonObject("{\"fillMode\":\"AngularGradient\"}"));
            t.Equal(VectorFillMode.AngularGradient, text.fillMode, "Text API accepts AngularGradient");
            t.Equal("AngularGradient", (string)ApiJson.FromObject(Call(typeof(WhimTexApi), "TextSnapshot", text))["fillMode"], "Text API reports AngularGradient");
            var document = ScriptableObject.CreateInstance<WhimTexDocument>(); owned.Add(document);
            document.hideFlags = HideFlags.HideAndDontSave; document.width = 320; document.height = 180;
            var vector = new VectorLayerBehaviour { referenceSize = new Vector2(320, 180) };
            string[] names = { "None", "Solid", "LinearGradient", "RadialGradient", "AngularGradient", "SquareGradient" };
            var fillModes = names.Select(name => (VectorFillMode)Enum.Parse(typeof(VectorFillMode),name)).ToArray();
            for (int mode = 0; mode < names.Length; mode++)
            {
                t.Equal(names[mode], fillModes[mode].ToString(), "Stable fill enum " + mode);
                for (int gate = 0; gate < 2; gate++)
                {
                    var shape = new ShapeLayerBehaviour
                    {
                        fill = gate == 1, fillMode = fillModes[mode], fillColor = new Color(2.25f, -.2f, .4f, .3f),
                        gradient = Palette(mode * 2 + gate), gradientStart = new Vector2(-.125f, .25f),
                        gradientEnd = gate == 0 ? new Vector2(-.125f, .25f) : new Vector2(.85f, 1.2f),
                        gradientRepetitions = gate == 0 ? .0001f : mode + 1.25f,
                        stroke = gate == 1, strokeMode = fillModes[names.Length - 1 - mode], strokePosition = ShapeLayerBehaviour.StrokePosition.Outside,
                        strokeColor = new Color(.1f, 3f, -.3f, .6f), strokeGradient = Palette(mode * 2 + gate + 3),
                        strokeGradientStart = new Vector2(.2f, -.4f),
                        strokeGradientEnd = gate == 0 ? new Vector2(.2f, -.4f) : new Vector2(1.1f, .7f),
                        strokeGradientRepetitions = gate == 0 ? mode + 2.5f : .0001f
                    };
                    document.layers.Add(new Layer(shape));
                    vector.shapes.Add(new VectorShape
                    {
                        visible = gate == 1, fillMode = shape.fillMode, fillColor = shape.fillColor,
                        gradient = shape.gradient.Clone(), gradientStart = shape.gradientStart, gradientEnd = shape.gradientEnd,
                        gradientRepetitions = shape.gradientRepetitions,
                        stroke = shape.stroke, strokeMode = shape.strokeMode, strokeColor = shape.strokeColor,
                        strokeGradient = shape.strokeGradient.Clone(), strokeGradientStart = shape.strokeGradientStart,
                        strokeGradientEnd = shape.strokeGradientEnd, strokeGradientRepetitions = shape.strokeGradientRepetitions
                    });
                }
            }
            foreach (var figure in vector.shapes)
                document.layers.Add(new Layer(new BooleanGroupLayerBehaviour
                {
                    referenceSize = vector.referenceSize, style = figure.Clone(false),
                    appearance = figure.stroke ? BooleanGroupAppearance.Group : BooleanGroupAppearance.Layers
                }));
            foreach (var figure in vector.shapes)
                document.layers.Add(new Layer(new VectorLayerBehaviour
                {
                    referenceSize = vector.referenceSize, shapes = new List<VectorShape> { figure }
                }));
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Flags).Invoke(document, null);
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                var options = new WhimTexJsonWriteOptions { Mode = mode };
                using var restored = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document, options).Json, false);
                Same(t, document, restored.Document, "JSON " + mode);
                using var fragment = WhimTexDocumentJson.Read(WhimTexDocumentJson.WriteLayers(document, document.layers, options).Json, false);
                Same(t, document, fragment.Document, "Clipboard " + mode);
            }
            using (var defaults = WhimTexDocumentJson.Read("{\"format\":\"whimtex.document\",\"version\":3,\"layers\":[{\"id\":\"shape\",\"behaviour\":{\"$type\":\"ShapeLayerBehaviour\"}},{\"id\":\"vector\",\"behaviour\":{\"$type\":\"VectorLayerBehaviour\",\"shapes\":[{}]}},{\"id\":\"boolean\",\"behaviour\":{\"$type\":\"BooleanGroupLayerBehaviour\"}}]}", false))
            {
                var shape = (ShapeLayerBehaviour)defaults.Document.layers[0].Behaviour;
                var figure = ((VectorLayerBehaviour)defaults.Document.layers[1].Behaviour).shapes[0];
                t.True(shape.fill, "Omitted Shape fill gate defaults on");
                SameFill(t, VectorFillMode.Solid, Color.white, new WhimTexGradient(), Vector2.zero, Vector2.right, 1f,
                    shape.fillMode, shape.fillColor, shape.gradient, shape.gradientStart, shape.gradientEnd, shape.gradientRepetitions, "Omitted Shape fill");
                SameFill(t, VectorFillMode.Solid, Color.white, new WhimTexGradient(), Vector2.zero, Vector2.right, 1f,
                    figure.fillMode, figure.fillColor, figure.gradient, figure.gradientStart, figure.gradientEnd, figure.gradientRepetitions, "Omitted Vector fill");
                t.True(!shape.stroke, "Omitted Shape stroke gate defaults off");
                SameFill(t, VectorFillMode.Solid, Color.black, new WhimTexGradient(), Vector2.zero, Vector2.right, 1f,
                    shape.strokeMode, shape.strokeColor, shape.strokeGradient, shape.strokeGradientStart, shape.strokeGradientEnd, shape.strokeGradientRepetitions, "Omitted Shape stroke");
                var group = (BooleanGroupLayerBehaviour)defaults.Document.layers[2].Behaviour;
                foreach (var style in new[] { figure, group.style })
                {
                    t.True(!style.stroke, "Omitted Vector/Boolean stroke gate defaults off");
                    SameFill(t, VectorFillMode.Solid, Color.black, new WhimTexGradient(), Vector2.zero, Vector2.right, 1f,
                        style.strokeMode, style.strokeColor, style.strokeGradient, style.strokeGradientStart, style.strokeGradientEnd, style.strokeGradientRepetitions,
                        "Omitted Vector/Boolean stroke");
                    t.True(!ReferenceEquals(style.gradient, style.strokeGradient), "Default fill and stroke palettes are independent");
                }
                t.True(!ReferenceEquals(shape.gradient, shape.strokeGradient), "Default Shape fill and stroke palettes are independent");
            }
            // Exercise the actual package API parsers/snapshots without opening or saving a user document.
            var apiShape = (ShapeLayerBehaviour)document.layers[0].Behaviour;
            var before = ApiJson.FromObject(Call(typeof(WhimTexApi), "ShapeSnapshot", apiShape));
            Call(typeof(WhimTexApi), "SetShape", apiShape, JsonObject("{\"fill\":true,\"fillMode\":\"AngularGradient\"}"));
            var after = ApiJson.FromObject(Call(typeof(WhimTexApi), "ShapeSnapshot", apiShape));
            foreach (string field in new[] { "fillColor", "gradient", "gradientStart", "gradientEnd", "gradientRepetitions",
                "stroke", "strokeMode", "strokeColor", "strokeGradient", "strokeGradientStart", "strokeGradientEnd", "strokeGradientRepetitions" })
                t.Equal(before[field].ToString(), after[field].ToString(), "Partial Shape API retains " + field);
            Call(typeof(WhimTexApi), "SetShape", apiShape, JsonObject("{\"stroke\":true,\"strokeMode\":\"None\"}"));
            var strokePatch = ApiJson.FromObject(Call(typeof(WhimTexApi), "ShapeSnapshot", apiShape));
            foreach (string field in new[] { "fill", "fillMode", "fillColor", "gradient", "gradientStart", "gradientEnd", "gradientRepetitions",
                "strokeColor", "strokeGradient", "strokeGradientStart", "strokeGradientEnd", "strokeGradientRepetitions" })
                t.Equal(after[field].ToString(), strokePatch[field].ToString(), "Stroke mode patch retains " + field);
            after = strokePatch;
            var apiCopy = new ShapeLayerBehaviour();
            Call(typeof(WhimTexApi), "SetShape", apiCopy, JsonObject(after.ToString()));
            t.Equal(after.ToString(), Call(typeof(WhimTexApi), "ShapeSnapshot", apiCopy).ToString(), "Shape snapshot can be replayed as settings");
            // API layers contain one figure; exercise each retained style independently.
            foreach (var figure in vector.shapes)
            {
                var source = new VectorLayerBehaviour { referenceSize = vector.referenceSize, shapes = new List<VectorShape> { figure } };
                var vectorSnapshot = Call(typeof(WhimTexDocumentJson), "VectorSnapshot", source, document);
                var vectorCopy = new VectorLayerBehaviour();
                Call(typeof(WhimTexDocumentJson), "PatchVector", vectorCopy, vectorSnapshot, document);
                t.Equal(vectorSnapshot.ToString(), Call(typeof(WhimTexDocumentJson), "VectorSnapshot", vectorCopy, document).ToString(),
                    "Vector API retains fill settings for " + figure.fillMode);
                SameFigure(t, figure, vectorCopy.shapes[0], "Vector API independent paint");
                SameFigure(t, figure, figure.Clone(), "Vector clone retains paint");
                SameFigure(t, figure, figure.Clone(false), "Vector clone with new IDs retains paint");
            }
            foreach (var group in document.layers.Select(layer => layer.Behaviour).OfType<BooleanGroupLayerBehaviour>())
            {
                var snapshot = Call(typeof(WhimTexDocumentJson), "BooleanGroupSnapshot", group, document);
                var copy = new BooleanGroupLayerBehaviour();
                Call(typeof(WhimTexDocumentJson), "PatchBooleanGroup", copy, snapshot, document);
                SameFigure(t, group.style, copy.style, "Boolean Group API paint");
                Call(typeof(WhimTexDocumentJson), "PatchBooleanGroup", copy, JsonObject("{\"appearance\":\"Layers\"}"), document);
                SameFigure(t, group.style, copy.style, "Boolean Group appearance patch retains paint");
            }
            foreach (var shape in document.layers.Select(layer => layer.Behaviour).OfType<ShapeLayerBehaviour>())
            {
                var converted = ShapeToVector.Convert(shape, document);
                t.Equal(shape.gradientRepetitions, converted.shapes[0].gradientRepetitions, "Shape conversion retains repetitions for " + shape.fillMode);
                var figure = converted.shapes[0];
                t.Equal(shape.stroke, figure.stroke, "Shape conversion retains stroke gate");
                SameFill(t, shape.strokeMode, shape.strokeColor, shape.strokeGradient, shape.strokeGradientStart, shape.strokeGradientEnd, shape.strokeGradientRepetitions,
                    figure.strokeMode, figure.strokeColor, figure.strokeGradient, figure.strokeGradientStart, figure.strokeGradientEnd, figure.strokeGradientRepetitions,
                    "Shape conversion retains independent stroke " + shape.strokeMode);
            }
            foreach (string field in new[] { "gradientRepetitions", "strokeGradientRepetitions" })
            {
                foreach (float repetitions in new[] { .0001f, .125f, 1f, 2.5f, float.MaxValue })
                {
                    var patch = new ApiJson { [field] = ApiJson.FromObject(repetitions) };
                    Call(typeof(WhimTexApi), "SetShape", apiCopy, JsonObject(patch.ToString()));
                    t.Equal(repetitions, field == "gradientRepetitions" ? apiCopy.gradientRepetitions : apiCopy.strokeGradientRepetitions, "Shape API accepts finite " + field);
                }
                foreach (float invalid in new[] { 0f, -1f, .00001f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    var patch = new ApiJson { [field] = ApiJson.FromObject(invalid) };
                    float previous = field == "gradientRepetitions" ? apiCopy.gradientRepetitions : apiCopy.strokeGradientRepetitions;
                    RejectRepetitions(t, () => Call(typeof(WhimTexApi), "SetShape", apiCopy, JsonObject(patch.ToString())), "Shape API invalid " + field, field);
                    t.Equal(previous, field == "gradientRepetitions" ? apiCopy.gradientRepetitions : apiCopy.strokeGradientRepetitions, "Rejected repetitions retain the previous value");
                }
            }
            foreach (string field in new[] { "strokeGradientStart", "strokeGradientEnd" })
            {
                var finite = new Vector2(float.MaxValue, -float.MaxValue);
                var patch = new ApiJson { [field] = ApiJson.FromObject(new[] { finite.x, finite.y }) };
                Call(typeof(WhimTexApi), "SetShape", apiCopy, JsonObject(patch.ToString()));
                t.Equal(finite, field == "strokeGradientStart" ? apiCopy.strokeGradientStart : apiCopy.strokeGradientEnd, "Stroke API accepts finite anchors outside unit range");
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    patch[field] = ApiJson.FromObject(new[] { invalid, 0f });
                    RejectRepetitions(t, () => Call(typeof(WhimTexApi), "SetShape", apiCopy, JsonObject(patch.ToString())), "Stroke API rejects non-finite anchor", field);
                    t.Equal(finite, field == "strokeGradientStart" ? apiCopy.strokeGradientStart : apiCopy.strokeGradientEnd, "Invalid stroke anchor retains previous value");
                }
            }

            string unity = EditorJsonUtility.ToJson(document);
            using var baseline = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }).Json, false);
            apiShape.fill = false; apiShape.fillMode = VectorFillMode.Solid; apiShape.gradientStart = Vector2.one;
            apiShape.gradient = new WhimTexGradient(); vector.shapes[5].gradientEnd = Vector2.zero;
            apiShape.gradientRepetitions = 19f; vector.shapes[5].gradientRepetitions = 23f;
            apiShape.stroke = false; apiShape.strokeMode = VectorFillMode.Solid; apiShape.strokeGradientStart = Vector2.one;
            apiShape.strokeGradient = new WhimTexGradient(); vector.shapes[5].strokeGradientEnd = Vector2.zero;
            apiShape.strokeGradientRepetitions = 29f; vector.shapes[5].strokeGradientRepetitions = 31f;
            ((BooleanGroupLayerBehaviour)document.layers[names.Length * 2].Behaviour).style.strokeGradientRepetitions = 37f;
            EditorJsonUtility.FromJsonOverwrite(unity, document);
            Same(t, baseline.Document, document, "Unity snapshot");

            using var container = new WhimTexDocumentContainer();
            byte[] payload = (byte[])Call(Serializer, "Serialize", document, container);
            container.Set(WhimTexDocumentContainer.DocumentBlock, payload);
            Same(t, document, Decode(payload, container, owned, t), "Native tagged payload");
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true); owned.Add(image);
            image.SetPixels(new[] { Color.clear, Color.red, Color.green, Color.blue }); image.Apply();
            byte[] tiff = (byte[])Call(Carrier, "Write", container, image, (bool?)false);
            object[] carrierArgs = { tiff, null, null };
            t.True((bool)Call(Carrier, "TryRead", carrierArgs), "TIFF bytes extract: " + carrierArgs[2]);
            using var reopened = WhimTexDocumentContainer.Parse((byte[])carrierArgs[1]);
            Same(t, document, Decode(reopened.Get(WhimTexDocumentContainer.DocumentBlock), reopened, owned, t), "TIFF carrier");

            string full = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }).Json;
            foreach (string kind in new[] { "Shape", "Vector", "BooleanGroup" })
            foreach (bool stroke in new[] { false, true })
            foreach (string invalid in new[] { "mode-name", "mode-number", "anchor-short", "anchor-overflow", "null-gradient",
                "repetitions-zero", "repetitions-negative", "repetitions-small", "repetitions-overflow", "repetitions-null", "repetitions-string" })
            {
                var json = ApiJson.Parse(full);
                var node = kind == "Vector" ? json["layers"].Last()["behaviour"]["shapes"][0] :
                    kind == "BooleanGroup" ? json["layers"][names.Length * 2]["behaviour"]["style"] : json["layers"][0]["behaviour"];
                string modeField = stroke ? "strokeMode" : "fillMode", paletteField = stroke ? "strokeGradient" : "gradient";
                string repetitionsField = paletteField + "Repetitions";
                node[modeField] = "AngularGradient";
                if (stroke) node["stroke"] = false; // A disabled gate must not hide malformed stored settings.
                if (invalid == "mode-name") node[modeField] = "UnknownGradient";
                if (invalid == "mode-number") node[modeField] = 6;
                if (invalid == "anchor-short") node[paletteField + "Start"] = ApiJson.FromObject(new[]{0});
                if (invalid == "anchor-overflow") node[paletteField + "End"] = ApiJson.FromObject(new[]{0d,1e40});
                if (invalid == "null-gradient") node[paletteField] = ApiJson.Parse("{\"v\":null}")["v"];
                if (invalid == "repetitions-zero") node[repetitionsField] = 0;
                if (invalid == "repetitions-negative") node[repetitionsField] = -1;
                if (invalid == "repetitions-small") node[repetitionsField] = ApiJson.FromObject(.00001);
                if (invalid == "repetitions-overflow") node[repetitionsField] = ApiJson.FromObject(1e40);
                if (invalid == "repetitions-null") node[repetitionsField] = ApiJson.Parse("{\"v\":null}")["v"];
                if (invalid == "repetitions-string") node[repetitionsField] = "2";
                Reject(t, () => { using var _ = WhimTexDocumentJson.Read(json.ToString(), false); }, kind + " " + modeField + " " + invalid);
            }
            // Inactive fields are retained, but malformed values must still fail storage validation.
            foreach (string kind in new[] { "Shape", "Vector", "BooleanGroup" })
            foreach (bool stroke in new[] { false, true })
            foreach (VectorFillMode mode in new[] { VectorFillMode.None, VectorFillMode.Solid, VectorFillMode.LinearGradient, VectorFillMode.AngularGradient })
            foreach (float invalid in new[] { 0f, -1f, .00001f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var invalidDocument = ScriptableObject.CreateInstance<WhimTexDocument>(); owned.Add(invalidDocument);
                var figure = new VectorShape();
                var shape = new ShapeLayerBehaviour { fill = false };
                if (stroke)
                {
                    figure.strokeMode = shape.strokeMode = mode;
                    figure.strokeGradientRepetitions = shape.strokeGradientRepetitions = invalid;
                }
                else
                {
                    figure.fillMode = shape.fillMode = mode;
                    figure.gradientRepetitions = shape.gradientRepetitions = invalid;
                }
                LayerBehaviour behaviour = kind == "Vector" ? new VectorLayerBehaviour { shapes = new List<VectorShape> { figure } } :
                    kind == "BooleanGroup" ? new BooleanGroupLayerBehaviour { style = figure } : shape;
                invalidDocument.layers.Add(new Layer(behaviour));
                string field = stroke ? "strokeGradientRepetitions" : "gradientRepetitions";
                string label = kind + " " + mode + " invalid " + field + " " + invalid;
                RejectRepetitions(t, () => WhimTexDocumentJson.Write(invalidDocument), "JSON write " + label, field);
                using var invalidContainer = new WhimTexDocumentContainer();
                RejectRepetitions(t, () => Call(Serializer, "Serialize", invalidDocument, invalidContainer), "Native write " + label, field);
            }
        }
        finally { for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); }
    });
}
