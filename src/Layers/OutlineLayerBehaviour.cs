using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "OutlineLayerBehaviour")]
    [Serializable]
    public sealed class OutlineLayerBehaviour : TargetedLayerBehaviour
    {
        public DistanceMetric metric = DistanceMetric.EuclideanExact;
        public SourceChannel sourceChannel = SourceChannel.Alpha;
        public Color outlineColor = Color.white;
        public float outlineWidth = 4f;
        public float outlineSoftness = 1f;
        public float outlineOffset;
        public bool fillCenter;
        public Color fillColor = Color.white;
        public OutlinePosition outlinePosition = OutlinePosition.Outside;
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
                    128,
                    (int)sourceChannel,
                    metric);

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
                    outlineWidth = Mathf.Max(0f, outlineWidth / context.scaleMultiplier),
                    outlineSoftness = Mathf.Max(0f, outlineSoftness / context.scaleMultiplier),
                    outlineOffset = outlineOffset / context.scaleMultiplier,
                    fillCenter = fillCenter,
                    fillColor = HdrUtility.Decode(fillColor),
                    outlineColor = HdrUtility.Decode(outlineColor),
                    antialiasedDistance = metric == DistanceMetric.EuclideanAntialiased,
                    width = context.width,
                    height = context.height,
                    outlinePosition = (int)outlinePosition
                };
                job.Schedule(outputPixels.Length, 128).Complete();

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
