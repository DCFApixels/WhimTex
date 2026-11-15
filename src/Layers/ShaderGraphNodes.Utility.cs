using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderGraphComparison { Less, LessOrEqual, Greater, GreaterOrEqual, Equal, NotEqual }

    [Serializable]
    public sealed class ShaderGraphSwitchCase
    {
        public string port = Guid.NewGuid().ToString("N");
        public ShaderGraphComparison comparison = ShaderGraphComparison.Equal;
        public float threshold;
    }

    internal static partial class ShaderGraphNodes
    {
        static readonly ShaderGraphSwitchCase[] defaultSwitchCases =
        {
            new() { port = "A", threshold = 0 }, new() { port = "B", threshold = 1 }
        };
        internal static IReadOnlyList<ShaderGraphSwitchCase> SwitchCases(ShaderGraphNode node) =>
            (IReadOnlyList<ShaderGraphSwitchCase>)node.switchCases ?? defaultSwitchCases;
        internal static List<ShaderGraphSwitchCase> EditableSwitchCases(ShaderGraphNode node)
        {
            if (node.switchCases == null)
            {
                node.switchCases = new List<ShaderGraphSwitchCase>();
                foreach (var item in defaultSwitchCases)
                    node.switchCases.Add(new ShaderGraphSwitchCase { port = item.port, comparison = item.comparison, threshold = item.threshold });
            }
            return node.switchCases;
        }
        internal static string ComparisonOperator(ShaderGraphComparison comparison) => comparison switch
        {
            ShaderGraphComparison.Less => "<", ShaderGraphComparison.LessOrEqual => "<=",
            ShaderGraphComparison.Greater => ">", ShaderGraphComparison.GreaterOrEqual => ">=",
            ShaderGraphComparison.Equal => "==", ShaderGraphComparison.NotEqual => "!=",
            _ => throw new ArgumentOutOfRangeException(nameof(comparison))
        };
        internal static string SwitchPortLabel(ShaderGraphNode node, string port)
        {
            var cases = SwitchCases(node);
            for (int i = 0; i < cases.Count; i++) if (cases[i]?.port == port) return ((char)('A' + i)).ToString();
            return port;
        }
        internal static void RemoveSwitchCase(ShaderGraph graph, ShaderGraphNode node, int index)
        {
            var cases = EditableSwitchCases(node);
            string port = cases[index].port;
            cases.RemoveAt(index);
            node.inputs.RemoveAll(p => p.port == port);
            graph.edges.RemoveAll(e => e.inputNode == node.id && e.inputPort == port);
        }
        static IReadOnlyList<ShaderGraphPort> UtilityInputs(ShaderGraphNode node)
        {
            if (node.kind != ShaderGraphNodeKind.Switch) return null;
            var cases = SwitchCases(node);
            var cached = node.switchPortsCache;
            bool valid = cached != null && cached.Length == cases.Count + 2;
            for (int i = 0; valid && i < cases.Count; i++) valid = cached[i + 2].name == cases[i]?.port;
            if (valid) return cached;
            var result = new ShaderGraphPort[cases.Count + 2];
            result[0] = N("Predicate"); result[1] = P("Fallback");
            for (int i = 0; i < cases.Count; i++) result[i + 2] = P(cases[i]?.port ?? "");
            return node.switchPortsCache = result;
        }
        static void BuildUtilityPorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            map[ShaderGraphNodeKind.Append] = new[] { P("A"), P("B") };
            map[ShaderGraphNodeKind.All] = map[ShaderGraphNodeKind.Any] = new[] { P("In") };
            map[ShaderGraphNodeKind.IsNaN] = map[ShaderGraphNodeKind.IsInfinite] = new[] { N("In") };
            BuildArtistPorts(map);
        }
        internal static string UtilityCategory(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.Append => "Channel",
            ShaderGraphNodeKind.Switch or ShaderGraphNodeKind.All or ShaderGraphNodeKind.Any or
            ShaderGraphNodeKind.IsNaN or ShaderGraphNodeKind.IsInfinite => "Logic",
            ShaderGraphNodeKind.ParallaxMapping or ShaderGraphNodeKind.ParallaxOcclusionMapping => "UV/Distortion",
            _ => ArtistCategory(kind)
        };
        internal static bool TryUtilityOutputType(ShaderGraphNodeKind kind, out ShaderGraphValueType type)
        {
            if (kind is ShaderGraphNodeKind.All or ShaderGraphNodeKind.Any or ShaderGraphNodeKind.IsNaN or ShaderGraphNodeKind.IsInfinite)
            { type = ShaderGraphValueType.Boolean; return true; }
            if (IsParallax(kind)) { type = ShaderGraphValueType.Float2; return true; }
            return TryArtistOutputType(kind, out type);
        }
        internal static ShaderGraphValueType AppendType(ShaderGraphValueType a, ShaderGraphValueType b) =>
            (ShaderGraphValueType)(Math.Min(3, Components(a)) + Math.Min(4 - Math.Min(3, Components(a)), Components(b)) - 1);
    }
}
