using System;
using System.Collections.Generic;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        readonly struct LoopFrame
        {
            internal readonly ShaderGraphNode begin;
            internal readonly ShaderGraphValueType type;
            internal string Value => "loopValue" + begin.id;
            internal string Index => "loopIndex" + begin.id;
            internal LoopFrame(ShaderGraphNode begin, ShaderGraphValueType type) { this.begin = begin; this.type = type; }
        }
        List<LoopFrame> loopFrames = new();
        bool previewLoopInputs, bufferEvaluation;

        string LoopParameters
        {
            get
            {
                if (loopFrames.Count == 0) return string.Empty;
                var text = new StringBuilder();
                foreach (var frame in loopFrames)
                    text.Append(',').Append(Type(frame.type)).Append(' ').Append(frame.Value).Append(",int ").Append(frame.Index);
                return text.ToString();
            }
        }
        string LoopArguments
        {
            get
            {
                if (loopFrames.Count == 0) return string.Empty;
                var text = new StringBuilder();
                foreach (var frame in loopFrames) text.Append(',').Append(frame.Value).Append(',').Append(frame.Index);
                return text.ToString();
            }
        }
        string LoopSamplingKey
        {
            get
            {
                string mode = previewLoopInputs ? ":preview" : ":runtime";
                if (loopFrames.Count == 0) return mode;
                var text = new StringBuilder(mode);
                foreach (var frame in loopFrames) text.Append(':').Append(frame.begin.id).Append(':').Append((int)frame.type);
                return text.ToString();
            }
        }
        static string Call(Value function, string arguments) => function.text + "(" + arguments + function.arguments + ")";

        T OutsideLoop<T>(Func<T> generate)
        {
            var previous = loopFrames; bool preview = previewLoopInputs, buffer = bufferEvaluation;
            loopFrames = new List<LoopFrame>(); previewLoopInputs = false; bufferEvaluation = true;
            try { return generate(); }
            finally { loopFrames = previous; previewLoopInputs = preview; bufferEvaluation = buffer; }
        }

        void ValidateLoops()
        {
            foreach (var node in graph.nodes)
                if (ShaderGraphLoops.IsEndpoint(node.kind) &&
                    (!nodes.TryGetValue(node.loopPartnerId ?? "", out var partner) ||
                    !ShaderGraphLoops.IsEndpoint(partner.kind) || partner.kind == node.kind || partner.loopPartnerId != node.id))
                    Error(node, "For Begin and For End must reference each other through loopPartnerId in the same graph.");
        }

        Value EmitForBegin(ShaderGraphNode node, string port)
        {
            foreach (var frame in loopFrames)
                if (frame.begin.id == node.id)
                    return port == "Index" ? new Value(ShaderGraphValueType.Float, "(float)" + frame.Index) : new Value(frame.type, frame.Value);
            if (previewLoopInputs)
                return port == "Index" ? new Value(ShaderGraphValueType.Float, "0.0") : Input(node, "Initial");
            Error(node, bufferEvaluation
                ? "A buffer cannot depend on a running loop's Index or Value. Bake before or after the complete loop."
                : "Index and Value can only be used inside their paired For End. Connect Result to the rest of the graph.");
            return default;
        }

        Value EmitForEnd(ShaderGraphNode node)
        {
            var begin = nodes[node.loopPartnerId];
            TraceNode(begin);
            foreach (var active in loopFrames) if (active.begin.id == begin.id) Error(node, "A For loop cannot enter itself.");
            var initial = Input(begin, "Initial");
            if (!ShaderGraphNodes.IsNumeric(initial.type)) Error(begin, "Initial must be Float–Float4 or Color.");
            var iterations = Input(begin, "Iterations");
            var frame = new LoopFrame(begin, initial.type);
            string name = "loopResult" + node.id, count = "loopCount" + node.id;
            body.Append(Type(initial.type)).Append(' ').Append(name).Append(" = ").Append(initial.text).AppendLine(";")
                .Append("int ").Append(count).Append(" = (int)max(0.0, floor(").Append(iterations.text).AppendLine("));")
                .Append("[loop] for (int ").Append(frame.Index).Append(" = 0; ").Append(frame.Index).Append(" < ").Append(count)
                .Append("; ++").Append(frame.Index).AppendLine(") {")
                .Append(Type(initial.type)).Append(' ').Append(frame.Value).Append(" = ").Append(name).AppendLine(";");
            var previousValues = values; bool previousSampled = sampledEvaluation;
            values = new Dictionary<string, Value>(); sampledEvaluation = true; usesSampleFootprint = true;
            loopFrames.Add(frame);
            try
            {
                var next = Cast(Input(node, "Value"), initial.type, node);
                body.Append(name).Append(" = ").Append(next.text).AppendLine(";");
            }
            finally
            {
                loopFrames.RemoveAt(loopFrames.Count - 1);
                values = previousValues; sampledEvaluation = previousSampled;
            }
            body.AppendLine("}");
            return new Value(initial.type, name);
        }
    }
}
