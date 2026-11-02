using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static JObject ShaderSnapshot(ShaderLayerBehaviour shader, WhimTexDocument document)
            => new JObject { ["graph"] = WhimTexDocumentJson.ShaderGraphSnapshot(shader.graph, document) };

        private static void SetShader(WhimTexDocument document, ShaderLayerBehaviour shader, JObject settings)
        {
            Keys(settings, "graph");
            if (settings["graph"] == null) return;
            // A complete replacement keeps IDs and wiring atomic; validation precedes mutation.
            // Function definitions and their plain-text source archive use the same codec;
            // loading a graph never refreshes its embedded backups from sourcePath.
            var graph = WhimTexDocumentJson.ReadShaderGraph(Obj(settings["graph"], "shader.graph"));
            WhimTexDocumentJson.RestoreMissingAssets(document);
            shader.graph = graph;
            WhimTexDocumentJson.CaptureMissingAssets(document);
            shader.InvalidateGraph();
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
                var modeNames = ShaderGraphNodes.ModeNames(kind);
                if (modeNames != null)
                {
                    int defaultOption = node.option;
                    entry["modeLabel"] = ShaderGraphNodes.ModeLabel(kind);
                    entry["defaultOption"] = defaultOption;
                    var modes = new JArray();
                    bool dynamicInputs = kind is ShaderGraphNodeKind.Shape or ShaderGraphNodeKind.RotateVector or ShaderGraphNodeKind.BilateralBlur;
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
                var settings = ShaderNodeSettings(node);
                if (settings.Count > 0) entry["settings"] = settings;
                if (ShaderGraphNodes.UsesHeightTexture(kind)) entry["dynamicInputs"] = true;
                if (ShaderGraphNodes.IsFilter(kind))
                {
                    entry["sampledInputs"] = ShaderNodeSampledInputs(node);
                    entry["outputTypeFromInput"] = "In";
                    if (kind is not ShaderGraphNodeKind.RadialBlur and not ShaderGraphNodeKind.SlopeBlur) entry["radiusUnits"] = "DocumentPixels";
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
                if (ShaderGraphNodes.TrySurfaceOutputType(node, port, out var type) ||
                    ShaderGraphNodes.TryProceduralOutputType(node.kind, out type) ||
                    ShaderGraphNodes.TryColorOutputType(node.kind, out type) ||
                    ShaderGraphNodes.TryExtendedOutputType(node.kind, out type))
                    result[port] = type.ToString();
                else if (node.kind == ShaderGraphNodeKind.TransformUV)
                    result[port] = ShaderGraphValueType.Float2.ToString();
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
                inputs.Add(input);
            }
            return inputs;
        }

        private static JObject ShaderNodeSettings(ShaderGraphNode node)
        {
            var settings = new JObject();
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
                    }
                    node.normalEncoding = original;
                }
                settings["normalEncoding"] = encoding;
            }
            return settings;
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
            ShaderGraphNodeKind.HeightBlend => "Blend signed/HDR scalar Bottom=0 and Top=1 by height, with Offset=0, Smoothness=0.1 and Opacity=1. Outputs Float Height and Mask; there is no Out. Offset shifts the top height, nonnegative Smoothness softens the height comparison and saturated Opacity controls the top contribution. At zero Smoothness the higher height wins, with weight 0.5 for a tie. Height is not clamped to 0..1.",
            ShaderGraphNodeKind.TileUV => "Grid (option 0) or Staggered (1) cell-local UV with Count=(4,4), Offset=(0,0) in cells, RowOffset=0.5 active only in Staggered, RandomRotation=0 degrees, RandomScale=0 (clamped 0..1), Seed=1337. Outputs UV, Cell and Center as Float2, Random and Mask as Float; there is no Out. Cell identifies the original cell; Center is in UV coordinates. Random transformations stay in their own cell: Mask clips transformed motifs, with no cross-cell overlap.",
            ShaderGraphNodeKind.PerspectiveUV => "Inverse projective mapping from a quadrilateral to unit-square source UV. Defaults BottomLeft=(0,0), BottomRight=(1,0), TopRight=(1,1), TopLeft=(0,1). Outputs Float2 UV and Float Mask, not Out. Mask selects the quad; invalid, degenerate or nonconvex quads return zero UV and zero Mask. Multiply sampled alpha by Mask for coverage.",
            ShaderGraphNodeKind.GaborNoise => "2D directional Gaussian-windowed sparse convolution with approximate antialiasing. Defaults Scale=(8,8), Frequency=2, Anisotropy=0.8, Angle=0 degrees, Seed=1337. Float outputs Value, Phase and Intensity are in 0..1; there is no Out. The footprint includes coordinate and Frequency/Angle/Anisotropy variation, plus Gaussian-envelope minification; it is not exact area filtering. Angle controls direction and Anisotropy controls directional coherence. More expensive than simple noise; sampled filters multiply its evaluation cost.",
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
            ShaderGraphNodeKind.NormalLighting => "Color Out combines surface Color with Ambient and diffuse lighting from Normal/LightDirection plus specular. normalEncoding selects PackedRGB (default) or SignedXYZ; Normal and LightDirection are normalized. Intensity defaults 1, Ambient 0.15, SpecularIntensity 0, Roughness 0.25. SpecularOnly suppresses ambient/diffuse; specular uses fixed view (0,0,1). LightColor/SpecularColor alpha scales their contributions; source alpha is preserved. Signed/HDR RGB and intensities are not clipped.",
            ShaderGraphNodeKind.Premultiply => "Color Out.rgb = Color.rgb*Color.a; alpha is unchanged. No implicit clamp. Output.RGBA expects straight color, so unpremultiply before Output when necessary.",
            ShaderGraphNodeKind.Unpremultiply => "Color Out.rgb = Color.rgb/Color.a for every nonzero alpha, including small or negative alpha. Exactly zero alpha returns zero RGB. Alpha is unchanged; no epsilon cutoff or implicit clamp.",
            ShaderGraphNodeKind.Flipbook => "Float2 atlas UV from UV, Columns=4, Rows=4 and Frame=0. Dimensions floor/clamp to 1..4096; floored Frame wraps, including negative values. option 0 is Bottom to Top (frame 0 bottom-left), 1 Top to Bottom. Local UV orientation is unchanged; no implicit clamp or sampling.",
            ShaderGraphNodeKind.TileableNoise => "Periodic Perlin Float Out using UV, Float2 Period=(1,1), Cells=(8,8), Offset=(0,0) and Seed=1337. Period is a UV repeat length, abs-bounded to at least 1e-5; Cells rounds/clamps to 1..4096 per axis. Offset is in UV units. Gradient-lattice indices wrap; ordinary Noise fractal/option settings do not apply.",
            ShaderGraphNodeKind.VoronoiEdge => "Float Out is nonnegative Euclidean distance to the nearest Voronoi cell boundary in UV*Scale coordinates, not F2-F1 or a mask. Float2 Scale=(8,8), Seed=1337, Jitter=1; Jitter clamps to 0..1. Threshold the distance explicitly to form lines.",
            ShaderGraphNodeKind.Dither => "Color Out quantizes RGB with an ordered Bayer pattern; alpha is unchanged and signed/HDR RGB is not clipped. Levels=8 rounds to at least 2; PixelSize=1 uses abs with minimum 1 document pixel. UV defaults to current-pixel UV. option 0/1/2 selects Bayer 4x4/2x2/8x8. Pattern size follows document dimensions, independently of preview size.",
            ShaderGraphNodeKind.Unlerp => "Component-wise (T-A)/(B-A), without clamping to 0..1. Defaults A=0, B=1, T=0. Out uses the widest numeric input width; Color participates as Float4. Equal bounds are undefined.",
            ShaderGraphNodeKind.InstanceIndex => "Float Out = Start + (float)_WhimTexInstanceIndex * Scale; Float inputs default to Start=0 and Scale=1. The shared uint index is cast with ordinary floating-point precision. Standard rendering currently supplies index 0, so Out equals Start; this is unrelated to Repeat.Cell or Cellular.Cell.",
            ShaderGraphNodeKind.Time => "Float Out = Start + _WhimTexTime * Scale; Float inputs default to Start=0 and Scale=1. Graph-window node and result previews receive shared Editor seconds, including result-preview FX. Other renders use time 0, so Out equals Start. No own preview.",
            ShaderGraphNodeKind.Noise => "Spatial noise from UV, Scale and Seed. Out is Float. fractal: None, FBm, Ridged or PingPong. Octaves rounds/clamps to 1..8, Lacunarity to 0.01..16, Gain to 0..1; PingPongStrength is nonnegative.",
            ShaderGraphNodeKind.Cellular => "Outputs are Float F1 (default), F2 and Cell; there is no Out. F1/F2 are nonnegative nearest/second-nearest distances in scaled coordinates using the selected metric, without the FNL -1 offset. Cell is a deterministic 0..1 value, not a unique ID. Jitter clamps to 0..1.",
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
            ShaderGraphNodeKind.DomainWarp => "Warp UV with unnormalized vector noise. Float2 Out=UV+Displacement. Strength=0 preserves UV and returns zero Displacement; no 0..1 clamp. fractal: None (one octave), Progressive or Independent. Octaves rounds/clamps to 1..8, Lacunarity to 0.01..16, Gain to 0..1. Scale/Offset affect noise sampling.",
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
            ShaderGraphNodeKind.ColorCurves => "Optional Master, Red, Green, Blue and Alpha Curve inputs are identity when disconnected. Master mode is RGB (default) or Luminance. For each curve use x + curve(saturate(x)) - saturate(x), retaining HDR/negative values at identity. Alpha changes only when its curve is connected. Out is Color.",
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
    }
}
