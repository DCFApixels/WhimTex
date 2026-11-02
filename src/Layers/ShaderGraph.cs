using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderGraphValueType { Float, Float2, Float3, Float4, Boolean, Texture, Gradient, Curve, Color }
    public enum ShaderGraphWrap { Clamp, Repeat, Mirror, Transparent }
    public enum ShaderGraphFilter { Point, Bilinear }
    public enum ShaderGraphConstant { PI, TAU, PHI, E, SQRT2 }
    public enum ShaderGraphNodeKind
    {
        Parameter, Constant, UV, CanvasData, TextureSize, SampleTexture, SampleGradient, SampleCurve,
        Split, Combine, Add, Subtract, Multiply, Divide, Min, Max, Clamp, Abs, OneMinus, Negate,
        Power, SquareRoot, Floor, Ceil, Round, Fraction, Sine, Cosine, Lerp, Remap, Step, Smoothstep,
        Length, Distance, Dot, Normalize, Compare, Select, TransformUV, Noise, Shape, Blend, Output, Reroute, CustomFunction, Swizzle,
        InstanceIndex, Time,
        Cellular, Random, Checkerboard, Stripes, Grid, Rings, GradientFactor, CombineDistance,
        PolarCoordinates, Repeat, Mirror, Kaleidoscope, DomainWarp, VectorDisplacement, Twirl, BulgePinch, Wave, Ripple,
        Hue, Saturation, Exposure, Lightness, Levels, ColorBalance, Posterize, ConvertColor, Luminance,
        GradientMap, ColorCurves, ColorMask, HueRange, MixColor, SetAlpha,
        HeightToNormal, NormalBlend, HeightToDistortion, NormalToDistortion,
        Sign, Saturate, Modulo, Atan2, Asin, Acos, Exp, Log, Cross, Reflect, Refract, And, Or, Not,
        NormalEncode, NormalDecode, NormalStrength, NormalLighting, Premultiply, Unpremultiply,
        Flipbook, TileableNoise, VoronoiEdge, Dither,
        Blur, Sharpen, EdgeDetection, Dilate, Erode, SubGraph, SubGraphInput, SubGraphOutput,
        Unlerp, PixelateUV, ChannelMixer, WhiteBalance, DirectionalBlur, RadialBlur,
        HeightBlend, TileUV, PerspectiveUV, GaborNoise, RotateVector, Blackbody, SlopeBlur, BilateralBlur, SampleInput, CanvasPosition
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
        public ShaderGraphConstant constant;
        public string swizzle = "xxxx";
        public string parameterId;
        public int option;
        public ShaderGraphFractal fractal;
        public ShaderGraphColorSpace inputColorSpace;
        public ShaderGraphColorSpace outputColorSpace = ShaderGraphColorSpace.SRGB;
        public ShaderGraphChannel channel;
        public ShaderGraphDerivative derivative;
        public ShaderGraphNormalEncoding normalEncoding;
        public ShaderGraphHeightSource heightSource;
        public bool hdr = true;
        public ShaderGraphWrap wrap;
        public ShaderGraphFilter filter = ShaderGraphFilter.Bilinear;
        public List<ShaderGraphInput> inputs = new List<ShaderGraphInput>();
        public string functionId;
        public string subGraphId;
        public Vector2 codeSize;
        public bool codeExpanded = true;
        public bool previewExpanded = true;
        [NonSerialized] internal ShaderGraphFunction function;
        [NonSerialized] internal ShaderGraphSubGraph subGraph;
        [NonSerialized] internal ShaderGraphInterfacePort interfacePort;
        [NonSerialized] internal string uniformNodeId, uniformPort;
        [NonSerialized] internal bool expandedInterface;
        internal ShaderGraphNode CopyForCompilation() => (ShaderGraphNode)MemberwiseClone();

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
    public sealed class ShaderGraphNote
    {
        public string id = Guid.NewGuid().ToString("N");
        public string title = "Note", contents = "";
        public Vector2 position, size = new Vector2(200, 160);
        public int theme, fontSize = 1;
        public float hueShift;
    }

    [Serializable]
    public sealed class ShaderGraphGroup
    {
        public string id = Guid.NewGuid().ToString("N");
        public string title = "Group";
        public Vector2 position;
        public List<string> elements = new List<string>();
    }

    [Serializable]
    public sealed partial class ShaderGraph
    {
        public int version = 1;
        public List<ShaderFXParameter> parameters = new List<ShaderFXParameter>();
        public List<ShaderGraphNode> nodes = new List<ShaderGraphNode>();
        public List<ShaderGraphEdge> edges = new List<ShaderGraphEdge>();
        public List<ShaderGraphFunction> functions = new List<ShaderGraphFunction>();
        public List<ShaderGraphSource> sources = new List<ShaderGraphSource>();
        public List<ShaderGraphNote> notes = new List<ShaderGraphNote>();
        public List<ShaderGraphGroup> groups = new List<ShaderGraphGroup>();
        public List<ShaderGraphSubGraph> subGraphs = new List<ShaderGraphSubGraph>();
        [NonSerialized] internal ShaderGraphSubGraph activeSubGraph;

        internal void PruneGroups()
        {
            var ids = new HashSet<string>();
            foreach (var node in nodes) ids.Add(node.id);
            foreach (var note in notes) ids.Add(note.id);
            foreach (var group in groups) group.elements.RemoveAll(id => !ids.Contains(id));
        }

        public ShaderGraphFunction FindFunction(string id) => functions?.Find(f => f != null && f.id == id);
        internal void BindFunctions()
        {
            BindNodeDefinitions(nodes, activeSubGraph);
            if (subGraphs != null) foreach (var definition in subGraphs) if (definition != null) BindNodeDefinitions(definition.nodes, definition);
        }
        internal void PruneFunctions()
        {
            var used = new HashSet<string>();
            foreach (var node in AllNodes()) if (node.kind == ShaderGraphNodeKind.CustomFunction) used.Add(node.functionId);
            functions.RemoveAll(f => f != null && !used.Contains(f.id));
            used.Clear();
            var byId = new Dictionary<string, ShaderGraphSource>();
            foreach (var source in sources) if (source?.id != null) byId.TryAdd(source.id, source);
            var pending = new Stack<string>();
            foreach (var f in functions) if (!string.IsNullOrEmpty(f.sourceId)) pending.Push(f.sourceId);
            while (pending.Count != 0)
            {
                string id = pending.Pop();
                if (!used.Add(id) || !byId.TryGetValue(id, out var source) || source.includes == null) continue;
                foreach (var include in source.includes) if (include?.sourceId != null) pending.Push(include.sourceId);
            }
            sources.RemoveAll(s => s != null && !used.Contains(s.id));
        }

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

    internal static partial class ShaderGraphNodes
    {
        static ShaderGraphPort P(string name, ShaderGraphValueType? type = null, float value = 0) => new ShaderGraphPort(name, type, Vector4.one * value);
        static ShaderGraphPort UV() => new ShaderGraphPort("UV", ShaderGraphValueType.Float2, default, true);
        internal static readonly ShaderGraphNodeKind[] Kinds = (ShaderGraphNodeKind[])Enum.GetValues(typeof(ShaderGraphNodeKind));
        static readonly Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> ports = BuildPorts();
        static readonly ShaderGraphPort[] none = Array.Empty<ShaderGraphPort>();
        static Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> BuildPorts()
        {
            var map = new Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]>();
            map[ShaderGraphNodeKind.InstanceIndex] = new[] { P("Start", ShaderGraphValueType.Float), P("Scale", ShaderGraphValueType.Float, 1) };
            map[ShaderGraphNodeKind.Time] = new[] { P("Start", ShaderGraphValueType.Float), P("Scale", ShaderGraphValueType.Float, 1) };
            foreach (var kind in new[] { ShaderGraphNodeKind.Add, ShaderGraphNodeKind.Subtract, ShaderGraphNodeKind.Min, ShaderGraphNodeKind.Max, ShaderGraphNodeKind.Distance, ShaderGraphNodeKind.Dot })
                map[kind] = new[] { P("A"), P("B") };
            foreach (var kind in new[] { ShaderGraphNodeKind.Multiply, ShaderGraphNodeKind.Divide, ShaderGraphNodeKind.Power })
                map[kind] = new[] { P("A", null, 1), P("B", null, 1) };
            foreach (var kind in new[] { ShaderGraphNodeKind.Abs, ShaderGraphNodeKind.OneMinus, ShaderGraphNodeKind.Negate, ShaderGraphNodeKind.SquareRoot, ShaderGraphNodeKind.Floor, ShaderGraphNodeKind.Ceil, ShaderGraphNodeKind.Round, ShaderGraphNodeKind.Fraction, ShaderGraphNodeKind.Sine, ShaderGraphNodeKind.Cosine, ShaderGraphNodeKind.Length, ShaderGraphNodeKind.Normalize, ShaderGraphNodeKind.Split })
                map[kind] = new[] { P("In") };
            map[ShaderGraphNodeKind.Clamp] = new[] { P("In"), P("Min"), P("Max", null, 1) };
            map[ShaderGraphNodeKind.Lerp] = new[] { P("A"), P("B", null, 1), P("T", ShaderGraphValueType.Float, .5f) };
            map[ShaderGraphNodeKind.Unlerp] = new[] { P("A"), P("B", null, 1), P("T") };
            map[ShaderGraphNodeKind.Remap] = new[] { P("In"), P("InMin", ShaderGraphValueType.Float), P("InMax", ShaderGraphValueType.Float, 1), P("OutMin", ShaderGraphValueType.Float), P("OutMax", ShaderGraphValueType.Float, 1) };
            map[ShaderGraphNodeKind.Step] = new[] { P("Edge", null, .5f), P("In") };
            map[ShaderGraphNodeKind.Smoothstep] = new[] { P("Min"), P("Max", null, 1), P("In") };
            map[ShaderGraphNodeKind.Compare] = new[] { P("A", ShaderGraphValueType.Float), P("B", ShaderGraphValueType.Float) };
            map[ShaderGraphNodeKind.Select] = new[] { P("Condition", ShaderGraphValueType.Boolean), P("True", null, 1), P("False") };
            map[ShaderGraphNodeKind.TextureSize] = new[] { P("Texture", ShaderGraphValueType.Texture) };
            map[ShaderGraphNodeKind.SampleTexture] = new[] { P("Texture", ShaderGraphValueType.Texture), UV() };
            map[ShaderGraphNodeKind.SampleInput] = new[] { UV() };
            map[ShaderGraphNodeKind.SampleGradient] = new[] { P("Gradient", ShaderGraphValueType.Gradient), P("T", ShaderGraphValueType.Float) };
            map[ShaderGraphNodeKind.SampleCurve] = new[] { P("Curve", ShaderGraphValueType.Curve), P("T", ShaderGraphValueType.Float) };
            map[ShaderGraphNodeKind.TransformUV] = new[] { UV(), P("Offset", ShaderGraphValueType.Float2), P("Scale", ShaderGraphValueType.Float2, 1), P("Rotation", ShaderGraphValueType.Float), P("Center", ShaderGraphValueType.Float2, .5f) };
            map[ShaderGraphNodeKind.Shape] = new[] { UV(), P("Center", ShaderGraphValueType.Float2, .5f), P("Size", ShaderGraphValueType.Float2, .5f), P("Radius", ShaderGraphValueType.Float, .1f), P("Feather", ShaderGraphValueType.Float) };
            map[ShaderGraphNodeKind.Blend] = new[] { P("Background", ShaderGraphValueType.Color), P("Foreground", ShaderGraphValueType.Color, 1), P("Opacity", ShaderGraphValueType.Float, 1) };
            map[ShaderGraphNodeKind.Output] = new[] { P("RGBA", ShaderGraphValueType.Color, 1), P("AlphaScale", ShaderGraphValueType.Float, 1) };
            map[ShaderGraphNodeKind.Reroute] = new[] { P("In") };
            map[ShaderGraphNodeKind.Swizzle] = new[] { P("In") };
            map[ShaderGraphNodeKind.Combine] = new[] { P("X", ShaderGraphValueType.Float), P("Y", ShaderGraphValueType.Float), P("Z", ShaderGraphValueType.Float), P("W", ShaderGraphValueType.Float, 1) };
            BuildProceduralPorts(map);
            BuildColorPorts(map);
            BuildSamplingPorts(map);
            BuildExtendedPorts(map);
            BuildSurfacePorts(map);
            return map;
        }
        internal static IReadOnlyList<ShaderGraphPort> Inputs(ShaderGraphNode node) => node.kind == ShaderGraphNodeKind.CustomFunction
            ? node.function?.InputPorts() ?? none : SubGraphInputs(node) ?? (node.kind == ShaderGraphNodeKind.Shape ? ShapeInputs(node) :
            UsesHeightTexture(node.kind) && node.heightSource == ShaderGraphHeightSource.Value ? heightValuePorts :
            SurfaceInputs(node) ?? ExtendedInputs(node) ?? NormalInputs(node) ?? (ports.TryGetValue(node.kind, out var result) ? result : none));
        internal static string[] Outputs(ShaderGraphNode node) => node.kind switch
        {
            ShaderGraphNodeKind.Output => Array.Empty<string>(),
            ShaderGraphNodeKind.CustomFunction => node.function?.OutputPorts() ?? Array.Empty<string>(),
            ShaderGraphNodeKind.SubGraph => node.subGraph?.OutputNames() ?? Array.Empty<string>(),
            ShaderGraphNodeKind.SubGraphOutput => Array.Empty<string>(),
            ShaderGraphNodeKind.Split => new[] { "X", "Y", "Z", "W" },
            ShaderGraphNodeKind.CanvasPosition => new[] { "Normalized", "AspectX", "AspectY" },
            ShaderGraphNodeKind.CanvasData => new[] { "Size", "Texel", "PixelScale" },
            ShaderGraphNodeKind.TextureSize => new[] { "Size", "Texel" },
            ShaderGraphNodeKind.Cellular => new[] { "F1", "F2", "Cell" },
            ShaderGraphNodeKind.Repeat => new[] { "Out", "Cell" },
            ShaderGraphNodeKind.DomainWarp => new[] { "Out", "Displacement" },
            ShaderGraphNodeKind.Shape => new[] { "Out", "Distance" },
            ShaderGraphNodeKind.CombineDistance => new[] { "Out", "Mask" },
            ShaderGraphNodeKind.HeightBlend => new[] { "Height", "Mask" },
            ShaderGraphNodeKind.TileUV => new[] { "UV", "Cell", "Center", "Random", "Mask" },
            ShaderGraphNodeKind.PerspectiveUV => new[] { "UV", "Mask" },
            ShaderGraphNodeKind.GaborNoise => new[] { "Value", "Phase", "Intensity" },
            _ => new[] { "Out" }
        };
        internal static string Name(ShaderGraphNodeKind kind) => kind == ShaderGraphNodeKind.BulgePinch ? "Bulge / Pinch" : UnityEditor.ObjectNames.NicifyVariableName(kind.ToString());
        internal static string Category(ShaderGraphNodeKind kind) => SurfaceCategory(kind) ?? ExtendedCategory(kind) ?? kind switch
        {
            ShaderGraphNodeKind.Parameter or ShaderGraphNodeKind.Constant or ShaderGraphNodeKind.UV or ShaderGraphNodeKind.CanvasData or
            ShaderGraphNodeKind.CanvasPosition or ShaderGraphNodeKind.InstanceIndex or ShaderGraphNodeKind.Time => "Input",
            ShaderGraphNodeKind.TextureSize or ShaderGraphNodeKind.SampleTexture or ShaderGraphNodeKind.SampleInput or ShaderGraphNodeKind.SampleGradient or ShaderGraphNodeKind.SampleCurve => "Sampling",
            ShaderGraphNodeKind.Split or ShaderGraphNodeKind.Combine or ShaderGraphNodeKind.Swizzle or ShaderGraphNodeKind.Length or ShaderGraphNodeKind.Distance or ShaderGraphNodeKind.Dot or ShaderGraphNodeKind.Normalize => "Vector",
            ShaderGraphNodeKind.Noise or ShaderGraphNodeKind.Cellular or ShaderGraphNodeKind.Random => "Procedural/Noise",
            ShaderGraphNodeKind.Checkerboard or ShaderGraphNodeKind.Stripes or ShaderGraphNodeKind.Grid or ShaderGraphNodeKind.Rings or ShaderGraphNodeKind.GradientFactor => "Procedural/Pattern",
            ShaderGraphNodeKind.Shape or ShaderGraphNodeKind.CombineDistance => "Procedural/Shape",
            ShaderGraphNodeKind.TransformUV or ShaderGraphNodeKind.PixelateUV or ShaderGraphNodeKind.PolarCoordinates or ShaderGraphNodeKind.Repeat or ShaderGraphNodeKind.Mirror or ShaderGraphNodeKind.Kaleidoscope => "UV",
            ShaderGraphNodeKind.DomainWarp or ShaderGraphNodeKind.VectorDisplacement or ShaderGraphNodeKind.Twirl or ShaderGraphNodeKind.BulgePinch or ShaderGraphNodeKind.Wave or ShaderGraphNodeKind.Ripple => "UV/Distortion",
            ShaderGraphNodeKind.Hue or ShaderGraphNodeKind.Saturation or ShaderGraphNodeKind.Exposure or ShaderGraphNodeKind.Lightness or
            ShaderGraphNodeKind.Levels or ShaderGraphNodeKind.ColorBalance or ShaderGraphNodeKind.Posterize or ShaderGraphNodeKind.ChannelMixer or ShaderGraphNodeKind.WhiteBalance => "Color/Adjustments",
            ShaderGraphNodeKind.ConvertColor or ShaderGraphNodeKind.Luminance => "Color/Conversion",
            ShaderGraphNodeKind.GradientMap or ShaderGraphNodeKind.ColorCurves => "Color/Mapping",
            ShaderGraphNodeKind.ColorMask or ShaderGraphNodeKind.HueRange => "Color/Selection",
            ShaderGraphNodeKind.MixColor or ShaderGraphNodeKind.Blend => "Color/Mixing",
            ShaderGraphNodeKind.SetAlpha => "Color/Alpha",
            ShaderGraphNodeKind.HeightToNormal or ShaderGraphNodeKind.NormalBlend => "Normal",
            ShaderGraphNodeKind.HeightToDistortion or ShaderGraphNodeKind.NormalToDistortion => "UV/Distortion",
            ShaderGraphNodeKind.Output => "Output",
            ShaderGraphNodeKind.Reroute => "Utility",
            ShaderGraphNodeKind.CustomFunction => "Utility",
            ShaderGraphNodeKind.SubGraph or ShaderGraphNodeKind.SubGraphInput or ShaderGraphNodeKind.SubGraphOutput => "Sub Graph",
            ShaderGraphNodeKind.Blur or ShaderGraphNodeKind.Sharpen or ShaderGraphNodeKind.EdgeDetection or ShaderGraphNodeKind.Dilate or ShaderGraphNodeKind.Erode or ShaderGraphNodeKind.DirectionalBlur or ShaderGraphNodeKind.RadialBlur => "Filter",
            _ => "Math"
        };
        internal static ShaderGraphNode Create(ShaderGraphNodeKind kind, Vector2 position)
        {
            var node = new ShaderGraphNode { kind = kind, position = position };
            if (kind == ShaderGraphNodeKind.CustomFunction) { node.function = ShaderGraphFunction.CreateInline(); node.functionId = node.function.id; }
            if (kind == ShaderGraphNodeKind.Reroute) return node;
            if (kind == ShaderGraphNodeKind.Combine) node.valueType = ShaderGraphValueType.Float4;
            foreach (var p in Inputs(node))
                if (!p.uvDefault && p.type is not (ShaderGraphValueType.Texture or ShaderGraphValueType.Gradient or ShaderGraphValueType.Curve))
                    node.inputs.Add(new ShaderGraphInput { port = p.name, type = p.type ?? ShaderGraphValueType.Float, value = p.value });
            return node;
        }
        internal static void SetFunction(ShaderGraphNode node, ShaderGraphFunction function)
        {
            node.function = function; node.functionId = function.id;
            node.inputs.RemoveAll(input => !function.ports.Exists(p => p.name == input.port && p.direction != ShaderGraphFunctionDirection.Output && p.type == input.type));
            foreach (var p in Inputs(node))
                if (node.FindInput(p.name) == null && !p.uvDefault && p.type != ShaderGraphValueType.Texture)
                    node.inputs.Add(new ShaderGraphInput { port = p.name, type = p.type ?? ShaderGraphValueType.Float, value = p.value });
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
        internal static ShaderGraphValueType SwizzleType(string mask) => (ShaderGraphValueType)(Mathf.Clamp(mask?.Length ?? 1, 1, 4) - 1);
        internal static ShaderGraphValueType SwizzleInputType(string mask, ShaderGraphValueType input)
        {
            if (!IsNumeric(input)) return input;
            int width = Components(input);
            if (mask != null) foreach (char component in mask) width = Math.Max(width, SwizzleComponent(component) + 1);
            return (ShaderGraphValueType)(width - 1);
        }
        internal static int SwizzleComponent(char component) => char.ToLowerInvariant(component) switch
        {
            'x' or 'r' => 0, 'y' or 'g' => 1, 'z' or 'b' => 2, 'w' or 'a' => 3, '_' => -1, _ => -2
        };
        internal static ShaderGraphValueType NumericType(ShaderGraphValueType type) => type == ShaderGraphValueType.Color ? ShaderGraphValueType.Float4 : type;
        internal static int Components(ShaderGraphValueType type) => type == ShaderGraphValueType.Color ? 4 : type <= ShaderGraphValueType.Float4 ? (int)type + 1 : type == ShaderGraphValueType.Boolean ? 1 : 0;
    }
}
