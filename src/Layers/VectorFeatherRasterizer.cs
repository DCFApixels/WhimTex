using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;

namespace DCFApixels.WhimTex
{
    internal sealed class VectorFeatherRasterizer : IDisposable
    {
        private struct Segment
        {
            internal Vector2 a, b;
            internal Vector2 Min => Vector2.Min(a, b);
            internal Vector2 Max => Vector2.Max(a, b);
        }
        private struct Node
        {
            internal Vector2 min, max, a, b;
            internal int escape, leaf;
        }
        private sealed class SegmentComparer : IComparer<Segment>
        {
            internal static readonly SegmentComparer X = new(0), Y = new(1);
            private readonly int axis;
            private SegmentComparer(int axis) => this.axis = axis;
            public int Compare(Segment a, Segment b) => (a.a[axis] + a.b[axis]).CompareTo(b.a[axis] + b.b[axis]);
        }

        private NativeArray<Node> fill, stroke;
        private readonly ProjectiveMatrix pixelToReference;
        private readonly Vector2 reference;
        private readonly float width, outside;
        private readonly bool clip;
        internal RectInt Bounds { get; }

        internal VectorFeatherRasterizer(Paths64 fills, Paths64 strokes, ProjectiveMatrix toPixels,
            Vector2 reference, int canvasWidth, int canvasHeight, float width, VectorFeatherPosition position, bool clip)
        {
            this.reference = reference; this.width = width; this.clip = clip;
            outside = position == VectorFeatherPosition.Inside ? 0f : position == VectorFeatherPosition.Outside ? 1f : .5f;
            if (!toPixels.TryInverse(out pixelToReference)) throw new InvalidOperationException("Vector Feather requires an invertible transform.");
            try
            {
                fill = Build(fills); stroke = Build(strokes);
                Bounds = GetBounds(toPixels, canvasWidth, canvasHeight);
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if (fill.IsCreated) fill.Dispose();
            if (stroke.IsCreated) stroke.Dispose();
        }

        private static NativeArray<Node> Build(Paths64 paths)
        {
            var edges = new List<Segment>();
            foreach (var path in paths)
            {
                if (path.Count < 3) continue;
                Vector2 a = Point(path[path.Count - 1]);
                foreach (var vertex in path)
                {
                    Vector2 b = Point(vertex);
                    if (a != b) edges.Add(new Segment { a = a, b = b });
                    if (edges.Count > VectorGeometry.MaxFlattenedPoints * 4) throw VectorGeometry.Limit("Feather edge count");
                    a = b;
                }
            }
            if (edges.Count == 0) return new NativeArray<Node>(0, Allocator.TempJob);
            var segments = edges.ToArray();
            var nodes = new Node[checked(segments.Length * 2 - 1)];
            int cursor = 0;
            void Add(int begin, int count)
            {
                int slot = cursor++;
                Vector2 min = segments[begin].Min, max = segments[begin].Max;
                for (int i = begin + 1; i < begin + count; i++)
                { min = Vector2.Min(min, segments[i].Min); max = Vector2.Max(max, segments[i].Max); }
                var node = new Node { min = min, max = max };
                if (count == 1) { node.a = segments[begin].a; node.b = segments[begin].b; node.leaf = 1; }
                else
                {
                    Array.Sort(segments, begin, count, max.x - min.x >= max.y - min.y ? SegmentComparer.X : SegmentComparer.Y);
                    int half = count / 2; Add(begin, half); Add(begin + half, count - half);
                }
                node.escape = cursor; nodes[slot] = node;
            }
            Add(0, segments.Length);
            return new NativeArray<Node>(nodes, Allocator.TempJob);

            static Vector2 Point(Point64 p) => new((float)(p.X / StrokeGeometry.Precision), (float)(p.Y / StrokeGeometry.Precision));
        }

        private RectInt GetBounds(ProjectiveMatrix toPixels, int canvasWidth, int canvasHeight)
        {
            if (fill.Length == 0 && stroke.Length == 0) return default;
            Vector2 min = fill.Length > 0 ? fill[0].min : stroke[0].min;
            Vector2 max = fill.Length > 0 ? fill[0].max : stroke[0].max;
            if (stroke.Length > 0) { min = Vector2.Min(min, stroke[0].min); max = Vector2.Max(max, stroke[0].max); }
            min -= Vector2.one * width * outside; max += Vector2.one * width * outside;
            if (clip) { min = Vector2.Max(Vector2.zero, min); max = Vector2.Min(reference, max); }
            if (min.x >= max.x || min.y >= max.y) return default;
            double x0 = double.PositiveInfinity, y0 = x0, x1 = double.NegativeInfinity, y1 = x1;
            foreach (var p in new[] { min, max, new Vector2(min.x, max.y), new Vector2(max.x, min.y) })
            {
                if (!toPixels.TryPoint(p, out var q)) throw VectorGeometry.Limit("Feather bounds at projective horizon");
                x0 = Math.Min(x0, q.x); y0 = Math.Min(y0, q.y); x1 = Math.Max(x1, q.x); y1 = Math.Max(y1, q.y);
            }
            int left = (int)Math.Clamp(Math.Floor(x0) - 1, 0, canvasWidth), bottom = (int)Math.Clamp(Math.Floor(y0) - 1, 0, canvasHeight);
            int right = (int)Math.Clamp(Math.Ceiling(x1) + 1, 0, canvasWidth), top = (int)Math.Clamp(Math.Ceiling(y1) + 1, 0, canvasHeight);
            return new RectInt(left, bottom, Math.Max(0, right - left), Math.Max(0, top - bottom));
        }

        internal void Render(NativeArray<Color> mask, RectInt bounds)
        {
            using var measure = WhimTexProfiler.Measure("vector.feather");
            new FeatherJob { fill = fill, stroke = stroke, output = mask, pixelToReference = pixelToReference,
                width = width, outside = outside, reference = reference, clip = clip,
                originX = bounds.x, originY = bounds.y, columns = bounds.width }.Schedule(mask.Length, 64).Complete();
        }

        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        private struct FeatherJob : IJobParallelFor
        {
            [ReadOnly] internal NativeArray<Node> fill, stroke;
            internal NativeArray<Color> output;
            internal ProjectiveMatrix pixelToReference;
            internal Vector2 reference;
            internal float width, outside;
            internal bool clip;
            internal int originX, originY, columns;

            public void Execute(int index)
            {
                float x = originX + index % columns, y = originY + index / columns;
                if (!Map(x + .5f, y + .5f, out var p) || !Map(x + 1.5f, y + .5f, out var px) || !Map(x + .5f, y + 1.5f, out var py))
                { output[index] = default; return; }
                float df = Nearest(fill, p, out var nf), ds = Nearest(stroke, p, out var ns);
                Vector2 dx = px - p, dy = py - p;
                float af = Mathf.Abs(Vector2.Dot(nf, dx)) + Mathf.Abs(Vector2.Dot(nf, dy));
                float ast = Mathf.Abs(Vector2.Dot(ns, dx)) + Mathf.Abs(Vector2.Dot(ns, dy));
                float aa = Mathf.Max(af, ast);
                Color sharp = output[index];
                if (df > width + aa && ds > width + aa && (!clip ||
                    FrameContains(x, y) && FrameContains(x + 1, y) && FrameContains(x, y + 1) && FrameContains(x + 1, y + 1))) return;
                const int samples = 4;
                float f = 0, s = 0, intersection = 0;
                for (int row = 0; row < samples; row++) for (int col = 0; col < samples; col++)
                {
                    if (!Map(x + (col + .5f) / samples, y + (row + .5f) / samples, out var q)) continue;
                    if (clip && (q.x < 0 || q.y < 0 || q.x > reference.x || q.y > reference.y)) continue;
                    float fc = Soft(fill, q), sc = Soft(stroke, q);
                    f += fc; s += sc; intersection += fc * sc;
                }
                float blend = Mathf.Clamp01(width / Mathf.Max(aa, 1e-6f));
                const float weight = 1f / (samples * samples);
                output[index] = Color.LerpUnclamped(sharp, new Color(f * weight, s * weight, intersection * weight, 0), blend);
            }

            private bool Map(float x, float y, out Vector2 point)
            {
                bool valid = pixelToReference.TryPoint(new Double2(x, y), out var result);
                point = new Vector2((float)result.x, (float)result.y);
                return valid;
            }

            private bool FrameContains(float x, float y) => Map(x, y, out var p) &&
                p.x >= 0 && p.y >= 0 && p.x <= reference.x && p.y <= reference.y;

            private float Soft(NativeArray<Node> nodes, Vector2 p)
            {
                if (nodes.Length == 0) return 0;
                float distance = Nearest(nodes, p, out _);
                if (Inside(nodes, p)) distance = -distance;
                float t = Mathf.Clamp01((distance + width * (1 - outside)) / width);
                return 1 - t * t * (3 - 2 * t);
            }

            private static float Nearest(NativeArray<Node> nodes, Vector2 p, out Vector2 normal)
            {
                float best = float.PositiveInfinity;
                normal = Vector2.zero;
                int slot = 0;
                while (slot < nodes.Length)
                {
                    var node = nodes[slot];
                    Vector2 near = new(Mathf.Clamp(p.x, node.min.x, node.max.x), Mathf.Clamp(p.y, node.min.y, node.max.y));
                    if ((near - p).sqrMagnitude > best) { slot = node.escape; continue; }
                    if (node.leaf != 0)
                    {
                        Vector2 ab = node.b - node.a;
                        float along = Mathf.Clamp01(Vector2.Dot(p - node.a, ab) / ab.sqrMagnitude);
                        Vector2 delta = p - (node.a + ab * along);
                        float squared = delta.sqrMagnitude;
                        if (squared < best)
                        { best = squared; normal = squared > 1e-12f ? delta / Mathf.Sqrt(squared) : new Vector2(-ab.y, ab.x).normalized; }
                    }
                    slot++;
                }
                return Mathf.Sqrt(best);
            }

            private static bool Inside(NativeArray<Node> nodes, Vector2 p)
            {
                int winding = 0, slot = 0;
                while (slot < nodes.Length)
                {
                    var node = nodes[slot];
                    if (p.y < node.min.y || p.y >= node.max.y || p.x > node.max.x) { slot = node.escape; continue; }
                    if (node.leaf != 0 && (node.a.y > p.y) != (node.b.y > p.y) &&
                        p.x < node.a.x + (p.y - node.a.y) * (node.b.x - node.a.x) / (node.b.y - node.a.y))
                        winding += node.b.y > node.a.y ? 1 : -1;
                    slot++;
                }
                return winding != 0;
            }
        }
    }
}
