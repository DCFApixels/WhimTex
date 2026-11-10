using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderGraphFunctionDirection { Input, Output, InOut }

    [Serializable]
    public sealed class ShaderGraphFunctionPort
    {
        public string name;
        public string label;
        public ShaderGraphValueType type;
        public ShaderGraphFunctionDirection direction;
        public Vector4 value;
        public bool uvDefault;
        public bool option;
        public ShaderEnableCondition visibleIf;
        public bool hasRange;
        public float minimum, maximum = 1;
    }

    [Serializable]
    public sealed class ShaderGraphFunction
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Custom Function", menuPath = "Custom Function";
        public bool inline = true;
        public bool fullSource;
        public string entryPoint = "Function", returnType = "void", returnName = "Result";
        public ShaderGraphValueType returnValueType;
        public List<ShaderGraphFunctionPort> ports = new List<ShaderGraphFunctionPort>();
        public List<ShaderGraphFunctionPort> draftPorts;
        public bool hasDraftPorts;
        // Unity serializes null lists as empty lists during reload and JsonUtility copying.
        // A nonempty list is self-describing; the flag preserves an intentionally empty draft.
        internal bool HasDraftPorts => hasDraftPorts || draftPorts?.Count > 0;
        internal IReadOnlyList<ShaderGraphFunctionPort> EditingPorts => HasDraftPorts
            ? (IReadOnlyList<ShaderGraphFunctionPort>)draftPorts ?? Array.Empty<ShaderGraphFunctionPort>() : ports;
        public string body = "Result = In;", draft = "Result = In;";
        public string sourceId, sourcePath, presetId, sourceFingerprint;
        public int presetVersion;
        public bool reflected;
        [NonSerialized] private RenderFingerprint.TextStamp bodyStamp;
        internal ulong BodyFingerprint => bodyStamp.Get(body);

        internal static ShaderGraphFunction CreateInline() => new ShaderGraphFunction
        {
            ports = new List<ShaderGraphFunctionPort>
            {
                new ShaderGraphFunctionPort { name = "In", type = ShaderGraphValueType.Color, value = Vector4.one },
                new ShaderGraphFunctionPort { name = "Result", type = ShaderGraphValueType.Color, direction = ShaderGraphFunctionDirection.Output }
            }
        };
        internal IReadOnlyList<ShaderGraphPort> InputPorts()
        {
            var result = new List<ShaderGraphPort>();
            if (ports != null) foreach (var p in ports)
                if (p.direction != ShaderGraphFunctionDirection.Output) result.Add(new ShaderGraphPort(p.name, p.type, p.value, p.uvDefault));
            return result;
        }
        internal string[] OutputPorts()
        {
            var result = new List<string>();
            if (returnType != "void") result.Add(returnName);
            if (ports != null) foreach (var p in ports)
                if (p.direction != ShaderGraphFunctionDirection.Input) result.Add(p.name);
            return result.ToArray();
        }
        internal ShaderGraphValueType OutputType(string port) => returnType != "void" && port == returnName
            ? returnValueType : ports?.Find(p => p.name == port && p.direction != ShaderGraphFunctionDirection.Input)?.type ?? ShaderGraphValueType.Float;
        internal static string TypeName(ShaderGraphValueType type) => type switch
        {
            ShaderGraphValueType.Float => "float", ShaderGraphValueType.Float2 => "float2", ShaderGraphValueType.Float3 => "float3",
            ShaderGraphValueType.Float4 or ShaderGraphValueType.Color => "float4", ShaderGraphValueType.Boolean => "bool",
            ShaderGraphValueType.Texture => "WhimTexTexture", _ => throw new FormatException("This resource cannot be passed to an HLSL function: " + type)
        };
        internal static ShaderGraphValueType ParseType(string type) => type switch
        {
            "float" or "half" or "float1" or "half1" => ShaderGraphValueType.Float, "float2" or "half2" => ShaderGraphValueType.Float2,
            "float3" or "half3" => ShaderGraphValueType.Float3, "float4" or "half4" => ShaderGraphValueType.Float4,
            "bool" => ShaderGraphValueType.Boolean, "WhimTexTexture" => ShaderGraphValueType.Texture,
            _ => throw new FormatException("Unsupported HLSL node port type: " + type)
        };
        internal string Signature(string functionName = null)
        {
            var result = new StringBuilder(returnType).Append(' ').Append(functionName ?? entryPoint).Append('(');
            for (int i = 0; i < ports.Count; i++)
            {
                var p = ports[i];
                if (i > 0) result.Append(", ");
                if (p.direction != ShaderGraphFunctionDirection.Input) result.Append(p.direction == ShaderGraphFunctionDirection.Output ? "out " : "inout ");
                result.Append(TypeName(p.type)).Append(' ').Append(p.name);
            }
            return result.Append(')').ToString();
        }
        internal void Validate()
        {
            int maximumCode = fullSource ? ShaderGraphSources.MaximumSourceBytes : 1024 * 1024;
            if (fullSource && !inline || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || ports == null || ports.Count > 64 ||
                (draftPorts?.Count ?? 0) > 64 || (draft?.Length ?? 0) > maximumCode || (body?.Length ?? 0) > maximumCode)
                throw new FormatException("Invalid HLSL function definition.");
            var inputs = new HashSet<string>(StringComparer.Ordinal); var outputs = new HashSet<string>(StringComparer.Ordinal);
            if (returnType != "void") { var type = ParseType(returnType); if (type == ShaderGraphValueType.Texture || ShaderGraphNodes.NumericType(type) != ShaderGraphNodes.NumericType(returnValueType)) throw new FormatException("Invalid function return type."); if (!Identifier(returnName)) throw new FormatException("Invalid function return port."); outputs.Add(returnName); }
            if (!QualifiedIdentifier(entryPoint)) throw new FormatException("Invalid HLSL function name.");
            var arguments = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in ports)
            {
                if (p == null || !Identifier(p.name) || !Enum.IsDefined(typeof(ShaderGraphFunctionDirection), p.direction)) throw new FormatException("Invalid HLSL function port.");
                if (!arguments.Add(p.name)) throw new FormatException("Duplicate function argument: " + p.name);
                TypeName(p.type);
                if (p.type == ShaderGraphValueType.Texture && p.direction != ShaderGraphFunctionDirection.Input) throw new FormatException("Texture ports must be inputs.");
                if (p.uvDefault && (p.type != ShaderGraphValueType.Float2 || p.direction != ShaderGraphFunctionDirection.Input)) throw new FormatException("UV defaults require a float2 input.");
                if (p.option && (p.type != ShaderGraphValueType.Boolean || p.direction != ShaderGraphFunctionDirection.Input))
                    throw new FormatException("Function options require Boolean inputs.");
                if (!string.IsNullOrEmpty(p.visibleIf.parameter))
                {
                    var driver = ports.Find(d => d != null && d.name == p.visibleIf.parameter);
                    if (p.direction != ShaderGraphFunctionDirection.Input || p.option || driver?.option != true ||
                        double.IsNaN(p.visibleIf.value) || double.IsInfinity(p.visibleIf.value))
                        throw new FormatException("Function input visibility requires an unconditional Boolean option.");
                }
                if (p.direction != ShaderGraphFunctionDirection.Output && !inputs.Add(p.name) || p.direction != ShaderGraphFunctionDirection.Input && !outputs.Add(p.name)) throw new FormatException("Duplicate HLSL function port: " + p.name);
                if (!ProjectiveMatrix.Finite(p.value.x) || !ProjectiveMatrix.Finite(p.value.y) || !ProjectiveMatrix.Finite(p.value.z) || !ProjectiveMatrix.Finite(p.value.w) ||
                    p.hasRange && (!ProjectiveMatrix.Finite(p.minimum) || !ProjectiveMatrix.Finite(p.maximum) || p.maximum <= p.minimum)) throw new FormatException("Invalid HLSL port default or range.");
            }
            if (outputs.Count == 0) throw new FormatException("An HLSL node requires an output.");
            if (!inline && string.IsNullOrEmpty(sourceId)) throw new FormatException("HLSL function source backup is missing.");
        }
        internal static bool Identifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128 || !(char.IsLetter(value[0]) && value[0] < 128 || value[0] == '_')) return false;
            foreach (char c in value) if (!(c < 128 && (char.IsLetterOrDigit(c) || c == '_'))) return false;
            return true;
        }
        internal static bool QualifiedIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (var part in value.Split(new[] { "::" }, StringSplitOptions.None)) if (!Identifier(part)) return false;
            return true;
        }
    }
}
