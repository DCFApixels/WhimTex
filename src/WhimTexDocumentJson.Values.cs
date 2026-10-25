using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexDocumentJson
    {
        private sealed class ModelJsonPaths { }
        private static WhimTexDocumentException JsonError(JToken token, string message)
        {
            string path = token?.Path ?? "$";
            if ((token?.Root as JObject)?["document"]?.Annotation<ModelJsonPaths>() != null &&
                (path == "document.layers" || path.StartsWith("document.layers[", StringComparison.Ordinal)))
                path = path.Substring("document.".Length);
            var error = new WhimTexDocumentException((string.IsNullOrEmpty(path) ? "$" : path) + ": " + message);
            error.Data["WhimTexJsonPath"] = true;
            return error;
        }

        private static object ReadScalar(JToken token, Type type)
        {
            if (token == null) throw JsonError(token, "Required " + type.Name + " value is missing.");
            if (type.IsEnum)
            {
                if (token.Type != JTokenType.String || Array.IndexOf(Enum.GetNames(type), (string)token) < 0)
                    throw JsonError(token, "Expected a named " + type.Name + " value (case-sensitive).");
                return Enum.Parse(type, (string)token, false);
            }
            if (type == typeof(string))
            {
                if (token.Type != JTokenType.String && token.Type != JTokenType.Null) throw JsonError(token, "Expected a string.");
                return (string)token;
            }
            if (type == typeof(bool))
            {
                if (token.Type != JTokenType.Boolean) throw JsonError(token, "Expected a boolean.");
                return (bool)token;
            }
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw JsonError(token, "Expected a number, not " + token.Type + ".");
            try
            {
                double number = (double)token;
                if (double.IsNaN(number) || double.IsInfinity(number)) throw JsonError(token, "Number must be finite.");
                if (type != typeof(float) && type != typeof(double) && type != typeof(decimal) && number != Math.Truncate(number))
                    throw JsonError(token, "Expected an integer.");
                object value = token.ToObject(type);
                if (value is float single && (float.IsNaN(single) || float.IsInfinity(single)))
                    throw JsonError(token, "Number exceeds the finite Single range.");
                return value;
            }
            catch (Exception error) when (error is OverflowException || error is FormatException || error is ArgumentException)
            { throw JsonError(token, "Number is outside the " + type.Name + " range."); }
        }

        private static float CurveNumber(JToken token, bool tangent = false)
        {
            // Infinite tangents encode stepped keys; no other non-finite components are meaningful.
            if (tangent && token?.Type == JTokenType.String)
            {
                if ((string)token == "Infinity") return float.PositiveInfinity;
                if ((string)token == "-Infinity") return float.NegativeInfinity;
            }
            return (float)ReadScalar(token, typeof(float));
        }

        private static JToken WriteUnityValue(object value)
        {
            switch (value)
            {
                case Vector2 v: return new JArray(v.x, v.y);
                case Vector3 v: return new JArray(v.x, v.y, v.z);
                case Vector4 v: return new JArray(v.x, v.y, v.z, v.w);
                case Vector2Int v: return new JArray(v.x, v.y);
                case Vector3Int v: return new JArray(v.x, v.y, v.z);
                case Quaternion v: return new JArray(v.x, v.y, v.z, v.w);
                case Color v: return new JArray(v.r, v.g, v.b, v.a);
                case Color32 v: return new JArray(v.r, v.g, v.b, v.a);
                case Rect v: return new JArray(v.x, v.y, v.width, v.height);
                case RectInt v: return new JArray(v.x, v.y, v.width, v.height);
                case Bounds v: return new JArray(v.center.x, v.center.y, v.center.z, v.size.x, v.size.y, v.size.z);
                case AnimationCurve v:
                    var keys = new JArray();
                    foreach (var key in v.keys) keys.Add(new JArray(key.time, key.value, key.inTangent, key.outTangent, key.inWeight, key.outWeight, key.weightedMode.ToString()));
                    return new JObject { ["preWrap"] = v.preWrapMode.ToString(), ["postWrap"] = v.postWrapMode.ToString(), ["keys"] = keys };
                default: return null;
            }
        }

        private static bool TryReadUnityValue(JToken token, Type type, out object value)
        {
            value = null;
            if (type == typeof(AnimationCurve))
            {
                var node = token as JObject ?? throw new WhimTexDocumentException("Expected curve object.");
                CheckKeys(node, "preWrap", "postWrap", "keys");
                var keys = node["keys"] as JArray ?? throw new WhimTexDocumentException("Curve keys must be an array.");
                if (keys.Count > 65536) throw new WhimTexDocumentException("Too many curve keys.");
                var result = new Keyframe[keys.Count];
                for (int i = 0; i < keys.Count; i++)
                {
                    if (!(keys[i] is JArray key) || key.Count != 7) throw new WhimTexDocumentException("Expected seven curve key components.");
                    result[i] = new Keyframe(CurveNumber(key[0]), CurveNumber(key[1]), CurveNumber(key[2], true), CurveNumber(key[3], true), CurveNumber(key[4]), CurveNumber(key[5]))
                    { weightedMode = (WeightedMode)ReadScalar(key[6], typeof(WeightedMode)) };
                }
                value = new AnimationCurve(result) { preWrapMode = (WrapMode)ReadScalar(node["preWrap"], typeof(WrapMode)),
                    postWrapMode = (WrapMode)ReadScalar(node["postWrap"], typeof(WrapMode)) };
                return true;
            }
            int dimensions =
                type == typeof(Vector2) ? 2 :
                type == typeof(Vector3) ? 3 :
                type == typeof(Vector4) ? 4 :
                type == typeof(Vector2Int) ? 2 :
                type == typeof(Vector3Int) ? 3 :
                type == typeof(Quaternion) ? 4 :
                type == typeof(Color) ? 4 :
                type == typeof(Color32) ? 4 :
                type == typeof(Rect) ? 4 :
                type == typeof(RectInt) ? 4 :
                type == typeof(Bounds) ? 6 : 0;
            if (dimensions == 0) return false;
            int minimum = type == typeof(Vector3) || type == typeof(Vector4) ? 2 : dimensions;
            if (!(token is JArray components) || components.Count < minimum || components.Count > dimensions)
                throw new WhimTexDocumentException("Invalid " + type.Name + " at " + token.Path);
            Type componentType = type == typeof(Color32) ? typeof(byte) :
                type == typeof(Vector2Int) || type == typeof(Vector3Int) || type == typeof(RectInt) ? typeof(int) : typeof(float);
            foreach (var component in components) ReadScalar(component, componentType);
            if (type == typeof(Vector2)) value = new Vector2((float)components[0], (float)components[1]);
            if (type == typeof(Vector3)) value = new Vector3((float)components[0], (float)components[1], components.Count > 2 ? (float)components[2] : 0f);
            if (type == typeof(Vector4)) value = new Vector4((float)components[0], (float)components[1],
                components.Count > 2 ? (float)components[2] : 0f, components.Count > 3 ? (float)components[3] : 0f);
            if (type == typeof(Vector2Int)) value = new Vector2Int((int)components[0], (int)components[1]);
            if (type == typeof(Vector3Int)) value = new Vector3Int((int)components[0], (int)components[1], (int)components[2]);
            if (type == typeof(Quaternion)) value = new Quaternion((float)components[0], (float)components[1], (float)components[2], (float)components[3]);
            if (type == typeof(Color)) value = new Color((float)components[0], (float)components[1], (float)components[2], (float)components[3]);
            if (type == typeof(Color32)) value = new Color32((byte)components[0], (byte)components[1], (byte)components[2], (byte)components[3]);
            if (type == typeof(Rect)) value = new Rect((float)components[0], (float)components[1], (float)components[2], (float)components[3]);
            if (type == typeof(RectInt)) value = new RectInt((int)components[0], (int)components[1], (int)components[2], (int)components[3]);
            if (type == typeof(Bounds)) value = new Bounds(new Vector3((float)components[0], (float)components[1], (float)components[2]),
                new Vector3((float)components[3], (float)components[4], (float)components[5]));
            return true;
        }

        private static JObject defaultsV3;
        internal static void ValidateGradientStorage(WhimTexGradient gradient)
        {
            if (gradient == null) return;
            if (gradient.Interpolation == WhimTexGradientInterpolation.Custom && !gradient.HasCustomCurves)
                throw new WhimTexDocumentException("Custom interpolation requires saved customKeys.");
            // Validate dormant data too, without clearing the saved approximation diagnostic.
            var automatic = gradient.Clone();
            automatic.Interpolation = WhimTexGradientInterpolation.Balanced;
            automatic.Evaluate(0);
            if (!Enum.IsDefined(typeof(WhimTexGradientColorMode), gradient.CustomColorMode))
                throw new WhimTexDocumentException("Invalid Custom color mode.");
            if (gradient.HasCustomCurves)
            {
                var manual = gradient.Clone();
                manual.Interpolation = WhimTexGradientInterpolation.Custom;
                manual.Evaluate(0);
            }
            if (float.IsNaN(gradient.CustomApproximationError) || float.IsInfinity(gradient.CustomApproximationError) || gradient.CustomApproximationError < 0)
                throw new WhimTexDocumentException("Custom approximation error must be finite and nonnegative.");
            gradient.Evaluate(0);
        }

        private static JObject DefaultsFor(Type type)
        {
            if (defaultsV3 == null)
            {
                defaultsV3 = JObject.Parse(WhimTexJsonDefaultsV3.Data);
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV3.CustomGradientData).Properties())
                    defaultsV3["WhimTexGradient"][entry.Name] = entry.Value;
                defaultsV3["WhimTexGradientCurveKey"] = JObject.Parse(WhimTexJsonDefaultsV3.CustomGradientKeyData);
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV3.LayoutData).Properties())
                    defaultsV3[entry.Name] = entry.Value;
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV3.VectorData).Properties())
                    defaultsV3[entry.Name] = entry.Value;
                defaultsV3["VectorShape"]["gradient"] = defaultsV3["WhimTexGradient"].DeepClone();
                defaultsV3["VectorShape"]["strokeGradient"] = defaultsV3["WhimTexGradient"].DeepClone();
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV3.LayerBooleanData).Properties())
                    defaultsV3["Layer"][entry.Name] = entry.Value;
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV3.ShapeFillData).Properties())
                    defaultsV3["ShapeLayerBehaviour"][entry.Name] = entry.Value;
                defaultsV3["ShapeLayerBehaviour"]["gradient"] = defaultsV3["WhimTexGradient"].DeepClone();
                defaultsV3["ShapeLayerBehaviour"]["strokeGradient"] = defaultsV3["WhimTexGradient"].DeepClone();
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV3.PreviewControlData).Properties())
                    defaultsV3["ShaderFXParameterControl"][entry.Name] = entry.Value;
                defaultsV3["ShaderFXParameterControl"]["containers"] = new JArray();
                defaultsV3["ShaderFXParameterControl"]["enableIf"] = new JArray();
                foreach (var modelName in new[] { "ShaderFXParameter", "ShaderFXParameterControl" })
                    foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV3.CyclicRangeData).Properties())
                        defaultsV3[modelName][entry.Name] = entry.Value.DeepClone();
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV3.TextFillData).Properties())
                    defaultsV3["TextLayerBehaviour"][entry.Name] = entry.Value;
                defaultsV3["TextLayerBehaviour"]["gradient"] = defaultsV3["WhimTexGradient"].DeepClone();
                defaultsV3["GradientLayerBehaviour"]["freeform"] = JObject.Parse(@"{""points"":[
                    {""position"":[0.25,0.3],""color"":[0.95,0.35,0.2,1],""influence"":0.35},
                    {""position"":[0.75,0.3],""color"":[0.2,0.5,0.95,1],""influence"":0.35},
                    {""position"":[0.5,0.75],""color"":[0.7,0.25,0.85,1],""influence"":0.35}]}");
                foreach (string model in new[] { "ShapeLayerBehaviour", "VectorShape" })
                {
                    defaultsV3[model]["freeform"] = defaultsV3["GradientLayerBehaviour"]["freeform"].DeepClone();
                    defaultsV3[model]["strokeFreeform"] = defaultsV3["GradientLayerBehaviour"]["freeform"].DeepClone();
                }
                defaultsV3["FreeformGradientPoint"] = JObject.Parse(@"{""position"":[0.5,0.5],""color"":[1,1,1,1],""influence"":0.35}");
            }
            return defaultsV3[TypeName(type)] as JObject;
        }

        private static bool IsDefault(Type type, string field, JToken value)
        {
            if (field == "id" || field == "behaviour" || field == "behaviourId" || field == "children" || field == "fx") return false;
            if (type == typeof(GradientLayerBehaviour) && field == "gradientType") return false;
            if (type == typeof(WhimTexGradient) && field == "customColorMode") return false;
            return DefaultsFor(type)?[field] is JToken baseline && JToken.DeepEquals(value, baseline);
        }

        private static bool IsActive(object value, string field)
        {
            if (value is TargetedLayerBehaviour targeted && field == "targetLayerId") return targeted.inputMode == EffectInputMode.Specific;
            if (value is GradientLayerBehaviour gradient && (field == "circularRepetitions" || field == "circularWrapMode" || field == "circularStartAngle"))
                return gradient.gradientType == GradientLayerBehaviour.GradientType.Angular;
            if (value is BlurLayerBehaviour blur)
            {
                if (field == "radius") return blur.mode == BlurType.Gaussian;
                if (field == "distance" || field == "angle") return blur.mode == BlurType.Linear;
                if (field == "arc" || field == "center") return blur.mode == BlurType.Circular;
                if (field == "direction") return blur.mode != BlurType.Gaussian;
            }
            if (value is NoiseLayerBehaviour noise)
            {
                if (field == "vectorOutput" || field == "normalize" || field == "strength") return noise.IsVectorField;
                if (field == "encoding") return !noise.IsVectorField;
                if (field == "gradient") return !noise.IsVectorField && noise.EffectiveOutput == NoiseLayerBehaviour.OutputEncoding.Gradient;
                if (field == "grainColor" || field == "grainSize") return noise.IsGrain;
                if (field == "direction" || field == "periodic1D") return !noise.IsGrain && noise.EffectiveDimensions == NoiseLayerBehaviour.NoiseDimensions.OneD;
                if (field == "periodic") return !noise.IsGrain && noise.EffectiveDimensions != NoiseLayerBehaviour.NoiseDimensions.OneD;
                if (field == "cellularReturn") return !noise.IsVectorField && noise.EffectiveNoiseType == NoiseLayerBehaviour.NoiseType.Cellular;
                if (field == "cellularDistance" || field == "cellularJitter") return noise.EffectiveNoiseType == NoiseLayerBehaviour.NoiseType.Cellular;
                if (field == "warpStrength" || field == "warpSeed" || field == "warpScale" || field == "warpScaleY" || field == "warpScaleZ" || field == "linkWarpScale") return !noise.IsGrain && noise.warp != NoiseLayerBehaviour.WarpType.None;
                if (field == "fractal") return !noise.IsGrain && noise.field != NoiseLayerBehaviour.Field.CellDirection;
                if (field == "warp") return !noise.IsGrain;
                if (field == "octaves" || field == "gain" || field == "lacunarity" || field == "weightedStrength") return !noise.IsGrain && noise.EffectiveFractal != NoiseLayerBehaviour.FractalType.None;
                if (field == "pingPongStrength") return !noise.IsGrain && noise.EffectiveFractal == NoiseLayerBehaviour.FractalType.PingPong;
            }
            if (value is SDFLayerBehaviour sdf && field == "gradient") return sdf.encoding == SDFLayerBehaviour.OutputEncoding.Gradient;
            if (value is OutlineLayerBehaviour outline && field == "fillColor") return outline.fillCenter;
            if (value is ShapeLayerBehaviour shape)
            {
                bool arc = shape.kind == ShapeLayerBehaviour.ShapeKind.Arc;
                // Keep both paint styles, as for VectorShape, even when their gates or modes are inactive.
                if (field == "fillColor" || field == "fillMode" || field == "gradient" ||
                    field == "gradientStart" || field == "gradientEnd" || field == "gradientRepetitions") return true;
                if (field == "strokeMode" || field == "strokeGradient" || field == "strokeGradientStart" ||
                    field == "strokeGradientEnd" || field == "strokeGradientRepetitions") return true;
                if (field == "strokeWidth" || field == "strokeColor" || field == "strokePosition" ||
                    field == "strokeJoin" || field == "strokeCap" || field == "miterLimit" ||
                    field == "dashPattern" || field == "dashOffset") return true;
                if (field == "arcThickness") return arc;
                if (field == "lineCap") return arc || shape.kind == ShapeLayerBehaviour.ShapeKind.Line;
                if (field == "feather" || field == "featherPosition") return shape.edgeMode != ShapeLayerBehaviour.EdgeMode.Step && (field == "feather" || shape.feather != 0);
                if (field == "startAngle" || field == "sweepAngle") return arc || shape.kind == ShapeLayerBehaviour.ShapeKind.Sector;
                if (field == "innerRadius") return shape.kind == ShapeLayerBehaviour.ShapeKind.Star;
                if (field == "sides") return shape.kind == ShapeLayerBehaviour.ShapeKind.Polygon || shape.kind == ShapeLayerBehaviour.ShapeKind.Star;
                if (field == "rectangleCorners") return shape.kind == ShapeLayerBehaviour.ShapeKind.Rectangle;
                if (field == "polygonCorners") return shape.kind == ShapeLayerBehaviour.ShapeKind.Polygon;
                if (field == "outerCorner" || field == "innerCorner") return shape.kind == ShapeLayerBehaviour.ShapeKind.Star ||
                    (shape.kind == ShapeLayerBehaviour.ShapeKind.Sector && shape.sweepAngle < 360);
                if (field == "linkCorners") return shape.kind == ShapeLayerBehaviour.ShapeKind.Rectangle || shape.kind == ShapeLayerBehaviour.ShapeKind.Polygon;
            }
            if (value is ColorFillLayerBehaviour fill && field == "pattern") return fill.mode != ColorFillLayerBehaviour.FillMode.Color;
            if (value is MakeSeamlessLayerBehaviour seam)
            {
                string mode = seam.EffectiveMode.ToString();
                if (field.StartsWith("quilting", StringComparison.Ordinal)) return mode == "PatchQuilting" &&
                    (field != "quiltingContrast" || seam.quiltingContrastCompensation) &&
                    (field != "quiltingPoissonEdges" && field != "quiltingCorrectionRadius" || seam.quiltingSeamCorrection);
                if (field.StartsWith("mirror", StringComparison.Ordinal) || field == "horizontal" || field == "vertical" || field == "blendWidth" || field == "falloff")
                    return mode == "Mirror" && (field != "mirrorContrast" || seam.mirrorContrastCompensation) &&
                        (field != "mirrorPoissonEdges" && field != "mirrorAutoRadius" && field != "mirrorCorrectionRadius" || seam.mirrorSeamCorrection) &&
                        (field != "mirrorCorrectionRadius" || !seam.mirrorAutoRadius);
                if (field.StartsWith("offset", StringComparison.Ordinal) || field == "edgeWidth" || field == "histogramContrast" ||
                    field == "leftEdge" || field == "rightEdge" || field == "topEdge" || field == "bottomEdge")
                    return mode == "OffsetBlend" && (field != "histogramContrast" || seam.offsetContrastCompensation) &&
                        (field != "offsetPoissonEdges" && field != "offsetAutoRadius" && field != "offsetCorrectionRadius" || seam.offsetSeamCorrection) &&
                        (field != "offsetCorrectionRadius" || !seam.offsetAutoRadius);
                if (field == "poissonEdges" || field == "screeningRadius") return mode == "ScreenedPoisson";
            }
            if (value is ShaderFXParameter p)
            {
                switch (field)
                {
                    case "floatValue": return p.type == ShaderFXParameterType.Float || p.type == ShaderFXParameterType.Enum || p.type == ShaderFXParameterType.Bool;
                    case "colorValue": return p.type == ShaderFXParameterType.Color;
                    case "vectorValue": return p.type == ShaderFXParameterType.Vector || p.type == ShaderFXParameterType.Vector2 || p.type == ShaderFXParameterType.Vector3 || p.type == ShaderFXParameterType.Normal || p.type == ShaderFXParameterType.Point;
                    case "gradientValue": return p.type == ShaderFXParameterType.Gradient;
                    case "curveValue": return p.type == ShaderFXParameterType.Curve;
                    case "transformValue": return p.type == ShaderFXParameterType.Transform2D;
                    case "textureSource": return p.type == ShaderFXParameterType.Texture2D;
                    case "textureLayerId": return p.type == ShaderFXParameterType.Texture2D && p.textureSource == ShaderFXTextureSource.Layer;
                    case "textureValue": return p.type == ShaderFXParameterType.Texture2D && p.textureSource == ShaderFXTextureSource.Texture;
                }
            }
            return true;
        }
    }
}
