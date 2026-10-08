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

        private static JObject defaultsV1;
        private static JObject DefaultsFor(Type type)
        {
            defaultsV1 ??= JObject.Parse(WhimTexJsonDefaultsV1.Data);
            return defaultsV1[TypeName(type)] as JObject;
        }

        private static bool IsDefault(Type type, string field, JToken value)
        {
            if (field == "id" || field == "behaviour" || field == "behaviourId" || field == "children" || field == "fx") return false;
            return DefaultsFor(type)?[field] is JToken baseline && JToken.DeepEquals(value, baseline);
        }

        private static bool IsActive(object value, string field)
        {
            if (value is TargetedLayerBehaviour targeted && field == "targetLayerId") return targeted.inputMode == EffectInputMode.Specific;
            if (value is GradientLayerBehaviour gradient && (field == "circularRepetitions" || field == "circularWrapMode"))
                return gradient.gradientType == GradientLayerBehaviour.GradientType.Circular;
            if (value is BlurLayerBehaviour blur)
            {
                if (field == "radius") return blur.mode == BlurType.Gaussian;
                if (field == "distance" || field == "angle") return blur.mode == BlurType.Linear;
                if (field == "arc" || field == "center") return blur.mode == BlurType.Circular;
                if (field == "direction") return blur.mode != BlurType.Gaussian;
            }
            if (value is NoiseLayerBehaviour noise)
            {
                if (field == "gradient") return noise.EffectiveOutput == NoiseLayerBehaviour.OutputEncoding.Gradient;
                if (field == "whiteNoiseColor" || field == "whiteNoiseSize") return noise.IsGrain;
                if (field == "direction" || field == "periodic1D") return noise.dimensions == NoiseLayerBehaviour.NoiseDimensions.OneD;
                if (field == "periodic") return !noise.IsGrain && noise.dimensions != NoiseLayerBehaviour.NoiseDimensions.OneD;
                if (field == "cellularDistance" || field == "cellularReturn" || field == "cellularJitter") return noise.noiseType == NoiseLayerBehaviour.NoiseType.Cellular;
                if (field == "warpStrength" || field == "warpScale" || field == "warpScaleY" || field == "linkWarpScale") return !noise.IsGrain && noise.warp != NoiseLayerBehaviour.WarpType.None;
                if (field == "fractal" || field == "warp") return !noise.IsGrain;
                if (field == "octaves" || field == "gain" || field == "lacunarity" || field == "weightedStrength") return !noise.IsGrain && noise.fractal != NoiseLayerBehaviour.FractalType.None;
                if (field == "pingPongStrength") return !noise.IsGrain && noise.fractal == NoiseLayerBehaviour.FractalType.PingPong;
            }
            if (value is SDFLayerBehaviour sdf && field == "gradient") return sdf.encoding == SDFLayerBehaviour.OutputEncoding.Gradient;
            if (value is OutlineLayerBehaviour outline && field == "fillColor") return outline.fillCenter;
            if (value is ShapeLayerBehaviour shape)
            {
                if (field == "fillColor") return shape.fill;
                if (field == "strokeWidth" || field == "strokeColor") return shape.stroke;
                if (field == "featherPosition") return shape.feather != 0;
                if (field == "innerRadius") return shape.kind == ShapeLayerBehaviour.ShapeKind.Star;
                if (field == "sides") return shape.kind == ShapeLayerBehaviour.ShapeKind.Polygon || shape.kind == ShapeLayerBehaviour.ShapeKind.Star;
                if (field == "cornerRoundness" || field == "linkCorners") return shape.kind == ShapeLayerBehaviour.ShapeKind.Rectangle;
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
