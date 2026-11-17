using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void ArrangeAuthoredGraph(JObject data, ShaderGraph draft, ShaderGraph previous)
        {
            if (data == null || draft == null) return;
            draft.BindFunctions();
            ArrangeScope(data, draft, previous?.nodes);
            if (data["subGraphs"] is not JArray definitions) return;
            for (int i = 0; i < definitions.Count; i++)
            {
                var definition = draft.subGraphs[i];
                ArrangeScope((JObject)definitions[i], draft.SubGraphView(definition),
                    previous?.FindSubGraph(definition.id)?.nodes);
            }
        }

        private static void ArrangeScope(JObject data, ShaderGraph graph, List<ShaderGraphNode> previous)
        {
            if (data["nodes"] is not JArray nodes) return;
            var moving = new HashSet<string>(System.StringComparer.Ordinal);
            Dictionary<string, Vector2> original = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (((JObject)nodes[i]).ContainsKey("position")) continue;
                var node = graph.nodes[i];
                if (original == null)
                {
                    original = new Dictionary<string, Vector2>(System.StringComparer.Ordinal);
                    if (previous != null)
                        foreach (var retained in previous) original.TryAdd(retained.id, retained.position);
                }
                if (original.TryGetValue(node.id, out var position))
                    node.position = position;
                else moving.Add(node.id);
            }
            if (moving.Count == 0) return;

            var positions = ShaderGraphArrangement.Plan(graph, moving);
            foreach (var node in graph.nodes)
                if (positions.TryGetValue(node.id, out var position)) node.position = position;
        }
    }
}
