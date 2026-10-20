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
                return D(("anyOf", assembly.GetTypes().Where(t => !t.IsAbstract && type.IsAssignableFrom(t)).Select(t =>
                    D(("allOf", new[] { Schema(t), D(("type", "object"), ("required", new[] { "$type" })) }))).ToArray()));
            if (type.Assembly != assembly) throw new Exception("Unsupported schema type: " + type);
            string name = Name(type);
            if (!definitions.ContainsKey(name))
            {
                var properties = new Dictionary<string, object>();
                definitions[name] = D(("type", "object"), ("additionalProperties", false), ("properties", properties));
                if (type == typeof(GradientLayerBehaviour)) ((Dictionary<string, object>)definitions[name])["required"] = new[] { "gradientType" };
                if (type == typeof(Layer)) ((Dictionary<string, object>)definitions[name])["required"] = new[] { "id", "behaviour" };
                properties["$type"] = D(("const", name));
                if (type == typeof(ShaderFX))
                { properties["$id"] = D(("type", "string")); properties["$name"] = D(("type", "string")); }
                if (type == typeof(DrawingLayerBehaviour)) properties["contentOmitted"] = D(("const", true));
                foreach (FieldInfo f in (FieldInfo[])fields.Invoke(null, new object[] { type }))
                    if (!(type == typeof(WhimTexDocument) && f.Name == "layers") && !(type == typeof(DrawingLayerBehaviour) && f.Name == "pixels"))
                        properties[f.Name] = type == typeof(FreeformGradient) && f.Name == "points"
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
                            type == typeof(ShaderFXParameter) && f.Name == "pixelUnits"
                            ? D(("type", "boolean"), ("default", false), ("description", "Numeric @param px declaration; the retained logical value is multiplied by document pixelScale once when uploaded.")) :
                            type == typeof(ShaderFXParameterControl) && f.Name == "enableIf"
                            ? D(("type", "array"), ("default", Array.Empty<object>()), ("maxItems", 16),
                                ("items", Schema(typeof(ShaderFXEnableCondition))),
                                ("description", "Enclosing @enableif scalar comparisons, combined with AND. Inspector controls remain visible and become disabled when any condition fails; stored values and shader evaluation are unchanged.")) :
                            type == typeof(ShaderFXParameterControl) && f.Name == "containers"
                            ? D(("type", "array"), ("default", Array.Empty<object>()), ("maxItems", 16),
                                ("items", Schema(typeof(ShaderFXLayoutContainer))),
                                ("description", "Outer-to-inner HLSL inspector layout path: Group, Foldout, Tabs, Tab. Derived from metadata, independent of shader values. Selected tabs and foldout expansion are editor-local and not stored here.")) :
                            type == typeof(ShaderFXParameterControl) && f.Name == "previewParameters"
                            ? D(("type", "array"), ("default", Array.Empty<string>()), ("maxItems", 128),
                                ("items", D(("type", "string"), ("pattern", "^[A-Za-z_][A-Za-z0-9_]*$"))),
                                ("description", "Ordered @param names explicitly passed after float2 uv to previewFunction. Empty retains the UV-only/optional-own-value signature. Referenced types must match exactly.")) :
                            (type == typeof(ShaderFXParameter) || type == typeof(ShaderFXParameterControl)) && f.Name == "cyclicRange"
                            ? D(("type", "boolean"), ("default", false), ("description", "Derived from [min % max] HLSL metadata. Float2 endpoints may cross the seam; float numeric edits repeat while slider dragging stays bounded. Two hard finite limits with min < max are required.")) :
                            type == typeof(Layer) && f.Name == "sourceCanvasSize"
                            ? D(("type", "array"), ("minItems", 2), ("maxItems", 2), ("default", new[] { 0, 0 }),
                                ("items", D(("type", "integer"), ("minimum", 0), ("maximum", 16384))),
                                ("anyOf", new[] { D(("const", new[] { 0, 0 })), D(("items", D(("type", "integer"), ("minimum", 1), ("maximum", 16384)))) }),
                                ("description", "Source reference canvas dimensions retained by Crop. [0,0] uses the current canvas; otherwise both dimensions must be positive and their product at most 16,777,216. Keeps Noise, ordinary Pattern and uninitialized Drawing sources at their original pixel scale. Not a source-image resolution, crop rectangle or rendering cache.")) :
                            type == typeof(Layer) && f.Name == "id" ? D(("type", "string"), ("minLength", 1)) : Schema(f.FieldType);
                if (type == typeof(Layer))
                    ((Dictionary<string, object>)properties["fx"])["description"] = "Document-owned HLSL effects only: inline code or marked .hlsl presets. Internal $ref identities are supported; external Material, Shader and ShaderFX asset references are rejected.";
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
                        ((Dictionary<string, object>)properties[palette])["description"] = "Independent WhimTexGradient palette, default black-to-white Perceptual/Clamp. Required non-null in gradient modes, even with painting disabled. Retains HDR colors, alpha, midpoints, interpolation, smoothness, colorSpace and wrapMode; wrapping follows the angular-only " + repetitions + " multiplier.";
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
