using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class ShaderGraphParameters
    {
        internal static ShaderParameterType Kind(ShaderParameter p) => p.controls.Exists(c => c.type == ShaderParameterType.Seed) ? ShaderParameterType.Seed : p.type;
        internal static ShaderParameterControl DefaultControl(ShaderParameter p) => new ShaderParameterControl
        {
            type = p.type, valueType = p.ValueType,
            hasMinimum = p.hasMinimum, hasMaximum = p.hasMaximum, minimum = p.minimum, maximum = p.maximum,
            softMinimum = p.softMinimum, softMaximum = p.softMaximum, cyclicRange = p.cyclicRange
        };
        internal static ShaderParameterControl EnsureControl(ShaderParameter p)
        {
            if (p.controls.Count == 0) p.controls.Add(DefaultControl(p));
            return p.controls[0];
        }
        internal static void SetKind(ShaderParameter p, ShaderParameterType kind)
        {
            p.type = kind == ShaderParameterType.Seed ? ShaderParameterType.Float : kind;
            p.valueType = kind == ShaderParameterType.Toggle ? "bool" : null;
            p.hasMinimum = p.hasMaximum = p.softMinimum = p.softMaximum = p.cyclicRange = false;
            p.controls.Clear();
            var c = EnsureControl(p);
            if (kind == ShaderParameterType.Seed)
            {
                c.type = ShaderParameterType.Seed; c.seed = true; c.hasMinimum = c.hasMaximum = true;
                c.valueType = "float"; c.valueMinimum = -ShaderSeed.Limit; c.valueMaximum = ShaderSeed.Limit;
                p.floatValue = Mathf.Clamp(Mathf.Round(p.floatValue), -ShaderSeed.Limit, ShaderSeed.Limit);
            }
            if (kind == ShaderParameterType.Enum) { c.optionNames = new[] { "First", "Second" }; c.optionValues = new[] { 0d, 1d }; }
            if (kind == ShaderParameterType.Point) p.vectorValue = new Vector4(.5f, .5f, 0, 0);
            if (kind == ShaderParameterType.Normal) p.vectorValue = Vector3.forward;
            if (kind == ShaderParameterType.Gradient) p.gradientValue ??= new WhimTexGradient();
            if (kind == ShaderParameterType.Curve) p.curveValue ??= WhimTexCurveTexture.Default();
        }
        internal static void SyncRange(ShaderParameter p)
        {
            var c = EnsureControl(p);
            c.hasMinimum = p.hasMinimum; c.hasMaximum = p.hasMaximum;
            c.minimum = p.minimum; c.maximum = p.maximum;
            c.softMinimum = p.softMinimum; c.softMaximum = p.softMaximum; c.cyclicRange = p.cyclicRange;
            if (c.type == ShaderParameterType.Seed)
            {
                c.hasMinimum = c.hasMaximum = true;
                c.valueMinimum = p.hasMinimum ? p.minimum : -ShaderSeed.Limit;
                c.valueMaximum = p.hasMaximum ? p.maximum : ShaderSeed.Limit;
                p.floatValue = Mathf.Clamp(Mathf.Round(p.Clamp(p.floatValue)), -ShaderSeed.Limit, ShaderSeed.Limit);
            }
            else if (p.type == ShaderParameterType.Float) p.floatValue = p.Clamp(p.floatValue);
            else if (p.IsInterval) p.vectorValue = p.ClampInterval(p.vectorValue);
        }
        // UI inactivity is not always an emission rule: these procedural helpers still
        // receive the argument in every visible mode, including its upstream expression.
        internal static bool EvaluatesInput(ShaderGraphNode node, string port) =>
            ShaderGraphNodes.InputVisible(node, port) &&
            (ShaderGraphNodes.InputInactiveReason(node, port) == null ||
             node.kind == ShaderGraphNodeKind.Shape && port == "Radius" ||
             node.kind == ShaderGraphNodeKind.Stripes && port == "Width" ||
             node.kind == ShaderGraphNodeKind.GradientFactor && port == "Angle");

        internal static ulong MetadataFingerprint(ShaderParameter p)
        {
            ulong hash = 14695981039346656037UL;
            void N(long value) { unchecked { hash = (hash ^ (ulong)value) * 1099511628211UL; } }
            void S(string value) { if (value != null) foreach (char c in value) N(c); N(-1); }
            void B(bool value) => N(value ? 1 : 0);
            void F(double value) => N(BitConverter.DoubleToInt64Bits(value));
            N((int)p.type); S(p.ValueType); S(p.name); B(p.pixelUnits); B(p.hasMinimum); B(p.hasMaximum); B(p.softMinimum); B(p.softMaximum); B(p.cyclicRange); F(p.minimum); F(p.maximum);
            foreach (var c in p.controls)
            {
                N((int)c.type); S(c.valueType); S(c.label); S(c.tooltip); N(c.order); B(c.hidden); B(c.rgba); B(c.seed);
                B(c.hasMinimum); B(c.hasMaximum); B(c.softMinimum); B(c.softMaximum); B(c.cyclicRange); F(c.minimum); F(c.maximum); F(c.valueMinimum); F(c.valueMaximum);
                foreach (var s in c.optionNames) S(s); foreach (var f in c.optionValues) F(f);
                foreach (var s in c.headers) S(s); foreach (var s in c.helpBoxes) S(s);
                S(c.visibleIfParameter); B(c.visibleIfNotEqual); F(c.visibleIfValue);
                foreach (var condition in c.enableIf) { S(condition.parameter); B(condition.notEqual); F(condition.value); }
                foreach (var container in c.containers) { N((int)container.kind); N(container.id); S(container.title); S(container.headerParameter); }
            }
            return hash;
        }
    }
}
