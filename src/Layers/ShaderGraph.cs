using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderGraphValueType { Float, Float2, Float3, Float4, Boolean, Texture, Gradient, Curve, Color }
    public enum ShaderGraphWrap { Clamp, Repeat, Mirror, Transparent }
    public enum ShaderGraphFilter { Point, Bilinear }
    public enum ShaderGraphNodeKind
    {
        Parameter, Constant, UV, ImageSize, TextureSize, SampleTexture, SampleGradient, SampleCurve,
        Split, Combine, Add, Subtract, Multiply, Divide, Min, Max, Clamp, Abs, OneMinus, Negate,
        Power, SquareRoot, Floor, Ceil, Round, Fraction, Sine, Cosine, Lerp, Remap, Step, Smoothstep,
        Length, Distance, Dot, Normalize, Compare, Select, TransformUV, Noise, Shape, Blend, Output, Reroute
    }

    [Serializable]
    public sealed class ShaderGraphInput
    {
        public string port;
        public ShaderGraphValueType type;
        public Vector4 value;
    }

    [Serializable]
    public sealed class ShaderGraphNode
    {
        public string id = Guid.NewGuid().ToString("N");
        public ShaderGraphNodeKind kind;
        public Vector2 position;
        public ShaderGraphValueType valueType;
        public Vector4 value;
        public string parameterId;
        public int option;
        public bool hdr = true;
        public ShaderGraphWrap wrap;
        public ShaderGraphFilter filter = ShaderGraphFilter.Bilinear;
        public List<ShaderGraphInput> inputs = new List<ShaderGraphInput>();

        public ShaderGraphInput FindInput(string port)
        {
            foreach (var input in inputs) if (input.port == port) return input;
            return null;
        }
    }

    [Serializable]
    public sealed class ShaderGraphEdge
    {
        public string outputNode, outputPort = "Out", inputNode, inputPort;
    }

    [Serializable]
    public sealed class ShaderGraph
    {
        public int version = 1;
        public List<ShaderFXParameter> parameters = new List<ShaderFXParameter>();
        public List<ShaderGraphNode> nodes = new List<ShaderGraphNode>();
        public List<ShaderGraphEdge> edges = new List<ShaderGraphEdge>();

        public static ShaderGraph CreateDefault()
        {
            var graph = new ShaderGraph();
            var tint = new ShaderFXParameter { name = "_Tint", type = ShaderFXParameterType.Color, colorValue = Color.white };
            graph.parameters.Add(tint);
            var parameter = ShaderGraphNodes.Create(ShaderGraphNodeKind.Parameter, new Vector2(40, 80));
            parameter.parameterId = tint.id;
            var output = ShaderGraphNodes.Create(ShaderGraphNodeKind.Output, new Vector2(360, 80));
            graph.nodes.Add(parameter); graph.nodes.Add(output);
            graph.edges.Add(new ShaderGraphEdge { outputNode = parameter.id, inputNode = output.id, inputPort = "RGBA" });
            return graph;
        }

        public ShaderGraphNode FindNode(string id)
        {
            if (nodes != null) foreach (var node in nodes) if (node != null && node.id == id) return node;
            return null;
        }
        public ShaderFXParameter FindParameter(string id)
        {
            if (parameters != null) foreach (var parameter in parameters) if (parameter != null && parameter.id == id) return parameter;
            return null;
        }
        public void Validate() => ShaderGraphCompiler.ValidateStorage(this);
    }

    internal readonly struct ShaderGraphPort
    {
        internal readonly string name;
        internal readonly ShaderGraphValueType? type;
        internal readonly Vector4 value;
        internal readonly bool uvDefault;
        internal ShaderGraphPort(string name, ShaderGraphValueType? type = null, Vector4 value = default, bool uvDefault = false)
        { this.name = name; this.type = type; this.value = value; this.uvDefault = uvDefault; }
    }

    internal static class ShaderGraphNodes
    {
        static ShaderGraphPort P(string name, ShaderGraphValueType? type = null, float value = 0) => new ShaderGraphPort(name, type, Vector4.one * value);
        static ShaderGraphPort UV() => new ShaderGraphPort("UV", ShaderGraphValueType.Float2, default, true);
        internal static readonly ShaderGraphNodeKind[] Kinds = (ShaderGraphNodeKind[])Enum.GetValues(typeof(ShaderGraphNodeKind));
        static readonly Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> ports = BuildPorts();
        static readonly ShaderGraphPort[] none = Array.Empty<ShaderGraphPort>();
        static Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> BuildPorts()
        {
            var map = new Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]>();
            foreach (var kind in new[] { ShaderGraphNodeKind.Add, ShaderGraphNodeKind.Subtract, ShaderGraphNodeKind.Min, ShaderGraphNodeKind.Max, ShaderGraphNodeKind.Distance, ShaderGraphNodeKind.Dot })
                map[kind] = new[] { P("A"), P("B") };
            foreach (var kind in new[] { ShaderGraphNodeKind.Multiply, ShaderGraphNodeKind.Divide, ShaderGraphNodeKind.Power })
                map[kind] = new[] { P("A", null, 1), P("B", null, 1) };
            foreach (var kind in new[] { ShaderGraphNodeKind.Abs, ShaderGraphNodeKind.OneMinus, ShaderGraphNodeKind.Negate, ShaderGraphNodeKind.SquareRoot, ShaderGraphNodeKind.Floor, ShaderGraphNodeKind.Ceil, ShaderGraphNodeKind.Round, ShaderGraphNodeKind.Fraction, ShaderGraphNodeKind.Sine, ShaderGraphNodeKind.Cosine, ShaderGraphNodeKind.Length, ShaderGraphNodeKind.Normalize, ShaderGraphNodeKind.Split })
                map[kind] = new[] { P("In") };
            map[ShaderGraphNodeKind.Clamp] = new[] { P("In"), P("Min"), P("Max", null, 1) };
            map[ShaderGraphNodeKind.Lerp] = new[] { P("A"), P("B", null, 1), P("T", ShaderGraphValueType.Float, .5f) };
            map[ShaderGraphNodeKind.Remap] = new[] { P("In"), P("InMin", ShaderGraphValueType.Float), P("InMax", ShaderGraphValueType.Float, 1), P("OutMin", ShaderGraphValueType.Float), P("OutMax", ShaderGraphValueType.Float, 1) };
            map[ShaderGraphNodeKind.Step] = new[] { P("Edge", null, .5f), P("In") };
            map[ShaderGraphNodeKind.Smoothstep] = new[] { P("Min"), P("Max", null, 1), P("In") };
            map[ShaderGraphNodeKind.Compare] = new[] { P("A", ShaderGraphValueType.Float), P("B", ShaderGraphValueType.Float) };
            map[ShaderGraphNodeKind.Select] = new[] { P("Condition", ShaderGraphValueType.Boolean), P("True", null, 1), P("False") };
            map[ShaderGraphNodeKind.TextureSize] = new[] { P("Texture", ShaderGraphValueType.Texture) };
            map[ShaderGraphNodeKind.SampleTexture] = new[] { P("Texture", ShaderGraphValueType.Texture), UV() };
            map[ShaderGraphNodeKind.SampleGradient] = new[] { P("Gradient", ShaderGraphValueType.Gradient), P("T", ShaderGraphValueType.Float) };
            map[ShaderGraphNodeKind.SampleCurve] = new[] { P("Curve", ShaderGraphValueType.Curve), P("T", ShaderGraphValueType.Float) };
            map[ShaderGraphNodeKind.TransformUV] = new[] { UV(), P("Offset", ShaderGraphValueType.Float2), P("Scale", ShaderGraphValueType.Float2, 1), P("Rotation", ShaderGraphValueType.Float), P("Center", ShaderGraphValueType.Float2, .5f) };
            map[ShaderGraphNodeKind.Noise] = new[] { UV(), P("Scale", ShaderGraphValueType.Float2, 8), P("Seed", ShaderGraphValueType.Float, 1337) };
            map[ShaderGraphNodeKind.Shape] = new[] { UV(), P("Center", ShaderGraphValueType.Float2, .5f), P("Size", ShaderGraphValueType.Float2, .5f), P("Radius", ShaderGraphValueType.Float, .1f), P("Feather", ShaderGraphValueType.Float) };
            map[ShaderGraphNodeKind.Blend] = new[] { P("Background", ShaderGraphValueType.Color), P("Foreground", ShaderGraphValueType.Color, 1), P("Opacity", ShaderGraphValueType.Float, 1) };
            map[ShaderGraphNodeKind.Output] = new[] { P("RGBA", ShaderGraphValueType.Color, 1) };
            map[ShaderGraphNodeKind.Reroute] = new[] { P("In") };
            map[ShaderGraphNodeKind.Combine] = new[] { P("X", ShaderGraphValueType.Float), P("Y", ShaderGraphValueType.Float), P("Z", ShaderGraphValueType.Float), P("W", ShaderGraphValueType.Float, 1) };
            return map;
        }
        internal static IReadOnlyList<ShaderGraphPort> Inputs(ShaderGraphNode node) => ports.TryGetValue(node.kind, out var result) ? result : none;
        internal static string[] Outputs(ShaderGraphNode node) => node.kind switch
        {
            ShaderGraphNodeKind.Output => Array.Empty<string>(),
            ShaderGraphNodeKind.Split => new[] { "X", "Y", "Z", "W" },
            ShaderGraphNodeKind.ImageSize or ShaderGraphNodeKind.TextureSize => new[] { "Size", "Texel" },
            _ => new[] { "Out" }
        };
        internal static string Name(ShaderGraphNodeKind kind) => UnityEditor.ObjectNames.NicifyVariableName(kind.ToString());
        internal static string Category(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.Parameter or ShaderGraphNodeKind.Constant or ShaderGraphNodeKind.UV or ShaderGraphNodeKind.ImageSize => "Input",
            ShaderGraphNodeKind.TextureSize or ShaderGraphNodeKind.SampleTexture or ShaderGraphNodeKind.SampleGradient or ShaderGraphNodeKind.SampleCurve => "Sampling",
            ShaderGraphNodeKind.Split or ShaderGraphNodeKind.Combine or ShaderGraphNodeKind.Length or ShaderGraphNodeKind.Distance or ShaderGraphNodeKind.Dot or ShaderGraphNodeKind.Normalize => "Vector",
            ShaderGraphNodeKind.TransformUV or ShaderGraphNodeKind.Noise or ShaderGraphNodeKind.Shape => "Procedural",
            ShaderGraphNodeKind.Blend => "Color",
            ShaderGraphNodeKind.Output => "Output",
            ShaderGraphNodeKind.Reroute => "Utility",
            _ => "Math"
        };
        internal static ShaderGraphNode Create(ShaderGraphNodeKind kind, Vector2 position)
        {
            var node = new ShaderGraphNode { kind = kind, position = position };
            if (kind == ShaderGraphNodeKind.Reroute) return node;
            if (kind == ShaderGraphNodeKind.Combine) node.valueType = ShaderGraphValueType.Float4;
            foreach (var p in Inputs(node))
                if (!p.uvDefault && p.type is not (ShaderGraphValueType.Texture or ShaderGraphValueType.Gradient or ShaderGraphValueType.Curve))
                    node.inputs.Add(new ShaderGraphInput { port = p.name, type = p.type ?? ShaderGraphValueType.Float, value = p.value });
            return node;
        }
        internal static ShaderGraphValueType ParameterType(ShaderFXParameter p) => p.type switch
        {
            ShaderFXParameterType.Texture2D => ShaderGraphValueType.Texture,
            ShaderFXParameterType.Gradient => ShaderGraphValueType.Gradient,
            ShaderFXParameterType.Curve => ShaderGraphValueType.Curve,
            ShaderFXParameterType.Toggle => ShaderGraphValueType.Boolean,
            ShaderFXParameterType.Color => ShaderGraphValueType.Color,
            _ => p.Components switch { 1 => ShaderGraphValueType.Float, 2 => ShaderGraphValueType.Float2, 3 => ShaderGraphValueType.Float3, 4 => ShaderGraphValueType.Float4, _ => throw new InvalidOperationException("Unsupported graph parameter: " + p.name) }
        };
        internal static bool IsNumeric(ShaderGraphValueType type) => type >= ShaderGraphValueType.Float && type <= ShaderGraphValueType.Float4 || type == ShaderGraphValueType.Color;
        internal static ShaderGraphValueType NumericType(ShaderGraphValueType type) => type == ShaderGraphValueType.Color ? ShaderGraphValueType.Float4 : type;
        internal static int Components(ShaderGraphValueType type) => type == ShaderGraphValueType.Color ? 4 : type <= ShaderGraphValueType.Float4 ? (int)type + 1 : type == ShaderGraphValueType.Boolean ? 1 : 0;
    }
}
