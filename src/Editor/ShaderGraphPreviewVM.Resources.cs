using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        ShaderParameter[] palettes = Array.Empty<ShaderParameter>();
        ShaderParameter[] transforms = Array.Empty<ShaderParameter>();
        Vector4[] transformRows;
        static readonly int TransformRowsId = Shader.PropertyToID("_VMTransformRows");
        readonly List<WhimTexGradientTexture> gradientTextures = new();
        readonly List<WhimTexCurveTexture> curveTextures = new();
        WhimTexGradient defaultGradient;
        AnimationCurve defaultCurve;

        void BindTransforms(Vector2 dimensions)
        {
            if (transforms.Length == 0) return;
            transformRows ??= new Vector4[8 * 6];
            for (int i=0;i<transforms.Length;i++)
                transforms[i].transformValue.GetRows(dimensions, out transformRows[i*6], out transformRows[i*6+1], out transformRows[i*6+2],
                    out transformRows[i*6+3], out transformRows[i*6+4], out transformRows[i*6+5]);
            material.SetVectorArray(TransformRowsId,transformRows);
        }

        void BindPalettes()
        {
            while (gradientTextures.Count < palettes.Length)
            { gradientTextures.Add(null); curveTextures.Add(null); }
            for (int i = 0; i < palettes.Length; i++)
            {
                var p = palettes[i]; Texture texture;
                if (p.type == ShaderParameterType.Gradient)
                    texture = (gradientTextures[i] ??= new WhimTexGradientTexture()).GetTexture(p.gradientValue ?? (defaultGradient ??= new WhimTexGradient()));
                else texture = (curveTextures[i] ??= new WhimTexCurveTexture()).GetTexture(p.curveValue ?? (defaultCurve ??= WhimTexCurveTexture.Default()));
                material.SetTexture("_VMPalette" + i, texture);
            }
        }
        void ReleasePalettes()
        {
            foreach (var texture in gradientTextures) texture?.Dispose();
            foreach (var texture in curveTextures) texture?.Dispose();
            gradientTextures.Clear(); curveTextures.Clear();
        }
        sealed partial class Builder
        {
            internal readonly List<ShaderParameter> palettes = new();
            internal readonly List<ShaderParameter> transforms = new();
            Value Palette(ShaderParameter parameter, ShaderGraphValueType type)
            {
                if (type == ShaderGraphValueType.Transform2D)
                {
                    parameter ??= ShaderGraphInput.DefaultResource(type);
                    int index=transforms.IndexOf(parameter);
                    if(index<0) {index=transforms.Count;if(index>=8) Unsupported("Preview transform limit");transforms.Add(parameter);}
                    extended=true;
                    return new Value(index,type);
                }
                if (type is not (ShaderGraphValueType.Gradient or ShaderGraphValueType.Curve))
                    Unsupported("Resource input");
                parameter ??= ShaderGraphInput.DefaultResource(type);
                int slot = palettes.IndexOf(parameter);
                if (slot < 0)
                { slot = palettes.Count; if (slot >= 8) Unsupported("Preview palette limit"); palettes.Add(parameter); }
                // Resource values carry a slot rather than a numeric instruction register.
                return new Value(slot, type);
            }
            Value SamplePalette(ShaderGraphNode node, string port)
            {
                bool gradient = node.kind == ShaderGraphNodeKind.SampleGradient;
                var palette = Input(node, gradient ? "Gradient" : "Curve");
                var value = SamplePalette(palette, Input(node, "T"));
                int component = "RGBA".IndexOf(port, StringComparison.Ordinal);
                return gradient && component >= 0 ? Add(new Instruction { op = 14, a = value.register, b = component }, ShaderGraphValueType.Float) : value;
            }
            Value SamplePalette(Value palette, Value position)
            {
                bool gradient = palette.type == ShaderGraphValueType.Gradient;
                var p = palettes[palette.register];
                return Add(new Instruction { op=42, a=palette.register, b=position.register,
                    c=gradient ? (int)(p.gradientValue?.WrapMode ?? WhimTexGradientWrapMode.Clamp) : 0 },
                    gradient ? ShaderGraphValueType.Color : ShaderGraphValueType.Float);
            }
            Value Component(Value value, int lane) => Add(new Instruction { op=14, a=value.register, b=lane }, ShaderGraphValueType.Float);
            Value ScalarOp(Value a, Value b, ShaderGraphNodeKind kind) => Add(new Instruction { op=10, a=a.register, b=b.register, c=(int)kind, d=1 }, ShaderGraphValueType.Float);
            Value GradientMap(ShaderGraphNode node)
            {
                var color = Input(node, "Color");
                var channel = Add(new Instruction { op=40, o=(int)ShaderGraphNodeKind.GradientMap, a=color.register, k=(int)node.channel }, ShaderGraphValueType.Float);
                var mapped = SamplePalette(Input(node,"Gradient"),channel);
                if (node.option == 2) return mapped;
                var alpha = Component(color,3);
                if (node.option == 1) alpha=ScalarOp(Component(mapped,3),alpha,ShaderGraphNodeKind.Multiply);
                return Add(new Instruction { op=40, o=(int)ShaderGraphNodeKind.SetAlpha, a=mapped.register, b=alpha.register },ShaderGraphValueType.Color);
            }
            Value ColorCurves(ShaderGraphNode node)
            {
                bool Assigned(string name) => incoming.ContainsKey(node.id+":"+name) || node.FindInput(name)!=null;
                Value Curve(string name, Value value)
                {
                    if (!Assigned(name)) return value;
                    var limited=Add(new Instruction { op=11,a=value.register,c=(int)ShaderGraphNodeKind.Saturate,d=1 },ShaderGraphValueType.Float);
                    // Preserve the native HDR extension: v + curve(saturate(v)) - saturate(v).
                    var mapped=SamplePalette(Input(node,name),limited);
                    return ScalarOp(ScalarOp(value,mapped,ShaderGraphNodeKind.Add),limited,ShaderGraphNodeKind.Subtract);
                }
                Value Join(Value r,Value g,Value b,Value a) => Add(new Instruction { op=13,a=r.register,b=g.register,c=b.register,d=a.register },ShaderGraphValueType.Color);
                var color=Input(node,"Color");
                if (Assigned("Master"))
                {
                    if (node.option==0) color=Join(Curve("Master",Component(color,0)),Curve("Master",Component(color,1)),Curve("Master",Component(color,2)),Component(color,3));
                    else
                    {
                        var luma=Add(new Instruction { op=40,o=(int)ShaderGraphNodeKind.Luminance,a=color.register },ShaderGraphValueType.Float);
                        var mapped=Curve("Master",luma);
                        color=Add(new Instruction { op=40,o=(int)ShaderGraphNodeKind.ColorCurves,a=color.register,b=luma.register,c=mapped.register },ShaderGraphValueType.Color);
                    }
                }
                return Join(Curve("Red",Component(color,0)),Curve("Green",Component(color,1)),Curve("Blue",Component(color,2)),Curve("Alpha",Component(color,3)));
            }
        }
    }
}
