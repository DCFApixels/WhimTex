using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class MorphGuideNode
    {
        public Vector2 from, to;
        public bool smooth;
        internal Vector4 Pair => new Vector4(from.x, from.y, to.x, to.y);
    }

    public sealed partial class MorphTransition
    {
        // One-based references. A zero reference uses the individual endpoint stored on the segment.
        public List<MorphGuideNode> nodes = new();
        public List<MorphGuidePath> guides = new();
        [NonSerialized] private List<MorphSegment> linearGuides;
        [NonSerialized] private List<MorphAnchor> combinedAnchors;

        internal IReadOnlyList<MorphAnchor> RasterAnchors(bool useMarco, IReadOnlyList<MorphSegment> edges, int width, int height)
        {
            if (!useMarco || marcoAnchors.Count == 0) return anchors;
            if (anchors.Count == 0 && (edges == null || edges.Count == 0)) return marcoAnchors;
            combinedAnchors ??= new();
            combinedAnchors.Clear();
            combinedAnchors.AddRange(anchors);
            Vector2 metric = new Vector2(width, height) / Mathf.Min(width, height);
            foreach (var candidate in marcoAnchors)
            {
                bool overlaps = false;
                foreach (var point in anchors)
                {
                    float radius = Mathf.Max(.08f, point.radius);
                    if (Distance(candidate.from, point.from, point.from) <= radius ||
                        Distance(candidate.to, point.to, point.to) <= radius) { overlaps = true; break; }
                }
                for (int i = 0; !overlaps && i < (edges?.Count ?? 0); i++)
                {
                    var edge = edges[i];
                    float radius = Mathf.Max(.08f, edge.radius);
                    overlaps = Distance(candidate.from, edge.fromStart, edge.fromEnd) <= radius ||
                        Distance(candidate.to, edge.toStart, edge.toEnd) <= radius;
                }
                if (!overlaps) combinedAnchors.Add(candidate);
            }
            return combinedAnchors;

            float Distance(Vector2 p, Vector2 a, Vector2 b)
            {
                Vector2 v = (p - a) * metric, d = (b - a) * metric;
                float t = d.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(v, d) / d.sqrMagnitude) : 0;
                return (v - t * d).magnitude;
            }
        }

        internal Vector4 Position(MorphSegment edge, bool end)
        {
            int index = end ? edge.endNode : edge.startNode;
            return index == 0 ? edge.Pair(end) : nodes[index - 1].Pair;
        }

        // WhimTexDocumentSerializer's per-list limit. Count only nodes that the edit actually adds.
        internal bool CanAddGuides(int addedSegments, int addedNodes)
        {
            const int maxEntries = 1_000_000;
            return segments != null && nodes != null && addedSegments >= 0 && addedNodes >= 0 &&
                addedSegments <= maxEntries - segments.Count && addedNodes <= maxEntries - nodes.Count;
        }

        // Preflight the UI's AddRange(path) + BindNodes() as one edit. Existing inline endpoints
        // are bound by that operation too, and coincident paired endpoints consume just one node.
        internal bool CanAppendGuidePath(IReadOnlyList<MorphSegment> path)
        {
            if (path == null || path.Count == 0 || !CanAddGuides(path.Count, 0)) return false;
            var bound = new HashSet<Vector4>();
            foreach (var node in nodes)
            {
                if (node == null || !VectorGeometry.Finite(node.from) || !VectorGeometry.Finite(node.to)) return false;
                bound.Add(node.Pair);
            }
            int existing = bound.Count;
            foreach (var edge in segments)
                if (!CountEnds(edge)) return false;
            foreach (var edge in path)
                if (!CountEnds(edge)) return false;
            return CanAddGuides(path.Count, bound.Count - existing);

            bool CountEnds(MorphSegment edge)
            {
                if (!TryGuideEnds(edge, out var a, out var b)) return false;
                if (edge.startNode == 0) bound.Add(a);
                if (edge.endNode == 0) bound.Add(b);
                return true;
            }
        }

        private bool TryGuideEnds(MorphSegment edge, out Vector4 a, out Vector4 b)
        {
            a = b = default;
            bool ValidNode(int index) => index == 0 || index > 0 && index <= nodes.Count && nodes[index - 1] != null;
            if (edge == null || !ValidNode(edge.startNode) || !ValidNode(edge.endNode) || !edge.HasFiniteHandles()) return false;
            a = Position(edge, false);
            b = Position(edge, true);
            return ValidSide(MorphSegment.Side(a, true), MorphSegment.Side(b, true), true) &&
                ValidSide(MorphSegment.Side(a, false), MorphSegment.Side(b, false), false);

            bool ValidSide(Vector2 start, Vector2 end, bool source) =>
                VectorGeometry.Finite(start) && VectorGeometry.Finite(end) && start != end &&
                (!edge.bezier || VectorGeometry.Finite(start + edge.Offset(source, false)) &&
                    VectorGeometry.Finite(end + edge.Offset(source, true)));
        }

        internal bool CanContinueGuide(int edgeIndex, bool end)
        {
            if (!CanAddGuides(1, 0) || (uint)edgeIndex >= segments.Count) return false;
            var edge = segments[edgeIndex];
            if (!TryGuideEnds(edge, out var a, out var b) || edge.foreground) return false;
            Vector4 joint = end ? b : a;
            // Imported edges can mix inline ends and node references. BindNodes keys both by the paired UV.
            for (int i = 0; i < segments.Count; i++)
            {
                if (i == edgeIndex) continue;
                if (!TryGuideEnds(segments[i], out a, out b) || a.Equals(joint) || b.Equals(joint)) return false;
            }
            return true;
        }

        // Match WhimTexDocument's default canvas when no image size is supplied.
        private static readonly Vector2 DefaultGuideImageSize = new Vector2(512, 512);

        internal bool CanSplitGuide(int edgeIndex, bool from, float t) =>
            CanSplitGuide(edgeIndex, from, t, DefaultGuideImageSize);

        internal bool SplitGuide(int edgeIndex, bool from, float t) =>
            SplitGuide(edgeIndex, from, t, DefaultGuideImageSize);

        internal bool CanSplitGuide(int edgeIndex, bool from, float t, Vector2 imageSize) =>
            TrySplitGuide(edgeIndex, from, t, imageSize, false);

        internal bool SplitGuide(int edgeIndex, bool from, float t, Vector2 imageSize) =>
            TrySplitGuide(edgeIndex, from, t, imageSize, true);

        private bool TrySplitGuide(int edgeIndex, bool from, float t, Vector2 size, bool apply)
        {
            if (!CanAddGuides(1, 1) || (uint)edgeIndex >= segments.Count || !(t > 0 && t < 1) ||
                !VectorGeometry.Finite(size) || size.x <= 0 || size.y <= 0) return false;
            var edge = segments[edgeIndex];
            if (!TryGuideEnds(edge, out var a, out var b) ||
                !edge.TryMapSplit(a, b, from, t, size, out var cut)) return false;

            var left = new MorphSegment();
            var right = new MorphSegment { endNode = edge.endNode, fromEnd = edge.fromEnd, toEnd = edge.toEnd,
                bezier = edge.bezier, radius = edge.radius, boundary = edge.boundary,
                correction = edge.correction, foreground = edge.foreground };
            if (!SplitSide(true, cut.x, out var fromJoint) || !SplitSide(false, cut.y, out var toJoint)) return false;
            var joint = new MorphGuideNode { from = fromJoint, to = toJoint, smooth = edge.bezier &&
                left.fromIn != Vector2.zero && right.fromOut != Vector2.zero &&
                left.toIn != Vector2.zero && right.toOut != Vector2.zero };
            if (edge.foreground)
            {
                // ClosedContour uses paired positions. A cut at another foreground vertex would create a branch.
                foreach (var other in segments)
                    if (other != edge && other?.foreground == true && (!TryGuideEnds(other, out var p, out var q) ||
                        p.Equals(joint.Pair) || q.Equals(joint.Pair))) return false;
            }
            if (edge.marks != null)
            {
                foreach (var mark in edge.marks)
                {
                    if (mark.Equals(cut)) continue; // The shared node now carries this correspondence.
                    bool before = mark.x < cut.x;
                    var local = before ? mark / cut : (mark - cut) / (Vector2.one - cut);
                    var marks = before ? left.marks : right.marks;
                    var previous = marks.Count == 0 ? Vector2.zero : marks[marks.Count - 1];
                    // Rounding near a cut must never discard a mark or serialize duplicate parameters.
                    if (!MorphSegment.InteriorAfter(local, previous)) return false;
                    marks.Add(local);
                }
            }
            if (!apply) return true;
            nodes.Add(joint);
            edge.endNode = right.startNode = nodes.Count;
            edge.fromEnd = edge.toEnd = default;
            edge.fromOut = left.fromOut; edge.fromIn = left.fromIn;
            edge.toOut = left.toOut; edge.toIn = left.toIn;
            edge.marks = left.marks;
            segments.Insert(edgeIndex + 1, right);
            return true;

            bool SplitSide(bool source, float at, out Vector2 middle)
            {
                Vector2 start = MorphSegment.Side(a, source), end = MorphSegment.Side(b, source);
                middle = Vector2.LerpUnclamped(start, end, at);
                if (edge.bezier)
                {
                    Vector2 c1 = start + edge.Offset(source, false), c2 = end + edge.Offset(source, true);
                    Vector2 p = Vector2.LerpUnclamped(start, c1, at), q = Vector2.LerpUnclamped(c1, c2, at),
                        r = Vector2.LerpUnclamped(c2, end, at);
                    Vector2 u = Vector2.LerpUnclamped(p, q, at), v = Vector2.LerpUnclamped(q, r, at);
                    middle = Vector2.LerpUnclamped(u, v, at);
                    left.SetOffset(source, false, p - start); left.SetOffset(source, true, u - middle);
                    right.SetOffset(source, false, v - middle); right.SetOffset(source, true, r - end);
                    if (!left.HasFiniteHandles() || !right.HasFiniteHandles()) return false;
                }
                return VectorGeometry.Finite(middle) && middle != start && middle != end;
            }
        }

        internal void BindNodes()
        {
            nodes ??= new();
            var indices = new Dictionary<Vector4, int>();
            for (int i = 0; i < nodes.Count; i++) indices.TryAdd(nodes[i].Pair, i + 1);
            int Bind(Vector4 position)
            {
                if (indices.TryGetValue(position, out int index)) return index;
                nodes.Add(new MorphGuideNode { from = new Vector2(position.x, position.y), to = new Vector2(position.z, position.w) });
                indices.Add(position, nodes.Count);
                return nodes.Count;
            }
            foreach (var edge in segments)
            {
                if (edge.startNode == 0) edge.startNode = Bind(edge.Pair(false));
                if (edge.endNode == 0) edge.endNode = Bind(edge.Pair(true));
                edge.fromStart = edge.toStart = edge.fromEnd = edge.toEnd = default;
            }
        }

        internal MorphSegment OtherCubic(MorphSegment edge, bool end, out bool otherEnd)
        {
            int index = end ? edge.endNode : edge.startNode;
            otherEnd = false;
            if (index == 0 || !edge.bezier) return null;
            MorphSegment other = null;
            foreach (var candidate in segments)
            {
                if (candidate == edge || candidate.startNode != index && candidate.endNode != index) continue;
                if (other != null || !candidate.bezier) return null;
                other = candidate;
                otherEnd = candidate.endNode == index;
            }
            return other;
        }

        internal void SetTangent(MorphSegment edge, bool end, bool source, Vector2 offset)
        {
            edge.SetOffset(source, end, offset);
            int index = end ? edge.endNode : edge.startNode;
            if (index == 0 || !nodes[index - 1].smooth || offset.sqrMagnitude < 1e-12f) return;
            var other = OtherCubic(edge, end, out bool otherEnd);
            if (other != null) other.SetOffset(source, otherEnd, -offset.normalized * other.Offset(source, otherEnd).magnitude);
        }

        internal bool CanMove(MorphSegment selected, bool end, bool source, Vector2 uv)
        {
            Vector4 at = Position(selected, end);
            foreach (var edge in segments)
            {
                Vector4 a = Position(edge, false), b = Position(edge, true);
                if (at.Equals(a) && MorphSegment.Side(b, source) == uv ||
                    at.Equals(b) && MorphSegment.Side(a, source) == uv) return false;
            }
            return true;
        }

        // A paired loop must close on both images. Ignore unrelated branches when removing a marked loop.
        internal List<MorphSegment> ClosedContour(MorphSegment start)
        {
            if (start == null) return null;
            var chain = new List<MorphSegment> { start };
            Vector4 origin = Position(start, false), joint = Position(start, true);
            while (!joint.Equals(origin))
            {
                MorphSegment next = null;
                foreach (var edge in segments)
                {
                    if (edge == chain[chain.Count - 1] || start.foreground && !edge.foreground) continue;
                    if (!Position(edge, false).Equals(joint) && !Position(edge, true).Equals(joint)) continue;
                    if (next != null || chain.Contains(edge)) return null;
                    next = edge;
                }
                if (next == null) return null;
                chain.Add(next);
                var a = Position(next, false);
                joint = a.Equals(joint) ? Position(next, true) : a;
            }
            int degree = 0;
            foreach (var e in segments)
                if ((!start.foreground || e.foreground) && (Position(e, false).Equals(origin) || Position(e, true).Equals(origin))) degree++;
            return degree == 2 && chain.Count >= 2 ? chain : null;
        }

        internal void RemoveEdge(int index)
        {
            if (segments[index].foreground && ClosedContour(segments[index]) is { } contour)
                foreach (var edge in contour) edge.foreground = false;
            segments.RemoveAt(index);
            for (int i = nodes.Count; i > 0; i--)
            {
                if (segments.Exists(e => e.startNode == i || e.endNode == i)) continue;
                nodes.RemoveAt(i - 1);
                foreach (var edge in segments)
                {
                    if (edge.startNode > i) edge.startNode--;
                    if (edge.endNode > i) edge.endNode--;
                }
            }
        }

        internal void ValidateGuides()
        {
            if (segments == null || nodes == null || guides == null) throw new InvalidOperationException("Missing morph guide list.");
            foreach (var guide in guides)
            {
                if (guide == null) throw new InvalidOperationException("Missing morph guide path.");
                guide.Validate();
            }
            foreach (var node in nodes)
                if (node == null || !VectorGeometry.Finite(node.from) || !VectorGeometry.Finite(node.to))
                    throw new InvalidOperationException("Morph guide nodes require finite UVs.");
            foreach (var edge in segments)
            {
                if (edge == null || edge.startNode < 0 || edge.endNode < 0 || edge.startNode > nodes.Count || edge.endNode > nodes.Count)
                    throw new InvalidOperationException("Invalid morph guide node reference.");
                edge.correction.Validate();
                if (!ValidRadius(edge.radius))
                    throw new InvalidOperationException("Morph segment radius must be between 0 and 1.");
                Vector4 a = Position(edge, false), b = Position(edge, true);
                foreach (bool source in new[]{true, false})
                {
                    Vector2 x = MorphSegment.Side(a, source), y = MorphSegment.Side(b, source);
                    if (!VectorGeometry.Finite(x) || !VectorGeometry.Finite(y) || x == y)
                        throw new InvalidOperationException("Morph guides require distinct finite endpoints on each side.");
                }
                if (!edge.HasFiniteHandles())
                    throw new InvalidOperationException("Invalid morph guide handles.");
                Vector2 previous = Vector2.zero;
                if (edge.marks == null) continue;
                foreach (var mark in edge.marks)
                {
                    if (!MorphSegment.InteriorAfter(mark, previous))
                        throw new InvalidOperationException("Morph guide marks must be interior and ordered in both images.");
                    previous = mark;
                }
            }
            HashSet<MorphSegment> validated = null;
            foreach (var edge in segments)
            {
                if (!edge.foreground || validated?.Contains(edge) == true) continue;
                var contour = ClosedContour(edge);
                if (contour == null) throw new InvalidOperationException("A foreground contour must close on both images.");
                (validated ??= new HashSet<MorphSegment>()).UnionWith(contour);
            }
        }

        internal IReadOnlyList<MorphSegment> RasterGuides(int width, int height)
        {
            if (segments.Count == 0 && guides.Count == 0) return segments;
            linearGuides ??= new();
            linearGuides.Clear();
            var size = new Vector2(width, height);
            foreach (var edge in segments)
                edge.AppendLinear(Position(edge, false), Position(edge, true), size, linearGuides);
            foreach (var guide in guides) guide.AppendLinear(size, linearGuides);
            return linearGuides;
        }
    }

    public sealed partial class MorphSegment
    {
        public int startNode, endNode;
        public bool bezier;
        // Cubic controls are offsets from their own endpoints, independently on A and B.
        public Vector2 fromOut, fromIn, toOut, toIn;
        public List<Vector2> marks = new();

        internal static Vector2 Side(Vector4 pair, bool source) => source ? new Vector2(pair.x, pair.y) : new Vector2(pair.z, pair.w);
        // Symmetric arc measure for a chord expressed in pixel coordinates on both keys.
        internal static float PairedLength(Vector4 axis) => .5f * (Side(axis, true).magnitude + Side(axis, false).magnitude);
        internal Vector2 Offset(bool source, bool end) => source ? (end ? fromIn : fromOut) : (end ? toIn : toOut);
        internal bool HasFiniteHandles() => VectorGeometry.Finite(fromOut) && VectorGeometry.Finite(fromIn) &&
            VectorGeometry.Finite(toOut) && VectorGeometry.Finite(toIn);

        internal static bool InteriorAfter(Vector2 mark, Vector2 previous) =>
            mark.x > previous.x && mark.y > previous.y && mark.x < 1 && mark.y < 1;

        internal bool TryMapSplit(Vector4 a, Vector4 b, bool from, float t, Vector2 size, out Vector2 cut)
        {
            cut = default;
            Vector2 previous = Vector2.zero, lo = Vector2.zero, hi = Vector2.one;
            if (marks != null)
                foreach (var mark in marks)
                {
                    if (!InteriorAfter(mark, previous)) return false;
                    previous = mark;
                    float parameter = from ? mark.x : mark.y;
                    if (parameter == t) cut = mark;
                    else if (parameter < t) lo = mark;
                    else if (hi.Equals(Vector2.one)) hi = mark;
                }
            if (cut.x > 0) return true;
            int side = from ? 0 : 1, other = 1 - side;
            float fraction = (t - lo[side]) / (hi[side] - lo[side]);
            float mapped = Mathf.LerpUnclamped(lo[other], hi[other], fraction);
            if (bezier)
            {
                // Walk the same two pixel-space polylines used by AppendLinear between these paired marks.
                var path = new List<Sample>();
                Flatten(a, b, from, lo[side], hi[side], size, path);
                if (!Usable(path)) return false;
                int i = 1;
                while (i < path.Count - 1 && path[i].parameter < t) i++;
                var p = path[i - 1]; var q = path[i];
                fraction = Mathf.Lerp(p.length, q.length, Mathf.InverseLerp(p.parameter, q.parameter, t)) /
                    path[path.Count - 1].length;
                Flatten(a, b, !from, lo[other], hi[other], size, path);
                if (!Usable(path)) return false;
                float length = fraction * path[path.Count - 1].length;
                i = 1;
                while (i < path.Count - 1 && path[i].length < length) i++;
                p = path[i - 1]; q = path[i];
                mapped = Mathf.Lerp(p.parameter, q.parameter, Mathf.InverseLerp(p.length, q.length, length));
            }
            cut = from ? new Vector2(t, mapped) : new Vector2(mapped, t);
            return mapped > lo[other] && mapped < hi[other];

            static bool Usable(List<Sample> path) => path.Count >= 2 && float.IsFinite(path[path.Count - 1].length);
        }

        internal void SetOffset(bool source, bool end, Vector2 uv)
        {
            if (source && end) fromIn = uv;
            else if (source) fromOut = uv;
            else if (end) toIn = uv;
            else toOut = uv;
        }

        internal Vector2 Evaluate(Vector4 a, Vector4 b, bool source, float t)
        {
            Vector2 start = Side(a, source), end = Side(b, source);
            if (!bezier) return Vector2.LerpUnclamped(start, end, t);
            float u = 1 - t;
            return u*u*u*start + 3*u*u*t*(start + Offset(source, false)) +
                3*u*t*t*(end + Offset(source, true)) + t*t*t*end;
        }

        internal void EnableBezier(Vector4 a, Vector4 b)
        {
            fromOut = (Side(b, true) - Side(a, true)) / 3;
            toOut = (Side(b, false) - Side(a, false)) / 3;
            fromIn = -fromOut;
            toIn = -toOut;
            bezier = true;
        }

        // On the first compiled chord only. The GPU evaluates one nearest correspondence per authored edge.
        [NonSerialized] internal int chordCount;
        [NonSerialized] internal bool pathChord;
        [NonSerialized] private List<MorphSegment> pieces;
        [NonSerialized] private (Vector4, Vector4, Vector2, Vector2, Vector2, Vector2, bool, Vector2, float, bool) geometry;
        [NonSerialized] private Vector2[] lastMarks;

        internal void AppendLinear(Vector4 a, Vector4 b, Vector2 size, List<MorphSegment> output)
        {
            int count = marks?.Count ?? 0;
            if (!bezier && count == 0 && startNode == 0 && endNode == 0) { output.Add(this); return; }
            var key = (a, b, fromOut, fromIn, toOut, toIn, bezier, size, radius, boundary);
            bool reuse = pieces != null && key.Equals(geometry) && lastMarks?.Length == count;
            for (int i = 0; reuse && i < count; i++) reuse = lastMarks[i].Equals(marks[i]);
            if (!reuse)
            {
                pieces ??= new();
                pieces.Clear();
                var first = new List<Sample>();
                var second = new List<Sample>();
                var previous = Vector2.zero;
                for (int i = 0; i <= count; i++)
                {
                    var next = i < count ? marks[i] : Vector2.one;
                    Flatten(a, b, true, previous.x, next.x, size, first);
                    Flatten(a, b, false, previous.y, next.y, size, second);
                    MatchLengths(first, second, pieces);
                    previous = next;
                }
                if (pieces.Count > 0) pieces[0].chordCount = pieces.Count;
                lastMarks = count == 0 ? Array.Empty<Vector2>() : marks.ToArray();
                geometry = key;
            }
            foreach (var piece in pieces)
            {
                piece.foreground = foreground;
                piece.correction = correction;
                output.Add(piece);
            }
        }

        private readonly struct Sample
        {
            internal readonly Vector2 uv;
            internal readonly float length, parameter;
            internal Sample(Vector2 uv, float length, float parameter)
            { this.uv = uv; this.length = length; this.parameter = parameter; }
        }

        private void Flatten(Vector4 a, Vector4 b, bool source, float start, float end, Vector2 size, List<Sample> path)
        {
            path.Clear();
            Vector2 p = Evaluate(a,b,source,start), q = Evaluate(a,b,source,end);
            path.Add(new Sample(p, 0, start));
            Add(start, end, p, q, 0);
            void Add(float lo, float hi, Vector2 left, Vector2 right, int depth)
            {
                // Test both thirds: a cubic inflection can lie exactly on the chord at its midpoint.
                if (bezier && depth < 10)
                {
                    bool Deviates(float u) => Vector2.Scale(size, Evaluate(a,b,source,Mathf.Lerp(lo, hi, u)) -
                        Vector2.Lerp(left,right,u)).sqrMagnitude > .0625f;
                    if (Deviates(1f/3) || Deviates(2f/3))
                    {
                        float mid = .5f*(lo+hi);
                        Vector2 split = Evaluate(a,b,source,mid);
                        Add(lo,mid,left,split,depth+1);
                        Add(mid,hi,split,right,depth+1);
                        return;
                    }
                }
                float length = Vector2.Scale(size, right-left).magnitude;
                if (length > 0) path.Add(new Sample(right,path[path.Count-1].length+length,hi));
            }
        }

        // Walk the union of normalized arc-length breakpoints between consecutive paired marks.
        private void MatchLengths(List<Sample> a, List<Sample> b, List<MorphSegment> result)
        {
            if (a.Count < 2 || b.Count < 2) return;
            int i = 1, j = 1;
            float totalA = a[a.Count-1].length, totalB = b[b.Count-1].length;
            Vector2 from = a[0].uv, to = b[0].uv;
            while (i < a.Count && j < b.Count)
            {
                float ta = a[i].length / totalA, tb = b[j].length / totalB, t = Mathf.Min(ta,tb);
                Vector2 nextA = ta <= tb ? a[i].uv : At(a,i,t*totalA);
                Vector2 nextB = tb <= ta ? b[j].uv : At(b,j,t*totalB);
                result.Add(new MorphSegment
                {
                    fromStart = from, fromEnd = nextA, toStart = to, toEnd = nextB,
                    radius = this.radius, boundary = this.boundary
                });
                from=nextA; to=nextB;
                if (ta <= tb) i++;
                if (tb <= ta) j++;
            }
            static Vector2 At(List<Sample> list, int i, float length) => Vector2.Lerp(list[i-1].uv, list[i].uv,
                Mathf.InverseLerp(list[i-1].length,list[i].length,length));
        }
    }
}
