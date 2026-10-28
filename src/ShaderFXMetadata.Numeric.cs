using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderFXMetadata
    {
        private static ShaderFXParameter ParseTypedParameter(string line, out bool explicitDefault, out ShaderFXParameterControl control)
        {
            explicitDefault = false; control = null;
            var match = Regex.Match(line, @"^\s*//\s*@param\s+(toggle|enum|seed|color)(?:\s*\(\s*([A-Za-z0-9]+)\s*\))?\s+([A-Za-z_]\w*)\s*(.*?)\s*$");
            if (!match.Success) return null;
            string field = match.Groups[1].Value;
            string type = match.Groups[2].Success ? match.Groups[2].Value : field == "color" ? "float4" : field == "seed" ? "int" : "float";
            int count = ShaderFXNumericValue.Components(type);
            if (count == 0 || (field == "enum" && (count != 1 || type == "bool")) ||
                (field == "seed" && type != "int" && type != "uint" && type != "float"))
                throw new FormatException("Unsupported " + field + " value type: " + type + ".");
            string rest = match.Groups[4].Value.Trim();
            var p = new ShaderFXParameter { name = match.Groups[3].Value, valueType = type,
                type = field == "color" ? ShaderFXParameterType.Color : count == 4 ? ShaderFXParameterType.Vector :
                    count == 3 ? ShaderFXParameterType.Vector3 : count == 2 ? ShaderFXParameterType.Vector2 :
                    field == "toggle" ? ShaderFXParameterType.Toggle : field == "enum" ? ShaderFXParameterType.Enum : ShaderFXParameterType.Float,
                floatValue = 0, colorValue = Color.clear };
            control = new ShaderFXParameterControl { valueType = type, type = field == "toggle" ? ShaderFXParameterType.Toggle :
                field == "enum" ? ShaderFXParameterType.Enum : field == "seed" ? ShaderFXParameterType.Seed : ShaderFXParameterType.Color,
                seed = field == "seed" };
            if (field == "enum")
            {
                var options = Regex.Match(rest, @"^(.*?)\{([^{}]+)\}\s*$");
                if (!options.Success) throw new FormatException("enum requires { Name: value, ... }.");
                rest = options.Groups[1].Value.Trim();
                var names = new List<string>(); var values = new List<double>();
                foreach (string item in options.Groups[2].Value.Split(','))
                {
                    var option = Regex.Match(item.Trim(), @"^([A-Za-z_]\w*)\s*:\s*(.+)$");
                    if (!option.Success) throw new FormatException("Enum items must be Name: number.");
                    string name = option.Groups[1].Value;
                    double number = ShaderFXNumericValue.Check(type, ShaderFXNumericValue.Parse(option.Groups[2].Value));
                    if (names.Contains(name) || values.Contains(number)) throw new FormatException("Enum names and values must be unique.");
                    names.Add(name); values.Add(number);
                }
                control.optionNames = names.ToArray(); control.optionValues = values.ToArray();
                p.SetNumber(0, values[0]);
            }
            if (field == "seed")
            {
                control.valueMinimum = type == "float" ? -ShaderFXSeed.Limit : ShaderFXNumericValue.Minimum(type);
                control.valueMaximum = type == "float" ? ShaderFXSeed.Limit : ShaderFXNumericValue.Maximum(type);
                var range = Regex.Match(rest, @"^(.*?)\[\s*(.*?)\s*\.\.\s*(.*?)\s*\]\s*$");
                if (range.Success)
                {
                    rest = range.Groups[1].Value.Trim();
                    p.softMinimum = control.softMinimum = range.Groups[2].Value.Trim().StartsWith("~");
                    p.softMaximum = control.softMaximum = range.Groups[3].Value.Trim().StartsWith("~");
                    control.valueMinimum = ShaderFXNumericValue.Check(type, ShaderFXNumericValue.Parse(range.Groups[2].Value.Trim().TrimStart('~')));
                    control.valueMaximum = ShaderFXNumericValue.Check(type, ShaderFXNumericValue.Parse(range.Groups[3].Value.Trim().TrimStart('~')));
                }
                if (control.valueMinimum > control.valueMaximum || control.valueMinimum != Math.Truncate(control.valueMinimum) ||
                    control.valueMaximum != Math.Truncate(control.valueMaximum) || type == "float" &&
                    (control.valueMinimum < -ShaderFXSeed.Limit || control.valueMaximum > ShaderFXSeed.Limit))
                    throw new FormatException("seed requires an ordered, exactly representable integer range.");
                p.hasMinimum = p.hasMaximum = control.hasMinimum = control.hasMaximum = true;
                p.minimum = control.minimum = (float)control.valueMinimum;
                p.maximum = control.maximum = (float)control.valueMaximum;
                p.SetNumber(0, Math.Max(control.valueMinimum, Math.Min(control.valueMaximum, 0)));
            }
            if (rest.Length == 0) return p;
            if (!rest.StartsWith("=", StringComparison.Ordinal)) throw new FormatException("Expected = default value after " + p.name + ".");
            explicitDefault = true; string value = rest.Substring(1).Trim();
            if (field == "enum")
            {
                int option = Array.IndexOf(control.optionNames, value);
                if (option >= 0) { p.SetNumber(0, control.optionValues[option]); return p; }
            }
            if (field == "color" && value.StartsWith("#", StringComparison.Ordinal))
            {
                p.SetNumericColor(ParseHexColor(value)); return p;
            }
            string[] components;
            if (count == 1) components = new[] { value };
            else
            {
                if (!value.StartsWith("(") || !value.EndsWith(")")) throw new FormatException("Expected vector components in parentheses.");
                components = value.Substring(1, value.Length - 2).Split(',');
            }
            if (components.Length != count) throw new FormatException("Expected " + count + " components for " + type + ".");
            for (int i = 0; i < count; i++)
            {
                double number = ShaderFXNumericValue.Parse(components[i]);
                if (field == "toggle" && number != 0 && number != 1) throw new FormatException("toggle values must be 0/1 or false/true.");
                if (field == "seed" && (number != Math.Truncate(number) || type == "float" && Math.Abs(number) > ShaderFXSeed.Limit || !control.softMinimum && number < control.valueMinimum || !control.softMaximum && number > control.valueMaximum))
                    throw new FormatException("seed default must be an integer inside its range.");
                p.SetNumber(i, number);
            }
            return p;
        }
    }
}
