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
            Keys(settings, "program", "presetId", "mode", "inputMode", "inputTexture", "inputLayerId", "draftGraph", "draftCode", "workingCode", "draftCodeParameters", "compile");
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
                (program.mode == ShaderProgramMode.Graph ? settings["draftGraph"] != null :
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
                Require(!HasFxProgramEdit(settings), "presetId supplies the complete program.");
                program = ShaderPresetLink.Load(FindShaderPreset(Text(settings, "presetId")));
            }
            else if (settings["program"] != null)
            {
                foreach (string field in new[] { "mode", "draftGraph", "draftCode", "workingCode", "draftCodeParameters" })
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
                if (settings["draftCodeParameters"] != null)
                {
                    Require(settings["draftCodeParameters"] is JArray, "draftCodeParameters must be an array.");
                    program.draftCodeParameters = WhimTexDocumentJson.ReadShaderCodeParameters((JArray)settings["draftCodeParameters"]);
                }
                if (settings["draftGraph"] != null || settings["draftCode"] != null || settings["workingCode"] != null)
                { program.presetId = null; program.presetVersion = 0; }
            }
            ResolveShaderProgramAliases(program, document, aliases);
            WhimTexDocumentJson.ValidateShaderProgramStorage(program);
            return program;
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
                if (kind is ShaderGraphNodeKind.SampleTexture or ShaderGraphNodeKind.SampleGradient)
                    entry["outputLabels"] = new JObject { ["Out"] = "RGBA" };
                var modeNames = ShaderGraphNodes.ModeNames(kind);
                if (modeNames != null)
                {
                    int defaultOption = node.option;
                    entry["modeLabel"] = ShaderGraphNodes.ModeLabel(kind);
                    entry["defaultOption"] = defaultOption;
                    var modes = new JArray();
                    bool dynamicInputs = kind is ShaderGraphNodeKind.Shape or ShaderGraphNodeKind.RotateVector or ShaderGraphNodeKind.BilateralBlur or ShaderGraphNodeKind.GaborNoise;
                    for (int option = 0; option < modeNames.Length; option++)
                    {
                        node.option = option;
                        var mode = new JObject { ["option"] = option, ["name"] = modeNames[option] };
                        if (dynamicInputs) mode["inputs"] = ShaderNodeInputs(node);
                        if (kind == ShaderGraphNodeKind.RotateVector) mode["outputTypes"] = ShaderNodeOutputTypes(node);
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
                if (ShaderGraphNodes.IsCaustics(kind))
                {
                    entry["sampledInputs"] = kind == ShaderGraphNodeKind.Caustics ? new JArray("Height") : new JArray();
                    entry["buffered"] = true;
                }
                if (settings.Count > 0) entry["settings"] = settings;
                if (ShaderGraphNodes.UsesHeightTexture(kind)) entry["dynamicInputs"] = true;
                if (ShaderGraphNodes.IsFilter(kind))
                {
                    entry["sampledInputs"] = ShaderNodeSampledInputs(node);
                    entry["outputTypeFromInput"] = "In";
                    if (kind is not ShaderGraphNodeKind.RadialBlur and not ShaderGraphNodeKind.SlopeBlur and
                        not ShaderGraphNodeKind.Buffer and not ShaderGraphNodeKind.Fractal) entry["radiusUnits"] = "DocumentPixels";
                }
                string description = ShaderNodeDescription(kind);
                if (description != null) entry["description"] = description;
                if (kind == ShaderGraphNodeKind.Constant)
                {
                    entry["constants"] = new JArray(System.Enum.GetNames(typeof(ShaderGraphConstant)));
                    entry["description"] = "constant selects PI (default), TAU, PHI, E or SQRT2. Out is Float. Arbitrary values belong to parameters or unconnected inputs.";
                }
                if (kind == ShaderGraphNodeKind.Swizzle)
                    entry["description"] = "swizzle is a 1–4 character mask (default xxxx): xyzw/rgba, case-insensitive, with _ inserting zero. Input is numeric/Color; its width is max(source width, highest mask component). Connected scalars repeat; shorter vectors gain zeros. Unconnected fields expose inferred components directly. Out is Float through Float4 by mask length. Example: float2 input with _x_y outputs float4(0,x,0,y).";
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
                if (ShaderGraphNodes.TryNoiseOutputType(node, port, out var type) ||
                    ShaderGraphNodes.TrySurfaceOutputType(node, port, out type) ||
                    ShaderGraphNodes.TryProceduralOutputType(node.kind, out type) ||
                    ShaderGraphNodes.TryColorOutputType(node.kind, out type) ||
                    ShaderGraphNodes.TryExtendedOutputType(node.kind, out type))
                    result[port] = type.ToString();
                else if (node.kind == ShaderGraphNodeKind.TransformUV)
                    result[port] = ShaderGraphValueType.Float2.ToString();
                else if (ShaderGraphNodes.IsCaustics(node.kind)) result[port] = ShaderGraphValueType.Float.ToString();
                else if (node.kind is ShaderGraphNodeKind.SampleTexture or ShaderGraphNodeKind.SampleGradient)
                    result[port] = (port == "Out" ? ShaderGraphValueType.Color : ShaderGraphValueType.Float).ToString();
                else if (node.kind == ShaderGraphNodeKind.SampleInput)
                    result[port] = ShaderGraphValueType.Color.ToString();
                else if (node.kind == ShaderGraphNodeKind.CanvasPosition || node.kind == ShaderGraphNodeKind.CanvasData)
                    result[port] = (port == "PixelScale" ? ShaderGraphValueType.Float : ShaderGraphValueType.Float2).ToString();
            }
            return result;
        }

        private static JArray ShaderNodeSampledInputs(ShaderGraphNode node) => node.kind switch
        {
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
                if (ShaderGraphNodes.IsNoise(node.kind))
                {
                    string reason = ShaderGraphNodes.NoiseInputInactiveReason(node, port.name);
                    input["active"] = reason == null;
                    if (reason != null) input["inactiveReason"] = reason;
                    if (port.name == "Position")
                        input["defaultExpression"] = ShaderGraphNodes.IsGrainNoise(node.kind) ? "UV" :
                            ShaderGraphNodes.NoiseDimensions(node) == 1 ? "UV.x" :
                            ShaderGraphNodes.NoiseDimensions(node) == 2 ? "UV" : "float3(UV, value.z)";
                }
                else if (node.kind == ShaderGraphNodeKind.Fractal)
                {
                    string reason = ShaderGraphNodes.InputInactiveReason(node, port.name);
                    input["active"] = reason == null;
                    if (reason != null) input["inactiveReason"] = reason;
                }
                inputs.Add(input);
            }
            return inputs;
        }

        private static JObject ShaderNodeSettings(ShaderGraphNode node)
        {
            var settings = new JObject();
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
            if (ShaderGraphNodes.IsCaustics(node.kind)) settings["wrap"] = ShaderNodeEnumSetting(node.wrap);
            if (node.kind == ShaderGraphNodeKind.ConvertColor)
            {
                settings["inputColorSpace"] = ShaderNodeEnumSetting(node.inputColorSpace, ShaderGraphNodes.ColorSpaceNames);
                settings["outputColorSpace"] = ShaderNodeEnumSetting(node.outputColorSpace, ShaderGraphNodes.ColorSpaceNames);
            }
            if (ShaderGraphNodes.UsesChannel(node.kind))
                settings["channel"] = ShaderNodeEnumSetting(node.channel, ShaderGraphNodes.ChannelNames);
            if (ShaderGraphNodes.UsesHeightTexture(node.kind))
            {
                var heightSource = ShaderNodeEnumSetting(node.heightSource);
                var original = node.heightSource;
                foreach (JObject value in (JArray)heightSource["values"])
                {
                    node.heightSource = (ShaderGraphHeightSource)System.Enum.Parse(typeof(ShaderGraphHeightSource), (string)value["value"]);
                    value["inputs"] = ShaderNodeInputs(node);
                        value["metricApplicable"] = node.noiseOutput != ShaderGraphNoiseOutput.DistanceToEdge;
                    value["radiusUnits"] = node.heightSource == ShaderGraphHeightSource.Texture ? "SourceTexels" : "DocumentPixels";
                }
                node.heightSource = original;
                settings["heightSource"] = heightSource;
                settings["derivative"] = ShaderNodeEnumSetting(node.derivative, ShaderGraphNodes.DerivativeNames);
                settings["wrap"] = ShaderNodeEnumSetting(node.wrap);
                settings["filter"] = ShaderNodeEnumSetting(node.filter);
            }
            if (ShaderGraphNodes.UsesNormalEncoding(node.kind))
            {
                var encoding = ShaderNodeEnumSetting(node.normalEncoding, ShaderGraphNodes.NormalEncodingNames);
                if (node.kind is ShaderGraphNodeKind.NormalBlend or ShaderGraphNodeKind.NormalToDistortion ||
                    ShaderGraphNodes.ExtendedUsesNormalEncoding(node.kind))
                {
                    var original = node.normalEncoding;
                    foreach (JObject value in (JArray)encoding["values"])
                    {
                        node.normalEncoding = (ShaderGraphNormalEncoding)System.Enum.Parse(typeof(ShaderGraphNormalEncoding), (string)value["value"]);
                        value["inputs"] = ShaderNodeInputs(node);
                        value["metricApplicable"] = node.noiseOutput != ShaderGraphNoiseOutput.DistanceToEdge;
                    }
                    node.normalEncoding = original;
                }
                settings["normalEncoding"] = encoding;
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
            ShaderGraphNodeKind.CausticsNoise => "Fixed 2D water caustics from a two-octave WT Flow height field. Position defaults to UV; translate it with Add before this node. Scale=(4,4), Period=1, Seed=1337, Phase=0 in cycles. Shared Caustics inputs: UV samples the completed image, Strength=0.02, Depth=1, IOR=1.333, LightDirection=(0,0,-1), Softness=1 receiver pixel, Intensity=0.25. Value is nonnegative HDR irradiance, with uniform light for a flat surface. Custom 512x512/Repeat by default; bilinear sampling. Graph-managed height and refraction/projection passes, bounded 16..512 ray grid, cached like Buffer. Not a standalone pointwise HLSL noise function; Fractal after this node requires literal SeedStep=0.",
            ShaderGraphNodeKind.Caustics => "Fixed 2D caustics from scalar Height. Height*Strength is baked once, then neighboring texels determine refraction onto a flat receiver; finite elliptical light footprints accumulate HDR Value. UV samples the completed image. Strength=0.02, Depth=1, IOR=1.333 (minimum 1), LightDirection=(0,0,-1), Softness=1 receiver pixel, Intensity=0.25. Flat height gives uniform light. Custom 512x512/Repeat defaults, bilinear sampling; height/ray grid is bounded to 16..512 per axis. Repeat needs smoothly periodic input. Same cache and Fractal SeedStep=0 boundary as Buffer; put Fractal upstream to change the source surface. No generated pixels are serialized.",
            ShaderGraphNodeKind.Buffer => "Render numeric/Color In into a reusable transient linear float32 RGBA GPU texture, then sample it at Float2 UV (implicit current-pixel UV). Out retains In's type. bufferResolution: Canvas (default, current render-target size including node previews) or Custom (exact bufferSize, default [512,512]); wrap/filter select sampling behavior. Bound material data/time/index/transforms/static texture changes invalidate contents. External RenderTexture inputs or CustomFunction code conservatively refresh on every owner render; allocation is reused and each buffer executes once per draw in dependency order. No per-pixel/per-octave allocation or document pixel serialization. Buffer before Fractal requires an unconnected literal SeedStep=0; Buffer after Fractal is allowed.",
            ShaderGraphNodeKind.Fractal => "Reevaluate numeric/Color In over the graph's 2D domain; Out retains In's type. FBm/Ridged/PingPong use component-wise normalized weighted accumulation, including Color alpha. Multifractal, HybridMultifractal, HeteroTerrain and RidgedMultifractal use signed s=2*In-1 and raw unnormalized cascades, retaining HDR/negative results. Bias=1 shifts the signed signal in Hybrid/Hetero and the ridge level in RidgedMultifractal; Feedback=2 (minimum 0) controls inter-octave feedback in Hybrid/RidgedMultifractal. Gain remains octave amplitude decay. WeightedStrength/PingPongStrength are unused by these four modes. Mode-specific unused ports are hidden unless connected; values and edges survive mode switches. FBm (default) retains signed/HDR values; Ridged applies 1-abs(2*v-1); PingPong applies 1-abs(frac(v*max(PingPongStrength,0))*2-1). None is unsupported. Inputs: Octaves=3 (integer, 1..8), Lacunarity=2 (0.01..16), Gain=0.5 and WeightedStrength=0 (0..1), PingPongStrength=2 (only PingPong), SeedStep=1 (integer conversion). noiseFiltering=true attenuates octaves using the local UV pixel footprint; upstream WT filtering remains independent. SeedStep offsets all upstream noise-node seeds including grain, not arbitrary CustomFunction seed parameters. This is generic branch repetition, not exact per-noise WT fractals; the public WT HLSL API is unchanged. Source branches containing Buffer require an unconnected literal SeedStep=0; connected SeedStep is conservatively rejected even at zero. Buffer after Fractal is allowed.",
            ShaderGraphNodeKind.CanvasPosition => "Document-space position, independent of layer/group transforms. Float2 outputs: Normalized (0..1 over the canvas), AspectX = Normalized * (Width/Height, 1), AspectY = Normalized * (1, Height/Width). All share the (0,0) origin and remain unclamped when sampling outside the canvas. No inputs.",
            ShaderGraphNodeKind.CanvasData => "Canvas Size (Float2 pixels), Texel (Float2 reciprocal dimensions) and PixelScale (Float document pixel multiplier configured by the Frame tool). Values are independent of preview resolution and camera zoom. No inputs or node preview.",
            ShaderGraphNodeKind.SampleInput => "Read the Shader layer's shared Input (None, Texture, Layer or CompositeBelow). UV defaults to local procedural UV; Out is Color. wrap/filter match Sample Texture. None or a missing source is transparent black. This reads an image without replacing the lower composition or changing ordinary blending.",
            ShaderGraphNodeKind.Output => "RGBA is a straight Color input; numeric RGB gains alpha 1. Float AlphaScale (Alpha Scale in the UI) defaults to 1 and multiplies RGBA.a, leaving RGB unchanged. Accepts a literal or connected mask. No implicit clamp; no outputs or node preview.",
            ShaderGraphNodeKind.HeightBlend => "Blend signed/HDR scalar Bottom=0 and Top=1 by height, with Offset=0, Smoothness=0.1 and Opacity=1. Outputs Float Height and Mask; there is no Out. Offset shifts the top height, nonnegative Smoothness softens the height comparison and saturated Opacity controls the top contribution. At zero Smoothness the higher height wins, with weight 0.5 for a tie. Height is not clamped to 0..1.",
            ShaderGraphNodeKind.TileUV => "Grid (option 0) or Staggered (1) cell-local UV with Count=(4,4), Offset=(0,0) in cells, RowOffset=0.5 active only in Staggered, RandomRotation=0 degrees, RandomScale=0 (clamped 0..1), Seed=1337. Outputs UV, Cell and Center as Float2, Random and Mask as Float; there is no Out. Cell identifies the original cell; Center is in UV coordinates. Random transformations stay in their own cell: Mask clips transformed motifs, with no cross-cell overlap.",
            ShaderGraphNodeKind.PerspectiveUV => "Inverse projective mapping from a quadrilateral to unit-square source UV. Defaults BottomLeft=(0,0), BottomRight=(1,0), TopRight=(1,1), TopLeft=(0,1). Outputs Float2 UV and Float Mask, not Out. Mask selects the quad; invalid, degenerate or nonconvex quads return zero UV and zero Mask. Multiply sampled alpha by Mask for coverage.",
            ShaderGraphNodeKind.RotateVector => "Rotate In by Angle degrees (default 0). option 0=2D uses Float2 In=(1,0) and Float2 Out; option 1=3D uses Float3 In=(1,0,0), Axis=(0,0,1) and Float3 Out. The 3D axis is normalized; a zero Axis preserves In. Vector magnitude is retained. Read modes[].inputs and modes[].outputTypes for the selected mode.",
            ShaderGraphNodeKind.Blackbody => "Temperature=6500 Kelvin, clamped to 1000..40000, produces Color Out with normalized linear RGB and alpha 1. Planck/CIE 1931 colors are approximated by 16 generated cubic Hermite segments in logarithmic temperature, selected by four binary branch decisions without a lookup texture. Negative RGB is clipped and the largest component normalized to 1. Temperature controls chromaticity; set brightness separately with Multiply or Exposure.",
            ShaderGraphNodeKind.SlopeBlur => "Reevaluate numeric/Color In along the negative, unnormalized gradient of scalar Slope=0, using central differences one document pixel apart on each side. Strength=8 scales document-pixel travel per unit UV-height slope; negative Strength reverses the path. Samples=16 rounds/clamps to 1..64 integration steps, plus the starting sample. option 0=Average, 1=Min, 2=Max. Out retains In's type; Average filters Color in premultiplied RGBA, while Min/Max process components independently. Constant Slope or zero Strength preserves In; a path with no movement returns the original evaluation, including its antialiasing and transparent RGB. No Radius input or radiusUnits; both In and Slope are sampled branches. Repeated slope and input evaluations can be expensive, especially with nested filters or noise.",
            ShaderGraphNodeKind.BilateralBlur => "Soft edge-aware Gaussian filtering of numeric/Color In. Radius=2 uses document pixels; Tolerance=0.1 is nonnegative, with zero accepting exact guide matches only. option 0=Source guides by In; option 1=Custom adds dynamic numeric/Color Guide, default scalar 0, reevaluated with In. Color guides in either mode compare premultiplied RGBA to ignore hidden RGB; numeric guides retain their component values. Out retains In's type independently of Guide; Color filtering is premultiplied and returns straight RGBA. Zero Radius preserves In; results are not clamped. Larger radii and nested sampled branches increase cost.",
            ShaderGraphNodeKind.PixelateUV => "Float2 Out snaps UV to cell centers in document pixels: (floor((UV*CanvasSize-Offset)/size)+0.5)*size+Offset, divided by CanvasSize. CellSize defaults to (8,8), uses absolute values with a 1-pixel minimum; Offset defaults to (0,0) pixels. Coordinates remain unclamped. Connect before a texture or procedural UV input.",
            ShaderGraphNodeKind.ChannelMixer => "Color Out uses dot(Color.rgb, Red/Green/Blue) for each RGB channel and retains alpha. Float3 coefficient rows default to the identity matrix. Values are weights (1 means 100%), may be negative, and are not normalized or clamped; HDR is retained.",
            ShaderGraphNodeKind.WhiteBalance => "Bradford chromatic adaptation of linear RGB with neutral Temperature=0 and Tint=0. Both inputs clamp to -1..1; positive Temperature warms, negative cools; positive Tint adds magenta, negative green. Output remains Color with original alpha, signed RGB and HDR; no output clamp.",
            ShaderGraphNodeKind.DirectionalBlur => "Gaussian-weighted sampling of numeric/Color In along a line at Angle degrees, over +/- Radius document pixels. Defaults Radius=8, Angle=0, Samples=17. Samples rounds upward to an odd count from 3 to 129; increase it for longer paths or fine detail. Out retains the input type; Color uses premultiplied filtering with straight RGBA output. Radius=0 is identity; no result clamp or intermediate texture.",
            ShaderGraphNodeKind.RadialBlur => "Gaussian-weighted sampling of numeric/Color In around Center=(0.5,0.5). Spin (option 0) samples +/- Angle degrees (default 10) in document-pixel space; Zoom (option 1) samples radius factors 1 +/- abs(Strength), default 0.1. The unused Angle/Strength input stays visible but inactive. Samples=17 rounds upward to an odd count from 3 to 129. Color filters premultiplied; numeric vectors filter per component. Zero amount and the center preserve the input; no clamp or intermediate texture.",
            ShaderGraphNodeKind.Sign => "Component-wise sign of numeric/Color In: -1, 0 or 1. Color participates as Float4; Out is numeric with the inferred input width.",
            ShaderGraphNodeKind.Saturate => "Clamp numeric/Color In component-wise to 0..1. Color participates as Float4; Out is numeric with the inferred input width.",
            ShaderGraphNodeKind.Modulo => "Floor modulo A - B*floor(A/B), component-wise at the widest numeric input width. Nonzero results have the divisor's sign; B=0 is undefined. Defaults A=0, B=1.",
            ShaderGraphNodeKind.Atan2 => "Component-wise atan2(Y,X) in radians, at the widest numeric input width. Defaults Y=0, X=1; (0,0) is undefined.",
            ShaderGraphNodeKind.Asin => "Component-wise inverse sine of In, in radians. Input domain -1..1 is not implicitly clamped. Out uses the inferred numeric width.",
            ShaderGraphNodeKind.Acos => "Component-wise inverse cosine of In, in radians. Input domain -1..1 is not implicitly clamped. Out uses the inferred numeric width.",
            ShaderGraphNodeKind.Exp => "Component-wise natural exponential exp(In), with inferred numeric output width and no implicit clamp.",
            ShaderGraphNodeKind.Log => "Component-wise natural logarithm log(In), default In=1. Requires positive input; the domain is not implicitly clamped. Out uses the inferred numeric width.",
            ShaderGraphNodeKind.Cross => "Float3 Out = cross(A,B), with Float3 A=(1,0,0) and B=(0,1,0) by default. Does not normalize inputs or output.",
            ShaderGraphNodeKind.Reflect => "Reflect Incident around Normal using the widest numeric input width. Inputs are not normalized automatically; supply a unit Normal. Defaults Incident=-1, Normal=1.",
            ShaderGraphNodeKind.Refract => "Refract Incident through Normal using scalar Eta (default 1). Supply unit Incident/Normal; no automatic normalization. Out uses their widest numeric width; total internal reflection returns zero.",
            ShaderGraphNodeKind.And => "Boolean Out = A && B. Both inputs default false; numeric/resource inputs are not Boolean conditions.",
            ShaderGraphNodeKind.Or => "Boolean Out = A || B. Both inputs default false; numeric/resource inputs are not Boolean conditions.",
            ShaderGraphNodeKind.Not => "Boolean Out = !In. In defaults false.",
            ShaderGraphNodeKind.NormalEncode => "Float3 Normal (default signed flat 0,0,1) to packed Float3 Out = Normal*0.5+0.5. No normalization, clamp, color conversion or normalEncoding setting.",
            ShaderGraphNodeKind.NormalDecode => "Float3 Normal (default packed flat 0.5,0.5,1) to signed Float3 Out = Normal*2-1. No normalization, clamp, color conversion or normalEncoding setting.",
            ShaderGraphNodeKind.NormalStrength => "Decode and normalize Float3 Normal, scale XY by Strength and blend Z from 1 toward the unit normal's Z using saturate(abs(Strength)), then normalize and reencode. Strength 0 is flat, 1 preserves the unit normal, negative values reverse XY. normalEncoding selects PackedRGB (default) or SignedXYZ.",
            ShaderGraphNodeKind.NormalLighting => "option Local=0 (default) uses LightDirection/LightColor/Intensity and optional SpecularOnly. Global=1 sums document lights, uses UV mapped into canvas pixels and Height (pixels), and retains Color/Ambient/SpecularColor/SpecularIntensity/Roughness. normalEncoding: PackedRGB or SignedXYZ. Fixed view +Z; source alpha and HDR RGB are preserved. Ambient is applied once.",
            ShaderGraphNodeKind.LightCount => "Number of document light slots, including disabled sources. Order matches document snapshot lights[].index.",
            ShaderGraphNodeKind.LightDirection => "Normalized surface-to-light direction for Index. UV maps through canvas coordinates; Height is in document pixels. Invalid indices return +Z.",
            ShaderGraphNodeKind.LightColor => "Linear HDR RGB times source intensity for Index, before attenuation. Disabled/invalid sources return zero. Multiply by LightAttenuation for incident radiance.",
            ShaderGraphNodeKind.LightAttenuation => "Directional=1; Point=pow(saturate(1-distance/range),falloff). UV and Height locate the surface in full canvas pixels. Invalid indices return zero.",
            ShaderGraphNodeKind.Premultiply => "Color Out.rgb = Color.rgb*Color.a; alpha is unchanged. No implicit clamp. Output.RGBA expects straight color, so unpremultiply before Output when necessary.",
            ShaderGraphNodeKind.Unpremultiply => "Color Out.rgb = Color.rgb/Color.a for every nonzero alpha, including small or negative alpha. Exactly zero alpha returns zero RGB. Alpha is unchanged; no epsilon cutoff or implicit clamp.",
            ShaderGraphNodeKind.Flipbook => "Float2 atlas UV from UV, Columns=4, Rows=4 and Frame=0. Dimensions floor/clamp to 1..4096; floored Frame wraps, including negative values. option 0 is Bottom to Top (frame 0 bottom-left), 1 Top to Bottom. Local UV orientation is unchanged; no implicit clamp or sampling.",
            ShaderGraphNodeKind.Dither => "Color Out quantizes RGB with an ordered Bayer pattern; alpha is unchanged and signed/HDR RGB is not clipped. Levels=8 rounds to at least 2; PixelSize=1 uses abs with minimum 1 document pixel. UV defaults to current-pixel UV. option 0/1/2 selects Bayer 4x4/2x2/8x8. Pattern size follows document dimensions, independently of preview size.",
            ShaderGraphNodeKind.Unlerp => "Component-wise (T-A)/(B-A), without clamping to 0..1. Defaults A=0, B=1, T=0. Out uses the widest numeric input width; Color participates as Float4. Equal bounds are undefined.",
            ShaderGraphNodeKind.RangeMask => "Component-wise range mask in 0..1. In is dynamic Float-Float4 (Color as Float4); Out follows In. Scalar Center=0.5, Width=0.2, Feather=0. Width is the full interval including inward transitions; Feather clamps to 0..Width/2. option selects Falloff: 0 Linear (default), 1 Smooth. Zero Feather includes both hard boundaries. Width<=0 is empty. In/Center retain signed and HDR values; no luminance or color conversion.",
            ShaderGraphNodeKind.InstanceIndex => "Float Out = Start + (float)_WhimTexInstanceIndex * Scale; Float inputs default to Start=0 and Scale=1. The shared uint index is cast with ordinary floating-point precision. Standard rendering currently supplies index 0, so Out equals Start; this is unrelated to Repeat.Cell or CellularNoise.CellValue.",
            ShaderGraphNodeKind.Time => "Float Out = Start + _WhimTexTime * Scale; Float inputs default to Start=0 and Scale=1. Graph-window node and result previews receive shared Editor seconds, including result-preview FX. Other renders use time 0, so Out equals Start. No own preview.",
            ShaderGraphNodeKind.PerlinNoise => ShaderNodeNoiseDescription("Perlin gradient noise. Gradient and 2D/3D Curl retain signed analytic derivatives and magnitude."),
            ShaderGraphNodeKind.ValueNoise => ShaderNodeNoiseDescription("Interpolated lattice values. Gradient and 2D/3D Curl retain signed analytic derivatives and magnitude."),
            ShaderGraphNodeKind.ValueCubicNoise => ShaderNodeNoiseDescription("Cubic-interpolated lattice values. Gradient and 2D/3D Curl retain signed analytic derivatives and magnitude."),
            ShaderGraphNodeKind.OpenSimplex2Noise => ShaderNodeNoiseDescription("OpenSimplex2 field. Gradient and 2D/3D Curl retain signed analytic derivatives and magnitude."),
            ShaderGraphNodeKind.OpenSimplex2SNoise => ShaderNodeNoiseDescription("Smooth OpenSimplex2S field. Gradient and 2D/3D Curl retain signed analytic derivatives and magnitude."),
            ShaderGraphNodeKind.FlowNoise => ShaderNodeNoiseDescription("Rotating OpenSimplex2S gradients; Phase=0 in cycles, with 0 and 1 equivalent. Gradient and 2D/3D Curl retain signed analytic derivatives and magnitude."),
            ShaderGraphNodeKind.CellularNoise => ShaderNodeNoiseDescription("Value uses noiseOutput (default F1): CellValue, F1, F2, Add, Subtract, Multiply, Divide or DistanceToEdge. Smoothness=0 preserves hard results; 0..1 smooths every Value mode, blending cell values or distances as appropriate. noiseMetric: Euclidean, EuclideanSquared, Manhattan or Hybrid; ignored only for DistanceToEdge, which uses Euclidean geometry. noiseDistribution: Uniform or FixedRadius. Jitter=1; Evolution=0 is signed unclamped bounded feature motion. Extra Float F1/F2/CellValue and dimension-width ToPoint expose hard, unfiltered first-octave geometry in the effective metric; no uint ID port."),
            ShaderGraphNodeKind.TriangleVoronoiNoise => "WT scalar noise, fixed 2D: noiseDimensions supports only TwoD; no Dimensions selector. Float Value is saturate(edgeDistance/sqrt(2)) before filtering, in 0..1 without gamma conversion. Float2 Position defaults to UV; Scale=(8,8), Period=(0,0), Seed=1337. Translate Position with Add before the noise node; nonpositive Period disables an axis. noiseDistribution: Uniform (default) or FixedRadius; Jitter=1 clamps to 0..1; Float Evolution=0 moves shared feature points, retaining spatial periods; Float2 Stretch=(1,1) uses absolute components clamped to 0.5..2. Uses WT Cellular feature points, selecting the nearest with a triangular gauge; no Metric or Output selector. Float Distance is Euclidean length to that feature, not the gauge or boundary distance; Float CellValue is its repeatable value, Float2 ToPoint points to it, and Float2 BorderNormal is a unit direction toward the nearest dominance boundary or zero at a degenerate boundary. Auxiliary geometry uses unstretched noise-lattice coordinates and is unfiltered first-octave data. Coincident affine-face equality plateaus use deterministic cell ties; plateau interiors are not borders. No raw edgeDistance, uint ID or Gradient/Curl ports. Single noise evaluation: no embedded fractal or octave inputs; use the separate Fractal node for generic 2D branch repetition. noiseFiltering=true supplies unwrapped Position derivatives, including sampled branches, and uses an approximate cell-family minification mean, not the exact tile mean. No alias-free guarantee or implicit texture mapping.",
            ShaderGraphNodeKind.GaborNoise => ShaderNodeNoiseDescription("Directional Gaussian-windowed sparse convolution. Frequency=2, Bandwidth=1, Anisotropy=0.8, Direction=(1,0,0) at the selected width. Float Phase/Intensity/Phasor share the first octave. option selects the Phasor Profile: 0 Sine (default), 1 Triangle, 2 Pulse. PhaseOffset=0 cycles and Width=0.5 (Pulse only, clamped 0..1) affect only Phasor; unused branches are not evaluated by other outputs. Width is hidden unless Pulse or connected; values and edges survive profile changes. Phasor uses analytic phase footprints and fades unresolved bands toward 0.5 (Sine/Triangle) or Width (Pulse); filtering is approximate with locally constant settings. Phase singularities are retained, with fading near signal zeros inside a pixel. Existing Value/Phase/Intensity and signed Gradient/Curl meanings are unchanged. Sampled filters multiply its evaluation cost."),
            ShaderGraphNodeKind.SpotsNoise => ShaderNodeNoiseDescription("Soft positive spots: Density=1, Radius=0.35, Softness=0.5, Elongation=1, Direction=(1,0,0) at the selected width."),
            ShaderGraphNodeKind.ShardNoise => ShaderNodeNoiseDescription("Faceted scalar noise: Sharpness=0.7 (0..1) controls soft slopes through sharp shards. Value is 0..1; use the separate Fractal node for octaves."),
            ShaderGraphNodeKind.WhiteNoise or ShaderGraphNodeKind.BlueNoise => "WT pixel grain. noiseDimensions: OneD or TwoD (default); Position remains Float2 with implicit UV. Translate Position with Add in UV units; divide a pixel displacement by ImageSize first. GrainSize=1 pixel, Direction=0 degrees, Seed=1337. Float2 ImageSize defaults to CanvasSize. noiseInverted defaults false. Float Value and Color (alpha 1) are emitted independently with the library color flag; Color has independent RGB. No fractal, Scale, Period or scalar-noise filtering controls.",
            ShaderGraphNodeKind.OpenSimplex2WarpNoise or ShaderGraphNodeKind.OpenSimplex2ReducedWarpNoise or ShaderGraphNodeKind.BasicGridWarpNoise => "WT vector displacement using the named warp basis. noiseDimensions selects OneD/TwoD/ThreeD (default TwoD), with matching Position/Scale/Period and output widths. Position defaults to UV.x/UV/float3(UV,value.z), with independently editable Z defaulting to 0. Translate Position with Add before this node. Scale=8 per axis, Period=0 (nonpositive disables that axis), Seed=1337, Strength=0.1. fractal: None (one octave), Progressive (warp each next octave) or Independent (sample original position); Octaves=3, Lacunarity=2, Gain=0.5. No WeightedStrength/PingPongStrength. noiseFiltering=true supplies unwrapped Position derivatives, including sampled branches. Displacement is signed and unnormalized; Position is input Position + Displacement. No implicit texture mapping.",
            ShaderGraphNodeKind.Random => "Deterministic Float Out in [0,1) from Float2 Coordinates and Seed. With no edge or explicit input literal, Coordinates defaults to current-pixel UV. Explicit Custom constants are retained. Connect Repeat.Cell for one value per cell.",
            ShaderGraphNodeKind.Checkerboard => "Checkerboard pattern with smoothed edges. Out is a Float mask.",
            ShaderGraphNodeKind.Stripes => "Antialiased Float mask with Square, Triangle or Sine profile. Width affects Square only. Angle is degrees; Phase is cycles. Connect Time to Phase for graph-preview animation.",
            ShaderGraphNodeKind.Grid => "Grid pattern with smoothed edges. Out is a Float mask.",
            ShaderGraphNodeKind.Rings => "Antialiased concentric Float mask around Center. Frequency is cycles per UV distance, Width is the filled fraction, Phase is cycles. Connect Time to Phase for graph-preview animation.",
            ShaderGraphNodeKind.GradientFactor => "Linear, Radial or Angular Float coordinate. Connect Out to SampleGradient.T and a Gradient parameter to SampleGradient.Gradient. Angle is degrees. Linear/Radial can exceed 0..1; Angular wraps to turns.",
            ShaderGraphNodeKind.Shape => "Float Out is a smoothed mask; Float Distance is signed, negative inside. Ellipse distance is approximate. Inputs depend on option: use modes[].inputs. Capsule uses Center/Size/Rotation; Line Segment uses Start/End/Width. Rotation is degrees.",
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
            ShaderGraphNodeKind.Levels => "Remap InputRange through Gamma into OutputRange using Luminance (default) or RGB mode. Clamp defaults to true and bounds the input to InputRange; false permits signed-gamma extrapolation. Color Out preserves alpha.",
            ShaderGraphNodeKind.ColorBalance => "Adjust Shadows, Midtones and Highlights with Float3 offsets and ShadowRange/HighlightRange weights. PreserveLuminance defaults to true. Color Out preserves alpha and has no 0..1 RGB clamp.",
            ShaderGraphNodeKind.Posterize => "Quantize each RGB channel with floor(Levels), at least 2, including endpoints 0 and 1. Levels defaults to 8. Color Out preserves alpha; RGB outside 0..1 is not clipped.",
            ShaderGraphNodeKind.ConvertColor => "Explicit conversion between inputColorSpace and outputColorSpace; defaults LinearRGB to SRGB. LinearRGB/SRGB/Oklab retain signed/HDR values. HSV/HSL use sRGB-encoded RGB and hue in turns: HSV clamps negative RGB to 0 but retains HDR V; HSL clamps RGB, L and S to 0..1 and cannot roundtrip HDR/negative values. Same-space conversion is exact. Color Out preserves alpha; wires never convert spaces implicitly.",
            ShaderGraphNodeKind.Luminance => "Float Out = dot(linear Color.rgb, (0.2126,0.7152,0.0722)), independent of alpha. No 0..1 clamp; explicitly convert encoded input to LinearRGB first.",
            ShaderGraphNodeKind.GradientMap => "Sample required Gradient using the selected channel of Color. Color Out uses gradient RGB; option selects alpha Preserve (default), Multiply or Replace. Gradient wrap controls the sampling coordinate.",
            ShaderGraphNodeKind.ColorCurves => "Optional Master, Red, Green, Blue and Alpha Curve inputs are identity unless connected or assigned an inline curve. Master mode is RGB (default) or Luminance. For each curve use x + curve(saturate(x)) - saturate(x), retaining HDR/negative values at identity. Alpha changes only through its own curve. Out is Color.",
            ShaderGraphNodeKind.ColorMask => "Float Out is 1 for matching KeyColor, with Tolerance and Softness controlling selection. Chroma (default) and RGB compare sRGB-encoded RGB without SDR clipping; Perceptual compares Oklab. Alpha does not multiply the mask.",
            ShaderGraphNodeKind.HueRange => "Float Out selects a cyclic hue Range in degrees (default 330,30), with Feather (default 15 degrees) and SaturationRange (default 0.05,0.1). Span >=360 is full; equal endpoints are empty unless Feather>0. Alpha is not applied to the mask.",
            ShaderGraphNodeKind.MixColor => "Interpolate A/B in Linear RGB, sRGB or Perceptual (default) space. Color Out has interpolated RGB and alpha; T is unbounded, default 0.5. This is color interpolation, not alpha compositing.",
            ShaderGraphNodeKind.SetAlpha => "Color Out retains RGB and replaces (default) or multiplies alpha by Alpha (default 1), without implicit clamping.",
            ShaderGraphNodeKind.HeightToNormal => "heightSource selects Texture (default, required resource) or Value (Float Height branch). Texture samples neighboring bound-source texels using channel/wrap/filter; Value reevaluates the branch at neighboring UVs with Radius in document pixels. Both retain derivative, Strength and FlipX/Y. Float3 Out encodes normalize(-dx,-dy,1) as PackedRGB (default) or SignedXYZ. See settings.heightSource.values[].inputs.",
            ShaderGraphNodeKind.NormalBlend => "Blend Float3 Base and Detail normals using Reoriented (default) or Whiteout and Strength. normalEncoding selects PackedRGB (default) or SignedXYZ for both inputs and normalized Float3 Out. Encoding-specific neutral defaults are listed in settings.normalEncoding.values[].inputs.",
            ShaderGraphNodeKind.HeightToDistortion => "heightSource selects Texture (default, source-texel Radius, channel/wrap/filter) or Value (Float Height branch reevaluated at neighboring UVs, document-pixel Radius). Both use derivative, Strength and FlipX/Y. Float2 Out is the unnormalized positive height gradient, encoded as Signed RG (default) or Packed RG. HeightToNormal uses the opposite XY sign. Connect to VectorDisplacement.Displacement with matching mode; Out is not UV.",
            ShaderGraphNodeKind.NormalToDistortion => "Decode Float3 Normal using normalEncoding, keep XY without normalization, apply Float2 Strength and FlipX/Y. Float2 Out is Signed RG (default) or Packed RG displacement, not UV. HeightToNormal XY has the opposite sign from HeightToDistortion; reverse Strength or FlipX/Y as needed. Match VectorDisplacement mode; packed neutral is (0.5,0.5).",
            ShaderGraphNodeKind.Blur => "Reevaluate numeric/Color In at neighboring UVs with a normalized finite Gaussian kernel. Radius is in document pixels; option 0/1/2 selects a minimum 3x3/5x5/9x9 kernel. Out retains the input type. Color uses premultiplied filtering and returns straight RGBA; numeric vectors filter every component. No implicit clamp or intermediate GPU texture. Sample spacing stays at most one document pixel; larger radii increase cost quadratically.",
            ShaderGraphNodeKind.Sharpen => "Out = input + (input - Gaussian blur) * Strength. In is a numeric/Color branch reevaluated at neighboring UVs. Radius is in document pixels; option 0/1/2 selects a minimum 3x3/5x5/9x9 kernel. Color sharpens RGB and retains center alpha; numeric vectors process every component. No implicit clamp. Sample spacing stays at most one document pixel.",
            ShaderGraphNodeKind.EdgeDetection => "Fixed 3x3 Sobel gradient magnitude per component, multiplied by Strength. In is a numeric/Color branch reevaluated at neighboring UVs; Radius is in document pixels. Out retains the input type. Color preserves center alpha independently of Strength; numeric vectors compute every component. No implicit grayscale conversion, clamp or intermediate GPU texture.",
            ShaderGraphNodeKind.Dilate => "Component-wise maximum of numeric/Color In reevaluated at neighboring UVs. Radius is in document pixels; option 0/1/2 selects a minimum 3x3/5x5/9x9 kernel. Type is retained and all components participate; no implicit clamp. Sample spacing stays at most one document pixel.",
            ShaderGraphNodeKind.Erode => "Component-wise minimum of numeric/Color In reevaluated at neighboring UVs. Radius is in document pixels; option 0/1/2 selects a minimum 3x3/5x5/9x9 kernel. Type is retained and all components participate; no implicit clamp. Sample spacing stays at most one document pixel.",
            _ => null
        };

        private static string ShaderNodeNoiseDescription(string family) =>
            "WT scalar noise: Float Value in 0..1 without gamma conversion. noiseDimensions selects OneD/TwoD/ThreeD (default TwoD). Position defaults to UV.x/UV/float3(UV,value.z), with independently editable Z defaulting to 0; Position, Scale=8 and Period=0 use the selected width. Translate Position with Add before the noise node; nonpositive Period disables an axis. Seed=1337. Single noise evaluation: no embedded fractal or octave inputs; use the separate Fractal node for generic 2D branch repetition. Inactive inputs retain their values and edges. noiseFiltering=true supplies unwrapped Position derivatives, including sampled branches; no implicit texture mapping. " + family;
    }
}
