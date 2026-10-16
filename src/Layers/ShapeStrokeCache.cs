using System;
using System.Collections.Generic;
using UnityEngine;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;

namespace DCFApixels.WhimTex
{
    // Cached outline geometry, not a raster mask. The shader measures its distance in
    // the original Shape metric, keeping Feather and inverse-transform sampling intact.
    internal sealed class ShapeStrokeCache : IDisposable
    {
        private Texture2D tree;
        private int key, nodeCount;
        private bool valid;
        private const float Tolerance = .025f;

        internal void Bind(Material material, ShapeLayerBehaviour shape, Vector2 half, float pixelScale)
        {
            var hash = new System.HashCode();
            hash.Add(half); hash.Add(pixelScale); hash.Add(shape.kind); hash.Add(shape.sides); hash.Add(shape.innerRadius);
            hash.Add(shape.startAngle); hash.Add(shape.sweepAngle); hash.Add(shape.arcThickness); hash.Add(shape.lineCap);
            shape.AddCornerHash(ref hash); shape.AddStrokeHash(ref hash);
            int next = hash.ToHashCode();
            if (!valid || next != key)
            {
                Paths64 polygons = BuildOutline(shape, half, pixelScale);
                Upload(polygons);
                key = next; valid = true;
            }
            material.SetTexture("_ShapeStrokeTree", tree != null ? tree : Texture2D.blackTexture);
            material.SetVector("_ShapeStrokeTreeSize", tree == null ? Vector4.one : new Vector4(tree.width, tree.height, 1f / tree.width, 1f / tree.height));
            material.SetInt("_ShapeStrokeNodes", nodeCount);
        }

        internal static Paths64 BuildOutline(ShapeLayerBehaviour shape, Vector2 half, float pixelScale)
        {
            var paths = Body(shape, half, pixelScale);
            var input = new Paths64();
            foreach (var path in paths) if (path.Count >= 3) input.Add(StrokeGeometry.ToPath(path));
            var interior = input.Count == 0 ? input : StrokeGeometry.Resolve(input, rule: FillRule.EvenOdd);
            return StrokeGeometry.Outline(paths, null, interior, StrokeStyle.From(shape, pixelScale), Tolerance);
        }

        private static List<List<Vector2>> Body(ShapeLayerBehaviour shape, Vector2 half, float pixelScale)
        {
            var result = new List<List<Vector2>>();
            // Reuse the analytic primitive-to-curve boundary, including its start point
            // and direction, so a conversion preserves the dash phase on Line and Arc.
            bool complexArc = shape.kind == ShapeLayerBehaviour.ShapeKind.Arc && ComplexArc(shape, half, pixelScale);
            if (!complexArc)
            {
                var contours = ShapeToVector.GeometryContours(shape, half, pixelScale);
                foreach (var contour in contours)
                {
                    var points = new List<Vector2>();
                    VectorGeometry.FlattenContour(contour, half * 2, Tolerance, points);
                    for (int i = 0; i < points.Count; i++) points[i] -= half;
                    result.Add(points);
                }
                return result;
            }
            var path = new List<Vector2>();
            float radius = ShapeLayerBehaviour.Limit(shape.arcThickness, 0, 8192, 8) * pixelScale * .5f;
            float sweep = ShapeLayerBehaviour.Limit(shape.sweepAngle, 0, 360, 90) * Mathf.Deg2Rad;
            if (radius <= 0 || sweep < 1e-5f) return result;
            bool full = sweep >= Mathf.PI * 2 - 1e-5f;
            Arc(path, Vector2.zero, half, ShapeLayerBehaviour.Limit(shape.startAngle, -360000, 360000, 0) * Mathf.Deg2Rad, sweep);
            if (full && path.Count > 1) path.RemoveAt(path.Count - 1);
            var bodyStyle = new StrokeStyle { strokeWidth = radius * 2, join = VectorStrokeJoin.Round, miterLimit = 4,
                cap = (VectorStrokeCap)shape.lineCap };
            var polygons = StrokeGeometry.Build(new List<List<Vector2>> { path }, new List<bool> { full }, bodyStyle, radius, Tolerance);
            foreach (var polygon in polygons)
            {
                var points = new List<Vector2>(polygon.Count);
                foreach (var p in polygon) points.Add(new Vector2((float)(p.X / StrokeGeometry.Precision), (float)(p.Y / StrokeGeometry.Precision)));
                result.Add(points);
            }
            return result;
        }

        private static bool ComplexArc(ShapeLayerBehaviour shape, Vector2 half, float pixelScale)
        {
            float radius = ShapeLayerBehaviour.Limit(shape.arcThickness, 0, 8192, 8) * pixelScale * .5f;
            float minor = Mathf.Min(half.x, half.y), major = Mathf.Max(half.x, half.y);
            if (radius >= minor * minor / major) return true;
            float start = ShapeLayerBehaviour.Limit(shape.startAngle, -360000, 360000, 0) * Mathf.Deg2Rad;
            float sweep = ShapeLayerBehaviour.Limit(shape.sweepAngle, 0, 360, 90) * Mathf.Deg2Rad;
            if (sweep <= Mathf.PI || sweep >= Mathf.PI * 2 - 1e-5f || shape.lineCap == ShapeLayerBehaviour.LineCap.Butt) return false;
            Vector2 a = new Vector2(half.x * Mathf.Cos(start), half.y * Mathf.Sin(start));
            Vector2 b = new Vector2(half.x * Mathf.Cos(start + sweep), half.y * Mathf.Sin(start + sweep));
            return Vector2.Distance(a, b) <= radius * (shape.lineCap == ShapeLayerBehaviour.LineCap.Square ? 2.8284272f : 2);
        }

        private static void Add(List<Vector2> path, Vector2 point)
        {
            if (path.Count == 0 || (path[path.Count - 1] - point).sqrMagnitude > 1e-12f) path.Add(point);
        }

        private static void Arc(List<Vector2> path, Vector2 center, Vector2 axes, float start, float sweep)
        {
            double radius = Math.Max(Math.Abs(axes.x), Math.Abs(axes.y));
            double step = 2 * Math.Acos(Math.Max(-1, 1 - Tolerance / Math.Max(1e-9, radius)));
            double required = Math.Ceiling(Math.Abs(sweep) / Math.Max(1e-9, step));
            if (required > 65536) throw VectorGeometry.Limit("Shape curve subdivision count");
            int count = Math.Max(1, (int)required);
            for (int i = 0; i <= count; i++)
            {
                double angle = start + (double)sweep * i / count;
                Add(path, center + new Vector2((float)(axes.x * Math.Cos(angle)), (float)(axes.y * Math.Sin(angle))));
            }
        }

        private struct Edge
        {
            internal Vector2 a, b;
            internal Vector2 Min => Vector2.Min(a, b);
            internal Vector2 Max => Vector2.Max(a, b);
        }

        private sealed class EdgeComparer : IComparer<Edge>
        {
            internal static readonly EdgeComparer X = new EdgeComparer(false), Y = new EdgeComparer(true);
            private readonly bool y;
            private EdgeComparer(bool y) { this.y = y; }
            public int Compare(Edge a, Edge b) => y ? (a.a.y + a.b.y).CompareTo(b.a.y + b.b.y) : (a.a.x + a.b.x).CompareTo(b.a.x + b.b.x);
        }

        private void Upload(Paths64 polygons)
        {
            var list = new List<Edge>();
            foreach (var path in polygons)
            {
                if (path.Count < 3) continue;
                var previous = path[path.Count - 1];
                foreach (var point in path)
                {
                    var a = new Vector2((float)(previous.X / StrokeGeometry.Precision), (float)(previous.Y / StrokeGeometry.Precision));
                    var b = new Vector2((float)(point.X / StrokeGeometry.Precision), (float)(point.Y / StrokeGeometry.Precision));
                    if ((a - b).sqrMagnitude > 1e-14f) list.Add(new Edge { a = a, b = b });
                    previous = point;
                }
            }
            int nodes = Math.Max(0, list.Count * 2 - 1);
            if (nodes == 0) { Dispose(); nodeCount = 0; return; }
            int texels = checked(nodes * 3), width = Math.Min(1024, SystemInfo.maxTextureSize);
            int height = (texels + width - 1) / width;
            if (height > SystemInfo.maxTextureSize) throw VectorGeometry.Limit("Shape stroke tree size");
            var data = new Color[width * height];
            var edges = list.ToArray();
            int cursor = 0;
            Build(0, edges.Length);
            void Build(int begin, int length)
            {
                int node = cursor++;
                Vector2 min = edges[begin].Min, max = edges[begin].Max;
                for (int i = begin + 1; i < begin + length; i++) { min = Vector2.Min(min, edges[i].Min); max = Vector2.Max(max, edges[i].Max); }
                data[node * 3] = new Color(min.x, min.y, max.x, max.y);
                if (length == 1)
                {
                    Edge edge = edges[begin];
                    data[node * 3 + 1] = new Color(edge.a.x, edge.a.y, edge.b.x, edge.b.y);
                }
                else
                {
                    Array.Sort(edges, begin, length, max.x - min.x >= max.y - min.y ? EdgeComparer.X : EdgeComparer.Y);
                    int split = length / 2; Build(begin, split); Build(begin + split, length - split);
                }
                // Preorder escape links allow a stackless traversal in the fragment shader.
                data[node * 3 + 2] = new Color(cursor, length == 1 ? 1 : 0, 0, 0);
            }
            Texture2D replacement = null;
            try
            {
                replacement = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                { name = "Shape Stroke Geometry", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                replacement.SetPixels(data); replacement.Apply(false, true);
                if (tree != null) UnityEngine.Object.DestroyImmediate(tree);
                tree = replacement; nodeCount = nodes;
            }
            catch { if (replacement != null) UnityEngine.Object.DestroyImmediate(replacement); throw; }
        }

        public void Dispose()
        {
            if (tree != null) UnityEngine.Object.DestroyImmediate(tree);
            tree = null; nodeCount = 0; valid = false;
        }
    }
}
