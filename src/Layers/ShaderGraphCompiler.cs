using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphCompilation
    {
        internal string source;
        internal int parameterCount;
        internal bool usesCanvasPosition;
        internal ShaderContextDependencies contextDependencies;
        internal bool usesTime => (contextDependencies & ShaderContextDependencies.Time) != 0;
        internal bool usesInstanceIndex => (contextDependencies & ShaderContextDependencies.InstanceIndex) != 0;
        internal readonly List<ShaderFXParameter> parameters = new List<ShaderFXParameter>();
        internal readonly List<ShaderFXParameter> shaderParameters = new List<ShaderFXParameter>();
        internal readonly Dictionary<string, ShaderGraphValueType> outputTypes = new Dictionary<string, ShaderGraphValueType>();
        internal readonly List<ShaderGraphNode> uniformNodes = new List<ShaderGraphNode>();
        internal HashSet<string> uniformNames;
    }

    internal enum ShaderGraphMessageLevel { Info, Warning, Error }
    internal readonly struct ShaderGraphMessage
    {
        internal readonly string nodeId, text;
        internal readonly ShaderGraphMessageLevel level;
        internal ShaderGraphMessage(string nodeId, string text, ShaderGraphMessageLevel level)
        { this.nodeId = nodeId; this.text = text; this.level = level; }
    }
    internal sealed class ShaderGraphException : InvalidOperationException
    {
        internal readonly string nodeId;
        internal readonly IReadOnlyList<ShaderGraphMessage> messages;
        internal ShaderGraphException(string nodeId, string message, IReadOnlyList<ShaderGraphMessage> messages = null) : base(message)
        { this.nodeId = nodeId; this.messages = messages ?? new[] { new ShaderGraphMessage(nodeId, message, ShaderGraphMessageLevel.Error) }; }
    }

    internal sealed partial class ShaderGraphCompiler
    {
        readonly ShaderGraph graph;
        readonly ShaderGraphCompilation result = new ShaderGraphCompilation();
        readonly Dictionary<string, ShaderGraphNode> nodes = new Dictionary<string, ShaderGraphNode>();
        readonly Dictionary<string, ShaderGraphEdge> incoming = new Dictionary<string, ShaderGraphEdge>();
        Dictionary<string, Value> values = new Dictionary<string, Value>();
        readonly HashSet<string> visiting = new HashSet<string>();
        readonly HashSet<string> declarations = new HashSet<string>();
        readonly HashSet<ShaderGraphNode> uniformNodes = new HashSet<ShaderGraphNode>();
        readonly StringBuilder inputDeclarations = new StringBuilder();
        readonly List<ShaderFXParameter> inputParameters = new List<ShaderFXParameter>();
        StringBuilder body = new StringBuilder();
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
            if (graph.subGraphs.Count != 0 || graph.activeSubGraph != null)
            {
                var expansion = ShaderGraphExpansion.Expand(graph);
                if (previewNode != null && expansion.rootOutputs.TryGetValue(previewNode + ":" + previewPort, out var endpoint))
                { previewNode = endpoint.node; previewPort = endpoint.port; }
                graph = expansion.graph;
                compiler = new ShaderGraphCompiler(graph);
                compiler.ValidateStructure(false, true);
            }
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
            if (previewNode == null)
            {
                Value alphaScale = compiler.Input(output, "AlphaScale");
                compiler.body.Append("float4 graphOutput = ").Append(rgba).AppendLine(";");
                compiler.body.Append("graphOutput.a *= ").Append(alphaScale.text).AppendLine(";");
                rgba = "graphOutput";
            }
            compiler.body.Append("return ").Append(rgba).AppendLine(";");
            string statements = compiler.body.ToString();
            string helpers = compiler.BuiltInSource(statements, out bool blend);
            var source = new StringBuilder("// @whimtex-effect Shader Graph\n");
            foreach (var parameter in compiler.result.parameters)
                AppendParameter(source, parameter);
            source.Append(compiler.inputDeclarations).AppendLine()
                .AppendLine("#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ProceduralUv.cginc\"")
                .Append(compiler.result.usesCanvasPosition ? CanvasCoordinatesSource : "")
                .Append(blend ? "static float _HdrBlend = 1.0;\n#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ColorBlend.cginc\"\n" : "")
                .Append(compiler.usesSampleFootprint ? "static float2 _GraphSampleDx, _GraphSampleDy;\n" : "")
                .Append(helpers)
                .Append(compiler.functionCode)
                .Append(compiler.samplingCode)
                .AppendLine("float4 ApplyFX(PinnedUV uv, float4 color)")
                .AppendLine("{").AppendLine(compiler.result.usesCanvasPosition
                    ? "    float2 graphUV = GraphSourceUv(float2(uv.x, uv.y));"
                    : "    float2 graphUV = ProceduralSourceUv(uv);");
            if (compiler.usesSampleFootprint)
                source.AppendLine(compiler.result.usesCanvasPosition
                    ? "    _GraphSampleDx = ddx(_GraphRawUV) * _GraphUvDirection; _GraphSampleDy = ddy(_GraphRawUV) * _GraphUvDirection;"
                    : "    _GraphSampleDx = ddx(graphUV); _GraphSampleDy = ddy(graphUV);");
            using (var lines = new System.IO.StringReader(statements))
                for (string line; (line = lines.ReadLine()) != null;) source.Append("    ").AppendLine(line);
            compiler.result.source = source.AppendLine("}").ToString();
            compiler.result.parameterCount = compiler.result.parameters.Count + compiler.declarations.Count;
            compiler.result.shaderParameters.AddRange(compiler.result.parameters);
            compiler.result.shaderParameters.AddRange(compiler.inputParameters);
            compiler.result.uniformNames = compiler.declarations;
            return compiler.result;
        }

        static void AppendParameter(StringBuilder source, ShaderFXParameter parameter)
        {
            source.Append("// @param ");
            if (parameter.pixelUnits) source.Append("px ");
            if (parameter.ValueType == null)
            {
                source.Append(parameter.type == ShaderFXParameterType.Texture2D ? "texture2D" :
                    parameter.type == ShaderFXParameterType.Gradient ? "gradient" : "curve").Append(' ').AppendLine(parameter.name);
                return;
            }
            // Preserve the storage type as well as Color/Boolean semantics. Resource
            // contents remain bound from the document, including arbitrary gradients.
            string declarationType = parameter.type == ShaderFXParameterType.Color ? "color(" + parameter.ValueType + ")" :
                parameter.type == ShaderFXParameterType.Toggle || parameter.IsBoolean ||
                    parameter.controls.Exists(control => control.type == ShaderFXParameterType.Toggle) ? "toggle(" + parameter.ValueType + ")" :
                parameter.type == ShaderFXParameterType.Enum ? "enum(" + parameter.ValueType + ")" :
                parameter.IsInteger ? "seed(" + parameter.ValueType + ")" :
                parameter.type == ShaderFXParameterType.Normal ? "normal" :
                parameter.type == ShaderFXParameterType.Point ? "point" : parameter.ValueType;
            source.Append(declarationType).Append(' ').Append(parameter.name).Append(" = ");
            if (parameter.Components > 1) source.Append('(');
            for (int i = 0; i < parameter.Components; i++)
            {
                if (i > 0) source.Append(", ");
                source.Append(parameter.IsBoolean ? parameter.GetNumber(i) == 0 ? "false" : "true" :
                    parameter.GetNumber(i).ToString("R", CultureInfo.InvariantCulture));
            }
            if (parameter.Components > 1) source.Append(')');
            if (parameter.type == ShaderFXParameterType.Enum)
            {
                source.Append(" { Current: ").Append(parameter.GetNumber().ToString("R", CultureInfo.InvariantCulture)).Append(" }");
            }
            source.AppendLine();
        }

        void ValidateStructure(bool includeDefinitions = true, bool expanded = false)
        {
            if (graph == null || graph.version != 1 || graph.nodes == null || graph.edges == null || graph.parameters == null)
                throw new FormatException("Unsupported or incomplete Shader graph.");
            if (graph.nodes.Count > (expanded ? 4096 : 512) || graph.edges.Count > (expanded ? 32768 : 4096) || graph.parameters.Count > 256)
                throw new FormatException("Shader graph exceeds the supported limits (512 nodes, 4096 edges, 256 parameters).");
            if (includeDefinitions || expanded) ValidateFunctions();
            if (includeDefinitions) ValidateSubGraphs();
            var ids = new HashSet<string>();
            foreach (var p in graph.parameters)
            {
                if (p == null || !Identifier(p.id) || !ids.Add(p.id)) throw new FormatException("Invalid or duplicate Shader parameter ID.");
                ShaderGraphNodes.ParameterType(p);
                if (p.type == ShaderFXParameterType.Texture2D && p.textureSource == ShaderFXTextureSource.Self)
                    throw new FormatException("A Shader layer cannot sample its own output. Choose a texture or another layer.");
                for (int i = 0; i < p.Components; i++)
                    ShaderFXNumericValue.Check(p.ValueType, p.GetNumber(i));
            }
            int outputs = 0;
            foreach (var n in graph.nodes)
            {
                if (n == null || !Identifier(n.id) || !nodes.TryAdd(n.id, n) || !Enum.IsDefined(typeof(ShaderGraphNodeKind), n.kind))
                    throw new FormatException("Invalid or duplicate Shader node.");
                if (n.kind == (graph.activeSubGraph == null ? ShaderGraphNodeKind.Output : ShaderGraphNodeKind.SubGraphOutput)) outputs++;
                if (n.kind == ShaderGraphNodeKind.SubGraph && n.subGraph == null) Error(n, "Sub Graph definition is missing.");
                if (n.kind == ShaderGraphNodeKind.SubGraphInput && n.interfacePort == null) Error(n, "Sub Graph input declaration is missing.");
                if (!expanded && (graph.activeSubGraph == null && ShaderGraphNodes.IsSubGraphInterface(n.kind) || graph.activeSubGraph != null && n.kind == ShaderGraphNodeKind.Output)) Error(n, "Output or interface node belongs to a different graph scope.");
                if (n.kind == ShaderGraphNodeKind.CustomFunction && n.function == null) Error(n, "Function definition is missing.");
                if (n.inputs == null || !ProjectiveMatrix.Finite(n.position.x) || !ProjectiveMatrix.Finite(n.position.y))
                    Error(n, "Invalid node data.");
                if (!ProjectiveMatrix.Finite(n.codeSize.x) || !ProjectiveMatrix.Finite(n.codeSize.y) || n.codeSize.x < 0 || n.codeSize.y < 0)
                    Error(n, "Invalid function editor size.");
                if (!Enum.IsDefined(typeof(ShaderGraphValueType), n.valueType) || !Enum.IsDefined(typeof(ShaderGraphWrap), n.wrap) || !Enum.IsDefined(typeof(ShaderGraphFilter), n.filter)) Error(n, "Unknown node option.");
                if (n.kind == ShaderGraphNodeKind.Parameter && !string.IsNullOrEmpty(n.parameterId) && graph.FindParameter(n.parameterId) == null) Error(n, "Parameter is missing.");
                if (n.kind == ShaderGraphNodeKind.Constant && !Enum.IsDefined(typeof(ShaderGraphConstant), n.constant)) Error(n, "Unknown mathematical constant.");
                if (n.kind == ShaderGraphNodeKind.Reroute && n.inputs.Count != 0) Error(n, "Reroute takes its value from its connection.");
                if (n.kind == ShaderGraphNodeKind.Combine && (n.valueType < ShaderGraphValueType.Float2 || n.valueType > ShaderGraphValueType.Float4)) Error(n, "Combine requires float2, float3 or float4.");
                var modes = ShaderGraphNodes.ModeNames(n.kind);
                if (modes != null && (n.option < 0 || n.option >= modes.Length) || n.kind == ShaderGraphNodeKind.Compare && (n.option < 0 || n.option > 5)) Error(n, "Unknown node mode.");
                if (!Enum.IsDefined(typeof(ShaderGraphFractal), n.fractal) ||
                    n.kind == ShaderGraphNodeKind.Noise && n.fractal > ShaderGraphFractal.PingPong ||
                    n.kind == ShaderGraphNodeKind.DomainWarp && n.fractal != ShaderGraphFractal.None && n.fractal < ShaderGraphFractal.Progressive) Error(n, "Unknown fractal mode.");
                if (n.kind == ShaderGraphNodeKind.Blend && !Enum.IsDefined(typeof(BlendMode), n.option)) Error(n, "Unknown blend mode.");
                ValidateColorOptions(n);
                var inputNames = new HashSet<string>();
                foreach (var input in n.inputs)
                {
                    if (input == null || !inputNames.Add(input.port ?? "") || !Finite(input.value) || !Enum.IsDefined(typeof(ShaderGraphValueType), input.type) || ShaderGraphNodes.Components(input.type) == 0 || !HasInput(n, input.port)) Error(n, "Invalid or duplicate input value.");
                }
            }
            if (outputs != 1) throw new FormatException(graph.activeSubGraph == null
                ? "A Shader graph requires exactly one Output node." : "A Sub Graph definition requires exactly one SubGraphOutput node.");
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
            ValidateAnnotations();
        }
        void ValidateAnnotations()
        {
            if (graph.notes == null || graph.groups == null || graph.notes.Count > 512 || graph.groups.Count > 512)
                throw new FormatException("Invalid Shader graph notes or groups (maximum 512 each).");
            var elements = new HashSet<string>(nodes.Keys);
            foreach (var note in graph.notes)
            {
                if (note == null || !Identifier(note.id) || !elements.Add(note.id) || note.title == null || note.contents == null ||
                    !ProjectiveMatrix.Finite(note.position.x) || !ProjectiveMatrix.Finite(note.position.y) ||
                    !ProjectiveMatrix.Finite(note.size.x) || !ProjectiveMatrix.Finite(note.size.y) || note.size.x <= 0 || note.size.y <= 0 ||
                    note.theme < 0 || note.theme > 1 || note.fontSize < 0 || note.fontSize > 3 ||
                    !ProjectiveMatrix.Finite(note.hueShift) || note.hueShift < -180 || note.hueShift > 180)
                    throw new FormatException("Invalid Shader graph note.");
            }
            var ids = new HashSet<string>(elements); var grouped = new HashSet<string>();
            foreach (var group in graph.groups)
            {
                if (group == null || !Identifier(group.id) || !ids.Add(group.id) || group.title == null || group.elements == null ||
                    !ProjectiveMatrix.Finite(group.position.x) || !ProjectiveMatrix.Finite(group.position.y))
                    throw new FormatException("Invalid Shader graph group.");
                foreach (var id in group.elements)
                    if (!elements.Contains(id ?? "") || !grouped.Add(id)) throw new FormatException("A node or note may belong to only one group; nested groups are not supported.");
            }
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
        internal static string InputName(ShaderGraphNode n, string port) => "_GraphI" + (n.uniformNodeId ?? n.id) + "_" + (n.uniformPort ?? port);
        static string Swizzle(ShaderGraphValueType type) => type switch { ShaderGraphValueType.Float2 => ".xy", ShaderGraphValueType.Float3 => ".xyz", ShaderGraphValueType.Float4 or ShaderGraphValueType.Color => "", _ => ".x" };
        Value Uniform(string name, ShaderGraphValueType type, Vector4 value)
        {
            if (declarations.Add(name))
            {
                inputParameters.Add(new ShaderFXParameter { name = name, id = name, type = ShaderFXParameterType.Vector, vectorValue = value });
                inputDeclarations.Append("// @param hidden float4 ").Append(name).Append(" = (");
                for (int i = 0; i < 4; i++)
                {
                    if (i > 0) inputDeclarations.Append(", ");
                    inputDeclarations.Append(value[i].ToString("R", CultureInfo.InvariantCulture));
                }
                inputDeclarations.AppendLine(")");
            }
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
                    var type = node.FindInput(name)?.type ?? port.type ?? ShaderGraphValueType.Float;
                    if (node.kind == ShaderGraphNodeKind.Swizzle) type = ShaderGraphNodes.SwizzleInputType(node.swizzle, type);
                    value = Uniform(InputName(node, name), type, node.FindInput(name)?.value ?? port.value);
                    if (uniformNodes.Add(node)) result.uniformNodes.Add(node);
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
                case ShaderGraphNodeKind.CustomFunction: return EmitFunction(n, port);
                case ShaderGraphNodeKind.SubGraphInput: resultValue = Input(n, "In"); break;
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
                case ShaderGraphNodeKind.Constant:
                    resultValue = new Value(ShaderGraphValueType.Float, n.constant switch
                    {
                        ShaderGraphConstant.PI => "3.14159265358979323846",
                        ShaderGraphConstant.TAU => "6.28318530717958647693",
                        ShaderGraphConstant.PHI => "1.61803398874989484820",
                        ShaderGraphConstant.E => "2.71828182845904523536",
                        ShaderGraphConstant.SQRT2 => "1.41421356237309504880",
                        _ => throw new ShaderGraphException(n.id, "Unknown mathematical constant.")
                    }); break;
                case ShaderGraphNodeKind.Swizzle: resultValue = EmitSwizzle(n); break;
                case ShaderGraphNodeKind.InstanceIndex:
                    result.contextDependencies |= ShaderContextDependencies.InstanceIndex;
                    resultValue = new Value(ShaderGraphValueType.Float, $"({V("Start").text} + (float)_WhimTexInstanceIndex * {V("Scale").text})"); break;
                case ShaderGraphNodeKind.Time:
                    result.contextDependencies |= ShaderContextDependencies.Time;
                    resultValue = new Value(ShaderGraphValueType.Float, $"({V("Start").text} + _WhimTexTime * {V("Scale").text})"); break;
                case ShaderGraphNodeKind.UV: resultValue = new Value(ShaderGraphValueType.Float2, "graphUV"); break;
                case ShaderGraphNodeKind.CanvasPosition:
                    result.usesCanvasPosition = true;
                    resultValue = new Value(ShaderGraphValueType.Float2, "GraphCanvasPosition(graphUV)" + (port == "AspectX" ? " * float2(_CanvasSize.x * _CanvasSize.w, 1)" :
                        port == "AspectY" ? " * float2(1, _CanvasSize.y * _CanvasSize.z)" : "")); break;
                case ShaderGraphNodeKind.CanvasData:
                    resultValue = port == "PixelScale" ? new Value(ShaderGraphValueType.Float, "_PixelScale") :
                        new Value(ShaderGraphValueType.Float2, "_CanvasSize" + (port == "Size" ? ".xy" : ".zw")); break;
                case ShaderGraphNodeKind.TextureSize: resultValue = new Value(ShaderGraphValueType.Float2, V("Texture").text + "_TexelSize" + (port == "Size" ? ".zw" : ".xy")); break;
                case ShaderGraphNodeKind.SampleTexture: resultValue = new Value(ShaderGraphValueType.Color, $"GraphSample({V("Texture").text}, {V("UV").text}, {(int)n.wrap}, {(int)n.filter})"); break;
                case ShaderGraphNodeKind.SampleInput: resultValue = new Value(ShaderGraphValueType.Color, $"GraphSample(_MainTex, {V("UV").text}, {(int)n.wrap}, {(int)n.filter})"); break;
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
                case ShaderGraphNodeKind.Unlerp:
                    var unlerpA = V("A"); var unlerpB = V("B"); var unlerpT = V("T"); var unlerpType = Common(n, unlerpA, unlerpB, unlerpT);
                    string ua = Cast(unlerpA, unlerpType, n).text, ub = Cast(unlerpB, unlerpType, n).text, ut = Cast(unlerpT, unlerpType, n).text;
                    resultValue = new Value(unlerpType, $"(({ut} - {ua}) / ({ub} - {ua}))"); break;
                case ShaderGraphNodeKind.Remap:
                    var remap = V("In"); Common(n, remap);
                    resultValue = new Value(ShaderGraphNodes.NumericType(remap.type), $"({V("OutMin").text} + ({remap.text} - {V("InMin").text}) / ({V("InMax").text} - {V("InMin").text}) * ({V("OutMax").text} - {V("OutMin").text}))"); break;
                case ShaderGraphNodeKind.Compare:
                    string compare = new[] { "<", "<=", ">", ">=", "==", "!=" }[n.option];
                    resultValue = new Value(ShaderGraphValueType.Boolean, $"({V("A").text} {compare} {V("B").text})"); break;
                case ShaderGraphNodeKind.TransformUV: resultValue = new Value(ShaderGraphValueType.Float2, $"GraphTransform({V("UV").text},{V("Offset").text},{V("Scale").text},{V("Rotation").text},{V("Center").text})"); break;
                case ShaderGraphNodeKind.Noise:
                case ShaderGraphNodeKind.Shape:
                case ShaderGraphNodeKind.Cellular:
                case ShaderGraphNodeKind.Random:
                case ShaderGraphNodeKind.Checkerboard:
                case ShaderGraphNodeKind.Stripes:
                case ShaderGraphNodeKind.Grid:
                case ShaderGraphNodeKind.Rings:
                case ShaderGraphNodeKind.GradientFactor:
                case ShaderGraphNodeKind.CombineDistance:
                case ShaderGraphNodeKind.PolarCoordinates:
                case ShaderGraphNodeKind.PixelateUV:
                case ShaderGraphNodeKind.Repeat:
                case ShaderGraphNodeKind.Mirror:
                case ShaderGraphNodeKind.Kaleidoscope:
                case ShaderGraphNodeKind.DomainWarp:
                case ShaderGraphNodeKind.VectorDisplacement:
                case ShaderGraphNodeKind.Twirl:
                case ShaderGraphNodeKind.BulgePinch:
                case ShaderGraphNodeKind.Wave:
                case ShaderGraphNodeKind.Ripple: resultValue = EmitProcedural(n, port); break;
                case ShaderGraphNodeKind.Blend: resultValue = new Value(ShaderGraphValueType.Color, $"GraphBlend({V("Background").text},{V("Foreground").text},{V("Opacity").text},{n.option},{(n.hdr ? 1 : 0)})"); break;
                default:
                    if (ShaderGraphNodes.SurfaceCategory(n.kind) != null) { resultValue = EmitSurface(n, port); break; }
                    if (ShaderGraphNodes.IsExtended(n.kind)) { resultValue = EmitExtended(n); break; }
                    if (ShaderGraphNodes.IsFilter(n.kind)) { resultValue = EmitFilter(n); break; }
                    if (ShaderGraphNodes.TryColorOutputType(n.kind, out _)) { resultValue = EmitColor(n); break; }
                    Error(n, "This node has no value output."); return default;
            }
            if (ShaderGraphNodes.Components(resultValue.type) > 0)
            {
                string variable = "n" + n.id + "_" + port;
                body.Append("#line 1 \"").Append(n.id).AppendLine("\"");
                body.Append(Type(resultValue.type)).Append(' ').Append(variable).Append(" = ").Append(resultValue.text).AppendLine(";");
                resultValue = new Value(resultValue.type, variable);
            }
            values.Add(key, resultValue); result.outputTypes[key] = resultValue.type;
            return resultValue;
        }

        Value EmitSwizzle(ShaderGraphNode node)
        {
            string mask = node.swizzle;
            if (string.IsNullOrEmpty(mask) || mask.Length > 4) Error(node, "Mask must contain 1 to 4 components: xyzw, rgba or _ for zero.");
            var input = Input(node, "In");
            if (!ShaderGraphNodes.IsNumeric(input.type)) Error(node, "Numeric or Color input required.");
            input = Cast(input, ShaderGraphNodes.SwizzleInputType(mask, input.type), node);
            int width = ShaderGraphNodes.Components(input.type);
            var type = ShaderGraphNodes.SwizzleType(mask);
            var expression = new StringBuilder();
            if (mask.Length > 1) expression.Append(Type(type)).Append('(');
            for (int i = 0; i < mask.Length; i++)
            {
                int component = ShaderGraphNodes.SwizzleComponent(mask[i]);
                if (component == -2) Error(node, "Unknown component '" + mask[i] + "'. Use xyzw, rgba or _ for zero.");
                if (i > 0) expression.Append(", ");
                if (component == -1) expression.Append("0.0");
                else
                {
                    expression.Append('(').Append(input.text).Append(')');
                    if (width > 1) expression.Append('.').Append("xyzw"[component]);
                }
            }
            if (mask.Length > 1) expression.Append(')');
            return new Value(type, expression.ToString());
        }

        internal static ulong CodeFingerprint(ShaderGraph graph)
        {
            ulong hash = 14695981039346656037UL;
            void Add(string value) { if (value != null) foreach (char c in value) { hash ^= c; hash *= 1099511628211UL; } hash ^= 255; hash *= 1099511628211UL; }
            void Number(int value) { unchecked { hash ^= (uint)value; hash *= 1099511628211UL; } }
            if (graph == null) return 0;
            Number(graph.version);
            if (graph.parameters != null) foreach (var p in graph.parameters) { if (p == null) { Number(-1); continue; } Add(p.id); Number((int)p.type); Add(p.valueType); }
            void AddNodes(List<ShaderGraphNode> nodeList)
            {
            if (nodeList != null) foreach (var n in nodeList)
            {
                if (n == null) { Number(-1); continue; }
                Add(n.id); Number((int)n.kind); Number((int)n.valueType); Add(n.parameterId); Add(n.functionId); Add(n.subGraphId); Number(n.option); Number(n.hdr ? 1 : 0); Number((int)n.wrap); Number((int)n.filter);
                if (n.kind is ShaderGraphNodeKind.Noise or ShaderGraphNodeKind.DomainWarp) Number((int)n.fractal);
                if (n.kind == ShaderGraphNodeKind.Constant) Number((int)n.constant);
                if (n.kind == ShaderGraphNodeKind.Swizzle) Add(n.swizzle);
                if (n.kind == ShaderGraphNodeKind.ConvertColor) { Number((int)n.inputColorSpace); Number((int)n.outputColorSpace); }
                if (ShaderGraphNodes.UsesChannel(n.kind)) Number((int)n.channel);
                if (ShaderGraphNodes.UsesHeightTexture(n.kind)) { Number((int)n.derivative); Number((int)n.heightSource); }
                if (ShaderGraphNodes.UsesNormalEncoding(n.kind)) Number((int)n.normalEncoding);
                if (n.inputs != null) foreach (var p in n.inputs) { if (p == null) { Number(-1); continue; } Add(p.port); Number((int)p.type); }
            }
            }
            void AddEdges(List<ShaderGraphEdge> edgeList)
            { if (edgeList != null) foreach (var e in edgeList) { if (e == null) { Number(-1); continue; } Add(e.outputNode); Add(e.outputPort); Add(e.inputNode); Add(e.inputPort); } }
            AddNodes(graph.nodes); AddEdges(graph.edges);
            if (graph.subGraphs != null) foreach (var definition in graph.subGraphs)
            {
                if (definition == null) { Number(-1); continue; }
                Add(definition.id); AddNodes(definition.nodes); AddEdges(definition.edges);
                void Pins(List<ShaderGraphInterfacePort> pins)
                { if (pins != null) foreach (var pin in pins) { if (pin == null) { Number(-1); continue; } Add(pin.id); Number((int)pin.type); Number(pin.uvDefault ? 1 : 0); } }
                Pins(definition.inputs); Pins(definition.outputs);
            }
            if (graph.functions != null) foreach (var f in graph.functions)
            {
                if (f == null) { Number(-1); continue; }
                Add(f.id); Number(f.inline ? 1 : 0); Number(f.fullSource ? 1 : 0); Add(f.sourceId); if (f.inline) hash = RenderFingerprint.Mix(hash, f.BodyFingerprint); Add(f.entryPoint); Add(f.returnType); Add(f.returnName); Number((int)f.returnValueType);
                if (f.ports != null) foreach (var p in f.ports) { if (p == null) { Number(-1); continue; } Add(p.name); Number((int)p.type); Number((int)p.direction); Number(p.uvDefault ? 1 : 0); }
            }
            if (graph.sources != null) foreach (var source in graph.sources)
            {
                if (source == null) { Number(-1); continue; }
                Add(source.id); hash = RenderFingerprint.Mix(hash, source.CodeFingerprint);
                if (source.includes != null) foreach (var include in source.includes)
                { if (include == null) { Number(-1); continue; } Add(include.requested); Add(include.sourceId); }
            }
            return hash;
        }

        const string CanvasCoordinatesSource = @"
int _GraphCanvasCoordinates, _GraphTiling;
float3 _GraphCanvasRow0, _GraphCanvasRow1, _GraphCanvasRow2;
static float2 _GraphCanvasUV, _GraphRawUV, _GraphSourceUV, _GraphUvDirection;
float2 GraphSourceUv(float2 canvasUV)
{
    _GraphCanvasUV = canvasUV;
    _GraphRawUV = ProceduralSourceUv(canvasUV);
    _GraphSourceUV = _GraphRawUV;
    _GraphUvDirection = 1;
    if (_GraphCanvasCoordinates != 0)
    {
        if (_GraphTiling == 0) clip(min(min(_GraphRawUV.x, _GraphRawUV.y), min(1 - _GraphRawUV.x, 1 - _GraphRawUV.y)));
        else if (_GraphTiling == 1) _GraphSourceUV = frac(_GraphRawUV);
        else if (_GraphTiling == 2)
        {
            float2 phase = frac(_GraphRawUV * 0.5) * 2;
            _GraphSourceUV = 1 - abs(phase - 1);
            _GraphUvDirection = 1 - 2 * step(1, phase);
        }
        else if (_GraphTiling == 4) _GraphSourceUV = saturate(_GraphRawUV);
    }
    return _GraphSourceUV;
}
float2 GraphLocalToCanvas(float2 localUV)
{
    float3 p = float3(localUV, 1);
    float w = dot(_GraphCanvasRow2, p);
    return float2(dot(_GraphCanvasRow0, p), dot(_GraphCanvasRow1, p)) * (abs(w) > 1e-8 ? rcp(w) : 0);
}
float2 GraphCanvasPosition(float2 localUV)
{
    if (_GraphCanvasCoordinates == 0) return localUV;
    float2 offset = (localUV - _GraphSourceUV) * _GraphUvDirection;
    return _GraphCanvasUV + (GraphLocalToCanvas(_GraphRawUV + offset) - GraphLocalToCanvas(_GraphRawUV));
}
";

        const string Helpers = @"
float2 GraphTransform(float2 uv, float2 offset, float2 scale, float rotation, float2 center)
{
    float s, c; sincos(radians(rotation), s, c); float2 p = (uv-center)*scale;
    return float2(c*p.x-s*p.y,s*p.x+c*p.y)+center+offset;
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
    if (mode == 29) return opacity <= 0 ? background : CrossfadePixels(background, foreground, saturate(opacity));
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
