using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFXParameter
    {
        public string valueType;
        public long[] integerValues = new long[4];
        internal string ValueType => !string.IsNullOrEmpty(valueType) ? valueType : ShaderFXNumericValue.NativeType(type);
        internal int Components => ShaderFXNumericValue.Components(ValueType);
        internal bool IsInteger => ValueType == "int" || ValueType == "uint" || ValueType == "bool" ||
            ValueType?.StartsWith("int", StringComparison.Ordinal) == true || ValueType?.StartsWith("uint", StringComparison.Ordinal) == true ||
            ValueType?.StartsWith("bool", StringComparison.Ordinal) == true;
        internal bool IsBoolean => ValueType?.StartsWith("bool", StringComparison.Ordinal) == true;
        internal double GetNumber(int component = 0)
        {
            if (IsInteger) return integerValues != null && component < integerValues.Length ? integerValues[component] : 0;
            if (type == ShaderFXParameterType.Color) return colorValue[component];
            return Components == 1 ? floatValue : vectorValue[component];
        }
        internal void SetNumber(int component, double value)
        {
            value = ShaderFXNumericValue.Check(ValueType, value);
            if (IsInteger)
            {
                if (integerValues == null || integerValues.Length != 4) integerValues = new long[4];
                integerValues[component] = (long)value;
            }
            else if (type == ShaderFXParameterType.Color) colorValue[component] = (float)value;
            else if (Components == 1) floatValue = (float)value;
            else vectorValue[component] = (float)value;
        }
        internal Color GetNumericColor()
        {
            var result = new Color(0, 0, 0, 1);
            for (int i = 0; i < Components; i++) result[i] = (float)GetNumber(i);
            return result;
        }
        internal void SetNumericColor(Color value)
        {
            for (int i = 0; i < Components; i++)
            {
                double number = value[i];
                if (IsInteger && value[i] == (float)GetNumber(i)) continue;
                if (IsBoolean) number = number >= .5 ? 1 : 0;
                else if (IsInteger) number = Math.Round(number, MidpointRounding.AwayFromZero);
                number = Math.Max(ShaderFXNumericValue.Minimum(ValueType), Math.Min(ShaderFXNumericValue.Maximum(ValueType), number));
                SetNumber(i, number);
            }
        }
        internal string TypedProperty(int index) => InternalPrefix + "Value" + index;
        [NonSerialized] private int[] typedPropertyIds;
        internal void SetTypedValue(Material material, ShaderFXParameter declaration)
        {
            if (declaration.typedPropertyIds == null)
            {
                declaration.typedPropertyIds = new int[Components];
                for (int i = 0; i < Components; i++) declaration.typedPropertyIds[i] = Shader.PropertyToID(declaration.TypedProperty(i));
            }
            Color decoded = type == ShaderFXParameterType.Color && !IsInteger ? HdrUtility.Decode(GetNumericColor()) : Color.clear;
            for (int i = 0; i < Components; i++)
                if (IsInteger) material.SetInteger(declaration.typedPropertyIds[i], unchecked((int)(long)GetNumber(i)));
                else material.SetFloat(declaration.typedPropertyIds[i], type == ShaderFXParameterType.Color ? decoded[i] : (float)GetNumber(i));
        }
    }

    internal static class ShaderFXNumericValue
    {
        internal static bool IsType(string type) => Components(type) != 0;
        internal static int Components(string type)
        {
            if (string.IsNullOrEmpty(type)) return 0;
            int length = type.StartsWith("float", StringComparison.Ordinal) ? 5 :
                type.StartsWith("bool", StringComparison.Ordinal) || type.StartsWith("uint", StringComparison.Ordinal) || type.StartsWith("half", StringComparison.Ordinal) ? 4 :
                type.StartsWith("int", StringComparison.Ordinal) ? 3 : 0;
            if (length == 0) return 0;
            if (type.Length == length) return 1;
            return type.Length == length + 1 && type[length] >= '2' && type[length] <= '4' ? type[length] - '0' : 0;
        }
        internal static string NativeType(ShaderFXParameterType type)
        {
            switch (type)
            {
                case ShaderFXParameterType.Float: case ShaderFXParameterType.Toggle: case ShaderFXParameterType.Enum: case ShaderFXParameterType.Seed: return "float";
                case ShaderFXParameterType.Vector2: case ShaderFXParameterType.Point: return "float2";
                case ShaderFXParameterType.Vector3: case ShaderFXParameterType.Normal: return "float3";
                case ShaderFXParameterType.Color: case ShaderFXParameterType.Vector: return "float4";
                default: return null;
            }
        }
        internal static double Minimum(string type) => type.StartsWith("uint") || type.StartsWith("bool") ? 0d : type.StartsWith("int") ? (double)int.MinValue : type.StartsWith("half") ? -65504d : -(double)float.MaxValue;
        internal static double Maximum(string type) => type.StartsWith("bool") ? 1d : type.StartsWith("uint") ? (double)uint.MaxValue : type.StartsWith("int") ? (double)int.MaxValue : type.StartsWith("half") ? 65504d : (double)float.MaxValue;
        internal static double Check(string type, double number)
        {
            if (!IsType(type) || double.IsNaN(number) || double.IsInfinity(number) || number < Minimum(type) || number > Maximum(type))
                throw new FormatException("Value is outside the range of " + type + ".");
            if ((type.StartsWith("int") || type.StartsWith("uint") || type.StartsWith("bool")) && number != Math.Truncate(number))
                throw new FormatException(type + " requires integral values.");
            return type.StartsWith("float") || type.StartsWith("half") ? (float)number : number;
        }
        internal static double Parse(string text)
        {
            if (text.Trim() == "true") return 1;
            if (text.Trim() == "false") return 0;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || double.IsNaN(result) || double.IsInfinity(result))
                throw new FormatException("Expected a finite number or true/false.");
            return result;
        }
        internal static bool NeedsTypedBinding(ShaderFXParameter p) => p.ValueType != null && p.ValueType != NativeType(p.type);
        internal static void Declare(ShaderFXParameter p, StringBuilder properties, StringBuilder uniforms)
        {
            var expression = new StringBuilder(p.ValueType + "(");
            for (int i = 0; i < p.Components; i++)
            {
                string name = p.TypedProperty(i);
                properties.AppendLine($"[HideInInspector] {name} (\"{p.name}\", {(p.IsInteger ? "Integer" : "Float")}) = 0");
                uniforms.AppendLine($"{(p.IsInteger ? "int" : "float")} {name};");
                if (i != 0) expression.Append(", ");
                expression.Append(p.IsBoolean ? "(" + name + " != 0)" : p.ValueType.StartsWith("uint") ? "asuint(" + name + ")" : name);
            }
            uniforms.AppendLine($"#define {p.name} ({expression}))");
        }
        internal static bool TryCondition(ShaderFXParameter p, string reference, out double value)
        {
            int dot = reference.IndexOf('.');
            int component = dot < 0 ? 0 : "xyzw".IndexOf(reference[dot + 1]);
            if (component < 0 && dot >= 0) component = "rgba".IndexOf(reference[dot + 1]);
            value = 0;
            if (p.Components == 0 || component < 0 || component >= p.Components || dot < 0 && p.Components != 1) return false;
            value = p.GetNumber(component); return true;
        }
    }
}
