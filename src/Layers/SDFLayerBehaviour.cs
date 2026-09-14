using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "SDFLayerBehaviour")]
    [Serializable]
    public sealed class SDFLayerBehaviour : TargetedLayerBehaviour
    {
        public DistanceMetric metric = DistanceMetric.EuclideanExact;
        public SourceChannel sourceChannel = SourceChannel.Alpha;
        [Range(0, 255)] public byte threshold = 128;
        public DistancePosition distancePosition = DistancePosition.Signed;
        public bool inverted;
        public float maxDistanceNormalization;
        public Gradient gradient = GradientUtility.Create(GradientUtility.WhiteToBlack);

        internal override bool RequiresColorInput => sourceChannel != SourceChannel.Alpha;

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            if (context.input == null)
                return null;

            Texture2D inputTexture = TextureCompositor.CopyToTexture2D(context.input, uploadToGpu: false);
            NativeArray<float> signedDistances = default;
            Texture2D resultTexture = null;
            try
            {
                NativeArray<Color32> inputPixels = inputTexture.GetRawTextureData<Color32>();
                signedDistances = new NativeArray<float>(
                    inputPixels.Length,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                DistanceFieldUtility.ComputeSignedDistance(
                    inputPixels,
                    signedDistances,
                    context.width,
                    context.height,
                    threshold,
                    (int)sourceChannel,
                    metric);

                resultTexture = new Texture2D(context.width, context.height, TextureFormat.RGBAFloat, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                NativeArray<Color> outputPixels = resultTexture.GetRawTextureData<Color>();
                float maxDistance = GetNormalizationDistance(context);
                bool isTwoColorGradient = GradientUtility.IsTwoColorGradient(gradient, out Color left, out Color right);
                Gradient evaluatedGradient = gradient ?? GradientUtility.WhiteToBlack;

                if (isTwoColorGradient)
                {
                    SdfTwoColorOutputJob job = new SdfTwoColorOutputJob
                    {
                        signedDistances = signedDistances,
                        output = outputPixels,
                        left = new float4(left.r, left.g, left.b, left.a),
                        right = new float4(right.r, right.g, right.b, right.a),
                        maxDistance = maxDistance,
                        distancePosition = (int)distancePosition,
                        inverted = inverted
                    };
                    job.Schedule(outputPixels.Length, 128).Complete();
                }
                else
                {
                    for (int i = 0; i < signedDistances.Length; i++)
                    {
                        float distance = ConvertDistance(signedDistances[i]);
                        float normalized = distancePosition == DistancePosition.Signed
                            ? (distance + maxDistance) / (2f * maxDistance)
                            : distance / maxDistance;
                        normalized = math.clamp(normalized, 0f, 1f);
                        if (inverted)
                            normalized = 1f - normalized;

                        outputPixels[i] = HdrUtility.Decode(evaluatedGradient.Evaluate(normalized));
                    }
                }

                resultTexture.Apply(false, false);
                return ApplyTransformAndModifiers(resultTexture, context);
            }
            finally
            {
                if (signedDistances.IsCreated)
                    signedDistances.Dispose();
                if (inputTexture != null)
                    UnityEngine.Object.DestroyImmediate(inputTexture);
                if (resultTexture != null)
                    UnityEngine.Object.DestroyImmediate(resultTexture);
            }
        }

        public override string ToString()
        {
            return "SDF";
        }

        private float ConvertDistance(float signedDistance)
        {
            switch (distancePosition)
            {
                case DistancePosition.Outside:
                    return math.max(signedDistance, 0f);
                case DistancePosition.Inside:
                    return math.max(-signedDistance, 0f);
                case DistancePosition.Center:
                    return math.abs(signedDistance);
                default:
                    return signedDistance;
            }
        }

        private float GetNormalizationDistance(in LayerRenderContext context)
        {
            if (maxDistanceNormalization > 0f)
                return Mathf.Max(0.0001f, maxDistanceNormalization / context.scaleMultiplier);

            switch (metric)
            {
                case DistanceMetric.Manhattan:
                    return Mathf.Max(1f, context.width + context.height);
                case DistanceMetric.Chebyshev:
                    return Mathf.Max(context.width, context.height);
                default:
                    return Mathf.Max(1f, Mathf.Sqrt((float)context.width * context.width + (float)context.height * context.height));
            }
        }

        public enum SourceChannel
        {
            Alpha,
            Red,
            Green,
            Blue,
            Luminance
        }

        public enum DistancePosition
        {
            Outside,
            Inside,
            Center,
            Signed
        }
    }

    [BurstCompile]
    internal struct SdfTwoColorOutputJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> signedDistances;
        [WriteOnly] public NativeArray<Color> output;
        public float4 left;
        public float4 right;
        public float maxDistance;
        public int distancePosition;
        public bool inverted;

        public void Execute(int index)
        {
            float distance = signedDistances[index];
            switch (distancePosition)
            {
                case (int)SDFLayerBehaviour.DistancePosition.Outside:
                    distance = math.max(distance, 0f);
                    break;
                case (int)SDFLayerBehaviour.DistancePosition.Inside:
                    distance = math.max(-distance, 0f);
                    break;
                case (int)SDFLayerBehaviour.DistancePosition.Center:
                    distance = math.abs(distance);
                    break;
            }

            float normalized = distancePosition == (int)SDFLayerBehaviour.DistancePosition.Signed
                ? (distance + maxDistance) / (2f * maxDistance)
                : distance / maxDistance;
            normalized = math.saturate(normalized);
            if (inverted)
                normalized = 1f - normalized;

            float4 color = math.lerp(left, right, normalized);
            output[index] = HdrUtility.Decode(new Color(color.x, color.y, color.z, color.w));
        }

        private static byte ToByte(float value)
        {
            return (byte)math.round(value * 255f);
        }
    }

    internal static class DistanceFieldUtility
    {
        public static void ComputeSignedDistance(
            NativeArray<Color32> input,
            NativeArray<float> output,
            int width,
            int height,
            byte threshold,
            int sourceChannel,
            DistanceMetric metric)
        {
            // The caller's output buffer doubles as the distance-to-object field and is
            // converted to signed distances in place after both transforms complete.
            NativeArray<float> distanceToObject = output;
            NativeArray<float> distanceToBackground = new NativeArray<float>(
                input.Length,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            try
            {
                if (metric == DistanceMetric.EuclideanExact || metric == DistanceMetric.EuclideanAntialiased)
                {
                    ComputeExactSignedDistance(
                        input,
                        output,
                        distanceToObject,
                        distanceToBackground,
                        width,
                        height,
                        threshold,
                        sourceChannel,
                        metric == DistanceMetric.EuclideanAntialiased);
                }
                else
                {
                    float diagonalCost;
                    switch (metric)
                    {
                        case DistanceMetric.Manhattan:
                            diagonalCost = 2f;
                            break;
                        case DistanceMetric.Chebyshev:
                            diagonalCost = 1f;
                            break;
                        default:
                            diagonalCost = math.sqrt(2f);
                            break;
                    }

                    InitializeApproximateDistancesJob initializeJob = new InitializeApproximateDistancesJob
                    {
                        input = input,
                        distanceToObject = distanceToObject,
                        distanceToBackground = distanceToBackground,
                        threshold = threshold,
                        sourceChannel = sourceChannel
                    };
                    JobHandle initialize = initializeJob.Schedule(input.Length, 128);

                    // Object and background fields are independent and can occupy two workers.
                    ApproximateDistanceTransformJob objectJob = new ApproximateDistanceTransformJob
                    {
                        distances = distanceToObject,
                        width = width,
                        height = height,
                        diagonalCost = diagonalCost
                    };
                    ApproximateDistanceTransformJob backgroundJob = new ApproximateDistanceTransformJob
                    {
                        distances = distanceToBackground,
                        width = width,
                        height = height,
                        diagonalCost = diagonalCost
                    };
                    JobHandle objectTransform = objectJob.Schedule(initialize);
                    JobHandle backgroundTransform = backgroundJob.Schedule(initialize);
                    JobHandle transforms = JobHandle.CombineDependencies(objectTransform, backgroundTransform);

                    FinalizeApproximateSignedDistanceJob finalizeJob = new FinalizeApproximateSignedDistanceJob
                    {
                        input = input,
                        output = output,
                        distanceToBackground = distanceToBackground,
                        threshold = threshold,
                        sourceChannel = sourceChannel
                    };
                    finalizeJob.Schedule(output.Length, 128, transforms).Complete();
                }
            }
            finally
            {
                distanceToBackground.Dispose();
            }
        }

        private static void ComputeExactSignedDistance(
            NativeArray<Color32> input,
            NativeArray<float> output,
            NativeArray<float> distanceToObject,
            NativeArray<float> distanceToBackground,
            int width,
            int height,
            byte threshold,
            int sourceChannel,
            bool antialiased)
        {
            int threadCount = JobsUtility.ThreadIndexCount;
            int maximumLineLength = math.max(width, height);
            NativeArray<float> temporary = new NativeArray<float>(
                input.Length,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<int> vertices = new NativeArray<int>(
                maximumLineLength * threadCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<float> boundaries = new NativeArray<float>(
                (maximumLineLength + 1) * threadCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<byte> coverageFlags = new NativeArray<byte>(
                threadCount * 2,
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);

            try
            {
                float maximumSquaredDistance = (float)width * width + (float)height * height;
                InitializeExactDistancesJob initializeJob = new InitializeExactDistancesJob
                {
                    input = input,
                    distanceToObject = distanceToObject,
                    distanceToBackground = distanceToBackground,
                    coverageFlags = coverageFlags,
                    largeValue = maximumSquaredDistance * 4f + 1f,
                    threshold = threshold,
                    sourceChannel = sourceChannel
                };
                initializeJob.Schedule(input.Length, 128).Complete();

                bool hasObject = false;
                bool hasBackground = false;
                for (int i = 0; i < threadCount; i++)
                {
                    hasObject |= coverageFlags[i * 2] != 0;
                    hasBackground |= coverageFlags[i * 2 + 1] != 0;
                }

                if (!hasObject || !hasBackground)
                {
                    FillDistanceJob fillJob = new FillDistanceJob
                    {
                        output = output,
                        value = (hasObject ? -1f : 1f) * (antialiased ? 1e10f : math.sqrt(maximumSquaredDistance))
                    };
                    fillJob.Schedule(output.Length, 128).Complete();
                    return;
                }

                if (antialiased)
                {
                    ContourCrossingsJob crossings = new ContourCrossingsJob
                    {
                        input = input, output = temporary, threshold = threshold, sourceChannel = sourceChannel,
                        lineLength = width, lineStride = 1, lineStartStride = width,
                        largeValue = maximumSquaredDistance * 4f + 1f
                    };
                    JobHandle horizontalSeeds = crossings.Schedule(height, 1);
                    ExactDistanceTransformPassJob transform = new ExactDistanceTransformPassJob
                    {
                        input = temporary, output = output, vertices = vertices, boundaries = boundaries,
                        scratchLineLength = maximumLineLength,
                        lineLength = height, lineStride = width, lineStartStride = 1
                    };
                    JobHandle horizontalField = transform.Schedule(width, 1, horizontalSeeds);
                    crossings.lineLength = height;
                    crossings.lineStride = width;
                    crossings.lineStartStride = 1;
                    JobHandle verticalSeeds = crossings.Schedule(width, 1, horizontalField);
                    transform.output = distanceToBackground;
                    transform.lineLength = width;
                    transform.lineStride = 1;
                    transform.lineStartStride = width;
                    JobHandle verticalField = transform.Schedule(height, 1, verticalSeeds);
                    FinalizeContourDistanceJob finalizeContour = new FinalizeContourDistanceJob
                    {
                        input = input, output = output, other = distanceToBackground,
                        threshold = threshold, sourceChannel = sourceChannel
                    };
                    finalizeContour.Schedule(output.Length, 128, verticalField).Complete();
                    return;
                }

                // The exact 2D EDT is separable. Lines within each vertical or horizontal pass
                // are independent; each worker receives its own small envelope scratch slice.
                JobHandle objectTransform = ScheduleExactTransform(
                    distanceToObject,
                    temporary,
                    vertices,
                    boundaries,
                    width,
                    height,
                    maximumLineLength,
                    default);
                JobHandle backgroundTransform = ScheduleExactTransform(
                    distanceToBackground,
                    temporary,
                    vertices,
                    boundaries,
                    width,
                    height,
                    maximumLineLength,
                    objectTransform);

                FinalizeExactSignedDistanceJob finalizeJob = new FinalizeExactSignedDistanceJob
                {
                    input = input,
                    output = output,
                    distanceToBackground = distanceToBackground,
                    threshold = threshold,
                    sourceChannel = sourceChannel
                };
                finalizeJob.Schedule(output.Length, 128, backgroundTransform).Complete();
            }
            finally
            {
                temporary.Dispose();
                vertices.Dispose();
                boundaries.Dispose();
                coverageFlags.Dispose();
            }
        }

        private static JobHandle ScheduleExactTransform(
            NativeArray<float> distances,
            NativeArray<float> temporary,
            NativeArray<int> vertices,
            NativeArray<float> boundaries,
            int width,
            int height,
            int scratchLineLength,
            JobHandle dependency)
        {
            ExactDistanceTransformPassJob verticalJob = new ExactDistanceTransformPassJob
            {
                input = distances,
                output = temporary,
                vertices = vertices,
                boundaries = boundaries,
                lineLength = height,
                lineStride = width,
                lineStartStride = 1,
                scratchLineLength = scratchLineLength
            };
            JobHandle vertical = verticalJob.Schedule(width, 1, dependency);

            ExactDistanceTransformPassJob horizontalJob = new ExactDistanceTransformPassJob
            {
                input = temporary,
                output = distances,
                vertices = vertices,
                boundaries = boundaries,
                lineLength = width,
                lineStride = 1,
                lineStartStride = width,
                scratchLineLength = scratchLineLength
            };
            return horizontalJob.Schedule(height, 1, vertical);
        }
    }

    [BurstCompile]
    internal struct InitializeApproximateDistancesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> input;
        [WriteOnly] public NativeArray<float> distanceToObject;
        [WriteOnly] public NativeArray<float> distanceToBackground;
        public byte threshold;
        public int sourceChannel;

        public void Execute(int index)
        {
            const float infinity = 1e10f;
            bool isObject = DistanceFieldSource.IsObject(input[index], sourceChannel, threshold);
            distanceToObject[index] = isObject ? 0f : infinity;
            distanceToBackground[index] = isObject ? infinity : 0f;
        }
    }

    [BurstCompile]
    internal struct ApproximateDistanceTransformJob : IJob
    {
        public NativeArray<float> distances;
        public int width;
        public int height;
        public float diagonalCost;

        public void Execute()
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    float best = distances[index];
                    if (x > 0)
                        best = math.min(best, distances[index - 1] + 1f);
                    if (y > 0)
                    {
                        best = math.min(best, distances[index - width] + 1f);
                        if (x > 0)
                            best = math.min(best, distances[index - width - 1] + diagonalCost);
                        if (x < width - 1)
                            best = math.min(best, distances[index - width + 1] + diagonalCost);
                    }
                    distances[index] = best;
                }
            }

            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = width - 1; x >= 0; x--)
                {
                    int index = y * width + x;
                    float best = distances[index];
                    if (x < width - 1)
                        best = math.min(best, distances[index + 1] + 1f);
                    if (y < height - 1)
                    {
                        best = math.min(best, distances[index + width] + 1f);
                        if (x > 0)
                            best = math.min(best, distances[index + width - 1] + diagonalCost);
                        if (x < width - 1)
                            best = math.min(best, distances[index + width + 1] + diagonalCost);
                    }
                    distances[index] = best;
                }
            }
        }
    }

    [BurstCompile]
    internal struct FinalizeApproximateSignedDistanceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> input;
        [ReadOnly] public NativeArray<float> distanceToBackground;
        public NativeArray<float> output;
        public byte threshold;
        public int sourceChannel;

        public void Execute(int index)
        {
            bool isObject = DistanceFieldSource.IsObject(input[index], sourceChannel, threshold);
            if (isObject)
                output[index] = -distanceToBackground[index];
        }
    }

    [BurstCompile]
    internal struct InitializeExactDistancesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> input;
        [WriteOnly] public NativeArray<float> distanceToObject;
        [WriteOnly] public NativeArray<float> distanceToBackground;
        [NativeDisableParallelForRestriction] public NativeArray<byte> coverageFlags;
        public float largeValue;
        public byte threshold;
        public int sourceChannel;
        [NativeSetThreadIndex] private int threadIndex;

        public void Execute(int index)
        {
            bool isObject = DistanceFieldSource.IsObject(input[index], sourceChannel, threshold);
            distanceToObject[index] = isObject ? 0f : largeValue;
            distanceToBackground[index] = isObject ? largeValue : 0f;
            coverageFlags[threadIndex * 2 + (isObject ? 0 : 1)] = 1;
        }
    }

    [BurstCompile]
    internal struct ContourCrossingsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> input;
        [NativeDisableParallelForRestriction] public NativeArray<float> output;
        public int lineLength, lineStride, lineStartStride;
        public int sourceChannel;
        public byte threshold;
        public float largeValue;

        public void Execute(int lineIndex)
        {
            int start = lineIndex * lineStartStride;
            for (int direction = 0; direction < 2; direction++)
            {
                int step = direction == 0 ? 1 : -1;
                int first = direction == 0 ? 0 : lineLength - 1;
                float nearest = -1e10f;
                for (int q = first; q >= 0 && q < lineLength; q += step)
                {
                    int index = start + q * lineStride;
                    int previous = q - step;
                    if (previous >= 0 && previous < lineLength)
                    {
                        float a = DistanceFieldSource.Value(input[start + previous * lineStride], sourceChannel);
                        float b = DistanceFieldSource.Value(input[index], sourceChannel);
                        if ((a > threshold) != (b > threshold))
                            nearest = previous + step * (threshold - a) / (b - a);
                    }
                    float delta = q - nearest;
                    float squared = math.min(largeValue, delta * delta);
                    output[index] = direction == 0 ? squared : math.min(output[index], squared);
                }
            }
        }
    }

    [BurstCompile]
    internal struct FinalizeContourDistanceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> input;
        [ReadOnly] public NativeArray<float> other;
        public NativeArray<float> output;
        public int sourceChannel;
        public byte threshold;

        public void Execute(int index)
        {
            float distance = math.sqrt(math.min(output[index], other[index]));
            output[index] = DistanceFieldSource.IsObject(input[index], sourceChannel, threshold) ? -distance : distance;
        }
    }

    [BurstCompile]
    internal struct FillDistanceJob : IJobParallelFor
    {
        [WriteOnly] public NativeArray<float> output;
        public float value;

        public void Execute(int index)
        {
            output[index] = value;
        }
    }

    [BurstCompile]
    internal struct ExactDistanceTransformPassJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> input;
        [NativeDisableParallelForRestriction] public NativeArray<float> output;
        [NativeDisableParallelForRestriction] public NativeArray<int> vertices;
        [NativeDisableParallelForRestriction] public NativeArray<float> boundaries;
        public int lineLength;
        public int lineStride;
        public int lineStartStride;
        public int scratchLineLength;
        [NativeSetThreadIndex] private int threadIndex;

        public void Execute(int lineIndex)
        {
            int lineStart = lineIndex * lineStartStride;
            int vertexStart = threadIndex * scratchLineLength;
            int boundaryStart = threadIndex * (scratchLineLength + 1);
            int envelopeSize = 0;
            vertices[vertexStart] = 0;
            boundaries[boundaryStart] = -1e20f;
            boundaries[boundaryStart + 1] = 1e20f;

            for (int q = 1; q < lineLength; q++)
            {
                float intersection;
                while (true)
                {
                    int p = vertices[vertexStart + envelopeSize];
                    float qValue = input[lineStart + q * lineStride];
                    float pValue = input[lineStart + p * lineStride];
                    intersection = ((qValue + q * q) - (pValue + p * p)) / (2f * (q - p));
                    if (intersection > boundaries[boundaryStart + envelopeSize])
                        break;
                    envelopeSize--;
                }

                envelopeSize++;
                vertices[vertexStart + envelopeSize] = q;
                boundaries[boundaryStart + envelopeSize] = intersection;
                boundaries[boundaryStart + envelopeSize + 1] = 1e20f;
            }

            envelopeSize = 0;
            for (int q = 0; q < lineLength; q++)
            {
                while (boundaries[boundaryStart + envelopeSize + 1] < q)
                    envelopeSize++;
                int p = vertices[vertexStart + envelopeSize];
                float delta = q - p;
                output[lineStart + q * lineStride] =
                    delta * delta + input[lineStart + p * lineStride];
            }
        }
    }

    [BurstCompile]
    internal struct FinalizeExactSignedDistanceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> input;
        [ReadOnly] public NativeArray<float> distanceToBackground;
        public NativeArray<float> output;
        public byte threshold;
        public int sourceChannel;

        public void Execute(int index)
        {
            bool isObject = DistanceFieldSource.IsObject(input[index], sourceChannel, threshold);
            output[index] = isObject
                ? -math.sqrt(distanceToBackground[index])
                : math.sqrt(output[index]);
        }
    }

    [BurstCompile]
    internal static class DistanceFieldSource
    {
        public static bool IsObject(Color32 color, int sourceChannel, byte threshold)
        {
            return Value(color, sourceChannel) > threshold;
        }

        public static float Value(Color32 color, int sourceChannel)
        {
            float value;
            switch (sourceChannel)
            {
                case 1:
                    value = color.r;
                    break;
                case 2:
                    value = color.g;
                    break;
                case 3:
                    value = color.b;
                    break;
                case 4:
                    value = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
                    break;
                default:
                    value = color.a;
                    break;
            }
            return value;
        }
    }
}
