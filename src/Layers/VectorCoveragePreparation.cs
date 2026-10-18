using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Independent row bands use the same double-precision scan intersections and sample order.
    internal static class VectorCoveragePreparation
    {
        internal struct Edge { internal double x0, y0, y1, slope; internal int winding; }
        private struct Crossing : IComparable<Crossing>
        {
            internal double x;
            internal int winding;
            public int CompareTo(Crossing other) => x.CompareTo(other.x);
        }
        private struct Span { internal double left, right; }
        private const int RowsPerJob = 32;
        private const int BandsPerBatch = 8;
        private const long MaxWork = 100000000;

        internal static void Render(Edge[] fill, Edge[] stroke, VectorRasterizer.Coverage coverage, bool step, ref long work)
            => Render(fill, stroke, coverage.texture.GetRawTextureData<Color>(), coverage.x, coverage.y,
                coverage.texture.width, coverage.texture.height, step, ref work);

        internal static void Render(Edge[] fill, Edge[] stroke, NativeArray<Color> output, int originX, int originY,
            int width, int height, bool step, ref long work)
        {
            using var measure = WhimTexProfiler.Measure("vector.mask");
            int bands = (height + RowsPerJob - 1) / RowsPerJob;
            // Temp allocations inside Execute are reclaimed at job completion, not by each
            // Dispose. Bound the entire scheduled batch, including list growth allocations.
            long scratch = Math.Min(bands, BandsPerBatch) *
                (4096L + width * 12L + ((long)fill.Length + stroke.Length) * 512L);
            if (scratch > 128L * 1024 * 1024) throw new VectorRasterizer.CoverageBudgetExceeded();
            // Conservative upper bound before scheduling: parallel workers must not each
            // spend the full global scan budget. The scalar fallback retains exact rejection semantics.
            long estimate = work + (long)width * height;
            Estimate(fill); Estimate(stroke);
            void Estimate(Edge[] edges)
            {
                foreach (var edge in edges)
                {
                    double rows = Math.Max(0, Math.Min(originY + height, Math.Ceiling(edge.y1)) -
                        Math.Max(originY, Math.Floor(edge.y0)));
                    estimate += (long)rows * (step ? 1 : 8);
                    if (estimate > MaxWork) throw new VectorRasterizer.CoverageBudgetExceeded();
                }
            }
            if (estimate > MaxWork) throw new VectorRasterizer.CoverageBudgetExceeded();
            using var fillEdges = new NativeArray<Edge>(fill, Allocator.TempJob);
            using var strokeEdges = new NativeArray<Edge>(stroke, Allocator.TempJob);
            using var counts = new NativeArray<long>(Math.Min(bands, BandsPerBatch), Allocator.TempJob);
            for (int first = 0; first < bands; first += BandsPerBatch)
            {
                int batch = Math.Min(BandsPerBatch, bands - first);
                new CoverageJob { fillEdges = fillEdges, strokeEdges = strokeEdges,
                    output = output, counts = counts,
                    width = width, height = height, originX = originX, originY = originY,
                    firstBand = first, samples = step ? 1 : 8 }.Schedule(batch, 1).Complete();
                for (int i = 0; i < batch; i++)
                {
                    work += counts[i];
                    if (work > MaxWork) throw VectorGeometry.Limit("scan conversion work");
                }
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        private struct CoverageJob : IJobParallelFor
        {
            [ReadOnly] internal NativeArray<Edge> fillEdges, strokeEdges;
            // Each job owns whole distinct rows of the output, never another job's pixels.
            [NativeDisableParallelForRestriction] internal NativeArray<Color> output;
            internal NativeArray<long> counts;
            internal int width, height, originX, originY, samples, firstBand;

            public void Execute(int band)
            {
                using var fillActive = new NativeList<Edge>(16, Allocator.Temp);
                using var strokeActive = new NativeList<Edge>(16, Allocator.Temp);
                using var crossings = new NativeList<Crossing>(32, Allocator.Temp);
                using var fillSpans = new NativeList<Span>(16, Allocator.Temp);
                using var strokeSpans = new NativeList<Span>(16, Allocator.Temp);
                using var fillStorage = new NativeArray<float>(width, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                using var strokeStorage = new NativeArray<float>(width, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                using var intersectionStorage = new NativeArray<float>(width, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var fillRow = fillStorage; var strokeRow = strokeStorage; var intersectionRow = intersectionStorage;
                int nextFill = 0, nextStroke = 0;
                long work = 0;
                int last = Math.Min(height, (firstBand + band + 1) * RowsPerJob);
                for (int row = (firstBand + band) * RowsPerJob; row < last; row++)
                {
                    for (int x = 0; x < width; x++) { fillRow[x] = 0; strokeRow[x] = 0; intersectionRow[x] = 0; }
                    for (int sample = 0; sample < samples; sample++)
                    {
                        double sy = originY + row + (sample + .5) / samples;
                        Scan(sy, fillEdges, fillActive, ref nextFill, crossings, fillSpans, ref work);
                        Scan(sy, strokeEdges, strokeActive, ref nextStroke, crossings, strokeSpans, ref work);
                        if (work > MaxWork) { counts[band] = work; return; }
                        for (int s = 0; s < fillSpans.Length; s++) Accumulate(fillRow, fillSpans[s]);
                        for (int s = 0; s < strokeSpans.Length; s++) Accumulate(strokeRow, strokeSpans[s]);
                        int a = 0, b = 0;
                        while (a < fillSpans.Length && b < strokeSpans.Length)
                        {
                            var f = fillSpans[a]; var s = strokeSpans[b];
                            double left = Math.Max(f.left, s.left), right = Math.Min(f.right, s.right);
                            if (right > left) Accumulate(intersectionRow, new Span { left = left, right = right });
                            if (f.right <= s.right) a++; else b++;
                        }
                    }
                    work += width;
                    if (work > MaxWork) { counts[band] = work; return; }
                    for (int x = 0; x < width; x++)
                        output[row * width + x] = new Color(Mathf.Clamp01(fillRow[x]), Mathf.Clamp01(strokeRow[x]), Mathf.Clamp01(intersectionRow[x]), 0);
                }
                counts[band] = work;
            }
            private void Scan(double y, NativeArray<Edge> edges, NativeList<Edge> active, ref int next,
                NativeList<Crossing> crossings, NativeList<Span> spans, ref long work)
            {
                spans.Clear(); crossings.Clear();
                while (next < edges.Length && edges[next].y0 <= y)
                {
                    var e = edges[next++];
                    if (e.y1 > y) active.Add(e);
                }
                for (int i = active.Length - 1; i >= 0; i--)
                {
                    var e = active[i];
                    if (e.y1 <= y) { active.RemoveAtSwapBack(i); continue; }
                    crossings.Add(new Crossing { x = e.x0 + (y - e.y0) * e.slope, winding = e.winding });
                }
                work += crossings.Length;
                crossings.AsArray().Sort();
                int winding = 0; double start = 0;
                for (int i = 0; i < crossings.Length;)
                {
                    double x = crossings[i].x; int delta = 0;
                    do { delta += crossings[i++].winding; } while (i < crossings.Length && crossings[i].x == x);
                    int before = winding; winding += delta;
                    if (before == 0 && winding != 0) start = x;
                    else if (before != 0 && winding == 0)
                    {
                        double left = Math.Max(originX, start), right = Math.Min(originX + width, x);
                        if (right > left) spans.Add(new Span { left = left, right = right });
                    }
                }
            }
            private void Accumulate(NativeArray<float> row, Span span)
            {
                if (samples == 1)
                {
                    int start = Math.Max(originX, (int)Math.Ceiling(span.left - .5));
                    int end = Math.Min(originX + width, (int)Math.Ceiling(span.right - .5));
                    for (int x = start; x < end; x++) row[x - originX] = 1;
                }
                else
                {
                    int start = Math.Max(originX, (int)Math.Floor(span.left));
                    int end = Math.Min(originX + width, (int)Math.Ceiling(span.right));
                    for (int x = start; x < end; x++)
                        row[x - originX] += (float)(Math.Min(x + 1, span.right) - Math.Max(x, span.left)) / samples;
                }
            }
        }
    }
}
