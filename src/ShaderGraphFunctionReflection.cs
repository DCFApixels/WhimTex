using System;
using System.Collections.Generic;
#if UNITY_6000_5_OR_NEWER
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.ShaderApiReflection;
using UnityEngine;
#endif

namespace DCFApixels.WhimTex
{
    // The 6.3 path has no dependency on reflection APIs. No internal APIs or CLR reflection.
    internal static class ShaderGraphFunctionReflection
    {
        internal static void Append(string source, string path, List<ShaderGraphFunction> definitions, Action<string, string> report)
        {
#if UNITY_6000_5_OR_NEWER
            if (source.IndexOf("UNITY_EXPORT_REFLECTION", StringComparison.Ordinal) < 0) return;
            string assetPath = Path.IsPathRooted(path) ? PresetLibraryPaths.AssetPath(path) : path;
            if (string.IsNullOrEmpty(assetPath)) return;
            var asset = AssetDatabase.LoadAssetAtPath<ShaderInclude>(assetPath);
            if (asset == null) return;
            var reflection = asset.Reflection;
            if (reflection == null) return;
            foreach (var diagnostic in reflection.LogMessages) report(path, diagnostic.Text);
            var marked = source.IndexOf("@whimtex-node", StringComparison.Ordinal) >= 0 ? ShaderGraphFunctionParser.Parse(source, path) : null;
            foreach (var function in reflection.ReflectedFunctions)
            {
                int existingIndex = -1;
                try
                {
                    var candidate = new ShaderGraphFunction
                    {
                        inline = false, reflected = true, sourcePath = path, entryPoint = function.FullyQualifiedName,
                        returnType = function.ReturnTypeName == "float1" ? "float" : function.ReturnTypeName == "half1" ? "half" : function.ReturnTypeName,
                        body = function.BodyText, draft = function.BodyText, name = function.Name
                    };
                    if (candidate.returnType != "void") candidate.returnValueType = ShaderGraphFunctionParser.PortType(candidate.returnType);
                    if (candidate.returnType != "void" && candidate.returnValueType == ShaderGraphValueType.Texture)
                        throw new FormatException("Texture values can only be function inputs.");
                    foreach (var parameter in function.Parameters)
                    {
                        // Public properties avoid depending on declaration formatting in ToString().
                        var direction = parameter.DirectionFlags == ReflectedParameter.Direction.In ? ShaderGraphFunctionDirection.Input :
                            parameter.DirectionFlags == ReflectedParameter.Direction.Out ? ShaderGraphFunctionDirection.Output :
                            parameter.DirectionFlags == ReflectedParameter.Direction.InOut ? ShaderGraphFunctionDirection.InOut :
                            throw new FormatException("Unsupported parameter direction: " + parameter.Name);
                        var type = ShaderGraphFunctionParser.PortType(parameter.TypeName);
                        candidate.ports.Add(new ShaderGraphFunctionPort
                        {
                            name = parameter.Name, label = parameter.Name, type = type, direction = direction,
                            uvDefault = type == ShaderGraphValueType.Float2 && direction == ShaderGraphFunctionDirection.Input && (parameter.Name == "UV" || parameter.Name == "uv")
                        });
                    }
                    // WhimTex annotations win even when their Color metadata differs from the raw HLSL type.
                    if (marked != null && marked.Exists(existing => existing.entryPoint == candidate.entryPoint && existing.Signature() == candidate.Signature())) continue;
                    existingIndex = definitions.FindIndex(existing => existing.entryPoint == candidate.entryPoint && existing.Signature() == candidate.Signature());
                    string display = Hint(function.Hints, "DisplayName");
                    if (!string.IsNullOrWhiteSpace(display)) candidate.name = display.Trim();
                    string category = Hint(function.Hints, "SearchCategory");
                    string searchName = Hint(function.Hints, "SearchName");
                    candidate.menuPath = (string.IsNullOrWhiteSpace(category) ? "HLSL/" + Path.GetFileNameWithoutExtension(path) : category.Trim().Trim('/')) + "/" +
                        (string.IsNullOrWhiteSpace(searchName) ? candidate.name : searchName.Trim());
                    // Native provider keys are opaque, not invented GUIDs or versioned WhimTex @preset pairs.
                    candidate.presetId = Hint(function.Hints, "ProviderKey");
                    candidate.presetVersion = 0;
                    int index = 0;
                    foreach (var parameter in function.Parameters) ApplyHints(candidate.ports[index++], parameter.Hints);
                    candidate.sourceId = "discovery";
                    candidate.Validate();
                    candidate.sourceId = null;
                    if (existingIndex >= 0) definitions[existingIndex] = candidate;
                    else definitions.Add(candidate);
                }
                catch (Exception error) when (error is FormatException || error is ArgumentException || error is OverflowException)
                {
                    if (existingIndex >= 0) definitions.RemoveAt(existingIndex);
                    report(path, function.FullyQualifiedName + ": " + error.Message);
                }
            }
#endif
        }

#if UNITY_6000_5_OR_NEWER
        static string Hint(IReadOnlyDictionary<string, string> hints, string name) =>
            hints != null && (hints.TryGetValue("sg:" + name, out string value) || hints.TryGetValue(name, out value)) ? value : null;
        static bool HasHint(IReadOnlyDictionary<string, string> hints, string name) =>
            hints != null && (hints.ContainsKey("sg:" + name) || hints.ContainsKey(name));

        static void ApplyHints(ShaderGraphFunctionPort port, IReadOnlyDictionary<string, string> hints)
        {
            string label = Hint(hints, "DisplayName");
            if (!string.IsNullOrWhiteSpace(label)) port.label = label.Trim();
            if (HasHint(hints, "Color") || HasHint(hints, "PrimaryColor"))
            {
                if (port.type != ShaderGraphValueType.Float4) throw new FormatException("Color hints require a four-component numeric port: " + port.name);
                port.type = ShaderGraphValueType.Color;
            }
            string referable = Hint(hints, "Referable");
            if (!string.IsNullOrWhiteSpace(referable) && referable.Trim() != "UV")
                throw new FormatException("Unsupported referable: " + referable);
            if (HasHint(hints, "UV") || referable?.Trim() == "UV") SetUv();
            string value = Hint(hints, "Default");
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (value.Trim() == "UV" || value.Trim() == "UV0") SetUv();
                else
                {
                    if (port.type == ShaderGraphValueType.Texture) throw new FormatException("Texture default bindings are not supported; connect the port.");
                    string[] numbers = value.Trim().Trim('(', ')').Split(',');
                    int components = port.type == ShaderGraphValueType.Boolean ? 1 : ShaderGraphNodes.Components(port.type);
                    if (numbers.Length != 1 && numbers.Length != components) throw new FormatException("Default component count differs from port " + port.name + ".");
                    for (int i = 0; i < components; i++)
                    {
                        string number = numbers[numbers.Length == 1 ? 0 : i].Trim();
                        float parsed = number == "true" ? 1 : number == "false" ? 0 : Number(number);
                        if (port.type == ShaderGraphValueType.Boolean && parsed != 0 && parsed != 1) throw new FormatException("Boolean defaults require 0/1 or false/true.");
                        port.value[i] = parsed;
                    }
                    port.uvDefault = false;
                }
            }
            string range = Hint(hints, "Range");
            if (!string.IsNullOrWhiteSpace(range))
            {
                string[] numbers = range.Trim().Trim('(', ')').Split(',');
                if (numbers.Length != 2 || !ShaderGraphNodes.IsNumeric(port.type)) throw new FormatException("Range requires two numeric bounds.");
                port.minimum = Number(numbers[0]); port.maximum = Number(numbers[1]); port.hasRange = true;
                if (port.maximum <= port.minimum) throw new FormatException("Range maximum must exceed minimum.");
            }

            void SetUv()
            {
                if (port.type != ShaderGraphValueType.Float2 || port.direction != ShaderGraphFunctionDirection.Input)
                    throw new FormatException("UV defaults require a float2/half2 input: " + port.name);
                port.uvDefault = true;
            }
        }

        static float Number(string value)
        {
            if (!float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result) || !ProjectiveMatrix.Finite(result))
                throw new FormatException("Expected a finite numeric hint: " + value);
            return result;
        }
#endif
    }
}
