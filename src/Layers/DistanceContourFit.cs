using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace DCFApixels.WhimTex
{
    // Per-construction work only. Nothing survives the owning contour's build.
    internal static class DistanceContourFit
    {
        internal struct Line
        {
            public float2 center, direction;
            public float error;
            public bool valid;
        }

        internal struct Proposal
        {
            public int index, gap;
            public float2 point;
            public Line left, right;
            public float score;
            public bool stableFlanks;
        }

        internal static Proposal[] Propose(float2[] samples, Line[] lines, List<int> candidates, int flank, float step)
            => ProposeCore(samples,lines,candidates,flank,step,false);

        internal static Proposal[] ProposeEnclosed(float2[] samples, Line[] lines, List<int> candidates, int flank, float step)
            => ProposeCore(samples,lines,candidates,flank,step,true);

        static Proposal[] ProposeCore(float2[] samples, Line[] lines, List<int> candidates, int flank, float step, bool encloseCaps)
        {
            int n = samples.Length;
            bool[] required = null;
            int Wrap(int i) => (i % n + n) % n;
            foreach (int i in candidates)
                for (int gap = 1; gap * step <= 4; gap++)
                {
                    Line left = lines[Wrap(i - gap - flank)], right = lines[Wrap(i + gap)];
                    if (!left.valid || !right.valid || math.dot(left.direction, right.direction) >= -.8660254f) continue;
                    required ??= new bool[n];
                    required[Wrap(i - gap - 2 * flank)] = required[Wrap(i + gap)] = true;
                }
            Line[] extended = required != null ? Compute(samples, 2 * flank, required) : Array.Empty<Line>();
            using var points = new NativeArray<float2>(samples, Allocator.TempJob);
            using var fitted = new NativeArray<Line>(lines, Allocator.TempJob);
            using var longFitted = new NativeArray<Line>(extended, Allocator.TempJob);
            using var indices = new NativeArray<int>(candidates.ToArray(), Allocator.TempJob);
            using var output = new NativeArray<Proposal>(candidates.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using (DistanceFieldTiming.Measure("contour.fitProposalJobs"))
                new ProposalsJob { samples = points, lines = fitted, longLines = longFitted, indices = indices, output = output,
                    flank = flank, step = step, encloseCaps = encloseCaps }.Schedule(candidates.Count, 8).Complete();
            return output.ToArray();
        }

        struct Statistics
        {
            public float2 origin, center;
            public double xx, xy, yy;
        }

        internal static Line[] Compute(float2[] samples, int flank, bool[] required)
        {
            var result = new Line[samples.Length];
            int count = 0;
            for (int i = 0; i < required.Length; i++) if (required[i]) count++;
            if (count == 0) return result;
            using var points = new NativeArray<float2>(samples, Allocator.TempJob);
            using var starts = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using var statistics = new NativeArray<Statistics>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var writableStarts = starts;
            for (int i = 0, j = 0; i < required.Length; i++) if (required[i]) writableStarts[j++] = i;
            using (DistanceFieldTiming.Measure("contour.fitStatisticsJobs"))
                new StatisticsJob { samples = points, starts = starts, output = statistics, flank = flank }
                    .Schedule(count, 8).Complete();
            // Keep the original managed trig and residual arithmetic. Strict Burst
            // alone does not promise bit-identical Math trig/float-returning expressions.
            using var residualTiming = DistanceFieldTiming.Measure("contour.fitResiduals");
            int n = samples.Length, windowCount = flank + 1;
            float2 At(int i) => samples[i % n];
            for (int i = 0; i < count; i++)
            {
                int first = starts[i];
                Statistics s = statistics[i];
                double angle = .5 * Math.Atan2(2 * s.xy, s.xx - s.yy);
                float2 direction = new float2((float)Math.Cos(angle), (float)Math.Sin(angle));
                if (math.dot(direction, At(first + flank) - At(first)) < 0) direction = -direction;
                float2 normal = new float2(-direction.y, direction.x);
                float error = 0;
                double squares = 0;
                for (int k = first; k <= first + flank; k++)
                {
                    float e = math.abs(math.dot(At(k) - s.origin - s.center, normal));
                    error = math.max(error, e); squares += e * e;
                }
                result[first] = new Line { center = s.origin + s.center, direction = direction,
                    error = (float)(squares / windowCount), valid = error <= .35f && squares / windowCount <= .025 };
            }
            return result;
        }

        [BurstCompile(FloatMode = FloatMode.Strict)]
        struct StatisticsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> samples;
            [ReadOnly] public NativeArray<int> starts;
            [WriteOnly] public NativeArray<Statistics> output;
            public int flank;
            public void Execute(int index)
            {
                int first = starts[index], count = flank + 1, n = samples.Length;
                float2 origin = samples[first];
                double x = 0, y = 0;
                for (int k = first; k <= first + flank; k++)
                {
                    float2 p = samples[k % n] - origin;
                    x += p.x; y += p.y;
                }
                float2 center = new float2((float)(x / count), (float)(y / count));
                double xx = 0, xy = 0, yy = 0;
                for (int k = first; k <= first + flank; k++)
                {
                    float2 p = samples[k % n] - origin - center;
                    xx += (double)p.x * p.x; xy += (double)p.x * p.y; yy += (double)p.y * p.y;
                }
                output[index] = new Statistics { origin = origin, center = center, xx = xx, xy = xy, yy = yy };
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict)]
        struct ProposalsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> samples;
            [ReadOnly] public NativeArray<Line> lines;
            [ReadOnly] public NativeArray<Line> longLines;
            [ReadOnly] public NativeArray<int> indices;
            [WriteOnly] public NativeArray<Proposal> output;
            public int flank;
            public float step;
            public bool encloseCaps;
            int Wrap(int i) => (i % samples.Length + samples.Length) % samples.Length;
            float2 At(int i) => samples[Wrap(i)];
            // Retain the wider intermediate arithmetic of the original managed
            // float-returning helpers, then round at their return boundaries.
            static float Dot(float2 a, float2 b) => (float)((double)a.x * b.x + (double)a.y * b.y);
            static float Cross(float2 a, float2 b) => (float)((double)a.x * b.y - (double)a.y * b.x);
            static float SquaredDistance(float2 p, float2 a, float2 b)
            {
                float2 delta = b - a;
                float length = Dot(delta, delta);
                float t = length > 1e-12f ? math.saturate(Dot(p - a, delta) / length) : 0;
                float2 gap = p - a - t * delta;
                return Dot(gap, gap);
            }
            static float2 Project(float2 p, Line line) => line.center + Dot(p - line.center, line.direction) * line.direction;
            public void Execute(int slot)
            {
                int i = indices[slot];
                var best = new Proposal { score = float.MaxValue };
                const float normalBudget = .6f;
                for (int gap = 1; gap * step <= 4; gap++)
                {
                    Line left = lines[Wrap(i - gap - flank)], right = lines[Wrap(i + gap)];
                    if (!left.valid || !right.valid) continue;
                    int measuredFlank = flank;
                    if (longLines.Length > 0 && Dot(left.direction, right.direction) < -.8660254f)
                    {
                        Line longerLeft = longLines[Wrap(i - gap - 2 * flank)], longerRight = longLines[Wrap(i + gap)];
                        // Long, independently straight flanks reduce the angle bias
                        // of the few AA samples nearest a narrow raster cap.
                        if (longerLeft.valid && longerRight.valid && Dot(longerLeft.direction, longerRight.direction) < -.8660254f &&
                            Dot(left.direction, longerLeft.direction) > .995f && Dot(right.direction, longerRight.direction) > .995f)
                        { left = longerLeft; right = longerRight; measuredFlank *= 2; }
                    }
                    float dot = Dot(left.direction, right.direction), cross = Cross(left.direction, right.direction);
                    if (dot > .7071068f || math.abs(cross) < .05f) continue;
                    float2 point = left.center + Cross(right.center - left.center, right.direction) / cross * left.direction;
                    if (encloseCaps && measuredFlank > flank && cross > .05f)
                    {
                        // Validate finite replacement against an enclosing corner,
                        // not an unconstrained intersection behind a merged AA tip.
                        // Exact unsampled vertices are checked again by the caller.
                        float2 nl = new float2(left.direction.y,-left.direction.x), nr = new float2(right.direction.y,-right.direction.x);
                        float leftExcess = 0, rightExcess = 0;
                        for (int k=i-gap-measuredFlank;k<=i+gap+measuredFlank;k++)
                        {
                            leftExcess = math.max(leftExcess,Dot(At(k)-left.center,nl));
                            rightExcess = math.max(rightExcess,Dot(At(k)-right.center,nr));
                        }
                        left.center += leftExcess*nl; right.center += rightExcess*nr;
                        point = left.center+Cross(right.center-left.center,right.direction)/cross*left.direction;
                    }
                    if (Dot(point - At(i - gap), left.direction) < 0 || Dot(point - At(i + gap), right.direction) > 0) continue;
                    float cosine = (float)Math.Sqrt(math.max(.0001f, (float)((1.0 + dot) * .5f)));
                    float2 a = Project(At(i - gap - measuredFlank), left), b = Project(At(i + gap + measuredFlank), right);
                    float error = 0, capError = 0, tipDistance = float.MaxValue;
                    for (int k = i - gap - measuredFlank; k <= i + gap + measuredFlank; k++)
                    {
                        float deviation = math.min(SquaredDistance(At(k), a, point), SquaredDistance(At(k), point, b));
                        if (measuredFlank > flank && k >= i-gap && k <= i+gap) capError = math.max(capError,deviation);
                        else error = math.max(error,deviation);
                        if (k >= i - gap && k < i + gap)
                            tipDistance = math.min(tipDistance, SquaredDistance(point, At(k), At(k + 1)));
                    }
                    // Normal uncertainty is amplified along an acute bisector.
                    // Only independently straight extended flanks can justify
                    // more than the original local two-pixel allowance. A thin
                    // smooth cap can otherwise imitate an acute intersection.
                    float amplifiedUncertainty = normalBudget / cosine;
                    bool stableFlanks = measuredFlank > flank;
                    // The tiny AA cap can deviate by a half-pixel diagonal even
                    // when both extended sides are independently straight. This
                    // engineering allowance is cap-only: flank fits and their
                    // .6px envelope are unchanged; resolved facets stay guarded.
                    const float capBudget = .7071068f;
                    float amplifiedCap = capBudget / cosine;
                    float tipLimit = stableFlanks && amplifiedCap <= flank * step
                        ? flank * step : math.min(2, amplifiedUncertainty);
                    if (error > (double)normalBudget * normalBudget || capError > (double)capBudget * capBudget ||
                        tipDistance > (double)tipLimit * tipLimit) continue;
                    float score = (float)((double)left.error + right.error + (double).001f * gap + (double).01f * math.max(error,capError));
                    if (stableFlanks && !best.stableFlanks || stableFlanks == best.stableFlanks && score < best.score)
                        best = new Proposal { index = i, gap = gap, point = point,
                            left = left, right = right, score = score, stableFlanks = stableFlanks };
                }
                output[slot] = best;
            }
        }
    }
}
