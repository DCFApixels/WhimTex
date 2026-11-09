using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    internal static class ShaderParameterDeclarations
    {
        internal static string Control(ShaderParameter parameter, ShaderParameterControl control, bool includeDefault = true)
        {
            var row = includeDefault ? parameter.Copy() : parameter.CopyDeclaration(); row.valueType = control.valueType ?? parameter.ValueType; row.type = control.type;
            row.hasMinimum = control.hasMinimum; row.hasMaximum = control.hasMaximum;
            row.softMinimum = control.softMinimum; row.softMaximum = control.softMaximum;
            row.cyclicRange = control.cyclicRange; row.minimum = control.minimum; row.maximum = control.maximum;
            // Bound graph values are uploaded separately, including non-exportable resources.
            if (!includeDefault)
            {
                row.floatValue = 0; row.colorValue = UnityEngine.Color.clear; row.vectorValue = default;
                row.integerValues = new long[4]; row.transformValue = ShaderTransform.Default;
                row.gradientValue = null; row.curveValue = null; row.SetTextureValue(null);
            }
            string text;
            if (!includeDefault && row.type is ShaderParameterType.Gradient or ShaderParameterType.Curve or ShaderParameterType.Texture2D)
                text = "// @param " + (row.type == ShaderParameterType.Texture2D ? "texture2D" : row.type == ShaderParameterType.Gradient ? "gradient" : "curve") + " " + row.name;
            else if (control.type is ShaderParameterType.Enum or ShaderParameterType.Toggle or ShaderParameterType.Seed or ShaderParameterType.Color)
                text = TypedDeclaration(row, control);
            else text = Declaration(row);
            if (!includeDefault) text = Regex.Replace(text, @"\s*=\s*[^\[\{~]+?(?=\s*[\[\{~]|$)", "");
            string modifiers = control.hidden ? "hidden " : string.Empty;
            if (control.rgba && control.type != ShaderParameterType.Color) modifiers += "rgba ";
            if (!string.IsNullOrEmpty(control.previewFunction)) modifiers += "preview(" + ShaderPreviewFunction.Declaration(control) + ") ";
            if (!string.IsNullOrWhiteSpace(control.label)) modifiers += "label(\"" + control.label.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\") ";
            text = text.Insert("// @param ".Length, modifiers);
            if (!string.IsNullOrEmpty(control.tooltip)) text += " // " + control.tooltip;
            if (control.helpBoxes != null && control.helpBoxes.Length > 0) text = "// @helpbox(" + string.Join(")\n// @helpbox(", control.helpBoxes) + ")\n" + text;
            if (control.headers != null && control.headers.Length > 0) text = "// @header(" + string.Join(")\n// @header(", control.headers) + ")\n" + text;
            return text;
        }

        private static string TypedDeclaration(ShaderParameter p, ShaderParameterControl control)
        {
            string[] components = new string[p.Components];
            for (int i = 0; i < components.Length; i++) components[i] = p.IsBoolean ? p.GetNumber(i) == 0 ? "false" : "true" : Number(p.GetNumber(i));
            string result = "// @param " + control.type.ToString().ToLowerInvariant() + "(" + p.ValueType + ") " + p.name + " = " +
                (components.Length == 1 ? components[0] : "(" + string.Join(", ", components) + ")");
            if (control.type == ShaderParameterType.Seed) result += " [" + (control.softMinimum ? "~" : "") + Number(control.valueMinimum) + " .. " + (control.softMaximum ? "~" : "") + Number(control.valueMaximum) + "]";
            if (control.type == ShaderParameterType.Enum)
            {
                var options = new System.Collections.Generic.List<string>();
                for (int i = 0; i < control.optionNames.Length; i++) options.Add(control.optionNames[i] + ": " + Number(control.optionValues[i]));
                result += " { " + string.Join(", ", options) + " }";
            }
            return result;
        }

        internal static bool CanDeclareDefault(ShaderParameterControl control, ShaderParameter parameter)
        {
            float value = parameter.floatValue;
            if (control.type == ShaderParameterType.Vector2 && control.hasMinimum && control.hasMaximum)
                return ShaderParameter.IsValidInterval(parameter.vectorValue, control.minimum, control.maximum, control.cyclicRange);
            if (control.type == ShaderParameterType.Toggle)
            {
                for (int i = 0; i < parameter.Components; i++) if (parameter.GetNumber(i) != 0 && parameter.GetNumber(i) != 1) return false;
                return true;
            }
            if (control.type != ShaderParameterType.Float) return true;
            return !(control.hasMinimum && !control.softMinimum && value < control.minimum ||
                control.hasMaximum && !control.softMaximum && value > control.maximum);
        }

        private static string Number(double value)
        {
            if (!ProjectiveMatrix.Finite(value)) throw new FormatException("Preset defaults must be finite numbers.");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new FormatException("Preset defaults must be finite numbers.");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string ColorDefault(UnityEngine.Color color) => "(" + Number(color.r) + ", " + Number(color.g) + ", " +
            Number(color.b) + ", " + Number(color.a) + ")";

        internal static string Declaration(ShaderParameter p)
        {
            string prefix = "// @param " + (p.pixelUnits ? "px " : "");
            switch (p.type)
            {
                case ShaderParameterType.Curve:
                    return prefix + "curve " + p.name + " = " + WhimTexCurveTexture.Format(p.curveValue);
                case ShaderParameterType.Gradient:
                    if (p.gradientValue != null && p.gradientValue.HasCustomCurves)
                        throw new FormatException("Endpoint-only HLSL gradient defaults cannot store active or retained Custom curves. Keep this gradient in the document or save it as a gradient preset.");
                    if (!GradientUtility.IsTwoColorGradient(p.gradientValue, out var left, out var right))
                        throw new FormatException("Gradient preset defaults currently support two endpoint colors only. Simplify the gradient before saving this preset.");
                    return prefix + "gradient " + p.name + " = " + ColorDefault(left) + " -> " + ColorDefault(right);
                case ShaderParameterType.Toggle:
                    return prefix + "toggle(float) " + p.name + " = " + (p.BoolValue ? "true" : "false");
                case ShaderParameterType.Float:
                case ShaderParameterType.Enum:
                    return prefix + "float " + p.name + " = " + Number(p.floatValue) +
                        (p.hasMinimum || p.hasMaximum ? " [" + (p.softMinimum ? "~" : "") + (p.hasMinimum ? Number(p.minimum) : "") + (p.cyclicRange ? " % " : " .. ") + (p.softMaximum ? "~" : "") + (p.hasMaximum ? Number(p.maximum) : "") + "]" : "");
                case ShaderParameterType.Color:
                    var c = p.colorValue;
                    return prefix + "color " + p.name + " = (" + Number(c.r) + ", " + Number(c.g) + ", " + Number(c.b) + ", " + Number(c.a) + ")";
                case ShaderParameterType.Vector2:
                case ShaderParameterType.Point:
                case ShaderParameterType.Vector3:
                case ShaderParameterType.Normal:
                    var n = p.type == ShaderParameterType.Normal ? (UnityEngine.Vector4)ShaderParameter.NormalizeNormal(p.vectorValue) : p.vectorValue;
                    string kind = p.type == ShaderParameterType.Normal ? "normal" : p.type == ShaderParameterType.Point ? "point" : p.type == ShaderParameterType.Vector2 ? "float2" : "float3";
                    return prefix + kind + " " + p.name + " = (" + Number(n.x) + ", " + Number(n.y) + (p.type == ShaderParameterType.Vector2 || p.type == ShaderParameterType.Point ? "" : ", " + Number(n.z)) + ")" +
                        (p.IsInterval ? " [" + Number(p.minimum) + (p.cyclicRange ? " % " : " .. ") + Number(p.maximum) + "]" : "");
                case ShaderParameterType.Vector:
                    var v = p.vectorValue;
                    return prefix + "float4 " + p.name + " = (" + Number(v.x) + ", " + Number(v.y) + ", " + Number(v.z) + ", " + Number(v.w) + ")";
                case ShaderParameterType.Transform2D:
                    var t = p.transformValue;
                    if (t.storage == TransformStorage.Projective)
                    {
                        var m = t.matrix;
                        return prefix + "transform2D " + p.name + " = matrix(" +
                            Number(m.m00) + ", " + Number(m.m01) + ", " + Number(m.m02) + ", " +
                            Number(m.m10) + ", " + Number(m.m11) + ", " + Number(m.m12) + ", " +
                            Number(m.m20) + ", " + Number(m.m21) + ", " + Number(m.m22) + ")";
                    }
                    return prefix + "transform2D " + p.name + " = (" + Number(t.position.x) + ", " + Number(t.position.y) + ", " + Number(t.size.x) + ", " + Number(t.size.y) + ", " + Number(t.rotation) + ")";
                case ShaderParameterType.Texture2D:
                    string declaration = prefix + "texture2D " + p.name;
                    if (p.textureSource == ShaderTextureSource.None) return declaration + " = none";
                    if (p.textureSource == ShaderTextureSource.Self) return declaration + " = self";
                    if (p.textureSource == ShaderTextureSource.Layer) return declaration;
                    string guid;
                    long localId;
                    if (p.textureValue == null)
                    {
                        WhimTexDocumentJson.PreserveShaderParameterAssets(p, p);
                        if (string.IsNullOrEmpty(p.missingTextureAsset)) return declaration;
                        var asset = Newtonsoft.Json.Linq.JObject.Parse(p.missingTextureAsset)["$asset"];
                        guid = (string)asset?["guid"];
                        if (!Guid.TryParseExact(guid, "N", out _) ||
                            !long.TryParse((string)asset?["localId"], NumberStyles.Integer, CultureInfo.InvariantCulture, out localId))
                            throw new FormatException("Texture " + p.name + " has no valid asset identity for a preset default.");
                    }
                    else if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(p.textureValue, out guid, out localId) || string.IsNullOrEmpty(guid))
                        throw new IOException("Texture " + p.name + " must be a saved project asset to be used as a preset default.");
                    return declaration + " = \"guid:" + guid + ":" + localId.ToString(CultureInfo.InvariantCulture) + "\"";
                default: throw new FormatException("Unsupported parameter type: " + p.type);
            }
        }

    }
}
