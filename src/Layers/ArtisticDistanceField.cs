using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;

namespace DCFApixels.WhimTex
{
    // All modes use the same subpixel contour. Joins change offset geometry, not coverage.
    internal static class ArtisticDistanceField
    {
        internal const float EmptyDistance = 1e10f;

        internal struct Feature
        {
            public float2 a, b, previousDirection, nextDirection;
            public int color;
            public int vertex;
            public float2 Minimum => math.min(a, b);
            public float2 Maximum => math.max(a, b);
        }

        internal struct Node
        {
            public float2 minimum, maximum;
            public int start, count, right;
        }

        sealed class Geometry : IDisposable
        {
            public NativeArray<Feature> features;
            public NativeArray<Node> nodes;
            public NativeArray<int> roots;
            public NativeArray<float> rootCosines;
            public void Dispose()
            {
                if (features.IsCreated) features.Dispose();
                if (nodes.IsCreated) nodes.Dispose();
                if (roots.IsCreated) roots.Dispose();
                if (rootCosines.IsCreated) rootCosines.Dispose();
            }
        }

        public static void Compute(NativeArray<Color32> input, NativeArray<float> output,
            int width, int height, byte threshold, int channel, DistanceMetric metric, float miterLimit)
        {
            using var geometry = Build(input, width, height, threshold, channel, metric);
            using var timing = DistanceFieldTiming.Measure("distance.jobs");
            new ScalarJob { input = input, output = output, features = geometry.features,
                nodes = geometry.nodes, roots = geometry.roots, rootCosines = geometry.rootCosines,
                width = width, threshold = threshold, channel = channel,
                metric = metric, miterLimit = math.max(1, miterLimit) }.Schedule(output.Length, 64).Complete();
        }

        public static void ComputeMultiChannel(NativeArray<Color32> input, NativeArray<float4> output,
            int width, int height, byte threshold, int channel)
        {
            using var geometry = Build(input, width, height, threshold, channel, DistanceMetric.MTSDF);
            long stage = DistanceFieldTiming.Start();
            new MultiJob { input = input, output = output, features = geometry.features,
                nodes = geometry.nodes, roots = geometry.roots, rootCosines = geometry.rootCosines,
                width = width, threshold = threshold, channel = channel
            }.Schedule(output.Length, 64).Complete();
            DistanceFieldTiming.Report("distance.jobs", ref stage);
            using var correction = new NativeArray<byte>(output.Length, Allocator.TempJob);
            new CorrectionFlagsJob { field = output, flags = correction, width = width, height = height }
                .Schedule(output.Length, 64).Complete();
            new CorrectColorsJob { field = output, flags = correction }.Schedule(output.Length, 64).Complete();
            DistanceFieldTiming.Report("mtsdf.correct", ref stage);
        }

        struct Link { public int a, b; }
        sealed class Vertex
        {
            public float2 point;
            public readonly List<int> links = new List<int>(2);
        }

        static Geometry Build(NativeArray<Color32> input, int width, int height,
            byte threshold, int channel, DistanceMetric metric)
        {
            using var timing = DistanceFieldTiming.Measure("geometry.total");
            long stage = DistanceFieldTiming.Start();
            var vertices = new List<Vertex>();
            var keys = new Dictionary<long, int>();
            var links = new List<Link>();
            int[] crossings = new int[4];
            long pixelKeyStart = (long)(width - 1) * height + (long)(height - 1) * width;
            int VertexAt(long key, float2 point)
            {
                // A contour exactly through a pixel center is shared by horizontal and
                // vertical grid edges. Weld it, or quantized soft masks become open paths.
                if (point.x == math.floor(point.x) && point.y == math.floor(point.y))
                    key = pixelKeyStart + (long)(int)point.y * width + (int)point.x;
                if (!keys.TryGetValue(key, out int id))
                {
                    id = vertices.Count; keys.Add(key, id); vertices.Add(new Vertex { point = point });
                }
                return id;
            }
            void AddLink(int a, int b)
            {
                if (a == b || math.lengthsq(vertices[a].point - vertices[b].point) < 1e-12f) return;
                int id = links.Count;
                links.Add(new Link { a = a, b = b });
                vertices[a].links.Add(id); vertices[b].links.Add(id);
            }
            long horizontalCount = (long)(width - 1) * height;
            int rowsPerBatch = math.max(1, DistanceContourCells.CellsPerBatch / math.max(1, width - 1));
            for (int firstRow = 0; firstRow + 1 < height; firstRow += rowsPerBatch)
            {
                int rowCount = math.min(rowsPerBatch, height - 1 - firstRow);
                using var cells = DistanceContourCells.Extract(input, width, height, threshold, channel, firstRow, rowCount);
                var reader = cells.AsReader();
                for (int row = 0; row < rowCount; row++)
                {
                    int count = reader.BeginForEachIndex(row);
                    for (int cellIndex = 0; cellIndex < count; cellIndex++)
                    {
                        var cell = reader.Read<DistanceContourCell>();
                        int x = cell.x, y = cell.y;
                        float a = cell.values.x, b = cell.values.y, c = cell.values.z, d = cell.values.w;
                        int n = 0;
                        if ((a > 0) != (b > 0)) crossings[n++] = VertexAt((long)y * (width - 1) + x, new float2(x + a / (a - b), y));
                        if ((b > 0) != (c > 0)) crossings[n++] = VertexAt(horizontalCount + (long)y * width + x + 1, new float2(x + 1, y + b / (b - c)));
                        if ((c > 0) != (d > 0)) crossings[n++] = VertexAt((long)(y + 1) * (width - 1) + x, new float2(x + d / (d - c), y + 1));
                        if ((d > 0) != (a > 0)) crossings[n++] = VertexAt(horizontalCount + (long)y * width + x, new float2(x, y + a / (a - d)));
                        if (n == 2) AddLink(crossings[0], crossings[1]);
                        else if (n == 4)
                        {
                            // Bilinear asymptotic decider: do not arbitrarily merge diagonal islands.
                            if (a * c - b * d >= 0)
                            { AddLink(crossings[0], crossings[1]); AddLink(crossings[2], crossings[3]); }
                            else
                            { AddLink(crossings[0], crossings[3]); AddLink(crossings[1], crossings[2]); }
                        }
                        if (links.Count > 2000000)
                            throw new InvalidOperationException("Distance contour exceeds two million segments. Reduce source detail or resolution.");
                    }
                    reader.EndForEachIndex();
                }
            }
            DistanceFieldTiming.Report("contour.extract", ref stage);
            SplitTouchingBranches(vertices, links, input, width, height, threshold, channel);
            var features = new List<Feature>();
            var ranges = new List<int2>();
            var contours = new List<List<float2>>();
            foreach (var vertex in vertices)
                if (vertex.links.Count == 0)
                    features.Add(new Feature { a = vertex.point, b = vertex.point, vertex = 4, color = 7 });
            if (features.Count > 0) ranges.Add(new int2(0, features.Count));
            void Append(List<float2> path, bool closed)
            {
                int first = features.Count;
                AddFeatures(path, closed, metric, features);
                if (features.Count > first) ranges.Add(new int2(first, features.Count - first));
            }
            var visited = new bool[links.Count];
            for (int pass = 0; pass < 2; pass++)
            for (int v = 0; v < vertices.Count; v++)
            {
                if (pass == 0 && vertices[v].links.Count != 1) continue;
                foreach (int firstLink in vertices[v].links)
                {
                    if (visited[firstLink]) continue;
                    var path = new List<float2>();
                    int current = v, linkId = firstLink;
                    bool closed = false;
                    path.Add(vertices[current].point);
                    while (!visited[linkId])
                    {
                        visited[linkId] = true;
                        var link = links[linkId];
                        current = link.a == current ? link.b : link.a;
                        if (current == v) { closed = true; break; }
                        path.Add(vertices[current].point);
                        linkId = -1;
                        foreach (int candidate in vertices[current].links)
                            if (!visited[candidate]) { linkId = candidate; break; }
                        if (linkId < 0) break;
                    }
                    if (path.Count < 2) continue;
                    Orient(path, input, width, height, channel);
                    path = Simplify(path, closed, metric);
                    if (closed && path.Count >= 3 && math.abs(SignedArea(path)) > 1e-8f) contours.Add(path);
                    // Zero-area threshold contours are lines/point contacts, not filled
                    // polygons. A boolean region operation must not erase their boundary.
                    else Append(path, closed);
                }
            }
            // Fitting separate raster components can make their reconstructed regions
            // overlap. Only the boundary of the filled union may generate joins.
            DistanceFieldTiming.Report("contour.traceFit", ref stage);
            foreach (var path in NormalizeContours(contours)) Append(path, true);
            DistanceFieldTiming.Report("contour.normalizeFeatures", ref stage);
            var array = features.ToArray();
            var nodes = new List<Node>();
            var roots = new List<int>(); var cosines = new List<float>();
            if (metric == DistanceMetric.MTSDF)
            {
                foreach (int2 range in ranges)
                {
                    float cosine = 1;
                    for (int i = range.x; i < range.x + range.y; i++)
                    {
                        Feature f = array[i];
                        cosine = math.min(cosine, math.sqrt(math.max(0, (1 + math.dot(f.previousDirection, math.normalizesafe(f.b - f.a))) * .5f)));
                        cosine = math.min(cosine, math.sqrt(math.max(0, (1 + math.dot(math.normalizesafe(f.b - f.a), f.nextDirection)) * .5f)));
                    }
                    roots.Add(BuildTree(array, range.x, range.y, nodes)); cosines.Add(cosine);
                }
            }
            else if (array.Length > 0) BuildTree(array, 0, array.Length, nodes);
            var result = new Geometry();
            try
            {
                result.features = new NativeArray<Feature>(array, Allocator.TempJob);
                result.nodes = new NativeArray<Node>(nodes.ToArray(), Allocator.TempJob);
                result.roots = new NativeArray<int>(roots.ToArray(), Allocator.TempJob);
                result.rootCosines = new NativeArray<float>(cosines.ToArray(), Allocator.TempJob);
                DistanceFieldTiming.Report("geometry.bvhUpload", ref stage);
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        static void SplitTouchingBranches(List<Vertex> vertices, List<Link> links,
            NativeArray<Color32> input, int width, int height, byte threshold, int channel)
        {
            for (int v = 0; v < vertices.Count; v++)
            {
                Vertex vertex = vertices[v];
                if (vertex.links.Count <= 2) continue;
                int Other(int link) => links[link].a == v ? links[link].b : links[link].a;
                float2 Direction(int link) => math.normalizesafe(vertices[Other(link)].point - vertex.point);
                var ordered = new List<int>(vertex.links);
                ordered.Sort((a, b) => math.atan2(Direction(a).y, Direction(a).x).CompareTo(math.atan2(Direction(b).y, Direction(b).x)));
                var used = new bool[ordered.Count];
                var pairs = new List<int2>();
                bool PositiveSector(int first, int second)
                {
                    float start = math.atan2(Direction(first).y, Direction(first).x);
                    float end = math.atan2(Direction(second).y, Direction(second).x);
                    float span = end - start;
                    if (span < 0) span += 2 * math.PI;
                    if (span < 1e-6f) return false;
                    int px = (int)vertex.point.x, py = (int)vertex.point.y;
                    if (vertex.point.x == px && vertex.point.y == py)
                    {
                        // At a welded threshold pixel, foreground sectors contain a positive
                        // incident grid ray. A chord bisector can miss a narrow bilinear wedge.
                        for (int ray = 0; ray < 4; ray++)
                        {
                            int x = px + (ray == 0 ? 1 : ray == 2 ? -1 : 0);
                            int y = py + (ray == 1 ? 1 : ray == 3 ? -1 : 0);
                            if (x < 0 || x >= width || y < 0 || y >= height ||
                                DistanceFieldSource.Value(input[y * width + x], channel) <= threshold) continue;
                            float angle = ray * (math.PI * .5f) - start;
                            if (angle < 0) angle += 2 * math.PI;
                            if (angle >= 2 * math.PI) angle -= 2 * math.PI;
                            if (angle > 1e-6f && angle < span - 1e-6f) return true;
                        }
                        return false;
                    }
                    float2 middle = math.normalizesafe(Direction(first) + Direction(second));
                    return Sample(vertex.point + .25f * middle, input, width, height, channel) > threshold;
                }
                for (int k = 0; k < ordered.Count; k++)
                {
                    int next = (k + 1) % ordered.Count;
                    if (used[k] || used[next]) continue;
                    if (!PositiveSector(ordered[k], ordered[next])) continue;
                    used[k] = used[next] = true;
                    pairs.Add(new int2(ordered[k], ordered[next]));
                }
                // Ambiguous/unpaired branches stay open rather than joining unrelated islands.
                for (int k = 0; k < ordered.Count; k++) if (!used[k]) pairs.Add(new int2(ordered[k], -1));
                vertex.links.Clear();
                for (int k = 0; k < pairs.Count; k++)
                {
                    int id = k == 0 ? v : vertices.Count;
                    Vertex target = k == 0 ? vertex : new Vertex { point = vertex.point };
                    if (k != 0) vertices.Add(target);
                    for (int c = 0; c < 2; c++)
                    {
                        int linkId = pairs[k][c];
                        if (linkId < 0) continue;
                        Link link = links[linkId];
                        if (link.a == v) link.a = id; else link.b = id;
                        links[linkId] = link;
                        target.links.Add(linkId);
                    }
                }
            }
        }

        static List<float2> Simplify(List<float2> points, bool closed, DistanceMetric metric,
            bool recoverCorners = true, float tolerance = .25f)
        {
            if (points.Count < 4) return points;
            if (closed)
            {
                float perimeter = 0;
                for (int i = 0; i < points.Count; i++) perimeter += math.distance(points[i], points[(i + 1) % points.Count]);
                // Do not spend a fixed simplification budget on an entire tiny island.
                // Even a small lost cap can move a positive sample onto the boundary.
                if (perimeter < 4) return points;
            }
            List<float2> original = points;
            if (closed && recoverCorners && metric != DistanceMetric.EuclideanAntialiased)
                points = RecoverCorners(points);
            var ordered = new List<float2>(points);
            if (closed) ordered.Add(points[0]);
            var keep = new bool[ordered.Count];
            keep[0] = keep[ordered.Count - 1] = true;
            int opposite = ordered.Count / 2;
            keep[opposite] = true;
            var pending = new Stack<int2>();
            pending.Push(new int2(0, opposite)); pending.Push(new int2(opposite, ordered.Count - 1));
            while (pending.Count > 0)
            {
                int2 range = pending.Pop();
                float2 a = ordered[range.x], delta = ordered[range.y] - a;
                float length = math.lengthsq(delta), best = tolerance * tolerance;
                int selected = -1;
                for (int i = range.x + 1; i < range.y; i++)
                {
                    float t = length > 1e-12f ? math.saturate(math.dot(ordered[i] - a, delta) / length) : 0;
                    float error = math.lengthsq(ordered[i] - a - t * delta);
                    if (error > best) { best = error; selected = i; }
                }
                if (selected >= 0)
                {
                    keep[selected] = true;
                    pending.Push(new int2(range.x, selected)); pending.Push(new int2(selected, range.y));
                }
            }
            var result = new List<float2>();
            for (int i = 0; i < ordered.Count - (closed ? 1 : 0); i++) if (keep[i]) result.Add(ordered[i]);
            if (closed)
            {
                float originalArea = SignedArea(original), simplifiedArea = SignedArea(result);
                if (result.Count < 3 || math.abs(simplifiedArea) < 1e-8f || originalArea * simplifiedArea <= 0 ||
                    math.abs(simplifiedArea) < .8f * math.abs(originalArea))
                    result = new List<float2>(original);
            }
            return result;
        }

        static List<List<float2>> NormalizeContours(List<List<float2>> contours)
        {
            using var timing = DistanceFieldTiming.Measure("contour.normalize");
            if (contours.Count == 0) return contours;
            var subjects = new PathsD(contours.Count);
            foreach (var contour in contours)
            {
                var path = new PathD(contour.Count);
                foreach (float2 p in contour) path.Add(new PointD(p.x, p.y));
                subjects.Add(path);
            }
            // Explicit micropixel quantization, not the library's default .01 px.
            var clipper = new ClipperD(6);
            clipper.AddSubject(subjects);
            var union = new PathsD();
            bool succeeded;
            using (DistanceFieldTiming.Measure("contour.union"))
                succeeded = clipper.Execute(ClipType.Union, FillRule.NonZero, union);
            if (!succeeded)
                throw new InvalidOperationException("Distance contour normalization failed.");
            var normalized = new List<List<float2>>(union.Count);
            int count = 0;
            foreach (var path in union)
            {
                var contour = new List<float2>(path.Count);
                foreach (PointD p in path) contour.Add(new float2((float)p.x, (float)p.y));
                normalized.Add(contour);
                count += contour.Count;
                if (count > 2000000) throw new InvalidOperationException("Normalized distance contour exceeds two million segments.");
            }
            // Intersections can leave tiny redundant flanks. Remove them with a
            // tighter budget, but never cross another boundary or close a narrow gap.
            var cleaned = new List<List<float2>>(normalized.Count);
            bool changed = false;
            foreach (var path in normalized)
            {
                var result = Simplify(path, true, DistanceMetric.Bevel, false, .05f);
                changed |= result.Count != path.Count;
                cleaned.Add(result);
            }
            if (!changed) return normalized;
            return CleanupPreservesTopology(normalized, cleaned) ? cleaned : normalized;
        }

        static bool CleanupPreservesTopology(List<List<float2>> original, List<List<float2>> cleaned)
        {
            using var timing = DistanceFieldTiming.Measure("contour.topologyGuard");
            Feature[] Edges(List<List<float2>> paths)
            {
                var edges = new List<Feature>();
                for (int c = 0; c < paths.Count; c++)
                    for (int i = 0; i < paths[c].Count; i++)
                        edges.Add(new Feature { a = paths[c][i], b = paths[c][(i + 1) % paths[c].Count], vertex = c, color = i });
                return edges.ToArray();
            }
            bool Meets(float2 a, float2 b, float2 c, float2 d)
            {
                // Robust integer predicates at the same precision as the union.
                Point64 P(float2 p) => new Point64(new PointD(p.x, p.y), 1000000);
                Point64 aa = P(a), bb = P(b), cc = P(c), dd = P(d);
                int ac = InternalClipper.CrossProductSign(aa, bb, cc), ad = InternalClipper.CrossProductSign(aa, bb, dd);
                int ca = InternalClipper.CrossProductSign(cc, dd, aa), cb = InternalClipper.CrossProductSign(cc, dd, bb);
                if (ac * ad < 0 && ca * cb < 0) return true;
                bool On(Point64 p, Point64 x, Point64 y) => p.X >= Math.Min(x.X, y.X) && p.X <= Math.Max(x.X, y.X) &&
                    p.Y >= Math.Min(x.Y, y.Y) && p.Y <= Math.Max(x.Y, y.Y);
                bool Endpoint(Point64 p, Point64 x, Point64 y) => p == x || p == y;
                if (ac == 0 && On(cc, aa, bb) && !Endpoint(cc, aa, bb) ||
                    ad == 0 && On(dd, aa, bb) && !Endpoint(dd, aa, bb) ||
                    ca == 0 && On(aa, cc, dd) && !Endpoint(aa, cc, dd) ||
                    cb == 0 && On(bb, cc, dd) && !Endpoint(bb, cc, dd)) return true;
                // Coincident endpoints are existing point contacts, not a new join.
                return ac == 0 && ad == 0 && (aa == cc && bb == dd || aa == dd && bb == cc);
            }
            bool Clear(float2 a, float2 b, Feature[] edges, List<Node> nodes, int path, int first, int last, int n)
            {
                float2 min = math.min(a, b), max = math.max(a, b);
                var stack = new Stack<int>();
                if (nodes.Count > 0) stack.Push(0);
                while (stack.Count > 0)
                {
                    int nodeId = stack.Pop();
                    Node node = nodes[nodeId];
                    if (math.any(node.maximum < min) || math.any(node.minimum > max)) continue;
                    if (node.count == 0) { stack.Push(node.right); stack.Push(nodeId + 1); continue; }
                    for (int i = node.start; i < node.start + node.count; i++)
                    {
                        Feature edge = edges[i];
                        int offset = (edge.color - first + n) % n, span = (last - first + n) % n;
                        if (edge.vertex == path && offset < span) continue;
                        if (Meets(a, b, edge.a, edge.b)) return false;
                    }
                }
                return true;
            }
            var sourceEdges = Edges(original); var sourceNodes = new List<Node>();
            if (sourceEdges.Length > 0) BuildTree(sourceEdges, 0, sourceEdges.Length, sourceNodes);
            var targetEdges = Edges(cleaned); var targetNodes = new List<Node>();
            if (targetEdges.Length > 0) BuildTree(targetEdges, 0, targetEdges.Length, targetNodes);
            for (int c = 0; c < original.Count; c++)
            {
                var indices = new Dictionary<float2, int>();
                for (int i = 0; i < original[c].Count; i++)
                {
                    if (indices.ContainsKey(original[c][i])) return false;
                    indices.Add(original[c][i], i);
                }
                for (int i = 0; i < cleaned[c].Count; i++)
                {
                    float2 a = cleaned[c][i], b = cleaned[c][(i + 1) % cleaned[c].Count];
                    int first = indices[a], last = indices[b], n = original[c].Count;
                    if ((last - first + n) % n == 1) continue;
                    if (!Clear(a, b, sourceEdges, sourceNodes, c, first, last, n) ||
                        !Clear(a, b, targetEdges, targetNodes, c, i, (i + 1) % cleaned[c].Count, cleaned[c].Count)) return false;
                }
            }
            Path64 Quantize(List<float2> path)
            {
                var result = new Path64(path.Count);
                foreach (float2 p in path) result.Add(new Point64(new PointD(p.x, p.y), 1000000));
                return result;
            }
            // An intersection-free chord can sweep over an entire small hole/island.
            // Preserve containment AND point-contact states, not only edge crossings.
            for (int c = 0; c < original.Count; c++)
            {
                if (original[c].Count == cleaned[c].Count) continue;
                var before = Quantize(original[c]); var after = Quantize(cleaned[c]);
                Rect64 bounds = InternalClipper.GetBounds(before);
                for (int other = 0; other < original.Count; other++)
                {
                    if (other == c) continue;
                    foreach (float2 p in original[other])
                    {
                        var point = new Point64(new PointD(p.x, p.y), 1000000);
                        if (point.X < bounds.left || point.X > bounds.right || point.Y < bounds.top || point.Y > bounds.bottom) continue;
                        if (InternalClipper.PointInPolygon(point, before) != InternalClipper.PointInPolygon(point, after)) return false;
                    }
                }
            }
            return true;
        }

        struct FittedLine { public float2 center, direction; public float error; }
        struct FittedCorner
        {
            public int index, gap;
            public float2 point;
            public FittedLine left, right;
            public float score;
        }

        // Fit the raw isocontour, before RDP chooses individual edges. Arc-length
        // weighting and orthogonal least squares avoid grid-axis and short-edge bias.
        // The budgets describe raster uncertainty, not a promise to recover vectors.
        static List<float2> RecoverCorners(List<float2> raw)
        {
            using var timing = DistanceFieldTiming.Measure("contour.cornerFit");
            const float spacing = .5f, support = 8f, normalBudget = .6f;
            float perimeter = 0;
            var arc = new float[raw.Count + 1];
            for (int i = 0; i < raw.Count; i++) arc[i + 1] = perimeter += math.distance(raw[i], raw[(i + 1) % raw.Count]);
            if (perimeter < support * 4) return raw;
            int n = (int)Math.Ceiling(perimeter / spacing);
            float step = perimeter / n;
            var samples = new float2[n];
            int segment = 0;
            for (int i = 0; i < n; i++)
            {
                float s = i * step;
                while (segment + 1 < raw.Count && arc[segment + 1] < s) segment++;
                samples[i] = math.lerp(raw[segment], raw[(segment + 1) % raw.Count],
                    (s - arc[segment]) / (arc[segment + 1] - arc[segment]));
            }
            float2 At(int i) => samples[(i % n + n) % n];
            bool Fit(int first, int last, out FittedLine line)
            {
                line = default;
                double x = 0, y = 0;
                int count = last - first + 1;
                float2 origin = At(first);
                for (int k = first; k <= last; k++) { float2 p = At(k) - origin; x += p.x; y += p.y; }
                float2 center = new float2((float)(x / count), (float)(y / count));
                double xx = 0, xy = 0, yy = 0;
                for (int k = first; k <= last; k++)
                {
                    float2 p = At(k) - origin - center;
                    xx += (double)p.x * p.x; xy += (double)p.x * p.y; yy += (double)p.y * p.y;
                }
                double angle = .5 * Math.Atan2(2 * xy, xx - yy);
                float2 direction = new float2((float)Math.Cos(angle), (float)Math.Sin(angle));
                if (math.dot(direction, At(last) - At(first)) < 0) direction = -direction;
                float2 normal = new float2(-direction.y, direction.x);
                float error = 0;
                double squares = 0;
                for (int k = first; k <= last; k++)
                {
                    float e = math.abs(math.dot(At(k) - origin - center, normal));
                    error = math.max(error, e); squares += e * e;
                }
                line = new FittedLine { center = origin + center, direction = direction, error = (float)(squares / count) };
                return error <= .35f && squares / count <= .025;
            }
            float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
            float2 Project(float2 p, FittedLine line) => line.center + math.dot(p - line.center, line.direction) * line.direction;
            int flank = (int)Math.Ceiling(support / step);
            var proposals = new List<FittedCorner>();
            for (int i = 0; i < n; i++)
            {
                // Cheap curvature gate before repeated fits. A smooth arc's two
                // nearby chords cannot establish a hard turn supported by both flanks.
                float2 incoming = math.normalizesafe(At(i) - At(i - flank));
                float2 outgoing = math.normalizesafe(At(i + flank) - At(i));
                if (math.dot(incoming, outgoing) > .8f) continue;
                FittedCorner best = default;
                best.score = float.MaxValue;
                for (int gap = 1; gap * step <= 4; gap++)
                {
                    if (!Fit(i - gap - flank, i - gap, out FittedLine left) || !Fit(i + gap, i + gap + flank, out FittedLine right)) continue;
                    float dot = math.dot(left.direction, right.direction), cross = Cross(left.direction, right.direction);
                    if (dot > .7071068f || math.abs(cross) < .05f) continue;
                    float2 point = left.center + Cross(right.center - left.center, right.direction) / cross * left.direction;
                    if (math.dot(point - At(i - gap), left.direction) < 0 || math.dot(point - At(i + gap), right.direction) > 0) continue;
                    float2 a = Project(At(i - gap - flank), left), b = Project(At(i + gap + flank), right);
                    float error = 0, tipDistance = float.MaxValue;
                    for (int k = i - gap - flank; k <= i + gap + flank; k++)
                    {
                        error = math.max(error, math.min(SquaredSegmentDistance(At(k), a, point), SquaredSegmentDistance(At(k), point, b)));
                        if (k >= i - gap && k < i + gap)
                            tipDistance = math.min(tipDistance, SquaredSegmentDistance(point, At(k), At(k + 1)));
                    }
                    float cosine = math.sqrt(math.max(.0001f, (1 + dot) * .5f));
                    float tipLimit = math.min(2, normalBudget / cosine);
                    if (error > normalBudget * normalBudget || tipDistance > tipLimit * tipLimit) continue;
                    float score = left.error + right.error + .001f * gap + .01f * error;
                    if (score < best.score) best = new FittedCorner { index = i, gap = gap, point = point, left = left, right = right, score = score };
                }
                if (best.score < float.MaxValue) proposals.Add(best);
            }
            proposals.Sort((a, b) => { int order = a.score.CompareTo(b.score); return order != 0 ? order : a.index.CompareTo(b.index); });
            var selected = new List<FittedCorner>();
            var occupied = new bool[n];
            foreach (FittedCorner candidate in proposals)
            {
                bool overlap = false;
                for (int k = -candidate.gap; k <= candidate.gap; k++) overlap |= occupied[(candidate.index + k + n) % n];
                if (overlap) continue;
                selected.Add(candidate);
                for (int k = -candidate.gap - 2; k <= candidate.gap + 2; k++) occupied[(candidate.index + k + n) % n] = true;
            }
            if (selected.Count == 0) return raw;
            selected.Sort((a, b) => a.index.CompareTo(b.index));
            var result = new List<float2>();
            for (int c = 0; c < selected.Count; c++)
            {
                FittedCorner current = selected[c], next = selected[(c + 1) % selected.Count], previous = selected[(c + selected.Count - 1) % selected.Count];
                int before = (current.index - previous.index + n) % n, after = (next.index - current.index + n) % n;
                if (selected.Count == 1) before = after = n;
                int start = current.index - Math.Min(current.gap + flank, before / 2);
                int end = current.index + Math.Min(current.gap + flank, after / 2);
                result.Add(Project(At(start), current.left)); result.Add(current.point); result.Add(Project(At(end), current.right));
                int nextStart = next.index + (c + 1 == selected.Count ? n : 0) - Math.Min(next.gap + flank, after / 2);
                // Preserve unsampled details outside fitted regions. Resampling is
                // only a weighting device; it must not replace the remaining contour.
                float from = end * step, to = nextStart * step;
                int cycle = (int)Math.Floor(from / perimeter);
                int index = Array.BinarySearch(arc, from - cycle * perimeter);
                index = index < 0 ? ~index : index + 1;
                if (index >= raw.Count) { index = 0; cycle++; }
                while (arc[index] + cycle * perimeter < to)
                {
                    result.Add(raw[index]);
                    if (++index == raw.Count) { index = 0; cycle++; }
                }
            }
            return result;
        }

        static float SquaredSegmentDistance(float2 p, float2 a, float2 b)
        {
            float2 delta = b - a;
            float length = math.lengthsq(delta);
            float t = length > 1e-12f ? math.saturate(math.dot(p - a, delta) / length) : 0;
            return math.lengthsq(p - a - t * delta);
        }

        static float SignedArea(List<float2> points)
        {
            if (points.Count < 3) return 0;
            float sum = 0;
            float2 origin = points[0];
            for (int i = 1; i + 1 < points.Count; i++)
            {
                float2 a = points[i] - origin, b = points[i + 1] - origin;
                sum += a.x * b.y - a.y * b.x;
            }
            return sum * .5f;
        }

        static void Orient(List<float2> points, NativeArray<Color32> input, int width, int height, int channel)
        {
            for (int i = 0; i + 1 < points.Count; i++)
            {
                float2 d = math.normalizesafe(points[i + 1] - points[i]);
                float2 normal = new float2(-d.y, d.x), mid = (points[i] + points[i + 1]) * .5f;
                float difference = Sample(mid + .1f * normal, input, width, height, channel) - Sample(mid - .1f * normal, input, width, height, channel);
                if (math.abs(difference) < .001f) continue;
                if (difference < 0) points.Reverse();
                return;
            }
        }

        static float Sample(float2 p, NativeArray<Color32> input, int width, int height, int channel)
        {
            p = math.clamp(p, 0, new float2(width - 1, height - 1));
            int x = (int)p.x, y = (int)p.y, x1 = math.min(x + 1, width - 1), y1 = math.min(y + 1, height - 1);
            return math.lerp(math.lerp(DistanceFieldSource.Value(input[y * width + x], channel), DistanceFieldSource.Value(input[y * width + x1], channel), math.frac(p.x)),
                math.lerp(DistanceFieldSource.Value(input[y1 * width + x], channel), DistanceFieldSource.Value(input[y1 * width + x1], channel), math.frac(p.x)), math.frac(p.y));
        }

        static void AddFeatures(List<float2> path, bool closed, DistanceMetric metric, List<Feature> features)
        {
            int n = path.Count, edgeCount = closed ? n : n - 1;
            var corners = new List<int>();
            if (closed)
                for (int i = 0; i < n; i++)
                    if (math.dot(math.normalizesafe(path[i] - path[(i + n - 1) % n]), math.normalizesafe(path[(i + 1) % n] - path[i])) < .8660254f)
                        corners.Add(i);
            int[] colors = new int[edgeCount];
            for (int i = 0; i < edgeCount; i++) colors[i] = 7;
            if (metric == DistanceMetric.MTSDF && corners.Count > 0)
            {
                if (corners.Count == 1)
                { corners.Add((corners[0] + math.max(1, n / 3)) % n); corners.Add((corners[0] + math.max(2, 2 * n / 3)) % n); corners.Sort(); }
                int lastColor = -1;
                for (int k = 0; k < corners.Count; k++)
                {
                    int colorIndex = k % 3;
                    if (k == corners.Count - 1 && colorIndex == 0) colorIndex = lastColor == 1 ? 2 : 1;
                    int color = colorIndex == 0 ? 6 : colorIndex == 1 ? 5 : 3;
                    int end = corners[(k + 1) % corners.Count];
                    for (int i = corners[k]; ; i = (i + 1) % n) { colors[i] = color; if ((i + 1) % n == end) break; }
                    lastColor = colorIndex;
                }
            }
            for (int i = 0; i < edgeCount; i++)
            {
                int j = (i + 1) % n;
                float2 direction = math.normalizesafe(path[j] - path[i]);
                if (math.lengthsq(direction) < .5f) continue;
                float2 previous = i > 0 || closed ? math.normalizesafe(path[i] - path[(i + n - 1) % n]) : direction;
                float2 next = j + 1 < n || closed ? math.normalizesafe(path[(j + 1) % n] - path[j]) : direction;
                features.Add(new Feature { a = path[i], b = path[j], previousDirection = previous, nextDirection = next, color = colors[i] });
                if (metric == DistanceMetric.Sharp || metric == DistanceMetric.Bevel)
                    features.Add(new Feature { a = path[i], b = path[i], previousDirection = previous, nextDirection = direction, vertex = !closed && i == 0 ? 2 : 1 });
            }
            if (!closed && (metric == DistanceMetric.Sharp || metric == DistanceMetric.Bevel))
            {
                float2 direction = math.normalizesafe(path[n - 1] - path[n - 2]);
                features.Add(new Feature { a = path[n - 1], b = path[n - 1], previousDirection = direction, nextDirection = direction, vertex = 3 });
            }
        }

        sealed class FeatureComparer : IComparer<Feature>
        {
            public int axis;
            public int Compare(Feature a, Feature b) => (a.a[axis] + a.b[axis]).CompareTo(b.a[axis] + b.b[axis]);
        }

        static int BuildTree(Feature[] features, int start, int count, List<Node> nodes)
        {
            float2 minimum = new float2(float.MaxValue), maximum = new float2(-float.MaxValue);
            for (int i = start; i < start + count; i++) { minimum = math.min(minimum, features[i].Minimum); maximum = math.max(maximum, features[i].Maximum); }
            int index = nodes.Count;
            nodes.Add(new Node { minimum = minimum, maximum = maximum, start = start, count = count });
            if (count > 8)
            {
                Array.Sort(features, start, count, new FeatureComparer { axis = maximum.x - minimum.x > maximum.y - minimum.y ? 0 : 1 });
                BuildTree(features, start, count / 2, nodes);
                int right = BuildTree(features, start + count / 2, count - count / 2, nodes);
                nodes[index] = new Node { minimum = minimum, maximum = maximum, count = 0, right = right };
            }
            return index;
        }

        internal static float Median(float3 v) => math.max(math.min(v.x, v.y), math.min(math.max(v.x, v.y), v.z));

        [BurstCompile]
        struct ScalarJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> input;
            [ReadOnly] public NativeArray<Feature> features;
            [ReadOnly] public NativeArray<Node> nodes;
            [ReadOnly] public NativeArray<int> roots;
            [ReadOnly] public NativeArray<float> rootCosines;
            [WriteOnly] public NativeArray<float> output;
            public int width, channel;
            public byte threshold;
            public DistanceMetric metric;
            public float miterLimit;
            public void Execute(int index)
            {
                bool inside = DistanceFieldSource.IsObject(input[index], channel, threshold);
                float2 point = new float2(index % width, index / width);
                float4 result = metric == DistanceMetric.MTSDF ? EvaluateContours(point, features, nodes, roots, rootCosines, inside)
                    : Evaluate(point, features, nodes, metric, miterLimit, inside);
                output[index] = metric == DistanceMetric.MTSDF ? Median(result.xyz) : result.x;
            }
        }

        [BurstCompile]
        struct MultiJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> input;
            [ReadOnly] public NativeArray<Feature> features;
            [ReadOnly] public NativeArray<Node> nodes;
            [ReadOnly] public NativeArray<int> roots;
            [ReadOnly] public NativeArray<float> rootCosines;
            [WriteOnly] public NativeArray<float4> output;
            public int width, channel;
            public byte threshold;
            public void Execute(int index) => output[index] = EvaluateContours(new float2(index % width, index / width), features, nodes,
                roots, rootCosines, DistanceFieldSource.IsObject(input[index], channel, threshold));
        }

        [BurstCompile]
        struct CorrectionFlagsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> field;
            [WriteOnly] public NativeArray<byte> flags;
            public int width, height;
            public void Execute(int index)
            {
                int x = index % width, y = index / width;
                bool correct = false;
                for (int cy = y - 1; cy <= y; cy++) for (int cx = x - 1; cx <= x; cx++)
                {
                    if (cx < 0 || cy < 0 || cx + 1 >= width || cy + 1 >= height) continue;
                    float4 a = field[cy * width + cx], b = field[cy * width + cx + 1];
                    float4 c = field[(cy + 1) * width + cx], d = field[(cy + 1) * width + cx + 1];
                    float sign = a.w < 0 ? -1 : 1;
                    // True distance certifies an entire cell away from the contour.
                    // Preserve the near-boundary corner band instead of rounding it.
                    if (math.min(math.min(sign * a.w, sign * b.w), math.min(sign * c.w, sign * d.w)) <= 1) continue;
                    float3 common = math.min(math.min(sign * a.xyz, sign * b.xyz), math.min(sign * c.xyz, sign * d.xyz));
                    // Two common positive channels guarantee a positive median for
                    // EVERY bilinear phase, not merely a few sampled phases.
                    if (math.select(0, 1, common.x > 0) + math.select(0, 1, common.y > 0) + math.select(0, 1, common.z > 0) < 2)
                        correct = true;
                }
                flags[index] = (byte)(correct ? 1 : 0);
            }
        }

        [BurstCompile]
        struct CorrectColorsJob : IJobParallelFor
        {
            public NativeArray<float4> field;
            [ReadOnly] public NativeArray<byte> flags;
            public void Execute(int index)
            {
                if (flags[index] == 0) return;
                float4 value = field[index];
                // Retain this artistic scalar exactly, and never alter true-distance
                // alpha. Only incompatible RGB color encoding is removed at a seam.
                value.xyz = new float3(Median(value.xyz));
                field[index] = value;
            }
        }

        static float4 EvaluateContours(float2 p, NativeArray<Feature> features, NativeArray<Node> nodes,
            NativeArray<int> roots, NativeArray<float> cosines, bool inside)
        {
            float sign = inside ? -1 : 1;
            float4 best = new float4(sign * EmptyDistance);
            float bestScalar = EmptyDistance, trueDistance = EmptyDistance;
            for (int i = 0; i < roots.Length; i++)
            {
                Node node = nodes[roots[i]];
                float bound = math.length(math.max(math.max(node.minimum - p, p - node.maximum), 0));
                if (bound > trueDistance && bound * cosines[i] > bestScalar) continue;
                // Keep a contour's color channels together. Mixing channels of unrelated
                // contours before resolving the median makes their Voronoi switch pop.
                float4 value = Evaluate(p, features, nodes, DistanceMetric.MTSDF, 4, inside, roots[i]);
                trueDistance = math.min(trueDistance, math.abs(value.w));
                float scalar = math.abs(Median(value.xyz));
                if (scalar < bestScalar) { bestScalar = scalar; best = value; }
            }
            best.w = sign * trueDistance;
            return best;
        }

        static float4 Evaluate(float2 p, NativeArray<Feature> features, NativeArray<Node> nodes,
            DistanceMetric metric, float miterLimit, bool inside, int root = 0)
        {
            if (nodes.Length == 0) return new float4(inside ? -EmptyDistance : EmptyDistance);
            float best = EmptyDistance, trueDistance = EmptyDistance;
            float3 channelDistance = new float3(EmptyDistance), pseudo = new float3(EmptyDistance), alignment = new float3(-1);
            var stack = new FixedList512Bytes<int>();
            stack.Add(root);
            while (stack.Length > 0)
            {
                int last = stack.Length - 1, nodeId = stack[last]; stack.RemoveAt(last);
                Node node = nodes[nodeId];
                float2 gap = math.max(math.max(node.minimum - p, p - node.maximum), 0);
                float bound = metric == DistanceMetric.Diamond ? gap.x + gap.y
                    : metric == DistanceMetric.Square ? math.cmax(gap) : math.length(gap);
                if (metric == DistanceMetric.Sharp) bound /= miterLimit;
                if (bound > (metric == DistanceMetric.MTSDF ? math.cmax(channelDistance) : best) + .00001f) continue;
                if (node.count == 0)
                {
                    int left = nodeId + 1, right = node.right;
                    float leftGap = math.lengthsq(math.max(math.max(nodes[left].minimum - p, p - nodes[left].maximum), 0));
                    float rightGap = math.lengthsq(math.max(math.max(nodes[right].minimum - p, p - nodes[right].maximum), 0));
                    stack.Add(leftGap < rightGap ? right : left); stack.Add(leftGap < rightGap ? left : right);
                    continue;
                }
                for (int i = node.start; i < node.start + node.count; i++)
                {
                    Feature f = features[i];
                    float2 r = p - f.a;
                    if (f.vertex != 0)
                    {
                        if (f.vertex == 4)
                        {
                            float pointDistance = math.length(r);
                            if (metric == DistanceMetric.MTSDF)
                            {
                                trueDistance = math.min(trueDistance, pointDistance);
                                for (int c = 0; c < 3; c++) if (pointDistance < channelDistance[c])
                                { channelDistance[c] = pointDistance; pseudo[c] = (inside ? -1 : 1) * pointDistance; alignment[c] = 1; }
                            }
                            else best = math.min(best, metric == DistanceMetric.Diamond || metric == DistanceMetric.Square ? Norm(r, metric) : pointDistance);
                            continue;
                        }
                        if (f.vertex != 2 && math.dot(r, f.previousDirection) < 0 || f.vertex != 3 && math.dot(r, f.nextDirection) > 0) continue;
                        float2 n1 = new float2(f.previousDirection.y, -f.previousDirection.x), n2 = new float2(f.nextDirection.y, -f.nextDirection.x);
                        float turn = f.previousDirection.x * f.nextDirection.y - f.previousDirection.y * f.nextDirection.x;
                        // Convex joins expand the outside; concave joins contract the inside.
                        // An acute join's valid fan also includes points behind ONE support line.
                        if (f.vertex == 1 && math.abs(turn) > .00001f && inside != (turn < 0)) continue;
                        float2 bisector = math.normalizesafe(n1 + n2);
                        float cosine = math.dot(n1, bisector);
                        float distance;
                        if (cosine < .0001f || math.dot(f.previousDirection, f.nextDirection) > .99999f) distance = math.length(r);
                        else if (metric == DistanceMetric.Bevel || 1f / cosine > miterLimit)
                            distance = math.abs(math.dot(r, bisector)) / cosine;
                        else distance = inside
                            ? math.max(-math.dot(r, n1), -math.dot(r, n2))
                            : math.max(math.dot(r, n1), math.dot(r, n2));
                        best = math.min(best, distance);
                        continue;
                    }
                    float2 delta = f.b - f.a;
                    float len2 = math.lengthsq(delta), t = math.dot(r, delta) / len2;
                    float2 direction = delta * math.rsqrt(len2);
                    // Use the shared endpoint exactly. Reconstructing B as A+delta gives
                    // adjacent edges different rounding errors far from the contour.
                    float euclidean = math.length(t <= 0 ? r : t >= 1 ? p - f.b : r - t * delta);
                    if (metric == DistanceMetric.MTSDF)
                    {
                        trueDistance = math.min(trueDistance, euclidean);
                        float2 normal = new float2(direction.y, -direction.x);
                        float signed = math.dot(r, normal);
                        // Perpendicular extension belongs only to this endpoint's corner fan.
                        // Else retain finite endpoint distance: an infinite supporting line
                        // must not invent a remote contour in two shared color channels.
                        float perp = signed;
                        if (t < 0 || t > 1)
                        {
                            float2 incoming = t < 0 ? f.previousDirection : direction;
                            float2 outgoing = t < 0 ? direction : f.nextDirection;
                            float2 endpointDelta = t < 0 ? r : p - f.b;
                            float turn = incoming.x * outgoing.y - incoming.y * outgoing.x;
                            bool cornerFan = math.abs(turn) > .00001f && inside == (turn < 0) &&
                                math.dot(endpointDelta, incoming) >= 0 && math.dot(endpointDelta, outgoing) <= 0;
                            if (!cornerFan) perp = (signed < 0 ? -1 : 1) * euclidean;
                        }
                        // Shared endpoint ties select the support plane on the source's
                        // signed side, not the plane with the largest absolute projection.
                        float edgeAlignment = euclidean > .00001f ? (inside ? -1 : 1) * perp / euclidean : 1;
                        for (int c = 0; c < 3; c++)
                            if ((f.color & (1 << c)) != 0 && (euclidean < channelDistance[c] - .00001f ||
                                (math.abs(euclidean - channelDistance[c]) <= .00001f && edgeAlignment > alignment[c])))
                            { channelDistance[c] = euclidean; pseudo[c] = perp; alignment[c] = edgeAlignment; }
                    }
                    else if (metric == DistanceMetric.Diamond || metric == DistanceMetric.Square)
                        best = math.min(best, SegmentMetric(r, delta, metric));
                    else if (t >= 0 && t <= 1) best = math.min(best, euclidean);
                }
            }
            float sign = inside ? -1 : 1;
            if (metric != DistanceMetric.MTSDF) return new float4(sign * best);
            for (int c = 0; c < 3; c++) if (channelDistance[c] == EmptyDistance) pseudo[c] = sign * trueDistance;
            // Prevent incompatible colored branches from inventing a contour between islands.
            float median = Median(pseudo);
            if (median * sign < 0 || math.abs(median) < .00001f && trueDistance > .5f)
                pseudo = new float3(sign * trueDistance);
            return new float4(pseudo, sign * trueDistance);
        }

        static float SegmentMetric(float2 r, float2 delta, DistanceMetric metric)
        {
            float best = math.min(Norm(r, metric), Norm(r - delta, metric));
            if (math.abs(delta.x) > 1e-12f) best = math.min(best, Norm(r - math.saturate(r.x / delta.x) * delta, metric));
            if (math.abs(delta.y) > 1e-12f) best = math.min(best, Norm(r - math.saturate(r.y / delta.y) * delta, metric));
            if (metric == DistanceMetric.Square)
            {
                if (math.abs(delta.x - delta.y) > 1e-12f) best = math.min(best, Norm(r - math.saturate((r.x - r.y) / (delta.x - delta.y)) * delta, metric));
                if (math.abs(delta.x + delta.y) > 1e-12f) best = math.min(best, Norm(r - math.saturate((r.x + r.y) / (delta.x + delta.y)) * delta, metric));
            }
            return best;
        }
        static float Norm(float2 r, DistanceMetric metric) => metric == DistanceMetric.Diamond ? math.csum(math.abs(r)) : math.cmax(math.abs(r));
    }
}
