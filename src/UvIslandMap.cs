using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    // Read-only UV topology. Position + UV identifies split vertices, not UV alone.
    internal sealed class UvIslandMap
    {
        internal struct Triangle
        {
            internal Vector2 a, b, c;
            internal int island;
        }
        internal sealed class Island
        {
            internal readonly List<int> triangles = new List<int>();
            internal readonly List<Vector4> edges = new List<Vector4>();
            internal Vector2 marker;
        }
        private readonly struct VertexKey : IEquatable<VertexKey>
        {
            private readonly Vector3 position;
            private readonly Vector2 uv;
            internal VertexKey(Vector3 position, Vector2 uv) { this.position = position; this.uv = uv; }
            public bool Equals(VertexKey other) => position.Equals(other.position) && uv.Equals(other.uv);
            public override bool Equals(object obj) => obj is VertexKey other && Equals(other);
            public override int GetHashCode() => unchecked(position.GetHashCode() * 397 ^ uv.GetHashCode());
        }
        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            private readonly int a, b;
            internal EdgeKey(int a, int b) { this.a = Math.Min(a, b); this.b = Math.Max(a, b); }
            public bool Equals(EdgeKey other) => a == other.a && b == other.b;
            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => unchecked(a * 397 ^ b);
        }
        private sealed class Edge
        {
            internal int triangle, other = -1, count = 1;
            internal Vector2 a, b, opposite, otherOpposite;
            internal List<int> extra;
            internal bool shared;
        }
        private const int GridSide = 32;
        internal readonly List<Triangle> triangles = new List<Triangle>();
        internal readonly List<Island> islands = new List<Island>();
        private readonly List<int>[] grid = new List<int>[GridSide * GridSide];
        private readonly List<int> largeTriangles = new List<int>();
        internal int SkippedTriangles { get; private set; }

        internal static UvIslandMap Build(Mesh mesh, int channel, int submesh)
        {
            if (mesh == null) throw new ArgumentException("Assign a Mesh to show its UV islands.");
            if (channel < 0 || channel > 7) throw new ArgumentOutOfRangeException(nameof(channel));
            var result = new UvIslandMap();
            using (var dataArray = MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var data = dataArray[0];
                if (!data.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel)))
                    throw new InvalidOperationException($"This mesh has no UV{channel} coordinates. Choose another UV channel.");
                if (submesh < -1 || submesh >= data.subMeshCount)
                    throw new InvalidOperationException("The selected submesh no longer exists. Choose All submeshes.");
                using (var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                using (var uvs = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp))
                {
                    data.GetVertices(positions);
                    data.GetUVs(channel, uvs);
                    var vertexIds = new int[data.vertexCount];
                    var vertices = new Dictionary<VertexKey, int>();
                    for (int i = 0; i < data.vertexCount; i++)
                    {
                        var key = new VertexKey(positions[i], uvs[i]);
                        if (!vertices.TryGetValue(key, out int id)) { id = vertices.Count; vertices.Add(key, id); }
                        vertexIds[i] = id;
                    }
                    var edges = new Dictionary<EdgeKey, Edge>();
                    void AddEdge(int a, int b, int opposite, int triangle)
                    {
                        var key = new EdgeKey(vertexIds[a], vertexIds[b]);
                        if (edges.TryGetValue(key, out Edge edge))
                        {
                            edge.count++;
                            if (edge.other < 0) { edge.other = triangle; edge.otherOpposite = uvs[opposite]; }
                            else (edge.extra ??= new List<int>()).Add(triangle);
                        }
                        else edges.Add(key, new Edge { a = uvs[a], b = uvs[b], opposite = uvs[opposite], triangle = triangle });
                    }
                    void AddTriangle(int a, int b, int c)
                    {
                        if ((uint)a >= data.vertexCount || (uint)b >= data.vertexCount || (uint)c >= data.vertexCount)
                            throw new InvalidOperationException("The mesh contains invalid triangle indices.");
                        if (!Finite(uvs[a]) || !Finite(uvs[b]) || !Finite(uvs[c]) ||
                            Math.Abs(Cross(uvs[b] - uvs[a], uvs[c] - uvs[a])) < 1e-14)
                        { result.SkippedTriangles++; return; }
                        int index = result.triangles.Count;
                        result.triangles.Add(new Triangle { a = uvs[a], b = uvs[b], c = uvs[c] });
                        AddEdge(a, b, c, index); AddEdge(b, c, a, index); AddEdge(c, a, b, index);
                    }
                    for (int s = 0; s < data.subMeshCount; s++)
                    {
                        if (submesh >= 0 && submesh != s) continue;
                        var descriptor = data.GetSubMesh(s);
                        if (descriptor.topology != MeshTopology.Triangles && descriptor.topology != MeshTopology.Quads) continue;
                        using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                        {
                            data.GetIndices(indices, s);
                            int step = descriptor.topology == MeshTopology.Quads ? 4 : 3;
                            for (int i = 0; i + step <= indices.Length; i += step)
                            {
                                AddTriangle(indices[i], indices[i + 1], indices[i + 2]);
                                if (step == 4) AddTriangle(indices[i], indices[i + 2], indices[i + 3]);
                            }
                        }
                    }
                    if (result.triangles.Count == 0) throw new InvalidOperationException("This UV channel has no non-empty triangle or quad faces.");
                    var parent = new int[result.triangles.Count];
                    var rank = new byte[parent.Length];
                    for (int i = 0; i < parent.Length; i++) parent[i] = i;
                    int Root(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                    foreach (Edge edge in edges.Values)
                    {
                        // Non-manifold and folded/stacked UV edges are kept as boundaries.
                        if (edge.count != 2 || Cross(edge.b - edge.a, edge.opposite - edge.a) *
                            Cross(edge.b - edge.a, edge.otherOpposite - edge.a) >= 0) continue;
                        edge.shared = true;
                        int a = Root(edge.triangle), b = Root(edge.other);
                        if (a == b) continue;
                        if (rank[a] < rank[b]) parent[a] = b;
                        else { parent[b] = a; if (rank[a] == rank[b]) rank[a]++; }
                    }
                    var roots = new Dictionary<int, int>();
                    for (int i = 0; i < parent.Length; i++)
                    {
                        int root = Root(i);
                        if (!roots.TryGetValue(root, out int id))
                        { id = result.islands.Count; roots.Add(root, id); result.islands.Add(new Island()); }
                        Triangle triangle = result.triangles[i]; triangle.island = id; result.triangles[i] = triangle;
                        result.islands[id].triangles.Add(i);
                        result.IndexTriangle(i);
                    }
                    // Record each unshared edge from the triangles, including non-manifold cases.
                    foreach (Island island in result.islands)
                    {
                        double largest = -1;
                        double totalArea = 0, centerX = 0, centerY = 0;
                        foreach (int index in island.triangles)
                        {
                            Triangle t = result.triangles[index];
                            double area = Math.Abs(Cross(t.b - t.a, t.c - t.a));
                            Vector2 center = (t.a + t.b + t.c) / 3f;
                            totalArea += area; centerX += center.x * area; centerY += center.y * area;
                            if (area > largest) { largest = area; island.marker = (t.a + t.b + t.c) / 3f; }
                        }
                        Vector2 centroid = new Vector2((float)(centerX / totalArea), (float)(centerY / totalArea));
                        foreach (int index in island.triangles)
                            if (Contains(result.triangles[index], centroid)) { island.marker = centroid; break; }
                    }
                    foreach (Edge edge in edges.Values)
                    {
                        if (edge.shared) continue;
                        var line = new Vector4(edge.a.x, edge.a.y, edge.b.x, edge.b.y);
                        result.islands[result.triangles[edge.triangle].island].edges.Add(line);
                        if (edge.other >= 0 && result.triangles[edge.other].island != result.triangles[edge.triangle].island)
                            result.islands[result.triangles[edge.other].island].edges.Add(line);
                        if (edge.extra != null)
                            foreach (int index in edge.extra) result.islands[result.triangles[index].island].edges.Add(line);
                    }
                }
            }
            return result;
        }

        private void IndexTriangle(int index)
        {
            Triangle t = triangles[index];
            Vector2 min = Vector2.Min(t.a, Vector2.Min(t.b, t.c)), max = Vector2.Max(t.a, Vector2.Max(t.b, t.c));
            if (max.x < 0 || max.y < 0 || min.x > 1 || min.y > 1) return;
            int x0 = Cell(min.x), x1 = Cell(max.x), y0 = Cell(min.y), y1 = Cell(max.y);
            if ((x1 - x0 + 1) * (y1 - y0 + 1) > 64) { largeTriangles.Add(index); return; }
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                (grid[y * GridSide + x] ??= new List<int>()).Add(index);
        }
        private static int Cell(float x) => Mathf.FloorToInt(Mathf.Clamp01(x) * (GridSide - 1));
        internal int Pick(Vector2 uv)
        {
            if (!Finite(uv) || uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) return -1;
            int found = -1;
            void Check(List<int> candidates)
            {
                if (candidates == null) return;
                foreach (int i in candidates)
                {
                    Triangle t = triangles[i];
                    if ((found < 0 || t.island < found) && Contains(t, uv)) found = t.island;
                }
            }
            Check(grid[Cell(uv.y) * GridSide + Cell(uv.x)]); Check(largeTriangles);
            return found;
        }
        internal static bool Contains(Triangle t, Vector2 p)
        {
            double a = Cross(t.b - t.a, p - t.a), b = Cross(t.c - t.b, p - t.b), c = Cross(t.a - t.c, p - t.c);
            return (a >= 0 && b >= 0 && c >= 0) || (a <= 0 && b <= 0 && c <= 0);
        }
        private static double Cross(Vector2 a, Vector2 b) => (double)a.x * b.y - (double)a.y * b.x;
        private static bool Finite(Vector2 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y);

        internal byte[] Rasterize(int island, int width, int height)
        {
            new CanvasSelection(width, height).ValidateSize();
            if ((uint)island >= islands.Count) throw new ArgumentOutOfRangeException(nameof(island));
            var pixels = new byte[width * height];
            foreach (int index in islands[island].triangles)
            {
                Triangle t = triangles[index];
                Vector2 a = Vector2.Scale(t.a, new Vector2(width, height)), b = Vector2.Scale(t.b, new Vector2(width, height)), c = Vector2.Scale(t.c, new Vector2(width, height));
                int y0 = Mathf.CeilToInt(Mathf.Clamp(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - .5f, 0, height));
                int y1 = Mathf.CeilToInt(Mathf.Clamp(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - .5f, 0, height));
                for (int y = y0; y < y1; y++)
                {
                    double left = double.PositiveInfinity, right = double.NegativeInfinity;
                    float scan = y + .5f;
                    void Intersect(Vector2 p, Vector2 q)
                    {
                        if ((p.y > scan) == (q.y > scan)) return;
                        double x = p.x + ((double)scan - p.y) * (q.x - (double)p.x) / (q.y - (double)p.y);
                        left = Math.Min(left, x); right = Math.Max(right, x);
                    }
                    Intersect(a, b); Intersect(b, c); Intersect(c, a);
                    if (left > right) continue;
                    int x0 = (int)Math.Ceiling(Math.Max(0, Math.Min(width, left - .5)));
                    int x1 = (int)Math.Ceiling(Math.Max(0, Math.Min(width, right - .5)));
                    if (x1 > x0) Array.Fill(pixels, (byte)255, y * width + x0, x1 - x0);
                }
            }
            return pixels;
        }
    }
}
