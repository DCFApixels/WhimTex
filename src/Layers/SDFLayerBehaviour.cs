using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class SDFLayerBehaviour : TargetedLayerBehaviour
    {
        public enum OutputEncoding { LinearData, Gradient, MultiChannelData }
        public DistanceMetric metric = DistanceMetric.EuclideanAntialiased;
        public float miterLimit = 4f;
        public SourceChannel sourceChannel = SourceChannel.Alpha;
        [Range(0, 255)] public byte threshold = 128;
        public DistancePosition distancePosition = DistancePosition.Signed;
        public bool inverted;
        public float maxDistanceNormalization;
        public Vector2 sourceOffset;
        public SourceEdges sourceEdges = SourceEdges.Transparent;
        public float contourOffset;
        public float insideDistance;
        public float outsideDistance;
        public AnimationCurve profile = AnimationCurve.Linear(0, 0, 1, 1);
        public OutputEncoding encoding = OutputEncoding.Gradient;
        public WhimTexGradient gradient = new WhimTexGradient();

        [NonSerialized] private WhimTexGradientTexture gradientLut;
        [NonSerialized] private Material gradientMaterial;
        [NonSerialized] private WhimTexCurveTexture profileLut;
        internal override bool RequiresColorInput => sourceChannel != SourceChannel.Alpha;

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            using var timing = DistanceFieldTiming.Measure("render.sdf");
            if (context.input == null)
                return null;
            if (encoding == OutputEncoding.MultiChannelData && metric != DistanceMetric.MTSDF)
                throw new InvalidOperationException("Multi Channel Data output requires the MTSDF distance algorithm.");

            Texture2D inputTexture = null;
            NativeArray<float> signedDistances = default;
            NativeArray<float4> multiChannel = default;
            Texture2D resultTexture = null;
            RenderTexture colored = null;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                using (DistanceFieldTiming.Measure("input.readback"))
                    inputTexture = WhimTexDocument.CopyToTexture2D(context.input, uploadToGpu: false);
                NativeArray<Color32> inputPixels = inputTexture.GetRawTextureData<Color32>();
                signedDistances = new NativeArray<float>(
                    inputPixels.Length,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                if (metric == DistanceMetric.MTSDF)
                    multiChannel = new NativeArray<float4>(inputPixels.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                ComputeSourceField(
                    inputPixels,
                    signedDistances,
                    context.width,
                    context.height,
                    threshold,
                    (int)sourceChannel,
                    metric, sourceOffset / context.scaleMultiplier, sourceEdges, miterLimit, multiChannel);

                long stage = DistanceFieldTiming.Start();
                resultTexture = new Texture2D(context.width, context.height, multiChannel.IsCreated ? TextureFormat.RGBAFloat : TextureFormat.RFloat, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                };
                if (multiChannel.IsCreated) resultTexture.SetPixelData(multiChannel, 0);
                else resultTexture.SetPixelData(signedDistances, 0);
                resultTexture.Apply(false, false);
                DistanceFieldTiming.Report("output.upload", ref stage);
                if (gradientMaterial == null)
                {
                    var shader = Shader.Find("Hidden/WhimTex/SdfGradient");
                    if (shader == null || !shader.isSupported) throw new InvalidOperationException("SDF gradient shader unavailable.");
                    gradientMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                bool applyGradient = encoding == OutputEncoding.Gradient;
                gradientMaterial.SetInt("_MultiChannel", multiChannel.IsCreated ? 1 : 0);
                gradientMaterial.SetInt("_DataOutput", encoding == OutputEncoding.MultiChannelData ? 1 : 0);
                gradientMaterial.SetInt("_UseGradient", applyGradient ? 1 : 0);
                if (applyGradient)
                {
                    gradient ??= new WhimTexGradient();
                    gradientLut ??= new WhimTexGradientTexture();
                    gradientMaterial.SetTexture("_GradientLut", gradientLut.GetTexture(gradient, ColorSpace.Gamma));
                    gradientMaterial.SetInt("_GradientWrapMode", (int)gradient.WrapMode);
                }
                gradientMaterial.SetFloat("_MaxDistance", GetNormalizationDistance(context));
                float fallback = GetNormalizationDistance(context);
                gradientMaterial.SetFloat("_InsideDistance", insideDistance > 0 ? insideDistance / context.scaleMultiplier : fallback);
                gradientMaterial.SetFloat("_OutsideDistance", outsideDistance > 0 ? outsideDistance / context.scaleMultiplier : fallback);
                gradientMaterial.SetFloat("_ContourOffset", contourOffset / context.scaleMultiplier);
                profileLut ??= new WhimTexCurveTexture();
                gradientMaterial.SetTexture("_ProfileLut", profileLut.GetTexture(profile));
                gradientMaterial.SetInt("_Position", (int)distancePosition);
                gradientMaterial.SetInt("_Inverted", inverted ? 1 : 0);
                colored = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                GL.sRGBWrite = false;
                Graphics.Blit(resultTexture, colored, gradientMaterial);
                var result = ApplyTransformAndFx(colored, context);
                DistanceFieldTiming.Report("output.submit", ref stage);
                return result;
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                if (colored != null) RenderTexture.ReleaseTemporary(colored);
                if (signedDistances.IsCreated)
                    signedDistances.Dispose();
                if (multiChannel.IsCreated) multiChannel.Dispose();
                if (inputTexture != null)
                    UnityEngine.Object.DestroyImmediate(inputTexture);
                if (resultTexture != null)
                    UnityEngine.Object.DestroyImmediate(resultTexture);
            }
        }

        internal override void ReleaseTransientResources()
        {
            gradientLut?.Dispose(); gradientLut = null;
            profileLut?.Dispose(); profileLut = null;
            if (gradientMaterial != null) UnityEngine.Object.DestroyImmediate(gradientMaterial);
            gradientMaterial = null;
            base.ReleaseTransientResources();
        }

        public override string ToString()
        {
            return "SDF";
        }

        private float GetNormalizationDistance(in LayerRenderContext context)
        {
            if (maxDistanceNormalization > 0f)
                return Mathf.Max(0.0001f, maxDistanceNormalization / context.scaleMultiplier);

            switch (metric)
            {
                case DistanceMetric.Diamond:
                    return Mathf.Max(1f, context.width + context.height);
                case DistanceMetric.Square:
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

        public enum SourceEdges { Transparent, Clamp, Repeat, Mirror }

        internal static void ComputeSourceDistance(NativeArray<Color32> source, NativeArray<float> output,
            int width, int height, byte threshold, int channel, DistanceMetric metric, Vector2 offset, SourceEdges edges)
            => ComputeSourceField(source, output, width, height, threshold, channel, metric, offset, edges, 4f, default);

        internal static void ComputeSourceField(NativeArray<Color32> source, NativeArray<float> output,
            int width, int height, byte threshold, int channel, DistanceMetric metric, Vector2 offset, SourceEdges edges,
            float miterLimit, NativeArray<float4> multiChannel)
        {
            using var timing = DistanceFieldTiming.Measure("field.total");
            // Translation commutes with the distance transform. Keep the source contour intact,
            // then sample the translated field; do not crop a shifted mask first.
            if (edges == SourceEdges.Repeat || edges == SourceEdges.Mirror)
            {
                offset.x = Mathf.Repeat(offset.x, width * (edges == SourceEdges.Mirror ? 2 : 1));
                offset.y = Mathf.Repeat(offset.y, height * (edges == SourceEdges.Mirror ? 2 : 1));
            }
            int left = edges == SourceEdges.Repeat ? -width : Mathf.Min(-1, Mathf.FloorToInt(-offset.x) - 1);
            int bottom = edges == SourceEdges.Repeat ? -height : Mathf.Min(-1, Mathf.FloorToInt(-offset.y) - 1);
            int right = edges == SourceEdges.Repeat ? width * 2 : Mathf.Max(width, Mathf.CeilToInt(width - 1 - offset.x) + 1);
            int top = edges == SourceEdges.Repeat ? height * 2 : Mathf.Max(height, Mathf.CeilToInt(height - 1 - offset.y) + 1);
            // Mirror queries are folded into the original tile; reflected contours cannot be
            // closer than their counterpart inside that tile.
            if (edges == SourceEdges.Mirror) { left = bottom = -1; right = width; top = height; }
            int w = right - left + 1, h = top - bottom + 1;
            if ((long)w * h > 67108864 || w <= 0 || h <= 0)
                throw new InvalidOperationException("SDF source offset/edge expansion exceeds 64 million pixels. Reduce the source offset or canvas resolution.");
            using var extended = new NativeArray<Color32>(w * h, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using var distances = new NativeArray<float>(w * h, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using (DistanceFieldTiming.Measure("source.extend"))
            new SdfExtendSourceJob { source = source, output = extended, width = width, height = height,
                extendedWidth = w, left = left, bottom = bottom, edges = (int)edges }.Schedule(extended.Length, 128).Complete();
            if (multiChannel.IsCreated)
            {
                using var channels = new NativeArray<float4>(w * h, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                DistanceFieldUtility.ComputeMtsdfDistance(extended, channels, w, h, threshold, channel);
                using (DistanceFieldTiming.Measure("source.sample"))
                new SdfSampleMultiSourceJob { source = channels, output = multiChannel, scalar = output,
                    width = width, height = height, extendedWidth = w, extendedHeight = h,
                    left = left, bottom = bottom, offset = offset, edges = (int)edges
                }.Schedule(output.Length, 128).Complete();
                return;
            }
            DistanceFieldUtility.ComputeArtisticDistance(extended, distances, w, h, threshold, channel, metric, miterLimit);
            using (DistanceFieldTiming.Measure("source.sample"))
            new SdfSampleSourceJob { source = distances, output = output, width = width, height = height,
                extendedWidth = w, extendedHeight = h, left = left, bottom = bottom,
                offset = offset, edges = (int)edges }.Schedule(output.Length, 128).Complete();
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
    internal struct SdfExtendSourceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> source;
        [WriteOnly] public NativeArray<Color32> output;
        public int width, height, extendedWidth, left, bottom, edges;
        public void Execute(int i)
        {
            int x = i % extendedWidth + left, y = i / extendedWidth + bottom;
            if (edges == 0 && (x < 0 || y < 0 || x >= width || y >= height)) { output[i] = default; return; }
            if (edges == 2) { x = ((x % width) + width) % width; y = ((y % height) + height) % height; }
            else { x = math.clamp(x, 0, width - 1); y = math.clamp(y, 0, height - 1); }
            output[i] = source[y * width + x];
        }
    }

    [BurstCompile]
    internal struct SdfSampleSourceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> source;
        [WriteOnly] public NativeArray<float> output;
        public int width, height, extendedWidth, extendedHeight, left, bottom, edges;
        public Vector2 offset;
        internal static float Fold(float x, int size)
        {
            float p = x + .5f;
            p -= math.floor(p / (2 * size)) * (2 * size);
            return math.clamp(math.min(p, 2 * size - p) - .5f, 0, size - 1);
        }
        public void Execute(int i)
        {
            float x = i % width - offset.x, y = i / width - offset.y;
            if (edges == 2) { x -= math.floor(x / width) * width; y -= math.floor(y / height) * height; }
            if (edges == 3) { x = Fold(x, width); y = Fold(y, height); }
            x -= left; y -= bottom;
            int x0 = math.clamp((int)math.floor(x), 0, extendedWidth - 1), y0 = math.clamp((int)math.floor(y), 0, extendedHeight - 1);
            int x1 = math.min(x0 + 1, extendedWidth - 1), y1 = math.min(y0 + 1, extendedHeight - 1);
            output[i] = math.lerp(math.lerp(source[y0 * extendedWidth + x0], source[y0 * extendedWidth + x1], math.frac(x)),
                math.lerp(source[y1 * extendedWidth + x0], source[y1 * extendedWidth + x1], math.frac(x)), math.frac(y));
        }
    }

    [BurstCompile]
    internal struct SdfSampleMultiSourceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float4> source;
        [WriteOnly] public NativeArray<float4> output;
        [WriteOnly] public NativeArray<float> scalar;
        public int width, height, extendedWidth, extendedHeight, left, bottom, edges;
        public Vector2 offset;
        public void Execute(int i)
        {
            float x = i % width - offset.x, y = i / width - offset.y;
            if (edges == 2) { x -= math.floor(x / width) * width; y -= math.floor(y / height) * height; }
            if (edges == 3) { x = SdfSampleSourceJob.Fold(x, width); y = SdfSampleSourceJob.Fold(y, height); }
            x -= left; y -= bottom;
            int x0 = math.clamp((int)math.floor(x), 0, extendedWidth - 1), y0 = math.clamp((int)math.floor(y), 0, extendedHeight - 1);
            int x1 = math.min(x0 + 1, extendedWidth - 1), y1 = math.min(y0 + 1, extendedHeight - 1);
            float4 value = math.lerp(math.lerp(source[y0 * extendedWidth + x0], source[y0 * extendedWidth + x1], math.frac(x)),
                math.lerp(source[y1 * extendedWidth + x0], source[y1 * extendedWidth + x1], math.frac(x)), math.frac(y));
            output[i] = value;
            scalar[i] = ArtisticDistanceField.Median(value.xyz);
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
            NativeArray<Color32> input, NativeArray<float> output, int width, int height,
            byte threshold, int sourceChannel, DistanceMetric metric)
            => ComputeArtisticDistance(input, output, width, height, threshold, sourceChannel, metric, 4f);

        public static void ComputeArtisticDistance(
            NativeArray<Color32> input, NativeArray<float> output, int width, int height,
            byte threshold, int sourceChannel, DistanceMetric metric, float miterLimit)
        {
            Validate(input.Length, output.Length, width, height, sourceChannel, metric, miterLimit);
            if (metric != DistanceMetric.EuclideanAntialiased && width > 1 && height > 1)
            {
                ArtisticDistanceField.Compute(input, output, width, height, threshold, sourceChannel, metric, miterLimit);
                return;
            }
            using var background = new NativeArray<float>(input.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            ComputeAntialiasedSignedDistance(input, output, background, width, height, threshold, sourceChannel);
        }

        public static void ComputeMtsdfDistance(
            NativeArray<Color32> input, NativeArray<float4> output, int width, int height, byte threshold, int sourceChannel)
        {
            Validate(input.Length, output.Length, width, height, sourceChannel, DistanceMetric.MTSDF, 4f);
            if (width > 1 && height > 1)
            {
                ArtisticDistanceField.ComputeMultiChannel(input, output, width, height, threshold, sourceChannel);
                return;
            }
            using var scalar = new NativeArray<float>(input.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            ComputeSignedDistance(input, scalar, width, height, threshold, sourceChannel, DistanceMetric.EuclideanAntialiased);
            for (int i = 0; i < output.Length; i++) output[i] = new float4(scalar[i]);
        }

        static void Validate(int inputLength, int outputLength, int width, int height,
            int channel, DistanceMetric metric, float miterLimit)
        {
            if (width <= 0 || height <= 0 || (long)width * height != inputLength || outputLength != inputLength)
                throw new ArgumentException("Distance field dimensions do not match the pixel buffers.");
            if (channel < 0 || channel > 4) throw new ArgumentOutOfRangeException(nameof(channel));
            if (!Enum.IsDefined(typeof(DistanceMetric), metric)) throw new ArgumentOutOfRangeException(nameof(metric));
            if (!float.IsFinite(miterLimit) || miterLimit < 1f || miterLimit > 64f) throw new ArgumentOutOfRangeException(nameof(miterLimit));
        }

        private static void ComputeAntialiasedSignedDistance(
            NativeArray<Color32> input, NativeArray<float> output, NativeArray<float> other,
            int width, int height, byte threshold, int sourceChannel)
        {
            int threadCount = JobsUtility.ThreadIndexCount;
            int maximumLineLength = math.max(width, height);
            using var temporary = new NativeArray<float>(input.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using var vertices = new NativeArray<int>(maximumLineLength * threadCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using var boundaries = new NativeArray<float>((maximumLineLength + 1) * threadCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using var coverageFlags = new NativeArray<byte>(threadCount * 2, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            new DistanceCoverageJob { input = input, coverageFlags = coverageFlags,
                threshold = threshold, sourceChannel = sourceChannel }.Schedule(input.Length, 128).Complete();
            bool hasObject = false, hasBackground = false;
            for (int i = 0; i < threadCount; i++)
            {
                hasObject |= coverageFlags[i * 2] != 0;
                hasBackground |= coverageFlags[i * 2 + 1] != 0;
            }
            if (!hasObject || !hasBackground)
            {
                new FillDistanceJob { output = output, value = hasObject ? -1e10f : 1e10f }.Schedule(output.Length, 128).Complete();
                return;
            }
            ContourCrossingsJob crossings = new ContourCrossingsJob
            {
                input = input, output = temporary, threshold = threshold, sourceChannel = sourceChannel,
                lineLength = width, lineStride = 1, lineStartStride = width,
                largeValue = ((float)width * width + (float)height * height) * 4f + 1f
            };
            JobHandle horizontalSeeds = crossings.Schedule(height, 1);
            ExactDistanceTransformPassJob transform = new ExactDistanceTransformPassJob
            {
                input = temporary, output = output, vertices = vertices, boundaries = boundaries,
                scratchLineLength = maximumLineLength,
                lineLength = height, lineStride = width, lineStartStride = 1
            };
            JobHandle horizontalField = transform.Schedule(width, 1, horizontalSeeds);
            crossings.lineLength = height; crossings.lineStride = width; crossings.lineStartStride = 1;
            JobHandle verticalSeeds = crossings.Schedule(width, 1, horizontalField);
            transform.output = other; transform.lineLength = width; transform.lineStride = 1; transform.lineStartStride = width;
            JobHandle verticalField = transform.Schedule(height, 1, verticalSeeds);
            new FinalizeContourDistanceJob
            {
                input = input, output = output, other = other, threshold = threshold, sourceChannel = sourceChannel
            }.Schedule(output.Length, 128, verticalField).Complete();
        }
    }

    [BurstCompile]
    internal struct DistanceCoverageJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> input;
        [NativeDisableParallelForRestriction] public NativeArray<byte> coverageFlags;
        public byte threshold;
        public int sourceChannel;
        [NativeSetThreadIndex] private int threadIndex;
        public void Execute(int index)
        {
            bool isObject = DistanceFieldSource.IsObject(input[index], sourceChannel, threshold);
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
