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
                if (type == typeof(ShaderGraphNodeKind) && token.Type == JTokenType.String)
                {
                    ShaderGraphNodeKind? renamed = (string)token switch
                    {
                        "ChromaticAberration" => ShaderGraphNodeKind.RGBShift,
                        "Exp" => ShaderGraphNodeKind.Exponential,
                        "Abs" => ShaderGraphNodeKind.Absolute,
                        "Asin" => ShaderGraphNodeKind.Arcsine,
                        "Acos" => ShaderGraphNodeKind.Arccosine,
                        "Atan2" => ShaderGraphNodeKind.Arctangent2,
                        "Min" => ShaderGraphNodeKind.Minimum,
                        "Max" => ShaderGraphNodeKind.Maximum,
                        "Ceil" => ShaderGraphNodeKind.Ceiling,
                        "Cross" => ShaderGraphNodeKind.CrossProduct,
                        "Dot" => ShaderGraphNodeKind.DotProduct,
                        "Reflect" => ShaderGraphNodeKind.Reflection,
                        "Unlerp" => ShaderGraphNodeKind.InverseLerp,
                        "RotateVector" => ShaderGraphNodeKind.RotateAboutAxis,
                        _ => null
                    };
                    if (renamed.HasValue) return renamed.Value;
                }
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
            if (type.Assembly != typeof(WhimTexDocument).Assembly) return null;
            if (defaultsV1 == null)
            {
                defaultsV1 = JObject.Parse(WhimTexJsonDefaultsV1.Data);
                defaultsV1["WhimTexLight"]["angle"] = 60;
                defaultsV1["WhimTexLight"]["softness"] = .25f;
                defaultsV1["WhimTexLight"]["areaShape"] = "Rectangle";
                defaultsV1["WhimTexLight"]["size"] = new JArray(256, 256);
                defaultsV1["WhimTexLight"]["rotation"] = 0;
                defaultsV1["WhimTexLight"]["cookie"] = JValue.CreateNull();
                defaultsV1["WhimTexDocument"]["ambientColor"] = new JArray(1, 1, 1, 1);
                // Program is mandatory and retains its own constructor defaults. Old flat FX
                // fields must not supply defaults or erase the inactive program branch.
                defaultsV1["ShaderFX"] = JObject.Parse(WhimTexJsonDefaultsV1.ShaderFxData);
                defaultsV1["FileLayerBehaviour"]["externalSourcePath"] = JValue.CreateNull();
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV1.NumericParameterData).Properties())
                    defaultsV1["ShaderParameter"][entry.Name] = entry.Value;
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV1.NumericControlData).Properties())
                    defaultsV1["ShaderParameterControl"][entry.Name] = entry.Value;
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV1.CustomGradientData).Properties())
                    defaultsV1["WhimTexGradient"][entry.Name] = entry.Value;
                defaultsV1["WhimTexGradientCurveKey"] = JObject.Parse(WhimTexJsonDefaultsV1.CustomGradientKeyData);
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV1.LayoutData).Properties())
                    defaultsV1[entry.Name] = entry.Value;
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV1.VectorData).Properties())
                    defaultsV1[entry.Name] = entry.Value;
                defaultsV1["VectorShape"]["gradient"] = defaultsV1["WhimTexGradient"].DeepClone();
                defaultsV1["VectorShape"]["strokeGradient"] = defaultsV1["WhimTexGradient"].DeepClone();
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV1.LayerBooleanData).Properties())
                    defaultsV1["Layer"][entry.Name] = entry.Value;
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV1.ShapeFillData).Properties())
                    defaultsV1["ShapeLayerBehaviour"][entry.Name] = entry.Value;
                defaultsV1["ShapeLayerBehaviour"]["gradient"] = defaultsV1["WhimTexGradient"].DeepClone();
                defaultsV1["ShapeLayerBehaviour"]["strokeGradient"] = defaultsV1["WhimTexGradient"].DeepClone();
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV1.PreviewControlData).Properties())
                    defaultsV1["ShaderParameterControl"][entry.Name] = entry.Value;
                defaultsV1["ShaderParameterControl"]["containers"] = new JArray();
                defaultsV1["ShaderParameterControl"]["enableIf"] = new JArray();
                foreach (var modelName in new[] { "ShaderParameter", "ShaderParameterControl" })
                    foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV1.CyclicRangeData).Properties())
                        defaultsV1[modelName][entry.Name] = entry.Value.DeepClone();
                foreach (var entry in JObject.Parse(WhimTexJsonDefaultsV1.TextFillData).Properties())
                    defaultsV1["TextLayerBehaviour"][entry.Name] = entry.Value;
                defaultsV1["TextLayerBehaviour"]["gradient"] = defaultsV1["WhimTexGradient"].DeepClone();
                defaultsV1["GradientLayerBehaviour"]["freeform"] = JObject.Parse(@"{""colorMode"":""Perceptual"",""points"":[
                    {""position"":[0.25,0.3],""color"":[0.95,0.35,0.2,1],""influence"":0.35},
                    {""position"":[0.75,0.3],""color"":[0.2,0.5,0.95,1],""influence"":0.35},
                    {""position"":[0.5,0.75],""color"":[0.7,0.25,0.85,1],""influence"":0.35}]}");
                foreach (string model in new[] { "ShapeLayerBehaviour", "VectorShape" })
                {
                    defaultsV1[model]["freeform"] = defaultsV1["GradientLayerBehaviour"]["freeform"].DeepClone();
                    defaultsV1[model]["strokeFreeform"] = defaultsV1["GradientLayerBehaviour"]["freeform"].DeepClone();
                }
                defaultsV1["FreeformGradientPoint"] = JObject.Parse(@"{""position"":[0.5,0.5],""color"":[1,1,1,1],""influence"":0.35}");
            }
            return defaultsV1[TypeName(type)] as JObject;
        }

        private static bool IsDefault(Type type, string field, JToken value)
        {
            if (type == typeof(MorphGuideCorrection) && field is "width" or "strength" or "feather")
                return (float)value == 0f;
            // Auto is exactly zero. Keep even very small positive guide radii in Compact.
            if (field == "radius" && (type == typeof(MorphAnchor) || type == typeof(MorphSegment) || type == typeof(MorphGuidePath)))
                return (float)value == 0f;
            if (field == "id" || field == "behaviour" || field == "behaviourId" || field == "children" || field == "fx") return false;
            if (type == typeof(GradientLayerBehaviour) && (field == "gradientType" || field == "mesh")) return false;
            if (type == typeof(MeshGradient)) return false;
            if (type == typeof(WhimTexGradient) && field == "customColorMode") return false;
            return DefaultsFor(type)?[field] is JToken baseline && JToken.DeepEquals(value, baseline);
        }
    }
}
