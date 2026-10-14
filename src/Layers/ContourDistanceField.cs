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
        // Raw interpolated threshold segments only: no fitting, union, corner
        // reconstruction or simplification. Pixel centers have integer coordinates.
        internal static void ComputeContour(NativeArray<Color32> input, NativeArray<float> output,
            int width, int height, byte threshold, int channel)
        {
            var features = new List<Feature>();
            int rowsPerBatch = math.max(1, DistanceContourCells.CellsPerBatch / (width - 1));
            using (DistanceFieldTiming.Measure("contour.extract"))
            for (int firstRow = 0; firstRow + 1 < height; firstRow += rowsPerBatch)
            {
                int rowCount = math.min(rowsPerBatch, height - 1 - firstRow);
                using var cells = DistanceContourCells.Extract(input, width, height, threshold, channel, firstRow, rowCount);
                var reader = cells.AsReader();
                for (int row = 0; row < rowCount; row++)
                {
                    int count = reader.BeginForEachIndex(row);
                    for (int i = 0; i < count; i++)
                    {
                        var cell = reader.Read<DistanceContourCell>();
                        float a = cell.values.x, b = cell.values.y, c = cell.values.z, d = cell.values.w;
                        int x = cell.x, y = cell.y;
                        var points = new FixedList64Bytes<float2>();
                        if ((a > 0) != (b > 0)) points.Add(new float2(x + a / (a - b), y));
                        if ((b > 0) != (c > 0)) points.Add(new float2(x + 1, y + b / (b - c)));
                        if ((c > 0) != (d > 0)) points.Add(new float2(x + d / (d - c), y + 1));
                        if ((d > 0) != (a > 0)) points.Add(new float2(x, y + a / (a - d)));
                        if (points.Length == 2)
                            features.Add(new Feature { a = points[0], b = points[1] });
                        else if (points.Length == 4)
                        {
                            // Bilinear asymptotic decider; exact saddles use a
                            // deterministic pairing, shared with the other contours.
                            bool pairAdjacent = a * c - b * d >= 0;
                            features.Add(new Feature { a = points[0], b = points[pairAdjacent ? 1 : 3] });
                            features.Add(new Feature { a = points[pairAdjacent ? 2 : 1], b = points[pairAdjacent ? 3 : 2] });
                        }
                        if (features.Count > 2000000)
                            throw new InvalidOperationException("Distance contour exceeds two million segments. Reduce source detail or resolution.");
                    }
                    reader.EndForEachIndex();
                }
            }
            var array = features.ToArray();
            var tree = new List<Node>();
            using (DistanceFieldTiming.Measure("geometry.bvhUpload"))
                if (array.Length > 0) BuildTree(array, 0, array.Length, tree);
            using var segments = new NativeArray<Feature>(array, Allocator.TempJob);
            using var nodes = new NativeArray<Node>(tree.ToArray(), Allocator.TempJob);
            using (DistanceFieldTiming.Measure("distance.jobs"))
                new ContourDistanceJob { input = input, output = output, features = segments,
                    nodes = nodes, width = width, threshold = threshold, channel = channel }
                    .Schedule(output.Length, 64).Complete();
        }

        [BurstCompile]
        struct ContourDistanceJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> input;
            [ReadOnly] public NativeArray<Feature> features;
            [ReadOnly] public NativeArray<Node> nodes;
            [WriteOnly] public NativeArray<float> output;
            public int width, channel;
            public byte threshold;

            public void Execute(int index)
            {
                float2 p = new float2(index % width, index / width);
                float bestSquared = EmptyDistance * EmptyDistance;
                var stack = new FixedList512Bytes<int>();
                if (nodes.Length > 0) stack.Add(0);
                while (stack.Length > 0)
                {
                    int last = stack.Length - 1, id = stack[last]; stack.RemoveAt(last);
                    Node node = nodes[id];
                    float bound = math.lengthsq(math.max(math.max(node.minimum - p, p - node.maximum), 0));
                    if (bound > bestSquared) continue;
                    if (node.count == 0)
                    {
                        int left = id + 1, right = node.right;
                        float leftBound = math.lengthsq(math.max(math.max(nodes[left].minimum - p, p - nodes[left].maximum), 0));
                        float rightBound = math.lengthsq(math.max(math.max(nodes[right].minimum - p, p - nodes[right].maximum), 0));
                        stack.Add(leftBound < rightBound ? right : left);
                        stack.Add(leftBound < rightBound ? left : right);
                        continue;
                    }
                    for (int i = node.start; i < node.start + node.count; i++)
                    {
                        Feature f = features[i];
                        float2 delta = f.b - f.a, r = p - f.a;
                        float len2 = math.lengthsq(delta);
                        float t = len2 > 0 ? math.dot(r, delta) / len2 : 0;
                        // Keep both finite endpoints, including zero-length
                        // threshold contacts. Never extend a supporting line.
                        float2 gap = t <= 0 ? r : t >= 1 ? p - f.b : r - t * delta;
                        bestSquared = math.min(bestSquared, math.lengthsq(gap));
                    }
                }
                float distance = math.sqrt(bestSquared);
                output[index] = DistanceFieldSource.IsObject(input[index], channel, threshold) ? -distance : distance;
            }
        }
    }
}
