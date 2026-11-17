using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>Computes positions without touching the graph or requiring an Editor window.</summary>
    internal static class ShaderGraphArrangement
    {
        const float ColumnGap = 96, RowGap = 40, ObstacleGap = 32;

        internal sealed class Geometry
        {
            // All bounds and ports are relative to the node's saved position.
            internal Rect bounds;
            internal readonly Dictionary<string, Vector2> inputs = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, Vector2> outputs = new(StringComparer.Ordinal);
            internal Vector2 Port(string name, bool output)
            {
                var ports = output ? outputs : inputs;
                return name != null && ports.TryGetValue(name, out var p) ? p :
                    new Vector2(output ? bounds.xMax : 0, bounds.yMin + Math.Min(48, bounds.height * .5f));
            }
        }

        sealed class Item
        {
            internal ShaderGraphNode node;
            internal ShaderGraphGroup group;
            internal Geometry geometry;
            internal Vector2 position, point;
            internal bool moving, placed;
            internal int order, rank, pending;
            internal readonly List<Link> incoming = new(), outgoing = new();
            internal Rect Bounds(Vector2 at) => Translate(geometry.bounds, at);
        }

        readonly struct Link
        {
            internal readonly Item from, to;
            internal readonly Vector2 output, input;
            internal Link(Item from, Item to, string output, string input)
            {
                this.from = from; this.to = to;
                this.output = from.geometry.Port(output, true);
                this.input = to.geometry.Port(input, false);
            }
        }

        internal static Dictionary<string, Vector2> Plan(ShaderGraph graph, HashSet<string> moving,
            IReadOnlyDictionary<string, Geometry> measured = null, IReadOnlyDictionary<string, Rect> measuredGroups = null,
            bool keepNearSelection = false)
        {
            var result = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            if (moving.Count == 0) return result;
            var items = new Dictionary<string, Item>(StringComparer.Ordinal);
            var portsWithInput = new HashSet<(string, string)>();
            var portsWithOutput = new HashSet<(string, string)>();
            var groupOf = new Dictionary<string, ShaderGraphGroup>(StringComparer.Ordinal);
            foreach (var group in graph.groups)
                foreach (string member in group.elements) groupOf.TryAdd(member, group);
            foreach (var edge in graph.edges)
            {
                portsWithInput.Add((edge.inputNode, edge.inputPort));
                portsWithOutput.Add((edge.outputNode, edge.outputPort));
            }
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                var node = graph.nodes[i];
                groupOf.TryGetValue(node.id, out var group);
                var geometry = measured != null && measured.TryGetValue(node.id, out var resolved)
                    ? resolved : Estimate(graph, node, portsWithInput, portsWithOutput);
                var item = new Item { node = node, position = node.position, group = group,
                    moving = moving.Contains(node.id), geometry = geometry, order = i };
                items.Add(node.id, item);
            }
            void LinkNodes(string from, string output, string to, string input)
            {
                if (from == null || to == null || !items.TryGetValue(from, out var a) || !items.TryGetValue(to, out var b)) return;
                var link = new Link(a, b, output, input);
                a.outgoing.Add(link); b.incoming.Add(link);
            }
            foreach (var edge in graph.edges)
                LinkNodes(edge.outputNode, edge.outputPort, edge.inputNode, edge.inputPort);
            foreach (var item in items.Values)
                if (ShaderGraphConnections.ImplicitDependency(item.node) is string from)
                    LinkNodes(from, "Value", item.node.id, null);

            var allItems = items.Values.ToArray();
            var notesByGroup = graph.notes.Where(n => groupOf.ContainsKey(n.id)).ToLookup(n => groupOf[n.id]);
            foreach (var batch in allItems.Where(i => i.moving).GroupBy(i => i.group).ToArray())
            {
                var selected = batch.ToArray();
                var group = batch.Key;
                var fixedItems = allItems.Where(i => !i.moving || i.placed).ToArray();
                var fixedByGroup = fixedItems.ToLookup(i => i.group);
                var blockers = fixedItems.Select(i => i.Bounds(i.position)).ToList();
                foreach (var note in graph.notes) blockers.Add(new Rect(note.position, note.size));
                foreach (var other in graph.groups)
                {
                    if (other == group) continue;
                    if (measuredGroups != null && measuredGroups.TryGetValue(other.id, out var rect))
                        blockers.Add(rect);
                    var members = fixedByGroup[other].Select(i => i.Bounds(i.position))
                        .Concat(notesByGroup[other].Select(n => new Rect(n.position, n.size))).ToArray();
                    if (members.Length > 0) blockers.Add(Expand(Enclose(members), ObstacleGap));
                    else if (other.elements.Count == 0) blockers.Add(new Rect(other.position, new Vector2(170, 48)));
                }

                PlaceInColumns(selected, group, keepNearSelection);
                ImproveWires(selected);
                var block = Enclose(selected.Select(i => i.Bounds(i.point)));
                Vector2 preferred = PreferredOffset(selected, fixedItems, blockers, block, group, keepNearSelection);
                IReadOnlyList<Rect> parts = group == null ? selected.Select(i => i.Bounds(i.point)).ToArray() : new[] { Expand(block, ObstacleGap) };
                var offset = AvoidObstacles(parts, blockers, block, preferred);
                foreach (var item in selected)
                {
                    item.position = item.point + offset;
                    item.placed = true;
                    result.Add(item.node.id, item.position);
                }
            }
            return result;
        }

        static void PlaceInColumns(Item[] nodes, ShaderGraphGroup group, bool keepNearSelection)
        {
            bool InBatch(Item item) => item.moving && item.group == group;
            var queue = new Queue<Item>();
            foreach (var node in nodes)
            {
                node.pending = node.incoming.Count(l => InBatch(l.from));
                if (node.pending == 0) queue.Enqueue(node);
            }

            // Longest path from sources. Retained inactive links can form a cycle;
            // put unresolved nodes in a final column rather than looping.
            int lastRank = 0;
            while (queue.Count != 0)
            {
                var item = queue.Dequeue();
                lastRank = Math.Max(lastRank, item.rank);
                foreach (var link in item.outgoing)
                {
                    var next = link.to;
                    if (!InBatch(next)) continue;
                    next.rank = Math.Max(next.rank, item.rank + 1);
                    if (--next.pending == 0) queue.Enqueue(next);
                }
            }
            foreach (var node in nodes)
                if (node.pending != 0) node.rank = lastRank + 1;
            var columns = nodes.GroupBy(n => n.rank).OrderBy(g => g.Key)
                .Select(g => keepNearSelection ? g.OrderBy(n => n.position.y).ThenBy(n => n.order).ToArray() : g.ToArray()).ToArray();

            float left = 0;
            foreach (var column in columns)
            {
                float x = left - column.Min(i => i.geometry.bounds.xMin);
                float y = 0;
                foreach (var node in column)
                {
                    node.point = new Vector2(x, y - node.geometry.bounds.yMin);
                    y = node.point.y + node.geometry.bounds.yMax + RowGap;
                }
                left = x + column.Max(i => i.geometry.bounds.xMax) + ColumnGap;
            }

            // Sweeping both directions makes the order follow the connected ports.
            // Give the local connections more weight when a wire spans several columns.
            for (int pass = 0; pass < 6; pass++)
            {
                bool forward = (pass & 1) != 0;
                for (int c = 0; c < columns.Length; c++)
                {
                    var column = columns[forward ? c : columns.Length - 1 - c];
                    var desired = new Dictionary<Item, float>(column.Length);
                    foreach (var node in column)
                    {
                        float sum = 0, weight = 0;
                        var links = forward ? node.incoming : node.outgoing;
                        foreach (var link in links)
                        {
                            var peer = forward ? link.from : link.to;
                            if (!InBatch(peer) || peer.rank == node.rank) continue;
                            float w = 1f / Math.Abs(peer.rank - node.rank);
                            sum += (peer.point.y + (forward ? link.output.y - link.input.y : link.input.y - link.output.y)) * w;
                            weight += w;
                        }
                        desired.Add(node, weight == 0 ? node.point.y : .9f * sum / weight + .1f * node.point.y);
                    }
                    Array.Sort(column, (a, b) =>
                    {
                        int cmp = desired[a].CompareTo(desired[b]);
                        return cmp == 0 ? a.order.CompareTo(b.order) : cmp;
                    });
                    PackColumn(column, desired);
                }
            }
        }

        static void PackColumn(Item[] column, Dictionary<Item, float> desired)
        {
            // Project the desired positions onto ordered positions with gaps.
            // Adjacent conflicting intervals merge and share their average shift.
            int length = column.Length, count = 0;
            var offsets = new float[length];
            var sums = new float[length];
            var starts = new int[length];
            var sizes = new int[length];
            for (int i = 0; i < length; i++)
            {
                if (i > 0) offsets[i] = offsets[i - 1] + column[i - 1].geometry.bounds.yMax - column[i].geometry.bounds.yMin + RowGap;
                sums[count] = desired[column[i]] - offsets[i]; starts[count] = i; sizes[count++] = 1;
                while (count > 1 && sums[count - 2] / sizes[count - 2] > sums[count - 1] / sizes[count - 1])
                {
                    count--;
                    sums[count - 1] += sums[count];
                    sizes[count - 1] += sizes[count];
                }
            }
            for (int i = 0; i < count; i++)
            {
                float y = sums[i] / sizes[i];
                for (int j = starts[i]; j < starts[i] + sizes[i]; j++) column[j].point.y = y + offsets[j];
            }
        }

        static void ImproveWires(Item[] nodes)
        {
            // Keep this pass bounded. Large graphs still use the port-order sweeps.
            if (nodes.Length > 64) return;
            var members = new HashSet<Item>(nodes);
            var links = nodes.SelectMany(n => n.outgoing).Where(l => members.Contains(l.to)).ToArray();
            if (links.Length == 0 || links.Length > 96) return;
            var bounds = nodes.Select(n => n.Bounds(n.point)).ToArray();
            var ends = new (Vector2 from, Vector2 to)[links.Length];

            float Score()
            {
                float score = 0;
                for (int i = 0; i < links.Length; i++)
                {
                    var link = links[i];
                    Vector2 from = link.from.point + link.output, to = link.to.point + link.input;
                    ends[i] = (from, to);
                    score += Vector2.Distance(from, to) / 800;
                    for (int j = 0; j < nodes.Length; j++)
                    {
                        var node = nodes[j];
                        if (node != link.from && node != link.to && SpansBox(from, to, bounds[j])) score += 6;
                    }
                    for (int j = 0; j < i; j++)
                    {
                        var other = ends[j];
                        if (Crosses(from, to, other.from, other.to)) score += 3;
                    }
                }
                return score;
            }

            float best = Score();
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < nodes.Length; i++)
                {
                    int index = pass == 0 ? i : nodes.Length - 1 - i;
                    var node = nodes[index];
                    float initialY = node.point.y, bestY = initialY;
                    void Try(float y)
                    {
                        if (Math.Abs(y - node.point.y) < 1) return;
                        var point = new Vector2(node.point.x, y);
                        var rect = node.Bounds(point);
                        var occupied = Expand(rect, RowGap - 1);
                        for (int j = 0; j < bounds.Length; j++)
                            if (j != index && occupied.Overlaps(bounds[j])) return;
                        bounds[index] = rect; node.point.y = y;
                        float score = Score();
                        if (score < best - .001f) { best = score; bestY = y; }
                        else { node.point.y = bestY; bounds[index] = node.Bounds(node.point); }
                    }
                    Try(initialY - 2 * RowGap);
                    Try(initialY - RowGap);
                    Try(initialY + RowGap);
                    Try(initialY + 2 * RowGap);
                    foreach (var link in node.incoming)
                        if (members.Contains(link.from)) Try(link.from.point.y + link.output.y - link.input.y);
                    foreach (var link in node.outgoing)
                        if (members.Contains(link.to)) Try(link.to.point.y + link.input.y - link.output.y);
                }
        }

        static bool SpansBox(Vector2 a, Vector2 b, Rect r)
        {
            if (b.x <= a.x + 1 || a.x >= r.xMax || b.x <= r.xMin) return false;
            float near = Mathf.Lerp(a.y, b.y, Mathf.Clamp01((r.xMin - a.x) / (b.x - a.x)));
            float far = Mathf.Lerp(a.y, b.y, Mathf.Clamp01((r.xMax - a.x) / (b.x - a.x)));
            return Math.Max(near, far) > r.yMin && Math.Min(near, far) < r.yMax;
        }

        static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            if (Math.Min(b.x, d.x) <= Math.Max(a.x, c.x) ||
                Math.Min(Math.Max(a.y, b.y), Math.Max(c.y, d.y)) <= Math.Max(Math.Min(a.y, b.y), Math.Min(c.y, d.y))) return false;
            float Side(Vector2 p, Vector2 q, Vector2 r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
            return Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;
        }

        static Vector2 PreferredOffset(Item[] batch, Item[] fixedItems, List<Rect> blockers, Rect block,
            ShaderGraphGroup group, bool keepNearSelection)
        {
            if (keepNearSelection)
                return Enclose(batch.Select(i => i.Bounds(i.position))).position - block.position;
            var anchors = new List<Vector2>();
            var inBatch = new HashSet<Item>(batch);
            foreach (var item in batch)
            {
                foreach (var link in item.incoming)
                    if (!inBatch.Contains(link.from) && (!link.from.moving || link.from.placed))
                        anchors.Add(link.from.position + link.output - item.point - link.input +
                            new Vector2(ColumnGap - item.geometry.bounds.xMin, 0));
                foreach (var link in item.outgoing)
                    if (!inBatch.Contains(link.to) && (!link.to.moving || link.to.placed))
                        anchors.Add(link.to.position + link.input - item.point - link.output +
                            new Vector2(link.to.geometry.bounds.xMin - ColumnGap, 0));
            }
            if (anchors.Count > 0)
            {
                float Median(IEnumerable<float> axis) => axis.OrderBy(v => v).ElementAt(anchors.Count / 2);
                return new Vector2(Median(anchors.Select(p => p.x)), Median(anchors.Select(p => p.y)));
            }
            if (group != null)
            {
                var members = fixedItems.Where(i => i.group == group).Select(i => i.Bounds(i.position)).ToArray();
                if (members.Length == 0) return group.position - block.position;
                var bounds = Enclose(members);
                return new Vector2(bounds.xMin, bounds.yMax + RowGap) - block.position;
            }
            if (blockers.Count == 0) return -block.position;
            var scene = Enclose(blockers);
            return new Vector2(scene.xMax + ColumnGap, scene.yMin) - block.position;
        }

        static Vector2 AvoidObstacles(IReadOnlyList<Rect> parts, List<Rect> blockers, Rect block, Vector2 preferred)
        {
            if (blockers.Count == 0) return preferred;
            var taken = Enclose(blockers);
            var xs = new[] { preferred.x, preferred.x - ColumnGap, preferred.x + ColumnGap,
                taken.xMin - block.xMax - 2 * ObstacleGap, taken.xMax - block.xMin + 2 * ObstacleGap };
            Vector2 best = preferred;
            float bestDistance = float.PositiveInfinity;
            var intervals = new List<Vector2>();
            foreach (float x in xs)
            {
                intervals.Clear();
                foreach (var blocker in blockers)
                    foreach (var part in parts)
                        if (part.xMin + x < blocker.xMax + ObstacleGap && part.xMax + x > blocker.xMin - ObstacleGap)
                            intervals.Add(new Vector2(blocker.yMin - part.yMax - ObstacleGap, blocker.yMax - part.yMin + ObstacleGap));
                intervals.Sort((a, b) => a.x.CompareTo(b.x));
                float bottom = float.NegativeInfinity, top = float.NegativeInfinity;
                foreach (var interval in intervals)
                {
                    if (interval.x > top)
                    {
                        if (preferred.y > bottom && preferred.y < top) break;
                        bottom = interval.x; top = interval.y;
                    }
                    else top = Math.Max(top, interval.y);
                }
                float y = preferred.y;
                if (y > bottom && y < top) y = y - bottom < top - y ? bottom - 1 : top + 1;
                var candidate = new Vector2(x, y);
                float distance = (candidate - preferred).sqrMagnitude;
                if (distance < bestDistance) { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        internal static Rect Translate(Rect rect, Vector2 by) => new Rect(rect.position + by, rect.size);

        static Rect Expand(Rect r, float pad) => Rect.MinMaxRect(r.xMin - pad, r.yMin - pad, r.xMax + pad, r.yMax + pad);

        static Rect Enclose(IEnumerable<Rect> rects)
        {
            using var cursor = rects.GetEnumerator();
            if (!cursor.MoveNext()) return default;
            var bounds = cursor.Current;
            while (cursor.MoveNext()) bounds = Union(bounds, cursor.Current);
            return bounds;
        }

        internal static Rect Union(Rect a, Rect b) =>
            Rect.MinMaxRect(Math.Min(a.xMin, b.xMin), Math.Min(a.yMin, b.yMin), Math.Max(a.xMax, b.xMax), Math.Max(a.yMax, b.yMax));

        static Geometry Estimate(ShaderGraph graph, ShaderGraphNode node,
            HashSet<(string, string)> connectedInputs, HashSet<(string, string)> connectedOutputs)
        {
            var result = new Geometry();
            if (node.kind == ShaderGraphNodeKind.Reroute)
            {
                result.bounds = new Rect(0, 0, 48, 24);
                result.inputs["In"] = new Vector2(4, 12);
                result.outputs["Out"] = new Vector2(44, 12);
                return result;
            }
            string title = node.kind switch
            {
                ShaderGraphNodeKind.Parameter => graph.FindParameter(node.parameterId)?.name,
                ShaderGraphNodeKind.CustomFunction => node.function?.name,
                ShaderGraphNodeKind.SubGraph => node.subGraph?.name,
                _ => ShaderGraphNodes.Name(node.kind)
            };
            if (node.kind == ShaderGraphNodeKind.Parameter)
            {
                float w = Math.Max(120, (title?.Length ?? 0) * 8 + 50);
                result.bounds = new Rect(0, 0, w, 42);
                result.outputs["Out"] = new Vector2(w - 4, 21);
                return result;
            }

            // Conservative size assumptions for the authoring API. The editor
            // uses laid-out views, including port labels and outside fields.
            var inputPorts = ShaderGraphNodes.StoredInputs(node).Where(p =>
                connectedInputs.Contains((node.id, p.name)) || ShaderGraphNodes.InputVisible(node, p.name)).ToArray();
            var outputPorts = ShaderGraphNodes.StoredOutputs(node).Where(p =>
                connectedOutputs.Contains((node.id, p)) || ShaderGraphNodes.OutputVisible(node, p)).ToArray();
            int LabelWidth(string name, bool output)
            {
                var label = node.function?.ports.Find(p => p.name == name)?.label;
                if (string.IsNullOrEmpty(label)) label = ShaderGraphNodes.PortDisplayName(node, name, output);
                return label?.Length ?? 0;
            }
            int inputChars = inputPorts.Select(p => LabelWidth(p.name, false)).DefaultIfEmpty().Max();
            int outputChars = outputPorts.Select(p => LabelWidth(p, true)).DefaultIfEmpty().Max();
            float outset = inputPorts.Where(p => !connectedInputs.Contains((node.id, p.name)))
                .Select(p => EstimateOutset(node, p)).DefaultIfEmpty().Max();
            float width = Math.Max(180, Math.Max((title?.Length ?? 0) * 8 + 48, (inputChars + outputChars) * 8 + 82));
            float height = 64 + Math.Max(inputPorts.Length, outputPorts.Length) * 26;
            if (node.previewExpanded && ShaderGraphNodes.CanHaveNodePreview(node.kind)) height += 156;
            if (ShaderGraphNodes.IsNoise(node.kind)) height += 102;
            else if (ShaderGraphNodes.ModeNames(node.kind) != null) height += 54;
            if (node.kind is ShaderGraphNodeKind.Levels or ShaderGraphNodeKind.HeightToNormal or ShaderGraphNodeKind.NormalLighting or
                ShaderGraphNodeKind.Caustics or ShaderGraphNodeKind.Scatter or ShaderGraphNodeKind.Kuwahara) height += 106;
            if (node.kind is ShaderGraphNodeKind.Expression or ShaderGraphNodeKind.Value or ShaderGraphNodeKind.Switch or
                ShaderGraphNodeKind.ConvertAngle or ShaderGraphNodeKind.ConvertColor or ShaderGraphNodeKind.Fractal) height += 144;
            if (node.kind == ShaderGraphNodeKind.CustomFunction && node.function?.inline == true)
            {
                width = Math.Max(width, node.codeExpanded ? Math.Max(340, node.codeSize.x) : 265);
                height += node.codeExpanded ? Math.Max(260, node.codeSize.y + 122) : 95;
            }
            for (int i = 0; i < inputPorts.Length; i++) result.inputs[inputPorts[i].name] = new Vector2(4, 56 + i * 26);
            for (int i = 0; i < outputPorts.Length; i++) result.outputs[outputPorts[i]] = new Vector2(width - 4, 56 + i * 26);
            result.bounds = new Rect(-outset, 0, width + outset, height);
            return result;
        }

        static float EstimateOutset(ShaderGraphNode node, ShaderGraphPort port)
        {
            // Port-input USS measures 45px per scalar and 4px gaps. Input
            // plaques add padding and a lead to the connector.
            if (node.function?.ports.Find(p => p.name == port.name)?.hasRange == true) return 238;
            if (port.type.HasValue && ShaderGraphNodes.MatrixSize(port.type.Value) > 0) return 0;
            var stored = node.FindInput(port.name);
            var type = node.kind == ShaderGraphNodeKind.Expression ? node.valueType : stored?.type ?? port.type ?? ShaderGraphValueType.Float;
            if (node.kind == ShaderGraphNodeKind.Swizzle ||
                node.kind == ShaderGraphNodeKind.ConvertColor && node.inputColorSpace != ShaderGraphColorSpace.LinearRGB) return 225;
            if (ShaderGraphInput.IsResource(type)) return 147;
            if (type == ShaderGraphValueType.Color || type == ShaderGraphValueType.Boolean) return 82;
            int components = Math.Max(1, ShaderGraphNodes.Components(type));
            float width = 34 + components * 45 + (components - 1) * 4;
            if (port.uvDefault || port.canvasSizeDefault) width = Math.Max(width, 108);
            return width;
        }
    }
}
