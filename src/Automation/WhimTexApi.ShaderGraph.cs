using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static JObject ShaderSnapshot(ShaderLayerBehaviour shader, WhimTexDocument document)
            => new JObject { ["graph"] = WhimTexDocumentJson.ShaderGraphSnapshot(shader.graph, document) };

        private static void SetShader(ShaderLayerBehaviour shader, JObject settings)
        {
            Keys(settings, "graph");
            if (settings["graph"] == null) return;
            // A complete replacement keeps IDs and wiring atomic; validation precedes mutation.
            // Function definitions and their plain-text source archive use the same codec;
            // loading a graph never refreshes its embedded backups from sourcePath.
            var graph = WhimTexDocumentJson.ReadShaderGraph(Obj(settings["graph"], "shader.graph"));
            shader.graph = graph;
            shader.InvalidateGraph();
        }

        private static JArray ShaderNodeCatalog()
        {
            var result = new JArray();
            foreach (var kind in ShaderGraphNodes.Kinds)
            {
                var node = ShaderGraphNodes.Create(kind, default);
                var entry = new JObject
                {
                    ["kind"] = kind.ToString(),
                    ["category"] = ShaderGraphNodes.Category(kind),
                    ["inputs"] = ShaderNodeInputs(node),
                    ["outputs"] = new JArray(ShaderGraphNodes.Outputs(node))
                };
                if (ShaderGraphNodes.TryProceduralOutputType(kind, out var outputType) || kind == ShaderGraphNodeKind.TransformUV)
                {
                    if (kind == ShaderGraphNodeKind.TransformUV) outputType = ShaderGraphValueType.Float2;
                    var outputTypes = new JObject();
                    foreach (string port in ShaderGraphNodes.Outputs(node)) outputTypes[port] = outputType.ToString();
                    entry["outputTypes"] = outputTypes;
                }
                var modeNames = ShaderGraphNodes.ModeNames(kind);
                if (modeNames != null)
                {
                    int defaultOption = node.option;
                    entry["modeLabel"] = ShaderGraphNodes.ModeLabel(kind);
                    entry["defaultOption"] = defaultOption;
                    var modes = new JArray();
                    for (int option = 0; option < modeNames.Length; option++)
                    {
                        var mode = new JObject { ["option"] = option, ["name"] = modeNames[option] };
                        if (kind == ShaderGraphNodeKind.Shape)
                        {
                            node.option = option;
                            mode["inputs"] = ShaderNodeInputs(node);
                        }
                        modes.Add(mode);
                    }
                    node.option = defaultOption;
                    entry["modes"] = modes;
                    if (kind == ShaderGraphNodeKind.Shape) entry["dynamicInputs"] = true;
                }
                if (kind is ShaderGraphNodeKind.Noise or ShaderGraphNodeKind.DomainWarp)
                {
                    var fractals = kind == ShaderGraphNodeKind.Noise
                        ? new[] { ShaderGraphFractal.None, ShaderGraphFractal.FBm, ShaderGraphFractal.Ridged, ShaderGraphFractal.PingPong }
                        : new[] { ShaderGraphFractal.None, ShaderGraphFractal.Progressive, ShaderGraphFractal.Independent };
                    var fractalModes = new JArray();
                    foreach (var fractal in fractals) fractalModes.Add(fractal.ToString());
                    entry["fractalModes"] = fractalModes;
                    entry["defaultFractal"] = node.fractal.ToString();
                }
                string description = ShaderNodeDescription(kind);
                if (description != null) entry["description"] = description;
                if (kind is ShaderGraphNodeKind.InstanceIndex or ShaderGraphNodeKind.Time)
                    entry["description"] = "Input node with no inputs. Out is Float and currently always returns 0.";
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

        private static JArray ShaderNodeInputs(ShaderGraphNode node)
        {
            var inputs = new JArray();
            foreach (var port in ShaderGraphNodes.Inputs(node))
                inputs.Add(new JObject
                {
                    ["port"] = port.name,
                    ["type"] = port.type?.ToString(),
                    ["passthrough"] = node.kind == ShaderGraphNodeKind.Reroute,
                    ["defaultUV"] = port.uvDefault,
                    ["value"] = new JArray(port.value.x, port.value.y, port.value.z, port.value.w)
                });
            return inputs;
        }

        private static string ShaderNodeDescription(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.Noise => "Spatial noise from UV, Scale and Seed. Out is Float. fractal: None, FBm, Ridged or PingPong. Octaves rounds/clamps to 1..8, Lacunarity to 0.01..16, Gain to 0..1; PingPongStrength is nonnegative.",
            ShaderGraphNodeKind.Cellular => "Outputs are Float F1 (default), F2 and Cell; there is no Out. F1/F2 are nonnegative nearest/second-nearest distances in scaled coordinates using the selected metric, without the FNL -1 offset. Cell is a deterministic 0..1 value, not a unique ID. Jitter clamps to 0..1.",
            ShaderGraphNodeKind.Random => "Deterministic Float Out in 0..1 from Float2 Coordinates and Seed. Coordinates defaults to constant zero; connect UV for per-pixel values or Repeat.Cell for per-cell values.",
            ShaderGraphNodeKind.Checkerboard => "Checkerboard pattern with smoothed edges. Out is a Float mask.",
            ShaderGraphNodeKind.Stripes => "Antialiased Float mask with Square, Triangle or Sine profile. Width affects Square only. Angle is degrees; Phase is cycles. Time currently returns 0.",
            ShaderGraphNodeKind.Grid => "Grid pattern with smoothed edges. Out is a Float mask.",
            ShaderGraphNodeKind.Rings => "Antialiased concentric Float mask around Center. Frequency is cycles per UV distance, Width is the filled fraction, Phase is cycles. Time currently returns 0.",
            ShaderGraphNodeKind.GradientFactor => "Linear, Radial or Angular Float coordinate. Connect Out to SampleGradient.T and a Gradient parameter to SampleGradient.Gradient. Angle is degrees. Linear/Radial can exceed 0..1; Angular wraps to turns.",
            ShaderGraphNodeKind.Shape => "Float Out is a smoothed mask; Float Distance is signed, negative inside. Ellipse distance is approximate. Inputs depend on option: use modes[].inputs. Capsule uses Center/Size/Rotation; Line Segment uses Start/End/Width. Rotation is degrees.",
            ShaderGraphNodeKind.CombineDistance => "Union, Intersection or Subtract (A minus B) of signed Float A/B. Out is signed, negative inside; Mask is antialiased coverage. Smoothness blends the distance operation; Feather widens only the mask edge.",
            ShaderGraphNodeKind.TransformUV => "Translate, scale and rotate coordinates independently of the layer Transform. Rotation is degrees. Out is Float2 UV without a 0..1 clamp.",
            ShaderGraphNodeKind.PolarCoordinates => "To Polar returns Float2(angle in turns, 2*distance from Center); From Polar reads UV.x as turns and UV.y/2 as radius around Center. Angle offsets degrees. No implicit 0..1 clamp; the angular coordinate wraps.",
            ShaderGraphNodeKind.Repeat => "For p=UV*Scale+Offset, Float2 Out=frac(p) and Float2 Cell=floor(p), including negative cells. Cell is not InstanceIndex; connect it to Random.Coordinates for one random value per cell.",
            ShaderGraphNodeKind.Mirror => "Fold X, Y or Both onto positive half-planes relative to Center. Out is Float2 UV; no implicit 0..1 clamp.",
            ShaderGraphNodeKind.Kaleidoscope => "Fold coordinates into radial sectors. Sectors rounds/clamps to 1..128; Angle is degrees. Out is Float2 UV; no implicit 0..1 clamp.",
            ShaderGraphNodeKind.DomainWarp => "Warp UV with unnormalized vector noise. Float2 Out=UV+Displacement. Strength=0 preserves UV and returns zero Displacement; no 0..1 clamp. fractal: None (one octave), Progressive or Independent. Octaves rounds/clamps to 1..8, Lacunarity to 0.01..16, Gain to 0..1. Scale/Offset affect noise sampling.",
            ShaderGraphNodeKind.VectorDisplacement => "Float2 Out=UV+decoded Displacement*Strength, component-wise. Signed RG uses the vector unchanged; Packed RG decodes 2*v-1, so (0.5,0.5) is neutral. Strength=(0,0) preserves UV; no 0..1 clamp or vector normalization.",
            ShaderGraphNodeKind.Twirl => "Rotate UV around Center with smooth radial falloff. Strength is degrees; Radius<=0 or Strength=0 preserves UV. Out is Float2; no 0..1 clamp.",
            ShaderGraphNodeKind.BulgePinch => "Radial UV deformation: positive Strength magnifies, negative Strength pinches the sampled image. Radius<=0 or Strength=0 preserves UV. Out is Float2; no 0..1 clamp.",
            ShaderGraphNodeKind.Wave => "Displace UV along Direction (degrees), varying across the perpendicular axis. Phase is cycles. Out is Float2 UV; Strength=0 preserves UV; no 0..1 clamp. Time currently returns 0.",
            ShaderGraphNodeKind.Ripple => "Radial UV wave around Center with Radius falloff and Phase in cycles. Out is Float2; Radius<=0 or Strength=0 preserves UV; no 0..1 clamp. Time currently returns 0.",
            ShaderGraphNodeKind.SampleGradient => "Sample a Gradient resource at scalar T. Out is Color. Connect GradientFactor.Out to T to color a gradient field.",
            _ => null
        };
    }
}
