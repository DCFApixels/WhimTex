using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class ShaderGraphLoops
    {
        internal static bool IsEndpoint(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.ForBegin or ShaderGraphNodeKind.ForEnd;

        internal static ShaderGraphNode AddPartner(ShaderGraph graph, ShaderGraphNode node)
        {
            if (!IsEndpoint(node.kind)) return null;
            bool begin = node.kind == ShaderGraphNodeKind.ForBegin;
            var partner = ShaderGraphNodes.Create(begin ? ShaderGraphNodeKind.ForEnd : ShaderGraphNodeKind.ForBegin,
                node.position + new Vector2(begin ? 360 : -360, 0));
            node.loopPartnerId = partner.id; partner.loopPartnerId = node.id;
            graph.nodes.Add(partner);
            graph.edges.Add(new ShaderGraphEdge { outputNode = begin ? node.id : partner.id, outputPort = "Value",
                inputNode = begin ? partner.id : node.id, inputPort = "Value" });
            return partner;
        }

        internal static void IncludePartners(ShaderGraph graph, HashSet<string> ids)
        {
            foreach (var node in graph.nodes)
                if (IsEndpoint(node.kind) && ids.Contains(node.id) && !string.IsNullOrEmpty(node.loopPartnerId)) ids.Add(node.loopPartnerId);
        }

        internal static void RemapPartners(IEnumerable<ShaderGraphNode> nodes, Dictionary<string, string> ids)
        {
            foreach (var node in nodes)
                if (IsEndpoint(node.kind))
                {
                    if (!ids.TryGetValue(node.loopPartnerId ?? "", out string partner))
                        throw new FormatException("Copy both endpoints of a For loop together.");
                    node.loopPartnerId = partner;
                }
        }
    }
}
