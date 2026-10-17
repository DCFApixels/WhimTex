using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>Rebuilds editable closed contours from the retained source mask.</summary>
    public static partial class VectorBrushGeometry
    {
        public const int MaxTracePoints = 131072, MaxContours = 512, MaxOutputPoints = 8192;
        public const int MaxFieldDimension = 2048, MaxFieldPixels = 4194304;
        const int MaxDistanceWork = 64000000, MaxFitWork = 32000000, MaxTopologyWork = 16000000;

        /// <summary>
        /// Tolerance/expansion are reference pixels; points are normalized layer coordinates.
        /// Positive expansion grows the mask, including beyond its original frame. Style is
        /// initialized to solid white; callers replacing an existing shape must retain its style.
        /// An empty result is valid after contraction and retains the draft's shape ID.
        /// </summary>
        public static VectorShape Rebuild(VectorBrushDraft draft, Vector2 referenceSize, CancellationToken cancellation = default)
        {
            using var diagnostic = WhimTexHangDiagnostics.Enter("vector.mask.rebuild");
            cancellation.ThrowIfCancellationRequested();
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            draft.Validate();
            referenceSize = Vector2.Scale(referenceSize, draft.frame.size);
            if (!VectorBrushDraft.Finite(referenceSize.x) || !VectorBrushDraft.Finite(referenceSize.y)
                || referenceSize.x <= 0 || referenceSize.y <= 0 || referenceSize.x > 1000000 || referenceSize.y > 1000000)
                throw new ArgumentOutOfRangeException(nameof(referenceSize), "Vector brush reference size must be finite, positive, and at most one million pixels per axis.");
            var shape = new VectorShape { id = draft.shapeId, name = "Brush", fillRule = VectorFillRule.EvenOdd };
            int minX = draft.width, minY = draft.height, maxX = -1, maxY = -1;
            for (int y = 0; y < draft.height; y++) for (int x = 0; x < draft.width; x++)
            {
                if (x == 0) cancellation.ThrowIfCancellationRequested();
                if (draft.coverage[y * draft.width + x] < 128) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            if (maxX < minX) return shape;
            Vector2 step = new Vector2(referenceSize.x / draft.width, referenceSize.y / draft.height);
            Grid source = CoverageGrid(draft, minX - 1, minY - 1, maxX + 1, maxY + 1, step, cancellation);
            Trace original = TraceGrid(source, cancellation);
            List<List<Vector2>> loops = original.loops;
            if (draft.expansion != 0 && original.segments.Count > 0)
            {
                int paddingX = ExpansionPadding(draft.expansion, step.x);
                int paddingY = ExpansionPadding(draft.expansion, step.y);
                Grid offset = NewGrid(minX - paddingX, minY - paddingY, maxX + paddingX, maxY + paddingY, step);
                var distance = new SegmentTree(original.segments, cancellation);
                var work = new WorkBudget(MaxDistanceWork, cancellation);
                double band = Math.Abs(draft.expansion) + 2 * Math.Max(step.x, step.y);
                for (int y = 0; y < offset.height; y++) for (int x = 0; x < offset.width; x++)
                {
                    Vector2 point = offset.Position(x, y);
                    // Distances outside this band cannot participate in a zero crossing.
                    float magnitude = (float)Math.Sqrt(distance.DistanceSquared(point, band * band, ref work));
                    bool inside = CoverageAt(draft, x + offset.x, y + offset.y) >= 128;
                    float value = (inside ? magnitude : -magnitude) + draft.expansion;
                    // A deterministic exterior tie avoids vertices shared by four cells.
                    offset.values[y * offset.width + x] = value == 0 ? -1e-6f * Math.Min(step.x, step.y) : value;
                }
                loops = TraceGrid(offset, cancellation).loops;
            }
            if (loops.Count == 0) return shape;
            shape.contours = FitContours(loops, draft.tolerance, step, referenceSize, draft.shapeId, cancellation);
            foreach (var contour in shape.contours) foreach (var point in contour.points)
            {
                point.position = draft.frame.min + Vector2.Scale(point.position, draft.frame.size);
                point.inTangent = Vector2.Scale(point.inTangent, draft.frame.size);
                point.outTangent = Vector2.Scale(point.outTangent, draft.frame.size);
            }
            return shape;
        }

        static int ExpansionPadding(float expansion, float step)
        {
            double pixels = Math.Ceiling(Math.Max(0, expansion) / (double)step) + 2;
            if (pixels > MaxFieldDimension) throw Complexity("Expanded brush field is too large for this reference size.");
            return (int)pixels;
        }

        sealed class Grid
        {
            public int x, y, width, height;
            public Vector2 step;
            public float[] values;
            public Vector2 Position(int column, int row) => new Vector2((x + column + .5f) * step.x, (y + row + .5f) * step.y);
        }

        static Grid NewGrid(int x, int y, int right, int top, Vector2 step)
        {
            long width = (long)right - x + 1, height = (long)top - y + 1;
            if (width < 2 || height < 2 || width > MaxFieldDimension || height > MaxFieldDimension || width * height > MaxFieldPixels)
                throw Complexity("Vector brush field exceeds 2048 by 2048 samples.");
            return new Grid { x = x, y = y, width = (int)width, height = (int)height,
                step = step, values = new float[(int)(width * height)] };
        }

        static byte CoverageAt(VectorBrushDraft draft, int x, int y) => x < 0 || y < 0 || x >= draft.width || y >= draft.height
            ? (byte)0 : draft.coverage[y * draft.width + x];

        static Grid CoverageGrid(VectorBrushDraft draft, int x, int y, int right, int top, Vector2 step, CancellationToken cancellation)
        {
            using var diagnostic = WhimTexHangDiagnostics.Enter("vector.mask.coverage_grid");
            Grid grid = NewGrid(x, y, right, top, step);
            for (int row = 0; row < grid.height; row++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (int col = 0; col < grid.width; col++)
                    grid.values[row * grid.width + col] = CoverageAt(draft, col + x, row + y) - 127.5f;
            }
            return grid;
        }

        struct Vertex
        {
            public Vector2 point;
            public int first, second;
        }

        struct Segment
        {
            public Vector2 a, b;
            public Segment(Vector2 a, Vector2 b) { this.a = a; this.b = b; }
        }

        sealed class Trace
        {
            public readonly List<List<Vector2>> loops = new List<List<Vector2>>();
            public readonly List<Segment> segments = new List<Segment>();
        }

        // Interpolated marching squares. Shared grid-edge IDs weld exactly, without
        // rounding positions. The bilinear determinant resolves diagonal ambiguity.
        static Trace TraceGrid(Grid grid, CancellationToken cancellation)
        {
            using var diagnostic = WhimTexHangDiagnostics.Enter("vector.mask.trace_grid");
            var result = new Trace();
            var vertices = new List<Vertex>();
            var keys = new Dictionary<long, int>();
            var crossings = new int[4];
            for (int y = 0; y + 1 < grid.height; y++) for (int x = 0; x + 1 < grid.width; x++)
            {
                if (x == 0) cancellation.ThrowIfCancellationRequested();
                int index = y * grid.width + x;
                float a = grid.values[index], b = grid.values[index + 1];
                float c = grid.values[index + grid.width + 1], d = grid.values[index + grid.width];
                int count = 0;
                if ((a > 0) != (b > 0)) crossings[count++] = VertexAt(x, y, false, a / (a - b));
                if ((b > 0) != (c > 0)) crossings[count++] = VertexAt(x + 1, y, true, b / (b - c));
                if ((d > 0) != (c > 0)) crossings[count++] = VertexAt(x, y + 1, false, d / (d - c));
                if ((a > 0) != (d > 0)) crossings[count++] = VertexAt(x, y, true, a / (a - d));
                if (count == 2) Connect(crossings[0], crossings[1]);
                else if (count == 4)
                {
                    bool adjacent = (double)a * c - (double)b * d >= 0;
                    Connect(crossings[0], crossings[adjacent ? 1 : 3]);
                    Connect(crossings[adjacent ? 2 : 1], crossings[adjacent ? 3 : 2]);
                }
            }
            var visited = new bool[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                if (visited[i]) continue;
                if (result.loops.Count >= MaxContours) throw Complexity("Vector brush exceeds 512 contours.");
                var loop = new List<Vector2>();
                int current = i, previous = -1;
                do
                {
                    if ((loop.Count & 1023) == 0) cancellation.ThrowIfCancellationRequested();
                    if (current < 0 || visited[current]) throw Complexity("Vector brush contour is not a closed manifold.");
                    Vertex vertex = vertices[current];
                    if (vertex.first < 0 || vertex.second < 0) throw Complexity("Vector brush contour reached the field boundary.");
                    visited[current] = true;
                    loop.Add(vertex.point);
                    int next = vertex.first == previous ? vertex.second : vertex.first;
                    previous = current; current = next;
                } while (current != i);
                if (loop.Count < 3) throw Complexity("Vector brush contains a degenerate contour.");
                result.loops.Add(loop);
            }
            return result;

            int VertexAt(int x, int y, bool vertical, float t)
            {
                long key = ((long)y * grid.width + x) * 2 + (vertical ? 1 : 0);
                if (keys.TryGetValue(key, out int existing)) return existing;
                if (vertices.Count >= MaxTracePoints) throw Complexity("Vector brush exceeds 131072 traced boundary samples.");
                Vector2 point = grid.Position(x, y);
                if (vertical) point.y += grid.step.y * t; else point.x += grid.step.x * t;
                int next = vertices.Count;
                vertices.Add(new Vertex { point = point, first = -1, second = -1 });
                keys.Add(key, next);
                return next;
            }

            void Connect(int first, int second)
            {
                Link(first, second); Link(second, first);
                result.segments.Add(new Segment(vertices[first].point, vertices[second].point));
            }

            void Link(int from, int to)
            {
                Vertex value = vertices[from];
                if (value.first < 0) value.first = to;
                else if (value.second < 0) value.second = to;
                else throw Complexity("Vector brush boundary has more than two neighbors.");
                vertices[from] = value;
            }
        }

        struct TopologyEdge
        {
            public Segment line;
            public int loop, point, length;
            public float minX, maxX, minY, maxY;
        }

        sealed class TopologyTree
        {
            struct Node
            {
                public float minX, maxX, minY, maxY;
                public int start, count, left, right;
            }
            sealed class CenterComparer : IComparer<TopologyEdge>
            {
                readonly bool horizontal;
                public CenterComparer(bool horizontal) { this.horizontal = horizontal; }
                public int Compare(TopologyEdge a, TopologyEdge b) => horizontal
                    ? ((double)a.minX + a.maxX).CompareTo((double)b.minX + b.maxX)
                    : ((double)a.minY + a.maxY).CompareTo((double)b.minY + b.maxY);
            }
            static readonly CenterComparer SortX = new CenterComparer(true), SortY = new CenterComparer(false);
            readonly TopologyEdge[] edges;
            readonly List<Node> nodes = new List<Node>();
            readonly CancellationToken cancellation;

            public TopologyTree(List<TopologyEdge> edges, CancellationToken cancellation)
            {
                this.edges = edges.ToArray(); this.cancellation = cancellation;
                Build(0, edges.Count);
            }

            int Build(int start, int count)
            {
                cancellation.ThrowIfCancellationRequested();
                TopologyEdge first = edges[start];
                var node = new Node { start = start, count = count, left = -1, right = -1,
                    minX = first.minX, maxX = first.maxX, minY = first.minY, maxY = first.maxY };
                for (int i = start + 1; i < start + count; i++)
                {
                    var edge = edges[i];
                    node.minX = Math.Min(node.minX, edge.minX); node.maxX = Math.Max(node.maxX, edge.maxX);
                    node.minY = Math.Min(node.minY, edge.minY); node.maxY = Math.Max(node.maxY, edge.maxY);
                }
                int index = nodes.Count; nodes.Add(default);
                if (count > 8)
                {
                    Array.Sort(edges, start, count, node.maxX - node.minX >= node.maxY - node.minY ? SortX : SortY);
                    int half = count / 2;
                    node.left = Build(start, half); node.right = Build(start + half, count - half);
                }
                nodes[index] = node;
                return index;
            }

            public bool HasIntersection(ref WorkBudget work)
            {
                for (int i = 0; i < edges.Length; i++)
                    if (Query(0, i, edges[i], ref work)) return true;
                return false;
            }

            bool Query(int index, int edgeIndex, TopologyEdge edge, ref WorkBudget work)
            {
                Spend(ref work, "Vector brush topology work limit exceeded.");
                var node = nodes[index];
                if (node.start + node.count <= edgeIndex + 1 || node.minX > edge.maxX || node.maxX < edge.minX ||
                    node.minY > edge.maxY || node.maxY < edge.minY) return false;
                if (node.left >= 0)
                    return Query(node.left, edgeIndex, edge, ref work) || Query(node.right, edgeIndex, edge, ref work);
                for (int j = Math.Max(node.start, edgeIndex + 1); j < node.start + node.count; j++)
                {
                    Spend(ref work, "Vector brush topology work limit exceeded.");
                    var other = edges[j];
                    if (other.minX > edge.maxX || other.maxX < edge.minX || other.minY > edge.maxY || other.maxY < edge.minY) continue;
                    int gap = Math.Abs(edge.point - other.point);
                    if (edge.loop == other.loop && (gap == 1 || gap == edge.length - 1)) continue;
                    if (Intersects(edge.line, other.line)) return true;
                }
                return false;
            }
        }

        static bool TopologyMatches(List<List<Vector2>> source, List<List<Vector2>> candidate,
            LoopBox[] sourceBounds, sbyte[] sourceNesting, ref WorkBudget work)
        {
            var edges = new List<TopologyEdge>();
            var bounds = new LoopBox[candidate.Count];
            for (int c = 0; c < candidate.Count; c++)
            {
                work.cancellation.ThrowIfCancellationRequested();
                List<Vector2> loop = candidate[c];
                if (Area(source[c]) * Area(loop) <= 0) return false;
                bounds[c] = LoopBounds(loop);
                for (int p = 0; p < loop.Count; p++)
                {
                    Vector2 a = loop[p], b = loop[(p + 1) % loop.Count];
                    if (Squared(a, b) == 0) return false;
                    edges.Add(new TopologyEdge { line = new Segment(a, b), loop = c, point = p, length = loop.Count,
                        minX = Math.Min(a.x, b.x), maxX = Math.Max(a.x, b.x), minY = Math.Min(a.y, b.y), maxY = Math.Max(a.y, b.y) });
                }
            }
            if (edges.Count > 0 && new TopologyTree(edges, work.cancellation).HasIntersection(ref work)) return false;
            for (int i = 0; i < candidate.Count; i++) for (int j = 0; j < candidate.Count; j++)
            {
                if (i == j) continue;
                Spend(ref work, "Vector brush nesting work limit exceeded.");
                int slot = i * candidate.Count + j;
                if (sourceNesting[slot] == 0)
                    sourceNesting[slot] = (sbyte)(InBounds(sourceBounds[j], source[i][0]) && Contains(source[j], source[i][0], ref work) ? 1 : -1);
                bool contained = InBounds(bounds[j], candidate[i][0]) && Contains(candidate[j], candidate[i][0], ref work);
                if ((sourceNesting[slot] > 0) != contained) return false;
            }
            return true;
        }

        struct LoopBox { public Vector2 min, max; }

        static LoopBox LoopBounds(List<Vector2> loop)
        {
            Vector2 min = loop[0], max = min;
            for (int i = 1; i < loop.Count; i++) { min = Vector2.Min(min, loop[i]); max = Vector2.Max(max, loop[i]); }
            return new LoopBox { min = min, max = max };
        }

        static bool InBounds(LoopBox bounds, Vector2 p) => p.x >= bounds.min.x && p.x <= bounds.max.x &&
            p.y >= bounds.min.y && p.y <= bounds.max.y;

        static bool Contains(List<Vector2> polygon, Vector2 p, ref WorkBudget work)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Spend(ref work, "Vector brush nesting work limit exceeded.");
                Vector2 a = polygon[j], b = polygon[i];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (double)(b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        static double Cross(Vector2 a, Vector2 b, Vector2 p) => ((double)b.x - a.x) * ((double)p.y - a.y) - ((double)b.y - a.y) * ((double)p.x - a.x);
        static bool Intersects(Segment a, Segment b)
        {
            double p = Cross(a.a, a.b, b.a), q = Cross(a.a, a.b, b.b);
            double r = Cross(b.a, b.b, a.a), s = Cross(b.a, b.b, a.b);
            return ((p <= 0 && q >= 0) || (q <= 0 && p >= 0)) && ((r <= 0 && s >= 0) || (s <= 0 && r >= 0));
        }

        static double Area(List<Vector2> loop)
        {
            double area = 0;
            Vector2 origin = loop[0];
            for (int i = 1; i + 1 < loop.Count; i++) area += Cross(origin, loop[i], loop[i + 1]);
            return area;
        }

        static double Squared(Vector2 a, Vector2 b) { double x = (double)a.x - b.x, y = (double)a.y - b.y; return x * x + y * y; }
        static double SegmentDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
        {
            double dx = (double)b.x - a.x, dy = (double)b.y - a.y;
            double px = (double)p.x - a.x, py = (double)p.y - a.y;
            double denominator = dx * dx + dy * dy;
            double t = denominator == 0 ? 0 : Math.Max(0, Math.Min(1, (px * dx + py * dy) / denominator));
            px -= t * dx; py -= t * dy;
            return px * px + py * py;
        }

        sealed class SegmentTree
        {
            struct Node
            {
                public Vector2 min, max;
                public int start, count, left, right;
            }
            sealed class CenterComparer : IComparer<Segment>
            {
                readonly bool horizontal;
                public CenterComparer(bool horizontal) { this.horizontal = horizontal; }
                public int Compare(Segment a, Segment b) => horizontal
                    ? ((double)a.a.x + a.b.x).CompareTo((double)b.a.x + b.b.x)
                    : ((double)a.a.y + a.b.y).CompareTo((double)b.a.y + b.b.y);
            }
            static readonly CenterComparer SortX = new CenterComparer(true), SortY = new CenterComparer(false);
            readonly Segment[] segments;
            readonly List<Node> nodes = new List<Node>();
            readonly CancellationToken cancellation;

            public SegmentTree(List<Segment> segments, CancellationToken cancellation)
            { this.segments = segments.ToArray(); this.cancellation = cancellation; Build(0, segments.Count); }

            int Build(int start, int count)
            {
                cancellation.ThrowIfCancellationRequested();
                Vector2 min = segments[start].a, max = min;
                for (int i = start; i < start + count; i++)
                {
                    min = Vector2.Min(min, Vector2.Min(segments[i].a, segments[i].b));
                    max = Vector2.Max(max, Vector2.Max(segments[i].a, segments[i].b));
                }
                int index = nodes.Count;
                nodes.Add(default);
                var node = new Node { min = min, max = max, start = start, count = count, left = -1, right = -1 };
                if (count > 8)
                {
                    Array.Sort(segments, start, count, max.x - min.x >= max.y - min.y ? SortX : SortY);
                    int half = count / 2;
                    node.left = Build(start, half); node.right = Build(start + half, count - half);
                    node.count = 0;
                }
                nodes[index] = node;
                return index;
            }

            public double DistanceSquared(Vector2 point, double maximumSquared, ref WorkBudget work)
            {
                double best = maximumSquared;
                Query(0, point, ref best, ref work);
                return best;
            }

            void Query(int index, Vector2 point, ref double best, ref WorkBudget work)
            {
                Spend(ref work, "Vector brush signed-distance work limit exceeded. Reduce mask resolution or expansion.");
                Node node = nodes[index];
                if (BoxDistance(node, point) > best) return;
                if (node.count > 0)
                {
                    for (int i = node.start; i < node.start + node.count; i++)
                    {
                        Spend(ref work, "Vector brush signed-distance work limit exceeded. Reduce mask resolution or expansion.");
                        best = Math.Min(best, SegmentDistanceSquared(point, segments[i].a, segments[i].b));
                    }
                    return;
                }
                double left = BoxDistance(nodes[node.left], point), right = BoxDistance(nodes[node.right], point);
                if (left <= right)
                {
                    if (left <= best) Query(node.left, point, ref best, ref work);
                    if (right <= best) Query(node.right, point, ref best, ref work);
                }
                else
                {
                    if (right <= best) Query(node.right, point, ref best, ref work);
                    if (left <= best) Query(node.left, point, ref best, ref work);
                }
            }

            static double BoxDistance(Node node, Vector2 point)
            {
                double x = Math.Max(0, Math.Max((double)node.min.x - point.x, (double)point.x - node.max.x));
                double y = Math.Max(0, Math.Max((double)node.min.y - point.y, (double)point.y - node.max.y));
                return x * x + y * y;
            }
        }

        struct WorkBudget
        {
            public int remaining;
            public readonly CancellationToken cancellation;
            public WorkBudget(int remaining, CancellationToken cancellation)
            { this.remaining = remaining; this.cancellation = cancellation; }
        }

        static void Spend(ref WorkBudget work, string message)
        {
            if ((work.remaining & 1023) == 0) work.cancellation.ThrowIfCancellationRequested();
            if (--work.remaining < 0) throw Complexity(message);
        }
        static InvalidOperationException Complexity(string message) => new InvalidOperationException(message);
    }
}
