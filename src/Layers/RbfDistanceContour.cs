using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ArtisticDistanceField
    {
        // Keep RBF's fractional contour; do not quantize it to a new alpha mask.
        // Experimental sharp continuation without raster angle/support/owner caps.
        internal static Geometry PrepareRbfContour(NativeArray<float> field, int gridWidth, int gridHeight,
            NativeArray<Color32> input, int width, int height, byte threshold, int channel, Allocator allocator,
            SharpDistanceMode sharpMode = SharpDistanceMode.Unrestricted)
        {
            for (int i = 0; i < field.Length; i++)
                if (!float.IsFinite(field[i])) throw new InvalidOperationException("RBF produced a non-finite interpolant.");
            var features = new List<Feature>();
            int batchRows = math.max(1, 65536 / (gridWidth - 1));
            for (int first = 0; first + 1 < gridHeight; first += batchRows)
            {
                int count = math.min(batchRows, gridHeight - 1 - first);
                using var stream = new NativeStream(count, Allocator.TempJob);
                new RbfExtractJob { field = field, width = gridWidth, first = first, writer = stream.AsWriter() }
                    .Schedule(count, 1).Complete();
                var reader = stream.AsReader();
                for (int row = 0; row < count; row++)
                {
                    int segments = reader.BeginForEachIndex(row);
                    for (int i = 0; i < segments; i++) features.Add(reader.Read<Feature>());
                    reader.EndForEachIndex();
                }
                if (features.Count > 2000000)
                    throw new InvalidOperationException("RBF contour exceeds two million segments. Reduce source detail or resolution.");
            }
            var vertices = new List<Vertex>();
            var indices = new Dictionary<float2, int>();
            var links = new List<Link>();
            int VertexAt(float2 point)
            {
                if (indices.TryGetValue(point, out int index)) return index;
                index = vertices.Count;
                indices.Add(point, index); vertices.Add(new Vertex { point = point });
                return index;
            }
            foreach (Feature f in features)
            {
                int a = VertexAt(f.a), b = VertexAt(f.b);
                if (a == b) continue;
                vertices[a].links.Add(links.Count); vertices[b].links.Add(links.Count);
                links.Add(new Link { a = a, b = b });
            }
            features.Clear();
            var contours = new List<List<float2>>();
            var ranges = new List<int2>();
            void Append(List<float2> path, bool closed)
            {
                int first = features.Count;
                AddFeatures(path, closed, DistanceMetric.Sharp, features, unrestricted: sharpMode == SharpDistanceMode.Unrestricted);
                if (features.Count > first) ranges.Add(new int2(first, features.Count - first));
            }
            var visited = new bool[links.Count];
            // Ambiguous junctions end open paths instead of connecting unrelated
            // branches and inventing a sharp corner at a threshold tie.
            for (int pass = 0; pass < 2; pass++)
            for (int v = 0; v < vertices.Count; v++)
            {
                if (pass == 0 && vertices[v].links.Count == 2) continue;
                foreach (int firstLink in vertices[v].links)
                {
                    if (visited[firstLink]) continue;
                    int current = v, linkId = firstLink;
                    bool closed = false;
                    var path = new List<float2> { vertices[v].point };
                    while (!visited[linkId])
                    {
                        visited[linkId] = true;
                        Link link = links[linkId];
                        current = link.a == current ? link.b : link.a;
                        if (current == v)
                        {
                            closed = vertices[v].links.Count == 2;
                            if (!closed) path.Add(vertices[v].point);
                            break;
                        }
                        path.Add(vertices[current].point);
                        if (vertices[current].links.Count != 2) break;
                        linkId = -1;
                        foreach (int next in vertices[current].links)
                            if (!visited[next]) { linkId = next; break; }
                        if (linkId < 0) break;
                    }
                    if (path.Count < 2) continue;
                    double orientation = 0;
                    int edges = closed ? path.Count : path.Count - 1;
                    for (int i = 0; i < edges; i++)
                    {
                        float2 a = path[i], b = path[(i + 1) % path.Count], middle = (a + b) * .5f;
                        float2 d = math.normalizesafe(b - a), left = new float2(-d.y, d.x) * .125f;
                        orientation += SampleRbf(field, gridWidth, gridHeight, middle - left)
                            - SampleRbf(field, gridWidth, gridHeight, middle + left);
                    }
                    if (orientation < 0) path.Reverse();
                    if (closed && path.Count >= 3 && math.abs(SignedArea(path)) > 1e-8f) contours.Add(path);
                    else Append(path, false);
                }
            }
            // Recover supported straight flanks on RBF's contour, then normalize
            // filled regions before allowing any corner extension.
            var fitted = NormalizeContours(FitContours(contours, DistanceMetric.Sharp));
            foreach (var path in fitted) Append(path, true);
            var signChanges = ReconstructedSignChanges(contours, fitted, width, height);
            for (int i = 0; i < input.Length; i++)
                if ((field[2 * (i / width) * gridWidth + 2 * (i % width)] < 0) !=
                    DistanceFieldSource.IsObject(input[i], channel, threshold)) signChanges[i] ^= 1;
            var array = features.ToArray();
            var tree = new List<Node>();
            var roots = new List<int>();
            foreach (int2 range in ranges) roots.Add(BuildTree(array, range.x, range.y, tree));
            var geometry = new Geometry { sharpMode = sharpMode };
            try
            {
                geometry.features = new NativeArray<Feature>(array, allocator);
                geometry.nodes = new NativeArray<Node>(tree.ToArray(), allocator);
                geometry.roots = new NativeArray<int>(roots.ToArray(), allocator);
                geometry.rootCosines = new NativeArray<float>(0, allocator);
                geometry.signChanges = new NativeArray<byte>(signChanges, allocator);
                PrepareCornerParameters(geometry.features, true, allocator, out geometry.cornerIndices, out geometry.corners, unrestricted: true);
                return geometry;
            }
            catch { geometry.Dispose(); throw; }
        }

        static float SampleRbf(NativeArray<float> field, int width, int height, float2 point)
        {
            point = math.clamp(point * 2, 0, new float2(width - 1, height - 1));
            int x = (int)point.x, y = (int)point.y, x1 = math.min(x + 1, width - 1), y1 = math.min(y + 1, height - 1);
            return math.lerp(math.lerp(field[y * width + x], field[y * width + x1], math.frac(point.x)),
                math.lerp(field[y1 * width + x], field[y1 * width + x1], math.frac(point.x)), math.frac(point.y));
        }

        [BurstCompile]
        struct RbfExtractJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> field;
            public NativeStream.Writer writer;
            public int width, first;
            public void Execute(int row)
            {
                writer.BeginForEachIndex(row);
                int y = first + row;
                for (int x = 0; x + 1 < width; x++)
                {
                    float a = field[y * width + x], b = field[y * width + x + 1];
                    float c = field[(y + 1) * width + x + 1], d = field[(y + 1) * width + x];
                    var points = new FixedList64Bytes<float2>();
                    if ((a < 0) != (b < 0)) points.Add(new float2(x + a / (a - b), y) * .5f);
                    if ((b < 0) != (c < 0)) points.Add(new float2(x + 1, y + b / (b - c)) * .5f);
                    if ((c < 0) != (d < 0)) points.Add(new float2(x + d / (d - c), y + 1) * .5f);
                    if ((d < 0) != (a < 0)) points.Add(new float2(x, y + a / (a - d)) * .5f);
                    if (points.Length == 2) writer.Write(new Feature { a = points[0], b = points[1] });
                    else if (points.Length == 4)
                    {
                        bool adjacent = a * c - b * d >= 0;
                        writer.Write(new Feature { a = points[0], b = points[adjacent ? 1 : 3] });
                        writer.Write(new Feature { a = points[adjacent ? 2 : 1], b = points[adjacent ? 3 : 2] });
                    }
                }
                writer.EndForEachIndex();
            }
        }

    }
}
