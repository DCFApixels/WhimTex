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
    internal static partial class ArtisticDistanceField
    {
        internal const float EmptyDistance = 1e10f;
        const int UnresolvedContour = 8;
        const int NoStartExtension = 16, NoEndExtension = 32;
        const float RasterMiterLimit = 4;

        // Temporary comparison policy: disable visual artifact rejection for
        // sharp prototypes, retaining finite geometry and numerical validity.
        internal static bool UnrestrictedCorners(DistanceMetric metric) =>
            metric == DistanceMetric.Sharp || metric == DistanceMetric.MTSDF ||
            metric == DistanceMetric.CornerFit || metric == DistanceMetric.Spring || metric == DistanceMetric.Rbf || metric == DistanceMetric.RbfDual;

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

        internal struct CornerParameters
        {
            public float2 incoming, outgoing, normal1, normal2, bisector;
            public float cosine, miterRatio;
            public int valid, inside;
        }

        internal sealed class Geometry : IDisposable
        {
            public SharpDistanceMode sharpMode;
            public NativeArray<Feature> features;
            public NativeArray<Node> nodes;
            public NativeArray<int> roots;
            public NativeArray<float> rootCosines;
            public NativeArray<byte> signChanges;
            public NativeArray<int2> cornerIndices;
            public NativeArray<CornerParameters> corners;
            internal long ByteCount => (long)features.Length * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<Feature>()
                + (long)nodes.Length * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<Node>()
                + (long)roots.Length * sizeof(int) + (long)rootCosines.Length * sizeof(float) + signChanges.Length
                + (long)cornerIndices.Length * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<int2>()
                + (long)corners.Length * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<CornerParameters>();
            public void Dispose()
            {
                if (features.IsCreated) features.Dispose();
                if (nodes.IsCreated) nodes.Dispose();
                if (roots.IsCreated) roots.Dispose();
                if (rootCosines.IsCreated) rootCosines.Dispose();
                if (signChanges.IsCreated) signChanges.Dispose();
                if (cornerIndices.IsCreated) cornerIndices.Dispose();
                if (corners.IsCreated) corners.Dispose();
            }
        }

        public static void Compute(NativeArray<Color32> input, NativeArray<float> output,
            int width, int height, byte threshold, int channel, DistanceMetric metric, float miterLimit,
            float contourMergeDistance = 0, SharpDistanceMode sharpMode = SharpDistanceMode.Unrestricted)
        {
            if (metric == DistanceMetric.MTSDF)
            {
                using var field = new NativeArray<float4>(output.Length,Allocator.TempJob);
                ComputeMultiChannel(input,field,width,height,threshold,channel,contourMergeDistance,sharpMode);
                new MedianJob { field=field,output=output }.Schedule(output.Length,64).Complete();
                return;
            }
            using var geometry = Build(input, width, height, threshold, channel, metric, contourMergeDistance, sharpMode);
            ComputePrepared(geometry, input, output, width, threshold, channel, metric, miterLimit);
        }

        internal static Geometry Prepare(NativeArray<Color32> input, int width, int height,
            byte threshold, int channel, DistanceMetric metric, SharpDistanceMode sharpMode = SharpDistanceMode.Unrestricted)
        {
            if (metric != DistanceMetric.CornerFit && metric != DistanceMetric.Spring)
                throw new ArgumentOutOfRangeException(nameof(metric));
            using var timing = DistanceFieldTiming.Measure("reconstruction.prepare");
            return BuildAllocated(input, width, height, threshold, channel, metric, 0, Allocator.Persistent, sharpMode);
        }

        internal static void ComputePrepared(Geometry geometry, NativeArray<Color32> input, NativeArray<float> output,
            int width, byte threshold, int channel, DistanceMetric metric, float miterLimit)
        {
            using var timing = DistanceFieldTiming.Measure("distance.jobs");
            float minimumCosine = 1f / 64;
            if (UnrestrictedCorners(metric))
            {
                minimumCosine = 1;
                foreach (var corner in geometry.corners)
                    if (corner.valid != 0) minimumCosine = math.min(minimumCosine, corner.cosine);
            }
            new ScalarJob { input = input, output = output, features = geometry.features,
                nodes = geometry.nodes, roots = geometry.roots, rootCosines = geometry.rootCosines,
                signChanges = geometry.signChanges,
                cornerIndices = geometry.cornerIndices, corners = geometry.corners,
                width = width, threshold = threshold, channel = channel,
                metric = metric, minimumCosine = minimumCosine, unrestricted = geometry.sharpMode == SharpDistanceMode.Unrestricted,
                miterLimit = math.max(1, miterLimit) }.Schedule(output.Length, 64).Complete();
        }

        public static void ComputeMultiChannel(NativeArray<Color32> input, NativeArray<float4> output,
            int width, int height, byte threshold, int channel, float contourMergeDistance = 0,
            SharpDistanceMode sharpMode = SharpDistanceMode.Unrestricted)
        {
            using var geometry = Build(input, width, height, threshold, channel, DistanceMetric.MTSDF, contourMergeDistance, sharpMode);
            long stage = DistanceFieldTiming.Start();
            new MultiJob { input = input, output = output, features = geometry.features,
                nodes = geometry.nodes, roots = geometry.roots, rootCosines = geometry.rootCosines,
                signChanges = geometry.signChanges,
                width = width, threshold = threshold, channel = channel, unrestricted = sharpMode == SharpDistanceMode.Unrestricted
            }.Schedule(output.Length, 64).Complete();
            DistanceFieldTiming.Report("distance.jobs", ref stage);
            if (sharpMode == SharpDistanceMode.GuardedSharp)
            {
                CorrectMtsdfQuality(output, geometry, width, height);
                DistanceFieldTiming.Report("mtsdf.correct", ref stage);
            }
        }

        struct Link { public int a, b; }
        // Nearly every contour vertex has two links. Keep those inline instead
        // of allocating a List and backing array for every threshold crossing.
        struct VertexLinks
        {
            int first, second, count;
            List<int> extra;
            public int Count => count;
            public void Add(int link)
            {
                if (count == 0) first = link;
                else if (count == 1) second = link;
                else { extra ??= new List<int>(2); extra.Add(link); }
                count++;
            }
            public void Clear() { count = 0; extra?.Clear(); }
            public Enumerator GetEnumerator() => new Enumerator(this);
            public struct Enumerator
            {
                readonly VertexLinks links;
                int index;
                internal Enumerator(VertexLinks links) { this.links = links; index = -1; }
                public bool MoveNext() => ++index < links.count;
                public int Current => index == 0 ? links.first : index == 1 ? links.second : links.extra[index - 2];
            }
        }
        sealed class Vertex
        {
            public float2 point;
            public VertexLinks links;
        }

        static Geometry Build(NativeArray<Color32> input, int width, int height,
            byte threshold, int channel, DistanceMetric metric, float contourMergeDistance = 0,
            SharpDistanceMode sharpMode = SharpDistanceMode.Unrestricted)
            => BuildAllocated(input, width, height, threshold, channel, metric, contourMergeDistance, Allocator.TempJob, sharpMode);

        static Geometry BuildAllocated(NativeArray<Color32> input, int width, int height,
            byte threshold, int channel, DistanceMetric metric, float contourMergeDistance, Allocator allocator,
            SharpDistanceMode sharpMode = SharpDistanceMode.Unrestricted)
        {
            bool reconstructed = metric == DistanceMetric.CornerFit || metric == DistanceMetric.Spring;
            bool adaptiveSharp = metric == DistanceMetric.Sharp || metric == DistanceMetric.MTSDF;
            bool unrestricted = UnrestrictedCorners(metric) && sharpMode == SharpDistanceMode.Unrestricted;
            var verifiedCorners = reconstructed ? new HashSet<float2>() : null;
            var reliableSharp = adaptiveSharp ? new HashSet<int2>() : null;
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
            bool hasOpenContours = false;
            bool useAdaptiveSharp = false;
            foreach (var vertex in vertices)
                if (vertex.links.Count == 0)
                    features.Add(new Feature { a = vertex.point, b = vertex.point, vertex = 4, color = 7 });
            if (features.Count > 0) ranges.Add(new int2(0, features.Count));
            void Append(List<float2> path, bool closed)
            {
                int first = features.Count;
                AddFeatures(path, closed, reconstructed ? DistanceMetric.Sharp : metric, features, unrestricted,
                    useAdaptiveSharp && closed ? reliableSharp : null);
                if (reconstructed && !unrestricted)
                    for (int i = first; i < features.Count; i++)
                    {
                        Feature f = features[i];
                        // Only inverse-raster-verified vertices can produce sharp
                        // extensions. A safe raw fallback stays finite/rounded.
                        f.color |= NoStartExtension | NoEndExtension;
                        if (closed && verifiedCorners.Contains(f.a)) f.color &= ~NoStartExtension;
                        if (closed && verifiedCorners.Contains(f.b)) f.color &= ~NoEndExtension;
                        features[i] = f;
                    }
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
                    if (closed && path.Count >= 3 && math.abs(SignedArea(path)) > 1e-8f) contours.Add(path);
                    // Zero-area threshold contours are lines/point contacts, not filled
                    // polygons. A boolean region operation must not erase their boundary.
                    else
                    {
                        hasOpenContours |= !closed;
                        Append(reconstructed ? path : Simplify(path, closed, metric), closed);
                    }
                }
            }
            // Closed-loop coverage alone cannot explain an open foreground that
            // crosses the canvas edge. Keep the raw geometry in that case.
            useAdaptiveSharp = adaptiveSharp && !hasOpenContours;
            var fittedContours = useAdaptiveSharp ? contours : reconstructed ? hasOpenContours ? contours : ReconstructRasterContours(contours, input, width, height,
                threshold, channel, metric, verifiedCorners) : FitContours(contours, metric);
            // Fitting separate raster components can make their reconstructed regions
            // overlap. Only the boundary of the filled union may generate joins.
            DistanceFieldTiming.Report("contour.traceFit", ref stage);
            var normalizedContours = reconstructed ? fittedContours : NormalizeContours(fittedContours);
            if (!adaptiveSharp && !reconstructed && normalizedContours.Count < fittedContours.Count && metric != DistanceMetric.EuclideanAntialiased)
            {
                // Merging a detached AA tip can leave a tiny cap between its
                // supported flanks. Stabilize that joined boundary once, with
                // convex cap enclosure rather than erasing covered samples.
                normalizedContours = NormalizeContours(FitContoursCore(normalizedContours, metric, true));
            }
            if (contourMergeDistance > 0 && (metric == DistanceMetric.Sharp || metric == DistanceMetric.MTSDF))
                normalizedContours = MergeCloseVerticesPhysical(normalizedContours, contourMergeDistance);
            if (useAdaptiveSharp)
                normalizedContours = ImproveSharpContours(normalizedContours, reliableSharp);
            foreach (var path in normalizedContours) Append(path, true);
            DistanceFieldTiming.Report("contour.normalizeFeatures", ref stage);
            var array = features.ToArray();
            var nodes = new List<Node>();
            var roots = new List<int>(); var cosines = new List<float>();
            if (metric == DistanceMetric.MTSDF || metric == DistanceMetric.Sharp || reconstructed)
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
            var result = new Geometry { sharpMode = sharpMode };
            try
            {
                result.features = new NativeArray<Feature>(array, allocator);
                result.nodes = new NativeArray<Node>(nodes.ToArray(), allocator);
                result.roots = new NativeArray<int>(roots.ToArray(), allocator);
                result.rootCosines = new NativeArray<float>(cosines.ToArray(), allocator);
                result.signChanges = reconstructed || useAdaptiveSharp
                    ? new NativeArray<byte>(ReconstructedSignChanges(contours, normalizedContours, width, height), allocator)
                    : new NativeArray<byte>(0, allocator);
                PrepareCornerParameters(result.features, reconstructed || metric == DistanceMetric.Sharp,
                    allocator, out result.cornerIndices, out result.corners, unrestricted: true);
                DistanceFieldTiming.Report("geometry.bvhUpload", ref stage);
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        static void PrepareCornerParameters(NativeArray<Feature> features, bool reconstructed, Allocator allocator,
            out NativeArray<int2> indices, out NativeArray<CornerParameters> corners, bool unrestricted = false)
        {
            indices = default; corners = default;
            using var timing = DistanceFieldTiming.Measure("geometry.cornerParameters");
            var map = new int2[reconstructed ? features.Length : 0];
            int count = 0;
            for (int i = 0; i < map.Length; i++)
            {
                Feature f = features[i]; int2 pair = new int2(-1);
                if (f.vertex == 0 && (f.color & UnresolvedContour) == 0)
                {
                    if ((f.color & NoStartExtension) == 0) pair.x = count++;
                    if ((f.color & NoEndExtension) == 0) pair.y = count++;
                }
                map[i] = pair;
            }
            try
            {
                indices = new NativeArray<int2>(map, allocator);
                corners = new NativeArray<CornerParameters>(count, allocator);
                if (count > 0) new CornerParametersJob { features = features, indices = indices, corners = corners, unrestricted = unrestricted }
                    .Schedule(features.Length, 64).Complete();
            }
            catch
            {
                if (indices.IsCreated) indices.Dispose(); indices = default;
                if (corners.IsCreated) corners.Dispose(); corners = default;
                throw;
            }
        }

        [BurstCompile]
        struct CornerParametersJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Feature> features;
            [ReadOnly] public NativeArray<int2> indices;
            // Compact indices are distinct for each feature/end; writes cannot overlap.
            [NativeDisableParallelForRestriction]
            public NativeArray<CornerParameters> corners;
            public bool unrestricted;
            public void Execute(int index)
            {
                int2 pair = indices[index];
                if (pair.x < 0 && pair.y < 0) return;
                Feature f = features[index]; float2 delta = f.b - f.a; float lengthSquared = math.lengthsq(delta);
                if (lengthSquared <= 1e-12f) return;
                float2 direction = delta * math.rsqrt(lengthSquared);
                if (pair.x >= 0) corners[pair.x] = Make(f.previousDirection, direction, unrestricted);
                if (pair.y >= 0) corners[pair.y] = Make(direction, f.nextDirection, unrestricted);
            }
            static CornerParameters Make(float2 incoming, float2 outgoing, bool unrestricted)
            {
                float turn = incoming.x * outgoing.y - incoming.y * outgoing.x;
                float2 normal1 = new float2(incoming.y, -incoming.x), normal2 = new float2(outgoing.y, -outgoing.x);
                float2 bisector = math.normalizesafe(normal1 + normal2);
                float cosine = unrestricted ? math.length(normal1 + normal2) * .5f : math.dot(normal1, bisector);
                // The unrestricted prototype keeps only a numerical degeneracy floor.
                if (unrestricted ? turn == 0 || cosine < 1e-6f
                    : math.abs(turn) <= .00001f || cosine < 1f / 64) return default;
                return new CornerParameters { incoming = incoming, outgoing = outgoing, normal1 = normal1, normal2 = normal2,
                    bisector = bisector, cosine = cosine, miterRatio = 1 / cosine, valid = 1, inside = turn < 0 ? 1 : 0 };
            }
        }

        static List<List<float2>> ReconstructRasterContours(List<List<float2>> contours, NativeArray<Color32> input,
            int width, int height, byte threshold, int channel, DistanceMetric metric, HashSet<float2> verified)
        {
            var initial = new List<List<float2>>(contours);
            if (metric == DistanceMetric.Spring)
            {
                using var timing = DistanceFieldTiming.Measure("contour.spring");
                for (int i = 0; i < contours.Count; i++)
                {
                    // Keep the bounded spring result even when its raster coverage
                    // differs from the input. Cross-loop topology is checked below;
                    // only subsequent corner recovery uses AA coverage acceptance.
                    initial[i] = SpringDistanceContour.Refine(contours[i], input, width, height, threshold, channel);
                }
                if (!MergePreservesTopology(contours, initial)) initial = new List<List<float2>>(contours);
            }
            var fitted = new List<List<float2>>(initial);
            using (DistanceFieldTiming.Measure("contour.coverageFit"))
                for (int i = 0; i < initial.Count; i++)
                {
                    var localVerified = new HashSet<float2>();
                    fitted[i] = RasterCornerDistanceContour.Refine(initial[i], input, width, height, threshold, channel, localVerified, fitted);
                    verified.UnionWith(localVerified);
                }
            if (!MergePreservesTopology(contours, fitted))
            {
                verified.Clear();
                return contours;
            }
            return fitted;
        }

        // XOR the original and reconstructed closed-region membership. Unchanged
        // open paths at the image boundary retain their original source signs.
        static byte[] ReconstructedSignChanges(List<List<float2>> original, List<List<float2>> fitted, int width, int height)
        {
            var rows = new List<float>[height];
            void Add(List<float2> path)
            {
                for (int i = 0; i < path.Count; i++)
                {
                    float2 a = path[i], b = path[(i + 1) % path.Count];
                    if (a.y == b.y) continue;
                    int first = math.max(0, (int)math.ceil(math.min(a.y, b.y)));
                    int end = math.min(height, (int)math.ceil(math.max(a.y, b.y)));
                    for (int y = first; y < end; y++)
                    {
                        rows[y] ??= new List<float>();
                        rows[y].Add(a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y));
                    }
                }
            }
            // Normalization can merge, split, remove or reorder closed paths.
            // XOR both complete regions, not pairs assumed to share an index.
            // Identical pairs may still be skipped: they cancel in the XOR.
            for (int i = 0; i < original.Count; i++)
                if (i >= fitted.Count || !ReferenceEquals(original[i], fitted[i])) Add(original[i]);
            for (int i = 0; i < fitted.Count; i++)
                if (i >= original.Count || !ReferenceEquals(fitted[i], original[i])) Add(fitted[i]);
            var result = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                var row = rows[y];
                if (row == null) continue;
                row.Sort();
                for (int i = 0; i + 1 < row.Count; i += 2)
                    for (int x = math.max(0, (int)math.ceil(row[i])); x < math.min(width, (int)math.ceil(row[i + 1])); x++)
                        result[y * width + x] = 1;
            }
            return result;
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
                var ordered = new List<int>(vertex.links.Count);
                foreach (int link in vertex.links) ordered.Add(link);
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

        // An opt-in geometric weld, not an extrapolated corner reconstruction.
        // Each original vertex participates at most once: short edge chains must
        // not gradually collapse a whole stroke or move a tip beyond the budget.
        internal static List<List<float2>> MergeCloseVertices(List<List<float2>> contours, float distance)
        {
            if (!float.IsFinite(distance) || distance < 0 || distance > 2)
                throw new ArgumentOutOfRangeException(nameof(distance));
            return MergeCloseVerticesPhysical(contours, distance);
        }

        private static List<List<float2>> MergeCloseVerticesPhysical(List<List<float2>> contours, float distance)
        {
            if (!float.IsFinite(distance) || distance < 0 || distance > 2f * WhimTexDocument.MaximumPixelScale)
                throw new ArgumentOutOfRangeException(nameof(distance));
            if (distance == 0) return contours;
            using var timing = DistanceFieldTiming.Measure("contour.merge");
            var merged = new List<List<float2>>(contours.Count);
            bool changed = false;
            foreach (var path in contours)
            {
                int n = path.Count;
                if (n <= 3) { merged.Add(path); continue; }
                // A repeated vertex is an existing self-contact, not a short
                // detail that this local pair operation may disconnect.
                if (new HashSet<float2>(path).Count != n) { merged.Add(path); continue; }
                var candidates = new List<int>();
                for (int i = 0; i < n; i++)
                    if (math.distancesq(path[i], path[(i + 1) % n]) <= distance * distance) candidates.Add(i);
                // Geometric tie-breaks keep reversal/cyclic rotation of the same
                // contour from selecting a different set of equally short edges.
                candidates.Sort((a, b) => {
                    float2 aa = path[a], ab = path[(a + 1) % n], ba = path[b], bb = path[(b + 1) % n];
                    int order = math.distancesq(aa, ab).CompareTo(math.distancesq(ba, bb));
                    if (order != 0) return order;
                    float2 ac = (aa + ab) * .5f, bc = (ba + bb) * .5f;
                    order = ac.x.CompareTo(bc.x);
                    return order != 0 ? order : ac.y.CompareTo(bc.y);
                });
                var used = new bool[n]; var collapse = new bool[n];
                int remaining = n;
                foreach (int i in candidates)
                {
                    int next = (i + 1) % n;
                    if (remaining <= 3 || used[i] || used[next]) continue;
                    collapse[i] = used[i] = used[next] = true;
                    remaining--;
                }
                if (remaining == n) { merged.Add(path); continue; }
                var result = new List<float2>(remaining);
                for (int i = 0; i < n; i++)
                {
                    if (collapse[(i + n - 1) % n]) continue;
                    result.Add(collapse[i] ? (path[i] + path[(i + 1) % n]) * .5f : path[i]);
                }
                float before = SignedArea(path), after = SignedArea(result);
                if (before * after <= 0 || math.abs(after) < .8f * math.abs(before)) merged.Add(path);
                else { merged.Add(result); changed = true; }
            }
            if (!changed) return contours;
            // Reject the batch if a moved edge creates a contact/crossing or
            // sweeps over another component. Never union away an actual hole.
            return MergePreservesTopology(contours, merged) ? merged : contours;
        }

        static bool MergePreservesTopology(List<List<float2>> original, List<List<float2>> merged)
        {
            var edges = new List<Feature>();
            for (int c = 0; c < merged.Count; c++)
                for (int i = 0; i < merged[c].Count; i++)
                    edges.Add(new Feature { a = merged[c][i], b = merged[c][(i + 1) % merged[c].Count], vertex = c, color = i });
            var array = edges.ToArray(); var nodes = new List<Node>();
            if (array.Length > 0) BuildTree(array, 0, array.Length, nodes);
            Point64 P(float2 p) => new Point64(new PointD(p.x, p.y), 1000000);
            bool On(Point64 p, Point64 a, Point64 b) => p.X >= Math.Min(a.X,b.X) && p.X <= Math.Max(a.X,b.X) &&
                p.Y >= Math.Min(a.Y,b.Y) && p.Y <= Math.Max(a.Y,b.Y);
            bool Meets(float2 a, float2 b, float2 c, float2 d)
            {
                Point64 aa=P(a), bb=P(b), cc=P(c), dd=P(d);
                int ac=InternalClipper.CrossProductSign(aa,bb,cc), ad=InternalClipper.CrossProductSign(aa,bb,dd);
                int ca=InternalClipper.CrossProductSign(cc,dd,aa), cb=InternalClipper.CrossProductSign(cc,dd,bb);
                return ac*ad<0 && ca*cb<0 || ac==0 && On(cc,aa,bb) || ad==0 && On(dd,aa,bb) ||
                    ca==0 && On(aa,cc,dd) || cb==0 && On(bb,cc,dd);
            }
            var pending = new Stack<int>();
            for (int c = 0; c < merged.Count; c++)
            {
                if (ReferenceEquals(original[c], merged[c])) continue;
                var unchanged = new HashSet<(float2, float2)>();
                for (int i=0;i<original[c].Count;i++) unchanged.Add((original[c][i],original[c][(i+1)%original[c].Count]));
                for (int i=0;i<merged[c].Count;i++)
                {
                    float2 a=merged[c][i], b=merged[c][(i+1)%merged[c].Count];
                    if (unchanged.Contains((a,b))) continue;
                    if (math.distancesq(a,b)<1e-12f) return false;
                    float2 min=math.min(a,b), max=math.max(a,b);
                    pending.Clear(); if (nodes.Count>0) pending.Push(0);
                    while (pending.Count>0)
                    {
                        int id=pending.Pop(); Node node=nodes[id];
                        if (math.any(node.maximum<min) || math.any(node.minimum>max)) continue;
                        if (node.count==0) { pending.Push(node.right); pending.Push(id+1); continue; }
                        for (int j=node.start;j<node.start+node.count;j++)
                        {
                            Feature edge=array[j];
                            if (edge.vertex==c && edge.color==i) continue;
                            bool adjacent=edge.vertex==c && ((edge.color+1)%merged[c].Count==i || (i+1)%merged[c].Count==edge.color);
                            if (adjacent)
                            {
                                float2 common=(edge.color+1)%merged[c].Count==i?a:b;
                                float2 other=(edge.color+1)%merged[c].Count==i?edge.a:edge.b;
                                float2 own=(edge.color+1)%merged[c].Count==i?b:a;
                                if (InternalClipper.CrossProductSign(P(common),P(own),P(other))==0 && math.dot(own-common,other-common)>0) return false;
                            }
                            else if (Meets(a,b,edge.a,edge.b)) return false;
                        }
                    }
                }
                var before=QuantizeContour(original[c]); var after=QuantizeContour(merged[c]);
                Rect64 ob=InternalClipper.GetBounds(before), nb=InternalClipper.GetBounds(after);
                for (int other=0;other<original.Count;other++)
                {
                    if (other==c) continue;
                    foreach (float2 point in original[other])
                    {
                        Point64 p=P(point);
                        if (p.X<Math.Min(ob.left,nb.left) || p.X>Math.Max(ob.right,nb.right) ||
                            p.Y<Math.Min(ob.top,nb.top) || p.Y>Math.Max(ob.bottom,nb.bottom)) continue;
                        if (InternalClipper.PointInPolygon(p,before)!=InternalClipper.PointInPolygon(p,after)) return false;
                    }
                }
            }
            return true;
        }

        static List<List<float2>> FitContours(List<List<float2>> contours, DistanceMetric metric)
            => FitContoursCore(contours, metric, false);

        static List<List<float2>> FitContoursCore(List<List<float2>> contours, DistanceMetric metric, bool encloseCaps)
        {
            var holes = new List<Path64>();
            foreach (var path in contours)
                if (SignedArea(path) < 0) holes.Add(QuantizeContour(path));
            if (holes.Count > 0)
            {
                var outer = new List<Path64>();
                foreach (var path in contours)
                    if (SignedArea(path) > 0) outer.Add(QuantizeContour(path));
                // A negative ring outside every filled outline is a separate
                // component under NonZero union, not an enclosed hole. Raster
                // saddle/tip tracing can give such a component either winding.
                holes.RemoveAll(hole => !outer.Exists(path =>
                    InternalClipper.PointInPolygon(hole[0], path) != PointInPolygonResult.IsOutside));
            }
            var result = new List<List<float2>>(contours.Count);
            foreach (var path in contours)
            {
                if (encloseCaps && SignedArea(path) <= 0) { result.Add(path); continue; }
                if (holes.Count == 0 && !encloseCaps) { result.Add(Simplify(path, true, metric)); continue; }
                var protectedHoles = new List<Path64>(holes.Count);
                var own = QuantizeContour(path);
                foreach (var hole in holes)
                {
                    bool same = hole.Count == own.Count;
                    for (int i = 0; same && i < hole.Count; i++) same = hole[i] == own[i];
                    if (!same) protectedHoles.Add(hole);
                }
                var recovered = metric != DistanceMetric.EuclideanAntialiased
                    ? RecoverCornersCore(path, protectedHoles, encloseCaps) : path;
                if (protectedHoles.Count > 0 && !ReferenceEquals(path, recovered) && !FittingPreservesHoles(path, recovered, protectedHoles)) recovered = path;
                var fitted = encloseCaps ? recovered : Simplify(recovered, true, metric, false);
                // An enclosing fitted tip must not sweep across an independent
                // hole before the region union. Cleanup after union is too late.
                if (protectedHoles.Count > 0 && !FittingPreservesHoles(path, fitted, protectedHoles)) fitted = recovered;
                result.Add(fitted);
            }
            return result;
        }

        static Path64 QuantizeContour(List<float2> path)
        {
            var result = new Path64(path.Count);
            foreach (float2 p in path) result.Add(new Point64(new PointD(p.x, p.y), 1000000));
            return result;
        }

        static bool FittingPreservesHoles(List<float2> original, List<float2> fitted, List<Path64> holes)
        {
            var before = QuantizeContour(original); var after = QuantizeContour(fitted);
            Rect64 oldBounds = InternalClipper.GetBounds(before), newBounds = InternalClipper.GetBounds(after);
            foreach (var hole in holes)
            {
                Rect64 holeBounds = InternalClipper.GetBounds(hole);
                bool Disjoint(Rect64 bounds) => holeBounds.right < bounds.left || holeBounds.left > bounds.right ||
                    holeBounds.bottom < bounds.top || holeBounds.top > bounds.bottom;
                if (Disjoint(oldBounds) && Disjoint(newBounds)) continue;
                // A hole's own outline may be refined; protect other components.
                if (hole.Count == before.Count)
                {
                    bool same = true;
                    for (int i = 0; i < hole.Count; i++) if (hole[i] != before[i]) { same = false; break; }
                    if (same) continue;
                }
                foreach (Point64 p in hole)
                {
                    bool oldInsideBounds = p.X >= oldBounds.left && p.X <= oldBounds.right && p.Y >= oldBounds.top && p.Y <= oldBounds.bottom;
                    bool newInsideBounds = p.X >= newBounds.left && p.X <= newBounds.right && p.Y >= newBounds.top && p.Y <= newBounds.bottom;
                    if (!oldInsideBounds && !newInsideBounds) continue;
                    if (InternalClipper.PointInPolygon(p, before) != InternalClipper.PointInPolygon(p, after)) return false;
                }
                if (ContoursCross(before, hole) != ContoursCross(after, hole)) return false;
            }
            return true;
        }

        static bool ContoursCross(Path64 a, Path64 b)
        {
            Rect64 ab = InternalClipper.GetBounds(a), bb = InternalClipper.GetBounds(b);
            if (ab.right < bb.left || ab.left > bb.right || ab.bottom < bb.top || ab.top > bb.bottom) return false;
            for (int i = 0; i < a.Count; i++)
            {
                Point64 p = a[i], q = a[(i + 1) % a.Count];
                if (Math.Max(p.X,q.X)<bb.left || Math.Min(p.X,q.X)>bb.right ||
                    Math.Max(p.Y,q.Y)<bb.top || Math.Min(p.Y,q.Y)>bb.bottom) continue;
                for (int j = 0; j < b.Count; j++)
                {
                    Point64 r = b[j], s = b[(j + 1) % b.Count];
                    if (Math.Max(p.X, q.X) < Math.Min(r.X, s.X) || Math.Max(r.X, s.X) < Math.Min(p.X, q.X) ||
                        Math.Max(p.Y, q.Y) < Math.Min(r.Y, s.Y) || Math.Max(r.Y, s.Y) < Math.Min(p.Y, q.Y)) continue;
                    if (InternalClipper.CrossProductSign(p, q, r) * InternalClipper.CrossProductSign(p, q, s) < 0 &&
                        InternalClipper.CrossProductSign(r, s, p) * InternalClipper.CrossProductSign(r, s, q) < 0) return true;
                }
            }
            return false;
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
            public bool stableFlanks;
        }

        // Fit the raw isocontour, before RDP chooses individual edges. Arc-length
        // weighting and orthogonal least squares avoid grid-axis and short-edge bias.
        // The budgets describe raster uncertainty, not a promise to recover vectors.
        static List<float2> RecoverCorners(List<float2> raw)
            => RecoverCornersCore(raw, null);

        static List<float2> RecoverCornersCore(List<float2> raw, List<Path64> holes, bool encloseCaps = false)
        {
            using var timing = DistanceFieldTiming.Measure("contour.cornerFit");
            const float spacing = .5f, support = 8f;
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
            float2 Project(float2 p, FittedLine line) => line.center + math.dot(p - line.center, line.direction) * line.direction;
            int flank = (int)Math.Ceiling(support / step);
            var candidates = new List<int>();
            bool[] required = null;
            for (int i = 0; i < n; i++)
            {
                // Cheap curvature gate before repeated fits. A smooth arc's two
                // nearby chords cannot establish a hard turn supported by both flanks.
                float2 incoming = math.normalizesafe(At(i) - At(i - flank));
                float2 outgoing = math.normalizesafe(At(i + flank) - At(i));
                if (math.dot(incoming, outgoing) > .8f) continue;
                candidates.Add(i);
                required ??= new bool[n];
                for (int gap = 1; gap * step <= 4; gap++)
                {
                    required[((i - gap - flank) % n + n) % n] = true;
                    required[(i + gap) % n] = true;
                }
            }
            if (candidates.Count == 0) return raw;
            var fitted = DistanceContourFit.Compute(samples, flank, required);
            var proposals = new List<FittedCorner>();
            foreach (var candidate in encloseCaps ? DistanceContourFit.ProposeEnclosed(samples,fitted,candidates,flank,step)
                : DistanceContourFit.Propose(samples,fitted,candidates,flank,step))
            {
                if (candidate.score >= float.MaxValue) continue;
                var corner = new FittedCorner {
                    index = candidate.index, gap = candidate.gap, point = candidate.point, score = candidate.score,
                    stableFlanks = candidate.stableFlanks,
                    left = new FittedLine { center = candidate.left.center, direction = candidate.left.direction, error = candidate.left.error },
                    right = new FittedLine { center = candidate.right.center, direction = candidate.right.direction, error = candidate.right.error }
                };
                if (encloseCaps)
                {
                    float Cross(float2 a,float2 b) => a.x*b.y-a.y*b.x;
                    float cross = Cross(corner.left.direction,corner.right.direction);
                    if (!corner.stableFlanks || cross <= .05f) continue;
                    int start=corner.index-corner.gap-flank, end=corner.index+corner.gap+flank;
                    float from=start*step,to=end*step;
                    var exact = new List<float2> { At(start) };
                    var positions = new List<float> { from };
                    int cycle=(int)Math.Floor(from/perimeter), index=Array.BinarySearch(arc,from-cycle*perimeter);
                    index=index<0?~index:index+1;
                    if(index>=raw.Count){index=0;cycle++;}
                    while(arc[index]+cycle*perimeter<to)
                    {
                        exact.Add(raw[index]);positions.Add(arc[index]+cycle*perimeter);
                        if(++index==raw.Count){index=0;cycle++;}
                    }
                    exact.Add(At(end));positions.Add(to);
                    float2 nl=new float2(corner.left.direction.y,-corner.left.direction.x),nr=new float2(corner.right.direction.y,-corner.right.direction.x);
                    float leftExcess=0,rightExcess=0;
                    foreach(float2 p in exact)
                    {
                        leftExcess=math.max(leftExcess,math.dot(p-corner.left.center,nl));
                        rightExcess=math.max(rightExcess,math.dot(p-corner.right.center,nr));
                    }
                    // Use actual unsampled union vertices, not only arc samples.
                    // Shift supported convex walls outward minimally; never shave
                    // a covered AA fragment to obtain a cleaner offset join.
                    corner.left.center += leftExcess*nl; corner.right.center += rightExcess*nr;
                    corner.point=corner.left.center+Cross(corner.right.center-corner.left.center,corner.right.direction)/cross*corner.left.direction;
                    float2 a=Project(At(start),corner.left),b=Project(At(end),corner.right);
                    bool valid=true;
                    for(int k=0;k<exact.Count;k++)
                    {
                        float budget=positions[k]>=(corner.index-corner.gap)*step&&positions[k]<=(corner.index+corner.gap)*step ? .7071068f : .6f;
                        if(math.min(SquaredSegmentDistance(exact[k],a,corner.point),SquaredSegmentDistance(exact[k],corner.point,b))>budget*budget) valid=false;
                    }
                    float tipDistance=float.MaxValue;
                    for(int k=corner.index-corner.gap;k<corner.index+corner.gap;k++)
                        tipDistance=math.min(tipDistance,SquaredSegmentDistance(corner.point,At(k),At(k+1)));
                    if(!valid||tipDistance>flank*step*flank*step) continue;
                }
                proposals.Add(corner);
            }
            proposals.Sort((a, b) => {
                int supportOrder = b.stableFlanks.CompareTo(a.stableFlanks);
                if (supportOrder != 0) return supportOrder;
                int order = a.score.CompareTo(b.score); return order != 0 ? order : a.index.CompareTo(b.index);
            });
            var selected = new List<FittedCorner>();
            var occupied = new bool[n];
            foreach (FittedCorner proposal in proposals)
            {
                FittedCorner candidate = proposal;
                bool overlap = false;
                for (int k = -candidate.gap; k <= candidate.gap; k++) overlap |= occupied[(candidate.index + k + n) % n];
                if (overlap) continue;
                if (holes != null && holes.Count > 0)
                {
                    int start = candidate.index - candidate.gap - flank, end = candidate.index + candidate.gap + flank;
                    var patch = new List<float2> { At(start) };
                    float from = start * step, to = end * step;
                    int cycle = (int)Math.Floor(from / perimeter);
                    int index = Array.BinarySearch(arc, from - cycle * perimeter);
                    index = index < 0 ? ~index : index + 1;
                    if (index >= raw.Count) { index = 0; cycle++; }
                    while (arc[index] + cycle * perimeter < to)
                    {
                        patch.Add(raw[index]);
                        if (++index == raw.Count) { index = 0; cycle++; }
                    }
                    patch.Add(At(end));
                    int rawCount = patch.Count;
                    void UpdatePatch()
                    {
                        if (patch.Count > rawCount) patch.RemoveRange(rawCount,patch.Count-rawCount);
                        patch.Add(Project(At(end),candidate.right)); patch.Add(candidate.point);
                        patch.Add(Project(At(start),candidate.left));
                    }
                    UpdatePatch();
                    // The local swept region must not contain or touch another
                    // hole. Reject this corner only, not unrelated fitted tips.
                    var swept = QuantizeContour(patch);
                    Rect64 bounds = InternalClipper.GetBounds(swept);
                    var conflicts = new List<Path64>();
                    foreach (var hole in holes)
                    {
                        Rect64 holeBounds = InternalClipper.GetBounds(hole);
                        if (holeBounds.right < bounds.left || holeBounds.left > bounds.right ||
                            holeBounds.bottom < bounds.top || holeBounds.top > bounds.bottom) continue;
                        bool touches = ContoursCross(swept,hole);
                        foreach (Point64 p in hole)
                            touches |= InternalClipper.PointInPolygon(p,swept) != PointInPolygonResult.IsOutside;
                        if (touches) conflicts.Add(hole);
                    }
                    overlap = conflicts.Count > 0;
                    float cross = candidate.left.direction.x*candidate.right.direction.y-candidate.left.direction.y*candidate.right.direction.x;
                    if (overlap && cross < -.05f && SignedArea(raw) > 0)
                    {
                        // A supported concave corner need not become a raster cap
                        // merely because its unconstrained walls cut a nearby hole.
                        // Move the cheaper wall outward, within the same .6px
                        // envelope, to keep the entire hole on its filled side.
                        var original = QuantizeContour(raw);
                        bool valid = true;
                        foreach (var hole in conflicts)
                        {
                            float2 nl = new float2(candidate.left.direction.y,-candidate.left.direction.x);
                            float2 nr = new float2(candidate.right.direction.y,-candidate.right.direction.x);
                            float left = float.MinValue, right = float.MinValue;
                            foreach (Point64 p in hole)
                            {
                                if (InternalClipper.PointInPolygon(p,original) != PointInPolygonResult.IsInside) valid = false;
                                float2 q = new float2((float)(p.X*.000001),(float)(p.Y*.000001));
                                left = math.max(left,math.dot(q-candidate.left.center,nl));
                                right = math.max(right,math.dot(q-candidate.right.center,nr));
                            }
                            const float clearance = .001f;
                            if (left <= right) candidate.left.center += math.max(0,left+clearance)*nl;
                            else candidate.right.center += math.max(0,right+clearance)*nr;
                        }
                        candidate.point = candidate.left.center +
                            ((candidate.right.center.x-candidate.left.center.x)*candidate.right.direction.y-
                             (candidate.right.center.y-candidate.left.center.y)*candidate.right.direction.x)/cross*candidate.left.direction;
                        float2 a = Project(At(start),candidate.left), b = Project(At(end),candidate.right);
                        for (int k=0;k<rawCount;k++)
                            if (math.min(SquaredSegmentDistance(patch[k],a,candidate.point),SquaredSegmentDistance(patch[k],candidate.point,b)) > .6f*.6f) valid = false;
                        float cosine = math.sqrt(math.max(.0001f,(1+math.dot(candidate.left.direction,candidate.right.direction))*.5f));
                        float tipLimit = candidate.stableFlanks && .7071068f/cosine <= flank*step ? flank*step : math.min(2,.6f/cosine);
                        float tipDistance = float.MaxValue;
                        for (int k=candidate.index-candidate.gap;k<candidate.index+candidate.gap;k++)
                            tipDistance = math.min(tipDistance,SquaredSegmentDistance(candidate.point,At(k),At(k+1)));
                        if (tipDistance > tipLimit*tipLimit) valid = false;
                        if (valid)
                        {
                            UpdatePatch(); swept = QuantizeContour(patch);
                            overlap = false;
                            foreach (var hole in holes)
                            {
                                foreach (Point64 p in hole)
                                    overlap |= InternalClipper.PointInPolygon(p,swept) != PointInPolygonResult.IsOutside;
                                overlap |= ContoursCross(swept,hole);
                            }
                        }
                    }
                    if (overlap) continue;
                }
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

        static void AddFeatures(List<float2> path, bool closed, DistanceMetric metric, List<Feature> features, bool unrestricted = false,
            HashSet<int2> reliableSharp = null)
        {
            int n = path.Count, edgeCount = closed ? n : n - 1;
            float perimeter = 0;
            for (int i=0;i<edgeCount;i++) perimeter += math.distance(path[i],path[(i+1)%n]);
            // Preserve tiny raster components geometrically, but do not extend
            // their unresolvable corner angles into distant miter/pseudo rays.
            bool unresolved = reliableSharp == null && !unrestricted && closed && perimeter < 4;
            bool HasSupport(int index, int step, float2 direction)
            {
                float length = 0;
                for (int k=0;k<edgeCount;k++)
                {
                    int other = index+step;
                    if (!closed && (other < 0 || other >= n)) return false;
                    other = (other+n)%n;
                    float2 d = step*(path[other]-path[index]);
                    float distance = math.length(d);
                    if (distance > .000001f)
                    {
                        if (math.dot(d/distance,direction) < .995f) return false;
                        length += distance;
                        if (length >= 2) return true;
                    }
                    index = other;
                }
                return false;
            }
            var supported = new bool[n];
            if (reliableSharp != null)
                for (int i = 0; i < n; i++) supported[i] = reliableSharp.Contains(CornerKey(path[i]));
            else if (!unrestricted && !unresolved && (metric == DistanceMetric.Sharp || metric == DistanceMetric.MTSDF))
                for (int i=0;i<n;i++)
                {
                    float2 incoming = math.normalizesafe(path[i]-path[(i+n-1)%n]);
                    float2 outgoing = math.normalizesafe(path[(i+1)%n]-path[i]);
                    supported[i] = HasSupport(i,-1,incoming) && HasSupport(i,1,outgoing);
                }
            var corners = new List<int>();
            if (closed)
                for (int i = 0; i < n; i++)
                    if ((reliableSharp == null || supported[i]) && math.dot(math.normalizesafe(path[i] - path[(i + n - 1) % n]), math.normalizesafe(path[(i + 1) % n] - path[i])) < .8660254f)
                        corners.Add(i);
            int[] colors = new int[edgeCount];
            for (int i = 0; i < edgeCount; i++) colors[i] = 7;
            if (metric == DistanceMetric.MTSDF) colors = ColorDistanceContour(path, closed, corners);
            for (int i = 0; i < edgeCount; i++)
            {
                int j = (i + 1) % n;
                float2 direction = math.normalizesafe(path[j] - path[i]);
                if (math.lengthsq(direction) < .5f) continue;
                float2 previous = i > 0 || closed ? math.normalizesafe(path[i] - path[(i + n - 1) % n]) : direction;
                float2 next = j + 1 < n || closed ? math.normalizesafe(path[(j + 1) % n] - path[j]) : direction;
                int flags = (reliableSharp != null || !unrestricted) && (metric == DistanceMetric.Sharp || metric == DistanceMetric.MTSDF)
                    ? (supported[i] ? 0 : NoStartExtension) | (supported[j] ? 0 : NoEndExtension) : 0;
                features.Add(new Feature { a = path[i], b = path[j], previousDirection = previous, nextDirection = next,
                    color = (unresolved ? 7 | UnresolvedContour : colors[i]) | flags });
                if (metric == DistanceMetric.Sharp || metric == DistanceMetric.Bevel)
                    features.Add(new Feature { a = path[i], b = path[i], previousDirection = previous, nextDirection = direction,
                        color = unresolved ? UnresolvedContour : 0, vertex = !closed && i == 0 ? 2 : 1 });
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
        struct MedianJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> field;
            [WriteOnly] public NativeArray<float> output;
            public void Execute(int index) => output[index]=Median(field[index].xyz);
        }

        [BurstCompile]
        struct ScalarJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> input;
            [ReadOnly] public NativeArray<byte> signChanges;
            [ReadOnly] public NativeArray<Feature> features;
            [ReadOnly] public NativeArray<Node> nodes;
            [ReadOnly] public NativeArray<int> roots;
            [ReadOnly] public NativeArray<float> rootCosines;
            [ReadOnly] public NativeArray<int2> cornerIndices;
            [ReadOnly] public NativeArray<CornerParameters> corners;
            [WriteOnly] public NativeArray<float> output;
            public int width, channel;
            public byte threshold;
            public DistanceMetric metric;
            public float miterLimit;
            public float minimumCosine;
            public bool unrestricted;
            public void Execute(int index)
            {
                bool inside = DistanceFieldSource.IsObject(input[index], channel, threshold);
                if (signChanges.Length > 0 && signChanges[index] != 0) inside = !inside;
                float2 point = new float2(index % width, index / width);
                bool reconstructed = metric == DistanceMetric.Sharp || metric == DistanceMetric.CornerFit || metric == DistanceMetric.Spring || metric == DistanceMetric.Rbf || metric == DistanceMetric.RbfDual;
                float4 result = (metric == DistanceMetric.Sharp || metric == DistanceMetric.Rbf || metric == DistanceMetric.RbfDual) && !unrestricted
                    ? new float4((inside ? -1 : 1) * EvaluateSharpContours(point, features, nodes, roots, miterLimit, inside, 1 / minimumCosine))
                    : reconstructed ? new float4((inside ? -1 : 1) * EvaluatePreparedReconstructed(point, features, nodes, roots, cornerIndices, corners, miterLimit, inside, unrestricted, minimumCosine))
                    : metric == DistanceMetric.MTSDF ? EvaluateContours(point, features, nodes, roots, rootCosines, inside, unrestricted)
                    : Evaluate(point, features, nodes, metric, miterLimit, inside);
                output[index] = metric == DistanceMetric.MTSDF ? Median(result.xyz) : result.x;
            }
        }

        [BurstCompile]
        struct MultiJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> input;
            [ReadOnly] public NativeArray<byte> signChanges;
            [ReadOnly] public NativeArray<Feature> features;
            [ReadOnly] public NativeArray<Node> nodes;
            [ReadOnly] public NativeArray<int> roots;
            [ReadOnly] public NativeArray<float> rootCosines;
            [WriteOnly] public NativeArray<float4> output;
            public int width, channel;
            public byte threshold;
            public bool unrestricted;
            public void Execute(int index)
            {
                bool inside = DistanceFieldSource.IsObject(input[index], channel, threshold);
                if (signChanges.Length > 0 && signChanges[index] != 0) inside = !inside;
                output[index] = EvaluateContours(new float2(index % width, index / width), features, nodes,
                    roots, rootCosines, inside, unrestricted);
            }
        }

        static float4 EvaluateContours(float2 p, NativeArray<Feature> features, NativeArray<Node> nodes,
            NativeArray<int> roots, NativeArray<float> cosines, bool inside, bool unrestricted = true)
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
                float4 value = Evaluate(p, features, nodes, DistanceMetric.MTSDF, 4, inside, roots[i], unrestricted);
                trueDistance = math.min(trueDistance, math.abs(value.w));
                float scalar = math.abs(Median(value.xyz));
                if (scalar < bestScalar) { bestScalar = scalar; best = value; }
            }
            best.w = sign * trueDistance;
            return best;
        }

        static float4 Evaluate(float2 p, NativeArray<Feature> features, NativeArray<Node> nodes,
            DistanceMetric metric, float miterLimit, bool inside, int root = 0, bool unrestricted = true)
        {
            if (nodes.Length == 0) return new float4(inside ? -EmptyDistance : EmptyDistance);
            if (metric == DistanceMetric.Sharp) return new float4((inside ? -1 : 1)*EvaluateSharp(p,features,nodes,miterLimit,inside,root));
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
                        if ((f.color & UnresolvedContour) != 0)
                        {
                            best = math.min(best,math.length(r));
                            continue;
                        }
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
                    if (metric == DistanceMetric.Diamond || metric == DistanceMetric.Square)
                    {
                        best = math.min(best, SegmentMetric(r, delta, metric));
                        continue;
                    }
                    float len2 = math.lengthsq(delta), t = math.dot(r, delta) / len2;
                    if (metric == DistanceMetric.MTSDF)
                    {
                        float2 direction = delta * math.rsqrt(len2);
                        // Use the shared endpoint exactly. Reconstructing B as A+delta gives
                        // adjacent edges different rounding errors far from the contour.
                        float euclidean = math.length(t <= 0 ? r : t >= 1 ? p - f.b : r - t * delta);
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
                            float cosine = math.length(incoming + outgoing) * .5f;
                            int endpointFlag = t < 0 ? NoStartExtension : NoEndExtension;
                            bool cornerFan = (f.color & (UnresolvedContour | endpointFlag)) == 0 && cosine >= 1e-6f &&
                                turn != 0 && inside == (turn < 0) &&
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
                    else if (t >= 0 && t <= 1)
                        best = math.min(best, math.length(t <= 0 ? r : t >= 1 ? p - f.b : r - t * delta));
                }
            }
            float sign = inside ? -1 : 1;
            if (metric != DistanceMetric.MTSDF) return new float4(sign * best);
            for (int c = 0; c < 3; c++) if (channelDistance[c] == EmptyDistance) pseudo[c] = sign * trueDistance;
            // Prevent incompatible colored branches from inventing a contour between islands.
            float median = Median(pseudo);
            if (!unrestricted && (median * sign < 0 || median == 0 && trueDistance > .5f))
                pseudo = new float3(sign * trueDistance);
            return new float4(pseudo, sign * trueDistance);
        }

        // Prototype: verified corner candidates compete directly with finite
        // boundaries. Keep the angular domain and miter limit, without fading
        // a corner back to radial distance when another contour is closer.
        static float EvaluateReconstructed(float2 p, NativeArray<Feature> features, NativeArray<Node> nodes,
            NativeArray<int> roots, float miterLimit, bool inside)
        {
            PrepareCornerParameters(features, true, Allocator.TempJob, out var indices, out var corners);
            try { return EvaluatePreparedReconstructed(p, features, nodes, roots, indices, corners, miterLimit, inside); }
            finally { indices.Dispose(); corners.Dispose(); }
        }

        static float EvaluatePreparedReconstructed(float2 p, NativeArray<Feature> features, NativeArray<Node> nodes,
            NativeArray<int> roots, NativeArray<int2> indices, NativeArray<CornerParameters> corners, float miterLimit, bool inside,
            bool unrestricted = false, float minimumCosine = 1f / 64)
        {
            float best = EmptyDistance;
            var stack = new FixedList512Bytes<int>();
            for (int root = 0; root < roots.Length; root++)
            {
                stack.Add(roots[root]);
                while (stack.Length > 0)
                {
                    int last = stack.Length - 1, id = stack[last]; stack.RemoveAt(last);
                    Node node = nodes[id];
                    // A fixed /64 bound would silently discard RBF's sharper
                    // corners. Use the smallest actual cosine for its geometry.
                    if (math.length(math.max(math.max(node.minimum - p, p - node.maximum), 0)) * minimumCosine > best) continue;
                    if (node.count == 0) { stack.Add(node.right); stack.Add(id + 1); continue; }
                    for (int i = node.start; i < node.start + node.count; i++)
                    {
                        Feature f = features[i];
                        if (f.vertex != 0 && f.vertex != 4) continue;
                        float2 d = f.b - f.a, r = p - f.a;
                        float len2 = math.lengthsq(d), t = len2 > 1e-12f ? math.dot(r, d) / len2 : 0;
                        float finite = math.length(t <= 0 ? r : t >= 1 ? p - f.b : r - t * d);
                        float candidate = finite;
                        if (f.vertex == 0 && (t < 0 || t > 1))
                        {
                            int cornerIndex = t < 0 ? indices[i].x : indices[i].y;
                            if (cornerIndex >= 0)
                            {
                                CornerParameters corner = corners[cornerIndex];
                                float2 delta = t < 0 ? r : p - f.b;
                                if (corner.valid != 0 && corner.inside == (inside ? 1 : 0) &&
                                    math.dot(delta, corner.incoming) >= 0 && math.dot(delta, corner.outgoing) <= 0)
                                {
                                    candidate = !unrestricted && corner.miterRatio > miterLimit ? math.abs(math.dot(delta, corner.bisector)) / corner.cosine
                                        : inside ? math.max(-math.dot(delta, corner.normal1), -math.dot(delta, corner.normal2))
                                        : math.max(math.dot(delta, corner.normal1), math.dot(delta, corner.normal2));
                                }
                            }
                        }
                        best = math.min(best, unrestricted ? math.max(0, candidate) : candidate);
                    }
                }
            }
            return best;
        }

        static float EvaluateSharpContours(float2 p, NativeArray<Feature> features, NativeArray<Node> nodes,
            NativeArray<int> roots, float miterLimit, bool inside, float safetyLimit = RasterMiterLimit)
        {
            float best = EmptyDistance;
            for (int i = 0; i < roots.Length; i++)
            {
                Node node = nodes[roots[i]];
                float bound = math.length(math.max(math.max(node.minimum - p, p - node.maximum), 0));
                if (bound / safetyLimit > best) continue;
                // Resolve finite ownership WITHIN each closed boundary first, then
                // combine its artistic distance. Choosing one Euclidean-nearest island
                // before its join evaluation makes ordinary offset unions discontinuous.
                best = math.min(best, EvaluateSharp(p, features, nodes, miterLimit, inside, roots[i], safetyLimit));
            }
            return best;
        }

        // Select by distance to FINITE geometry first. A remote supporting line
        // must not win merely because its angular distance is very small.
        static float EvaluateSharp(float2 p, NativeArray<Feature> features, NativeArray<Node> nodes,
            float miterLimit, bool inside, int root, float safetyLimit = RasterMiterLimit)
        {
            float nearest = EmptyDistance, result = EmptyDistance;
            var stack = new FixedList512Bytes<int>(); stack.Add(root);
            while (stack.Length > 0)
            {
                int last=stack.Length-1,id=stack[last];stack.RemoveAt(last);
                Node node=nodes[id];
                if (math.length(math.max(math.max(node.minimum-p,p-node.maximum),0)) > nearest+.00001f) continue;
                if (node.count==0) { stack.Add(node.right);stack.Add(id+1);continue; }
                for (int i=node.start;i<node.start+node.count;i++)
                {
                    Feature f=features[i];
                    if (f.vertex!=0 && f.vertex!=4) continue;
                    float2 d=f.b-f.a,r=p-f.a;
                    float len2=math.lengthsq(d),t=len2>1e-12f?math.dot(r,d)/len2:0;
                    float finite=math.length(t<=0?r:t>=1?p-f.b:r-t*d);
                    if (finite > nearest+.00001f) continue;
                    float artistic=finite;
                    if (f.vertex==0 && (t<0 || t>1))
                    {
                        float2 direction=d*math.rsqrt(len2);
                        float2 incoming=t<0?f.previousDirection:direction,outgoing=t<0?direction:f.nextDirection;
                        float2 delta=t<0?r:p-f.b;
                        float turn=incoming.x*outgoing.y-incoming.y*outgoing.x;
                        float2 n1=new float2(incoming.y,-incoming.x),n2=new float2(outgoing.y,-outgoing.x);
                        float2 bisector=math.normalizesafe(n1+n2);
                        float cosine=math.length(n1+n2)*.5f;
                        int flag=t<0?NoStartExtension:NoEndExtension;
                        if ((f.color & (UnresolvedContour | flag))==0 && turn!=0 && inside==(turn<0) &&
                            cosine>=1e-6f && math.dot(delta,incoming)>=0 && math.dot(delta,outgoing)<=0)
                            artistic=1/cosine>miterLimit ? math.abs(math.dot(delta,bisector))/cosine
                                : inside ? math.max(-math.dot(delta,n1),-math.dot(delta,n2))
                                : math.max(math.dot(delta,n1),math.dot(delta,n2));
                    }
                    // Equal finite distances may involve a separate microcomponent.
                    // Prefer the conservative tie rather than inventing a new ray.
                    if (finite < nearest-.00001f) { nearest=finite;result=artistic; }
                    else { nearest=math.min(nearest,finite);result=math.max(result,artistic); }
                }
            }
            return result;
        }

        static float FiniteDistance(float2 p, NativeArray<Feature> features, NativeArray<Node> nodes, NativeArray<int> roots)
        {
            float best=EmptyDistance;
            var stack=new FixedList512Bytes<int>();
            for (int root=0;root<roots.Length;root++)
            {
                stack.Add(roots[root]);
                while(stack.Length>0)
                {
                int last=stack.Length-1,id=stack[last];stack.RemoveAt(last);Node node=nodes[id];
                if(math.length(math.max(math.max(node.minimum-p,p-node.maximum),0))>best)continue;
                if(node.count==0){stack.Add(node.right);stack.Add(id+1);continue;}
                for(int i=node.start;i<node.start+node.count;i++)
                {
                    Feature f=features[i];if(f.vertex!=0&&f.vertex!=4)continue;
                    float2 d=f.b-f.a,r=p-f.a;float len2=math.lengthsq(d),t=len2>1e-12f?math.saturate(math.dot(r,d)/len2):0;
                    best=math.min(best,math.length(t<=0?r:t>=1?p-f.b:r-t*d));
                }
                }
            }
            return best;
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
