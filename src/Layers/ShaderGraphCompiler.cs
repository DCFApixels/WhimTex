using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphCompilation
    {
        internal string source;
        internal readonly List<ShaderFXParameter> parameters = new List<ShaderFXParameter>();
        internal readonly Dictionary<string, ShaderGraphValueType> outputTypes = new Dictionary<string, ShaderGraphValueType>();
    }

    internal sealed class ShaderGraphException : InvalidOperationException
    {
        internal readonly string nodeId;
        internal ShaderGraphException(string nodeId, string message) : base(message) { this.nodeId = nodeId; }
    }

    internal sealed class ShaderGraphCompiler
    {
        readonly ShaderGraph graph;
        readonly ShaderGraphCompilation result = new ShaderGraphCompilation();
        readonly Dictionary<string, ShaderGraphNode> nodes = new Dictionary<string, ShaderGraphNode>();
        readonly Dictionary<string, ShaderGraphEdge> incoming = new Dictionary<string, ShaderGraphEdge>();
        readonly Dictionary<string, Value> values = new Dictionary<string, Value>();
        readonly HashSet<string> visiting = new HashSet<string>();
        readonly HashSet<string> declarations = new HashSet<string>();
        readonly StringBuilder uniforms = new StringBuilder(), body = new StringBuilder();
        readonly HashSet<string> usedParameters = new HashSet<string>();
        readonly struct Value
        {
            internal readonly ShaderGraphValueType type;
            internal readonly string text;
            internal Value(ShaderGraphValueType type, string text) { this.type = type; this.text = text; }
        }
        ShaderGraphCompiler(ShaderGraph graph) { this.graph = graph; }
        internal static void Validate(ShaderGraph graph) => Compile(graph);
        internal static void ValidateStorage(ShaderGraph graph) => new ShaderGraphCompiler(graph).ValidateStructure();
        internal static ShaderGraphCompilation Compile(ShaderGraph graph, string previewNode = null, string previewPort = "Out")
        {
            var compiler = new ShaderGraphCompiler(graph);
            compiler.ValidateStructure();
            var output = graph.nodes.Find(n => n.kind == ShaderGraphNodeKind.Output);
            Value color = previewNode == null ? compiler.Input(output, "RGBA") : compiler.Emit(previewNode, previewPort);
            if (ShaderGraphNodes.Components(color.type) == 0) throw new ShaderGraphException(previewNode, "Resource outputs cannot be displayed as an image.");
            string rgba = color.type switch
            {
                ShaderGraphValueType.Float4 or ShaderGraphValueType.Color => color.text,
                ShaderGraphValueType.Float3 => "float4(" + color.text + ", 1)",
                ShaderGraphValueType.Float2 => "float4(" + color.text + ", 0, 1)",
                _ => "float4((" + color.text + ").xxx, 1)"
            };
            compiler.body.Append("return ").Append(rgba).AppendLine(";");
            var properties = new StringBuilder();
            ShaderFXSourceBuilder.BuildParameterDeclarations(compiler.result.parameters, properties, compiler.uniforms, new HashSet<string>());
            var textureViews = new StringBuilder();
            foreach (var parameter in compiler.result.parameters)
                if (parameter.type == ShaderFXParameterType.Texture2D) ShaderFXTextureViews.Declare(parameter.name, properties, textureViews);
            compiler.result.source = "Shader \"Hidden/WhimTex/ShaderGraph\" { Properties {\n" + properties +
                "}\nSubShader { Cull Off ZWrite Off ZTest Always Blend Off Pass { CGPROGRAM\n" +
                "#pragma target 3.5\n#pragma vertex vert_img\n#pragma fragment GraphFragment\n#include \"UnityCG.cginc\"\n" +
                ShaderFXSourceBuilder.NoiseLibraryInclude + ShaderFXSourceBuilder.ColorLibraryInclude +
                "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ProceduralUv.cginc\"\n" +
                "static float _HdrBlend = 1.0;\n#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ColorBlend.cginc\"\n" +
                "float4 _GraphImageSize;\n" + compiler.uniforms + ShaderFXTextureViews.Types + textureViews + Helpers +
                "float4 GraphFragment(v2f_img input) : SV_Target {\nfloat2 graphUV = ProceduralSourceUv(input.uv);\n" +
                compiler.body + "}\nENDCG\n} } Fallback Off }";
            return compiler.result;
        }

        void ValidateStructure()
        {
            if (graph == null || graph.version != 1 || graph.nodes == null || graph.edges == null || graph.parameters == null)
                throw new FormatException("Unsupported or incomplete Shader graph.");
            if (graph.nodes.Count > 512 || graph.edges.Count > 4096 || graph.parameters.Count > 256)
                throw new FormatException("Shader graph exceeds the supported limits (512 nodes, 4096 edges, 256 parameters).");
            var ids = new HashSet<string>();
            foreach (var p in graph.parameters)
            {
                if (p == null || !Identifier(p.id) || !ids.Add(p.id)) throw new FormatException("Invalid or duplicate Shader parameter ID.");
                ShaderGraphNodes.ParameterType(p);
                if (p.type == ShaderFXParameterType.Texture2D && p.textureSource == ShaderFXTextureSource.Self)
                    throw new FormatException("A Shader layer cannot sample its own output. Choose a texture or another layer.");
                for (int i = 0; i < p.Components; i++)
                    if (double.IsNaN(p.GetNumber(i)) || double.IsInfinity(p.GetNumber(i))) throw new FormatException("Non-finite parameter: " + p.name);
            }
            int outputs = 0;
            foreach (var n in graph.nodes)
            {
                if (n == null || !Identifier(n.id) || !nodes.TryAdd(n.id, n) || !Enum.IsDefined(typeof(ShaderGraphNodeKind), n.kind))
                    throw new FormatException("Invalid or duplicate Shader node.");
                if (n.kind == ShaderGraphNodeKind.Output) outputs++;
                if (n.inputs == null || !Finite(n.value) || !ProjectiveMatrix.Finite(n.position.x) || !ProjectiveMatrix.Finite(n.position.y))
                    Error(n, "Invalid node data.");
                if (!Enum.IsDefined(typeof(ShaderGraphValueType), n.valueType) || !Enum.IsDefined(typeof(ShaderGraphWrap), n.wrap) || !Enum.IsDefined(typeof(ShaderGraphFilter), n.filter)) Error(n, "Unknown node option.");
                if (n.kind == ShaderGraphNodeKind.Parameter && !string.IsNullOrEmpty(n.parameterId) && graph.FindParameter(n.parameterId) == null) Error(n, "Parameter is missing.");
                if (n.kind == ShaderGraphNodeKind.Constant && ShaderGraphNodes.Components(n.valueType) == 0) Error(n, "Constants must be numeric or Boolean.");
                if (n.kind == ShaderGraphNodeKind.Reroute && n.inputs.Count != 0) Error(n, "Reroute takes its value from its connection.");
                if (n.kind == ShaderGraphNodeKind.Combine && (n.valueType < ShaderGraphValueType.Float2 || n.valueType > ShaderGraphValueType.Float4)) Error(n, "Combine requires float2, float3 or float4.");
                if ((n.kind == ShaderGraphNodeKind.Noise || n.kind == ShaderGraphNodeKind.Compare) && (n.option < 0 || n.option > 5) || n.kind == ShaderGraphNodeKind.Shape && (n.option < 0 || n.option > 2)) Error(n, "Unknown node mode.");
                if (n.kind == ShaderGraphNodeKind.Blend && !Enum.IsDefined(typeof(BlendMode), n.option)) Error(n, "Unknown blend mode.");
                var inputNames = new HashSet<string>();
                foreach (var input in n.inputs)
                {
                    if (input == null || !inputNames.Add(input.port ?? "") || !Finite(input.value) || !Enum.IsDefined(typeof(ShaderGraphValueType), input.type) || ShaderGraphNodes.Components(input.type) == 0 || !HasInput(n, input.port)) Error(n, "Invalid or duplicate input value.");
                }
            }
            if (outputs != 1) throw new FormatException("A Shader graph requires exactly one Output node.");
            foreach (var edge in graph.edges)
            {
                if (edge == null || !nodes.TryGetValue(edge.outputNode ?? "", out var from) || !nodes.TryGetValue(edge.inputNode ?? "", out var to) ||
                    Array.IndexOf(ShaderGraphNodes.Outputs(from), edge.outputPort) < 0 || !HasInput(to, edge.inputPort)) throw new FormatException("Shader graph connection has a missing node or port.");
                if (!incoming.TryAdd(edge.inputNode + ":" + edge.inputPort, edge)) Error(to, "An input can have only one connection.");
            }
            var complete = new HashSet<string>();
            void Visit(ShaderGraphNode node)
            {
                if (complete.Contains(node.id)) return;
                if (!visiting.Add(node.id)) Error(node, "Cyclic connections are not allowed.");
                foreach (var port in ShaderGraphNodes.Inputs(node))
                    if (incoming.TryGetValue(node.id + ":" + port.name, out var edge)) Visit(nodes[edge.outputNode]);
                visiting.Remove(node.id); complete.Add(node.id);
            }
            foreach (var node in graph.nodes) Visit(node);
        }
        static bool Identifier(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (char c in id) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_')) return false;
            return true;
        }
        static bool Finite(Vector4 v) => ProjectiveMatrix.Finite(v.x) && ProjectiveMatrix.Finite(v.y) && ProjectiveMatrix.Finite(v.z) && ProjectiveMatrix.Finite(v.w);
        static bool HasInput(ShaderGraphNode n, string name) { foreach (var p in ShaderGraphNodes.Inputs(n)) if (p.name == name) return true; return false; }
        static void Error(ShaderGraphNode n, string message) => throw new ShaderGraphException(n.id, ShaderGraphNodes.Name(n.kind) + ": " + message);
        static string Type(ShaderGraphValueType t) => t == ShaderGraphValueType.Boolean ? "bool" : "float" + (t == ShaderGraphValueType.Float ? "" : ShaderGraphNodes.Components(t).ToString());
        internal static string ConstantName(ShaderGraphNode n) => "_GraphC" + n.id;
        internal static string InputName(ShaderGraphNode n, string port) => "_GraphI" + n.id + "_" + port;
        static string Swizzle(ShaderGraphValueType type) => type switch { ShaderGraphValueType.Float2 => ".xy", ShaderGraphValueType.Float3 => ".xyz", ShaderGraphValueType.Float4 or ShaderGraphValueType.Color => "", _ => ".x" };
        Value Uniform(string name, ShaderGraphValueType type)
        {
            if (declarations.Add(name)) uniforms.Append("float4 ").Append(name).AppendLine(";");
            return new Value(type, type == ShaderGraphValueType.Boolean ? "(" + name + ".x != 0)" : name + Swizzle(type));
        }
        Value Cast(Value value, ShaderGraphValueType type, ShaderGraphNode node)
        {
            if (value.type == type) return value;
            if (ShaderGraphNodes.IsNumeric(value.type) && ShaderGraphNodes.IsNumeric(type))
            {
                int from = ShaderGraphNodes.Components(value.type), to = ShaderGraphNodes.Components(type);
                if (from == to) return new Value(type, value.text);
                string expression = "(" + value.text + ")";
                if (from > to) return new Value(type, expression + "." + "xyzw".Substring(0, to));
                if (from == 1)
                    return new Value(type, type == ShaderGraphValueType.Color ? "float4(" + expression + ".xxx, 1)" : expression + "." + new string('x', to));
                string tail = type == ShaderGraphValueType.Color ? (from == 2 ? ", 0, 1)" : ", 1)") : (to - from == 2 ? ", 0, 0)" : ", 0)");
                return new Value(type, Type(type) + "(" + value.text + tail);
            }
            Error(node, "Cannot connect " + value.type + " to " + type + ".");
            return default;
        }
        Value Input(ShaderGraphNode node, string name)
        {
            foreach (var port in ShaderGraphNodes.Inputs(node))
            {
                if (port.name != name) continue;
                Value value;
                if (incoming.TryGetValue(node.id + ":" + name, out var edge)) value = Emit(edge.outputNode, edge.outputPort);
                else if (port.uvDefault && node.FindInput(name) == null) value = new Value(ShaderGraphValueType.Float2, "graphUV");
                else
                {
                    if (port.type is ShaderGraphValueType.Texture or ShaderGraphValueType.Gradient or ShaderGraphValueType.Curve) Error(node, "Connect the " + name + " input.");
                    value = Uniform(InputName(node, name), node.FindInput(name)?.type ?? port.type ?? ShaderGraphValueType.Float);
                }
                return port.type.HasValue ? Cast(value, port.type.Value, node) : value;
            }
            Error(node, "Unknown input " + name); return default;
        }
        ShaderGraphValueType Common(ShaderGraphNode node, params Value[] inputs)
        {
            var type = ShaderGraphValueType.Float;
            foreach (var input in inputs)
            {
                if (!ShaderGraphNodes.IsNumeric(input.type)) Error(node, "Numeric input required.");
                var numeric = ShaderGraphNodes.NumericType(input.type);
                if (numeric > type) type = numeric;
            }
            return type;
        }
        Value Emit(string id, string port)
        {
            string key = id + ":" + port;
            if (values.TryGetValue(key, out var cached)) return cached;
            var n = nodes[id];
            Value V(string name) => Input(n, name);
            Value resultValue;
            Value Op(string function, string a = "A", string b = "B", bool infix = false)
            {
                var x = V(a); var y = V(b); var type = Common(n, x, y);
                return new Value(type, infix ? "(" + Cast(x, type, n).text + function + Cast(y, type, n).text + ")" : function + "(" + Cast(x, type, n).text + "," + Cast(y, type, n).text + ")");
            }
            switch (n.kind)
            {
                case ShaderGraphNodeKind.Reroute:
                    if (!incoming.TryGetValue(n.id + ":In", out var source)) { Error(n, "Connect the In input."); return default; }
                    resultValue = Emit(source.outputNode, source.outputPort);
                    values.Add(key, resultValue); result.outputTypes[key] = resultValue.type;
                    return resultValue;
                case ShaderGraphNodeKind.Parameter:
                    var p = graph.FindParameter(n.parameterId);
                    if (p == null) Error(n, "Select a parameter.");
                    string name = "_GraphP" + p.id;
                    if (usedParameters.Add(p.id)) { var declaration = p.Copy(); declaration.name = name; result.parameters.Add(declaration); }
                    resultValue = new Value(ShaderGraphNodes.ParameterType(p), name); break;
                case ShaderGraphNodeKind.Constant: resultValue = Uniform(ConstantName(n), n.valueType); break;
                case ShaderGraphNodeKind.UV: resultValue = new Value(ShaderGraphValueType.Float2, "graphUV"); break;
                case ShaderGraphNodeKind.ImageSize: resultValue = new Value(ShaderGraphValueType.Float2, "_GraphImageSize" + (port == "Size" ? ".xy" : ".zw")); break;
                case ShaderGraphNodeKind.TextureSize: resultValue = new Value(ShaderGraphValueType.Float2, V("Texture").text + "_TexelSize" + (port == "Size" ? ".zw" : ".xy")); break;
                case ShaderGraphNodeKind.SampleTexture: resultValue = new Value(ShaderGraphValueType.Color, $"GraphSample({V("Texture").text}, {V("UV").text}, {(int)n.wrap}, {(int)n.filter})"); break;
                case ShaderGraphNodeKind.SampleGradient: resultValue = new Value(ShaderGraphValueType.Color, V("Gradient").text + "_Sample(" + V("T").text + ")"); break;
                case ShaderGraphNodeKind.SampleCurve: resultValue = new Value(ShaderGraphValueType.Float, V("Curve").text + "_Sample(" + V("T").text + ")"); break;
                case ShaderGraphNodeKind.Split:
                    var split = V("In"); int channel = "XYZW".IndexOf(port, StringComparison.Ordinal);
                    if (!ShaderGraphNodes.IsNumeric(split.type) || channel >= ShaderGraphNodes.Components(split.type)) Error(n, "Component " + port + " is not present in the input.");
                    resultValue = new Value(ShaderGraphValueType.Float, split.type == ShaderGraphValueType.Float ? split.text : "(" + split.text + ")." + "xyzw"[channel]); break;
                case ShaderGraphNodeKind.Combine:
                    var components = new List<string>();
                    for (int i = 0; i < ShaderGraphNodes.Components(n.valueType); i++) components.Add(V("XYZW"[i].ToString()).text);
                    resultValue = new Value(n.valueType, Type(n.valueType) + "(" + string.Join(",", components) + ")"); break;
                case ShaderGraphNodeKind.Add: resultValue = Op("+", infix: true); break;
                case ShaderGraphNodeKind.Subtract: resultValue = Op("-", infix: true); break;
                case ShaderGraphNodeKind.Multiply: resultValue = Op("*", infix: true); break;
                case ShaderGraphNodeKind.Divide: resultValue = Op("/", infix: true); break;
                case ShaderGraphNodeKind.Min: resultValue = Op("min"); break;
                case ShaderGraphNodeKind.Max: resultValue = Op("max"); break;
                case ShaderGraphNodeKind.Power: resultValue = Op("pow"); break;
                case ShaderGraphNodeKind.Step: resultValue = Op("step", "Edge", "In"); break;
                case ShaderGraphNodeKind.Distance: case ShaderGraphNodeKind.Dot:
                    var pair = Op(n.kind == ShaderGraphNodeKind.Distance ? "distance" : "dot"); resultValue = new Value(ShaderGraphValueType.Float, pair.text); break;
                case ShaderGraphNodeKind.Abs: case ShaderGraphNodeKind.OneMinus: case ShaderGraphNodeKind.Negate:
                case ShaderGraphNodeKind.SquareRoot: case ShaderGraphNodeKind.Floor: case ShaderGraphNodeKind.Ceil:
                case ShaderGraphNodeKind.Round: case ShaderGraphNodeKind.Fraction: case ShaderGraphNodeKind.Sine:
                case ShaderGraphNodeKind.Cosine: case ShaderGraphNodeKind.Length: case ShaderGraphNodeKind.Normalize:
                    var unary = V("In"); Common(n, unary);
                    string fn = n.kind switch { ShaderGraphNodeKind.OneMinus => "1-", ShaderGraphNodeKind.Negate => "-", ShaderGraphNodeKind.SquareRoot => "sqrt", ShaderGraphNodeKind.Fraction => "frac", ShaderGraphNodeKind.Sine => "sin", ShaderGraphNodeKind.Cosine => "cos", _ => n.kind.ToString().ToLowerInvariant() };
                    resultValue = new Value(n.kind == ShaderGraphNodeKind.Length ? ShaderGraphValueType.Float : ShaderGraphNodes.NumericType(unary.type),
                        n.kind == ShaderGraphNodeKind.OneMinus || n.kind == ShaderGraphNodeKind.Negate ? "(" + fn + unary.text + ")" : fn + "(" + unary.text + ")"); break;
                case ShaderGraphNodeKind.Clamp: case ShaderGraphNodeKind.Smoothstep:
                    var v = V("In"); var lo = V("Min"); var hi = V("Max"); var width = Common(n, v, lo, hi);
                    string vs = Cast(v, width, n).text, ls = Cast(lo, width, n).text, hs = Cast(hi, width, n).text;
                    resultValue = new Value(width, n.kind == ShaderGraphNodeKind.Clamp ? $"clamp({vs},{ls},{hs})" : $"smoothstep({ls},{hs},{vs})"); break;
                case ShaderGraphNodeKind.Lerp: case ShaderGraphNodeKind.Select:
                    bool select = n.kind == ShaderGraphNodeKind.Select;
                    var a = V(select ? "True" : "A"); var b = V(select ? "False" : "B"); var common = Common(n, a, b);
                    string aa = Cast(a, common, n).text, bb = Cast(b, common, n).text;
                    resultValue = new Value(common, select ? $"({V("Condition").text} ? {aa} : {bb})" : $"lerp({aa},{bb},{V("T").text})"); break;
                case ShaderGraphNodeKind.Remap:
                    var remap = V("In"); Common(n, remap);
                    resultValue = new Value(ShaderGraphNodes.NumericType(remap.type), $"({V("OutMin").text} + ({remap.text} - {V("InMin").text}) / ({V("InMax").text} - {V("InMin").text}) * ({V("OutMax").text} - {V("OutMin").text}))"); break;
                case ShaderGraphNodeKind.Compare:
                    string compare = new[] { "<", "<=", ">", ">=", "==", "!=" }[n.option];
                    resultValue = new Value(ShaderGraphValueType.Boolean, $"({V("A").text} {compare} {V("B").text})"); break;
                case ShaderGraphNodeKind.TransformUV: resultValue = new Value(ShaderGraphValueType.Float2, $"GraphTransform({V("UV").text},{V("Offset").text},{V("Scale").text},{V("Rotation").text},{V("Center").text})"); break;
                case ShaderGraphNodeKind.Noise: resultValue = new Value(ShaderGraphValueType.Float, $"GraphNoise({V("UV").text}*{V("Scale").text},{V("Seed").text},{n.option})"); break;
                case ShaderGraphNodeKind.Shape: resultValue = new Value(ShaderGraphValueType.Float, $"GraphShape({V("UV").text},{V("Center").text},{V("Size").text},{V("Radius").text},{V("Feather").text},{n.option})"); break;
                case ShaderGraphNodeKind.Blend: resultValue = new Value(ShaderGraphValueType.Color, $"GraphBlend({V("Background").text},{V("Foreground").text},{V("Opacity").text},{n.option},{(n.hdr ? 1 : 0)})"); break;
                default: Error(n, "This node has no value output."); return default;
            }
            if (ShaderGraphNodes.Components(resultValue.type) > 0)
            {
                string variable = "n" + n.id + "_" + port;
                body.Append(Type(resultValue.type)).Append(' ').Append(variable).Append(" = ").Append(resultValue.text).AppendLine(";");
                resultValue = new Value(resultValue.type, variable);
            }
            values.Add(key, resultValue); result.outputTypes[key] = resultValue.type;
            return resultValue;
        }

        internal static ulong CodeFingerprint(ShaderGraph graph)
        {
            ulong hash = 14695981039346656037UL;
            void Add(string value) { if (value != null) foreach (char c in value) { hash ^= c; hash *= 1099511628211UL; } hash ^= 255; hash *= 1099511628211UL; }
            void Number(int value) { unchecked { hash ^= (uint)value; hash *= 1099511628211UL; } }
            if (graph == null) return 0;
            Number(graph.version);
            if (graph.parameters != null) foreach (var p in graph.parameters) { if (p == null) { Number(-1); continue; } Add(p.id); Number((int)p.type); Add(p.valueType); }
            if (graph.nodes != null) foreach (var n in graph.nodes)
            {
                if (n == null) { Number(-1); continue; }
                Add(n.id); Number((int)n.kind); Number((int)n.valueType); Add(n.parameterId); Number(n.option); Number(n.hdr ? 1 : 0); Number((int)n.wrap); Number((int)n.filter);
                if (n.inputs != null) foreach (var p in n.inputs) { if (p == null) { Number(-1); continue; } Add(p.port); Number((int)p.type); }
            }
            if (graph.edges != null) foreach (var e in graph.edges) { if (e == null) { Number(-1); continue; } Add(e.outputNode); Add(e.outputPort); Add(e.inputNode); Add(e.inputPort); }
            return hash;
        }

        const string Helpers = @"
float2 GraphTransform(float2 uv, float2 offset, float2 scale, float rotation, float2 center)
{
    float s, c; sincos(radians(rotation), s, c); float2 p = (uv-center)*scale;
    return float2(c*p.x-s*p.y,s*p.x+c*p.y)+center+offset;
}
float GraphNoise(float2 uv, float seed, int kind)
{
    fnl_state state = fnlCreateState((int)seed); state.frequency = 1; state.noise_type = kind;
    return fnlGetNoise2D(state, uv.x, uv.y)*0.5+0.5;
}
float GraphShape(float2 uv, float2 center, float2 size, float radius, float feather, int kind)
{
    float2 halfSize = max(abs(size)*0.5, 1e-7); float2 p = uv-center;
    float r = clamp(radius, 0, min(halfSize.x, halfSize.y));
    float2 q = abs(p)-halfSize+(kind == 2 ? r : 0);
    float d = kind == 0 ? (length(p/halfSize)-1)*min(halfSize.x,halfSize.y) : length(max(q,0))+min(max(q.x,q.y),0)-(kind == 2 ? r : 0);
    float aa = max(fwidth(d)*0.5, 1e-7); float edge = max(aa, max(feather,0)*0.5);
    return 1-smoothstep(-edge,edge,d);
}
float4 GraphFetch(WhimTexTexture tex, float2 pixel, int wrap)
{
    float2 size = max(tex.texelSize.zw,1);
    if (wrap == 3 && (any(pixel < 0) || any(pixel >= size))) return 0;
    if (wrap == 1) pixel -= floor(pixel/size)*size;
    else if (wrap == 2) { pixel -= floor(pixel/(size*2))*(size*2); pixel = min(pixel,size*2-1-pixel); }
    else pixel = clamp(pixel,0,size-1);
    return SampleTextureLod(tex,(pixel+0.5)/size,0);
}
float4 GraphSample(WhimTexTexture tex, float2 uv, int wrap, int filter)
{
    float2 p = uv*max(tex.texelSize.zw,1)-0.5;
    if (filter == 0) return GraphFetch(tex,floor(p+0.5),wrap);
    float2 i = floor(p), f = frac(p);
    return lerp(lerp(GraphFetch(tex,i,wrap),GraphFetch(tex,i+float2(1,0),wrap),f.x),
        lerp(GraphFetch(tex,i+float2(0,1),wrap),GraphFetch(tex,i+1,wrap),f.x),f.y);
}
float4 GraphBlend(float4 background, float4 foreground, float opacity, float mode, float hdr)
{
    float oldHdr = _HdrBlend; _HdrBlend = hdr;
    float4 b = float4(background.rgb*background.a, background.a);
    float4 f = float4(foreground.rgb*foreground.a, foreground.a);
    float4 c = CompositeBrushPixel(b,f,opacity,mode,0,1-hdr);
    _HdrBlend = oldHdr;
    return float4(c.a > 1e-7 ? c.rgb/c.a : background.rgb,c.a);
}
";
    }
}
