// Pipeline run_script entry DocumentJsonSchema.Run. Regenerates the shared-format schema from model fields.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class DocumentJsonSchema
{
    const string SharpModeContract = " Corner Mode applies to Sharp, MTSDF, CornerFit, Spring, Rbf and RbfDual; default Unrestricted. Other algorithms retain but ignore it. Current Sharp/MTSDF quality prototype: closed raster contours use line fits over 2/4/8/16-pixel supports, requiring agreement across scales in the two wall-normal directions with bounded longitudinal uncertainty. Supported vertices receive sharp extensions; uncertain joins receive small tangent quadratic caps (at most 0.15px displacement) and finite endpoint distances. This replaces their older fixed 8/16-pixel fitting and 2-pixel endpoint eligibility rules. Contour Merge happens before adaptive fitting. Both Corner Mode policies retain these geometric checks and MTSDF per-contour ownership. GuardedSharp additionally retains scalar finite-edge ownership and MTSDF selective interpolation correction: proximity-aware edge coloring, protected corner texels, analytic channel-crossing candidates verified against the same pseudo-distance field, RGB median and alpha unchanged at integer texels. Unrestricted omits that interpolation pass and scalar finite ownership. Miter Limit bevels scalar joins only in GuardedSharp; MTSDF ignores it. Max Distance is display remapping only. CornerFit, Spring, Rbf and RbfDual are unchanged. Very acute low-resolution corners may still round or differ with threshold; no universal reconstruction guarantee. This supersedes earlier Sharp/MTSDF fitting and correction descriptions.";
    const string RbfDualContract = " RbfDual (ID 15, UI RBF Dual Contouring) is a separate experimental raster-only scalar algorithm for SDF and Outline. It shares the Rbf fit, extracts half-pixel cell connectivity using direct continuous RBF root searches, analytic blended normals and a bounded 2D quadratic error function (QEF), then uses the shared sharp-distance stage. It is not a faithful implementation of Occupancy-Based Dual Contouring (2024); no upstream code was copied. Existing Rbf is unchanged. The same source, threshold, sharpMode, miterLimit and scalar output controls apply. Contour Merge is retained but ignored. Max Distance remaps the retained field; the metric participates in the preparation cache key. Gradient/LinearData are supported; MultiChannelData remains MTSDF-only. Sharp-corner recovery and a quality advantage over Rbf are not guaranteed.";
const string RbfContract = " Rbf (ID 14, UI RBF Your SDF) is an experimental 2D adaptation of Cheng and Gingold, arXiv:2609.06209v3 with a WhimTex sharp-offset stage. It seeds cubic RBF interpolation with raster-derived Contour distances, refines implied tangent-circle points in fixed overlapping patches, extracts a half-pixel zero contour, fits supported flanks and normalizes closed regions, then applies experimental unrestricted sharp joins. Fractional contour points are passed directly, without re-rasterizing a mask. This is not the authors' full implementation or a Euclidean distance field. RBF omits angle and flank-support caps, nearest-boundary ownership and miter beveling; extra rays and overlaps may appear. Numerical degeneracy and contour-validity checks remain. Rounded input or recovered contour segments are not guaranteed to become sharp. Contour Merge and Miter Limit are ignored. Max Distance remaps the retained scalar field; Changing the retained Miter Limit value does not rebuild RBF geometry or distance. Source or threshold changes reconstruct it. No universal corner-recovery or quality advantage is claimed. Refinement is limited to 16,777,216 samples; one-dimensional input uses Round AA. Gradient/LinearData are supported; MultiChannelData remains MTSDF-only.";
    const string ExperimentalCornerContract = " CornerFit (ID 12, UI Corner Fit AA) and Spring (ID 13, UI Spring Simulation) are separate experimental raster-only scalar modes for SDF and Outline. CornerFit starts with raw Contour geometry, fits sides over multiple pixels and verifies candidate corners by local AA re-rasterization. Spring first applies automatic spring relaxation to the raw contour, inspired by Matusovic, Parakkat and Eisemann, Interactive Depixelization of Pixel Art through Spring Simulation (2023), DOI 10.1111/cgf.14743, then uses the same AA-tested corner proposals. The paper concerns interactive depixelization, not SDF generation; Spring is not the full interactive tool or a C++ replica. Both reject intersecting or unconfirmed extensions and retain the raw contour for ambiguous cases. Only AA-verified corners receive bounded sharp joins with continuous, local endpoint domains. miterLimit (finite 1..64, default 4) bevels excessive validated joins; it does not validate uncertain corners. Threshold is supported through coverage remapping of the selected source channel; soft/noncoverage masks give an approximate fit. SDF uses its threshold; Outline uses fixed byte threshold 128. Both use the existing scalar downstream controls and support Gradient/LinearData, never MultiChannelData. SDF contourMergeDistance is validated and retained but ignored. CornerFit is an experimental hybrid, not a known guaranteed reconstruction algorithm; neither mode promises universal sharp recovery. Round AA remains the default at ID 4 and the previous eight modes are unchanged.";
    const string ContourContract = " Contour (ID 11, UI Contour AA) is an experimental Euclidean distance field to finite straight segments extracted by raw marching squares from alpha or the selected source channel. Threshold crossings are linearly interpolated to subpixel endpoints. It applies no contour fitting, smoothing, simplification, corner recovery, merge/union, miter extension or MSDF channel construction. Expanded corners round. It measures the current raster threshold boundary and does not restore lost detail. It is a separate algorithm, not an AAEDT modification or a sharp-miter mode. It uses the existing source-channel, threshold, SourceEdges, offset and rendering wrapper; SDF exposes threshold and Outline uses fixed byte threshold 128. miterLimit and SDF contourMergeDistance are retained and validated but ignored. SDF supports Gradient/LinearData; MultiChannelData remains MTSDF-only. Quality comparison is experimental, with no guaranteed improvement across all inputs. Round AA remains the default at ID 4; AAEDT remains ID 10 and all existing modes are unchanged.";
    const string AaedtContract = " AAEDT (ID 10, UI AAEDT + 8SED) is an experimental anti-aliased Euclidean sweep-and-update transform with 8-neighbor vector propagation, adapted from Stefan Gustavson edtaa3 in a portable Burst job. It estimates the raster boundary; wide external contours remain round. It bypasses segment reconstruction and sharp/miter extensions; miterLimit and SDF contourMergeDistance are retained and validated but ignored. Source alpha (or the selected source channel) is assumed to be area coverage of a crisp edge, sampled with a box filter; blur, feather and arbitrary opacity do not give exact geometry. For coverage a in 0..1 and normalized byte threshold t=threshold/255, 0<t<1 uses c=0.5*a/t when a<=t, else c=0.5+0.5*(a-t)/(1-t). This monotonic piecewise remap sends t to 0.5 and preserves 0/1 endpoints. Thresholds 0 and 255 use binary coverage: 1 only when the selected byte-range source value exceeds threshold, otherwise 0. Outline uses fixed threshold 128. SDF supports Gradient/LinearData; MultiChannelData remains MTSDF-only. The default Round AA is unchanged. No accuracy or performance superiority is claimed.";
    const string RasterGuardContract = " Supported concave fits may minimally shift one wall outward within the existing 0.6-pixel envelope to preserve a nearby hole, then recheck finite-tip/topology bounds. After a component merge, one bounded convex-cap enclosure pass checks arc samples and exact unsampled union vertices, without another 0.25-pixel reduction. Closed components with perimeter below 4 pixels keep finite geometry but do not generate distant Sharp/Bevel miter or MTSDF pseudo extensions. Sharp/MTSDF endpoint extensions require at least 2 pixels of straight support on both flanks after union (successive direction dot >= 0.995), with angular amplification capped at 4. Unsupported or above-cap endpoints use finite segment distance, even inside a large merged contour. Sharp resolves finite ownership within each contour before combining contour fields. Very acute or uncertain corners can round; requested Miter Limit above 4 cannot override raster safety. This is a bounded raster reconstruction, not an exact inverse.";
    static Dictionary<string, object> D(params (string key, object value)[] fields)
    { var d = new Dictionary<string, object>(); foreach (var p in fields) d[p.key] = p.value; return d; }
    public static string Run()
    {
        var assembly = typeof(WhimTexDocument).Assembly;
        var fields = typeof(WhimTexDocumentJson).GetMethod("Fields", BindingFlags.NonPublic | BindingFlags.Static);
        var definitions = new Dictionary<string, object>();
        var asset = D(("type", "object"), ("additionalProperties", false), ("required", new[] { "$asset" }),
            ("properties", D(("$asset", D(("type", "object"), ("additionalProperties", false),
                ("properties", D(("guid", D(("type", "string"))), ("path", D(("type", "string"))),
                    ("localId", D(("type", "string"))), ("type", D(("type", "string"))))), ("required", new[] { "guid", "path", "localId", "type" }))))));
        var reference = D(("type", "object"), ("additionalProperties", false), ("required", new[] { "$ref" }), ("properties", D(("$ref", D(("type", "string"))))));
        object Number(Type type) => type == typeof(float) ? D(("type", "number"), ("minimum", -float.MaxValue), ("maximum", float.MaxValue)) :
            type == typeof(int) ? D(("type", "integer"), ("minimum", int.MinValue), ("maximum", int.MaxValue)) :
            type == typeof(byte) ? D(("type", "integer"), ("minimum", 0), ("maximum", 255)) :
            D(("type", type == typeof(double) || type == typeof(decimal) ? "number" : "integer"));
        object Tuple(int count, Type component = null, int? minimum = null) => D(("type", "array"), ("minItems", minimum ?? count), ("maxItems", count), ("items", Number(component ?? typeof(float))));
        string Name(Type t) => t.FullName.Substring("DCFApixels.WhimTex.".Length);
        object Schema(Type type)
        {
            if (type.IsEnum) return D(("type", "string"), ("enum", Enum.GetNames(type)));
            if (type == typeof(bool)) return D(("type", "boolean"));
            if (type == typeof(string) || type == typeof(char)) return D(("type", new[] { "string", "null" }));
            if (type.IsPrimitive || type == typeof(decimal)) return Number(type);
            if (type == typeof(Vector2Int)) return Tuple(2, typeof(int));
            if (type == typeof(Vector3Int)) return Tuple(3, typeof(int));
            if (type == typeof(RectInt)) return Tuple(4, typeof(int));
            if (type == typeof(Color32)) return Tuple(4, typeof(byte));
            if (type == typeof(Vector2)) return Tuple(2);
            if (type == typeof(Vector3)) return Tuple(3, minimum: 2);
            if (type == typeof(Vector4)) return Tuple(4, minimum: 2);
            if (type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Rect)) return Tuple(4);
            if (type == typeof(Bounds)) return Tuple(6);
            if (type == typeof(AnimationCurve))
            {
                object tangent = D(("anyOf", new[] { Number(typeof(float)), D(("enum", new[] { "Infinity", "-Infinity" })) }));
                object key = D(("type", "array"), ("minItems", 7), ("maxItems", 7),
                    ("prefixItems", new object[] { Number(typeof(float)), Number(typeof(float)), tangent, tangent,
                        Number(typeof(float)), Number(typeof(float)), Schema(typeof(WeightedMode)) }));
                return D(("type", new[] { "object", "null" }), ("additionalProperties", false),
                    ("required", new[] { "preWrap", "postWrap", "keys" }), ("properties", D(
                        ("preWrap", Schema(typeof(WrapMode))), ("postWrap", Schema(typeof(WrapMode))),
                        ("keys", D(("type", "array"), ("maxItems", 65536), ("items", key))))));
            }
            if (typeof(IList).IsAssignableFrom(type)) return D(("type", new[] { "array", "null" }),
                ("items", Schema(type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0])));
            if (typeof(UnityEngine.Object).IsAssignableFrom(type) && type != typeof(WhimTexDocument) && type != typeof(ShaderFX))
            {
                var variants = new List<object> { D(("type", "null")), asset };
                return D(("anyOf", variants));
            }
            if (type.IsAbstract && typeof(LayerBehaviour).IsAssignableFrom(type))
                return D(("anyOf", assembly.GetTypes().Where(t => t.IsPublic && !t.IsAbstract && type.IsAssignableFrom(t)).Select(t =>
                    D(("allOf", new[] { Schema(t), D(("type", "object"), ("required", new[] { "$type" })) }))).ToArray()));
            if (type.Assembly != assembly) throw new Exception("Unsupported schema type: " + type);
            string name = Name(type);
            if (!definitions.ContainsKey(name))
            {
                var properties = new Dictionary<string, object>();
                definitions[name] = D(("type", "object"), ("additionalProperties", false), ("properties", properties));
                if (type == typeof(ShaderGraphInput))
                    ((Dictionary<string, object>)definitions[name])["description"] = "Unconnected input. Numeric source Value uses value; UVX/UVY select the coordinate on scalar UV-enabled inputs (vector UV inputs use full UV; Float3 preserves value.z as an independently editable component, default 0). Texture, Gradient and Curve use resource: a matching ShaderParameter (Texture2D, Gradient or Curve). Inline textures accept textureSource Texture only. Missing resource uses the default. Connections override but retain the input; Sub Graph calls use the interface port ID.";
                if (type == typeof(ShaderGraphNode))
                {
                    var definition = (Dictionary<string, object>)definitions[name];
                    definition["description"] = "Stored graph node settings and literal inputs only. Buffer (149) stores bufferResolution/bufferSize/wrap/filter, never generated GPU pixels. Fractal (150) repeats a numeric/Color branch in the 2D graph domain; omitted fractal defaults to FBm. Scalar noise kinds no longer accept embedded fractal settings or octave ports; unsupported old data is rejected without migration.";
                    definition["allOf"] = new[] {
                        D(("if", D(("required", new[] { "kind" }), ("properties", D(("kind", D(("enum", new[] { "CausticsNoise", "Caustics" }))))))),
                          ("then", D(("properties", D(("bufferResolution", D(("default", "Custom"))), ("wrap", D(("default", "Repeat")))))))),
                        D(("if", D(("required", new[] { "kind" }), ("properties", D(("kind", D(("const", "Fractal"))))))),
                          ("then", D(("properties", D(("fractal", D(("type", "string"), ("enum", new[] { "FBm", "Ridged", "PingPong", "Multifractal", "HybridMultifractal", "HeteroTerrain", "RidgedMultifractal" }), ("default", "FBm")))))))),
                        D(("if", D(("required", new[] { "kind" }), ("properties", D(("kind", D(("enum", new[] { "OpenSimplex2WarpNoise", "OpenSimplex2ReducedWarpNoise", "BasicGridWarpNoise" }))))))),
                          ("then", D(("properties", D(("fractal", D(("type", "string"), ("enum", new[] { "None", "Progressive", "Independent" }), ("default", "None")))))))),
                        D(("if", D(("required", new[] { "kind" }), ("properties", D(("kind", D(("enum", new[] { "PerlinNoise", "ValueNoise", "ValueCubicNoise", "OpenSimplex2Noise", "OpenSimplex2SNoise", "FlowNoise", "CellularNoise", "TriangleVoronoiNoise", "GaborNoise", "SpotsNoise", "ShardNoise", "WhiteNoise", "BlueNoise" }))))))),
                          ("then", D(("properties", D(("fractal", D(("const", "None")))))))),
                        D(("if", D(("required", new[] { "kind" }), ("properties", D(("kind", D(("const", "TriangleVoronoiNoise"))))))),
                          ("then", D(("properties", D(("noiseDimensions", D(("const", "TwoD"))))))))
                    };
                }
                if (type == typeof(ShaderGraph))
                    ((Dictionary<string, object>)definitions[name])["description"] = "Shared Shader/FX fragment graph, version 1. Parameters retain their control declarations and stable IDs. Parameter.parameterId refers to a root parameter; SubGraphInput.parameterId refers to an input interface ID. Sub Graph instances reference the embedded subGraphs table and use stable interface IDs as port keys. Definitions share root parameters/functions/sources; nesting is limited to 16, recursion is forbidden, and compilation expands at most 4096 nodes. HDR values are retained. Arrays replace the complete corresponding graph data.";
                if (type == typeof(ShaderGraphSubGraph))
                {
                    var definition = (Dictionary<string, object>)definitions[name];
                    definition["description"] = "Embedded reusable definition: id, name, inputs, outputs, nodes, edges, notes and groups. Contains exactly one SubGraphOutput, with input port keys from outputs[].id. SubGraphInput nodes reference inputs[].id through parameterId and expose Out. Node IDs must be unique across the root and all definitions. No separate parameter/function/source tables or external dependency.";
                    definition["required"] = new[] { "nodes", "outputs" };
                }
                if (type == typeof(ShaderGraphInterfacePort))
                {
                    var definition = (Dictionary<string, object>)definitions[name];
                    definition["description"] = "Typed Sub Graph interface pin. id is the stable wiring key, name is a display label, value is the unconnected default. uvDefault is valid only for Float2; resource types need a resource connection. IDs are unique across the definition's input and output pins.";
                    definition["anyOf"] = new[] {
                        D(("properties", D(("uvDefault", D(("const", false)))))),
                        D(("required", new[] { "type" }), ("properties", D(("type", D(("const", "Float2")))))) };
                }
                if (type == typeof(ShaderLayerBehaviour))
                    ((Dictionary<string, object>)definitions[name])["description"] = "Procedural Shader layer with independent Graph (default) and Code modes. Switching mode converts nothing. Both graph and code state, declarations and values persist in every writing mode. Code uses WhimTex HLSL ApplyFX/@param: draftCode is an editable draft, workingCode is the last successfully compiled source, and draftCodeParameters holds its accepted declarations and current values. Rendering uses workingCode, never the draft. UV is local procedural UV. Shared Input supplies color/SampleInput in Code and SampleInput nodes in Graph; None and missing sources read transparent black. Output is straight RGBA followed by ordinary layer transform, FX and blending; reading CompositeBelow does not replace that composition. Stored texture parameters in both modes are validated/remapped. Shared inputLayerId is remapped even while inactive; only selected sources affect rendering dependencies and stamps.";
                if (type == typeof(ShaderLayerBehaviour) || type == typeof(ShaderFX))
                    ((Dictionary<string, object>)definitions[name])["required"] = new[] { "program" };
                if (type == typeof(ShaderProgram))
                    ((Dictionary<string, object>)definitions[name])["description"] = "Editable program shared by Shader layers and FX. mode selects Graph or Code without conversion. Both branches, parameter values, functions, subgraphs and embedded include sources persist in all write modes. presetId/presetVersion link the same catalog; the full local program remains usable and editable without the preset. Layer input settings and FX activation belong to the host. A Self texture is supported by FX only; layer hosts reject Self in either branch. Code text is limited to 8 MiB UTF-8 per field; TIFF deduplicates/compresses shader source text in a shared table with a 32 MiB budget.";
                if (type == typeof(ShaderFX))
                    ((Dictionary<string, object>)definitions[name])["description"] = "Document-owned Shader effect with an editable shared program. Graph and Code modes retain independent data. Graph SampleInput reads the image entering this effect in its chain. active controls application. $ref shares an owned FX object within the document; external ShaderFX assets are unsupported.";
                if (type == typeof(GradientLayerBehaviour)) ((Dictionary<string, object>)definitions[name])["required"] = new[] { "gradientType" };
                if (type == typeof(MeshGradient))
                {
                    var definition = (Dictionary<string, object>)definitions[name];
                    definition["required"] = new[] { "columns", "rows", "colorMode", "positions", "colors", "nodeModes" };
                    definition["anyOf"] = new[] {
                        D(("properties", D(("colorMode", D(("not", D(("const", "Hue")))))))),
                        D(("required", new[] { "hueChroma" }), ("properties", D(("hueChroma", D(("type", "array")))))) };
                }
                if (type == typeof(Layer)) ((Dictionary<string, object>)definitions[name])["required"] = new[] { "id", "behaviour" };
                properties["$type"] = D(("const", name));
                if (type == typeof(ShaderFX))
                { properties["$id"] = D(("type", "string")); properties["$name"] = D(("type", "string")); }
                if (type == typeof(DrawingLayerBehaviour)) properties["contentOmitted"] = D(("const", true));
                foreach (FieldInfo f in (FieldInfo[])fields.Invoke(null, new object[] { type }))
                    if (!(type == typeof(WhimTexDocument) && f.Name == "layers") && !(type == typeof(DrawingLayerBehaviour) && f.Name == "pixels"))
                        properties[f.Name] = (type == typeof(ShaderLayerBehaviour) || type == typeof(ShaderFX)) && f.Name == "program"
                            ? D(("allOf", new[] { Schema(typeof(ShaderProgram)), D(("type", "object")) })) :
                            type == typeof(ShaderProgram) && f.Name == "draftGraph"
                            ? D(("allOf", new[] { Schema(typeof(ShaderGraph)), D(("type", "object")) })) :
                            type == typeof(ShaderProgram) && f.Name == "mode"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderProgramMode))), ("default", "Graph"),
                                ("description", "Select an independent stored mode without conversion or draft application.")) :
                            type == typeof(ShaderLayerBehaviour) && f.Name == "inputMode"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderLayerInputMode))), ("default", "None"),
                                ("description", "Shared Graph/Code source. None reads transparent black; Texture uses inputTexture; Layer uses inputLayerId; CompositeBelow reads the lower composition including external backdrops through Pass Through ancestors. Isolated/clipped groups stop inheritance. Self and above are excluded. Blending is independent; Crossfade mixes before/after without accumulating alpha.")) :
                            type == typeof(ShaderLayerBehaviour) && f.Name == "inputLayerId"
                            ? D(("type", new[] { "string", "null" }), ("default", null),
                                ("description", "Source layer/group ID. Retained and remapped even when inputMode is not Layer. Empty selections read transparent black. Validation rejects unresolved nonempty selected IDs and self/cyclic dependencies.")) :
                            type == typeof(ShaderProgram) && (f.Name == "draftCode" || f.Name == "workingCode")
                            ? D(("type", f.Name == "draftCode" ? new[] { "string" } : new[] { "string", "null" }), ("maxLength", 8 * 1024 * 1024), ("default", ShaderLayerBehaviour.DefaultCode),
                                ("description", f.Name == "draftCode" ? "Editable WhimTex HLSL ApplyFX/@param draft. Saving or changing mode does not apply it." :
                                    "Last successfully compiled HLSL source used by Code rendering; not a compiled GPU binary. Null means no accepted Code source. Omission alongside an explicit code draft also means null, so a code-only FX recipe can apply its supplied source. workingCodeParameters retains working values; draftCodeParameters supplies them when no saved values exist. Retained while Graph is active.")) :
                            type == typeof(ShaderProgram) && (f.Name is "draftCodeParameters" or "workingCodeParameters")
                            ? D(("type", "array"), ("default", Array.Empty<object>()), ("items", Schema(typeof(ShaderParameter))),
                                ("description", f.Name == "draftCodeParameters" ? "Accepted Code declarations and current values, including control metadata and texture identities. Retained independently of draftGraph.parameters. Array replacement; no reparse of draft code." :
                                    "Last working Code declarations and bound values. Retained separately so a broken draft can change or remove declarations without losing the rendered result. Omitted/empty values use draftCodeParameters when preparing the saved source.")) :
                            type == typeof(ShaderGraph) && f.Name == "version"
                            ? D(("const", 1)) :
                            (type == typeof(ShaderGraph) || type == typeof(ShaderGraphSubGraph)) && f.Name == "nodes"
                            ? D(("type", "array"), ("maxItems", 512), ("items", Schema(typeof(ShaderGraphNode)))) :
                            (type == typeof(ShaderGraph) || type == typeof(ShaderGraphSubGraph)) && (f.Name == "notes" || f.Name == "groups")
                            ? D(("type", "array"), ("default", Array.Empty<object>()), ("maxItems", 512), ("items", Schema(f.FieldType.GetGenericArguments()[0])),
                                ("description", "Editor annotations only; excluded from shader/render fingerprints. IDs are unique across nodes, notes and groups. Groups contain node/note IDs in elements; one group per element, no nesting.")) :
                            (type == typeof(ShaderGraphNote) || type == typeof(ShaderGraphGroup)) && f.Name == "id"
                            ? D(("type", "string"), ("pattern", "^[A-Za-z0-9_]{1,64}$")) :
                            type == typeof(ShaderGraphGroup) && f.Name == "elements"
                            ? D(("type", "array"), ("default", Array.Empty<object>()), ("uniqueItems", true), ("items", D(("type", "string")))) :
                            type == typeof(ShaderGraphNote) && f.Name == "theme"
                            ? D(("type", "integer"), ("minimum", 0), ("maximum", 1), ("default", 0)) :
                            type == typeof(ShaderGraphNote) && f.Name == "fontSize"
                            ? D(("type", "integer"), ("minimum", 0), ("maximum", 3), ("default", 1)) :
                            type == typeof(ShaderGraphNote) && f.Name == "hueShift"
                            ? D(("type", "number"), ("minimum", -180), ("maximum", 180), ("default", 0),
                                ("description", "HSV hue rotation in degrees, applied to the native note background, outlines and text palette. Saturation, value, alpha and non-color styling stay unchanged. Retained across note theme changes. Zero uses the unmodified native colors.")) :
                            type == typeof(ShaderGraphNote) && f.Name == "size"
                            ? D(("type", "array"), ("minItems", 2), ("maxItems", 2), ("items", D(("type", "number"), ("exclusiveMinimum", 0))), ("default", new[] { 200, 160 })) :
                            (type == typeof(ShaderGraph) || type == typeof(ShaderGraphSubGraph)) && f.Name == "edges"
                            ? D(("type", "array"), ("maxItems", 4096), ("items", Schema(typeof(ShaderGraphEdge)))) :
                            type == typeof(ShaderGraph) && f.Name == "subGraphs"
                            ? D(("type", "array"), ("default", Array.Empty<object>()), ("maxItems", 128), ("items", Schema(typeof(ShaderGraphSubGraph)))) :
                            type == typeof(ShaderGraphSubGraph) && (f.Name == "inputs" || f.Name == "outputs")
                            ? D(("type", "array"), ("minItems", f.Name == "outputs" ? 1 : 0), ("maxItems", 64), ("items", Schema(typeof(ShaderGraphInterfacePort)))) :
                            (type == typeof(ShaderGraphSubGraph) || type == typeof(ShaderGraphInterfacePort)) && f.Name == "id"
                            ? D(("type", "string"), ("pattern", "^[A-Za-z0-9_]{1,64}$")) :
                            (type == typeof(ShaderGraphSubGraph) || type == typeof(ShaderGraphInterfacePort)) && f.Name == "name"
                            ? D(("type", "string"), ("pattern", "\\S")) :
                            type == typeof(ShaderGraphInterfacePort) && f.Name == "uvDefault"
                            ? D(("type", "boolean"), ("default", false), ("description", "Use current evaluation UV when unconnected. Valid only with type Float2.")) :
                            type == typeof(ShaderGraph) && f.Name == "parameters"
                            ? D(("type", "array"), ("maxItems", 256), ("items", Schema(typeof(ShaderParameter)))) :
                            type == typeof(ShaderGraph) && f.Name == "sources"
                            ? D(("type", "array"), ("default", Array.Empty<object>()), ("maxItems", 4096),
                                ("items", D(("allOf", new[] { Schema(typeof(ShaderGraphSource)), D(("type", "object")) }))),
                                ("description", "Embedded HLSL source archive. At most 32 MiB of UTF-8 text across all entries, 8 MiB per entry, 32768 include directives and 64 nested modules. Capture and validation retain every source, including inactive preprocessor branches. TIFF deduplicates/compresses text across all graphs; JSON retains plain code.")) :
                            type == typeof(ShaderGraph) && f.Name == "functions"
                            ? D(("type", "array"), ("default", Array.Empty<object>()), ("maxItems", 512), ("items", Schema(f.FieldType.GetGenericArguments()[0]))) :
                            type == typeof(ShaderGraphFunction) && f.Name == "hasDraftPorts"
                            ? D(("type", "boolean"), ("default", false),
                                ("description", "Marks a pending port draft, including an intentionally empty draftPorts list. A nonempty draftPorts also implies a draft. An empty/null draftPorts without this flag uses the applied ports, including after Unity domain reload or JsonUtility copying.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "subGraphId"
                            ? D(("type", new[] { "string", "null" }), ("description", "SubGraph node reference to graph.subGraphs[].id. Ports are stable interface IDs from that definition, not pin display names.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "previewExpanded"
                            ? D(("type", "boolean"), ("default", true),
                                ("description", "Per-node preview expansion, retained in the document and clipboard, including Sub Graph nodes. Editor layout only; excluded from shader compilation and rendered-image cache fingerprints.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "parameterId"
                            ? D(("type", new[] { "string", "null" }), ("description", "Parameter: root graph.parameters[].id. SubGraphInput: the enclosing definition's inputs[].id; output port is Out. Interface nodes cannot be created in the root graph.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "heightSource"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderGraphHeightSource))), ("default", "Texture"),
                                ("description", "HeightToNormal/HeightToDistortion: Texture requires a Height resource, source-texel Radius and channel/wrap/filter; Value accepts Float Height and reevaluates its branch at neighboring UVs with document-pixel Radius. Both retain derivative, Strength and FlipX/FlipY.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "option"
                            ? D(("type", "integer"), ("default", 0),
                                ("description", "Zero-based node mode; use discovery modes for kind-specific values. RangeMask: 0=Linear (default), 1=Smooth inward falloff; scalar Center=0.5, Width=0.2, Feather=0; dynamic In/Out use component-wise numeric values without color conversion. Width includes transitions, Feather clamps to 0..Width/2, Width<=0 is empty. GaborNoise: 0=Sine, 1=Triangle, 2=Pulse, affecting only Phasor; PhaseOffset (cycles) and Pulse Width are numeric inputs. Blur/Sharpen/Dilate/Erode: 0=3x3, 1=5x5, 2=9x9; EdgeDetection has a fixed 3x3 Sobel kernel. RadialBlur: 0=Spin, 1=Zoom; DirectionalBlur has no mode. SlopeBlur: 0=Average, 1=Min, 2=Max. BilateralBlur: 0=Source, 1=Custom (adds dynamic numeric/Color Guide, default scalar 0; Out follows In only). TileUV: 0=Grid, 1=Staggered (activates RowOffset). RotateVector: 0=2D (Float2 In/Out), 1=3D (Float3 In/Out, adds Axis). Flipbook: 0=Bottom to Top, 1=Top to Bottom. Dither: 0=Bayer 4x4, 1=Bayer 2x2, 2=Bayer 8x8.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "constant"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderGraphConstant))), ("default", "PI"),
                                ("description", "Constant node selection. Emits a Float HLSL literal; arbitrary values use parameters or unconnected inputs.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "fractal"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderGraphFractal))),
                                ("description", "Fractal node: FBm (default), Ridged, PingPong, Multifractal, HybridMultifractal, HeteroTerrain or RidgedMultifractal; numeric/Color In is repeated in the 2D graph domain, not through per-noise WT fractal evaluation. Octaves=3, Lacunarity=2, Gain=0.5, WeightedStrength=0, PingPongStrength=2, Bias=1, Feedback=2 and SeedStep=1 are input ports. The four multifractal/terrain modes transform source values by s=2*In-1 and return raw unclamped heights. Bias applies to Hybrid/Hetero/RidgedMultifractal; nonnegative Feedback applies to Hybrid/RidgedMultifractal. WeightedStrength and PingPongStrength do not apply to these four modes; inactive ports retain values/connections. A source containing Buffer requires an unconnected literal SeedStep=0. Warp kinds retain None (default), Progressive or Independent with Octaves/Lacunarity/Gain. Ordinary scalar/grain noise has no fractal controls and retains only None in this shared storage field; old embedded fractal data is unsupported. The public WT HLSL API is unchanged.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "bufferResolution"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderGraphBufferResolution))), ("default", "Canvas"),
                                ("description", "Buffer: Canvas uses current render-target dimensions, including node preview resolution; Custom uses exact bufferSize. Linear float32 RGBA GPU buffers reuse allocation; bound data/context changes invalidate contents. External RenderTexture inputs and CustomFunction code conservatively refresh each owner render. No generated pixels are serialized.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "bufferSize"
                            ? D(("type", "array"), ("minItems", 2), ("maxItems", 2), ("items", D(("type", "integer"), ("minimum", 1), ("maximum", int.MaxValue))), ("default", new[] { 512, 512 }),
                                ("description", "Buffer Custom resolution [width,height], stored as Vector2Int. Retained but inactive for Canvas; both values must be positive. Runtime allocation remains subject to supported GPU texture dimensions.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "noiseDimensions"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderGraphNoiseDimensions))), ("default", "TwoD"),
                                ("description", "WT noise width: OneD=0, TwoD=1, ThreeD=2. Scalar/warp Position, Scale, Offset, Period, Direction and vector outputs follow the selected width; Gradient exists in all dimensions, Curl only in 2D/3D on supported bases. Grain accepts only OneD/TwoD and retains Float2 Position/Offset/ImageSize. Discovery settings.noiseDimensions.values supplies exact ports and outputTypes. Dimension changes retain values and remove only absent-port edges.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "noiseDistribution"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderGraphNoiseDistribution))), ("default", "Uniform"),
                                ("description", "CellularNoise/TriangleVoronoiNoise point distribution: Uniform=0 or FixedRadius=1. Jitter is a numeric input.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "noiseMetric"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderGraphNoiseMetric))), ("default", "Euclidean"),
                                ("description", "CellularNoise distance metric: Euclidean=0, EuclideanSquared=1, Manhattan=2, Hybrid=3 (Manhattan plus squared Euclidean).")) :
                            type == typeof(ShaderGraphNode) && f.Name == "noiseOutput"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderGraphNoiseOutput))), ("default", "F1"),
                                ("description", "CellularNoise normalized Value selection: CellValue=0, F1=1, F2=2, Add=3, Subtract=4, Multiply=5, Divide=6, DistanceToEdge=8. Smoothness=0 preserves hard results; 0..1 smooths all Value modes. Metric is ignored only for DistanceToEdge (Euclidean). Auxiliary F1/F2/CellValue/ToPoint remain hard, unfiltered first-octave geometry; there is no uint ID port.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "noiseFiltering"
                            ? D(("type", "boolean"), ("default", true),
                                ("description", "WT scalar/warp filtering uses unwrapped Position derivatives and graph sample footprints in sampled branches. Fractal uses this flag for octave attenuation in its 2D graph domain. No implicit texture mapping; grain uses its separate pixel-based settings.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "noiseInverted"
                            ? D(("type", "boolean"), ("default", false),
                                ("description", "WhiteNoise/BlueNoise inversion, applied independently to monochrome Value and independent-RGB Color (alpha 1).")) :
                            type == typeof(ShaderGraphNode) && (f.Name == "inputColorSpace" || f.Name == "outputColorSpace")
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderGraphColorSpace))),
                                ("default", f.Name == "inputColorSpace" ? "LinearRGB" : "SRGB"),
                                ("description", "ConvertColor's explicit source/destination space. HSV/HSL use sRGB-encoded RGB and hue in turns. HSV excludes negative RGB but preserves HDR Value; HSL is SDR (0..1). LinearRGB, SRGB and Oklab retain signed/HDR values. Alpha is unchanged.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "channel"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderGraphChannel))), ("default", "Luminance"),
                                ("description", "GradientMap source channel, or HeightToNormal/HeightToDistortion channel when heightSource is Texture. Ignored for Value. Luminance uses linear RGB weights, without color conversion.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "derivative"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderGraphDerivative))), ("default", "Scharr"),
                                ("description", "Height node finite-difference stencil. Radius uses bound-source texels for heightSource Texture or document pixels for Value; gradient magnitude is retained before normal encoding.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "normalEncoding"
                            ? D(("type", "string"), ("enum", Enum.GetNames(typeof(ShaderGraphNormalEncoding))), ("default", "PackedRGB"),
                                ("description", "HeightToNormal output; NormalBlend/NormalStrength input/output; NormalToDistortion/NormalLighting input encoding. PackedRGB decodes as 2*x-1; SignedXYZ is unchanged. NormalToDistortion never normalizes XY. Unconnected defaults follow the selected encoding. NormalEncode/NormalDecode have fixed directions and do not use this setting.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "expression"
                            ? D(("type", "string"), ("default", "A"), ("maxLength", 4096),
                                ("description", "Expression node HLSL formula. Variables become case-sensitive input names; valueType selects Float-Float4 for all inputs and Out. Functions, numeric literals, comments, casts and swizzles do not create inputs. Empty expression returns zero. Invalid expressions may be retained as uncompiled drafts.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "loopPartnerId"
                            ? D(("type", new[] { "string", "null" }), ("description", "ForBegin/ForEnd partner node ID. Endpoints must reference each other in the same graph. Initial sets the numeric/Color state type; Index starts at zero, Value carries state, Result returns the final state. Other node kinds leave this field null.")) :
                            type == typeof(ShaderGraphNode) && f.Name == "swizzle"
                            ? D(("type", new[] { "string", "null" }), ("default", "xxxx"),
                                ("description", "Swizzle node mask: 1–4 xyzw/rgba letters (case-insensitive) or _ for zero. Output width is mask length. Input width is max(source width, highest mask component); connected scalars repeat and shorter vectors gain zeros. Unconnected values expose inferred components directly. Invalid masks may be retained as uncompiled drafts.")) :
                            type == typeof(MeshGradient) && (f.Name == "columns" || f.Name == "rows")
                            ? D(("type", "integer"), ("minimum", 1), ("maximum", MeshGradient.MaxCells)) :
                            (type == typeof(MeshGradient) || type == typeof(FreeformGradient)) && f.Name == "colorMode"
                            ? D(("type", "string"), ("enum", new[] { "Linear", "Gamma", "Perceptual", "Hue" })) :
                            type == typeof(MeshGradient) && f.Name == "hueChroma"
                            ? D(("type", new[] { "array", "null" }), ("minItems", 16), ("maxItems", 2401), ("items", Number(typeof(float))),
                                ("description", "Required non-null for Hue: one finite chroma coefficient per color control, length equals (3*columns+1)*(3*rows+1). Colors hold OKLab/alpha; this scalar lattice is subdivided alongside them to preserve appearance. Controls can be negative; evaluated target chroma cannot. Null for other models.")) :
                            type == typeof(MeshGradient) && f.Name == "positions"
                            ? D(("type", "array"), ("minItems", 16), ("maxItems", 2401), ("items", Tuple(2))) :
                            type == typeof(MeshGradient) && f.Name == "colors"
                            ? D(("type", "array"), ("minItems", 16), ("maxItems", 2401), ("items", Tuple(4))) :
                            type == typeof(MeshGradient) && f.Name == "nodeModes"
                            ? D(("type", new[] { "array", "null" }), ("minItems", 4), ("maxItems", (MeshGradient.MaxCells + 1) * (MeshGradient.MaxCells + 1)), ("items", Schema(typeof(MeshGradientNodeMode)))) :
                            type == typeof(MeshGradient) && f.Name == "patchPriorities"
                            ? D(("type", new[] { "array", "null" }), ("minItems", 1), ("maxItems", MeshGradient.MaxCells * MeshGradient.MaxCells),
                                ("items", D(("type", "array"), ("minItems", 3), ("maxItems", 3), ("items", D(("type", "number"), ("minimum", 0), ("maximum", 1))))),
                                ("description", "Optional row-major priority coefficients per cell. Retained u slopes in the first row and v slopes in the first column define subdivision intervals. Each axis is normalized; zero totals and null/omission use equal intervals. Effective priorities are continuous across shared edges; independent stored offsets do not select front surfaces. Clone/API reads canonicalize the planes. Length equals columns*rows; finite nonnegative triples sum to at most 1. Preserve coefficients when replacing a mesh.")) :
                            type == typeof(FreeformGradient) && f.Name == "points"
                            ? D(("type", "array"), ("minItems", 1), ("maxItems", FreeformGradient.MaxPoints), ("items", Schema(typeof(FreeformGradientPoint)))) :
                            type == typeof(FreeformGradientPoint) && f.Name == "influence"
                            ? D(("type", "number"), ("minimum", FreeformGradient.MinInfluence), ("maximum", FreeformGradient.MaxInfluence), ("default", .35),
                                ("description", "Relative influence in local layer coordinates; 0.35 is displayed as 35%. Smooth inverse-distance mixing has no hard boundary.")) :
                            type == typeof(FreeformGradientPoint) && f.Name == "position"
                            ? D(("type", "array"), ("minItems", 2), ("maxItems", 2), ("items", D(("type", "number"), ("minimum", -10000), ("maximum", 10000)))) :
                            type == typeof(FreeformGradientPoint) && f.Name == "color"
                            ? D(("type", "array"), ("minItems", 4), ("maxItems", 4), ("prefixItems", new object[] { Number(typeof(float)), Number(typeof(float)), Number(typeof(float)), D(("type", "number"), ("minimum", 0), ("maximum", 1)) }), ("items", false),
                                ("description", "Straight encoded RGBA; signed HDR RGB is retained. Mixing decodes RGB into linear working space; alpha uses the same weights.")) :
                            type == typeof(ShapeLayerBehaviour) && (f.Name == "rectangleCorners" || f.Name == "polygonCorners")
                            ? D(("type", "array"), ("minItems", f.Name == "rectangleCorners" ? 4 : 3), ("maxItems", f.Name == "rectangleCorners" ? 4 : 32), ("items", Schema(typeof(ShapeLayerBehaviour.Corner)))) :
                            type == typeof(Layer) && f.Name == "groupFxInLayerSpace"
                            ? D(("type", "boolean"), ("default", false), ("description", "On groups only, preserve a converted source layer's LayerToLocal FX coordinates instead of the ordinary group's canvas coordinates. Character Text-to-Vector sets this true; non-group layers ignore it. Retained by save and Undo.")) :
                            type == typeof(GradientLayerBehaviour) && f.Name == "circularStartAngle"
                            ? D(("type", "number"), ("default", 0), ("description", "Angular gradient start angle in counterclockwise degrees from the local left ray. Independent of transform and palette. One period spans 360/circularRepetitions degrees; default start angle preserves the existing field.")) :
                            (type == typeof(SDFLayerBehaviour) || type == typeof(OutlineLayerBehaviour)) && f.Name == "miterLimit"
                            ? D(("type", "number"), ("minimum", 1), ("maximum", 64),
("description", "Range 1..64, default 4. Finite dimensionless miter-length limit relative to the contour offset. Used by Sharp, CornerFit and Spring; excessive joins are beveled. Rbf retains but ignores this value. Same field on SDF and Outline. CornerFit/Spring apply it only to AA-verified corners with bounded local endpoint influence; it does not validate uncertain corners. In Sharp, values above 4 cannot override the separate raster angular-safety cap; unsupported or very acute corners can round.")) :
                            type == typeof(SDFLayerBehaviour) && f.Name == "contourMergeDistance"
                            ? D(("type", "number"), ("minimum", 0), ("maximum", 2), ("default", 0),
                                ("description", "Experimental SDF-only contour merge in current raster/render pixels, independent of distance remap width. Finite 0..2 inclusive; default 0 disables it. Applies only to Sharp and MTSDF; other modes retain but ignore it, with the same validation. One pass merges disjoint consecutive-vertex pairs within one contour, shortest eligible pairs first, to their midpoints. Each pair is within the requested distance; each original vertex is used at most once. Never merges different loops or builds multi-vertex clusters. A topology conflict rolls back the whole batch. Existing safety protections remain. May shift short details and does not guarantee restored sharpness.")) :
                            type == typeof(ShapeLayerBehaviour.Corner) && f.Name == "amount" ? D(("type", "number"), ("minimum", 0), ("maximum", 1)) :
                            type == typeof(TextLayerBehaviour) && f.Name == "edgeMode" ? D(("type", "string"), ("enum", Enum.GetNames(typeof(TextEdgeMode))), ("default", "HintedAA"),
                                ("description", "Glyph rasterization: HintedAA (default) uses grid-fitted antialiasing; Antialiased uses antialiasing without hinting; Pixel uses binary glyph coverage, snapped glyph placement and nearest sampling by default. Explicit filtering and FX can soften Pixel output; this is not final-alpha quantization. The mode is part of the saved-appearance key.")) :
                            type == typeof(TextLayerBehaviour) && (f.Name == "fontSize" || f.Name == "maxFontSize") ? D(("type", "number"), ("minimum", 1), ("maximum", 2048)) :
                            type == typeof(TextLayerBehaviour) && f.Name == "fallbackPixelScale" ? D(("type", "number"), ("exclusiveMinimum", 0), ("maximum", 16384), ("default", 1),
                                ("description", "Pixel scale of the retained font fallback bitmap. Missing-font rendering rescales its placement by current pixelScale/reference without changing retained typography.")) :
                            type == typeof(TextLayerBehaviour) && f.Name == "characterHorizontalScale" ? D(("type", "number"), ("minimum", .01), ("maximum", 10)) :
                            type == typeof(TextLayerBehaviour) && f.Name == "layoutOffset" ? D(("type", "array"), ("minItems", 2), ("maxItems", 2),
                                ("items", D(("type", "number"), ("minimum", -1000000), ("maximum", 1000000))), ("default", new[] { 0, 0 }),
                                ("description", "Layout translation in local document pixels before rasterization, independent of the layer transform. Preserved across Point/Frame switches.")) :
                            type == typeof(TextSpacing) ? D(("type", "number"), ("minimum", -1), ("maximum", 10)) :
                            type == typeof(TextLayerBehaviour) && f.Name == "frameSize" ? D(("type", "array"), ("minItems", 2), ("maxItems", 2),
                                ("items", D(("type", "number"), ("minimum", 1), ("maximum", 32768)))) :
                            type == typeof(WhimTexDocument) && (f.Name == "width" || f.Name == "height")
                            ? D(("type", "integer"), ("minimum", 1), ("maximum", 16384)) :
                            type == typeof(WhimTexDocument) && f.Name == "pixelScale"
                            ? D(("type", "number"), ("exclusiveMinimum", 0), ("maximum", 16384), ("default", 1),
                                ("description", "Logical pixel lengths multiplied once at rendering/painting. Does not change source texel indices, canvas buffer dimensions, antialiasing grids, normalized layer coordinates or UI pixels.")) :
                            type == typeof(Layer) && f.Name == "sourcePixelScale"
                            ? D(("type", "number"), ("minimum", 0), ("maximum", 16384), ("default", 0),
                                ("description", "Source pixel-scale reference retained at the first canvas change; zero uses the current document pixelScale.")) :
                            type == typeof(Layer) && f.Name == "sourceCanvasScale"
                            ? D(("type", "array"), ("minItems", 2), ("maxItems", 2), ("default", new[] { 1, 1 }),
                                ("items", D(("type", "number"), ("exclusiveMinimum", 0))),
                                ("description", "Per-source-axis physical pixel metric accumulated by Scale, including nested rotations and quarter turns. Perspective uses the Jacobian at the source centre. Crop and Orientation retain it. Multiplies sourceCanvasSize only for pixel-sized Pattern/Grain parameters; source buffers and dimensionless Noise retain their original basis.")) :
                            type == typeof(ShaderParameter) && f.Name == "pixelUnits"
                            ? D(("type", "boolean"), ("default", false), ("description", "Numeric @param px declaration; the retained logical value is multiplied by document pixelScale once when uploaded.")) :
                            (type == typeof(ShaderParameter) || type == typeof(ShaderParameterControl)) && f.Name == "valueType"
                            ? D(("type", new[] { "string", "null" }), ("default", null), ("pattern", "^(bool|int|uint|half|float)[234]?$"),
                                ("description", "Declared HLSL scalar/vector type, independent of the control presentation. Resource parameters have no numeric valueType.")) :
                            type == typeof(ShaderParameter) && f.Name == "integerValues"
                            ? D(("type", "array"), ("minItems", 4), ("maxItems", 4), ("default", new long[4]),
                                ("items", D(("type", "integer"), ("minimum", int.MinValue), ("maximum", uint.MaxValue))),
                                ("description", "Exact integer/bool components; active count and signedness follow valueType. Bool components are 0/1.")) :
                            type == typeof(ShaderParameterControl) && f.Name == "rgba"
                            ? D(("type", "boolean"), ("default", false)) :
                            type == typeof(ShaderParameterControl) && (f.Name == "valueMinimum" || f.Name == "valueMaximum")
                            ? D(("type", "number"), ("default", 0)) :
                            type == typeof(ShaderParameterControl) && f.Name == "enableIf"
                            ? D(("type", "array"), ("default", Array.Empty<object>()), ("maxItems", 16),
                                ("items", Schema(typeof(ShaderEnableCondition))),
                                ("description", "Enclosing @enableif scalar comparisons, combined with AND. Inspector controls remain visible and become disabled when any condition fails; stored values and shader evaluation are unchanged.")) :
                            type == typeof(ShaderParameterControl) && f.Name == "containers"
                            ? D(("type", "array"), ("default", Array.Empty<object>()), ("maxItems", 16),
                                ("items", Schema(typeof(ShaderLayoutContainer))),
                                ("description", "Outer-to-inner HLSL inspector layout path: Group, Foldout, Tabs, Tab. Derived from metadata, independent of shader values. Selected tabs and foldout expansion are editor-local and not stored here.")) :
                            type == typeof(ShaderParameterControl) && f.Name == "previewParameters"
                            ? D(("type", "array"), ("default", Array.Empty<string>()), ("maxItems", 128),
                                ("items", D(("type", "string"), ("pattern", "^[A-Za-z_][A-Za-z0-9_]*$"))),
                                ("description", "Ordered @param names explicitly passed after float2 uv to previewFunction. Empty retains the UV-only/optional-own-value signature. Referenced types must match exactly.")) :
                            (type == typeof(ShaderParameter) || type == typeof(ShaderParameterControl)) && f.Name == "cyclicRange"
                            ? D(("type", "boolean"), ("default", false), ("description", "Derived from [min % max] HLSL metadata. Float2 endpoints may cross the seam; float numeric edits repeat while slider dragging stays bounded. Two hard finite limits with min < max are required.")) :
                            type == typeof(Layer) && f.Name == "sourceCanvasSize"
                            ? D(("type", "array"), ("minItems", 2), ("maxItems", 2), ("default", new[] { 0, 0 }),
                                ("items", D(("type", "integer"), ("minimum", 0), ("maximum", 16384))),
                                ("anyOf", new[] { D(("const", new[] { 0, 0 })), D(("items", D(("type", "integer"), ("minimum", 1), ("maximum", 16384)))) }),
                                ("description", "Source reference canvas dimensions retained by Crop. [0,0] uses the current canvas; otherwise both dimensions must be positive and their product at most 16,777,216. Keeps Noise, ordinary Pattern and uninitialized Drawing sources at their original pixel scale. Not a source-image resolution, crop rectangle or rendering cache.")) :
                            type == typeof(Layer) && f.Name == "id" ? D(("type", "string"), ("minLength", 1)) : Schema(f.FieldType);
                if (type == typeof(ShaderGraphSource))
                {
                    var definition = (Dictionary<string, object>)definitions[name];
                    definition["required"] = new[] { "id", "code" };
                    definition["description"] = "Portable HLSL module. code retains original include directives; includes binds each distinct requested spelling within this module to another source ID. Missing non-engine bindings, unused bindings and cycles are rejected. Captured IDs hash code plus dependency bindings; IDs are opaque references and may be retained during explicit edits.";
                    properties["id"] = D(("type", "string"), ("minLength", 1), ("maxLength", 128), ("pattern", "^[A-Za-z0-9_-]+$"));
                    properties["code"] = D(("type", "string"), ("maxLength", 8 * 1024 * 1024),
                        ("description", "Unexpanded source text, limited to 8 MiB when encoded as UTF-8. Preserved verbatim in JSON/Undo. Native TIFF stores this string in a shared content-digest block."));
                    properties["includes"] = D(("type", "array"), ("default", Array.Empty<object>()), ("maxItems", 32768),
                        ("items", D(("allOf", new[] { Schema(typeof(ShaderGraphInclude)), D(("type", "object")) }))));
                }
                if (type == typeof(ShaderGraphInclude))
                {
                    ((Dictionary<string, object>)definitions[name])["required"] = new[] { "requested", "sourceId" };
                    properties["requested"] = D(("type", "string"), ("minLength", 1), ("maxLength", 4096),
                        ("pattern", "^[^\\u0000\\r\\n\"<>]+$"),
                        ("description", "Exact include string without quotes/angle brackets, scoped to the including module. Duplicate requested spellings use one binding."));
                    properties["sourceId"] = D(("type", "string"), ("minLength", 1), ("maxLength", 128), ("pattern", "^[A-Za-z0-9_-]+$"));
                }
                if (type == typeof(ShaderProgram))
                {
                    properties["workingGraph"] = D(("anyOf", new[] { D(("$ref", "#/$defs/ShaderGraph")), D(("type", "null")) }), ("default", null),
                        ("description", "Last successfully compiled graph with functions, include backups, parameters and bound node-input values. Optional; null means no saved working Graph. Rendering restores this snapshot while graph retains an unfinished draft. TIFF permits pending/failed drafts only with a working version in the selected mode."));
                    properties["presetId"] = D(("type", new[] { "string", "null" }), ("default", null),
                        ("description", "Optional @preset GUID in N or D form; editor writes lowercase N. A nonempty ID requires presetVersion >= 1. No Unity asset GUID or content/path matching."));
                    properties["presetVersion"] = D(("type", "integer"), ("minimum", 0), ("default", 0),
                        ("description", "Exact expected preset version. Zero/omitted only without a preset ID. Missing, invalid or differently versioned sources use the complete saved program and retain this reference."));
                    ((Dictionary<string, object>)definitions[name])["oneOf"] = new[] {
                        D(("properties", D(("presetId", D(("enum", new object[] { null, "" }))), ("presetVersion", D(("const", 0)))))),
                        D(("required", new[] { "presetId", "presetVersion" }), ("properties", D(
                            ("presetId", D(("type", "string"), ("pattern", "^(?:[0-9a-fA-F]{32}|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$"),
                                ("not", D(("enum", new[] { "00000000000000000000000000000000", "00000000-0000-0000-0000-000000000000" }))))),
                            ("presetVersion", D(("minimum", 1))))))
                    };
                }
                if (type == typeof(WhimTexGradient))
                {
                    ((Dictionary<string, object>)definitions[name])["description"] = "Independent color model and scalar interpolation. colors/alphas/colorMode retain automatic data. Custom uses independent customKeys/customColorMode; inactive automatic and manual data remain stored in every write mode. huePath, smoothness, midpoint and chromaCorrection do not change Custom curves. Retired mode and transition fields are unsupported.";
                    ((Dictionary<string, object>)properties["colorMode"])["default"] = "Perceptual";
                    ((Dictionary<string, object>)properties["interpolation"])["default"] = "Balanced";
                    ((Dictionary<string, object>)properties["huePath"])["default"] = "Shortest";
                    properties["chromaCorrection"] = D(("type", "number"), ("minimum", 0), ("maximum", 1), ("default", 0));
                    properties["smoothness"] = D(("type", "number"), ("minimum", 0), ("maximum", 1), ("default", 1),
                        ("description", "Used only by Blend/Balanced. Smooth, Adaptive, Fixed and Custom retain but ignore it."));
                    properties["colorSpace"] = D(("type", "string"), ("enum", new[] { "Gamma", "Linear" }), ("default", "Gamma"),
                        ("description", "Technical encoding of stored color keys, independent of colorMode."));
                    ((Dictionary<string, object>)properties["wrapMode"])["default"] = "Clamp";
                    properties["customKeys"] = D(("type", new[] { "array", "null" }), ("maxItems", 256), ("default", null),
                        ("items", Schema(typeof(WhimTexGradientCurveKey))),
                        ("description", "Optional retained manual data. When nonempty: one to 64 keys in each channel, at most 64 distinct color times across channels 0..2, unique positive IDs, channel times increasing by at least 0.000001. Keys are independent. Markers cluster all channels sorted by time/id within 0.00001 of each earliest anchor, not transitively; color/alpha filtering happens inside these clusters. Groups are never serialized; the obsolete link field is rejected. Empty/null allowed only outside Custom."));
                    properties["customColorMode"] = D(("type", "string"), ("enum", Enum.GetNames(typeof(WhimTexGradientColorMode))), ("default", "Perceptual"),
                        ("description", "Required with nonempty customKeys. Linear/Gamma use RGB, Perceptual uses L/a/b, Hue uses L/C/H; alpha is channel 3. Color conversion must explicitly refit the curves; changing this field reinterprets values."));
                    properties["customApproximationError"] = D(("type", "number"), ("minimum", 0), ("default", 0),
                        ("description", "Saved conversion diagnostic: maximum observed relative RGB/alpha sample deviation, not a certified bound. It does not affect evaluation."));
                    var customSelected = D(("required", new[] { "interpolation" }),
                        ("properties", D(("interpolation", D(("const", "Custom"))))));
                    var hasManualKeys = D(("required", new[] { "customKeys" }),
                        ("properties", D(("customKeys", D(("type", "array"), ("minItems", 1))))));
                    properties["customCurveFormat"] = D(("type", "integer"), ("enum", new[] { 0, 1 }), ("default", 0), ("description", "1 is required with Custom Bezier keys; 0 means no manual data."));
                    var requiresManualKeys = D(("required", new[] { "customKeys", "customColorMode", "customCurveFormat" }),
                        ("properties", D(("customKeys", D(("type", "array"), ("minItems", 4))))));
                    ((Dictionary<string, object>)definitions[name])["allOf"] = new[] {
                        D(("if", customSelected), ("then", requiresManualKeys)),
                        D(("if", hasManualKeys), ("then", D(("required", new[] { "customColorMode", "customCurveFormat" }), ("properties", D(("customCurveFormat", D(("const", 1))))))))
                    };
                }
                if (type == typeof(GradientLayerBehaviour))
                {
                    ((Dictionary<string, object>)properties["mesh"])["description"] = "Independent Mesh data, retained in every JSON write mode while inactive. Required non-null when gradientType is Mesh. Mesh is supported only by Gradient layers.";
                    var meshSelected = D(("required", new[] { "gradientType" }), ("properties", D(("gradientType", D(("const", "Mesh"))))));
                    var meshRequired = D(("required", new[] { "mesh" }), ("properties", D(("mesh", D(("type", "object"))))));
                    ((Dictionary<string, object>)definitions[name])["allOf"] = new[] { D(("if", meshSelected), ("then", meshRequired)) };
                }
                if (type == typeof(MeshGradient))
                    ((Dictionary<string, object>)definitions[name])["description"] = "Tensor-product cubic Bezier lattice. columns/rows are cell counts; positions and colors must each have (3*columns+1)*(3*rows+1) entries in row-major order. positions are finite local coordinates, unrestricted by 0..1 or folding. nodeModes has (columns+1)*(rows+1) row-major Smooth/Corner entries, or null for all Smooth; new nodes default to Smooth. colors are working coefficients: linear RGB for Linear, encoded RGB for Gamma, OKLab for Perceptual/Hue; alpha is a straight cubic coefficient. Hue requires a separate hueChroma lattice of equal length, subdivided with colors; circular mixing favors the minor arc and fades to Perceptual on conflict, without huePath. Coefficients may overshoot; only evaluated alpha is clamped. Keep every coefficient and node mode to preserve subdivision and editing behavior. Overlaps use normalized color/opacity blending with soft transitions near folds and exposed edges, without accumulating alpha. Single-surface regions retain their color and opacity. Runtime validation rejects nonfinite values, malformed arrays and unsupported modes.";
                if (type == typeof(WhimTexGradientCurveKey))
                {
                    properties["id"] = D(("type", "integer"), ("minimum", 1), ("maximum", int.MaxValue));
                    properties["channel"] = D(("type", "integer"), ("minimum", 0), ("maximum", 3), ("default", 0));
                    properties["time"] = D(("type", "number"), ("minimum", 0), ("maximum", 1), ("default", 0));
                    foreach (string side in new[] { "inMode", "outMode" })
                        ((Dictionary<string, object>)properties[side])["description"] = "Free, ClampedAuto, Linear, Constant (previous value) or ConstantNext (next value). Joined sides must both be Free or ClampedAuto. Weighted Bezier evaluation uses AnimationCurve.";
                    foreach (string weight in new[] { "inWeight", "outWeight" })
                        properties[weight] = D(("type", "number"), ("minimum", 0), ("maximum", 1), ("default", 1f / 3), ("description", "Fraction of adjacent time span; unweighted sides use 1/3."));
                    ((Dictionary<string, object>)definitions[name])["required"] = new[] { "id" };
                    var alphaChannel = D(("required", new[] { "channel" }), ("properties", D(("channel", D(("const", 3))))));
                    var alphaValue = D(("properties", D(("value", D(("type", "number"), ("minimum", 0), ("maximum", 1))))));
                    ((Dictionary<string, object>)definitions[name])["allOf"] = new[] { D(("if", alphaChannel), ("then", alphaValue)) };
                }
                if (type == typeof(Layer))
                    ((Dictionary<string, object>)properties["fx"])["description"] = "Document-owned shared Shader programs in Code or Graph mode. Both modes and a complete preset fallback are embedded. Internal $ref identities are supported; external Material, Shader and ShaderFX asset references are rejected.";
                if (type == typeof(ShapeLayerBehaviour) || type == typeof(VectorShape))
                {
                    var paintConstraints = new List<object>();
                    foreach (bool stroke in new[] { false, true })
                    {
                        string mode = stroke ? "strokeMode" : "fillMode";
                        string palette = stroke ? "strokeGradient" : "gradient";
                        string start = palette + "Start", end = palette + "End", repetitions = palette + "Repetitions";
                        ((Dictionary<string, object>)properties[mode])["default"] = "Solid";
                        ((Dictionary<string, object>)properties[mode])["description"] = stroke
                            ? "Independent stroke paint: None disables painting without clearing settings; Solid uses strokeColor; four gradient modes use strokeGradient; Freeform uses strokeFreeform. The stroke boolean remains an additional gate. Every JSON mode retains inactive stroke settings."
                            : "Shared figure fill: None=0, Solid=1, LinearGradient=2, RadialGradient=3, SquareGradient=4, AngularGradient=6, Freeform=7. Freeform uses the independent freeform point field. Shape fill is an additional overall gate. Full, FullOptimized and Compact retain inactive fill settings.";
                        ((Dictionary<string, object>)properties[palette])["description"] = "Independent WhimTexGradient palette, default black-to-white Perceptual/Balanced/Clamp. Required non-null in gradient modes, even with painting disabled. Retains HDR colors, alpha, midpoints, colorMode, interpolation, huePath, chromaCorrection, smoothness, colorSpace and wrapMode; wrapping follows the angular-only " + repetitions + " multiplier.";
                        ((Dictionary<string, object>)properties[start])["default"] = new[] { 0, 0 };
                        ((Dictionary<string, object>)properties[start])["description"] = "Finite normalized layer-local y-up anchor, allowed outside 0..1. Linear start; center for other gradients. Pixel metric: Shape current canvas dimensions before layer Transform; Vector and Boolean Group referenceSize. Changing this anchor does not change the layer transform or the other paint.";
                        ((Dictionary<string, object>)properties[end])["default"] = new[] { 1, 0 };
                        ((Dictionary<string, object>)properties[end])["description"] = "Finite normalized layer-local endpoint. Sets radius and orientation from " + start + " in reference pixels. Radial uses length, Square max(abs(rotated coordinates)). Angular is the positive CCW angle from this ray divided by one turn and multiplied by " + repetitions + ", with its seam on the ray. Equal anchors yield t=0 for every gradient type.";
                        properties[repetitions] = D(("type", "number"), ("minimum", .0001), ("maximum", float.MaxValue), ("default", 1),
                            ("description", "Finite angular-gradient repetitions, at least 0.0001; default 1. AngularGradient only: one period spans 360/" + repetitions + " degrees before palette wrapping. Retained and validated in every paint mode, including disabled painting."));
                        var invalidPalette = D(("required", new[] { mode, palette }),
                            ("properties", D((mode, D(("enum", new[] { "LinearGradient", "RadialGradient", "SquareGradient", "AngularGradient" }))),
                                (palette, D(("type", "null"))))));
                        paintConstraints.Add(D(("not", invalidPalette)));
                        string freeform = stroke ? "strokeFreeform" : "freeform";
                        paintConstraints.Add(D(("not", D(("required", new[] { mode, freeform }),
                            ("properties", D((mode, D(("const", "Freeform"))), (freeform, D(("type", "null")))))))));
                        ((Dictionary<string, object>)properties[freeform])["description"] = "Independent Freeform points in normalized layer-local coordinates. Retained in inactive modes; required non-null when Freeform is active, including disabled painting.";
                    }
                    ((Dictionary<string, object>)definitions[name])["allOf"] = paintConstraints;
                }
                if (type == typeof(ShapeLayerBehaviour))
                {
                    ((Dictionary<string, object>)properties["strokePosition"])["default"] = "Inside";
                    ((Dictionary<string, object>)properties["strokePosition"])["description"] = "Relative to the body contour. Shape retains Inside=0, Center=1, Outside=2; Vector uses a different numeric ordering.";
                    ((Dictionary<string, object>)properties["strokeJoin"])["default"] = "Round";
                    ((Dictionary<string, object>)properties["strokeJoin"])["description"] = "Outline join, independent of the body's corner descriptors.";
                    ((Dictionary<string, object>)properties["strokeCap"])["default"] = "Butt";
                    ((Dictionary<string, object>)properties["strokeCap"])["description"] = "Ends of stroke dashes. Separate from lineCap, which shapes the Line/Arc body ends.";
                    properties["miterLimit"] = D(("type", "number"), ("minimum", 1), ("maximum", 10000), ("default", 4),
                        ("description", "Maximum miter ratio before a Miter join becomes Bevel. Retained for other joins."));
                    properties["dashPattern"] = D(("type", new[] { "array", "null" }), ("maxItems", 256), ("default", new float[0]),
                        ("items", D(("type", "number"), ("exclusiveMinimum", 0), ("maximum", 10000000))),
                        ("description", "Alternating dash/gap lengths, always in multiples of strokeWidth, independent of stroke position. Empty/null is solid; odd lists repeat twice. Shape has no dashPatternRelative field."));
                    properties["dashOffset"] = D(("type", "number"), ("minimum", -10000000), ("maximum", 10000000), ("default", 0),
                        ("description", "Phase along the contour in document pixels, never a stroke-width multiplier. Shape retains its transform metric: ordinary resizing keeps width, dash metrics and feather in document pixels; perspective retains the existing local approximation. Vector scales its local stroke geometry."));
                    ((Dictionary<string, object>)properties["lineCap"])["description"] = "Line/Arc body end caps. Independent of strokeCap and the outline dash pattern.";
                }
                if (type == typeof(VectorLayerBehaviour))
                {
                    properties["feather"] = D(("type", "number"), ("minimum", 0), ("maximum", 8192), ("default", 0),
                        ("description", "Finite total soft transition width in vector reference pixels; scales with the vector geometry like stroke width. Smoothstep of signed distance to fill and actual stroke/dash boundaries, not a blur. Zero preserves the existing renderer. Step ignores Feather without clearing either stored setting."));
                    properties["featherPosition"] = D(("type", "string"), ("enum", Enum.GetNames(typeof(VectorFeatherPosition))), ("default", "Centered"),
                        ("description", "Inside fades inward, Outside outward, Centered equally across the painted boundary. Layer-wide setting, not a VectorShape or Boolean Group style field. Retained when Feather is zero or Edge Mode is Step."));
                    properties["referenceSize"] = D(("type", "array"), ("minItems", 2), ("maxItems", 2),
                        ("items", D(("type", "number"), ("exclusiveMinimum", 0), ("maximum", 10000000))),
                        ("default", new[] { 256, 256 }), ("description", "Physical document pixel size at creation. Coordinates and tangent offsets are layer-local y-up; stroke widths, dash offsets and Feather use these reference pixels. Relative dash lengths multiply strokeWidth. All follow the layer transform."));
                    ((Dictionary<string, object>)properties["brushDraft"])["description"] = "Last stroke's original coverage and editable rebuild controls. Retained in all write modes, Undo and TIFF until the next stroke replaces it.";
                    properties["shapes"] = D(("type", "array"), ("maxItems", 4096), ("items", D(("$ref", "#/$defs/VectorShape"))),
                        ("description", "One figure per authored layer; multiple contours, including holes, belong to that figure. Older arrays with several figures are accepted only for normalization into an ordinary group with one figure per child, retaining the original shell and shared transform/FX."));
                }
                if (type == typeof(FileLayerBehaviour))
                {
                    properties["externalSourcePath"] = D(("type", new[] { "string", "null" }),
                        ("description", "Absolute linked external PNG/JPEG/TGA/BMP/EXR or compatible RGBA TIFF path; sourceTexture is null. Pixels are decoded on demand, cached per document and never embedded. Missing paths remain stored. Project sources use sourceTexture and leave this null."));
                }
                if (type == typeof(BooleanGroupLayerBehaviour))
                {
                    ((Dictionary<string, object>)definitions[name])["description"] = "Layer.children contain closed Shape, Vector, editable Text or nested Boolean Group operands. Text retains TextLayerBehaviour settings and uses original source-font outlines; missing or unsupported outlines fail explicitly, without raster tracing. Bottom enabled child is the base; higher children apply booleanOperation upward. Combine retains separate figures; appearance Group shares this group's styling, Layers uses original child appearance. Other Boolean operations always use the group style. Child FX, opacity and blend modes remain ignored; group FX apply to the result. Isolated only; raster operands and open contours fail explicitly.";
                    properties["referenceSize"] = D(("type", "array"), ("minItems", 2), ("maxItems", 2),
                        ("items", D(("type", "number"), ("exclusiveMinimum", 0), ("maximum", 10000000))), ("default", new[] { 256, 256 }));
                    properties["style"] = D(("type", "object"), ("allOf", new object[] { Schema(typeof(VectorShape)),
                        D(("properties", D(("contours", D(("type", "array"), ("maxItems", 0)))))) }),
                        ("description", "Common fill/stroke settings. Geometry comes from children; style.contours must be empty. No continuous inheritance from child appearance."));
                }
                if (type == typeof(Layer))
                {
                    ((Dictionary<string, object>)properties["transform"])["description"] = "Omitted transform.tiling defaults to Source for every layer. The source declares its stable policy: geometry and procedural generators use Unbounded; Drawing, Text, groups and image effects use Clip; File inherits its bound texture's U/V wrap modes. Never inferred from preview or render-cache textures. Explicit modes, including Clip, survive Compact writing and Shape/Text conversion.";
                    properties["booleanOperation"] = D(("type", "string"), ("enum", Enum.GetNames(typeof(VectorBooleanOperation))), ("default", "Union"),
                        ("description", "Operation when this layer is an operand of a Boolean Group. Bottom enabled operand is the base, so its operation is ignored. Retained outside Boolean Groups."));
                }
                if (type == typeof(TextLayerBehaviour))
                {
                    properties["fillMode"] = D(("type", "string"), ("enum", Enum.GetNames(typeof(VectorFillMode)).Where(n => n != "Freeform").ToArray()), ("default", "Solid"));
                    properties["gradientScope"] = D(("type", "string"), ("enum", Enum.GetNames(typeof(TextGradientScope))), ("default", "Text"),
                        ("description", "Text normalizes one field over all glyph bounds; Character repeats shared anchors over each nonempty glyph rectangle, not advance or whitespace. Paint changes do not rebuild glyph coverage. Original Edge Mode remains in control."));
                    properties["gradientRepetitions"] = D(("type", "number"), ("minimum", .0001), ("default", 1));
                    properties["fallbackPaint"] = D(("type", new[] { "string", "null" }), ("maxLength", 1048576), ("contentEncoding", "base64"),
                        ("description", "Optional missing-font Character paint snapshot. Deflate-compressed little-endian RGBA float32, bottom-up, exactly fallbackPaintWidth*fallbackPaintHeight*16 decoded bytes; at most 768 KiB compressed. Full float precision including HDR and alpha. Bound to fallbackPaintKey; cannot be repainted without glyphs."));
                    foreach (string field in new[] { "fallbackPaintWidth", "fallbackPaintHeight" })
                        properties[field] = D(("type", "integer"), ("minimum", 0), ("maximum", 2048));
                }
                if (type == typeof(TextureTransform))
                {
                    var tiling = (Dictionary<string, object>)properties["tiling"];
                    tiling["default"] = "Source";
                    tiling["description"] = "Source delegates to the layer source's intrinsic tiling policy, not render-cache texture metadata. Explicit modes override it. Reset restores Source.";
                }
                if (type == typeof(VectorPoint) || type == typeof(VectorContour) || type == typeof(VectorShape))
                    properties["id"] = D(("type", "string"), ("minLength", 1), ("maxLength", 256));
                if (type == typeof(VectorShape))
                {
                    properties["contours"] = D(("type", "array"), ("maxItems", 8192), ("items", D(("$ref", "#/$defs/VectorContour"))));
                    properties["strokeWidth"] = D(("type", "number"), ("minimum", 0), ("maximum", 10000000));
                    properties["miterLimit"] = D(("type", "number"), ("minimum", 1), ("maximum", 10000));
                    properties["dashOffset"] = D(("type", "number"), ("minimum", -10000000), ("maximum", 10000000));
                    properties["dashPattern"] = D(("type", new[] { "array", "null" }), ("maxItems", 256),
                        ("items", D(("type", "number"), ("exclusiveMinimum", 0), ("maximum", 10000000))),
                        ("description", "Alternating dash/gap lengths. Set dashPatternRelative:true for multiples of strokeWidth; false retains reference-pixel lengths. Empty/null is solid; odd lists repeat twice."));
                    properties["dashPatternRelative"] = D(("type", "boolean"), ("default", false),
                        ("description", "True: dashPattern values multiply strokeWidth, independent of stroke position. The inspector authors this mode. False/omitted preserves older pixel patterns; the inspector converts them on pattern/width edits. dashOffset always remains reference pixels."));
                    foreach (string color in new[] { "fillColor", "strokeColor" })
                        properties[color] = D(("type", "array"), ("minItems", 4), ("maxItems", 4),
                            ("prefixItems", new[] { Number(typeof(float)), Number(typeof(float)), Number(typeof(float)), D(("type", "number"), ("minimum", 0), ("maximum", 1)) }));
                }
                if (type == typeof(VectorContour))
                    properties["points"] = D(("type", "array"), ("maxItems", 65536), ("items", D(("$ref", "#/$defs/VectorPoint"))));
                if (type == typeof(VectorBrushDraft))
                {
                    ((Dictionary<string, object>)definitions[name])["required"] = new[] { "width", "height", "coverage", "shapeId" };
                    properties["width"] = D(("type", "integer"), ("minimum", 1), ("maximum", 1024));
                    properties["height"] = D(("type", "integer"), ("minimum", 1), ("maximum", 1024));
                    properties["coverage"] = D(("type", "string"), ("contentEncoding", "base64"), ("maxLength", 1398104),
                        ("description", "Original unmodified coverage bytes in bottom-up row-major order, exactly width*height bytes after base64 decoding; at most 1048576 bytes. Not a rendered preview."));
                    properties["shapeId"] = D(("type", "string"), ("minLength", 1), ("maxLength", 128));
                    properties["tolerance"] = D(("type", "number"), ("minimum", .25), ("maximum", 64), ("default", 1));
                    properties["expansion"] = D(("type", "number"), ("minimum", -256), ("maximum", 256), ("default", 0));
                    properties["frame"] = D(("type", "array"), ("minItems", 4), ("maxItems", 4), ("default", new[] { 0, 0, 1, 1 }),
                        ("prefixItems", new object[] { Number(typeof(float)), Number(typeof(float)),
                            D(("type", "number"), ("exclusiveMinimum", 0)), D(("type", "number"), ("exclusiveMinimum", 0)) }),
                        ("description", "Original mask rectangle [x,y,width,height] in normalized layer-local y-up coordinates. Reframing a figure updates this mapping, not the coverage bytes."));
                }
                if (type == typeof(WhimTexDocument))
                {
                    var lights = (Dictionary<string, object>)properties["lights"];
                    lights["maxItems"] = WhimTexDocument.MaximumLights;
                    lights["description"] = "Document lights shared by FX and Shader Graph. Bottom-left canvas-pixel XY and height Z; HDR color, no inter-layer shadows. Stable IDs; list order is the zero-based shader index, including disabled sources.";
                    var notes = (Dictionary<string, object>)properties["comments"];
                    properties["commentNumberSequence"] = D(("type", "integer"), ("minimum", 0), ("maximum", int.MaxValue),
                        ("description", "Highest assigned comment number. Retained after deletion, omitted from layer fragments. Zero starts a fresh sequence."));
                    notes["maxItems"] = WhimTexDocument.MaximumCommentThreads;
                    notes["description"] = "Optional local canvas threads, omitted when empty and from layer fragments. Anchors use bottom-left UV and may lie outside the canvas after Crop. TIFF stores a separate optional comments block; annotations never alter composite pixels.";
                    var receipts = (Dictionary<string, object>)properties["agentReceipts"];
                    receipts["maxItems"] = 2048;
                    receipts["description"] = "Document-owned work/request receipts prevent duplicate application after transport failures. Retained by TIFF/JSON and Undo, omitted from layer fragments and when empty. Combined metadata limit 4 MiB.";
                }
                if (type == typeof(WhimTexComment))
                {
                    properties["resolved"] = D(("type", "boolean"), ("default", false),
                        ("description", "Resolved status. Mutually exclusive with awaitingReview; both false means In Progress."));
                    properties["awaitingReview"] = D(("type", "boolean"), ("default", false),
                        ("description", "Awaiting Review status. Explicit task state, never changed by opening or reading a thread."));
                    ((Dictionary<string, object>)definitions[name])["not"] = D(("required", new[] { "resolved", "awaitingReview" }),
                        ("properties", D(("resolved", D(("const", true))), ("awaitingReview", D(("const", true))))));
                    properties["number"] = D(("type", "integer"), ("minimum", 0), ("maximum", int.MaxValue),
                        ("description", "Permanent document-local display number. Zero/omitted is assigned once in list order after the highest existing number. Assigned positive values must be unique."));
                    properties["useAuthorColor"] = D(("type", "boolean"), ("default", false),
                        ("description", "Track the creator's profile color. Omitted/false preserves the stored manual color, including older comments. The color field retains the latest portable snapshot."));
                    properties["colorAuthorId"] = D(("type", new[] { "string", "null" }), ("pattern", "^([0-9a-fA-F]{32})?$"),
                        ("description", "Optional local author profile GUID for automatic color updates across nickname changes. Not a Unity account or project ID."));
                    var rgb = D(("type", "number"), ("minimum", 0), ("maximum", 1));
                    properties["color"] = D(("type", "array"), ("minItems", 4), ("maxItems", 4),
                        ("prefixItems", new object[] { rgb, rgb, rgb, D(("const", 1)) }));
                    var messages = (Dictionary<string, object>)properties["messages"];
                    messages["type"] = "array"; messages["minItems"] = 1; messages["maxItems"] = WhimTexDocument.MaximumCommentMessages;
                    properties["id"] = D(("type", "string"), ("minLength", 1), ("pattern", @"\S"));
                    ((Dictionary<string, object>)definitions[name])["required"] = new[] { "id", "messages" };
                }
                if (type == typeof(WhimTexCommentMessage))
                {
                    properties["authorId"] = D(("type", new[] { "string", "null" }), ("pattern", "^([0-9a-fA-F]{32})?$"),
                        ("description", "Optional local author profile GUID, independent of the discussion color."));
                    var rgb = D(("type", "number"), ("minimum", 0), ("maximum", 1));
                    properties["authorColor"] = D(("anyOf", new object[] { D(("const", new[] { 0, 0, 0, 0 })),
                        D(("type", "array"), ("minItems", 4), ("maxItems", 4),
                            ("prefixItems", new object[] { rgb, rgb, rgb, D(("const", 1)) })) }),
                        ("default", new[] { 0, 0, 0, 0 }), ("description", "Portable author color snapshot. All-zero means absent: resolve the local profile/name or use the author-name hue. The UI always displays an opaque author stripe."));
                    ((Dictionary<string, object>)properties["attachments"])["maxItems"] = WhimTexDocument.MaximumCommentAttachments;
                    properties["agentWorkId"] = D(("type", new[] { "string", "null" }), ("maxLength", 128));
                    properties["agentContentHash"] = D(("type", new[] { "string", "null" }), ("maxLength", 128),
                        ("description", "API provenance fingerprint. Editing message content invalidates its agent-only status for feedback guards."));
                    properties["id"] = D(("type", "string"), ("minLength", 1), ("pattern", @"\S"));
                    properties["author"] = D(("type", "string"), ("minLength", 1), ("maxLength", WhimTexDocument.MaximumCommentAuthorLength), ("pattern", @"\S"));
                    properties["text"] = D(("type", "string"), ("minLength", 1), ("maxLength", WhimTexDocument.MaximumCommentTextLength), ("pattern", @"\S"));
                    properties["createdUtcTicks"] = D(("type", "integer"), ("minimum", 0), ("maximum", DateTime.MaxValue.Ticks));
                    properties["editedUtcTicks"] = D(("type", "integer"), ("minimum", 0), ("maximum", DateTime.MaxValue.Ticks),
                        ("description", "Zero means never edited. Otherwise UTC ticks at or after createdUtcTicks; preserves the original author and creation time."));
                    ((Dictionary<string, object>)definitions[name])["required"] = new[] { "id", "text" };
                }
                if (type == typeof(WhimTexAgentReceipt))
                {
                    foreach (string field in new[] { "workId", "requestId", "requestHash" })
                        properties[field] = D(("type", "string"), ("minLength", 1), ("maxLength", 128), ("pattern", @"\S"));
                    properties["resultJson"] = D(("type", "string"), ("minLength", 1), ("maxLength", 262144));
                    properties["createdUtcTicks"] = D(("type", "integer"), ("minimum", 0), ("maximum", DateTime.MaxValue.Ticks));
                    ((Dictionary<string, object>)definitions[name])["required"] = new[] { "workId", "requestId", "requestHash", "resultJson", "createdUtcTicks" };
                }
                if (type == typeof(WhimTexCommentAttachment))
                {
                    properties["name"] = D(("type", "string"), ("minLength", 1), ("maxLength", 1024));
                    properties["layerId"] = D(("type", new[] { "string", "null" }), ("maxLength", 128));
                    properties["assetGuid"] = D(("type", new[] { "string", "null" }), ("description", "Persistent project asset GUID; localId identifies a sub-asset. Missing targets remain references."));
                    ((Dictionary<string, object>)definitions[name])["required"] = new[] { "name" };
                    ((Dictionary<string, object>)definitions[name])["description"] = "Exactly one nonempty layerId, assetGuid or nonnull mask. Layer/mask references use localId 0. Empty UI draft fields are omitted before storage.";
                }
                if (type == typeof(WhimTexCommentMask))
                {
                    properties["version"] = D(("const", 1));
                    properties["id"] = D(("type", "string"), ("pattern", "^[0-9a-fA-F]{32}$"));
                    foreach (string axis in new[] { "width", "height" }) properties[axis] = D(("type", "integer"), ("minimum", 1), ("maximum", 16384));
                    properties["coverageDeflate"] = D(("type", "string"), ("minLength", 1), ("maxLength", WhimTexDocument.MaximumCommentsBlockBytes));
                    properties["sha256"] = D(("type", "string"), ("minLength", 44), ("maxLength", 44));
                    ((Dictionary<string, object>)definitions[name])["required"] = new[] { "id", "width", "height", "coverageDeflate", "sha256" };
                    ((Dictionary<string, object>)definitions[name])["description"] = "Version 1: lossless Deflate/base64 R8 coverage, bottom row first, cropped to nonzero bounds. SHA-256/base64 checks decoded bytes. At most 16,777,216 pixels per mask and 67,108,864 per document. localToCanvas maps local bottom-left UV to current canvas UV, finite invertible affine only. Original pixels survive Crop/Scale/Orientation; consumers sample at pixel centres with nearest filtering. Mask IDs are unique in the document.";
                }
                if (type == typeof(SDFLayerBehaviour) || type == typeof(OutlineLayerBehaviour))
                {
                    var mode = (Dictionary<string, object>)properties["sharpMode"];
                    mode["default"] = "Unrestricted";
                    mode["description"] = SharpModeContract;
                    ((Dictionary<string, object>)properties["miterLimit"])["description"] = "Finite 1..64, default 4. In GuardedSharp, bevels joins above this ratio for Sharp, CornerFit, Spring, Rbf and RbfDual. Ignored in Unrestricted and MTSDF. No separate fixed angular rounding cap.";
                    var metric = (Dictionary<string, object>)properties["metric"];
                    metric["description"] = "All ten modes are antialiased. EuclideanAntialiased is the default Round AA, retaining ID 4 and the original crossing-seed EDT rather than distance to continuous contour segments. Sharp/Bevel are contour-offset fields, not Euclidean SDFs. Diamond is axis-dependent L1; Square is axis-dependent L-infinity. Sharp/Bevel/Diamond/Square/MTSDF use fixed 0.25-pixel contour simplification. Sharp/Bevel/Diamond/Square/MTSDF fit straight flanks to multiple raw isocontour samples by orthogonal least squares before simplification; sampled flank deviation <= 0.6 px and supported acute caps have an engineering allowance <= sqrt(0.5) px and tip displacement within the measured 8-pixel base support, only when independently straight 16-pixel flanks agree with the base fits and sqrt(0.5) px / cosHalfTurn fits in that support; otherwise tip distance <= min(2 px, 0.6 px / cosHalfTurn). Uncertain flanks are preserved. Closed filled contours undergo NonZero union at 1e-6-pixel precision; holes and genuinely separate islands remain, while regions overlapping after reconstruction share one boundary. Paths with perimeter below 4 px bypass simplification; open and zero-area contours bypass region union. Post-union cleanup uses a 0.05-pixel budget and rejects chords crossing nonincident original or replacement boundaries or changing containment/contact states. MTSDF resolves each contour median before combining contours; alpha remains the nearest true distance. Away from the contour, a common-channel bound certifies safe RGB interpolation; suspect cells receive nine finite-contour distance checks. Geometric sign/amplification errors fall back to true distance, color-only incompatibility preserves the scalar median, and alpha is unchanged. Scalar MTSDF uses the corrected RGB median; near-boundary texels are unchanged. Reconstruction may alter subpixel detail and is not an exact original matte. MTSDF has corner-preserving RGB perpendicular/pseudo distances plus true signed Euclidean subpixel-contour distance in alpha. Old IDs 0..3 are removed.";
                }
                if (type == typeof(SDFLayerBehaviour) || type == typeof(OutlineLayerBehaviour))
                {
                    var metric = (Dictionary<string, object>)properties["metric"];
                    metric["default"] = "EuclideanAntialiased";
                    metric["description"] = ((string)metric["description"]).Replace("All ten modes", "All twelve modes") + RasterGuardContract + AaedtContract + ContourContract + ExperimentalCornerContract + RbfContract + RbfDualContract + SharpModeContract;
                }
                if (type == typeof(SDFLayerBehaviour))
                {
                    ((Dictionary<string, object>)properties["threshold"])["description"] = "Byte threshold 0..255. In AAEDT, normalize to t=threshold/255 and remap coverage monotonically so t maps to 0.5 while 0/1 remain unchanged. Extreme thresholds use binary coverage: 1 only when the selected byte-range source value exceeds threshold, otherwise 0. CornerFit and Spring also support the threshold through coverage remapping; fitting is approximate for soft masks or values that do not represent area coverage. Remaining modes keep their existing threshold behavior.";
                    var encoding = (Dictionary<string, object>)properties["encoding"];
                    encoding["description"] = "Gradient is the default. Gradient/LinearData use the RGB median for MTSDF before artistic remapping. MultiChannelData requires metric MTSDF and encodes raw linear RGBA as saturate(0.5 + 0.5*d/maxDistanceNormalization) (0 selects auto). Alpha is true signed Euclidean distance to the reconstructed subpixel contour, not coverage, binary-bitmap distance or an exact original matte. MTSDF uses constrained multi-sample corner fitting followed by fixed 0.25-pixel simplification. Sampled flank deviation <= 0.6 px; supported acute caps have an engineering allowance <= sqrt(0.5) px and tip displacement within the measured 8-pixel base support, only when independently straight 16-pixel flanks agree with the base fits and sqrt(0.5) px / cosHalfTurn fits in that support; otherwise tip distance <= min(2 px, 0.6 px / cosHalfTurn). Closed filled contours undergo NonZero union before generating edge colors. Holes and genuinely separate islands remain; overlapping reconstructed regions share one boundary. Tiny paths bypass simplification; open and zero-area contours bypass union. MTSDF resolves each contour median before combining contours; alpha remains the nearest true distance. Away from the contour, a common-channel bound certifies safe RGB interpolation; suspect cells receive nine finite-contour distance checks. Geometric sign/amplification errors fall back to true distance, color-only incompatibility preserves the scalar median, and alpha is unchanged. Scalar MTSDF uses the corrected RGB median; near-boundary texels are unchanged. Use blendMode Overwrite for raw RGBA composition/export because Normal treats alpha as coverage and can alter data. Standalone ordinary FX/effect-layer inputs receive rendered RGBA. Position, Contour Offset, inside/outside ranges, Inverted, Profile and palette are bypassed.";
                    encoding["description"] = (string)encoding["description"] + RasterGuardContract + SharpModeContract;
                    var scalar = D(("properties", D(("encoding", D(("not", D(("const", "MultiChannelData"))))))));
                    var mtsdf = D(("required", new[] { "metric" }),
                        ("properties", D(("metric", D(("const", "MTSDF"))))));
                    ((Dictionary<string, object>)definitions[name])["anyOf"] = new[] { scalar, mtsdf };
                }
            }
            object modelRef = D(("$ref", "#/$defs/" + name));
            if (type == typeof(ShaderFX)) return D(("anyOf", new[] { modelRef, reference, D(("type", "null")) }));
            return !type.IsValueType && type != typeof(WhimTexDocument) && type != typeof(Layer) &&
                type != typeof(WhimTexComment) && type != typeof(WhimTexCommentMessage) && type != typeof(WhimTexCommentAttachment) && type != typeof(WhimTexAgentReceipt)
                ? D(("anyOf", new[] { modelRef, D(("type", "null")) })) : modelRef;
        }
        object document = Schema(typeof(WhimTexDocument)), layers = D(("type", "array"), ("maxItems", 1024), ("items", Schema(typeof(Layer))));
        var schema = D(("$schema", "https://json-schema.org/draft/2020-12/schema"), ("title", "WhimTex document v" + WhimTexDocumentJson.Version),
            ("type", "object"), ("additionalProperties", false), ("required", new[] { "format", "version", "layers" }),
            ("properties", D(("format", D(("const", WhimTexDocumentJson.Format))), ("version", D(("const", WhimTexDocumentJson.Version))),
                ("writeMode", Schema(typeof(WhimTexJsonWriteMode))),
                ("document", document), ("layers", layers))), ("$defs", definitions));
        var json = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        var formatting = Type.GetType("Newtonsoft.Json.Formatting, Newtonsoft.Json", true);
        string text = (string)json.GetMethod("SerializeObject", new[] { typeof(object), formatting }).Invoke(null, new[] { schema, Enum.ToObject(formatting, 1) });
        File.WriteAllText("Packages/com.dcfapixels.whimtex/Documentation~/AI/document.schema.json", text + "\n");
        return "Generated shared JSON schema: " + definitions.Count + " model types.";
    }
}
