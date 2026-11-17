namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool usesExtended;

        Value EmitExtended(ShaderGraphNode node, string port)
        {
            string V(string name) => Input(node, name).text;
            string trigonometry = ShaderGraphNodes.UnaryTrigonometryFunction(node.kind);
            if (trigonometry != null)
            {
                var input = Input(node, "In");
                var type = Common(node, input);
                return new Value(type, $"{trigonometry}({Cast(input, type, node).text})");
            }
            switch (node.kind)
            {
                case ShaderGraphNodeKind.ConvertAngle:
                {
                    var input = Input(node, "In");
                    var type = Common(node, input);
                    double factor = ShaderGraphNodes.AngleConversionFactor(node.inputAngleUnit, node.outputAngleUnit);
                    return new Value(type, factor == 1 ? input.text : $"({input.text} * {factor.ToString("R", System.Globalization.CultureInfo.InvariantCulture)})");
                }
                case ShaderGraphNodeKind.LightCount:
                    result.contextDependencies |= ShaderContextDependencies.Lighting;
                    return new Value(ShaderGraphValueType.Float, "(float)WTLightCount()");
                case ShaderGraphNodeKind.LightDirection: case ShaderGraphNodeKind.LightColor: case ShaderGraphNodeKind.LightAttenuation:
                    result.contextDependencies |= ShaderContextDependencies.Lighting;
                    result.usesCanvasPosition = true;
                    string component = node.kind == ShaderGraphNodeKind.LightDirection ? "direction" :
                        node.kind == ShaderGraphNodeKind.LightColor ? "color" : "attenuation";
                    return new Value(node.kind == ShaderGraphNodeKind.LightAttenuation ? ShaderGraphValueType.Float : ShaderGraphValueType.Float3,
                        $"WTSampleLight((int)({V("Index")}),float3(GraphCanvasPosition({V("UV")})*_CanvasSize.xy,{V("Height")})).{component}");
                case ShaderGraphNodeKind.Sign: case ShaderGraphNodeKind.Saturate:
                case ShaderGraphNodeKind.Exponential: case ShaderGraphNodeKind.Log:
                case ShaderGraphNodeKind.Reciprocal: case ShaderGraphNodeKind.ReciprocalSquareRoot:
                case ShaderGraphNodeKind.SoftSign: case ShaderGraphNodeKind.InverseSoftSign:
                {
                    var input = Input(node, "In"); var type = Common(node, input);
                    string expression = Cast(input, type, node).text;
                    string code = node.kind switch
                    {
                        ShaderGraphNodeKind.Exponential => node.option == 1 ? $"exp2({expression})" : $"exp({expression})",
                        ShaderGraphNodeKind.Log => node.option == 1 ? $"log2({expression})" : node.option == 2 ? $"log10({expression})" : $"log({expression})",
                        ShaderGraphNodeKind.Reciprocal => node.option == 1 ? $"rcp({expression})" : $"(1.0 / ({expression}))",
                        ShaderGraphNodeKind.ReciprocalSquareRoot => $"rsqrt({expression})",
                        ShaderGraphNodeKind.SoftSign => $"(({expression}) / (1.0 + abs({expression})))",
                        ShaderGraphNodeKind.InverseSoftSign => $"(({expression}) / (1.0 - abs({expression})))",
                        _ => $"{node.kind.ToString().ToLowerInvariant()}({expression})"
                    };
                    return new Value(type, code);
                }
                case ShaderGraphNodeKind.Modulo: case ShaderGraphNodeKind.Arctangent2:
                {
                    bool atan = node.kind == ShaderGraphNodeKind.Arctangent2;
                    var a = Input(node, atan ? "Y" : "A"); var b = Input(node, atan ? "X" : "B");
                    var type = Common(node, a, b);
                    string x = Cast(a, type, node).text, y = Cast(b, type, node).text;
                    return new Value(type, atan ? $"atan2({x},{y})" : $"fmod({x},{y})");
                }
                case ShaderGraphNodeKind.CrossProduct:
                    return new Value(ShaderGraphValueType.Float3, $"cross({V("A")},{V("B")})");
                case ShaderGraphNodeKind.Projection: case ShaderGraphNodeKind.Rejection: case ShaderGraphNodeKind.SphereMask:
                {
                    bool sphere = node.kind == ShaderGraphNodeKind.SphereMask;
                    var a = Input(node, sphere ? "Coords" : "A"); var b = Input(node, sphere ? "Center" : "B");
                    var type = Common(node, a, b);
                    string x = Widen(a), y = Widen(b);
                    if (sphere) return new Value(ShaderGraphValueType.Float, $"GraphSphereMask({x},{y},{V("Radius")},{V("Hardness")})");
                    string projection = $"GraphVectorProjection({x},{y})" + (type == ShaderGraphValueType.Float ? ".x" : VectorSwizzle(ShaderGraphNodes.Components(type)));
                    return new Value(type, node.kind == ShaderGraphNodeKind.Rejection ? $"({Cast(a, type, node).text}-{projection})" : projection);
                    string Widen(Value value)
                    {
                        string text = Cast(value, type, node).text;
                        return type == ShaderGraphValueType.Float ? $"float4({text},0,0,0)" : Cast(new Value(type, text), ShaderGraphValueType.Float4, node).text;
                    }
                }
                case ShaderGraphNodeKind.FresnelEffect:
                    return new Value(ShaderGraphValueType.Float, $"GraphFresnelEffect({V("Normal")},{V("ViewDir")},{V("Power")})");
                case ShaderGraphNodeKind.Transform:
                    result.usesCanvasPosition = true;
                    bool implicitPosition = !incoming.ContainsKey(node.id + ":In") && node.FindInput("In")?.source != ShaderGraphInputSource.Value;
                    return new Value(ShaderGraphValueType.Float2, $"GraphTransformPosition({V("In")},{node.option},{(implicitPosition ? "true" : "false")})");
                case ShaderGraphNodeKind.Refract:
                {
                    usesExtended = usesProcedural = usesColor = true;
                    var refracted = ProceduralShared(node, ShaderGraphValueType.Float4,
                        () => $"GraphRefract({V("Incident")},{V("Normal")},{V("IORSource")},{V("IORMedium")},{node.option})");
                    return new Value(port == "Intensity" ? ShaderGraphValueType.Float : ShaderGraphValueType.Float3,
                        refracted.text + (port == "Intensity" ? ".w" : ".xyz"));
                }
                case ShaderGraphNodeKind.Reflection:
                {
                    var incident = Input(node, "Incident"); var normal = Input(node, "Normal");
                    var type = Common(node, incident, normal);
                    string i = Cast(incident, type, node).text, n = Cast(normal, type, node).text;
                    return new Value(type, $"reflect({i},{n})");
                }
                case ShaderGraphNodeKind.And: case ShaderGraphNodeKind.Or:
                    return new Value(ShaderGraphValueType.Boolean, $"({V("A")} {(node.kind == ShaderGraphNodeKind.And ? "&&" : "||")} {V("B")})");
                case ShaderGraphNodeKind.Not:
                    return new Value(ShaderGraphValueType.Boolean, $"(!({V("In")}))");
                case ShaderGraphNodeKind.NormalEncode:
                    return new Value(ShaderGraphValueType.Float3, $"(({V("Normal")})*0.5+0.5)");
                case ShaderGraphNodeKind.NormalDecode:
                    return new Value(ShaderGraphValueType.Float3, $"(({V("Normal")})*2.0-1.0)");
                case ShaderGraphNodeKind.Premultiply:
                {
                    string color = V("Color");
                    return new Value(ShaderGraphValueType.Float4, $"float4(({color}).rgb*({color}).a,({color}).a)");
                }
            }

            if (!ShaderGraphNodes.TryExtendedOutputType(node.kind, out var outputType))
                throw new ShaderGraphException(node.id, "Unknown extended node.");
            // ExtendedHelpers contains references to both shared helper blocks, even
            // when only one of its functions is called. Emit those blocks first.
            usesExtended = usesProcedural = usesColor = true;
            string text;
            switch (node.kind)
            {
                case ShaderGraphNodeKind.NormalStrength:
                    text = $"GraphNormalStrength({V("Normal")},{V("Strength")})"; break;
                case ShaderGraphNodeKind.NormalLighting:
                    if (node.option == 1)
                    {
                        globalLightingNodes.Add(node.id);
                        if (simplifiedPreviewLighting)
                        {
                            text = $"GraphNormalLighting({V("Color")},{V("Normal")},float3(-0.5,0.5,0.7071067812),float4(1,1,1,1),1,{V("Ambient")}," +
                                $"{V("SpecularColor")},{V("SpecularIntensity")},{V("Roughness")},false)";
                            break;
                        }
                        if (separateLighting && !sampledEvaluation && !bufferEvaluation && samplingDepth == 0 && loopFrames.Count == 0)
                        { text = EmitSeparateLighting(node).text; break; }
                        result.contextDependencies |= ShaderContextDependencies.Lighting;
                        result.usesCanvasPosition = true;
                        text = $"WTGlobalLighting(float3(GraphCanvasPosition({V("UV")})*_CanvasSize.xy,{V("Height")}),{V("Color")}," +
                            $"{V("Normal")},{V("Ambient")},({V("SpecularColor")}).rgb," +
                            $"{V("SpecularIntensity")}*({V("SpecularColor")}).a,{V("Roughness")})";
                        break;
                    }
                    text = $"GraphNormalLighting({V("Color")},{V("Normal")},{V("LightDirection")},{V("LightColor")},{V("Intensity")},{V("Ambient")}," +
                        $"{V("SpecularColor")},{V("SpecularIntensity")},{V("Roughness")},{V("SpecularOnly")})"; break;
                case ShaderGraphNodeKind.Unpremultiply:
                    text = $"GraphUnpremultiply({V("Color")})"; break;
                case ShaderGraphNodeKind.Flipbook:
                    text = $"GraphFlipbook({V("UV")},{V("Columns")},{V("Rows")},{V("Frame")},{node.option})"; break;
                case ShaderGraphNodeKind.Dither:
                    text = $"GraphDither({V("Color")},{V("UV")},{V("Levels")},{V("PixelSize")},{node.option})"; break;
                default: throw new ShaderGraphException(node.id, "Unknown extended node.");
            }
            return new Value(outputType, text);
        }

        static string ExtendedHelpers => ShaderGraphSharedSource.Read("Extended");
    }
}
