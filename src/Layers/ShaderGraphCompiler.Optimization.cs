using System;
using System.Collections.Generic;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        // Simplify compiler-owned pure expressions only after evaluating their inputs.
        // Input evaluation also validates nodes, emits calls and marks required helpers.
        // Dictionary uses reference identity here; each values instance denotes one
        // evaluation scope. All states are released together with this compiler.
        readonly Dictionary<Dictionary<string, Value>, OptimizationState> optimizationStates =
            new Dictionary<Dictionary<string, Value>, OptimizationState>();
        ulong optimizationGeneration;

        sealed class OptimizationState
        {
            internal ulong generation;
            internal readonly Dictionary<OptimizationKey, Value> expressions = new Dictionary<OptimizationKey, Value>();
            internal readonly Dictionary<OptimizationKey, OptimizationShape> shapes = new Dictionary<OptimizationKey, OptimizationShape>();
        }

        readonly struct OptimizationKey : IEquatable<OptimizationKey>
        {
            readonly ShaderGraphValueType type;
            readonly string text;
            internal OptimizationKey(Value value) { type = value.type; text = value.text; }
            public bool Equals(OptimizationKey other) => type == other.type && string.Equals(text, other.text, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is OptimizationKey other && Equals(other);
            public override int GetHashCode() => unchecked((int)type * 397 ^ (text == null ? 0 : StringComparer.Ordinal.GetHashCode(text)));
        }

        // A source is an already materialized numeric value, or an explicitly static
        // compiler literal. There is deliberately no parser for expression strings.
        sealed class OptimizationSource
        {
            internal readonly Value value;
            internal OptimizationSource(Value value) { this.value = value; }
        }

        readonly struct OptimizationLane
        {
            internal readonly OptimizationSource source;
            internal readonly int component;
            internal OptimizationLane(OptimizationSource source, int component) { this.source = source; this.component = component; }
        }

        sealed class OptimizationShape
        {
            internal readonly OptimizationLane[] lanes;
            internal OptimizationShape(OptimizationLane[] lanes) { this.lanes = lanes; }
        }

        static readonly OptimizationSource OptimizationZero = new OptimizationSource(new Value(ShaderGraphValueType.Float, "0.0"));
        static readonly OptimizationSource OptimizationOne = new OptimizationSource(new Value(ShaderGraphValueType.Float, "1.0"));

        OptimizationState CurrentOptimizationState
        {
            get
            {
                if (!optimizationStates.TryGetValue(values, out var state))
                {
                    state = new OptimizationState { generation = optimizationGeneration };
                    optimizationStates.Add(values, state);
                }
                else if (state.generation != optimizationGeneration)
                {
                    state.expressions.Clear(); state.shapes.Clear();
                    state.generation = optimizationGeneration;
                }
                return state;
            }
        }

        void OptimizationBarrier() { unchecked { optimizationGeneration++; } }

        void BeginOptimizedEmission(ShaderGraphNode node)
        {
            if (!IsOptimizableBuiltin(node.kind)) OptimizationBarrier();
        }

        Value CompleteOpaqueEmission(Value value)
        {
            // EmitFunction has an early return and may have evaluated pure inputs before
            // emitting its call. Invalidate those entries as well as any restored context.
            OptimizationBarrier();
            return value;
        }

        Value MaterializeOptimizedValue(ShaderGraphNode node, string port, Value expression, string variable = null)
        {
            bool pure = IsOptimizableBuiltin(node.kind);
            if (!pure) OptimizationBarrier();
            if (ShaderGraphNodes.Components(expression.type) == 0 && ShaderGraphNodes.MatrixSize(expression.type) == 0) return expression;

            var state = CurrentOptimizationState;
            var key = new OptimizationKey(expression);
            if (pure && state.expressions.TryGetValue(key, out var existing)) return existing;

            // Keep Constant's float declaration: casting an inline literal can change
            // constant evaluation in the native compiler (for example sin(PI)).
            // Identical constants still share this one declaration through CSE.

            // Routing expressions have at most four lanes rooted in materialized values.
            // Forward them without a temporary so Combine/Split round trips disappear
            // before HLSL compilation. Opaque inputs keep the normal declaration path.
            if (IsStructuralBuiltin(node.kind) && state.shapes.ContainsKey(key))
            {
                state.expressions[key] = expression;
                return expression;
            }

            variable = variable ?? "n" + NodeCodeName(node) + "_" + port;
            body.Append("#line 1 \"").Append(NodeSourceName(node)).AppendLine("\"");
            body.Append(Type(expression.type)).Append(' ').Append(variable).Append(" = ").Append(expression.text).AppendLine(";");
            var materialized = new Value(expression.type, variable);
            if (pure) state.expressions[key] = materialized;
            RememberOptimizationSource(state, materialized);
            return materialized;
        }

        static void RememberOptimizationSource(OptimizationState state, Value value)
        {
            if (!ShaderGraphNodes.IsNumeric(value.type)) return;
            var source = new OptimizationSource(value);
            var lanes = new OptimizationLane[ShaderGraphNodes.Components(value.type)];
            for (int i = 0; i < lanes.Length; i++) lanes[i] = new OptimizationLane(source, i);
            state.shapes[new OptimizationKey(value)] = new OptimizationShape(lanes);
        }

        Value OptimizeNumericCast(Value value, ShaderGraphValueType type)
        {
            if (value.type == type) return value;
            int from = ShaderGraphNodes.Components(value.type), to = ShaderGraphNodes.Components(type);
            var state = CurrentOptimizationState;
            if (state.shapes.TryGetValue(new OptimizationKey(value), out var shape))
            {
                var lanes = new OptimizationLane[to];
                for (int i = 0; i < to; i++)
                {
                    if (i < from) lanes[i] = shape.lanes[i];
                    else if (type == ShaderGraphValueType.Color && i == 3) lanes[i] = new OptimizationLane(OptimizationOne, 0);
                    else if (from == 1) lanes[i] = shape.lanes[0];
                    else lanes[i] = new OptimizationLane(OptimizationZero, 0);
                }
                return BuildOptimizationValue(state, type, lanes);
            }

            // Preserve the existing evaluation and formatting for unknown expressions.
            // In particular, never duplicate or inspect a helper/custom function call.
            if (from == to) return new Value(type, value.text);
            string expression = "(" + value.text + ")";
            if (from > to) return new Value(type, expression + "." + "xyzw".Substring(0, to));
            if (from == 1)
                return new Value(type, type == ShaderGraphValueType.Color ? "float4(" + expression + ".xxx, 1)" : expression + "." + new string('x', to));
            string tail = type == ShaderGraphValueType.Color ? (from == 2 ? ", 0, 1)" : ", 1)") : (to - from == 2 ? ", 0, 0)" : ", 0)");
            return new Value(type, Type(type) + "(" + value.text + tail);
        }

        Value EmitOptimizedCombine(ShaderGraphNode node)
        {
            int width = ShaderGraphNodes.Components(node.valueType);
            var inputs = new Value[width];
            for (int i = 0; i < width; i++) inputs[i] = Input(node, "XYZW"[i].ToString());
            // Fetch provenance only after every Input: a later input can erect a barrier.
            var state = CurrentOptimizationState;
            var lanes = new OptimizationLane[width];
            bool known = true;
            var text = new StringBuilder(Type(node.valueType)).Append('(');
            for (int i = 0; i < width; i++)
            {
                if (i != 0) text.Append(',');
                text.Append(inputs[i].text);
                if (state.shapes.TryGetValue(new OptimizationKey(inputs[i]), out var shape)) lanes[i] = shape.lanes[0];
                else known = false;
            }
            return known ? BuildOptimizationValue(state, node.valueType, lanes) : new Value(node.valueType, text.Append(')').ToString());
        }

        Value EmitOptimizedSplit(ShaderGraphNode node, string port)
        {
            var input = Input(node, "In");
            int component = "XYZW".IndexOf(port, StringComparison.Ordinal);
            if (!ShaderGraphNodes.IsNumeric(input.type) || component < 0 || component >= ShaderGraphNodes.Components(input.type))
                Error(node, "Component " + port + " is not present in the input.");
            var state = CurrentOptimizationState;
            if (state.shapes.TryGetValue(new OptimizationKey(input), out var shape))
                return BuildOptimizationValue(state, ShaderGraphValueType.Float, new[] { shape.lanes[component] });
            return new Value(ShaderGraphValueType.Float, input.type == ShaderGraphValueType.Float ? input.text : "(" + input.text + ")." + "xyzw"[component]);
        }

        Value EmitOptimizedSwizzle(ShaderGraphNode node)
        {
            string mask = node.swizzle;
            if (string.IsNullOrEmpty(mask) || mask.Length > 4) Error(node, "Mask must contain 1 to 4 components: xyzw, rgba or _ for zero.");
            var input = Input(node, "In");
            if (!ShaderGraphNodes.IsNumeric(input.type)) Error(node, "Numeric or Color input required.");
            input = Cast(input, ShaderGraphNodes.SwizzleInputType(mask, input.type), node);
            var type = ShaderGraphNodes.SwizzleType(mask);
            var state = CurrentOptimizationState;
            state.shapes.TryGetValue(new OptimizationKey(input), out var shape);
            var lanes = new OptimizationLane[mask.Length];
            bool known = true;
            var text = new StringBuilder();
            if (mask.Length > 1) text.Append(Type(type)).Append('(');
            for (int i = 0; i < mask.Length; i++)
            {
                int component = ShaderGraphNodes.SwizzleComponent(mask[i]);
                if (component == -2) Error(node, "Unknown component '" + mask[i] + "'. Use xyzw, rgba or _ for zero.");
                if (i != 0) text.Append(", ");
                if (component == -1)
                {
                    lanes[i] = new OptimizationLane(OptimizationZero, 0);
                    text.Append("0.0");
                }
                else
                {
                    if (shape != null) lanes[i] = shape.lanes[component];
                    else known = false;
                    text.Append('(').Append(input.text).Append(')');
                    if (ShaderGraphNodes.Components(input.type) > 1) text.Append('.').Append("xyzw"[component]);
                }
            }
            if (mask.Length > 1) text.Append(')');
            return known ? BuildOptimizationValue(state, type, lanes) : new Value(type, text.ToString());
        }

        Value BuildOptimizationValue(OptimizationState state, ShaderGraphValueType type, OptimizationLane[] lanes)
        {
            // Coalesce only adjacent lanes from the very same source object. Reconstructing
            // float4 from its XYZW returns the source verbatim; rgba/xyzw are equivalent.
            var text = new StringBuilder();
            int groups = 0;
            for (int start = 0; start < lanes.Length;)
            {
                int end = start + 1;
                while (end < lanes.Length && ReferenceEquals(lanes[end].source, lanes[start].source)) end++;
                if (groups++ != 0) text.Append(',');
                AppendOptimizationLanes(text, lanes, start, end);
                start = end;
            }
            string expression = text.ToString();
            if (groups > 1) expression = Type(type) + "(" + expression + ")";
            var value = new Value(type, expression);
            state.shapes[new OptimizationKey(value)] = new OptimizationShape(lanes);
            return value;
        }

        static void AppendOptimizationLanes(StringBuilder text, OptimizationLane[] lanes, int start, int end)
        {
            Value source = lanes[start].source.value;
            int width = ShaderGraphNodes.Components(source.type);
            bool identity = end - start == width;
            for (int i = start; identity && i < end; i++) identity = lanes[i].component == i - start;
            if (identity) { text.Append(source.text); return; }
            text.Append('(').Append(source.text).Append(").");
            for (int i = start; i < end; i++) text.Append("xyzw"[lanes[i].component]);
        }

        static bool IsStructuralBuiltin(ShaderGraphNodeKind kind) =>
            kind == ShaderGraphNodeKind.Combine || kind == ShaderGraphNodeKind.Split || kind == ShaderGraphNodeKind.Swizzle;

        static bool IsOptimizableBuiltin(ShaderGraphNodeKind kind)
        {
            // Explicit allowlist: adding a node requires checking its emitter and callees
            // for writes, implicit evaluation context and user-supplied code. Expression,
            // sampling, loops, noise, Blend and custom functions intentionally remain opaque.
            if (ShaderGraphNodes.UnaryTrigonometryFunction(kind) != null || kind == ShaderGraphNodeKind.Arctangent2)
                return true;
            switch (kind)
            {
                case ShaderGraphNodeKind.Parameter:
                case ShaderGraphNodeKind.Value:
                case ShaderGraphNodeKind.MatrixConstruction:
                case ShaderGraphNodeKind.MatrixDeterminant:
                case ShaderGraphNodeKind.MatrixSplit:
                case ShaderGraphNodeKind.MatrixTranspose:
                case ShaderGraphNodeKind.Projection:
                case ShaderGraphNodeKind.Rejection:
                case ShaderGraphNodeKind.FresnelEffect:
                case ShaderGraphNodeKind.SphereMask:
                case ShaderGraphNodeKind.Constant:
                case ShaderGraphNodeKind.UV:
                case ShaderGraphNodeKind.CanvasData:
                case ShaderGraphNodeKind.Reroute:
                case ShaderGraphNodeKind.SubGraphInput:
                case ShaderGraphNodeKind.Combine:
                case ShaderGraphNodeKind.Split:
                case ShaderGraphNodeKind.Swizzle:
                case ShaderGraphNodeKind.Add:
                case ShaderGraphNodeKind.Subtract:
                case ShaderGraphNodeKind.Multiply:
                case ShaderGraphNodeKind.Divide:
                case ShaderGraphNodeKind.Minimum:
                case ShaderGraphNodeKind.Maximum:
                case ShaderGraphNodeKind.Power:
                case ShaderGraphNodeKind.Step:
                case ShaderGraphNodeKind.Distance:
                case ShaderGraphNodeKind.DotProduct:
                case ShaderGraphNodeKind.Absolute:
                case ShaderGraphNodeKind.OneMinus:
                case ShaderGraphNodeKind.Negate:
                case ShaderGraphNodeKind.SquareRoot:
                case ShaderGraphNodeKind.Floor:
                case ShaderGraphNodeKind.Ceiling:
                case ShaderGraphNodeKind.Round:
                case ShaderGraphNodeKind.Truncate:
                case ShaderGraphNodeKind.NoiseSineWave:
                case ShaderGraphNodeKind.SawtoothWave:
                case ShaderGraphNodeKind.SquareWave:
                case ShaderGraphNodeKind.TriangleWave:
                case ShaderGraphNodeKind.Fraction:
                case ShaderGraphNodeKind.Length:
                case ShaderGraphNodeKind.Normalize:
                case ShaderGraphNodeKind.Reciprocal:
                case ShaderGraphNodeKind.ReciprocalSquareRoot:
                case ShaderGraphNodeKind.Clamp:
                case ShaderGraphNodeKind.Smoothstep:
                case ShaderGraphNodeKind.Lerp:
                case ShaderGraphNodeKind.Select:
                case ShaderGraphNodeKind.InverseLerp:
                case ShaderGraphNodeKind.Remap:
                case ShaderGraphNodeKind.Compare:
                case ShaderGraphNodeKind.Hue:
                case ShaderGraphNodeKind.Saturation:
                case ShaderGraphNodeKind.Exposure:
                case ShaderGraphNodeKind.Lightness:
                case ShaderGraphNodeKind.Levels:
                case ShaderGraphNodeKind.ColorBalance:
                case ShaderGraphNodeKind.ChannelMixer:
                case ShaderGraphNodeKind.WhiteBalance:
                case ShaderGraphNodeKind.Posterize:
                case ShaderGraphNodeKind.ConvertColor:
                case ShaderGraphNodeKind.Luminance:
                case ShaderGraphNodeKind.ColorMask:
                case ShaderGraphNodeKind.HueRange:
                case ShaderGraphNodeKind.MixColor:
                case ShaderGraphNodeKind.SetAlpha:
                    return true;
                default: return false;
            }
        }
    }
}
