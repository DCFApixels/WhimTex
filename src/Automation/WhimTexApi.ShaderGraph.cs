using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static JObject ShaderSnapshot(ShaderLayerBehaviour shader, WhimTexDocument document)
            => WhimTexDocumentJson.ShaderLayerSnapshot(shader, document);

        private static void SetShader(WhimTexDocument document, ShaderLayerBehaviour shader, JObject settings,
            System.Collections.Generic.Dictionary<string, Layer> aliases)
        {
            Keys(settings, "program", "presetId", "mode", "inputMode", "inputTexture", "inputLayerId", "draftGraph", "parameterLayoutOps", "draftCode", "workingCode", "draftCodeParameters", "compile");
            var inputMode = Enum(settings, "inputMode", shader.inputMode);
            var inputTexture = settings["inputTexture"] == null ? shader.inputTexture :
                WhimTexDocumentJson.ReadShaderInputTexture(settings["inputTexture"]);
            string inputLayerId = shader.inputLayerId;
            if (settings["inputLayerId"] != null)
            {
                Require(settings["inputLayerId"].Type is JTokenType.String or JTokenType.Null,
                    "shader.inputLayerId must be a layer ID, @alias or null.");
                inputLayerId = (string)settings["inputLayerId"];
                if (inputLayerId?.StartsWith("@", System.StringComparison.Ordinal) == true)
                {
                    Require(aliases != null, "Layer aliases are unavailable in this operation.");
                    inputLayerId = Resolve(document, inputLayerId, aliases).Id;
                }
            }
            bool compile = Bool(settings, "compile");
            var program = ReadShaderProgramEdit(shader.program, settings, document, aliases);
            bool invalidate = settings["program"] != null || settings["presetId"] != null || program.mode != shader.mode ||
                (program.mode == ShaderProgramMode.Graph ? settings["draftGraph"] != null || settings["parameterLayoutOps"] != null :
                settings["workingCode"] != null || settings["draftCodeParameters"] != null);
            WhimTexDocumentJson.RestoreMissingAssets(document);
            shader.inputMode = inputMode;
            shader.inputLayerId = inputLayerId;
            if (settings["inputTexture"] != null)
                WhimTexDocumentJson.SetShaderInputTexture(shader, inputTexture, settings["inputTexture"]);
            shader.program = program;
            WhimTexDocumentJson.CaptureMissingAssets(document);
            if (invalidate) shader.InvalidateGraph();
            if (compile)
            {
                bool compiled = shader.mode == ShaderProgramMode.Graph ? shader.CompileGraph(document) : shader.CompileCode(document);
                Require(compiled, shader.mode == ShaderProgramMode.Graph ? shader.Diagnostics : shader.CodeDiagnostics, "shader_compile_failed");
                WhimTexDocumentJson.CaptureMissingAssets(document);
            }
        }

        private static ShaderProgram ReadShaderProgramEdit(ShaderProgram current, JObject settings, WhimTexDocument document,
            System.Collections.Generic.Dictionary<string, Layer> aliases)
        {
            ShaderProgram program;
            if (settings["presetId"] != null)
            {
                Require(!HasFxProgramEdit(settings) && settings["parameterLayoutOps"] == null, "presetId supplies the complete program.");
                program = ShaderPresetLink.Load(FindShaderPreset(Text(settings, "presetId")));
            }
            else if (settings["program"] != null)
            {
                foreach (string field in new[] { "mode", "draftGraph", "parameterLayoutOps", "draftCode", "workingCode", "draftCodeParameters" })
                    Require(settings[field] == null, "program cannot be combined with " + field + ".");
                program = WhimTexDocumentJson.ReadShaderProgram(Obj(settings["program"], "program"));
            }
            else
            {
                program = new ShaderProgram
                {
                    mode = Enum(settings, "mode", current.mode),
                    draftGraph = settings["draftGraph"] == null ? current.draftGraph : WhimTexDocumentJson.ReadShaderGraph(Obj(settings["draftGraph"], "draftGraph")),
                    workingGraph = current.workingGraph,
                    draftCode = ShaderCodeText(settings, "draftCode", current.draftCode),
                    workingCode = ShaderCodeText(settings, "workingCode", current.workingCode),
                    draftCodeParameters = current.draftCodeParameters,
                    workingCodeParameters = current.workingCodeParameters,
                    presetId = current.presetId, presetVersion = current.presetVersion
                };
                if (settings["parameterLayoutOps"] != null)
                {
                    Require(settings["draftGraph"] == null, "parameterLayoutOps cannot be combined with draftGraph replacement.");
                    program.draftGraph = EditShaderParameterLayout(current.draftGraph, settings["parameterLayoutOps"], document);
                }
                if (settings["draftCodeParameters"] != null)
                {
                    Require(settings["draftCodeParameters"] is JArray, "draftCodeParameters must be an array.");
                    program.draftCodeParameters = WhimTexDocumentJson.ReadShaderCodeParameters((JArray)settings["draftCodeParameters"]);
                }
                if (settings["draftGraph"] != null || settings["parameterLayoutOps"] != null || settings["draftCode"] != null || settings["workingCode"] != null)
                { program.presetId = null; program.presetVersion = 0; }
            }
            ResolveShaderProgramAliases(program, document, aliases);
            WhimTexDocumentJson.ValidateShaderProgramStorage(program);
            return program;
        }

        // Bounded authoring edits operate on a detached graph. A rejected operation never
        // publishes a partial hierarchy or changes the current/working program's values.
        private static ShaderGraph EditShaderParameterLayout(ShaderGraph current, JToken token, WhimTexDocument document)
        {
            Require(token is JArray operations && operations.Count > 0 && operations.Count <= 128,
                "parameterLayoutOps must contain 1..128 operations.");
            var graph = WhimTexDocumentJson.ReadShaderGraph(WhimTexDocumentJson.ShaderGraphSnapshot(current, document));
            ShaderGraphParameterLayout.Normalize(graph);
            var aliases = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);

            string ItemId(JObject operation, string field, bool optional = false)
            {
                if (optional && (operation[field] == null || operation[field].Type == JTokenType.Null)) return null;
                string id = Text(operation, field);
                Require(!string.IsNullOrEmpty(id), field + " requires a layout item ID or an earlier @alias.");
                if (id.StartsWith("@", System.StringComparison.Ordinal))
                {
                    Require(aliases.TryGetValue(id.Substring(1), out string resolved), "Unknown layout alias: " + id);
                    id = resolved;
                }
                Require(ShaderGraphParameterLayout.Find(graph, id) != null, "Unknown layout item: " + id);
                return id;
            }
            System.Collections.Generic.List<ShaderGraphParameterItem> Children(string parentId)
            {
                if (parentId == null) return graph.parameterLayout;
                var parent = ShaderGraphParameterLayout.Find(graph, parentId);
                Require(parent != null && string.IsNullOrEmpty(parent.parameterId), "parentId must refer to a container.");
                return parent.children;
            }
            string Alias(JObject operation, string field)
            {
                if (operation[field] == null) return null;
                string name = Text(operation, field);
                Require(System.Text.RegularExpressions.Regex.IsMatch(name, @"\A[A-Za-z][A-Za-z0-9_]{0,63}\z") && !aliases.ContainsKey(name),
                    field + " must be a unique alias of 1..64 letters, digits or underscores, starting with a letter (without @).");
                return name;
            }
            void Move(ShaderGraphParameterItem item, string parentId, JObject operation)
            {
                var siblings = Children(parentId);
                int count = siblings.Count;
                int previousIndex = siblings.IndexOf(item);
                if (previousIndex >= 0) count--;
                int index = Int(operation, "index", count, 0, count);
                // The model accepts an insertion slot before removing the source item;
                // the API exposes its final zero-based position in the destination.
                if (previousIndex >= 0 && index > previousIndex) index++;
                ShaderGraphParameterLayout.Move(graph, item.id, parentId, index);
            }

            int operationIndex = 0;
            foreach (var value in (JArray)token)
            {
                try
                {
                    var operation = Obj(value, "parameterLayoutOps[" + operationIndex + "]");
                    string kind = Text(operation, "op");
                    switch (kind)
                    {
                        case "addContainer":
                        {
                            Keys(operation, "op", "kind", "title", "parentId", "index", "as", "tabAs");
                            Require(operation["kind"] != null && operation["title"] != null, "addContainer requires kind and title.");
                            var containerKind = Enum(operation, "kind", ShaderContainerKind.Group);
                            string title = Text(operation, "title");
                            string parentId = ItemId(operation, "parentId", optional: true);
                            string alias = Alias(operation, "as"), tabAlias = Alias(operation, "tabAs");
                            Require(tabAlias == null || containerKind == ShaderContainerKind.Tabs,
                                "tabAs is only valid when adding Tabs.");
                            Require(alias == null || alias != tabAlias, "as and tabAs must be different aliases.");
                            int count = Children(parentId).Count;
                            int index = Int(operation, "index", count, 0, count);
                            var item = ShaderGraphParameterLayout.AddContainer(graph, containerKind, title, parentId);
                            if (index != count) ShaderGraphParameterLayout.Move(graph, item.id, parentId, index);
                            if (alias != null) aliases.Add(alias, item.id);
                            if (tabAlias != null) aliases.Add(tabAlias, item.children[0].id);
                            break;
                        }
                        case "rename":
                        case "setHeader":
                        {
                            Keys(operation, "op", "id", kind == "rename" ? "title" : "headerParameterId");
                            var item = ShaderGraphParameterLayout.Find(graph, ItemId(operation, "id"));
                            Require(string.IsNullOrEmpty(item.parameterId), kind + " requires a container item.");
                            if (kind == "rename")
                            {
                                Require(operation["title"] != null, "rename requires title.");
                                item.title = Text(operation, "title");
                            }
                            else
                            {
                                Require(operation["headerParameterId"] != null, "setHeader requires headerParameterId (null clears it).");
                                item.headerParameterId = operation["headerParameterId"].Type == JTokenType.Null ? null : Text(operation, "headerParameterId");
                            }
                            break;
                        }
                        case "setTitleVisibility":
                        {
                            Keys(operation, "op", "id", "visible");
                            Require(operation["visible"] != null, "setTitleVisibility requires visible.");
                            ShaderGraphParameterLayout.SetTitleVisible(ShaderGraphParameterLayout.Find(graph, ItemId(operation, "id")), Bool(operation, "visible", true));
                            break;
                        }
                        case "move":
                        {
                            Keys(operation, "op", "id", "parentId", "index");
                            var item = ShaderGraphParameterLayout.Find(graph, ItemId(operation, "id"));
                            Move(item, ItemId(operation, "parentId", optional: true), operation);
                            break;
                        }
                        case "ungroup":
                            Keys(operation, "op", "id");
                            ShaderGraphParameterLayout.Ungroup(graph, ItemId(operation, "id"));
                            break;
                        default:
                            throw new WhimTexApiException("invalid_request", "Unknown parameter layout operation: " + kind);
                    }
                    ShaderGraphParameterLayout.Validate(graph);
                }
                catch (System.Exception error) when (error is System.FormatException || error is System.InvalidOperationException || error is WhimTexApiException)
                {
                    throw new WhimTexApiException("invalid_request", "parameterLayoutOps[" + operationIndex + "]: " + error.Message);
                }
                operationIndex++;
            }
            return graph;
        }

        private static void ResolveShaderProgramAliases(ShaderProgram program, WhimTexDocument document,
            System.Collections.Generic.Dictionary<string, Layer> aliases)
        {
            foreach (var parameter in ShaderProgramStorage.StoredTextureParameters(program, true))
                if (parameter.textureLayerId?.StartsWith("@", System.StringComparison.Ordinal) == true)
                {
                    Require(aliases != null, "Layer aliases are unavailable in this operation.");
                    parameter.textureLayerId = Resolve(document, parameter.textureLayerId, aliases).Id;
                }
        }

        private static string ShaderCodeText(JObject settings, string field, string fallback)
        {
            if (settings[field] == null) return fallback;
            Require(settings[field].Type == JTokenType.String, "shader." + field + " must be a string.");
            string source = (string)settings[field];
            Require(source.Length <= 2 * 1024 * 1024, "shader." + field + " exceeds 2 Mi characters.");
            return source;
        }

        private static JArray ShaderNodeCatalog()
        {
            var result = new JArray();
            foreach (var kind in ShaderGraphNodes.Kinds)
            {
                // Interface nodes belong to a definition, never to ordinary node creation.
                if (ShaderGraphNodes.IsSubGraphInterface(kind)) continue;
                if (kind == ShaderGraphNodeKind.SubGraph)
                {
                    result.Add(new JObject
                    {
                        ["kind"] = kind.ToString(), ["category"] = ShaderGraphNodes.Category(kind),
                        ["requiresDefinition"] = true, ["definitionField"] = "subGraphId",
                        ["definitionTable"] = "subGraphs", ["dynamicInputs"] = true, ["dynamicOutputs"] = true,
                        ["inputs"] = new JArray(), ["outputs"] = new JArray(),
                        ["description"] = "Requires subGraphId referencing graph.subGraphs. Input/output port keys are stable interface IDs, not display names. Empty catalog ports are placeholders; read the referenced definition. SubGraphInput and SubGraphOutput are definition-only nodes. Definitions share root parameters/functions/sources and expand during compilation; no external dependency."
                    });
                    continue;
                }
                var node = ShaderGraphNodes.Create(kind, default);
                var entry = new JObject
                {
                    ["kind"] = kind.ToString(),
                    ["category"] = ShaderGraphNodes.Category(kind),
                    ["inputs"] = ShaderNodeInputs(node),
                    ["outputs"] = new JArray(ShaderGraphNodes.Outputs(node))
                };
                var outputTypes = ShaderNodeOutputTypes(node);
                if (outputTypes.Count > 0) entry["outputTypes"] = outputTypes;
                if (kind is ShaderGraphNodeKind.SampleTexture or ShaderGraphNodeKind.SampleInput or ShaderGraphNodeKind.SampleGradient)
                    entry["outputLabels"] = new JObject { ["Out"] = "RGBA" };
                var modeNames = ShaderGraphNodes.ModeNames(kind);
                if (modeNames != null)
                {
                    int defaultOption = node.option;
                    entry["modeLabel"] = ShaderGraphNodes.ModeLabel(kind);
                    entry["defaultOption"] = defaultOption;
                    var modes = new JArray();
                    bool dynamicInputs = kind is ShaderGraphNodeKind.Shape or ShaderGraphNodeKind.BilateralBlur or ShaderGraphNodeKind.GaborNoise or ShaderGraphNodeKind.RGBShift;
                    for (int option = 0; option < modeNames.Length; option++)
                    {
                        node.option = option;
                        var mode = new JObject { ["option"] = option, ["name"] = modeNames[option] };
                        if (dynamicInputs) mode["inputs"] = ShaderNodeInputs(node);
                        if (kind == ShaderGraphNodeKind.Scatter)
                        {
                            if (option == 2) mode["outputTypes"] = ShaderNodeOutputTypes(node);
                            else mode["outputTypeFromInput"] = "Source";
                        }
                        if (kind == ShaderGraphNodeKind.BilateralBlur) mode["sampledInputs"] = ShaderNodeSampledInputs(node);
                        modes.Add(mode);
                    }
                    node.option = defaultOption;
                    entry["modes"] = modes;
                    if (dynamicInputs) entry["dynamicInputs"] = true;
                }
                if (ShaderGraphNodes.IsNoise(kind))
                {
                    entry["dynamicInputs"] = true;
                    entry["dynamicOutputs"] = true;
                    entry["defaultOutput"] = ShaderGraphNodes.Outputs(node)[0];
                    if (ShaderGraphNodes.IsWarpNoise(kind))
                    {
                        var fractalModes = new JArray();
                        foreach (var fractal in ShaderNodeNoiseFractals(kind)) fractalModes.Add(fractal.ToString());
                        entry["fractalModes"] = fractalModes;
                        entry["defaultFractal"] = node.fractal.ToString();
                    }
                }
                if (kind is ShaderGraphNodeKind.Buffer or ShaderGraphNodeKind.Fractal)
                {
                    entry["dynamicInputs"] = true;
                    entry["outputTypeFromInput"] = "In";
                    entry["sampledInputs"] = new JArray("In");
                    if (kind == ShaderGraphNodeKind.Fractal)
                    {
                        var modes = new JArray();
                        foreach (var mode in ShaderGraphNodes.FractalModeValues) modes.Add(mode.ToString());
                        entry["fractalModes"] = modes;
                        entry["defaultFractal"] = node.fractal.ToString();
                    }
                }
                var settings = ShaderNodeSettings(node);
                if (kind == ShaderGraphNodeKind.Scatter)
                {
                    entry["dynamicInputs"] = true;
                    entry["dynamicOutputs"] = true;
                    entry["outputTypeFromInput"] = "Source";
                    entry["sampledInputs"] = ShaderNodeSampledInputs(node);
                    entry["centerSampling"] = new JArray("Density", "Mask", "ScaleMap", "RotationMap");
                }
                if (kind == ShaderGraphNodeKind.CausticsNoise)
                {
                    entry["dynamicInputs"] = true;
                    entry["defaultOutput"] = "Value";
                }
                if (kind == ShaderGraphNodeKind.Caustics)
                {
                    entry["sampledInputs"] = new JArray("Height");
                    entry["buffered"] = true;
                }
                if (ShaderGraphNodes.ImageFilterMode(kind) is >= 0 and <= 2)
                {
                    entry["sampledInputs"] = new JArray("Height");
                    if (kind != ShaderGraphNodeKind.HeightShadow) entry["radiusUnits"] = "DocumentPixels";
                }
                if (kind == ShaderGraphNodeKind.FlowWarp) entry["sampledInputs"] = new JArray("Flow");
                if (kind == ShaderGraphNodeKind.NormalCurvature) entry["sampledInputs"] = new JArray("Normal");
                if (kind is ShaderGraphNodeKind.Median or ShaderGraphNodeKind.Kuwahara) entry["sampledInputs"] = new JArray("In");
                if (settings.Count > 0) entry["settings"] = settings;
                if (ShaderGraphNodes.UsesHeightTexture(kind)) entry["dynamicInputs"] = true;
                if (kind is ShaderGraphNodeKind.ParallaxMapping or ShaderGraphNodeKind.ParallaxOcclusionMapping)
                {
                    entry["dynamicInputs"] = true;
                    entry["amplitudeUnits"] = "UV";
                    entry["searchAliases"] = kind == ShaderGraphNodeKind.ParallaxMapping
                        ? new JArray("Bump Offset", "Parallax Offset") : new JArray("POM");
                }
                if (ShaderGraphNodes.IsFilter(kind))
                {
                    entry["sampledInputs"] = ShaderNodeSampledInputs(node);
                    if (kind != ShaderGraphNodeKind.RGBShift) entry["outputTypeFromInput"] = "In";
                    else entry["amountUnits"] = "DocumentPixels";
                    if (kind is not ShaderGraphNodeKind.RadialBlur and not ShaderGraphNodeKind.SlopeBlur and
                        not ShaderGraphNodeKind.Buffer and not ShaderGraphNodeKind.Fractal and not ShaderGraphNodeKind.RGBShift and
                        not ShaderGraphNodeKind.Supersample and not ShaderGraphNodeKind.MakeTileable) entry["radiusUnits"] = "DocumentPixels";
                }
                string description = ShaderNodeDescription(kind);
                if (description != null) entry["description"] = description;
                if (kind == ShaderGraphNodeKind.Constant)
                {
                    entry["constants"] = new JArray(System.Enum.GetNames(typeof(ShaderGraphConstant)));
                    entry["description"] = "constant selects PI (default), TAU, PHI, E or SQRT2. Out is Float. Use Value for editable local values.";
                }
                if (kind == ShaderGraphNodeKind.Swizzle)
                    entry["description"] = "swizzle is a 1–4 character mask (default xxxx): xyzw/rgba, case-insensitive, with _ inserting zero. Input is numeric/Color; its width is max(source width, highest mask component). Connected scalars repeat; shorter vectors gain zeros. Unconnected fields expose inferred components directly. Out is Float through Float4 by mask length. Example: float2 input with _x_y outputs float4(0,x,0,y).";
                if (kind == ShaderGraphNodeKind.Expression) entry["dynamicInputs"] = true;
                if (kind == ShaderGraphNodeKind.Parameter) entry["dynamicOutputs"] = true;
                if (kind is ShaderGraphNodeKind.Append or ShaderGraphNodeKind.Switch)
                {
                    entry["dynamicInputs"] = true;
                    entry["dynamicOutputs"] = true;
                    if (kind == ShaderGraphNodeKind.Append)
                    {
                        entry["maxOutputComponents"] = 4;
                        entry["maxAComponents"] = 3;
                    }
                    else entry["caseInputsField"] = "switchCases";
                }
                if (kind is ShaderGraphNodeKind.All or ShaderGraphNodeKind.Any) entry["dynamicInputs"] = true;
                if (kind == ShaderGraphNodeKind.Multiply)
                {
                    entry["dynamicOutputs"] = true;
                    entry["matrixTypes"] = new JArray("Matrix2x2", "Matrix3x3", "Matrix4x4");
                }
                if (ShaderGraphNodes.HasDynamicMatrixInput(kind))
                {
                    entry["dynamicInputs"] = true;
                    if (kind != ShaderGraphNodeKind.MatrixDeterminant) entry["dynamicOutputs"] = true;
                }
                if (kind == ShaderGraphNodeKind.Value)
                {
                    entry["dynamicOutputs"] = true;
                    entry["matrixTypes"] = new JArray("Matrix2x2", "Matrix3x3", "Matrix4x4");
                }
                if (kind == ShaderGraphNodeKind.Reroute)
                {
                    entry["dynamicInputs"] = true;
                    entry["dynamicOutputs"] = true;
                    entry["outputTypeFromInput"] = "In";
                }
                if (ShaderGraphLoops.IsEndpoint(kind))
                {
                    entry["partnerField"] = "loopPartnerId";
                    entry["partnerKind"] = kind == ShaderGraphNodeKind.ForBegin ? "ForEnd" : "ForBegin";
                    entry["outputTypeFromInput"] = kind == ShaderGraphNodeKind.ForBegin ? "Initial" : "partner.Initial";
                    if (kind == ShaderGraphNodeKind.ForEnd) entry["sampledInputs"] = new JArray("Value");
                }
                if (kind == ShaderGraphNodeKind.CustomFunction)
                {
                    entry["dynamicInputs"] = true;
                    entry["dynamicOutputs"] = true;
                    entry["description"] = "Ports shown are the default inline template. Node functionId references graph.functions; actual ports and defaults come from that definition. draft and optional draftPorts retain uncompiled edits; hasDraftPorts preserves an intentionally empty port draft. File-backed functions reference sourceId in graph.sources, including all non-engine include bindings and plain source code. Complete graph replacement preserves embedded backups without reading sourcePath.";
                }
                result.Add(entry);
            }
            return result;
        }

        private static JObject ShaderNodeOutputTypes(ShaderGraphNode node)
        {
            var result = new JObject();
            foreach (string port in ShaderGraphNodes.Outputs(node))
            {
                if (ShaderGraphNodes.TryImageFilterOutputType(node, out var type) || ShaderGraphNodes.TryUtilityOutputType(node.kind, out type) ||
                    ShaderGraphNodes.TryNoiseOutputType(node, port, out type) ||
                    ShaderGraphNodes.TrySurfaceOutputType(node, port, out type) ||
                    ShaderGraphNodes.TryProceduralOutputType(node.kind, out type) ||
                    ShaderGraphNodes.TryColorOutputType(node.kind, out type) ||
                    ShaderGraphNodes.TryExtendedOutputType(node.kind, out type, port))
                    result[port] = ShaderGraphNodes.NumericType(type).ToString();
                else if (node.kind == ShaderGraphNodeKind.MatrixConstruction)
                    result[port] = ShaderGraphNodes.MatrixConstructionType(port).ToString();
                else if (node.kind == ShaderGraphNodeKind.MatrixDeterminant)
                    result[port] = "Float";
                else if (node.kind is ShaderGraphNodeKind.TransformUV or ShaderGraphNodeKind.ApplyTransform2D or
                    ShaderGraphNodeKind.ParallaxMapping or ShaderGraphNodeKind.ParallaxOcclusionMapping)
                    result[port] = ShaderGraphValueType.Float2.ToString();
                else if (node.kind == ShaderGraphNodeKind.Expression) result[port] = node.valueType.ToString();
                else if (node.kind == ShaderGraphNodeKind.Scatter && node.option == 2)
                    result[port] = ShaderGraphValueType.Float4.ToString();
                else if (node.kind == ShaderGraphNodeKind.ScatterInstance)
                    result[port] = (port is "UV" or "Center" ? ShaderGraphValueType.Float2 : ShaderGraphValueType.Float).ToString();
                else if (node.kind == ShaderGraphNodeKind.ForBegin && port == "Index") result[port] = "Float";
                else if (node.kind is ShaderGraphNodeKind.Caustics or ShaderGraphNodeKind.CausticsNoise)
                    result[port] = (port == "Color" ? ShaderGraphValueType.Float4 : ShaderGraphValueType.Float).ToString();
                else if (node.kind is ShaderGraphNodeKind.SampleTexture or ShaderGraphNodeKind.SampleInput or ShaderGraphNodeKind.SampleGradient)
                    result[port] = (port == "Out" ? ShaderGraphValueType.Float4 : ShaderGraphValueType.Float).ToString();
                else if (node.kind == ShaderGraphNodeKind.CanvasPosition || node.kind == ShaderGraphNodeKind.CanvasData)
                    result[port] = (port == "PixelScale" ? ShaderGraphValueType.Float : ShaderGraphValueType.Float2).ToString();
            }
            return result;
        }

        private static JArray ShaderNodeSampledInputs(ShaderGraphNode node) => node.kind switch
        {
            ShaderGraphNodeKind.Scatter => new JArray("Source", "Density", "Mask", "ScaleMap", "RotationMap"),
            ShaderGraphNodeKind.SlopeBlur => new JArray("In", "Slope"),
            ShaderGraphNodeKind.BilateralBlur when node.option == 1 => new JArray("In", "Guide"),
            _ => new JArray("In")
        };

        private static JArray ShaderNodeInputs(ShaderGraphNode node)
        {
            var inputs = new JArray();
            foreach (var port in ShaderGraphNodes.Inputs(node))
            {
                var input = new JObject
                {
                    ["port"] = port.name,
                    ["type"] = port.type?.ToString(),
                    ["passthrough"] = node.kind == ShaderGraphNodeKind.Reroute,
                    ["defaultUV"] = port.uvDefault,
                    ["value"] = new JArray(port.value.x, port.value.y, port.value.z, port.value.w)
                };
                if (ShaderGraphNodes.IsOptionalCurve(node, port.name)) input["optional"] = true;
                if (ShaderGraphNodes.HasDynamicMatrixInput(node.kind))
                {
                    input.Remove("value");
                    input["acceptedTypes"] = new JArray("Matrix2x2", "Matrix3x3", "Matrix4x4");
                    input["defaultExpression"] = "Identity";
                }
                if (port.uvDefault && port.type == ShaderGraphValueType.Float)
                {
                    input["defaultExpression"] = "UV.x";
                    input["sources"] = new JArray("UVX", "UVY", "Value");
                }
                if (port.uvDefault && port.type == ShaderGraphValueType.Float3)
                {
                    input["defaultExpression"] = "float3(UV, value.z)";
                    input["sources"] = new JArray("UVX", "UVY", "Value");
                    input["editableUVComponents"] = new JArray("z");
                }
                if (port.type.HasValue && ShaderGraphInput.IsResource(port.type.Value))
                {
                    input.Remove("value");
                    input["valueField"] = "resource";
                    input["resourceType"] = port.type == ShaderGraphValueType.Texture ? "Texture2D" : port.type.Value.ToString();
                }
                if (port.canvasSizeDefault)
                {
                    input["default"] = "CanvasSize";
                    input["defaultExpression"] = "CanvasSize";
                    input.Remove("value");
                }
                if (ShaderGraphNodes.IsNoise(node.kind) || node.kind == ShaderGraphNodeKind.CausticsNoise)
                {
                    string reason = ShaderGraphNodes.NoiseInputInactiveReason(node, port.name);
                    input["active"] = reason == null;
                    if (reason != null) input["inactiveReason"] = reason;
                    if (port.name == "Position")
                        input["defaultExpression"] = ShaderGraphNodes.IsGrainNoise(node.kind) ? "UV" :
                            ShaderGraphNodes.NoiseDimensions(node) == 1 ? "UV.x" :
                            ShaderGraphNodes.NoiseDimensions(node) == 2 ? "UV" : "float3(UV, value.z)";
                }
                else if (node.kind is ShaderGraphNodeKind.Fractal or ShaderGraphNodeKind.Scatter or ShaderGraphNodeKind.RGBShift)
                {
                    string reason = ShaderGraphNodes.InputInactiveReason(node, port.name);
                    input["active"] = reason == null;
                    if (reason != null) input["inactiveReason"] = reason;
                    if (node.kind == ShaderGraphNodeKind.Scatter && port.name == "Period")
                        input["activeWhen"] = new JObject { ["scatterPeriodic"] = true };
                }
                inputs.Add(input);
            }
            return inputs;
        }

        private static JObject ShaderNodeSettings(ShaderGraphNode node)
        {
            var settings = new JObject();
            bool parallax = ShaderGraphNodes.IsParallax(node.kind);
            if (node.kind == ShaderGraphNodeKind.PaletteQuantize)
                settings["paletteKeys"] = new JObject { ["type"] = "boolean", ["default"] = false,
                    ["description"] = "Palette: false=Samples, true=Gradient Keys. Keys uses every authored color stop and ignores Colors, interpolation and alpha stops." };
            if (node.kind == ShaderGraphNodeKind.Sharpen)
                settings["adaptiveSharpen"] = new JObject { ["type"] = "boolean", ["default"] = node.adaptiveSharpen };
            if (node.kind == ShaderGraphNodeKind.Kuwahara)
                settings["kuwaharaHighPrecision"] = new JObject { ["type"] = "boolean", ["default"] = node.kuwaharaHighPrecision,
                    ["description"] = "Classic-only High Precision forces direct dense-quadrant evaluation; Anisotropic retains but ignores this setting. False permits summed-area tables for max(0,floor(Radius))>5 with unconnected Radius and standard current-pixel UV (unconnected, not Value) outside sampled/For/Fractal/Scatter scopes. Both settings use the same document-resolution source with a real halo; output interpolates between pixel centers and clamps outside the canvas. Other contexts already evaluate the input branch directly. Stored on the node, not an input port." };
            if (node.kind == ShaderGraphNodeKind.Switch)
            {
                settings["floorPredicate"] = new JObject { ["type"] = "boolean", ["default"] = node.floorPredicate };
                var cases = new JArray();
                foreach (var item in ShaderGraphNodes.SwitchCases(node))
                    cases.Add(new JObject { ["port"] = item.port, ["comparison"] = item.comparison.ToString(), ["threshold"] = item.threshold });
                settings["switchCases"] = new JObject
                {
                    ["type"] = "array", ["nullable"] = true, ["default"] = cases, ["maxItems"] = 26,
                    ["description"] = "Ordered cases; first match wins. Null/omitted uses the default cases; an empty array always uses Fallback. Each unique stable port identifies a dynamic value input. Enum parameters do not generate cases.",
                    ["items"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["port"] = new JObject { ["type"] = "string", ["description"] = "Unique stable identifier, excluding Predicate and Fallback. Retain it in inputs and edges when editing conditions." },
                            ["comparison"] = ShaderNodeEnumSetting(ShaderGraphComparison.Equal),
                            ["threshold"] = new JObject { ["type"] = "number", ["default"] = 0, ["description"] = "Finite scalar comparison value." }
                        }
                    }
                };
            }
            if (node.kind == ShaderGraphNodeKind.Scatter)
                settings["scatterPeriodic"] = new JObject { ["type"] = "boolean", ["default"] = node.scatterPeriodic };
            if (ShaderGraphNodes.HasNoisePeriod(node.kind))
                settings["noisePeriodic"] = new JObject { ["type"] = "boolean", ["default"] = node.noisePeriodic };
            if (node.kind is ShaderGraphNodeKind.CausticsNoise or ShaderGraphNodeKind.Caustics)
                settings["causticsWhiteBalance"] = new JObject { ["type"] = "boolean", ["default"] = node.causticsWhiteBalance };
            if (node.kind == ShaderGraphNodeKind.CausticsNoise)
                settings["noiseFiltering"] = new JObject { ["type"] = "boolean", ["default"] = node.noiseFiltering };
            if (node.kind == ShaderGraphNodeKind.Expression)
            {
                settings["expression"] = new JObject { ["type"] = "string", ["default"] = node.expression, ["maxLength"] = ShaderGraphExpression.MaximumLength };
                settings["valueType"] = new JObject { ["type"] = "enum", ["default"] = node.valueType.ToString(), ["values"] = new JArray("Float", "Float2", "Float3", "Float4") };
            }
            if (ShaderGraphNodes.IsNoise(node.kind))
            {
                bool grain = ShaderGraphNodes.IsGrainNoise(node.kind);
                var dimensions = ShaderNodeEnumSetting(node.noiseDimensions);
                var originalDimensions = node.noiseDimensions;
                var dimensionValues = new JArray();
                foreach (JObject value in (JArray)dimensions["values"])
                {
                    var dimension = (ShaderGraphNoiseDimensions)System.Enum.Parse(typeof(ShaderGraphNoiseDimensions), (string)value["value"]);
                    if (grain && dimension == ShaderGraphNoiseDimensions.ThreeD) continue;
                    if (node.kind == ShaderGraphNodeKind.TriangleVoronoiNoise && dimension != ShaderGraphNoiseDimensions.TwoD) continue;
                    node.noiseDimensions = dimension;
                    value["dimensions"] = ShaderGraphNodes.NoiseDimensions(node);
                    value["inputs"] = ShaderNodeInputs(node);
                        value["metricApplicable"] = node.noiseOutput != ShaderGraphNoiseOutput.DistanceToEdge;
                    value["outputs"] = new JArray(ShaderGraphNodes.Outputs(node));
                    value["outputTypes"] = ShaderNodeOutputTypes(node);
                    dimensionValues.Add(value);
                }
                node.noiseDimensions = originalDimensions;
                dimensions["values"] = dimensionValues;
                settings["noiseDimensions"] = dimensions;
                if (grain)
                    settings["noiseInverted"] = new JObject { ["type"] = "boolean", ["default"] = node.noiseInverted };
                else
                {
                    settings["noiseFiltering"] = new JObject { ["type"] = "boolean", ["default"] = node.noiseFiltering };
                    if (ShaderGraphNodes.IsWarpNoise(node.kind)) settings["fractal"] = ShaderNodeFractalSetting(node);
                }
                if (node.kind is ShaderGraphNodeKind.CellularNoise or ShaderGraphNodeKind.TriangleVoronoiNoise)
                    settings["noiseDistribution"] = ShaderNodeEnumSetting(node.noiseDistribution);
                if (node.kind == ShaderGraphNodeKind.CellularNoise)
                {
                    settings["noiseMetric"] = ShaderNodeEnumSetting(node.noiseMetric);
                    settings["noiseMetric"]["inactiveWhen"] = "noiseOutput == DistanceToEdge (Euclidean)";
                    var output = ShaderNodeEnumSetting(node.noiseOutput);
                    var originalOutput = node.noiseOutput;
                    foreach (JObject value in (JArray)output["values"])
                    {
                        node.noiseOutput = (ShaderGraphNoiseOutput)System.Enum.Parse(typeof(ShaderGraphNoiseOutput), (string)value["value"]);
                        value["inputs"] = ShaderNodeInputs(node);
                        value["metricApplicable"] = node.noiseOutput != ShaderGraphNoiseOutput.DistanceToEdge;
                    }
                    node.noiseOutput = originalOutput;
                    settings["noiseOutput"] = output;
                }
            }
            if (node.kind == ShaderGraphNodeKind.Fractal)
            {
                settings["fractal"] = ShaderNodeFractalSetting(node);
                settings["noiseFiltering"] = new JObject { ["type"] = "boolean", ["default"] = node.noiseFiltering };
            }
            if (ShaderGraphNodes.IsBuffered(node.kind))
            {
                settings["bufferResolution"] = ShaderNodeEnumSetting(node.bufferResolution);
                settings["bufferSize"] = new JObject
                {
                    ["type"] = "array", ["items"] = new JObject { ["type"] = "integer", ["minimum"] = 1 },
                    ["minItems"] = 2, ["maxItems"] = 2,
                    ["default"] = new JArray(node.bufferSize.x, node.bufferSize.y),
                    ["activeWhen"] = new JObject { ["bufferResolution"] = "Custom" }
                };
            }
            if (node.kind is ShaderGraphNodeKind.SampleInput or ShaderGraphNodeKind.SampleTexture or ShaderGraphNodeKind.Buffer)
            {
                settings["wrap"] = ShaderNodeEnumSetting(node.wrap);
                settings["filter"] = ShaderNodeEnumSetting(node.filter);
            }
            if (node.kind == ShaderGraphNodeKind.Caustics) settings["wrap"] = ShaderNodeEnumSetting(node.wrap);
            if (node.kind == ShaderGraphNodeKind.ConvertColor)
            {
                settings["inputColorSpace"] = ShaderNodeEnumSetting(node.inputColorSpace, ShaderGraphNodes.ColorSpaceNames);
                settings["outputColorSpace"] = ShaderNodeEnumSetting(node.outputColorSpace, ShaderGraphNodes.ColorSpaceNames);
            }
            if (ShaderGraphNodes.UsesChannel(node.kind))
            {
                var channel = ShaderNodeEnumSetting(node.channel, ShaderGraphNodes.ChannelNames);
                channel["inputs"] = new JArray(node.kind == ShaderGraphNodeKind.GradientMap ? new[] { "Color" } : ShaderGraphNodes.ChannelInputs(node.kind));
                channel["description"] = node.kind == ShaderGraphNodeKind.GradientMap
                    ? "Select Luminance (Linear RGB), Red, Green, Blue or Alpha from Color as the gradient coordinate. Alpha Mode remains independent."
                    : "Select Luminance (Linear RGB), Red, Green, Blue or Alpha before converting height/mask inputs to Float. Scalar inputs pass through unchanged; Float2/3 use zero for missing RGB and one for missing alpha. Also applies to Height Source=Value.";
                settings["channel"] = channel;
            }
            if (node.kind == ShaderGraphNodeKind.RangeMask)
                settings["rangeLuminance"] = new JObject { ["type"] = "boolean", ["default"] = false,
                    ["description"] = "Input: false=Components (dynamic numeric Out), true=Luminance (Float Out, scalar input unchanged). Independent of Falloff." };
            if (ShaderGraphNodes.UsesHeightTexture(node.kind))
            {
                var heightSource = ShaderNodeEnumSetting(node.heightSource);
                var original = node.heightSource;
                foreach (JObject value in (JArray)heightSource["values"])
                {
                    node.heightSource = (ShaderGraphHeightSource)System.Enum.Parse(typeof(ShaderGraphHeightSource), (string)value["value"]);
                    value["inputs"] = ShaderNodeInputs(node);
                    if (parallax)
                        value["sampledInputs"] = node.heightSource == ShaderGraphHeightSource.Value
                            ? new JArray("Height") : new JArray();
                    else value["radiusUnits"] = node.heightSource == ShaderGraphHeightSource.Texture ? "SourceTexels" : "DocumentPixels";
                }
                node.heightSource = original;
                settings["heightSource"] = heightSource;
                if (!parallax) settings["derivative"] = ShaderNodeEnumSetting(node.derivative, ShaderGraphNodes.DerivativeNames);
                settings["wrap"] = ShaderNodeEnumSetting(node.wrap);
                settings["filter"] = ShaderNodeEnumSetting(node.filter);
                if (parallax)
                    foreach (string field in new[] { "channel", "wrap", "filter" })
                        settings[field]["activeWhen"] = new JObject { ["heightSource"] = "Texture" };
            }
            return settings;
        }

        private static ShaderGraphFractal[] ShaderNodeNoiseFractals(ShaderGraphNodeKind kind) => ShaderGraphNodes.IsWarpNoise(kind)
            ? new[] { ShaderGraphFractal.None, ShaderGraphFractal.Progressive, ShaderGraphFractal.Independent }
            : ShaderGraphNodes.FractalModeValues;

        private static JObject ShaderNodeFractalSetting(ShaderGraphNode node)
        {
            var values = new JArray();
            var original = node.fractal;
            foreach (var fractal in ShaderNodeNoiseFractals(node.kind))
            {
                node.fractal = fractal;
                values.Add(new JObject { ["value"] = fractal.ToString(), ["name"] = fractal.ToString(), ["inputs"] = ShaderNodeInputs(node) });
            }
            node.fractal = original;
            return new JObject { ["default"] = original.ToString(), ["values"] = values };
        }

        private static JObject ShaderNodeEnumSetting<T>(T defaultValue, string[] labels = null) where T : struct, System.Enum
        {
            var names = System.Enum.GetNames(typeof(T));
            var values = new JArray();
            for (int i = 0; i < names.Length; i++)
                values.Add(new JObject { ["value"] = names[i], ["name"] = labels == null ? names[i] : labels[i] });
            return new JObject { ["default"] = defaultValue.ToString(), ["values"] = values };
        }

        private static string ShaderNodeDescription(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.FlowWarp => "Float2 Out is UV integrated along the unnormalized Float2 Flow branch, default (0,0); UV defaults to the current pixel. Signed Strength=16 uses document pixels, and Steps=8 rounds/clamps to 1..32. Each midpoint step evaluates Flow at the current position and the half-step position, then advances by the midpoint vector times Strength/Steps/CanvasSize. Two Flow evaluations per step, at most 64. Vector magnitude affects travel; negative Strength reverses the path. Zero Flow or Strength preserves UV. Connect Out to downstream sampling; it is a UV position, not a flow direction. Native HLSL and SVM with sampled-branch work/nesting fallback; no implicit Buffer.",
            ShaderGraphNodeKind.Median => "Dynamic numeric/Color In, current-pixel UV and Radius=1 produce Out with the same components. r=max(0,floor(Radius)) document pixels covers every sample of the (2*r+1)^2 square; r=0 is identity. Signed/HDR values and RGBA are independent, without alpha weighting or clamping. Root evaluation with default UV prepares float32 tiles for unconnected r>2 or a connected proven pixel-uniform Radius (Value/Parameter/Constant, Canvas Data, Time/Instance Index and supported pure math). Connected uniform values can change without recompilation; a cached 1x1 HLSL scalar pass preserves shader arithmetic and is shared by HLSL/SVM. Pixel-dependent, opaque Expression/Custom Function, explicit UV and sampled/iterative contexts keep exact direct HLSL. Prepared output uses bilinear interpolation on the document grid and Clamp outside the canvas. No extra controls.",
            ShaderGraphNodeKind.Kuwahara => "Dynamic numeric/Color In (default Float 0), UV=current-pixel UV and Radius=2 document pixels produce Out with the same components. Mode option 0=Classic (default), 1=Anisotropic. Classic uses r=max(0,floor(Radius)): four overlapping dense quadrants each contain (r+1)^2 points. Return the mean of the quadrant with lowest mean variance across active components, retaining the first on ties. Radius zero preserves the input. All components, including alpha, are processed directly without implicit alpha weighting. Boolean node setting kuwaharaHighPrecision defaults false and applies only to Classic; Anisotropic retains but ignores it. For r>5 with unconnected Radius and standard current-pixel UV (unconnected, not Value) outside sampled/For/Fractal/Scatter scopes, both settings use a source rasterized at document resolution with a real r-pixel halo on every side. Prepared output uses bilinear interpolation between document-pixel centers and Clamp outside the canvas; this does not preserve arbitrary procedural evaluation off-grid or beyond the canvas. False uses summed-area tables; true directly samples that same grid, avoiding summed-area accumulation error at potentially higher cost. Small radii, connected Radius/UV, literal Value UV and sampled/iterative contexts use direct input-branch evaluation without errors; High Precision has no additional effect there. Floating-point accumulation can differ between paths. Anisotropic uses eight weighted elliptical sectors oriented by a smoothed structure tensor. Its Radius remains a nonnegative Float in document pixels, without integer rounding. Float ports Sharpness=1 (s=clamp(value,0,1), internal sharpness 16*s*s), Uniformity=4 (u=max(0,floor(value)) document pixels, Gaussian sigma u/3) and Eccentricity=1 (e=clamp(value,0,2), stretch 1+anisotropy*max(0.01,e)) are active only in Anisotropic and use generic inputs/edges. All active components including alpha are filtered directly. Ordinary root evaluation with unconnected Radius/Sharpness/Uniformity/Eccentricity and standard UV (unconnected, not Value) uses a tiled prepared image at document resolution with a source halo. Tensor preparation uses compute and separable Gaussian smoothing; without compute, the prepared pixel shader calculates the tensor directly. Missing CopyTexture support, connected controls/UV, Value UV or sampled/iterative evaluation use native procedural HLSL. SVM consumes the legal prepared boundary and continues with existing instructions; no new VM opcode. High Precision does not affect Anisotropic. In, UV and Radius remain shared; no Samples or Density port.",
            ShaderGraphNodeKind.NormalCurvature => "Signed XYZ Float3 Normal defaults to (0,0,1). UV defaults to the current pixel; Radius=1 document pixel and Strength=1 clamp below at zero. Normalize four signed Normal branch samples at left/right/down/up. Curvature=Strength*((right.x-left.x)+(up.y-down.y))/(2*Radius), with a guarded denominator and zero at Radius=0. Float Convex=saturate(Curvature), Concave=saturate(-Curvature); Curvature stays signed and unclamped. No Out port. Constant normals have zero response. Native HLSL and SVM with sampled-branch work/nesting fallback; no implicit Buffer.",
            ShaderGraphNodeKind.Truchet => "Float Out is a seeded tile-pattern mask; Float Distance is the signed field before smoothing (negative inside, zero at the boundary, positive outside). Inputs: UV=current-pixel UV, Float2 Scale=(8,8), Width=0.1 in cell units, Feather=0 in cell units, Seed=1337. Positive Feather widens the centered transition; negative values act as zero. Feather does not affect Distance, which skips antialiasing evaluations when used alone. Bottom Shape uses option 0=Arcs (default), 1=Lines. Cell coordinates floor(UV*Scale) and Seed select motif orientation by hash; there is no explicit period or unit-UV repetition guarantee. Width<=0 returns zero; positive Width uses shared distance-mask antialiasing from the pixel footprint; transformed UV and replayed branches are supported. Generates the mask directly without a sampled source branch. Native HLSL and SVM.",
            ShaderGraphNodeKind.HeightCurvature => "Sample Float Height=0 at current-pixel UV and four axial neighbors separated by Radius=2 document pixels. Curvature=(center-averageOfFourNeighbors)*Strength, with Strength=8. Float outputs Convex=saturate(Curvature), Concave=saturate(-Curvature), Curvature signed and unclamped; no Out port. Radius and Strength clamp below at zero; either zero gives zero outputs. Height is a numeric branch, not a Texture resource; sample a texture channel first. Five Height evaluations, with fixed source coordinates staying fixed. Native HLSL and SVM, with HLSL fallback beyond sampled-branch work/nesting limits; no implicit Buffer.",
            ShaderGraphNodeKind.HeightToAO => "Float Out is approximate local ambient visibility, 1 unoccluded and 0 occluded. Sample Float Height=0 at UV=current-pixel UV. Radius=16 document pixels, HeightScale=16 pixels per unit height, Strength=1, Bias=0.05 height pixels; these controls clamp below at zero. Directions=8 and Steps=4 round/clamp to 1..16. A center and four one-pixel neighbors estimate the local tangent; Directions*Steps horizon samples measure excess occlusion relative to it, so flat ramps stay lit. At most 261 Height evaluations; zero Radius or Strength gives 1. Numeric Height branch only; texture sources must first be sampled. Native HLSL and SVM with sampled-branch budget fallback; no implicit Buffer. Narrow blockers can be missed.",
            ShaderGraphNodeKind.HeightShadow => "Float Out is approximate height-field shadow visibility, 1 lit and 0 shadowed. Float Height=0 is reevaluated at sample UV; UV defaults to current-pixel UV. Float3 LightDirection=(0.5,0.5,1) points toward the light in the local UV/height frame. HeightScale=16 pixels per unit height, Distance=64 document pixels, Bias=0.05 height pixels and angular Softness=0.1 clamp below at zero. Steps=32 rounds/clamps to 1..64 samples beyond the center, at most 65 Height evaluations. Ray marching compares sampled heights with the light ray; Softness is angular, not a blur radius. Narrow blockers can fall between samples. Numeric Height branch only, no depth/UV displacement or implicit Buffer. Native HLSL and SVM with sampled-branch work/nesting fallback.",
            ShaderGraphNodeKind.PaletteQuantize => "Color Out replaces Color.rgb with the nearest palette candidate and preserves Color.a. Bottom Distance uses option 0=Linear (squared linear-RGB distance), 1=Perceptual (default, squared Oklab distance), shared with ColorMask/ReplaceColor. Both return the chosen candidate's original RGB, not Oklab coordinates. Color defaults opaque white; Palette is a Gradient resource. Colors=8 rounds/clamps to 1..64 candidates sampled at t=i/max(count-1,1), including both endpoints for count>1 and only t=0 for count=1. Ties retain the first candidate. Palette alpha is ignored in both metrics. paletteKeys=false (default Palette: Samples) uses Colors uniform samples. paletteKeys=true (Gradient Keys) uses all authored color keys in time order, converted to Linear, without interpolation or alpha stops. Custom gradients use combined RGB marker colors. Colors becomes inactive with its connection retained. No spatial sampling or UV input. Native HLSL and SVM; surrounding sampled branches retain the work/nesting budget and HLSL fallback.",
            ShaderGraphNodeKind.Supersample => "Reevaluate dynamic numeric/Color In at a centered subpixel grid around UV=current-pixel UV. Out retains In's type; unconnected In is Float 0. Bottom Quality uses option 0=2x2 (default), 1=3x3, 2=4x4, for 4/9/16 evaluations in the captured render-pixel UV footprint. Quality is a node setting, not an input port; there is no Radius. Numeric components average independently. Color averages premultiplied RGB and alpha, returns straight RGBA and zero RGB at zero alpha. Fixed upstream coordinates stay fixed. Native HLSL and SVM with sampled-branch work/nesting fallback; no implicit Buffer.",
            ShaderGraphNodeKind.MakeTileable => "Reevaluate dynamic numeric/Color In four times; Float2 UV defaults to current-pixel UV, and Out retains In's type. Unconnected In is Float 0. With q=frac(UV), half-tile samples are q, frac(q+(0.5,0)), frac(q+(0,0.5)), frac(q+(0.5,0.5)), all in [0,1). Use component-wise a=w(2*min(q,1-q)), where quintic w(t)=6*t^5-15*t^4+10*t^3; weights in sample order are a.x*a.y, (1-a.x)*a.y, a.x*(1-a.y), (1-a.x)*(1-a.y). Unshifted weights vanish at tile edges; shifted weights vanish at midpoint wrap cuts. Works with Clamp and Repeat sources. Float BlendWidth=0.5 preserves full-tile blending. Positive width clamps to 0.5 and replaces a with w(saturate(min(q,1-q)/BlendWidth)); smaller widths preserve the center. Nonpositive width simply repeats the source without seam correction. UV repeats outside 0..1; no Radius input. Fixed source coordinates stay fixed, and shifted recognizable shapes may ghost. Numeric components blend independently; Color blends premultiplied RGB/alpha and returns straight RGBA, zero RGB at zero alpha. Native HLSL and SVM with sampled-branch budget fallback; no implicit Buffer.",
            ShaderGraphNodeKind.Append => "Concatenate leading numeric components of A then B. A contributes at most 3; B contributes up to the remaining places in a 4-component result. Both default to scalar zero. Out is Float2-Float4 by the total retained width; Color is raw RGBA, without conversion. Float2 A plus Float3 B yields float4(A.xy,B.xy).",
            ShaderGraphNodeKind.Switch => "Scalar Predicate=0 selects the first matching ordered switchCases entry, otherwise Fallback. floorPredicate=true compares floor(Predicate); disable for fractional thresholds. Cases and Fallback default to scalar zero and determine the widest numeric Out (Float-Float4; Color is Float4). Null/omitted cases are A Equal 0 and B Equal 1; an empty list uses Fallback. At most 26 cases, with unique stable port identifiers, comparison enum and finite threshold. Inputs listed here describe default cases; use each stored case.port for value inputs/edges. Enum parameters supply scalar values only, without automatic cases or labels.",
            ShaderGraphNodeKind.FadeTransition => "Float NoiseValue=0.5, FadeValue=0 and FadeContrast=1 produce Float Out = saturate(FadeValue*(FadeContrast+1)+(NoiseValue-1)*FadeContrast). NoiseValue is an input mask, not generated noise. For NoiseValue in 0..1 and nonnegative FadeContrast, FadeValue 0/1 gives 0/1; higher contrast narrows the ramp, and zero contrast gives saturate(FadeValue). No branch resampling.",
            ShaderGraphNodeKind.ReplaceColor => "Color Out replaces RGB near From.rgb with To.rgb, retaining Color.a; Float Mask exposes the same selection as ColorMask. Distance option 0=Linear, 1=Perceptual (default, Oklab), 2=Chroma (Oklab a/b only). From defaults black, To and Color white; Range=0.1 is the fully selected radius, Fuzziness=0.05 the outer linear fade. Negative Range/Fuzziness behave as zero; there is no upper bound. Zero Fuzziness selects distance<=Range exactly. Out.rgb=lerp(Color.rgb,To.rgb,Mask). From/To alpha is ignored; signed/HDR RGB is not clamped.",
            ShaderGraphNodeKind.Contrast => "Native Color Out = float4((Color.rgb-midpoint)*Contrast+midpoint,Color.a), with midpoint=pow(0.5,2.2), Color white and Contrast=1. Retains alpha and signed/HDR RGB. Distinct from the cataloged HLSL Contrast function with Amount/Result and midpoint 0.5.",
            ShaderGraphNodeKind.NormalReconstructZ => "Float2 In=(0,0) supplies signed XY. Float3 Out = normalize(float3(In,sqrt(1-saturate(dot(In,In))))), with nonnegative Z. XY outside the unit disk normalizes onto Z=0. No packed-RG decoding.",
            ShaderGraphNodeKind.NormalUnpack => "Float4 In=(0.5,0.5,1,1) to signed Float3 Out. option 0=Tangent calls UnityCG UnpackNormal for imported platform normal packing, including alpha where needed. option 1=Object returns In.rgb*2-1 without normalization. No coordinate-space transform.",
            ShaderGraphNodeKind.RadialShear => "Float2 UV defaults to current-pixel UV; Center=(0.5,0.5), Strength=(10,10), Offset=(0,0). With d=UV-Center, Float2 Out=UV+float2(d.y,-d.x)*dot(d,d)*Strength+Offset. No radius cutoff, normalization, sampling or clamp. Strength zero retains Offset; identity also needs Offset zero.",
            ShaderGraphNodeKind.All => "Boolean Out is true when every numeric In component is nonzero, including negative values. Dynamic Float-Float4/Color input defaults to scalar zero; Color uses four raw components.",
            ShaderGraphNodeKind.Any => "Boolean Out is true when any numeric In component is nonzero, including negative values. Dynamic Float-Float4/Color input defaults to scalar zero; Color uses four raw components.",
            ShaderGraphNodeKind.IsNaN => "Scalar Float In=0 to Boolean Out, true only for NaN. Split vectors to test components. Does not repair non-finite data.",
            ShaderGraphNodeKind.IsInfinite => "Scalar Float In=0 to Boolean Out, true for positive or negative infinity, false for NaN and finite values. Split vectors to test components.",
            ShaderGraphNodeKind.RGBShift => "Resamples Color In and returns Color Out. option 0=Radial (default) spreads R and B with normalized UV distance from Center=(0.5,0.5) and Falloff=1. option 1=Directional samples R/B at opposite constant offsets along Angle; green stays at the center. option 2=Triangle samples R, G and B at equal constant distances along Angle, Angle+120 and Angle+240 degrees in document-pixel space. Angle defaults to 0 degrees and applies to Directional and Triangle. Amount=2 document pixels, Blend=1 clamped to 0..1. Shifted samples clamp to document texel centers. Amount<=0 or Blend<=0 preserves In exactly. Alpha stays at the center and signed/HDR RGB is preserved. No intermediate texture. SVM previews use the native HLSL fallback.",
            ShaderGraphNodeKind.Scatter => "Reevaluate numeric/Color Source (default Float 1) within each instance's local 0..1 square on a jittered grid. Out follows Source for option 0 Max (zero background) and 1 Add; option 2 Alpha Over returns straight-alpha Color, converting Float-Float3 with alpha 1. Per-mode output rules override the default-mode outputTypeFromInput. Count=(8,8) uses rounded absolute whole cells in 1..1024; Size=(0.8,0.8) uses cell widths, absolute values capped at 16. SizeRandom=0.25 and ScaleMap=1 only shrink; Jitter=1 spans the whole cell. Density=1, Mask=1, ScaleMap and RotationMap=0 sample the canonical instance center; Source samples local UV. Density/Mask/ScaleMap/SizeRandom/Jitter clamp to 0..1. Rotation=0, RotationRandom=180 (plus/minus) and RotationMap use degrees; other settings use Scatter evaluation UV. Seed=1337 does not modify WT source seeds automatically. scatterPeriodic=false; when true, Count covers Period=(1,1), period components use max(abs(value),0.0001), and centers/identity wrap. Inactive Period retains its value and wire. sampledInputs lists all repeated branches; centerSampling lists the center-sampled subset. Source extent and size/rotation bounds limit neighbor evaluation. Ordinary buffered sources are allowed; buffers cannot bake live ScatterInstance dependencies, while Buffer after Scatter stores the completed image. Native HLSL with SVM fallback.",
            ShaderGraphNodeKind.ScatterInstance => "No inputs. UV is Float2 source-local sampling UV; Center is the Float2 canonical instance center, wrapped when periodic. Index is a stable hash-derived Float ID in 0..16777215, not a sequential number; Random is Float in 0..1. Available in Scatter Source and its center-sampled controls; nested Scatter uses the innermost context and restores the enclosing instance. Connect Random/Index explicitly to source seeds or other controls for per-instance variation. Evaluation outside Scatter reports a diagnostic; standalone source-node previews use Index=0, Random=0.5 and Center=(0.5,0.5). A buffer producer may not depend on a live instance. Native HLSL with SVM fallback; context values are not serialized.",
            ShaderGraphNodeKind.Parameter => "parameterId references graph.parameters[].id. Out follows that declaration: Float/Enum use Float (the Seed selection is a Float parameter with a Seed control), Vector2/Point use Float2, Vector3/Normal use Float3, Vector uses Float4, Color uses Color, Toggle uses Boolean, and Texture2D/Gradient/Curve/Transform2D use their matching resource ports. Enum stores float option values in controls[].optionNames/optionValues. Seed uses exact float integers from -16777216 through 16777216. Its control requires type=Seed, seed=true, valueType=float, hasMinimum=true, hasMaximum=true and explicit valueMinimum/valueMaximum for validation and Random. With Range off, parameter.hasMinimum/hasMaximum remain false but the control flags stay true and its valueMinimum/valueMaximum are -16777216/16777216. With Range on, parameter limits and the control's numeric generation bounds use the selected endpoints. Normal supplies a normalized float3. Preserve control labels, options, range flags, pixelUnits and values when replacing the complete graph; do not infer Out from the catalog's default node. This graph contract does not add typed integer ports or expanded color/toggle vectors.",
            ShaderGraphNodeKind.Reroute => "Pass In to Out without conversion. A chain without a source acts as disconnected: downstream inputs use their saved or default values, while nodes and edges remain stored. The connected type is retained, including Transform2D and other resources; a dynamic port here is not limited to numeric types. Transform2D cannot be used as a numeric vector or Color.",
            ShaderGraphNodeKind.Value => "Editable node-local value with one Out port. Store Float, Vector2, Vector3, Vector (four components), Color, Toggle, Texture2D, Gradient, Curve or Transform2D in literal (ShaderParameter). Omit literal for Float 0. For matrices set valueType to Matrix2x2, Matrix3x3 or Matrix4x4 and use matrixRows in row order. Omitted rows give identity. Connect a matrix to a Custom Function input of the same size. A disconnected matrix input uses identity.",
            ShaderGraphNodeKind.MatrixConstruction => "Construct square matrices from Float4 M0–M3 (zero by default). Outputs 4x4, 3x3 and 2x2 keep the required vectors and their leading components. Axis option 0=Row (default) or 1=Column.",
            ShaderGraphNodeKind.MatrixSplit => "Split a 2x2, 3x3 or 4x4 matrix In into vectors M0–M3 of the corresponding width. Axis option 0=Row (default) or 1=Column. Unavailable rows or columns output zero. Without an input the node uses identity, 2x2 by default.",
            ShaderGraphNodeKind.MatrixDeterminant => "Compute the determinant of a square matrix In as Float Out. An unconnected In uses identity, 2x2 by default.",
            ShaderGraphNodeKind.MatrixTranspose => "Exchange rows and columns of a square matrix In. Out retains its size. An unconnected In uses identity, 2x2 by default. Set valueType to choose another default size.",
            ShaderGraphNodeKind.Multiply => "Numeric A and B multiply component-wise. Matrix times matrix uses mul(A,B) at the smaller dimension, taking the upper-left submatrix of the larger operand. A matrix and a numeric value use mul(A,B) with operand order retained and the numeric value converted to the corresponding vector width.",
            ShaderGraphNodeKind.ApplyTransform2D => "Apply a Transform2D resource to UV (Float2, default current-pixel UV). Transform is the Transform2D input port. Local and Input are Float2 outputs, matching the parameter's _Name_ToLocal(UV) and _Name_ToInput(UV) helpers respectively; there is no Out. Use a Transform2D parameter or an inline inputs entry with port Transform, type Transform2D and resource.type Transform2D/transformValue. An omitted resource uses identity (ShaderTransform.Default). The compact inline Transform button opens a PopupWindow with the shared ShaderParameterView: TRS fields and Reset, without a canvas button; canvas manipulators belong to the parameter. Both directions retain projective mapping and the guarded homogeneous divide; neither clips, wraps nor masks coordinates. Transform2D is not accepted by numeric math.",
            ShaderGraphNodeKind.ForBegin => "Paired single-pass loop input. loopPartnerId names a ForEnd which points back to this node. Initial determines Float-Float4/Color state type. Iterations defaults to 4, is floored and bounded below by zero. Index is 0-based Float; Value is Initial on the first iteration, then the previous ForEnd.Value. Both outputs belong to this loop body. Internal node previews show the first iteration. Pairs must stay in the same graph scope.",
            ShaderGraphNodeKind.ForEnd => "Paired single-pass loop result. loopPartnerId names its ForBegin. Value is reevaluated each iteration and converted to the Initial type; Result is the final state (Initial for zero iterations). Nested loops are supported. Buffers may be sampled inside the loop, but their baked contents cannot depend on an active loop's Index or Value. Buffer after the full loop is allowed.",
            ShaderGraphNodeKind.Expression => "A single HLSL expression; expression defaults to A. Variables become case-sensitive inputs in first-appearance order; functions, types, comments and swizzles do not create inputs. valueType selects Float-Float4 for every input and Out. Supports arithmetic, HLSL functions, constructors and component access. Empty expression returns zero. Removed connected variables may be retained in inputs as inactive ports. Expression errors remain editable and are reported on the node.",
            ShaderGraphNodeKind.CausticsNoise => "Single built-in fixed 2D procedural noise kind. Bottom Algorithm selector uses option 0=OpenSimplex2S, 1=Simplex (default); both share Float2 Position (implicit UV), Scale=(8,8), Offset=(0,0), Period=(1,1), Seed=1337, Evolution=0 and Strength=1, plus Float Dispersion=1 (nonnegative, no upper limit). Outputs: Value is Float; Color is Color (col), alpha=1. Float Value includes the complete profile and final Smoothstep in 0..1 and is unchanged by Dispersion. For nonzero dispersion, Color combines six spectral samples at the same Strength, profile, Seed, Scale, Offset, Period and Evolution. Their XY positions shift along a smooth bounded local direction evaluated independently from the same periodic field at a fixed offset; nonnegative Dispersion scales the signed noise-space shifts. Omitted Spectrum uses CIE 1931 / D65 weights with the existing sample positions. The signed sum is fitted to 0..1 RGB at constant luminance. An explicit Spectrum is a Gradient input, editable inline or connected through a parameter. Six linear RGB samples at t=0,0.2,0.4,0.6,0.8,1 color the six spectral contributions from long to short wavelengths. The inline Custom ramp starts with red, orange, yellow-green, green-cyan, blue, violet. Negative RGB is clipped; gradient alpha is ignored. causticsWhiteBalance is a node setting, default true: divide each band by the sum of its channel weights (epsilon 1e-6). Channels absent from the entire palette stay zero; a black palette stays black. With balance off, each band is divided by six, retaining overall palette tint and HDR. Only Color reads Spectrum; Value is unchanged. At zero dispersion CIE/D65 returns grayscale Value. A custom Spectrum can tint it. Spectrum is stored as a generic resource input or edge; causticsWhiteBalance is stored on the node. HLSL and SVM use the same six-band weights. At nonzero dispersion green is not the unchanged scalar Value. Both algorithms support Color with Periodic, Filtering and native HLSL/SVM. At zero dispersion one noise trace supplies all six weighted contributions, including for a dynamic zero. Value-only evaluation skips the Dispersion input branch and extra channel computations. Optional Dispersion uses generic node.inputs/edges; omitted inputs default to 1 without migration. The following mappings define each unfiltered graph sample. OpenSimplex2S evaluates the former main calculation at Scale*1.125 and Strength*2, then smoothstep(0,1,0.5*exp(oldValue*3-1.5)); oldValue is its raw 0.5+0.5*signedResult, Evolution is unchanged (578-unit loop). Simplex evaluates the former temporary calculation at Evolution*2 with Scale/Strength unchanged, then smoothstep(0,1,pow(oldValue,1/1.7)); oldValue includes its exponential mapping. Simplex retains unnormalized gradients and the fixed -6 direction-field coefficient. Both algorithms support Seed, periodicity and footprint filtering. noisePeriodic/noiseFiltering default false; Offset/Period are evaluated only with noisePeriodic=true, retaining inactive values and edges. Offset shifts Position before Scale. Simplex rotates its lattice orthogonally and repeats corner hashes without blending copies; each positive period fits max(1,floor(abs(Scale)*Period+0.5)) noise-space units (Scale=8/Period=1 retain density 8). Periodic changes the pattern orientation; nonperiodic Simplex is unchanged. OpenSimplex2S repeats corner hashes on both halves of an axis-aligned BCC lattice without blending copies; periodic orientation changes while nonperiodic ImproveXY and Scale*1.125 calibration remain. Its 578-unit Evolution loop fits 501 temporal lattice units, changing internal phase speed by less than 0.1%; nonpositive components disable a spatial axis. Filtering attenuates the completed profile toward 0.5 using unwrapped Position derivatives and sample footprints; nonlinear warp aliasing can remain. Native HLSL/SVM share the same internal core; generated shaders specialize by algorithm. Use Fractal for graph octaves with ordinary SeedStep behavior. No Dimensions selector, UV resampling port, refraction controls, Resolution/Wrap or buffer. Graph calibration/profile uses a separate internal entry; public WTNoiseCaustics signatures, raw mapping, nonperiodic result and defaults (strength=0.625, evolution=0) are unchanged; periodic calls share the new BCC repetition. New WTNoiseCausticsColor(float2 p, WTNoiseSettings n, WTNoiseCausticsSettings s, float dispersion) and defaults overload (p,n,dispersion) combine six spectral position shifts at shared Strength with the public raw result, without the graph profile; Both signatures and the public fixed spectrum are unchanged. The temporary Simplex test catalog entry is removed; existing Custom Functions retain embedded source, without document rewriting or migration. Separate Caustics remains buffered.",
            ShaderGraphNodeKind.Caustics => "Fixed 2D caustics from scalar Height. Height*Strength is baked once, then neighboring texels determine refraction onto a flat receiver; finite elliptical light footprints accumulate HDR Value. Outputs: Value is Float; Color is Color (col) with HDR RGB and alpha=1. Optional Float Dispersion=1 is nonnegative with no upper limit and affects Color only. Its six spectral samples use IOR anchored at 550 nm and default to CIE 1931 / D65. An explicit Spectrum is a Gradient input, editable inline or connected through a parameter. Six linear RGB samples at t=0,0.2,0.4,0.6,0.8,1 color the six spectral contributions from long to short wavelengths. The inline Custom ramp starts with red, orange, yellow-green, green-cyan, blue, violet. Negative RGB is clipped; gradient alpha is ignored. causticsWhiteBalance is a node setting, default true: divide each band by the sum of its channel weights (epsilon 1e-6). Channels absent from the entire palette stay zero; a black palette stays black. With balance off, each band is divided by six, retaining overall palette tint and HDR. Only Color reads Spectrum; Value is unchanged. At zero dispersion Color is grayscale Value with CIE/D65 or a fully balanced palette, and can be tinted with balance off. Spectrum is stored as a generic resource input or edge; causticsWhiteBalance is stored on the node. HLSL and SVM use the same six-band weights. For d=max(Dispersion,0), f(d)=d*(0.3+0.7*d*d) for d<=1 and 1+2.4*(d-1) above one; n(lambda)=max(IOR,1)+(max(IOR,1)-1)*f(d)*((550/lambda)^2-1), with wavelength in nm. Small values give gentle separation; d=0.1 at IOR=1.333 approximates water. The existing 0..1 mapping is unchanged; above one the refractive spread grows linearly with matching value and slope. Ray tracing clamps each spectral IOR to at least one. Six samples are retained at every positive Dispersion. All samples share the baked height buffer and reuse one temporary ray texture. Color always performs six weighted projections directly into its linear RGB output, including at zero dispersion. CIE/D65 weights are preconverted to signed linear sRGB and accumulate in the existing receiver. The final sample fits negative channels to nonnegative RGB at constant luminance. The projection footprints use the same Softness. A custom palette uses six CPU samples from the existing readable gradient LUT. No GPU readback, XYZ texture or extra pass is needed. HDR above one is preserved. Value retains the original IOR; Color green is not the unchanged scalar Value at nonzero dispersion. Caustics has no compile-time zero shortcut. When Value and Color are both used, they have distinct output buffers and share the same height pass. Requesting Color does not upgrade or repurpose the Value buffer. Value-only evaluation does not evaluate the Dispersion input branch or extra channel computations. Generic node.inputs/edges store Dispersion; omitted inputs default to 1 without migration. UV samples the completed image. Strength=0.02, Depth=1, IOR=1.333 (minimum 1), LightDirection=(0,0,-1), Softness=1 receiver pixel, Intensity=0.25. Flat height gives uniform light. Custom 512x512/Repeat defaults, bilinear sampling; height/ray grid is bounded to 16..512 per axis. Repeat needs smoothly periodic input. Same cache and Fractal SeedStep=0 boundary as Buffer; put Fractal upstream to change the source surface. No generated pixels are serialized.",
            ShaderGraphNodeKind.Buffer => "Render numeric/Color In into a reusable transient linear float32 RGBA GPU texture, then sample it at Float2 UV (implicit current-pixel UV). Out retains In's type. bufferResolution: Canvas (default, current render-target size including node previews) or Custom (exact bufferSize, default [512,512]); wrap/filter select sampling behavior. Bound material data/time/index/transforms/static texture changes invalidate contents. External RenderTexture inputs or CustomFunction code conservatively refresh on every owner render; allocation is reused and each buffer executes once per draw in dependency order. No per-pixel/per-octave allocation or document pixel serialization. Buffer before Fractal requires an unconnected literal SeedStep=0; Buffer after Fractal is allowed.",
            ShaderGraphNodeKind.Fractal => "Reevaluate numeric/Color In over the graph's 2D domain; Out retains In's type. FBm/Ridged/PingPong use component-wise normalized weighted accumulation, including Color alpha. Multifractal, HybridMultifractal, HeteroTerrain and RidgedMultifractal use signed s=2*In-1 and raw unnormalized cascades, retaining HDR/negative results. Bias=1 shifts the signed signal in Hybrid/Hetero and the ridge level in RidgedMultifractal; Feedback=2 (minimum 0) controls inter-octave feedback in Hybrid/RidgedMultifractal. Gain remains octave amplitude decay. WeightedStrength/PingPongStrength are unused by these four modes. Mode-specific unused ports are hidden unless connected; values and edges survive mode switches. FBm (default) retains signed/HDR values; Ridged applies 1-abs(2*v-1); PingPong applies 1-abs(frac(v*max(PingPongStrength,0))*2-1). None is unsupported. Inputs: Octaves=3 (integer, 1..8), Lacunarity=2 (0.01..16), Gain=0.5 and WeightedStrength=0 (0..1), PingPongStrength=2 (only PingPong), SeedStep=1 (integer conversion). noiseFiltering=true attenuates octaves using the local UV pixel footprint; upstream WT filtering remains independent. SeedStep offsets all upstream noise-node seeds including grain, not arbitrary CustomFunction seed parameters. This is generic branch repetition, not exact per-noise WT fractals; the public WT HLSL API is unchanged. Source branches containing Buffer require an unconnected literal SeedStep=0; connected SeedStep is conservatively rejected even at zero. Buffer after Fractal is allowed.",
            ShaderGraphNodeKind.CanvasPosition => "Document-space position, independent of layer/group transforms. Float2 outputs: Normalized (0..1 over the canvas), AspectX = Normalized * (Width/Height, 1), AspectY = Normalized * (1, Height/Width). All share the (0,0) origin and remain unclamped when sampling outside the canvas. No inputs.",
            ShaderGraphNodeKind.CanvasData => "Canvas Size (Float2 pixels), Texel (Float2 reciprocal dimensions) and PixelScale (Float document pixel multiplier configured by the Frame tool). Values are independent of preview resolution and camera zoom. No inputs or node preview.",
            ShaderGraphNodeKind.SampleInput => "Read the Shader layer's shared Input (None, Texture, Layer or CompositeBelow), or the preceding image in an FX chain. UV defaults to local procedural UV; Out is Color labeled RGBA, with scalar R/G/B/A outputs sharing the same sample. wrap/filter match Sample Texture. None or a missing source is transparent black. This reads an image without replacing the lower composition or changing ordinary blending.",
            ShaderGraphNodeKind.Output => "RGBA is a straight Color input; numeric RGB gains alpha 1. Float AlphaScale (Alpha Scale in the UI) defaults to 1 and multiplies RGBA.a, leaving RGB unchanged. Accepts a literal or connected mask. No implicit clamp; no outputs or node preview.",
            ShaderGraphNodeKind.HeightBlend => "Blend signed/HDR scalar Bottom=0 and Top=1 by height, with Offset=0, Smoothness=0.1 and Opacity=1. Outputs Float Height and Mask; there is no Out. Offset shifts the top height, nonnegative Smoothness softens the height comparison and saturated Opacity controls the top contribution. At zero Smoothness the higher height wins, with weight 0.5 for a tie. Height is not clamped to 0..1.",
            ShaderGraphNodeKind.TileUV => "Grid (option 0) or Staggered (1) cell-local UV with Count=(4,4), Offset=(0,0) in cells, RowOffset=0.5 active only in Staggered, RandomRotation=0 degrees, RandomScale=0 (clamped 0..1), Seed=1337. Outputs UV, Cell and Center as Float2, Random and Mask as Float; there is no Out. Cell identifies the original cell; Center is in UV coordinates. Random transformations stay in their own cell: Mask clips transformed motifs, with no cross-cell overlap.",
            ShaderGraphNodeKind.PerspectiveUV => "Inverse projective mapping from a quadrilateral to unit-square source UV. Defaults BottomLeft=(0,0), BottomRight=(1,0), TopRight=(1,1), TopLeft=(0,1). Outputs Float2 UV and Float Mask, not Out. Mask selects the quad; invalid, degenerate or nonconvex quads return zero UV and zero Mask. Multiply sampled alpha by Mask for coverage.",
            ShaderGraphNodeKind.RotateAboutAxis => "Rotate Float3 In around Float3 Axis by scalar Rotation. Unit: option 0=Radians (default), 1=Degrees. Inputs default to zero. Axis is normalized; supply a nonzero axis. Float3 Out retains vector magnitude.",
            ShaderGraphNodeKind.Blackbody => "Temperature=6500 Kelvin, clamped to 1000..40000, produces Color Out with normalized linear RGB and alpha 1. Planck/CIE 1931 colors are approximated by 16 generated cubic Hermite segments in logarithmic temperature, selected by four binary branch decisions without a lookup texture. Negative RGB is clipped and the largest component normalized to 1. Temperature controls chromaticity; set brightness separately with Multiply or Exposure.",
            ShaderGraphNodeKind.SlopeBlur => "Reevaluate numeric/Color In along the negative, unnormalized gradient of scalar Slope=0, using central differences one document pixel apart on each side. Strength=8 scales document-pixel travel per unit UV-height slope; negative Strength reverses the path. Samples=16 rounds/clamps to 1..64 integration steps, plus the starting sample. option 0=Average, 1=Min, 2=Max. Out retains In's type; Average filters Color in premultiplied RGBA, while Min/Max process components independently. Constant Slope or zero Strength preserves In; a path with no movement returns the original evaluation, including its antialiasing and transparent RGB. No Radius input or radiusUnits; both In and Slope are sampled branches. Repeated slope and input evaluations can be expensive, especially with nested filters or noise.",
            ShaderGraphNodeKind.BilateralBlur => "Soft edge-aware Gaussian filtering of numeric/Color In. Radius=2 uses document pixels; Tolerance=0.1 is nonnegative, with zero accepting exact guide matches only. option 0=Source guides by In; option 1=Custom adds dynamic numeric/Color Guide, default scalar 0, reevaluated with In. Color guides in either mode compare premultiplied RGBA to ignore hidden RGB; numeric guides retain their component values. Out retains In's type independently of Guide; Color filtering is premultiplied and returns straight RGBA. Zero Radius preserves In; results are not clamped. Larger radii and nested sampled branches increase cost.",
            ShaderGraphNodeKind.PixelateUV => "Float2 Out snaps UV to cell centers in document pixels: (floor((UV*CanvasSize-Offset)/size)+0.5)*size+Offset, divided by CanvasSize. CellSize defaults to (8,8), uses absolute values with a 1-pixel minimum; Offset defaults to (0,0) pixels. Coordinates remain unclamped. Connect before a texture or procedural UV input.",
            ShaderGraphNodeKind.ChannelMixer => "Color Out uses dot(Color.rgb, Red/Green/Blue) for each RGB channel and retains alpha. Float3 coefficient rows default to the identity matrix. Values are weights (1 means 100%), may be negative, and are not normalized or clamped; HDR is retained.",
            ShaderGraphNodeKind.WhiteBalance => "Bradford chromatic adaptation of linear RGB with neutral Temperature=0 and Tint=0. Both inputs clamp to -1..1; positive Temperature warms, negative cools; positive Tint adds magenta, negative green. Output remains Color with original alpha, signed RGB and HDR; no output clamp.",
            ShaderGraphNodeKind.DirectionalBlur => "Gaussian-weighted sampling of numeric/Color In along a line at Angle degrees, over +/- Radius document pixels. Defaults Radius=8, Angle=0, Samples=17. Samples rounds upward to an odd count from 3 to 129; increase it for longer paths or fine detail. Out retains the input type; Color uses premultiplied filtering with straight RGBA output. Radius=0 is identity; no result clamp or intermediate texture.",
            ShaderGraphNodeKind.RadialBlur => "Gaussian-weighted sampling of numeric/Color In around Center=(0.5,0.5). Spin (option 0) samples +/- Angle degrees (default 10) in document-pixel space; Zoom (option 1) samples radius factors 1 +/- abs(Strength), default 0.1. The unused Angle/Strength input stays visible but inactive. Samples=17 rounds upward to an odd count from 3 to 129. Color filters premultiplied; numeric vectors filter per component. Zero amount and the center preserve the input; no clamp or intermediate texture.",
            ShaderGraphNodeKind.Sign => "Component-wise sign of numeric/Color In: -1, 0 or 1. Color participates as Float4; Out is numeric with the inferred input width.",
            ShaderGraphNodeKind.Truncate => "Rounds each numeric component toward zero. In defaults to 0. Out retains the inferred numeric width, with Color treated as Float4.",
            ShaderGraphNodeKind.Saturate => "Clamp numeric/Color In component-wise to 0..1. Color participates as Float4; Out is numeric with the inferred input width.",
            ShaderGraphNodeKind.Modulo => "Truncating remainder fmod(A,B), component-wise at the widest numeric input width. Nonzero results have A's sign; B=0 is undefined. Defaults A=0, B=1.",
            ShaderGraphNodeKind.Sine or ShaderGraphNodeKind.Cosine or ShaderGraphNodeKind.Tangent => "Component-wise trigonometric function of In in radians. Out uses the inferred numeric width.",
            ShaderGraphNodeKind.Arctangent2 => "Component-wise atan2(Y,X) in radians, at the widest numeric input width. Defaults Y=0, X=1. The angle at (0,0) is undefined.",
            ShaderGraphNodeKind.Arcsine or ShaderGraphNodeKind.Arccosine => "Component-wise inverse trigonometric function. In requires -1..1 without implicit clamping. Out is in radians and uses the inferred numeric width.",
            ShaderGraphNodeKind.Arctangent => "Component-wise atan(In) in radians. Out uses the inferred numeric width.",
            ShaderGraphNodeKind.HyperbolicSine or ShaderGraphNodeKind.HyperbolicCosine or ShaderGraphNodeKind.HyperbolicTangent => "Component-wise hyperbolic function of In. Out uses the inferred numeric width.",
            ShaderGraphNodeKind.DegreesToRadians or ShaderGraphNodeKind.RadiansToDegrees => "Component-wise conversion between degrees and radians. Out uses the inferred numeric width.",
            ShaderGraphNodeKind.Exponential => "Component-wise exponential with inferred numeric output width and no implicit clamp. Base uses option 0 for Base E (default, exp(In)) or 1 for Base 2 (exp2(In)).",
            ShaderGraphNodeKind.Log => "Component-wise logarithm of In=1. Base: option 0=Base E (default), 1=Base 2, 2=Base 10. Requires positive input without implicit clamping. Out uses the inferred numeric width.",
            ShaderGraphNodeKind.Reciprocal => "Component-wise reciprocal with In=1 by default. Method option 0 (Default) computes 1/In and option 1 (Fast) uses rcp(In). Out retains the numeric input width.",
            ShaderGraphNodeKind.ReciprocalSquareRoot => "Component-wise reciprocal square root rsqrt(In), default In=1. Use positive inputs for finite results. Out retains the numeric input width.",
            ShaderGraphNodeKind.SoftSign => "Component-wise In/(1+abs(In)), default In=0. Smoothly compresses finite signed values toward -1 and 1. Out retains the numeric input width; Color is treated as Float4, including alpha. No implicit clamp or color conversion.",
            ShaderGraphNodeKind.InverseSoftSign => "Component-wise In/(1-abs(In)), default In=0. Inverse of Soft Sign for -1<In<1; singular at -1 and 1. No domain clamp: outside that interval the formula is still evaluated but is not the inverse mapping. Out retains numeric width; Color is Float4, including alpha.",
            ShaderGraphNodeKind.CrossProduct => "Float3 Out = cross(A,B), with Float3 A=(1,0,0) and B=(0,1,0) by default. Does not normalize inputs or output.",
            ShaderGraphNodeKind.Projection => "Project A onto B. Out follows the widest numeric input. For B=0, Out=0.",
            ShaderGraphNodeKind.Rejection => "Return the part of A perpendicular to B. Out follows the widest numeric input. For B=0, Out=A.",
            ShaderGraphNodeKind.FresnelEffect => "Float Out is pow(1-saturate(dot(Normal,ViewDir)),Power) after normalizing both directions. Float3 Normal and ViewDir default to (0,0,1), and Float Power defaults to 1.",
            ShaderGraphNodeKind.SphereMask => "Float 0..1 mask based on distance from Coords to Center. The widest numeric input sets the dimension. Coords=0, Center=0.5, Radius=0.1 and Hardness=0.8. Radius is fully covered and 1-saturate(Hardness) widens it outward. Hardness=1 gives a hard edge.",
            ShaderGraphNodeKind.Transform => "Convert Float2 positions between normalized layer and canvas coordinates. option 0=Layer to Canvas (default), 1=Canvas to Layer. When unconnected In uses the current pixel in the selected input space. Out is Float2.",
            ShaderGraphNodeKind.Reflection => "Reflect Incident around Normal using the widest numeric input width. Inputs are not normalized automatically; supply a unit Normal. Defaults Incident=-1, Normal=1.",
            ShaderGraphNodeKind.Refract => "Float3 Incident=(0,0,1), Normal=(0,0,-1), scalar IORSource=1 and IORMedium=1.5. IOR inputs clamp to at least 1. Supply unit directions. Outputs Refracted(Float3) and Intensity(Float). Mode: option 0=Critical Angle, 1=Safe (default). Beyond the critical angle, Critical Angle reflects with Intensity=1; Safe returns the tangential component with Intensity=0.",
            ShaderGraphNodeKind.And => "Boolean Out = A && B. Both inputs default false; numeric/resource inputs are not Boolean conditions.",
            ShaderGraphNodeKind.Or => "Boolean Out = A || B. Both inputs default false; numeric/resource inputs are not Boolean conditions.",
            ShaderGraphNodeKind.Not => "Boolean Out = !In. In defaults false.",
            ShaderGraphNodeKind.NormalEncode => "Float3 Normal (default signed flat 0,0,1) to packed Float3 Out = Normal*0.5+0.5. No normalization, clamp, color conversion.",
            ShaderGraphNodeKind.NormalDecode => "Float3 Normal (default packed flat 0.5,0.5,1) to signed Float3 Out = Normal*2-1. No normalization, clamp, color conversion.",
            ShaderGraphNodeKind.NormalStrength => "Normalize signed XYZ Float3 Normal, scale XY by Strength and blend Z from 1 toward the unit normal's Z using saturate(abs(Strength)), then normalize. Strength 0 is flat, 1 preserves the unit normal, negative values reverse XY. Input/output use Signed XYZ; neutral (0,0,1).",
            ShaderGraphNodeKind.NormalLighting => "option Local=0 (default) uses LightDirection/LightColor/Intensity and optional SpecularOnly. Global=1 sums document lights, uses UV mapped into canvas pixels and Height (pixels), and retains Color/Ambient/SpecularColor/SpecularIntensity/Roughness. Normal uses Signed XYZ; neutral (0,0,1). Fixed view +Z; source alpha and HDR RGB are preserved. Ambient is applied once.",
            ShaderGraphNodeKind.LightCount => "Number of document light slots, including disabled sources. Order matches document snapshot lights[].index.",
            ShaderGraphNodeKind.LightDirection => "Normalized surface-to-light direction for Index. UV maps through canvas coordinates; Height is in document pixels. Invalid indices return +Z.",
            ShaderGraphNodeKind.LightColor => "Linear HDR RGB times source intensity for Index, before attenuation. Disabled/invalid sources return zero. Multiply by LightAttenuation for incident radiance.",
            ShaderGraphNodeKind.LightAttenuation => "Directional=1; Point=pow(saturate(1-distance/range),falloff). UV and Height locate the surface in full canvas pixels. Invalid indices return zero.",
            ShaderGraphNodeKind.Premultiply => "Color Out.rgb = Color.rgb*Color.a; alpha is unchanged. No implicit clamp. Output.RGBA expects straight color, so unpremultiply before Output when necessary.",
            ShaderGraphNodeKind.Unpremultiply => "Color Out.rgb = Color.rgb/Color.a for every nonzero alpha, including small or negative alpha. Exactly zero alpha returns zero RGB. Alpha is unchanged; no epsilon cutoff or implicit clamp.",
            ShaderGraphNodeKind.Flipbook => "Float2 atlas UV from UV, Columns=4, Rows=4 and Frame=0. Dimensions floor/clamp to 1..4096; floored Frame wraps, including negative values. option 0 is Bottom to Top (frame 0 bottom-left), 1 Top to Bottom. Local UV orientation is unchanged; no implicit clamp or sampling.",
            ShaderGraphNodeKind.Dither => "Color Out quantizes RGB with an ordered Bayer pattern; alpha is unchanged and signed/HDR RGB is not clipped. Levels=8 rounds to at least 2; PixelSize=1 uses abs with minimum 1 document pixel. UV defaults to current-pixel UV. option 0/1/2 selects Bayer 4x4/2x2/8x8. Pattern size follows document dimensions, independently of preview size.",
            ShaderGraphNodeKind.InverseLerp => "Component-wise (T-A)/(B-A), without clamping to 0..1. Defaults A=0, B=1, T=0. Out uses the widest numeric input width; Color participates as Float4. Equal bounds are undefined.",
            ShaderGraphNodeKind.DDX => "Signed horizontal derivative of In over one render pixel. Out follows the numeric width; Color is treated as Float4.",
            ShaderGraphNodeKind.DDY => "Signed vertical derivative of In over one render pixel. Out follows the numeric width; Color is treated as Float4.",
            ShaderGraphNodeKind.FWidth => "Component-wise abs(DDX(In)) + abs(DDY(In)). Measures change over one render pixel. Out follows the numeric width; Color is treated as Float4. Search alias: DDXY.",
            ShaderGraphNodeKind.RangeMask => "Component-wise range mask in 0..1. In is dynamic Float-Float4 (Color as Float4); Out follows In. Scalar Center=0.5, Width=0.2, Feather=0. Width is the full interval including inward transitions; Feather clamps to 0..Width/2. option selects Falloff: 0 Linear (default), 1 Smooth. Zero Feather includes both hard boundaries. Width<=0 is empty. In/Center retain signed and HDR values. rangeLuminance=false (Input Components) retains component-wise Out; true (Input Luminance) selects Linear luminance before masking and returns Float, with scalar inputs unchanged.",
            ShaderGraphNodeKind.InstanceIndex => "Float Out = Start + (float)_WhimTexInstanceIndex * Scale; Float inputs default to Start=0 and Scale=1. The shared uint index is cast with ordinary floating-point precision. Standard rendering currently supplies index 0, so Out equals Start; this is unrelated to Repeat.Cell or CellularNoise.CellValue.",
            ShaderGraphNodeKind.Time => "Float Out = Start + _WhimTexTime * Scale; Float inputs default to Start=0 and Scale=1. Graph-window node and result previews receive shared Editor seconds, including result-preview FX. Other renders use time 0, so Out equals Start. No own preview.",
            ShaderGraphNodeKind.PerlinNoise => ShaderNodeNoiseDescription("Perlin gradient noise. Gradient and 2D/3D Curl retain signed analytic derivatives and magnitude."),
            ShaderGraphNodeKind.ValueNoise => ShaderNodeNoiseDescription("Interpolated lattice values. Gradient and 2D/3D Curl retain signed analytic derivatives and magnitude."),
            ShaderGraphNodeKind.ValueCubicNoise => ShaderNodeNoiseDescription("Cubic-interpolated lattice values. Gradient and 2D/3D Curl retain signed analytic derivatives and magnitude."),
            ShaderGraphNodeKind.OpenSimplex2Noise => ShaderNodeNoiseDescription("OpenSimplex2 field. Gradient and 2D/3D Curl retain signed analytic derivatives and magnitude."),
            ShaderGraphNodeKind.OpenSimplex2SNoise => ShaderNodeNoiseDescription("Smooth OpenSimplex2S field. Gradient and 2D/3D Curl retain signed analytic derivatives and magnitude."),
            ShaderGraphNodeKind.FlowNoise => ShaderNodeNoiseDescription("Rotating OpenSimplex2S gradients; Phase=0 in cycles, with 0 and 1 equivalent. Gradient and 2D/3D Curl retain signed analytic derivatives and magnitude."),
            ShaderGraphNodeKind.CellularNoise => ShaderNodeNoiseDescription("Value uses noiseOutput (default F1): CellValue, F1, F2, Add, Subtract, Multiply, Divide or DistanceToEdge. Smoothness=0 preserves hard results; 0..1 smooths every Value mode, blending cell values or distances as appropriate. noiseMetric: Euclidean, EuclideanSquared, Manhattan or Hybrid; ignored only for DistanceToEdge, which uses Euclidean geometry. noiseDistribution: Uniform or FixedRadius. Jitter=1; Evolution=0 is signed unclamped bounded feature motion. Extra Float F1/F2/CellValue and dimension-width ToPoint expose hard, unfiltered first-octave geometry in the effective metric; no uint ID port."),
            ShaderGraphNodeKind.TriangleVoronoiNoise => "WT scalar noise, fixed 2D: noiseDimensions supports only TwoD; no Dimensions selector. Float Value is saturate(edgeDistance/sqrt(2)) before filtering, in 0..1 without gamma conversion. Float2 Position defaults to UV; Scale=(8,8), Offset=(0,0), Period=(1,1), Seed=1337. Offset and Period are used only when noisePeriodic=true (default false). Offset shifts Position in input units before Scale and periodic reduction; otherwise translate Position with Add; nonpositive Period disables an axis. noiseDistribution: Uniform (default) or FixedRadius; Jitter=1 clamps to 0..1; Float Evolution=0 moves shared feature points, retaining spatial periods; Float2 Stretch=(1,1) uses absolute components clamped to 0.5..2. Uses WT Cellular feature points, selecting the nearest with a triangular gauge; no Metric or Output selector. Float Distance is Euclidean length to that feature, not the gauge or boundary distance; Float CellValue is its repeatable value, Float2 ToPoint points to it, and Float2 BorderNormal is a unit direction toward the nearest dominance boundary or zero at a degenerate boundary. Auxiliary geometry uses unstretched noise-lattice coordinates and is unfiltered first-octave data. Coincident affine-face equality plateaus use deterministic cell ties; plateau interiors are not borders. No raw edgeDistance, uint ID or Gradient/Curl ports. Single noise evaluation: no embedded fractal or octave inputs; use the separate Fractal node for generic 2D branch repetition. noiseFiltering defaults false; enabling it supplies unwrapped Position derivatives, including sampled branches, and uses an approximate cell-family minification mean, not the exact tile mean. No alias-free guarantee or implicit texture mapping.",
            ShaderGraphNodeKind.GaborNoise => ShaderNodeNoiseDescription("Directional Gaussian-windowed sparse convolution. Frequency=2, Bandwidth=1, Anisotropy=0.8, Direction=(1,0,0) at the selected width. Float Phase/Intensity/Phasor share the first octave. option selects the Phasor Profile: 0 Sine (default), 1 Triangle, 2 Pulse. PhaseOffset=0 cycles shifts Value, Phase, Phasor, Gradient and Curl. Whole turns repeat. Intensity is independent and skips PhaseOffset when used alone. Gradient and Curl differentiate in Position units with PhaseOffset and other settings held locally constant. Profile and Width=0.5 (Pulse only, clamped 0..1) shape Phasor only. Width is hidden unless Pulse or connected; values and edges survive profile changes. Phasor uses analytic phase footprints and fades unresolved bands toward 0.5 (Sine/Triangle) or Width (Pulse); filtering is approximate with locally constant settings. Phase singularities are retained, with fading near signal zeros inside a pixel. Existing Value/Phase/Intensity and signed Gradient/Curl meanings are unchanged. Sampled filters multiply its evaluation cost."),
            ShaderGraphNodeKind.SpotsNoise => ShaderNodeNoiseDescription("Soft positive spots: Density=1, Radius=0.35, Softness=0.5, Elongation=1, Direction=(1,0,0) at the selected width."),
            ShaderGraphNodeKind.ShardNoise => ShaderNodeNoiseDescription("Faceted scalar noise: Sharpness=0.7 (0..1) controls soft slopes through sharp shards. Value is 0..1; use the separate Fractal node for octaves."),
            ShaderGraphNodeKind.WhiteNoise or ShaderGraphNodeKind.BlueNoise => "WT pixel grain. noiseDimensions: OneD or TwoD (default); Position remains Float2 with implicit UV. Translate Position with Add in UV units; divide a pixel displacement by ImageSize first. GrainSize=1 pixel, Direction=0 degrees, Seed=1337. Float2 ImageSize defaults to CanvasSize. noiseInverted defaults false. Float Value and Color (alpha 1) are emitted independently with the library color flag; Color has independent RGB. No fractal, Scale, Period or scalar-noise filtering controls.",
            ShaderGraphNodeKind.OpenSimplex2WarpNoise or ShaderGraphNodeKind.OpenSimplex2ReducedWarpNoise or ShaderGraphNodeKind.BasicGridWarpNoise => "WT vector displacement using the named warp basis. noiseDimensions selects OneD/TwoD/ThreeD (default TwoD), with matching Position/Scale/Offset/Period and output widths. Position defaults to UV.x/UV/float3(UV,value.z), with independently editable Z defaulting to 0. Scale=8, Offset=0 and Period=1 per axis. Offset and Period are used only when noisePeriodic=true (default false; nonpositive Period disables an axis). Offset shifts Position in input units before Scale and periodic reduction; otherwise translate Position with Add. Seed=1337, Strength=0.1. fractal: None (one octave), Progressive (warp each next octave) or Independent (sample original position); Octaves=3, Lacunarity=2, Gain=0.5. No WeightedStrength/PingPongStrength. noiseFiltering defaults false; enabling it supplies unwrapped Position derivatives, including sampled branches. Displacement is signed and unnormalized; Position is input Position + Displacement. No implicit texture mapping.",
            ShaderGraphNodeKind.Random => "Deterministic Float Out in [0,1) from Float2 Coordinates and Seed. With no edge or explicit input literal, Coordinates defaults to current-pixel UV. Explicit Custom constants are retained. Connect Repeat.Cell for one value per cell.",
            ShaderGraphNodeKind.RandomRange => "Deterministic Float Out between Min and Max from Float2 Seed. Defaults Seed=(0,0), Min=0 and Max=1. Uses the same hash as Random with Coordinates=Seed and scalar Seed=0. Equal bounds return that value and reversed bounds reverse the mapping.",
            ShaderGraphNodeKind.NoiseSineWave => "Component-wise sine of In in radians with deterministic additive noise. In defaults to 0. Float2 MinMax=(-0.5,0.5) defines the noise bounds. Out uses the inferred numeric input width without clamping.",
            ShaderGraphNodeKind.SawtoothWave or ShaderGraphNodeKind.SquareWave or ShaderGraphNodeKind.TriangleWave => "Component-wise waveform with a period of 1 and signed output. In defaults to 0. Out uses the inferred numeric input width.",
            ShaderGraphNodeKind.Checkerboard => "Checkerboard pattern with smoothed edges. Out is a Float mask.",
            ShaderGraphNodeKind.Stripes => "Antialiased Float mask with Square, Triangle or Sine profile. Width affects Square only. Angle is degrees; Phase is cycles. Connect Time to Phase for graph-preview animation.",
            ShaderGraphNodeKind.Grid => "Grid pattern with smoothed edges. Out is a Float mask.",
            ShaderGraphNodeKind.Rings => "Antialiased concentric Float mask around Center. Frequency is cycles per UV distance, Width is the filled fraction, Phase is cycles. Connect Time to Phase for graph-preview animation.",
            ShaderGraphNodeKind.GradientFactor => "Linear, Radial or Angular Float coordinate. Connect Out to SampleGradient.T and a Gradient parameter to SampleGradient.Gradient. Angle is degrees. Linear/Radial can exceed 0..1; Angular wraps to turns.",
            ShaderGraphNodeKind.Shape => "Float Out is a smoothed mask; Float Distance is signed, negative inside. Ellipse distance is approximate. Inputs depend on option: use modes[].inputs. Capsule uses Center/Size/Rotation; Line Segment uses Start/End/Width. Rounded Polygon is option 7: Center=(0.5,0.5), Size=(0.5,0.5), Sides=5, Roundness=0.3, Rotation=0, Feather=0. Sides rounds/clamps to 3..64; Roundness clamps to 0..1 from sharp to circular (elliptical under nonuniform Size). Its signed field is not an exact Euclidean SDF. Rotation is degrees.",
            ShaderGraphNodeKind.CombineDistance => "Union, Intersection or Subtract (A minus B) of signed Float A/B. Out is signed, negative inside; Mask is antialiased coverage. Smoothness blends the distance operation; Feather widens only the mask edge.",
            ShaderGraphNodeKind.TransformUV => "Translate, scale and rotate coordinates independently of the layer Transform. Rotation is degrees. Out is Float2 UV without a 0..1 clamp.",
            ShaderGraphNodeKind.PolarCoordinates => "To Polar returns Float2(angle in turns, 2*distance from Center); From Polar reads UV.x as turns and UV.y/2 as radius around Center. Angle offsets degrees. No implicit 0..1 clamp; the angular coordinate wraps.",
            ShaderGraphNodeKind.Repeat => "For p=UV*Scale+Offset, Float2 Out=frac(p) and Float2 Cell=floor(p), including negative cells. Cell is not InstanceIndex; connect it to Random.Coordinates for one random value per cell.",
            ShaderGraphNodeKind.Mirror => "Fold X, Y or Both onto positive half-planes relative to Center. Out is Float2 UV; no implicit 0..1 clamp.",
            ShaderGraphNodeKind.Kaleidoscope => "Fold coordinates into radial sectors. Sectors rounds/clamps to 1..128; Angle is degrees. Out is Float2 UV; no implicit 0..1 clamp.",
            ShaderGraphNodeKind.VectorDisplacement => "Float2 Out=UV+decoded Displacement*Strength, component-wise. Signed RG uses the vector unchanged; Packed RG decodes 2*v-1, so (0.5,0.5) is neutral. Strength=(0,0) preserves UV; no 0..1 clamp or vector normalization.",
            ShaderGraphNodeKind.Twirl => "Rotate UV around Center with smooth radial falloff. Strength is degrees; Radius<=0 or Strength=0 preserves UV. Out is Float2; no 0..1 clamp.",
            ShaderGraphNodeKind.BulgePinch => "Radial UV deformation: positive Strength magnifies, negative Strength pinches the sampled image. Radius<=0 or Strength=0 preserves UV. Out is Float2; no 0..1 clamp.",
            ShaderGraphNodeKind.Wave => "Displace UV along Direction (degrees), varying across the perpendicular axis. Phase is cycles. Out is Float2 UV; Strength=0 preserves UV; no 0..1 clamp. Connect Time to Phase for graph-preview animation.",
            ShaderGraphNodeKind.Ripple => "Radial UV wave around Center with Radius falloff and Phase in cycles. Out is Float2; Radius<=0 or Strength=0 preserves UV; no 0..1 clamp. Connect Time to Phase for graph-preview animation.",
            ShaderGraphNodeKind.SampleGradient => "Sample a Gradient resource at scalar T. Out is Color. Connect GradientFactor.Out to T to color a gradient field.",
            ShaderGraphNodeKind.Hue => "Rotate hue by Degrees (default 0) in sRGB-encoded RGB and return linear Color Out. Alpha is preserved; signed and HDR values are not clipped to 0..1.",
            ShaderGraphNodeKind.Saturation => "Scale saturation in sRGB-encoded RGB by nonnegative Amount (default 1); 0 removes saturation. Return linear Color Out, preserving alpha and unclipped signed/HDR values.",
            ShaderGraphNodeKind.Exposure => "Color Out.rgb = Color.rgb * exp2(Stops), in linear RGB. Stops defaults to 0. Alpha is preserved; no 0..1 clamp.",
            ShaderGraphNodeKind.Lightness => "Adjust lightness in sRGB-encoded RGB with Amount clamped to -100..100 (default 0). Return linear Color Out and preserve alpha; no final 0..1 RGB clamp.",
            ShaderGraphNodeKind.Levels => "Remap InputRange through Gamma into OutputRange using Luminance (default) or RGB mode. Clamp defaults to true and bounds the input to InputRange; false permits signed-gamma extrapolation. The node displays it as a bottom Clamp Input checkbox, stored in inputs under Clamp. An existing connected Clamp stays visible and drives the value; the checkbox is disabled until disconnected. Color Out preserves alpha.",
            ShaderGraphNodeKind.ColorBalance => "Adjust Shadows, Midtones and Highlights with Float3 offsets and ShadowRange/HighlightRange weights. PreserveLuminance defaults to true. Color Out preserves alpha and has no 0..1 RGB clamp.",
            ShaderGraphNodeKind.Posterize => "Quantize each RGB channel with floor(Levels), at least 2, including endpoints 0 and 1. Levels defaults to 8. Color Out preserves alpha; RGB outside 0..1 is not clipped.",
            ShaderGraphNodeKind.ConvertColor => "Explicit conversion between inputColorSpace and outputColorSpace; defaults LinearRGB to SRGB. LinearRGB/SRGB/Oklab retain signed/HDR values. HSV/HSL use sRGB-encoded RGB and hue in turns: HSV clamps negative RGB to 0 but retains HDR V; HSL clamps RGB, L and S to 0..1 and cannot roundtrip HDR/negative values. Same-space conversion is exact. Color Out preserves alpha; wires never convert spaces implicitly.",
            ShaderGraphNodeKind.Luminance => "Float Out = dot(linear Color.rgb, (0.2126,0.7152,0.0722)), independent of alpha. No 0..1 clamp; explicitly convert encoded input to LinearRGB first.",
            ShaderGraphNodeKind.GradientMap => "Sample required Gradient using the selected channel of Color. Color Out uses gradient RGB; option selects alpha Preserve (default), Multiply or Replace. Gradient wrap controls the sampling coordinate.",
            ShaderGraphNodeKind.ColorCurves => "Optional Master, Red, Green, Blue and Alpha Curve inputs are identity unless connected or assigned an inline curve. Master mode is RGB (default) or Luminance. For each curve use x + curve(saturate(x)) - saturate(x), retaining HDR/negative values at identity. Alpha changes only through its own curve. Out is Color.",
            ShaderGraphNodeKind.ColorMask => "Float Out is the same selection mask as ReplaceColor.Mask. Distance option 0=Linear, 1=Perceptual (default, Oklab), 2=Chroma (Oklab a/b only, ignoring L but not saturation). Range=0.1 is the fully selected radius; Fuzziness=0.05 is the outer linear fade. Negative Range/Fuzziness behave as zero, without an upper limit. Zero Fuzziness selects distance<=Range exactly. Alpha is ignored; no SDR clipping or per-mode distance normalization.",
            ShaderGraphNodeKind.HueRange => "Float Out selects a cyclic hue Range in degrees (default 330,30), with Feather (default 15 degrees) and SaturationRange (default 0.05,0.1). Span >=360 is full; equal endpoints are empty unless Feather>0. Alpha is not applied to the mask.",
            ShaderGraphNodeKind.MixColor => "Interpolate A/B in Linear RGB, sRGB or Perceptual (default) space. Color Out has interpolated RGB and alpha; T is unbounded, default 0.5. This is color interpolation, not alpha compositing.",
            ShaderGraphNodeKind.SetAlpha => "Color Out retains RGB and replaces (default) or multiplies alpha by Alpha (default 1), without implicit clamping.",
            ShaderGraphNodeKind.HeightToNormal => "heightSource selects Texture (default, required resource) or Value (Float Height branch). Texture samples neighboring bound-source texels using channel/wrap/filter; Value reevaluates the branch at neighboring UVs with Radius in document pixels. Channel applies to Texture and Value; scalar Value inputs pass through unchanged. Both retain derivative, Strength and FlipX/Y. Float3 Out is the Signed XYZ normal normalize(-dx,-dy,1). Preview displays XYZ directly, without packing. See settings.heightSource.values[].inputs.",
            ShaderGraphNodeKind.NormalBlend => "Blend Float3 Base and Detail normals using Reoriented (default) or Whiteout and Strength. Both inputs and normalized Float3 Out use Signed XYZ; neutral defaults (0,0,1).",
            ShaderGraphNodeKind.HeightToDistortion => "heightSource selects Texture (default, source-texel Radius, channel/wrap/filter) or Value (Float Height branch reevaluated at neighboring UVs, document-pixel Radius). Channel applies to both sources; scalar Value inputs pass through unchanged. Both use derivative, Strength and FlipX/Y. Float2 Out is the unnormalized positive height gradient, encoded as Signed RG (default) or Packed RG. HeightToNormal uses the opposite XY sign. Connect to VectorDisplacement.Displacement with matching mode; Out is not UV.",
            ShaderGraphNodeKind.NormalToDistortion => "Read Signed XYZ Float3 Normal, keep XY without normalization, apply Float2 Strength and FlipX/Y. Float2 Out is Signed RG (default) or Packed RG displacement, not UV. HeightToNormal XY has the opposite sign from HeightToDistortion; reverse Strength or FlipX/Y as needed. Match VectorDisplacement mode; packed neutral is (0.5,0.5).",
            ShaderGraphNodeKind.ParallaxMapping => "Float2 Out is displaced UV. Inputs: Height, UV=current-pixel UV, Float3 ViewDirection=(0.5,0.5,1), Float Amplitude=0.05 in UV units. heightSource Texture (default) samples Height with channel/wrap/filter; Value selects channel from a Color/vector branch before evaluating scalar height at sample UV; scalar inputs pass through, with unconnected Height=0.5. One height sample: Out=UV+(height-0.5)*Amplitude*v.xy/(v.z+0.42), where v is normalized ViewDirection. Zero view or v.z<=0 returns unchanged UV. No depth-buffer output or implicit buffer. Native HLSL and SVM within nesting/work limits; hardware derivatives outside the sampled Height branch use HLSL fallback.",
            ShaderGraphNodeKind.ParallaxOcclusionMapping => "Float2 Out is displaced UV from a height-ray intersection. Same Height/UV/ViewDirection/Amplitude defaults and Texture/Value source settings as ParallaxMapping; Steps=16 is rounded and clamped to 1..64 as the minimum coarse count. Saturate sampled height to 0..1; search from height 1 to 0 with centered reference 0.5 and ray=Amplitude*v.xy/max(v.z,0.05), normalized v. The coarse count grows to max(Steps,ceil(64*saturate(length(ray)))); use 2 secant refinements when adaptation adds steps, otherwise 3. At most 68 Height evaluations. Zero view or v.z<=0 returns unchanged UV. Value reevaluates its Height branch at every sampled UV; use an explicit Buffer for costly sources. No depth-buffer output or implicit buffer. Native HLSL and SVM within nesting/work limits; hardware derivatives outside the sampled Height branch use HLSL fallback.",
            ShaderGraphNodeKind.Blur => "Reevaluate numeric/Color In at neighboring UVs with a normalized finite Gaussian kernel. Radius is in document pixels; sampling density is automatic, with half-extent max(1,ceil(abs(Radius))) and at least 3x3 samples. The option field is unused. Out retains the input type. All active components, including alpha, filter independently; Color produces Float4. No implicit clamp or intermediate GPU texture. Sample spacing stays at most one document pixel; larger radii increase cost quadratically.",
            ShaderGraphNodeKind.Sharpen => "Numeric/Color In, UV=current-pixel UV, Radius=1 document pixel and Strength=1 produce Out of the same type. Boolean node setting adaptiveSharpen=false preserves ordinary Out=input+(input-GaussianBlur)*Strength: sampling density is automatic with half-extent max(1,ceil(abs(Radius))) and at least 3x3 samples; spacing stays at most one document pixel, and output is unclamped. Bottom Adaptive=true uses nine samples in a fixed 3x3 neighborhood and clamps Radius/Strength below at zero. It compares the center with the four axial neighbors' mean; Strength/(1+Strength) is attenuated by local contrast and the correction clamps to the neighborhood's component min/max. All active components, including alpha, are processed independently; Color produces Float4. Adaptive is a setting, not an input port or new kind. Native HLSL and SVM for the adaptive path with sampled-branch budget fallback.",
            ShaderGraphNodeKind.EdgeDetection => "Fixed 3x3 Sobel gradient magnitude per component, multiplied by Strength. In is a numeric/Color branch reevaluated at neighboring UVs; Radius is in document pixels. Out retains the input type. Color preserves center alpha independently of Strength; numeric vectors compute every component. No implicit grayscale conversion, clamp or intermediate GPU texture.",
            ShaderGraphNodeKind.Dilate => "Component-wise maximum of numeric/Color In reevaluated at neighboring UVs. Radius is in document pixels; sampling density is automatic, with half-extent max(1,ceil(abs(Radius))) and at least 3x3 samples. The option field is unused. Type is retained and all components participate; no implicit clamp. Sample spacing stays at most one document pixel.",
            ShaderGraphNodeKind.Erode => "Component-wise minimum of numeric/Color In reevaluated at neighboring UVs. Radius is in document pixels; sampling density is automatic, with half-extent max(1,ceil(abs(Radius))) and at least 3x3 samples. The option field is unused. Type is retained and all components participate; no implicit clamp. Sample spacing stays at most one document pixel.",
            _ => null
        };

        private static string ShaderNodeNoiseDescription(string family) =>
            "WT scalar noise: Float Value in 0..1 without gamma conversion. noiseDimensions selects OneD/TwoD/ThreeD (default TwoD). Position defaults to UV.x/UV/float3(UV,value.z), with independently editable Z defaulting to 0; Position, Scale=8, Offset=0 and Period=1 use the selected width. noisePeriodic defaults false; Offset and Period are used only when enabled. Offset shifts Position in input units before Scale and periodic reduction; otherwise translate Position with Add. Nonpositive Period disables an axis. Seed=1337. Single noise evaluation: no embedded fractal or octave inputs; use the separate Fractal node for generic 2D branch repetition. Inactive inputs retain their values and edges. noiseFiltering defaults false; enabling it supplies unwrapped Position derivatives, including sampled branches; no implicit texture mapping. " + family;
    }
}
