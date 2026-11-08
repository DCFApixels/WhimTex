using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        sealed partial class Builder
        {
            Value EmitSimple(ShaderGraphNode node, string port)
            {
                Instruction op = new Instruction { op = 40, o = (int)node.kind, i = node.option, j = (int)node.normalEncoding,
                    k = (int)node.inputColorSpace, l = (int)node.outputColorSpace };
                Value Map(ShaderGraphValueType type, params string[] names)
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        int value = node.kind == ShaderGraphNodeKind.TileUV && names[i] == "RowOffset" && node.option == 0
                            ? Number(0).register : Input(node, names[i]).register;
                        switch (i) { case 0: op.a=value; break; case 1: op.b=value; break; case 2: op.c=value; break;
                            case 3: op.d=value; break; case 4: op.e=value; break; case 5: op.f=value; break; case 6: op.g=value; break; case 7: op.h=value; break; }
                    }
                    return Add(op, type);
                }
                switch (node.kind)
                {
                    case ShaderGraphNodeKind.SpotsNoise: case ShaderGraphNodeKind.TriangleVoronoiNoise:
                    case ShaderGraphNodeKind.WhiteNoise: case ShaderGraphNodeKind.BlueNoise:
                    case ShaderGraphNodeKind.OpenSimplex2WarpNoise: case ShaderGraphNodeKind.OpenSimplex2ReducedWarpNoise:
                    case ShaderGraphNodeKind.BasicGridWarpNoise: return RemainingNoise(node,port);
                    case ShaderGraphNodeKind.EdgeDetection: return EdgeDetection(node);
                    case ShaderGraphNodeKind.ApplyTransform2D:
                        extended = true;
                        return Add(new Instruction { op=46, a=Input(node,"Transform").register, b=Input(node,"UV").register, i=port=="Local"?0:3 }, ShaderGraphValueType.Float2);
                    case ShaderGraphNodeKind.GradientMap: return GradientMap(node);
                    case ShaderGraphNodeKind.ColorCurves: return ColorCurves(node);
                    case ShaderGraphNodeKind.Shape: case ShaderGraphNodeKind.CombineDistance:
                    case ShaderGraphNodeKind.Checkerboard: case ShaderGraphNodeKind.Stripes:
                    case ShaderGraphNodeKind.Grid: case ShaderGraphNodeKind.Rings: return Procedural(node,port);
                    case ShaderGraphNodeKind.PixelateUV: return Map(ShaderGraphValueType.Float2,"UV","CellSize","Offset");
                    case ShaderGraphNodeKind.Blackbody: return Map(ShaderGraphValueType.Color,"Temperature");
                    case ShaderGraphNodeKind.Dither: return Map(ShaderGraphValueType.Color,"Color","UV","Levels","PixelSize");
                    case ShaderGraphNodeKind.TileUV:
                        op.m=port switch { "UV"=>0,"Cell"=>1,"Center"=>2,"Random"=>3,"Mask"=>4,_=>throw new NotSupportedException("Tile output") };
                        return Map(op.m<3?ShaderGraphValueType.Float2:ShaderGraphValueType.Float,"UV","Count","Offset","RowOffset","RandomRotation","RandomScale","Seed");
                    case ShaderGraphNodeKind.SampleGradient: case ShaderGraphNodeKind.SampleCurve:
                        return SamplePalette(node, port);
                    case ShaderGraphNodeKind.HeightToNormal: case ShaderGraphNodeKind.HeightToDistortion:
                        return HeightSlope(node);
                    case ShaderGraphNodeKind.Blend:
                        op.j = node.hdr ? 1 : 0;
                        return Map(ShaderGraphValueType.Color, "Background", "Foreground", "Opacity");
                    case ShaderGraphNodeKind.NormalLighting:
                    {
                        if (node.option == 1 && !simplifiedLighting) Unsupported("Exact global lighting needs a native boundary");
                        extended = true;
                        op.op = 43;
                        int R(string name) => Input(node, name).register;
                        op.a=R("Color"); op.b=R("Normal");
                        op.c=node.option==1 ? Literal(new Vector4(-.5f,.5f,.7071067812f,0),ShaderGraphValueType.Float3).register : R("LightDirection");
                        op.d=node.option==1 ? Literal(Vector4.one,ShaderGraphValueType.Color).register : R("LightColor");
                        op.e=node.option==1 ? Number(1).register : R("Intensity");
                        op.f=R("Ambient"); op.g=R("SpecularColor"); op.h=R("SpecularIntensity");
                        op.n=R("Roughness"); op.m=node.option==1 ? Number(0).register : R("SpecularOnly");
                        return Add(op,ShaderGraphValueType.Color);
                    }
                    case ShaderGraphNodeKind.Compare:
                    case ShaderGraphNodeKind.And: case ShaderGraphNodeKind.Or:
                        return Map(ShaderGraphValueType.Boolean, "A", "B");
                    case ShaderGraphNodeKind.Not: return Map(ShaderGraphValueType.Boolean, "In");
                    case ShaderGraphNodeKind.Select:
                    {
                        var a = Input(node, "True"); var b = Input(node, "False"); var type = Common(a, b);
                        op.a = Cast(a, type).register; op.b = Cast(b, type).register; op.c = Input(node, "Condition").register;
                        return Add(op, type);
                    }
                    case ShaderGraphNodeKind.Swizzle:
                    {
                        string mask = node.swizzle;
                        if (string.IsNullOrEmpty(mask) || mask.Length > 4) Unsupported("Swizzle mask");
                        var input = Input(node, "In");
                        op.a = Cast(input, ShaderGraphNodes.SwizzleInputType(mask, input.type)).register;
                        op.value = new Vector4(-1, -1, -1, -1);
                        for (int lane = 0; lane < mask.Length; lane++)
                        {
                            int component = ShaderGraphNodes.SwizzleComponent(mask[lane]);
                            if (component < -1) Unsupported("Swizzle component");
                            op.value[lane] = component;
                        }
                        return Add(op, ShaderGraphNodes.SwizzleType(mask));
                    }
                    case ShaderGraphNodeKind.Reflect: case ShaderGraphNodeKind.Refract:
                    {
                        var a = Input(node, "Incident"); var b = Input(node, "Normal"); var type = Common(a, b);
                        op.a = Cast(a, type).register; op.b = Cast(b, type).register;
                        op.c = node.kind == ShaderGraphNodeKind.Refract ? Input(node, "Eta").register : op.a;
                        op.m = ShaderGraphNodes.Components(type);
                        return Add(op, type);
                    }
                    case ShaderGraphNodeKind.Cross: return Map(ShaderGraphValueType.Float3, "A", "B");
                    case ShaderGraphNodeKind.Remap:
                        return Map(ShaderGraphNodes.NumericType(Input(node, "In").type), "In", "InMin", "InMax", "OutMin", "OutMax");
                    case ShaderGraphNodeKind.RangeMask:
                    {
                        var input = Input(node, "In"); op.a = Cast(input, ShaderGraphValueType.Float4).register;
                        op.b = Input(node, "Center").register; op.c = Input(node, "Width").register; op.d = Input(node, "Feather").register;
                        return Cast(Add(op, ShaderGraphValueType.Float4), ShaderGraphNodes.NumericType(input.type));
                    }
                    case ShaderGraphNodeKind.RotateVector:
                        return node.option == 0 ? Map(ShaderGraphValueType.Float2, "In", "Angle") : Map(ShaderGraphValueType.Float3, "In", "Angle", "Axis");
                    case ShaderGraphNodeKind.TransformUV: return Map(ShaderGraphValueType.Float2, "UV", "Offset", "Scale", "Rotation", "Center");
                    case ShaderGraphNodeKind.Random: return Map(ShaderGraphValueType.Float, "Coordinates", "Seed");
                    case ShaderGraphNodeKind.GradientFactor: return Map(ShaderGraphValueType.Float, "UV", "Center", "Scale", "Angle");
                    case ShaderGraphNodeKind.PolarCoordinates: return Map(ShaderGraphValueType.Float2, "UV", "Center", "Angle");
                    case ShaderGraphNodeKind.Repeat: op.m = port == "Cell" ? 1 : 0; return Map(ShaderGraphValueType.Float2, "UV", "Scale", "Offset");
                    case ShaderGraphNodeKind.Mirror: return Map(ShaderGraphValueType.Float2, "UV", "Center");
                    case ShaderGraphNodeKind.Kaleidoscope: return Map(ShaderGraphValueType.Float2, "UV", "Center", "Sectors", "Angle");
                    case ShaderGraphNodeKind.VectorDisplacement: return Map(ShaderGraphValueType.Float2, "UV", "Displacement", "Strength");
                    case ShaderGraphNodeKind.Twirl: return Map(ShaderGraphValueType.Float2, "UV", "Center", "Strength", "Radius");
                    case ShaderGraphNodeKind.BulgePinch: return Map(ShaderGraphValueType.Float2, "UV", "Center", "Strength", "Radius");
                    case ShaderGraphNodeKind.Wave: return Map(ShaderGraphValueType.Float2, "UV", "Strength", "Frequency", "Direction", "Phase");
                    case ShaderGraphNodeKind.Ripple: return Map(ShaderGraphValueType.Float2, "UV", "Center", "Strength", "Frequency", "Radius", "Phase");
                    case ShaderGraphNodeKind.Flipbook: return Map(ShaderGraphValueType.Float2, "UV", "Columns", "Rows", "Frame");
                    case ShaderGraphNodeKind.PerspectiveUV: var perspective = Map(ShaderGraphValueType.Float4, "UV", "BottomLeft", "BottomRight", "TopRight", "TopLeft"); return port == "Mask" ? Add(new Instruction { op=14, a=perspective.register, b=2 }, ShaderGraphValueType.Float) : Cast(perspective, ShaderGraphValueType.Float2);
                    case ShaderGraphNodeKind.HeightBlend: var height = Map(ShaderGraphValueType.Float2, "Bottom", "Top", "Offset", "Smoothness", "Opacity"); return Add(new Instruction { op=14, a=height.register, b=port == "Height" ? 0 : 1 }, ShaderGraphValueType.Float);
                    case ShaderGraphNodeKind.Hue: return Map(ShaderGraphValueType.Color, "Color", "Degrees");
                    case ShaderGraphNodeKind.Saturation: return Map(ShaderGraphValueType.Color, "Color", "Amount");
                    case ShaderGraphNodeKind.Exposure: return Map(ShaderGraphValueType.Color, "Color", "Stops");
                    case ShaderGraphNodeKind.Lightness: return Map(ShaderGraphValueType.Color, "Color", "Amount");
                    case ShaderGraphNodeKind.Levels: return Map(ShaderGraphValueType.Color, "Color", "InputRange", "Gamma", "OutputRange", "Clamp");
                    case ShaderGraphNodeKind.ColorBalance: return Map(ShaderGraphValueType.Color, "Color", "Shadows", "Midtones", "Highlights", "ShadowRange", "HighlightRange", "PreserveLuminance");
                    case ShaderGraphNodeKind.ChannelMixer: return Map(ShaderGraphValueType.Color, "Color", "Red", "Green", "Blue");
                    case ShaderGraphNodeKind.WhiteBalance: return Map(ShaderGraphValueType.Color, "Color", "Temperature", "Tint");
                    case ShaderGraphNodeKind.Posterize: return Map(ShaderGraphValueType.Color, "Color", "Levels");
                    case ShaderGraphNodeKind.ConvertColor: return Map(ShaderGraphValueType.Color, "Color");
                    case ShaderGraphNodeKind.Luminance: return Map(ShaderGraphValueType.Float, "Color");
                    case ShaderGraphNodeKind.ColorMask: return Map(ShaderGraphValueType.Float, "Color", "KeyColor", "Tolerance", "Softness");
                    case ShaderGraphNodeKind.HueRange: return Map(ShaderGraphValueType.Float, "Color", "Range", "Feather", "SaturationRange");
                    case ShaderGraphNodeKind.MixColor: return Map(ShaderGraphValueType.Color, "A", "B", "T");
                    case ShaderGraphNodeKind.SetAlpha: return Map(ShaderGraphValueType.Color, "Color", "Alpha");
                    case ShaderGraphNodeKind.NormalBlend: return Map(ShaderGraphValueType.Float3, "Base", "Detail", "Strength");
                    case ShaderGraphNodeKind.NormalToDistortion: return Map(ShaderGraphValueType.Float2, "Normal", "Strength", "FlipX", "FlipY");
                    case ShaderGraphNodeKind.NormalEncode: return Map(ShaderGraphValueType.Float3, "Normal");
                    case ShaderGraphNodeKind.NormalDecode: return Map(ShaderGraphValueType.Float3, "Normal");
                    case ShaderGraphNodeKind.NormalStrength: return Map(ShaderGraphValueType.Float3, "Normal", "Strength");
                    case ShaderGraphNodeKind.Premultiply: return Map(ShaderGraphValueType.Color, "Color");
                    case ShaderGraphNodeKind.Unpremultiply: return Map(ShaderGraphValueType.Color, "Color");
                    default: Unsupported("Node " + node.kind); return default;
                }
            }
        }
    }
}
