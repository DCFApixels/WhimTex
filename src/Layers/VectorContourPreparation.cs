using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Snapshots contain no layer/Unity objects. Each worker preserves the original DFS point order.
    internal static class VectorContourPreparation
    {
        private struct Control { internal Vector2 position, incoming, outgoing; }
        private struct Contour { internal int start, count, closed; }
        private struct Cubic { internal Vector2 a, b, c, d; internal int depth; }

        internal static List<List<Vector2>> Flatten(List<VectorContour> contours, Vector2 reference, float tolerance)
        {
            using var measure = WhimTexProfiler.Measure("vector.contours");
            VectorGeometry.ValidateReferenceSize(reference);
            if (!VectorGeometry.Finite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            if (contours == null) throw new ArgumentNullException(nameof(contours));
            if (contours.Count > VectorGeometry.MaxContours) throw VectorGeometry.Limit("contour count");
            var result = new List<List<Vector2>>(contours.Count);
            if (contours.Count == 0) return result;
            int total = 0;
            foreach (var contour in contours)
            {
                if (contour?.points == null) throw new ArgumentNullException(nameof(contours));
                if (contour.points.Count > VectorGeometry.MaxPoints || (total += contour.points.Count) > VectorGeometry.MaxPoints)
                    throw VectorGeometry.Limit("anchor count");
                foreach (var point in contour.points) VectorGeometry.ValidatePoint(point);
            }
            using var controls = new NativeArray<Control>(total, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using var ranges = new NativeArray<Contour>(contours.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var controlData = controls; var rangeData = ranges;
            int offset = 0;
            for (int i = 0; i < contours.Count; i++)
            {
                var contour = contours[i];
                rangeData[i] = new Contour { start = offset, count = contour.points.Count, closed = contour.closed ? 1 : 0 };
                foreach (var point in contour.points)
                    controlData[offset++] = new Control { position = Vector2.Scale(point.position, reference),
                        incoming = Vector2.Scale(point.position + point.inTangent, reference),
                        outgoing = Vector2.Scale(point.position + point.outTangent, reference) };
            }
            int flattened = 0;
            // Each contour has a hard point limit; bounded batches also cap aggregate native
            // scratch before the global point limit is checked (at most ~16 MiB of point data).
            for (int first = 0; first < contours.Count; first += 8)
            {
                int batch = Math.Min(8, contours.Count - first);
                using var errors = new NativeArray<int>(batch, Allocator.TempJob);
                using var output = new NativeStream(batch, Allocator.TempJob);
                new FlattenJob { controls = controls, ranges = ranges, errors = errors, output = output.AsWriter(),
                    first = first, toleranceSquared = tolerance * tolerance }.Schedule(batch, 1).Complete();
                var reader = output.AsReader();
                for (int i = 0; i < batch; i++)
                {
                    if (errors[i] != 0) throw VectorGeometry.Limit(errors[i] == 1 ? "coordinate range" :
                        errors[i] == 2 ? "flattened point count" : "cubic subdivision depth");
                    int count = reader.BeginForEachIndex(i);
                    var path = new List<Vector2>(count);
                    for (int p = 0; p < count; p++) path.Add(reader.Read<Vector2>());
                    reader.EndForEachIndex();
                    if (contours[first + i].closed && path.Count > 1 && path[0] == path[path.Count - 1]) path.RemoveAt(path.Count - 1);
                    if ((flattened += path.Count) > VectorGeometry.MaxFlattenedPoints) throw VectorGeometry.Limit("total flattened point count");
                    result.Add(path);
                }
            }
            return result;
        }

        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        private struct FlattenJob : IJobParallelFor
        {
            [ReadOnly] internal NativeArray<Control> controls;
            [ReadOnly] internal NativeArray<Contour> ranges;
            internal NativeArray<int> errors;
            internal NativeStream.Writer output;
            internal float toleranceSquared;
            internal int first;

            public void Execute(int index)
            {
                output.BeginForEachIndex(index);
                var range = ranges[first + index]; int count = 0, error = 0;
                Vector2 previous = default;
                if (range.count > 0)
                {
                    Add(controls[range.start].position, ref previous, ref count, ref error);
                    int segments = range.count - (range.closed == 1 ? 0 : 1);
                    for (int segment = 0; segment < segments && error == 0; segment++)
                    {
                        var a = controls[range.start + segment];
                        var b = controls[range.start + (segment + 1) % range.count];
                        var stack = new FixedList4096Bytes<Cubic>();
                        stack.Add(new Cubic { a = a.position, b = a.outgoing, c = b.incoming, d = b.position });
                        while (stack.Length > 0 && error == 0)
                        {
                            var curve = stack[stack.Length - 1]; stack.Length--;
                            if (Mathf.Max(Distance(curve.b, curve.a, curve.d), Distance(curve.c, curve.a, curve.d)) <= toleranceSquared)
                            { Add(curve.d, ref previous, ref count, ref error); continue; }
                            if (curve.depth >= 24) { error = 3; break; }
                            var ab = (curve.a + curve.b) * .5f; var bc = (curve.b + curve.c) * .5f;
                            var cd = (curve.c + curve.d) * .5f;
                            var abc = (ab + bc) * .5f; var bcd = (bc + cd) * .5f;
                            var mid = (abc + bcd) * .5f;
                            stack.Add(new Cubic { a = mid, b = bcd, c = cd, d = curve.d, depth = curve.depth + 1 });
                            stack.Add(new Cubic { a = curve.a, b = ab, c = abc, d = mid, depth = curve.depth + 1 });
                        }
                    }
                }
                errors[index] = error;
                output.EndForEachIndex();
            }
            private static float Distance(Vector2 p, Vector2 a, Vector2 b)
            {
                // Managed Vector2 dot/squared-length retain wider intermediates up to the
                // float return boundary. Keep those boundaries explicit in Burst as well.
                Vector2 ab = b - a, ap = p - a;
                float length = (float)((double)ab.x * ab.x + (double)ab.y * ab.y);
                float dot = (float)((double)ap.x * ab.x + (double)ap.y * ab.y);
                float t = length > 0 ? Mathf.Clamp01(dot / length) : 0;
                Vector2 delta = p - (a + t * ab);
                return (float)((double)delta.x * delta.x + (double)delta.y * delta.y);
            }
            private void Add(Vector2 point, ref Vector2 previous, ref int count, ref int error)
            {
                if (!VectorGeometry.Finite(point) || Math.Abs(point.x) > VectorGeometry.MaxCoordinate || Math.Abs(point.y) > VectorGeometry.MaxCoordinate)
                { error = 1; return; }
                if (count >= VectorGeometry.MaxFlattenedPoints) { error = 2; return; }
                if (count != 0 && previous == point) return;
                output.Write(point); previous = point; count++;
            }
        }
    }
}
