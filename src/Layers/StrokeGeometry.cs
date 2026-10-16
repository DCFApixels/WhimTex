using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;

namespace DCFApixels.WhimTex
{
    // Build a union of segment bodies, joins and caps. Clipper resolves overlapping bodies
    // before rasterization, so translucent strokes never darken at joins or intersections.
    internal static class StrokeGeometry
    {
        internal const double Precision = 4096;
        private const int MaxGeneratedVertices = 1048576;

        [ThreadStatic] private static BuildState currentBuild;

        // Synchronous, thread-local lifetime: nested scopes borrow the outer build.
        // Only the outer owner retains an engine, and only until that scope exits.
        internal static IDisposable BeginBuild()
        {
            if (currentBuild != null) return BorrowedBuild.Instance;
            return currentBuild = new BuildState();
        }

        private sealed class BorrowedBuild : IDisposable
        {
            internal static readonly BorrowedBuild Instance = new BorrowedBuild();
            public void Dispose() { }
        }

        private sealed class BuildState : IDisposable
        {
            internal Clipper64 engine;
            internal bool inUse;

            public void Dispose()
            {
                try { engine?.Clear(); }
                finally
                {
                    engine = null;
                    if (ReferenceEquals(currentBuild, this)) currentBuild = null;
                }
            }
        }

        internal static Paths64 Resolve(Paths64 subject, Paths64 clip = null, ClipType operation = ClipType.Union,
            FillRule rule = FillRule.NonZero)
        {
            using var total = VectorGeometryTiming.Measure("vector.clip.resolve");
            var build = currentBuild;
            // NoClip skips Clipper's Reset(), including its success flag. Keep the
            // fresh-engine behavior for that operation. Never share an active engine.
            bool reuse = build != null && !build.inUse && operation != ClipType.NoClip;
            Clipper64 engine = reuse ? build.engine : null;
            if (reuse) build.inUse = true;
            try
            {
                using (VectorGeometryTiming.Measure("vector.clip.budget")) CheckIntersectionBudget(subject, clip);
                // Only the empty/empty case is an unconditional identity; a nonempty
                // Clipper result may still change on another normalization pass.
                if (subject.Count == 0 && (clip == null || clip.Count == 0)) return new Paths64();
                using (VectorGeometryTiming.Measure("vector.clip.input"))
                {
                    if (engine == null)
                    {
                        engine = new Clipper64();
                        if (reuse) build.engine = engine;
                    }
                    engine.AddSubject(subject);
                    if (clip != null) engine.AddClip(clip);
                }
                // Output containers and their paths belong to this call, never to the
                // scoped engine. Later Clear/Execute calls cannot mutate these outputs.
                var result = new Paths64();
                using (VectorGeometryTiming.Measure("vector.clip.execute"))
                    if (!engine.Execute(operation, rule, result))
                        throw new InvalidOperationException("Vector polygon resolution failed. Source geometry was preserved.");
                int count = 0;
                foreach (var path in result) if ((count += path.Count) > MaxGeneratedVertices) throw VectorGeometry.Limit("resolved vertex count");
                return result;
            }
            finally
            {
                // Execute clears only its solution, retaining input vertices. Clear
                // releases inputs but preserves Clipper's per-instance object pools.
                try { engine?.Clear(); }
                finally { if (reuse) build.inUse = false; }
            }
        }

        // Clipper's output can grow quadratically for adversarial crossing inputs. Bound an
        // intentionally conservative sweep workload before entering its allocation-heavy resolver.
        private static long CheckIntersectionBudget(Paths64 subject, Paths64 clip)
        {
            int count = 0;
            Count(subject); if (clip != null) Count(clip);
            void Count(Paths64 paths)
            {
                foreach (var path in paths)
                    if (path.Count >= 3 && (count += path.Count) > MaxGeneratedVertices)
                        throw VectorGeometry.Limit("polygon input vertex count");
            }
            if (count == 0) return 0;
            using var lefts = new NativeArray<long>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using var rights = new NativeArray<long>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using var result = new NativeArray<long>(1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var leftData = lefts; var rightData = rights;
            int index = 0;
            Append(subject); if (clip != null) Append(clip);
            void Append(Paths64 paths)
            {
                foreach (var path in paths)
                {
                    if (path.Count < 3) continue;
                    Point64 previous = path[path.Count - 1];
                    foreach (var p in path)
                    {
                        leftData[index] = Math.Min(previous.X, p.X); rightData[index] = Math.Max(previous.X, p.X);
                        index++; previous = p;
                    }
                }
            }
            new IntersectionBudgetJob { lefts = lefts, rights = rights, result = result }.Run();
            if (result[0] > 4000000) throw VectorGeometry.Limit("polygon intersection workload");
            return result[0];
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct IntersectionBudgetJob : IJob
        {
            internal NativeArray<long> lefts, rights, result;
            public void Execute()
            {
                lefts.Sort(); rights.Sort();
                int expired = 0;
                long visits = 0;
                for (int i = 0; i < lefts.Length; i++)
                {
                    // Count before expiring, exactly like the old active-list scan (including
                    // edges that expire on this step). Since right >= left, no future edge
                    // can expire here, so two sorted endpoints replace repeated overlap scans.
                    visits += i - expired;
                    if (visits > 4000000) break;
                    while (expired < rights.Length && rights[expired] < lefts[i]) expired++;
                }
                result[0] = visits;
            }
        }

        internal static Path64 ToPath(List<Vector2> points)
        {
            var result = new Path64(points.Count);
            foreach (var p in points) result.Add(Quantize(p));
            return result;
        }

        private static Point64 Quantize(Vector2 p)
        {
            if (!VectorGeometry.Finite(p) || Math.Abs(p.x) > VectorGeometry.MaxCoordinate || Math.Abs(p.y) > VectorGeometry.MaxCoordinate)
                throw VectorGeometry.Limit("stroke coordinate range");
            return new Point64(Math.Round(p.x * Precision), Math.Round(p.y * Precision));
        }

        internal static Paths64 Outline(List<List<Vector2>> closedPaths, List<List<Vector2>> openPaths,
            Paths64 interior, StrokeStyle style, float tolerance)
        {
            style.Validate();
            var result = new Paths64();
            if (style.strokeWidth <= 0) return result;
            if (closedPaths.Count > 0)
            {
                var trajectories = closedPaths;
                if (style.strokePosition != VectorStrokePosition.Center)
                    trajectories = OffsetTrajectories(interior, style, tolerance);
                var flags = new List<bool>(trajectories.Count); foreach (var ignored in trajectories) flags.Add(true);
                // Place dashes and caps AFTER moving the trajectory. Clipping a double-width
                // finished stroke against the fill cuts round dots and caps in half.
                result = Build(trajectories, flags, style, style.strokeWidth * .5f, tolerance);
            }
            if (openPaths != null && openPaths.Count > 0)
            {
                var flags = new List<bool>(openPaths.Count); foreach (var ignored in openPaths) flags.Add(false);
                result.AddRange(Build(openPaths, flags, style, style.strokeWidth * .5f, tolerance));
                if (result.Count > 0) result = Resolve(result);
            }
            return result;
        }

        private static List<List<Vector2>> OffsetTrajectories(Paths64 interior, StrokeStyle style, float tolerance)
        {
            if (interior.Count == 0) return new List<List<Vector2>>();
            // The resolved fill defines the filled side, including holes, nested contours
            // and self-intersections. Its boundary band constructs the offset region;
            // only that region's boundary is used as the new centreline, never as a cap mask.
            var boundaries = Trajectories(interior);
            var flags = new List<bool>(boundaries.Count); foreach (var ignored in boundaries) flags.Add(true);
            var solid = style; solid.dashPattern = null; solid.dashOffset = 0;
            var band = Build(boundaries, flags, solid, style.strokeWidth * .5f, tolerance);
            var offset = Resolve(interior, band, style.strokePosition == VectorStrokePosition.Inside
                ? ClipType.Difference : ClipType.Union);
            // Collapsed inward offsets stay empty: falling back to a clipped stroke here
            // would silently reintroduce the cut caps this path is meant to avoid.
            return Trajectories(offset);
        }

        private static List<List<Vector2>> Trajectories(Paths64 polygons)
        {
            var result = new List<List<Vector2>>(polygons.Count);
            foreach (var polygon in polygons)
            {
                if (polygon.Count < 3) continue;
                // Boolean resolution may rotate the vertex array. Choose a stable seam
                // so dashes do not depend on its output ordering or Shape/Vector origin.
                int first = 0;
                for (int i = 1; i < polygon.Count; i++)
                    if (polygon[i].X < polygon[first].X ||
                        (polygon[i].X == polygon[first].X && polygon[i].Y < polygon[first].Y)) first = i;
                var points = new List<Vector2>(polygon.Count);
                for (int i = 0; i < polygon.Count; i++)
                {
                    var p = polygon[(first + i) % polygon.Count];
                    points.Add(new Vector2((float)(p.X / Precision), (float)(p.Y / Precision)));
                }
                result.Add(points);
            }
            return result;
        }

        internal static Paths64 Build(List<List<Vector2>> paths, List<bool> closed, StrokeStyle style, float radius, float tolerance)
        {
            var builder = new Builder(style, radius, tolerance);
            for (int i = 0; i < paths.Count; i++)
            {
                if (style.dashPattern == null || style.dashPattern.Count == 0) builder.Stroke(paths[i], closed[i]);
                else builder.Dashed(paths[i], closed[i]);
            }
            return builder.Polygons.Count == 0 ? builder.Polygons : Resolve(builder.Polygons);
        }

        private sealed class Builder
        {
            internal readonly Paths64 Polygons = new Paths64();
            private readonly StrokeStyle style;
            private readonly float radius, tolerance;
            private int vertices, dashPieces;
            internal Builder(StrokeStyle style, float radius, float tolerance)
            { this.style = style; this.radius = radius; this.tolerance = tolerance; }

            private void Polygon(params Vector2[] points)
            {
                if ((vertices += points.Length) > MaxGeneratedVertices) throw VectorGeometry.Limit("stroke vertex count");
                var path = new Path64(points.Length);
                foreach (var point in points) path.Add(Quantize(point));
                double area = Clipper.Area(path);
                if (area == 0) return;
                if (area < 0) path.Reverse();
                Polygons.Add(path);
            }

            private void Arc(Vector2 center, Vector2 from, Vector2 to, float angle, bool sector)
            {
                double step = 2 * Math.Acos(Math.Max(-1, Math.Min(1, 1d - (double)tolerance / radius)));
                double required = Math.Ceiling(Math.Abs(angle) / Math.Max(1e-12, step));
                if (required > 8192) throw VectorGeometry.Limit("round stroke subdivision count");
                int count = Math.Max(1, (int)required);
                if (vertices + count + 2 > MaxGeneratedVertices) throw VectorGeometry.Limit("round stroke subdivision count");
                int first = sector ? 1 : 0;
                var points = new Vector2[count + 1 + first];
                if (sector) points[0] = center;
                double start = Math.Atan2(from.y, from.x);
                // Share the exact endpoints with segment bodies. Recomputing them with
                // trigonometry can put the two sides on different integer-grid cells.
                points[first] = center + from * radius;
                points[first + count] = center + to * radius;
                for (int i = 1; i < count; i++)
                {
                    double a = start + angle * i / count;
                    points[first + i] = center + radius * new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                }
                Polygon(points);
            }

            internal void Stroke(List<Vector2> source, bool closed)
            {
                if (source.Count == 0 || radius <= 0) return;
                var points = new List<Vector2>(source.Count);
                foreach (var p in source)
                    if (points.Count == 0 || (p - points[points.Count - 1]).sqrMagnitude > 1e-14f) points.Add(p);
                if (closed && points.Count > 1 && points[0] == points[points.Count - 1]) points.RemoveAt(points.Count - 1);
                if (points.Count == 1)
                {
                    if (closed || style.cap == VectorStrokeCap.Butt) return;
                    if (style.cap == VectorStrokeCap.Round) Arc(points[0], Vector2.right, Vector2.right, Mathf.PI * 2, false);
                    else { Vector2 p = points[0], r = Vector2.one * radius; Polygon(p - r, p + new Vector2(radius, -radius), p + r, p + new Vector2(-radius, radius)); }
                    return;
                }
                int segments = points.Count - (closed ? 0 : 1);
                for (int i = 0; i < segments; i++)
                {
                    Vector2 a = points[i], b = points[(i + 1) % points.Count], direction = (b - a).normalized;
                    Vector2 normal = new Vector2(-direction.y, direction.x) * radius;
                    if (!closed && style.cap == VectorStrokeCap.Square)
                    { if (i == 0) a -= direction * radius; if (i == segments - 1) b += direction * radius; }
                    // Joins close against two half-edges meeting at the path vertex.
                    // Keep that same quantized vertex in the adjacent body rather than
                    // assuming it stays collinear with the two offset vertices.
                    bool joinStart = closed || i > 0, joinEnd = closed || i + 1 < segments;
                    var body = new Vector2[4 + (joinStart ? 1 : 0) + (joinEnd ? 1 : 0)];
                    int at = 0;
                    body[at++] = a + normal;
                    if (joinStart) body[at++] = a;
                    body[at++] = a - normal; body[at++] = b - normal;
                    if (joinEnd) body[at++] = b;
                    body[at] = b + normal;
                    Polygon(body);
                }
                for (int i = closed ? 0 : 1; i < points.Count - (closed ? 0 : 1); i++)
                {
                    Vector2 p = points[i], incoming = (p - points[(i + points.Count - 1) % points.Count]).normalized;
                    Vector2 outgoing = (points[(i + 1) % points.Count] - p).normalized;
                    float cross = incoming.x * outgoing.y - incoming.y * outgoing.x;
                    float dot = Mathf.Clamp(Vector2.Dot(incoming, outgoing), -1, 1);
                    if (Mathf.Abs(cross) < 1e-6f)
                    {
                        if (dot < 0 && style.join == VectorStrokeJoin.Round) Arc(p, Vector2.right, Vector2.right, Mathf.PI * 2, false);
                        continue;
                    }
                    float side = Mathf.Sign(cross);
                    Vector2 n1 = new Vector2(incoming.y, -incoming.x) * side;
                    Vector2 n2 = new Vector2(outgoing.y, -outgoing.x) * side;
                    Vector2 a = p + n1 * radius, b = p + n2 * radius;
                    if (style.join == VectorStrokeJoin.Round) Arc(p, n1, n2, Mathf.Atan2(cross, dot), true);
                    else if (style.join == VectorStrokeJoin.Miter && 1 + dot > 1e-7f)
                    {
                        Vector2 extension = (n1 + n2) * (radius / (1 + dot));
                        if (extension.sqrMagnitude <= radius * radius * style.miterLimit * style.miterLimit)
                            Polygon(p, a, p + extension, b);
                        else Polygon(p, a, b);
                    }
                    else Polygon(p, a, b);
                }
                if (!closed && style.cap == VectorStrokeCap.Round)
                {
                    Vector2 first = (points[1] - points[0]).normalized, last = (points[points.Count - 1] - points[points.Count - 2]).normalized;
                    // A semicircle closes directly along its diameter, with no centre
                    // vertex: quantizing that third point can create a tiny internal slit.
                    Vector2 firstNormal = new Vector2(-first.y, first.x), lastNormal = new Vector2(last.y, -last.x);
                    Arc(points[0], firstNormal, -firstNormal, Mathf.PI, false);
                    Arc(points[points.Count - 1], lastNormal, -lastNormal, Mathf.PI, false);
                }
            }

            internal void Dashed(List<Vector2> points, bool closed)
            {
                if (points.Count == 0 || radius <= 0 || style.strokeWidth <= 0) return;
                var pattern = style.dashPattern;
                double scale = style.DashLengthScale;
                int count = pattern.Count * ((pattern.Count & 1) == 0 ? 1 : 2);
                double period = 0;
                for (int i = 0; i < count; i++) period += pattern[i % pattern.Count] * scale;
                double offset = style.dashOffset % period;
                if (offset < 0) offset += period;
                int index = 0;
                while (offset >= pattern[index % pattern.Count] * scale) { offset -= pattern[index % pattern.Count] * scale; index = (index + 1) % count; }
                double remaining = pattern[index % pattern.Count] * scale - offset;
                bool startsOn = (index & 1) == 0, on = startsOn;
                if (points.Count == 1) { if (on) Stroke(points, closed); return; }
                var pieces = new List<List<Vector2>>();
                List<Vector2> current = on ? new List<Vector2> { points[0] } : null;
                for (int segment = 0; segment < points.Count - (closed ? 0 : 1); segment++)
                {
                    Vector2 a = points[segment], b = points[(segment + 1) % points.Count];
                    double length = Vector2.Distance(a, b), traveled = 0;
                    if (length < 1e-12) continue;
                    while (traveled < length)
                    {
                        if (++dashPieces > MaxGeneratedVertices) throw VectorGeometry.Limit("dash subdivision count");
                        double step = Math.Min(remaining, length - traveled);
                        double next = traveled + step;
                        if (next <= traveled) throw VectorGeometry.Limit("dash precision");
                        Vector2 end = Vector2.LerpUnclamped(a, b, (float)(next / length));
                        if (on) current.Add(end);
                        traveled = next; remaining -= step;
                        if (remaining <= 1e-10)
                        {
                            if (on && current.Count > 1) pieces.Add(current);
                            index = (index + 1) % count; on = (index & 1) == 0;
                            remaining = pattern[index % pattern.Count] * scale;
                            current = on ? new List<Vector2> { end } : null;
                        }
                    }
                }
                bool endsOn = on && current != null && current.Count > 1;
                if (endsOn) pieces.Add(current);
                // The seam is an ordinary join when an on interval crosses it; avoid duplicate caps.
                if (closed && pieces.Count > 1 && pieces[0][0] == points[0] &&
                    pieces[pieces.Count - 1][pieces[pieces.Count - 1].Count - 1] == points[0])
                {
                    var first = pieces[0]; var last = pieces[pieces.Count - 1];
                    last.AddRange(first.GetRange(1, first.Count - 1));
                    pieces.RemoveAt(0);
                }
                foreach (var piece in pieces)
                    Stroke(piece, closed && pieces.Count == 1 && piece[0] == piece[piece.Count - 1]);
            }
        }
    }
}
