using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class OutlineLayerBehaviour : TargetedLayerBehaviour
    {
        public DistanceMetric metric = DistanceMetric.EuclideanAntialiased;
        public SharpDistanceMode sharpMode = SharpDistanceMode.Unrestricted;
        public float miterLimit = 4f;
        public SourceChannel sourceChannel = SourceChannel.Alpha;
        public Color outlineColor = Color.white;
        public float outlineWidth = 4f;
        public float outlineSoftness = 1f;
        public float outlineOffset;
        public bool fillCenter;
        public Color fillColor = Color.white;
        public OutlinePosition outlinePosition = OutlinePosition.Outside;
        [NonSerialized] private ReconstructedDistanceCache reconstructedCache;
        internal override bool RequiresColorInput => sourceChannel != SourceChannel.Alpha;

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            using var timing = DistanceFieldTiming.Measure("render.outline");
            if (context.input == null)
            {
                ReleaseDistancePreparation();
                return null;
            }

            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            Texture2D inputTexture = null;
            NativeArray<float> signedDistances = default;
            Texture2D resultTexture = null;
            try
            {
                using (DistanceFieldTiming.Measure("input.readback"))
                    inputTexture = WhimTexDocument.CopyToTexture2D(context.input, uploadToGpu: false);
                NativeArray<Color32> inputPixels = inputTexture.GetRawTextureData<Color32>();
                signedDistances = new NativeArray<float>(
                    inputPixels.Length,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                bool cacheDistances = ReconstructedDistanceCache.Supports(metric);
                if (cacheDistances) reconstructedCache ??= new ReconstructedDistanceCache();
                else ReleaseDistancePreparation();
                SDFLayerBehaviour.ComputeCachedSourceField(
                    inputPixels,
                    signedDistances,
                    context.width,
                    context.height,
                    128,
                    (int)sourceChannel,
                    metric, Vector2.zero, SDFLayerBehaviour.SourceEdges.Transparent, miterLimit, default, 0, reconstructedCache, sharpMode);

                long stage = DistanceFieldTiming.Start();
                resultTexture = new Texture2D(context.width, context.height, TextureFormat.RGBAFloat, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                NativeArray<Color> outputPixels = resultTexture.GetRawTextureData<Color>();
                OutlineJob job = new OutlineJob
                {
                    signedDistances = signedDistances,
                    output = outputPixels,
                    outlineWidth = Mathf.Max(0f, outlineWidth * context.pixelScale / context.scaleMultiplier),
                    outlineSoftness = Mathf.Max(0f, outlineSoftness * context.pixelScale / context.scaleMultiplier),
                    outlineOffset = outlineOffset * context.pixelScale / context.scaleMultiplier,
                    fillCenter = fillCenter,
                    fillColor = HdrUtility.Decode(fillColor),
                    outlineColor = HdrUtility.Decode(outlineColor),
                    antialiasedDistance = true,
                    width = context.width,
                    height = context.height,
                    outlinePosition = (int)outlinePosition
                };
                DistanceFieldTiming.Report("output.setup", ref stage);
                using (DistanceFieldTiming.Measure("outline.shade"))
                    job.Schedule(outputPixels.Length, 128).Complete();

                stage = DistanceFieldTiming.Start();
                resultTexture.Apply(false, false);
                DistanceFieldTiming.Report("output.upload", ref stage);
                var result = ApplyTransformAndFx(resultTexture, context);
                DistanceFieldTiming.Report("output.submit", ref stage);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                if (signedDistances.IsCreated)
                    signedDistances.Dispose();
                if (inputTexture != null)
                    UnityEngine.Object.DestroyImmediate(inputTexture);
                if (resultTexture != null)
                    UnityEngine.Object.DestroyImmediate(resultTexture);
            }
        }

        internal void ReleaseDistancePreparation()
        {
            reconstructedCache?.Dispose(); reconstructedCache = null;
        }

        internal override void ReleaseTransientResources()
        {
            ReleaseDistancePreparation();
            base.ReleaseTransientResources();
        }

        public override string ToString()
        {
            return $"Outline: {outlineWidth:0.##} px";
        }

        public enum OutlinePosition
        {
            Outside,
            Inside,
            Center
        }

        public enum SourceChannel
        {
            Alpha,
            Red,
            Green,
            Blue,
            Luminance
        }
    }

    [BurstCompile]
    internal struct OutlineJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> signedDistances;
        public NativeArray<Color> output;
        public float outlineWidth;
        public float outlineSoftness;
        public float outlineOffset;
        public bool fillCenter;
        public Color fillColor;
        public Color outlineColor;
        public int outlinePosition;
        public bool antialiasedDistance;
        public int width, height;

        public void Execute(int index)
        {
            float distance = signedDistances[index];
            if (!antialiasedDistance)
                distance = math.sign(distance) * math.max(0f, math.abs(distance) - 0.5f);
            float lower = outlinePosition == (int)OutlineLayerBehaviour.OutlinePosition.Outside ? 0f
                : outlinePosition == (int)OutlineLayerBehaviour.OutlinePosition.Inside ? -outlineWidth : -outlineWidth * 0.5f;
            lower += outlineOffset;
            float upper = lower + outlineWidth;
            float pixelSpan = 1f;
            if (antialiasedDistance)
            {
                int x = index % width;
                int y = index / width;
                float dx = math.max(x > 0 ? math.abs(distance - signedDistances[index - 1]) : 0f,
                    x + 1 < width ? math.abs(distance - signedDistances[index + 1]) : 0f);
                float dy = math.max(y > 0 ? math.abs(distance - signedDistances[index - width]) : 0f,
                    y + 1 < height ? math.abs(distance - signedDistances[index + width]) : 0f);
                pixelSpan = math.max(1f, dx + dy);
            }
            float transition = math.max(pixelSpan, outlineSoftness);
            float inner = 1f - math.saturate((distance - lower) / transition + 0.5f);
            float outer = 1f - math.saturate((distance - upper) / transition + 0.5f);
            float borderAlpha = (outer - inner) * outlineColor.a;
            if (!fillCenter)
            {
                output[index] = new Color(outlineColor.r, outlineColor.g, outlineColor.b, borderAlpha);
                return;
            }
            float centerAlpha = inner * fillColor.a;
            float alpha = borderAlpha + centerAlpha;

            output[index] = new Color(
                alpha > 0f ? (outlineColor.r * borderAlpha + fillColor.r * centerAlpha) / alpha : outlineColor.r,
                alpha > 0f ? (outlineColor.g * borderAlpha + fillColor.g * centerAlpha) / alpha : outlineColor.g,
                alpha > 0f ? (outlineColor.b * borderAlpha + fillColor.b * centerAlpha) / alpha : outlineColor.b,
                alpha);
        }
    }
}
