using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        readonly struct ValueSource
        {
            internal readonly string node, port, parameter;
            readonly Vector4 fallback;
            readonly bool usesPixelScale;
            internal ValueSource(string node, string port, Vector4 fallback)
            { this.node = node; this.port = port; this.fallback = fallback; parameter = null; usesPixelScale = false; }
            internal ValueSource(ShaderParameter parameter)
            { this.parameter = parameter.id; usesPixelScale = parameter.UsesPixelScale; node = port = null; fallback = default; }
            internal ValueSource(string node, ShaderParameter value)
            { this.node = node; port = null; fallback = default; parameter = null; usesPixelScale = value?.UsesPixelScale == true; }
            internal ShaderParameter Resource(ShaderGraphBindingState state) => parameter != null ? state.Graph.FindParameter(parameter)
                : port == null ? state.nodes[node].literal : state.nodes[node].FindInput(port)?.resource;
            internal Vector4 Read(ShaderGraphBindingState state)
            {
                if (parameter == null && port != null) return state.nodes[node].FindInput(port)?.value ?? fallback;
                var value = Resource(state);
                if (value == null && parameter == null) return Vector4.zero;
                // Pixel units change the instruction sequence, even though native HLSL
                // only needs a new uniform value and retains the same code stamp.
                if (value == null || value.UsesPixelScale != usesPixelScale)
                    throw new NotSupportedException("Parameter pixel units changed");
                return ParameterValue(value);
            }
        }
        readonly struct ValueBinding
        {
            internal readonly ValueSource source;
            internal readonly int register;
            internal ValueBinding(ValueSource source, int register) { this.source = source; this.register = register; }
        }
        struct ResourceGuard
        {
            internal readonly ValueSource source;
            readonly ShaderParameter value;
            internal ulong stamp;
            internal bool captured;
            internal ResourceGuard(ValueSource source, ShaderParameter value)
            { this.source = source; this.value = value; stamp = 0; captured = false; }
            internal void Capture()
            {
                if (captured) return;
                stamp = ShaderFX.HashParameter(14695981039346656037UL, value); captured = true;
            }
        }
        ValueBinding[] valueBindings = Array.Empty<ValueBinding>();
        ResourceGuard[] resourceGuards = Array.Empty<ResourceGuard>();
        int[] fixedValues = Array.Empty<int>();
        Vector4[] nextValues;
        bool[] assignedValues;
        ShaderGraphBindingState valueState;
        bool hybridValuesCurrent;

        static bool SameValue(Vector4 a, Vector4 b) =>
            BitConverter.SingleToInt32Bits(a.x) == BitConverter.SingleToInt32Bits(b.x) &&
            BitConverter.SingleToInt32Bits(a.y) == BitConverter.SingleToInt32Bits(b.y) &&
            BitConverter.SingleToInt32Bits(a.z) == BitConverter.SingleToInt32Bits(b.z) &&
            BitConverter.SingleToInt32Bits(a.w) == BitConverter.SingleToInt32Bits(b.w);

        static Vector4 ParameterValue(ShaderParameter parameter)
        {
            if (parameter == null) throw new NotSupportedException("Missing parameter");
            Vector4 data = Vector4.zero;
            for (int i = 0; i < parameter.Components; i++) data[i] = (float)parameter.GetNumber(i);
            bool typed = ShaderNumericValue.NeedsTypedBinding(parameter);
            if (ShaderGraphNodes.ParameterType(parameter) == ShaderGraphValueType.Color && !parameter.IsInteger) data = HdrUtility.Decode((Color)data);
            if (parameter.type == ShaderParameterType.Toggle) data.x = parameter.BoolValue ? 1 : 0;
            if (parameter.type == ShaderParameterType.Normal && !typed) data = ShaderParameter.NormalizeNormal(data);
            return data;
        }

        // Caller has already compared the branch's code stamp. Values are validated for
        // every consumer before mutating instructions or any retained lighting producer.
        internal bool TryUpdateValues(ShaderGraph graph, ShaderGraphBindingState state = null)
        {
            if (graph == null || NeedsExpansion(graph)) return false;
            if (state == null) { valueState ??= new ShaderGraphBindingState(); valueState.Prepare(graph); state = valueState; }
            try
            {
                if (!PrepareValues(state)) return false;
                CommitValues(graph);
                return true;
            }
            catch (Exception error) when (error is NotSupportedException || error is KeyNotFoundException || error is FormatException)
            { return false; }
        }
        bool PrepareValues(ShaderGraphBindingState state)
        {
            if (Failed || hybridPreparation != null || hybridRequest != null || instructions.Length == 0 ||
                shaderRevision != ShaderProgramSourceRevision.Current || HasBoundaries && hybridRuntime == null) return false;
            foreach (var guard in resourceGuards)
                if (!guard.captured || ShaderFX.HashParameter(14695981039346656037UL, guard.source.Resource(state)) != guard.stamp) return false;
            if (nextValues == null || nextValues.Length != instructions.Length)
            { nextValues = new Vector4[instructions.Length]; assignedValues = new bool[instructions.Length]; }
            Array.Clear(assignedValues, 0, assignedValues.Length);
            for (int i = 0; i < instructions.Length; i++) nextValues[i] = instructions[i].value;
            foreach (int register in fixedValues) assignedValues[register] = true;
            foreach (var binding in valueBindings)
            {
                Vector4 value = binding.source.Read(state);
                for (int i = 0; i < 4; i++) if (!float.IsFinite(value[i])) return false;
                int register = binding.register;
                // Equal literals may share a register, including an immutable algorithm constant.
                // If they diverge, rebuild with the existing register allocator instead of
                // silently changing another input or increasing every program's instruction count.
                if (assignedValues[register] && !SameValue(nextValues[register], value)) return false;
                assignedValues[register] = true; nextValues[register] = value;
            }
            bool periodic = false;
            foreach (var op in instructions)
            {
                if (op.op == 30 && (!SameValue(nextValues[op.a], instructions[op.a].value) ||
                    !SameValue(nextValues[op.f], instructions[op.f].value))) return false;
                if (op.op is 20 or 21 or 22 or 25 or 26 or 27 or 28 or 36)
                {
                    var period = nextValues[op.n];
                    periodic |= instructions[op.n].op != 0 || period.x > 0 || op.m >= 2 && period.y > 0 || op.m == 3 && period.z > 0;
                }
            }
            if (periodic != periodicNoise) return false;
            if (lightingSurfaces != null) foreach (var surface in lightingSurfaces)
                if (surface != null && !surface.PrepareValues(state)) return false;
            return true;
        }
        void CommitValues(ShaderGraph graph)
        {
            bool changed = false;
            for (int i = 0; i < instructions.Length; i++)
                if (!SameValue(instructions[i].value, nextValues[i])) { instructions[i].value = nextValues[i]; changed = true; }
            if (HasBoundaries) { hybridGraph = graph; hybridValuesCurrent = true; }
            if (changed && material != null) Upload();
            if (lightingSurfaces != null) foreach (var surface in lightingSurfaces) surface?.CommitValues(graph);
        }

        sealed partial class Builder
        {
            internal readonly List<ValueBinding> valueBindings = new();
            internal readonly List<ResourceGuard> resourceGuards = new();
            internal readonly HashSet<int> fixedValues = new();
            Value BoundLiteral(ValueSource source, Vector4 data, ShaderGraphValueType type)
            {
                var value = Literal(data, type, bound: true);
                valueBindings.Add(new ValueBinding(source, value.register)); return value;
            }
            Value BoundResource(ValueSource source, ShaderParameter parameter, ShaderGraphValueType type, ShaderParameter fallback = null)
            {
                resourceGuards.Add(new ResourceGuard(source, parameter));
                return Palette(parameter ?? fallback, type);
            }
        }
    }
}
