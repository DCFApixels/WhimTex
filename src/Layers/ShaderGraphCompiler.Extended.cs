namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool usesExtended;

        Value EmitExtended(ShaderGraphNode node)
        {
            string V(string name) => Input(node, name).text;
            switch (node.kind)
            {
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
                case ShaderGraphNodeKind.Asin: case ShaderGraphNodeKind.Acos:
                case ShaderGraphNodeKind.Exp: case ShaderGraphNodeKind.Log:
                {
                    var input = Input(node, "In"); var type = Common(node, input);
                    // Natural exp/log and radian angles. Asin/Acos require [-1,1];
                    // Log requires positive inputs. Do not silently clip the domain.
                    return new Value(type, node.kind.ToString().ToLowerInvariant() + "(" + Cast(input, type, node).text + ")");
                }
                case ShaderGraphNodeKind.Modulo: case ShaderGraphNodeKind.Atan2:
                {
                    bool atan = node.kind == ShaderGraphNodeKind.Atan2;
                    var a = Input(node, atan ? "Y" : "A"); var b = Input(node, atan ? "X" : "B");
                    var type = Common(node, a, b);
                    string x = Cast(a, type, node).text, y = Cast(b, type, node).text;
                    // Floor modulo has the divisor's sign. Zero divisors, like
                    // atan2(0,0), retain the intrinsic's undefined-domain behavior.
                    return new Value(type, atan ? $"atan2({x},{y})" : $"(({x})-({y})*floor(({x})/({y})))");
                }
                case ShaderGraphNodeKind.Cross:
                    return new Value(ShaderGraphValueType.Float3, $"cross({V("A")},{V("B")})");
                case ShaderGraphNodeKind.Reflect: case ShaderGraphNodeKind.Refract:
                {
                    var incident = Input(node, "Incident"); var normal = Input(node, "Normal");
                    var type = Common(node, incident, normal);
                    string i = Cast(incident, type, node).text, n = Cast(normal, type, node).text;
                    return new Value(type, node.kind == ShaderGraphNodeKind.Reflect
                        ? $"reflect({i},{n})" : $"refract({i},{n},{V("Eta")})");
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
                    return new Value(ShaderGraphValueType.Color, $"float4(({color}).rgb*({color}).a,({color}).a)");
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
                    text = $"GraphNormalStrength({V("Normal")},{V("Strength")},{(int)node.normalEncoding})"; break;
                case ShaderGraphNodeKind.NormalLighting:
                    if (node.option == 1)
                    {
                        result.contextDependencies |= ShaderContextDependencies.Lighting;
                        result.usesCanvasPosition = true;
                        text = $"WTGlobalLighting(float3(GraphCanvasPosition({V("UV")})*_CanvasSize.xy,{V("Height")}),{V("Color")}," +
                            $"GraphDecodeNormal({V("Normal")},{(int)node.normalEncoding}),{V("Ambient")},({V("SpecularColor")}).rgb," +
                            $"{V("SpecularIntensity")}*({V("SpecularColor")}).a,{V("Roughness")})";
                        break;
                    }
                    text = $"GraphNormalLighting({V("Color")},{V("Normal")},{V("LightDirection")},{V("LightColor")},{V("Intensity")},{V("Ambient")}," +
                        $"{V("SpecularColor")},{V("SpecularIntensity")},{V("Roughness")},{V("SpecularOnly")},{(int)node.normalEncoding})"; break;
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
