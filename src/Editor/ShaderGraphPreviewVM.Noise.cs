using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        sealed partial class Builder
        {
            Value CausticsNoise(ShaderGraphNode node, string port)
            {
                if (node.noiseDimensions != ShaderGraphNoiseDimensions.TwoD || node.fractal != ShaderGraphFractal.None)
                    Unsupported("Caustics Noise requires 2D positions and a separate Fractal node");
                int R(string name) => Input(node,name).register;
                extended=true; noiseFamilies|=256;
                var op=new Instruction { op=36, a=R("Position"), b=R("Scale"), c=R("Seed"),
                    d=R("Strength"), e=R("Evolution"), m=2, i=node.option,
                    j=port=="Color"?1:0, f=port=="Color"?R("Dispersion"):0,
                    n=node.noisePeriodic?R("Period"):Number(0).register };
                NoiseFootprint(node,ref op);
                op.g=-1; // CIE/D65 when Color has no gradient override.
                if(port=="Color" && (node.FindInput("Spectrum")?.resource!=null || incoming.ContainsKey(node.id+":Spectrum")))
                {
                    var spectrum=Input(node,"Spectrum");
                    op.g=spectrum.register;
                    op.h=(int)(palettes[spectrum.register].gradientValue?.WrapMode ?? WhimTexGradientWrapMode.Clamp);
                    op.k=node.causticsWhiteBalance?1:0;
                }
                if(node.noisePeriodic) op.value.w=R("Offset")+1;
                return Add(op,port=="Color"?ShaderGraphValueType.Color:ShaderGraphValueType.Float);
            }

            Value RemainingNoise(ShaderGraphNode node,string port)
            {
                int dimensions=ShaderGraphNodes.NoiseDimensions(node);
                bool grain=ShaderGraphNodes.IsGrainNoise(node.kind),warp=ShaderGraphNodes.IsWarpNoise(node.kind);
                if(dimensions<1 || dimensions>(grain?2:3) ||
                    node.kind==ShaderGraphNodeKind.TriangleVoronoiNoise && dimensions!=2 ||
                    (!warp && node.fractal!=ShaderGraphFractal.None) ||
                    warp && node.fractal is not (ShaderGraphFractal.None or ShaderGraphFractal.Progressive or ShaderGraphFractal.Independent))
                    Unsupported("Noise dimensions or fractal mode");
                int R(string name)=>Input(node,name).register;
                extended=true; volume|=dimensions==3;
                var op=new Instruction {op=grain?29:warp?28:node.kind==ShaderGraphNodeKind.SpotsNoise?26:27,
                    a=R("Position"),c=R("Seed"),m=dimensions};
                if(grain)
                {
                    noiseFamilies|=128;
                    op.b=R("ImageSize");op.d=R("GrainSize");op.e=dimensions==1?R("Direction"):Number(0).register;
                    op.i=node.kind==ShaderGraphNodeKind.BlueNoise?1:0;op.j=port=="Color"?1:0;op.k=node.noiseInverted?1:0;
                    return Add(op,op.j!=0?ShaderGraphValueType.Color:ShaderGraphValueType.Float);
                }
                op.b=R("Scale");op.n=node.noisePeriodic?R("Period"):Number(0).register;
                NoiseFootprint(node,ref op);
                if(node.noisePeriodic) op.value.w=R("Offset")+1;
                if(warp)
                {
                    noiseFamilies|=64;
                    op.d=R("Strength");op.h=node.fractal==ShaderGraphFractal.Progressive?1:node.fractal==ShaderGraphFractal.Independent?2:0;
                    op.e=op.h!=0?R("Octaves"):Number(1).register;op.f=op.h!=0?R("Lacunarity"):Number(2).register;
                    op.g=op.h!=0?R("Gain"):Number(.5f).register;
                    op.i=node.kind==ShaderGraphNodeKind.OpenSimplex2WarpNoise?0:node.kind==ShaderGraphNodeKind.OpenSimplex2ReducedWarpNoise?1:2;
                    op.j=port=="Position"?1:0;
                    return Add(op,ShaderGraphNodes.NoisePositionType(node));
                }
                if(op.op==26)
                {
                    noiseFamilies|=16;
                    op.d=R("Density");op.e=R("Radius");op.f=R("Softness");
                    op.g=dimensions>1?R("Elongation"):Number(1).register;op.h=dimensions>1?R("Direction"):Number(1).register;
                    return Add(op,ShaderGraphValueType.Float);
                }
                if(!Enum.IsDefined(typeof(ShaderGraphNoiseDistribution),node.noiseDistribution)) Unsupported("Triangle distribution");
                noiseFamilies|=32;
                op.d=R("Jitter");op.e=R("Evolution");op.f=R("Stretch");op.g=(int)node.noiseDistribution;
                op.j=port switch {"Value"=>0,"Distance"=>1,"CellValue"=>2,"ToPoint"=>3,"BorderNormal"=>4,_=>throw new NotSupportedException("Triangle output")};
                return Add(op,op.j>=3?ShaderGraphValueType.Float2:ShaderGraphValueType.Float);
            }
        }
    }
}
