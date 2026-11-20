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
        [NonSerialized] private List<MorphSegment> linearGuides;

        internal Vector4 Position(MorphSegment edge, bool end)
        {
            int index = end ? edge.endNode : edge.startNode;
            return index == 0 ? edge.Pair(end) : nodes[index - 1].Pair;
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

        internal void RemoveEdge(int index)
        {
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
            if (segments == null || nodes == null) throw new InvalidOperationException("Missing morph guide list.");
            foreach (var node in nodes)
                if (node == null || !VectorGeometry.Finite(node.from) || !VectorGeometry.Finite(node.to))
                    throw new InvalidOperationException("Morph guide nodes require finite UVs.");
            foreach (var edge in segments)
            {
                if (edge == null || edge.startNode < 0 || edge.endNode < 0 || edge.startNode > nodes.Count || edge.endNode > nodes.Count)
                    throw new InvalidOperationException("Invalid morph guide node reference.");
                Vector4 a = Position(edge, false), b = Position(edge, true);
                foreach (bool source in new[]{true, false})
                {
                    Vector2 x = MorphSegment.Side(a, source), y = MorphSegment.Side(b, source);
                    if (!VectorGeometry.Finite(x) || !VectorGeometry.Finite(y) || x == y)
                        throw new InvalidOperationException("Morph guides require distinct finite endpoints on each side.");
                }
                if (!VectorGeometry.Finite(edge.fromOut) || !VectorGeometry.Finite(edge.fromIn) ||
                    !VectorGeometry.Finite(edge.toOut) || !VectorGeometry.Finite(edge.toIn))
                    throw new InvalidOperationException("Invalid morph guide handles.");
                Vector2 previous = Vector2.zero;
                if (edge.marks == null) continue;
                foreach (var mark in edge.marks)
                {
                    if (!(mark.x > previous.x && mark.y > previous.y && mark.x < 1 && mark.y < 1))
                        throw new InvalidOperationException("Morph guide marks must be interior and ordered in both images.");
                    previous = mark;
                }
            }
        }

        internal IReadOnlyList<MorphSegment> RasterGuides(int width, int height)
        {
            if (segments.Count == 0) return segments;
            linearGuides ??= new();
            linearGuides.Clear();
            foreach (var edge in segments)
                edge.AppendLinear(Position(edge, false), Position(edge, true), new Vector2(width, height), linearGuides);
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
        internal Vector2 Offset(bool source, bool end) => source ? (end ? fromIn : fromOut) : (end ? toIn : toOut);
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
        [NonSerialized] private List<MorphSegment> pieces;
        [NonSerialized] private (Vector4, Vector4, Vector2, Vector2, Vector2, Vector2, bool, Vector2) geometry;
        [NonSerialized] private Vector2[] lastMarks;

        internal void AppendLinear(Vector4 a, Vector4 b, Vector2 size, List<MorphSegment> output)
        {
            int count = marks?.Count ?? 0;
            if (!bezier && count == 0 && startNode == 0 && endNode == 0) { output.Add(this); return; }
            var key = (a, b, fromOut, fromIn, toOut, toIn, bezier, size);
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
            output.AddRange(pieces);
        }

        private readonly struct Sample
        {
            internal readonly Vector2 uv;
            internal readonly float length;
            internal Sample(Vector2 uv, float length) { this.uv = uv; this.length = length; }
        }

        private void Flatten(Vector4 a, Vector4 b, bool source, float start, float end, Vector2 size, List<Sample> path)
        {
            path.Clear();
            Vector2 p = Evaluate(a,b,source,start), q = Evaluate(a,b,source,end);
            path.Add(new Sample(p, 0));
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
                if (length > 0) path.Add(new Sample(right,path[path.Count-1].length+length));
            }
        }

        // Walk the union of normalized arc-length breakpoints between consecutive paired marks.
        private static void MatchLengths(List<Sample> a, List<Sample> b, List<MorphSegment> result)
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
                result.Add(new MorphSegment {fromStart=from,fromEnd=nextA,toStart=to,toEnd=nextB});
                from=nextA; to=nextB;
                if (ta <= tb) i++;
                if (tb <= ta) j++;
            }
            static Vector2 At(List<Sample> list, int i, float length) => Vector2.Lerp(list[i-1].uv, list[i].uv,
                Mathf.InverseLerp(list[i-1].length,list[i].length,length));
        }
    }
}
