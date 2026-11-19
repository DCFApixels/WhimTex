using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class MorphRasterRenderer
    {
        private const int SampleAxis = 32;
        private const int WarpPass = 0, MomentsPass = 1, TranslationPass = 2, CopyPass = 3;
        private const int FeaturesPass = 4, ReducePass = 5, SolvePass = 6, GuideResidualPass = 7;
        // The reusable correspondence field is sampled at every output pixel.
        private const int MaxFlowAxis = 256, CoarseAxis = 16;
        private static Material material;
        private static Texture2D anchorTable;
        private static bool anchorUploadPending;

        static MorphRasterRenderer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            EditorApplication.quitting += Release;
        }

        // Key the one retained pair by content revisions, including animated inputs, rather than RT instance IDs.
        internal sealed class FlowCache
        {
            private (Layer, Layer, ulong, ulong, int, int, float) key;
            private RenderTexture field;

            private MorphRasterMethod method;
            private Vector4[] guides = Array.Empty<Vector4>();

            internal RenderTexture Get(RenderTexture from, RenderTexture to,
                (Layer, Layer, ulong, ulong, int, int, float) pair, MorphRasterMethod selected, IReadOnlyList<MorphAnchor> anchors)
            {
                int count = selected == MorphRasterMethod.Liao ? anchors?.Count ?? 0 : 0;
                bool reuse = field != null && field.IsCreated() && key.Equals(pair) && selected == method && guides.Length == count;
                for (int i = 0; reuse && i < count; i++)
                    reuse = guides[i].Equals(MorphGroupLayerBehaviour.Coordinates(anchors[i]));
                if (reuse) return field;

                Clear();
                field = BuildFlow(from, to, selected, anchors);
                method = selected;
                if (guides.Length != count) guides = new Vector4[count];
                for (int i = 0; i < count; i++) guides[i] = MorphGroupLayerBehaviour.Coordinates(anchors[i]);
                key = pair;
                return field;
            }

            internal void Clear()
            {
                if (field != null) ProfiledRenderTexture.ReleaseTemporary(field);
                field = null;
                key = default;
            }
        }

        // Endpoints are borrowed straight linear RGBA. The caller releases the returned temporary.
        internal static RenderTexture Render(RenderTexture from, RenderTexture to, IReadOnlyList<MorphAnchor> anchors, float t,
            FlowCache cache = null, (Layer, Layer, ulong, ulong, int, int, float) pair = default,
            MorphRasterMethod method = MorphRasterMethod.LocalContrast)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (from.width != to.width || from.height != to.height)
                throw new ArgumentException("Morph endpoints must have matching dimensions.", nameof(to));
            if (!float.IsFinite(t)) throw new ArgumentOutOfRangeException(nameof(t));

            RenderTexture previous = RenderTexture.active, output = null, temporaryFlow = null, residuals = null;
            bool srgb = GL.sRGBWrite;
            try
            {
                if (material == null)
                {
                    var shader = Shader.Find("Hidden/WhimTex/MorphRaster");
                    if (shader == null || !shader.isSupported)
                        throw new InvalidOperationException("WhimTex raster morph shader is unavailable.");
                    material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }

                int width = from.width, height = from.height;
                bool fullPrecision = from.format == RenderTextureFormat.ARGBFloat || to.format == RenderTextureFormat.ARGBFloat;
                output = HdrUtility.Temporary(width, height, fullPrecision);
                GL.sRGBWrite = false;
                material.SetVector("_RasterSize", new Vector4(width, height, 0, 0));
                if (t <= 0 || t >= 1)
                {
                    Graphics.Blit(t <= 0 ? from : to, output, material, CopyPass);
                }
                else
                {
                    int count = anchors?.Count ?? 0;
                    material.SetInteger("_AnchorCount", count);
                    material.SetFloat("_MorphTime", t);
                    material.SetTexture("_ToTex", to);
                    var flow = cache != null ? cache.Get(from, to, pair, method, anchors) : (temporaryFlow = BuildFlow(from, to, method, anchors));
                    material.SetTexture("_Flow", flow);
                    int longest = Mathf.Max(width, height);
                    material.SetFloat("_RefineSpacing", method == MorphRasterMethod.LocalContrast && longest > MaxFlowAxis ? Mathf.Max(1f, longest / (2f * MaxFlowAxis)) : 0f);
                    if (method == MorphRasterMethod.Liao) material.SetInteger("_AnchorCount", 0);
                    else if (count > 0)
                    {
                        BindAnchors(anchors);
                        int columns = Mathf.Min(count, anchorTable.width);
                        residuals = HdrUtility.Temporary(columns, 1 + (count - 1) / columns, fullPrecision);
                        Graphics.Blit(from, residuals, material, GuideResidualPass);
                        material.SetTexture("_GuideResiduals", residuals);
                    }
                    Graphics.Blit(from, output, material, WarpPass);
                }
                var result = output;
                output = null;
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                if (material != null)
                {
                    material.SetTexture("_MainTex", null);
                    material.SetTexture("_ToTex", null);
                    material.SetTexture("_GuideResiduals", null);
                    material.SetTexture("_Flow", null);
                }
                if (residuals != null) ProfiledRenderTexture.ReleaseTemporary(residuals);
                if (temporaryFlow != null) ProfiledRenderTexture.ReleaseTemporary(temporaryFlow);
                if (output != null) ProfiledRenderTexture.ReleaseTemporary(output);
            }
        }

        private static RenderTexture BuildFlow(RenderTexture from, RenderTexture to, MorphRasterMethod method, IReadOnlyList<MorphAnchor> anchors)
        {
            int width = from.width, height = from.height;
            float scale = Mathf.Min(1f, MaxFlowAxis / (float)Mathf.Max(width, height));
            int w = Mathf.Max(1, Mathf.RoundToInt(width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            // At most five levels for a 256-pixel working image.
            var levels = new RenderTexture[5];
            RenderTexture rows = null, translation = null, flow = null, next = null;
            try
            {
                int columns = Mathf.Min(width, SampleAxis), samplesHigh = Mathf.Min(height, SampleAxis);
                material.SetVector("_SampleGrid", new Vector4(columns, samplesHigh, 0, 0));
                bool fullPrecision = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat);
                rows = HdrUtility.Temporary(4, samplesHigh, fullPrecision);
                translation = HdrUtility.Temporary(3, 1, fullPrecision);
                Graphics.Blit(from, rows, material, MomentsPass);
                Graphics.Blit(rows, translation, material, TranslationPass);

                int last = 0;
                levels[0] = HdrUtility.Temporary(w, h);
                SetLevel(w, h);
                material.SetTexture("_Flow", translation);
                Graphics.Blit(from, levels[0], material, FeaturesPass);
                while (Mathf.Max(w, h) > CoarseAxis)
                {
                    w = (w + 1) / 2;
                    h = (h + 1) / 2;
                    SetLevel(w, h);
                    levels[++last] = HdrUtility.Temporary(w, h);
                    Graphics.Blit(levels[last - 1], levels[last], material, ReducePass);
                }
                bool liao = method == MorphRasterMethod.Liao;
                bool guided = liao && anchors?.Count > 0;
                if (guided) BindAnchors(anchors);
                for (int i = last; i >= 0; i--)
                {
                    var features = levels[i];
                    SetLevel(features.width, features.height);
                    next = HdrUtility.Temporary(features.width, features.height);
                    material.SetInteger("_Coarsest", i == last ? 1 : 0);
                    material.SetTexture("_Flow", flow != null ? flow : translation);
                    if (liao) SolveLiaoLevel(features, next, i == last, guided);
                    else Graphics.Blit(features, next, material, SolvePass);
                    if (flow != null) ProfiledRenderTexture.ReleaseTemporary(flow);
                    flow = next;
                    next = null;
                }
                var result = flow;
                flow = null;
                return result;
            }
            finally
            {
                material.SetTexture("_Flow", null);
                foreach (var level in levels)
                    if (level != null) ProfiledRenderTexture.ReleaseTemporary(level);
                if (rows != null) ProfiledRenderTexture.ReleaseTemporary(rows);
                if (translation != null) ProfiledRenderTexture.ReleaseTemporary(translation);
                if (flow != null) ProfiledRenderTexture.ReleaseTemporary(flow);
                if (next != null) ProfiledRenderTexture.ReleaseTemporary(next);
            }
        }

        private static void SolveLiaoLevel(RenderTexture features, RenderTexture result, bool coarsest, bool guided)
        {
            const int initialize = 8, constraints = 9, warpSignals = 10, coefficients = 11, force = 12, relax = 13;
            int w = features.width, h = features.height;
            RenderTexture scratch = null, signals = null, stats = null, forces = null, pins = null, hypotheses = null, seed = null;
            try
            {
                scratch = HdrUtility.Temporary(w, h);
                signals = HdrUtility.Temporary(w, h);
                stats = HdrUtility.Temporary(w, h);
                forces = HdrUtility.Temporary(w, h);
                if (coarsest)
                {
                    hypotheses = HdrUtility.Temporary(45, 9);
                    seed = HdrUtility.Temporary(1, 1);
                    Graphics.Blit(features, hypotheses, material, 14);
                    Graphics.Blit(hypotheses, seed, material, 15);
                    material.SetTexture("_Flow", seed);
                }
                Graphics.Blit(features, result, material, initialize);
                if (guided)
                {
                    pins = HdrUtility.Temporary(w, h);
                    Graphics.Blit(features, pins, material, constraints);
                }
                material.SetTexture("_LiaoPins", pins);
                material.SetInteger("_LiaoGuided", guided ? 1 : 0);
                int iterations = coarsest ? 32 : 8;
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    material.SetTexture("_Flow", result);
                    Graphics.Blit(features, signals, material, warpSignals);
                    Graphics.Blit(signals, stats, material, coefficients);
                    material.SetTexture("_LiaoStats", stats);
                    Graphics.Blit(features, forces, material, force);
                    material.SetTexture("_Flow", null);
                    material.SetTexture("_LiaoForce", forces);
                    // Points of one parity share no grid cell, so each topology check sees fixed neighbours.
                    for (int parity = 0; parity < 4; parity++)
                    {
                        material.SetInteger("_LiaoParity", parity);
                        var source = (parity & 1) == 0 ? result : scratch;
                        var target = (parity & 1) == 0 ? scratch : result;
                        Graphics.Blit(source, target, material, relax);
                    }
                }
            }
            finally
            {
                material.SetTexture("_LiaoStats", null);
                material.SetTexture("_LiaoForce", null);
                material.SetTexture("_LiaoPins", null);
                material.SetTexture("_Flow", null);
                if (scratch != null) ProfiledRenderTexture.ReleaseTemporary(scratch);
                if (signals != null) ProfiledRenderTexture.ReleaseTemporary(signals);
                if (stats != null) ProfiledRenderTexture.ReleaseTemporary(stats);
                if (forces != null) ProfiledRenderTexture.ReleaseTemporary(forces);
                if (hypotheses != null) ProfiledRenderTexture.ReleaseTemporary(hypotheses);
                if (seed != null) ProfiledRenderTexture.ReleaseTemporary(seed);
                if (pins != null) ProfiledRenderTexture.ReleaseTemporary(pins);
            }
        }

        private static void SetLevel(int w, int h) =>
            material.SetVector("_Level", new Vector4(w, h, 1f / w, 1f / h));

        private static void BindAnchors(IReadOnlyList<MorphAnchor> anchors)
        {
            int count = anchors.Count;
            bool grow = anchorTable == null || (long)anchorTable.width * anchorTable.height < count;
            if (grow)
            {
                int limit = SystemInfo.maxTextureSize;
                int width = Mathf.Min(limit, Mathf.NextPowerOfTwo(Mathf.Min(count, limit)));
                int rows = 1 + (count - 1) / width;
                if (rows > limit) throw new ArgumentException("Morph anchor table exceeds the GPU texture size.", nameof(anchors));
                var replacement = new Texture2D(width, Mathf.Min(limit, Mathf.NextPowerOfTwo(rows)), TextureFormat.RGBAFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                if (anchorTable != null) UnityEngine.Object.DestroyImmediate(anchorTable);
                anchorTable = replacement;
                anchorUploadPending = true;
            }
            var data = anchorTable.GetRawTextureData<Vector4>();
            for (int i = 0; i < count; i++)
            {
                MorphAnchor pair = anchors[i];
                var value = new Vector4(pair.from.x, pair.from.y, pair.to.x, pair.to.y);
                if (data[i].Equals(value)) continue;
                data[i] = value;
                anchorUploadPending = true;
            }
            if (anchorUploadPending)
            {
                anchorTable.Apply(false, false);
                anchorUploadPending = false;
            }
            material.SetTexture("_AnchorTable", anchorTable);
            material.SetInteger("_AnchorWidth", anchorTable.width);
        }

        private static void Release()
        {
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            if (anchorTable != null) UnityEngine.Object.DestroyImmediate(anchorTable);
            material = null;
            anchorTable = null;
        }
    }
}
