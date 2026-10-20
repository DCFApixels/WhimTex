using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DCFApixels.WhimTex
{
    internal static class ShaderFXPreviewFunction
    {
        internal static void Parse(string text, out string function, out string[] parameters)
        {
            string[] parts = (text ?? "").Split(',');
            if (parts.Length > 129) throw new FormatException("A preview may reference at most 128 parameters.");
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
                if (!Regex.IsMatch(parts[i], @"^[A-Za-z_][A-Za-z0-9_]*$"))
                    throw new FormatException("Preview expects a function name followed by optional comma-separated @param names, not expressions or empty arguments.");
            }
            function = parts[0];
            parameters = parts.Length == 1 ? Array.Empty<string>() : new string[parts.Length - 1];
            if (parameters.Length > 0) Array.Copy(parts, 1, parameters, 0, parameters.Length);
        }

        internal static string Declaration(ShaderFXParameterControl control) => control.previewFunction +
            (control.previewParameters == null || control.previewParameters.Length == 0 ? "" : ", " + string.Join(", ", control.previewParameters));

        internal static string Call(string source, string function, ShaderFXParameter parameter, ShaderFXParameterType controlType,
            string[] previewParameters = null, IReadOnlyList<ShaderFXParameter> declarations = null)
        {
            if (!Regex.IsMatch(function ?? "", @"^[A-Za-z_][A-Za-z0-9_]*$"))
                throw new FormatException("Preview requires a function name.");
            var clean = new StringBuilder();
            bool block = false;
            using (var reader = new StringReader(source ?? ""))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    clean.AppendLine(ShaderFXSourceBuilder.MaskComments(line, ref block));
            }
            var matches = Regex.Matches(clean.ToString(), @"\bfloat4\s+" + Regex.Escape(function) + @"\s*\(([^()]*)\)\s*\{");
            if (matches.Count != 1)
                throw new FormatException("Preview " + function + " must have exactly one float4 function definition.");
            string[] arguments = matches[0].Groups[1].Value.Split(',');
            if (!Argument(arguments[0], "float2"))
                throw new FormatException("Preview " + function + " expects float2 uv first, followed by an optional parameter value.");
            if (previewParameters != null && previewParameters.Length > 0)
            {
                if (arguments.Length != previewParameters.Length + 1)
                    throw new FormatException("Preview " + function + " expects float2 uv followed by exactly " + previewParameters.Length + " listed parameter arguments.");
                var call = new StringBuilder(function + "(input.uv");
                for (int i = 0; i < previewParameters.Length; i++)
                {
                    string name = previewParameters[i];
                    ShaderFXParameter declared = null;
                    if (declarations != null)
                        foreach (var candidate in declarations)
                            if (candidate != null && candidate.name == name) { declared = candidate; break; }
                    if (declared == null) throw new FormatException("Preview " + function + " references undeclared @param " + name + ".");
                    string expected = ValueType(declared.type);
                    if (expected == null || !Argument(arguments[i + 1], expected))
                        throw new FormatException("Preview " + function + " argument " + (i + 1) + " for " + name + " must be " + expected + ".");
                    bool sweep = parameter != null && parameter.name == name && controlType == ShaderFXParameterType.Float;
                    call.Append(", ").Append(Value(declared, declared.type, sweep));
                }
                return call.Append(')').ToString();
            }
            if (arguments.Length > 2)
                throw new FormatException("Preview " + function + " expects float2 uv first, followed by an optional parameter value.");
            if (arguments.Length == 1) return function + "(input.uv)";
            if (parameter == null)
                throw new FormatException("Standalone preview " + function + " accepts only float2 uv.");
            string type = ValueType(controlType);
            if (type == null || !Argument(arguments[1], type))
                throw new FormatException("Preview " + function + " value must be " + (type ?? "omitted for this parameter type") + ".");
            return function + "(input.uv, " + Value(parameter, controlType, true) + ")";
        }

        private static string Value(ShaderFXParameter parameter, ShaderFXParameterType type, bool sweep)
        {
            string value = parameter.name;
            switch (type)
            {
                case ShaderFXParameterType.Float:
                    if (sweep) value = "lerp(" + value + ", lerp(_WhimTex_PreviewRange.x, _WhimTex_PreviewRange.y, input.uv.x), _WhimTex_PreviewRange.z)";
                    break;
                case ShaderFXParameterType.Bool: value = "(" + value + " >= 0.5)"; break;
                case ShaderFXParameterType.Gradient: value = parameter.InternalPrefix + "Gradient"; break;
                case ShaderFXParameterType.Curve: value = parameter.InternalPrefix + "Curve"; break;
                case ShaderFXParameterType.Transform2D:
                    string prefix = parameter.InternalPrefix + "ToLocalRow";
                    value = "float3x3(" + prefix + "0.xyz, " + prefix + "1.xyz, " + prefix + "2.xyz)";
                    break;
            }
            return value;
        }

        private static bool Argument(string argument, string type) =>
            Regex.IsMatch(argument.Trim(), @"^(?:in\s+)?" + Regex.Escape(type) + @"\s+[A-Za-z_][A-Za-z0-9_]*$");

        internal static string ValueType(ShaderFXParameterType type)
        {
            switch (type)
            {
                case ShaderFXParameterType.Float:
                case ShaderFXParameterType.Enum: return "float";
                case ShaderFXParameterType.Bool: return "bool";
                case ShaderFXParameterType.Point:
                case ShaderFXParameterType.Vector2: return "float2";
                case ShaderFXParameterType.Normal:
                case ShaderFXParameterType.Vector3: return "float3";
                case ShaderFXParameterType.Color:
                case ShaderFXParameterType.Vector: return "float4";
                case ShaderFXParameterType.Texture2D:
                case ShaderFXParameterType.Gradient:
                case ShaderFXParameterType.Curve: return "sampler2D";
                case ShaderFXParameterType.Transform2D: return "float3x3";
                default: return null;
            }
        }
    }
}
