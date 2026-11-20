using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class MorphRasterRenderer
    {
        private const int SampleAxis = 32;
        private const int WarpPass = 0, MomentsPass = 1, TranslationPass = 2, CopyPass = 3;
        private const int FeaturesPass = 4, ReducePass = 5;
        private const int ArcEquationPass = 14, ArcRelaxPass = 15, ArcPackPass = 16;
        private const int CopyArcPass = 17;
        private const int RigidGuidesPass = 26, RigidTargetsPass = 27, RigidForcePass = 28;
        private const int VisibilityEvidencePass = 22, VisibilityMaskPass = 23, PackVisibilityPass = 24, BoundaryLinksPass = 25;
        private const int StationaryEvidencePass = 29, LayerEvidencePass = 30;
        private const int ToneGuidePass = 31, ToneStatisticsPass = 32, SurfaceAgreementPass = 33;
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
            private RenderTexture visibilityFeatures;
            private RenderTexture arc;
            private RenderTexture layerEvidence;
            private readonly TonePair tones = new();

            private bool detailMatching;
            private bool preserveGeometry, holdStill, threeHypotheses;
            private float[] pointRadii = Array.Empty<float>();
            private Vector2[] edgeOptions = Array.Empty<Vector2>();
            private int[] guideSpans = Array.Empty<int>();
            private Vector4[] segmentEnds = Array.Empty<Vector4>();
            private Vector4[] guides = Array.Empty<Vector4>();

            internal RenderTexture Get(RenderTexture from, RenderTexture to,
                (Layer, Layer, ulong, ulong, int, int, float) pair, IReadOnlyList<MorphAnchor> anchors,
                bool useDetailMatching, bool useGeometry, IReadOnlyList<MorphSegment> segments, bool checkStationary, bool useThree)
            {
                int count = anchors?.Count ?? 0;
                int ends = 2 * (segments?.Count ?? 0);
                bool reuse = segmentEnds.Length == ends && field != null && field.IsCreated() && key.Equals(pair) &&
                    detailMatching == useDetailMatching && preserveGeometry == useGeometry && holdStill == checkStationary &&
                    threeHypotheses == useThree && guides.Length == count;
                for (int i = 0; reuse && i < count; i++)
                    reuse = guides[i].Equals(MorphGroupLayerBehaviour.Coordinates(anchors[i])) && pointRadii[i] == anchors[i].radius;
                for (int i = 0; reuse && i < ends; i++)
                    reuse = segmentEnds[i].Equals(segments[i / 2].Pair((i & 1) != 0));
                for (int i = 0; reuse && i < ends / 2; i++) reuse = guideSpans[i] == segments[i].chordCount && edgeOptions[i].Equals(EdgeOptions(segments[i]));
                if (reuse && (visibilityFeatures == null || visibilityFeatures.IsCreated())) return field;

                ClearFlow();
                field = BuildFlow(from, to, anchors, useDetailMatching, useGeometry, segments, checkStationary, useThree, out visibilityFeatures);
                threeHypotheses = useThree;
                if (segmentEnds.Length != ends) segmentEnds = new Vector4[ends];
                for (int i = 0; i < ends; i++) segmentEnds[i] = segments[i / 2].Pair((i & 1) != 0);
                if (guideSpans.Length != ends / 2) guideSpans = new int[ends / 2];
                if (edgeOptions.Length != guideSpans.Length) edgeOptions = new Vector2[guideSpans.Length];
                for (int i = 0; i < guideSpans.Length; i++)
                {
                    guideSpans[i] = segments[i].chordCount;
                    edgeOptions[i] = EdgeOptions(segments[i]);
                }
                detailMatching = useDetailMatching;
                preserveGeometry = useGeometry;
                holdStill = checkStationary;
                if (guides.Length != count) guides = new Vector4[count];
                if (pointRadii.Length != count) pointRadii = new float[count];
                for (int i = 0; i < count; i++)
                {
                    guides[i] = MorphGroupLayerBehaviour.Coordinates(anchors[i]);
                    pointRadii[i] = anchors[i].radius;
                }
                key = pair;
                return field;
            }

            internal RenderTexture WithVisibility() => ApplyVisibility(ref field, ref visibilityFeatures);
            internal void BindTones(RenderTexture from, RenderTexture to) => tones.Bind(from, to, field);

            internal MorphCorrectionAtlas corrections;
            internal void Clear()
            {
                corrections?.Dispose();
                corrections = null;
                ClearFlow();
            }

            private void ClearFlow()
            {
                tones.Clear();
                if (arc != null) ProfiledRenderTexture.ReleaseTemporary(arc);
                arc = null;
                if (layerEvidence != null) ProfiledRenderTexture.ReleaseTemporary(layerEvidence);
                layerEvidence = null;
                if (visibilityFeatures != null) ProfiledRenderTexture.ReleaseTemporary(visibilityFeatures);
                visibilityFeatures = null;
                if (field != null) ProfiledRenderTexture.ReleaseTemporary(field);
                field = null;
                key = default;
            }

            internal RenderTexture GetLayerEvidence(RenderTexture from)
            {
                if (layerEvidence == null || !layerEvidence.IsCreated())
                {
                    var next = MeasureRigidLayer(from);
                    if (layerEvidence != null) ProfiledRenderTexture.ReleaseTemporary(layerEvidence);
                    layerEvidence = next;
                }
                return layerEvidence;
            }

            internal RenderTexture GetArc(IReadOnlyList<MorphSegment> segments)
            {
                if (arc == null || !arc.IsCreated())
                {
                    var next = BuildArc(field, segments);
                    if (arc != null) ProfiledRenderTexture.ReleaseTemporary(arc);
                    arc = next;
                }
                return arc;
            }
        }

        // Endpoints are borrowed straight linear RGBA. The caller releases the returned temporary.
        internal static RenderTexture Render(RenderTexture from, RenderTexture to, IReadOnlyList<MorphAnchor> anchors, float t,
            FlowCache cache = null, (Layer, Layer, ulong, ulong, int, int, float) pair = default,
            MorphInterpolation interpolation = MorphInterpolation.Linear,
            bool classicLiao = false, bool preserveGeometry = false, IReadOnlyList<MorphSegment> segments = null, bool holdStill = false, bool visibilityMask = true, bool threeHypotheses = false,
            MorphMaskBlend maskBlend = MorphMaskBlend.Original)
        {
            return RenderCore(from,to,anchors,t,cache,pair,interpolation,classicLiao,preserveGeometry,segments,holdStill,visibilityMask,threeHypotheses,maskBlend);
        }

        private static RenderTexture RenderCore(RenderTexture from, RenderTexture to, IReadOnlyList<MorphAnchor> anchors, float t,
            FlowCache cache, (Layer, Layer, ulong, ulong, int, int, float) pair, MorphInterpolation interpolation,
            bool classicLiao, bool preserveGeometry, IReadOnlyList<MorphSegment> segments, bool holdStill, bool visibilityMask,
            bool threeHypotheses, MorphMaskBlend maskBlend, int warpPass = WarpPass, MorphCorrectionAtlas atlas = null)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (from.width != to.width || from.height != to.height)
                throw new ArgumentException("Morph endpoints must have matching dimensions.", nameof(to));
            if (!float.IsFinite(t)) throw new ArgumentOutOfRangeException(nameof(t));

            RenderTexture previous = RenderTexture.active, output = null, temporaryFlow = null, temporaryArc = null, temporaryEvidence = null;
            RenderTexture temporaryFeatures = null;
            RenderTexture preference = null;
            TonePair temporaryTones = null;
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
                    int segmentCount = segments?.Count ?? 0;
                    material.SetInteger("_SegmentCount", segmentCount);
                    int count = anchors?.Count ?? 0;
                    material.SetInteger("_AnchorCount", count);
                    material.SetFloat("_MorphTime", t);
                    material.SetInteger("_RefineTrace", count >= 2 || segmentCount > 0 ? 1 : 0);
                    material.SetTexture("_ToTex", to);
                    bool detailMatching = !classicLiao;
                    bool useVisibility = detailMatching && visibilityMask;
                    material.SetInteger("_CheckStationary", holdStill ? 1 : 0);
                    bool useThree = detailMatching && threeHypotheses;
                    var flow = cache != null ? cache.Get(from, to, pair, anchors, detailMatching, preserveGeometry, segments, holdStill, useThree) :
                        (temporaryFlow = BuildFlow(from, to, anchors, detailMatching, preserveGeometry, segments, holdStill, useThree, out temporaryFeatures));
                    if (useVisibility) flow = cache != null ? cache.WithVisibility() : ApplyVisibility(ref temporaryFlow, ref temporaryFeatures);
                    bool curved = interpolation == MorphInterpolation.AutomaticCurve;
                    if (curved)
                    {
                        var arc = cache != null ? cache.GetArc(segments) : (temporaryArc = BuildArc(flow, segments));
                        material.SetTexture("_ArcPath", arc);
                    }
                    material.SetInteger("_Curved", curved ? 1 : 0);
                    material.SetInteger("_UseVisibility", useVisibility ? 1 : 0);
                    material.SetFloat("_MorphTime", t);
                    material.SetTexture("_Flow", flow);
                    var blendMode = useVisibility ? maskBlend : MorphMaskBlend.Original;
                    material.SetInteger("_MaskBlend", (int)blendMode);
                    if (blendMode != MorphMaskBlend.Original)
                    {
                        if (cache != null) cache.BindTones(from, to);
                        else (temporaryTones = new TonePair()).Bind(from, to, flow);
                    }
                    bool useRigidLayer = holdStill && BindRigidLayer(anchors, segments, width, height, curved, t);
                    material.SetInteger("_UseRigidLayer", useRigidLayer ? 1 : 0);
                    if (useRigidLayer)
                    {
                        var evidence = cache != null ? cache.GetLayerEvidence(from) : (temporaryEvidence = MeasureRigidLayer(from));
                        material.SetTexture("_LayerEvidence", evidence);
                    }
                    material.SetInteger("_HasForeground",atlas?.Foreground == true ? 1 : 0);
                    material.SetTexture("_GuideAtlas",atlas?.Texture);
                    if (atlas != null && warpPass == Pass("LocalChoiceWarp"))
                    {
                        preference = PrepareLocalPreference(from, width, height);
                        material.SetTexture("_ChoicePreference", preference);
                    }
                    Graphics.Blit(from, output, material, warpPass);
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
                    material.SetTexture("_GuideAtlas", null);
                    material.SetTexture("_Flow", null);
                    material.SetTexture("_ArcPath", null);
                    material.SetTexture("_LayerEvidence", null);
                    material.SetTexture("_ToneA", null);
                    material.SetTexture("_ToneB", null);
                    material.SetTexture("_SurfaceAgreement", null);
                }
                if (preference != null)
                {
                    material.SetTexture("_ChoicePreference", null);
                    ProfiledRenderTexture.ReleaseTemporary(preference);
                }
                temporaryTones?.Clear();
                if (temporaryEvidence != null) ProfiledRenderTexture.ReleaseTemporary(temporaryEvidence);
                if (temporaryArc != null) ProfiledRenderTexture.ReleaseTemporary(temporaryArc);
                if (temporaryFeatures != null) ProfiledRenderTexture.ReleaseTemporary(temporaryFeatures);
                if (temporaryFlow != null) ProfiledRenderTexture.ReleaseTemporary(temporaryFlow);
                if (output != null) ProfiledRenderTexture.ReleaseTemporary(output);
            }
        }

        // Cached alongside the correspondence. Both tone modes use the same endpoint statistics.
        private sealed class TonePair
        {
            private RenderTexture first, second, agreement;

            public void Clear()
            {
                if (first != null) ProfiledRenderTexture.ReleaseTemporary(first);
                if (second != null) ProfiledRenderTexture.ReleaseTemporary(second);
                if (agreement != null) ProfiledRenderTexture.ReleaseTemporary(agreement);
                first = second = agreement = null;
            }

            public void Bind(RenderTexture from, RenderTexture to, RenderTexture flow)
            {
                if (agreement == null || !agreement.IsCreated() || !first.IsCreated() || !second.IsCreated())
                {
                    Clear();
                    float scale = Mathf.Min(1, 768f / Mathf.Max(from.width, from.height));
                    int w = Mathf.Max(1, Mathf.RoundToInt(from.width * scale));
                    int h = Mathf.Max(1, Mathf.RoundToInt(from.height * scale));
                    bool full = from.format == RenderTextureFormat.ARGBFloat || to.format == RenderTextureFormat.ARGBFloat;
                    var guide = HdrUtility.Temporary(w, h, full);
                    try
                    {
                        material.SetVector("_ToneSize", new Vector4(w, h, 1f / w, 1f / h));
                        first = HdrUtility.Temporary(w, h, full);
                        second = HdrUtility.Temporary(w, h, full);
                        Filter(from, first);
                        Filter(to, second);
                        agreement = HdrUtility.Temporary(flow.width, flow.height);
                        SetLevel(flow.width, flow.height);
                        Graphics.Blit(from, agreement, material, SurfaceAgreementPass);
                    }
                    catch { Clear(); throw; }
                    finally
                    {
                        material.SetTexture("_ToneGuide", null);
                        ProfiledRenderTexture.ReleaseTemporary(guide);
                    }

                    void Filter(RenderTexture source, RenderTexture target)
                    {
                        material.SetTexture("_ToneGuide", null);
                        Graphics.Blit(source, guide, material, ToneGuidePass);
                        material.SetTexture("_ToneGuide", guide);
                        Graphics.Blit(source, target, material, ToneStatisticsPass);
                    }
                }
                material.SetTexture("_ToneA", first);
                material.SetTexture("_ToneB", second);
                material.SetTexture("_SurfaceAgreement", agreement);
            }
        }

        // Fit in raster pixels, so non-square documents preserve distances and angles.
        private static bool BindRigidLayer(IReadOnlyList<MorphAnchor> points, IReadOnlyList<MorphSegment> segments,
            int width, int height, bool curved, float t)
        {
            int pointCount = points?.Count ?? 0, edgeCount = segments?.Count ?? 0;
            int count = pointCount + 2 * edgeCount;
            if (count == 0) return false;
            for (int i = 0; i < pointCount; ++i)
                if (points[i].radius > 0) return false;
            for (int i = 0; i < edgeCount; ++i)
                if (segments[i].radius > 0 || segments[i].boundary) return false;

            Vector4 Pixels(int i)
            {
                var pair = i < pointCount ? MorphGroupLayerBehaviour.Coordinates(points[i])
                    : segments[(i - pointCount) / 2].Pair(((i - pointCount) & 1) != 0);
                return new Vector4(pair.x * width - .5f, pair.y * height - .5f,
                    pair.z * width - .5f, pair.w * height - .5f);
            }

            var first = Pixels(0);
            var firstDelta = new Vector2(first.z - first.x, first.w - first.y);
            var mean = Vector4.zero;
            var lower = Vector2.positiveInfinity;
            var upper = Vector2.negativeInfinity;
            bool translation = true;
            for (int i = 0; i < count; ++i)
            {
                var pair = Pixels(i);
                mean += pair;
                var a = new Vector2(pair.x, pair.y);
                var b = new Vector2(pair.z, pair.w);
                lower = Vector2.Min(lower, Vector2.Min(a, b));
                upper = Vector2.Max(upper, Vector2.Max(a, b));
                translation &= (b - a - firstDelta).sqrMagnitude <= .25f;
            }
            mean /= count;
            var delta = new Vector2(mean.z - mean.x, mean.w - mean.y);
            float angle = 0, span = 0;
            if (translation)
            {
                if (delta.sqrMagnitude < 4) return false;
            }
            else
            {
                // Linear interpolation follows the chord between each pair of authored points.
                if (!curved) return false;
                float dot = 0, cross = 0;
                for (int i = 0; i < count; ++i)
                {
                    var p = Pixels(i) - mean;
                    dot += p.x * p.z + p.y * p.w;
                    cross += p.x * p.w - p.y * p.z;
                    span = Mathf.Max(span, p.x * p.x + p.y * p.y);
                }
                if (span < 4 || dot * dot + cross * cross < .0001f) return false;
                angle = Mathf.Atan2(cross, dot);
                // Exactly opposite directions do not determine which arc to follow.
                if (Mathf.Abs(angle) > Mathf.PI - .001f) return false;
                float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                for (int i = 0; i < count; ++i)
                {
                    var p = Pixels(i) - mean;
                    var error = new Vector2(c * p.x - s * p.y - p.z, s * p.x + c * p.y - p.w);
                    if (error.sqrMagnitude > .25f) return false;
                }
            }
            var center = (lower + upper) * .5f;
            var reach = (upper - lower) * .5f + Vector2.one * Mathf.Max(4, Mathf.Max(.5f * delta.magnitude, 2 * Mathf.Sqrt(span)));
            material.SetVector("_LayerRegion", new Vector4(center.x, center.y, reach.x, reach.y));
            material.SetVector("_LayerOrigins", mean);
            material.SetVector("_LayerTurn", new Vector4(Mathf.Cos(angle), Mathf.Sin(angle), 0, 0));
            material.SetVector("_LayerPhase", new Vector4(Mathf.Cos(t * angle), -Mathf.Sin(t * angle),
                Mathf.Cos((1 - t) * angle), Mathf.Sin((1 - t) * angle)));
            return true;
        }

        private static RenderTexture MeasureRigidLayer(RenderTexture from)
        {
            var result = HdrUtility.Temporary(1, 1);
            try { Graphics.Blit(from, result, material, LayerEvidencePass); return result; }
            catch { ProfiledRenderTexture.ReleaseTemporary(result); throw; }
        }

        // Nested iteration for the screened Poisson system on the halfway domain.
        // RG stores endpoint displacement. BA stores the deviation at half time.
        private static RenderTexture BuildArc(RenderTexture flow, IReadOnlyList<MorphSegment> segments)
        {
            RenderTexture bend = null, scratch = null, equation = null, packed = null;
            try
            {
                int width = flow.width, height = flow.height;
                material.SetTexture("_Flow", flow);
                int stride = 1;
                while (Mathf.Max(width, height) > 12 * stride) stride *= 2;
                for (; stride >= 1; stride /= 2)
                {
                    int w = (width + stride - 1) / stride, h = (height + stride - 1) / stride;
                    SetLevel(w, h);
                    equation = HdrUtility.Temporary(w, h);
                    Graphics.Blit(null, equation, material, ArcEquationPass);
                    material.SetTexture("_ArcEquation", equation);
                    scratch = HdrUtility.Temporary(w, h);
                    // Explicit linear sampling also works when the previous level has point filtering.
                    Graphics.Blit(bend != null ? (Texture)bend : Texture2D.blackTexture, scratch, material, CopyArcPass);
                    if (bend != null) ProfiledRenderTexture.ReleaseTemporary(bend);
                    bend = scratch;
                    scratch = null;
                    scratch = HdrUtility.Temporary(w, h);
                    int iterations = Mathf.Max(w, h) <= 12 ? 64 : 24;
                    for (int i = 0; i < iterations; i++)
                    {
                        Graphics.Blit(bend, scratch, material, ArcRelaxPass);
                        (bend, scratch) = (scratch, bend);
                    }
                    ProfiledRenderTexture.ReleaseTemporary(scratch); scratch = null;
                    material.SetTexture("_ArcEquation", null);
                    ProfiledRenderTexture.ReleaseTemporary(equation); equation = null;
                }
                if (segments?.Count > 0) BindAnchors(null, segments);
                packed = HdrUtility.Temporary(width, height);
                Graphics.Blit(bend, packed, material, ArcPackPass);
                var result = packed;
                packed = null;
                return result;
            }
            finally
            {
                material.SetTexture("_Flow", null);
                material.SetTexture("_ArcEquation", null);
                if (packed != null) ProfiledRenderTexture.ReleaseTemporary(packed);
                if (equation != null) ProfiledRenderTexture.ReleaseTemporary(equation);
                if (scratch != null) ProfiledRenderTexture.ReleaseTemporary(scratch);
                if (bend != null) ProfiledRenderTexture.ReleaseTemporary(bend);
            }
        }

        private static RenderTexture BuildFlow(RenderTexture from, RenderTexture to, IReadOnlyList<MorphAnchor> anchors,
            bool detailMatching, bool preserveGeometry, IReadOnlyList<MorphSegment> segments, bool checkStationary, bool threeHypotheses, out RenderTexture visibilityFeatures)
        {
            visibilityFeatures = null;
            int width = from.width, height = from.height;
            float scale = Mathf.Min(1f, MaxFlowAxis / (float)Mathf.Max(width, height));
            int w = Mathf.Max(1, Mathf.RoundToInt(width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            // At most five levels for a 256-pixel working image.
            var levels = new RenderTexture[5];
            RenderTexture rows = null, translation = null, flow = null, next = null;
            RenderTexture rigidGuides = null;
            try
            {
                int columns = Mathf.Min(width, SampleAxis), samplesHigh = Mathf.Min(height, SampleAxis);
                if (threeHypotheses)
                {
                    material.EnableKeyword("WHIMTEX_MORPH_TOP3");
                    material.SetTexture("_FineA", from);
                    material.SetTexture("_FineB", to);
                }
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
                bool guided = anchors?.Count > 0 || segments?.Count > 0;
                if (guided) BindAnchors(anchors, segments);
                if (preserveGeometry && guided && anchors?.Count >= 2)
                {
                    int index = 0;
                    while (Mathf.Max(levels[index].width, levels[index].height) > 64) index++;
                    rigidGuides = HdrUtility.Temporary(levels[index].width, levels[index].height);
                    SetLevel(rigidGuides.width, rigidGuides.height);
                    Graphics.Blit(null, rigidGuides, material, RigidGuidesPass);
                }
                material.SetTexture("_RigidGuides", rigidGuides);
                material.SetInteger("_HasRigidGuides", rigidGuides != null ? 1 : 0);
                bool boundary = false;
                for (int i = 0; i < (segments?.Count ?? 0) && !boundary; i++) boundary = segments[i].boundary;
                bool guideFrame = guided && anchors?.Count >= 3 && BindGuideFrame(anchors);
                material.SetInteger("_LiaoGuideFrame", guideFrame ? 1 : 0);
                for (int i = last; i >= 0; i--)
                {
                    var features = levels[i];
                    SetLevel(features.width, features.height);
                    next = HdrUtility.Temporary(features.width, features.height);
                    material.SetInteger("_Coarsest", i == last ? 1 : 0);
                    var parent = flow != null ? flow : translation;
                    material.SetTexture("_Flow", parent);
                    SolveLiaoLevel(features, next, parent, i == last ? levels[Mathf.Max(0, i - 1)] : null,
                        detailMatching && i == last ? levels[Mathf.Max(0, i - 2)] : null, guided, guideFrame, preserveGeometry, boundary, checkStationary,
                        checkStationary && i == last && !guideFrame ? levels[Mathf.Max(0, i - 2)] : null);
                    if (flow != null) ProfiledRenderTexture.ReleaseTemporary(flow);
                    flow = next;
                    next = null;
                }
                if (detailMatching)
                {
                    // Both warped keys guide the mask at the resolution of the motion field.
                    visibilityFeatures = levels[0];
                    levels[0] = null;
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
                if (threeHypotheses)
                {
                    material.DisableKeyword("WHIMTEX_MORPH_TOP3");
                    material.SetTexture("_FineA", null);
                    material.SetTexture("_FineB", null);
                }
                material.SetTexture("_RigidGuides", null);
                if (rigidGuides != null) ProfiledRenderTexture.ReleaseTemporary(rigidGuides);
                if (rows != null) ProfiledRenderTexture.ReleaseTemporary(rows);
                if (translation != null) ProfiledRenderTexture.ReleaseTemporary(translation);
                if (flow != null) ProfiledRenderTexture.ReleaseTemporary(flow);
                if (next != null) ProfiledRenderTexture.ReleaseTemporary(next);
            }
        }

        private static RenderTexture ApplyVisibility(ref RenderTexture flow, ref RenderTexture features)
        {
            if (features == null) return flow;
            var packed = PackVisibility(flow, features);
            ProfiledRenderTexture.ReleaseTemporary(flow);
            ProfiledRenderTexture.ReleaseTemporary(features);
            features = null;
            flow = packed;
            return flow;
        }

        private static RenderTexture PackVisibility(RenderTexture flow, RenderTexture features)
        {
            RenderTexture reduced = null, evidence = null, mask = null, packed = null;
            try
            {
                var coarse = features;
                if (Mathf.Max(features.width, features.height) > 128)
                {
                    reduced = HdrUtility.Temporary((features.width + 1) / 2, (features.height + 1) / 2);
                    SetLevel(reduced.width, reduced.height);
                    Graphics.Blit(features, reduced, material, ReducePass);
                    coarse = reduced;
                }
                material.SetTexture("_Flow", flow);
                SetLevel(coarse.width, coarse.height);
                evidence = HdrUtility.Temporary(coarse.width, coarse.height);
                Graphics.Blit(coarse, evidence, material, VisibilityEvidencePass);
                mask = HdrUtility.Temporary(coarse.width, coarse.height);
                Graphics.Blit(evidence, mask, material, VisibilityMaskPass);
                SetLevel(flow.width, flow.height);
                material.SetTexture("_VisibilityFeatures", features);
                material.SetVector("_MaskGrid", new Vector4(mask.width, mask.height, 0, 0));
                packed = HdrUtility.Temporary(flow.width, flow.height);
                Graphics.Blit(mask, packed, material, PackVisibilityPass);
                var result = packed;
                packed = null;
                return result;
            }
            finally
            {
                material.SetTexture("_Flow", null);
                material.SetTexture("_VisibilityFeatures", null);
                if (packed != null) ProfiledRenderTexture.ReleaseTemporary(packed);
                if (mask != null) ProfiledRenderTexture.ReleaseTemporary(mask);
                if (evidence != null) ProfiledRenderTexture.ReleaseTemporary(evidence);
                if (reduced != null) ProfiledRenderTexture.ReleaseTemporary(reduced);
            }
        }

        // Fit full displacement on the halfway domain. Center the coordinates before
        // forming the normal equations so a cluster near a canvas edge stays stable.
        private static bool BindGuideFrame(IReadOnlyList<MorphAnchor> anchors)
        {
            if (anchors.Count < 3) return false;
            Vector2 center = Vector2.zero, motion = Vector2.zero;
            int globalPins = 0;
            for (int i = 0; i < anchors.Count; i++)
            {
                var pin = anchors[i];
                if (pin.radius > 0) continue;
                globalPins++;
                center += .5f * (pin.from + pin.to);
                motion += pin.to - pin.from;
            }
            if (globalPins < 3) return false;
            center /= globalPins;
            motion /= globalPins;
            float xx = 0, xy = 0, yy = 0;
            Vector2 bx = Vector2.zero, by = Vector2.zero;
            for (int i = 0; i < anchors.Count; i++)
            {
                var pin = anchors[i];
                if (pin.radius > 0) continue;
                Vector2 p = .5f * (pin.from + pin.to) - center;
                Vector2 d = pin.to - pin.from - motion;
                xx += p.x * p.x; xy += p.x * p.y; yy += p.y * p.y;
                bx += p.x * d; by += p.y * d;
            }
            float determinant = xx * yy - xy * xy;
            // Collinear or tightly clustered points do not determine a canvas-wide affine map.
            if (determinant <= .0004f * globalPins * (xx + yy)) return false;
            Vector2 dx = (yy * bx - xy * by) / determinant;
            Vector2 dy = (xx * by - xy * bx) / determinant;
            // det(I + t * J) must stay positive along both endpoint maps, t in [-.5,.5].
            float trace = dx.x + dy.y, det = dx.x * dy.y - dx.y * dy.x;
            float minimum = 1f - .5f * Mathf.Abs(trace) + .25f * det;
            if (det > 0 && Mathf.Abs(trace) < det) minimum = 1f - .25f * trace * trace / det;
            // At a clamped pyramid border interpolation may vary along a single axis.
            if (minimum < .25f || Mathf.Abs(dx.x) > 1.6f || Mathf.Abs(dy.y) > 1.6f) return false;
            Vector2 offset = motion - center.x * dx - center.y * dy;
            material.SetVector("_LiaoGuideX", new Vector4(dx.x, dy.x, offset.x, 0));
            material.SetVector("_LiaoGuideY", new Vector4(dx.y, dy.y, offset.y, 0));
            return true;
        }

        private static void SolveLiaoLevel(RenderTexture features, RenderTexture result, RenderTexture parent,
            RenderTexture seedFeatures, RenderTexture detailFeatures, bool guided, bool guideFrame, bool preserveGeometry, bool boundary, bool checkStationary, RenderTexture motionCheckFeatures)
        {
            const int initialize = 6, constraints = 7, warpSignals = 8, coefficients = 9, force = 10, relax = 11;
            int w = features.width, h = features.height;
            RenderTexture scratch = null, signals = null, stats = null, forces = null, pins = null;
            RenderTexture seed = null, refinedSeed = null, nearSeed = null, detail = null;
            RenderTexture rigidTargets = null, boundaryLinks = null, stationary = null;
            try
            {
                scratch = HdrUtility.Temporary(w, h);
                signals = HdrUtility.Temporary(w, h);
                stats = HdrUtility.Temporary(w, h);
                forces = HdrUtility.Temporary(w, h);
                bool coarsest = seedFeatures != null;
                if (coarsest && !guideFrame)
                {
                    seed = HdrUtility.Temporary(1, 1);
                    refinedSeed = HdrUtility.Temporary(1, 1);
                    material.SetVector("_LiaoSeedStep", new Vector4(1f / w, 1f / h, 0, 0));
                    ScoreLiaoSeed(features, seed, SeedSearch.Coarse);
                    material.SetTexture("_LiaoPrior", seed);
                    ScoreLiaoSeed(seedFeatures, refinedSeed, SeedSearch.Refine);
                    if (motionCheckFeatures != null && Mathf.Min(motionCheckFeatures.width, motionCheckFeatures.height) >= 12)
                    {
                        // Repeated grid lines can alias even at the first refinement level.
                        // Admit a small pan only when it explains the finer image pair.
                        nearSeed = HdrUtility.Temporary(1, 1);
                        material.SetTexture("_LiaoPrior", refinedSeed);
                        ScoreLiaoSeed(motionCheckFeatures, nearSeed, SeedSearch.NearIdentity);
                    }
                    SetLevel(w, h);
                }
                material.SetTexture("_LiaoPrior", nearSeed != null ? nearSeed : refinedSeed != null ? refinedSeed : parent);
                if (detailFeatures != null && Mathf.Min(w, h) >= 4)
                    detail = BuildDetailPrior(detailFeatures, w, h);
                material.SetTexture("_LiaoDetail", detail);
                material.SetInteger("_LiaoHasDetail", detail != null ? 1 : 0);
                Graphics.Blit(features, result, material, initialize);
                if (boundary)
                {
                    boundaryLinks = HdrUtility.Temporary(w, h);
                    Graphics.Blit(null, boundaryLinks, material, BoundaryLinksPass);
                }
                material.SetInteger("_HasBoundary", boundary ? 1 : 0);
                material.SetTexture("_BoundaryLinks", boundaryLinks);
                if (guided)
                {
                    pins = HdrUtility.Temporary(w, h);
                    Graphics.Blit(features, pins, material, constraints);
                }
                material.SetTexture("_LiaoPins", pins);
                material.SetInteger("_LiaoGuided", guided ? 1 : 0);
                if (checkStationary)
                {
                    stationary = HdrUtility.Temporary(w, h);
                    material.SetTexture("_Flow", result);
                    Graphics.Blit(features, stationary, material, StationaryEvidencePass);
                }
                material.SetTexture("_StationaryEvidence", stationary);
                int iterations = coarsest ? 32 : 12;
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    material.SetTexture("_Flow", result);
                    if (preserveGeometry && iteration == (coarsest ? 8 : 2))
                    {
                        int stride = Mathf.Max(w, h) > 128 ? 2 : 1;
                        rigidTargets = HdrUtility.Temporary((w + stride - 1) / stride, (h + stride - 1) / stride);
                        material.SetVector("_RigidGridInv", new Vector4(1f / rigidTargets.width, 1f / rigidTargets.height, 0, 0));
                        Graphics.Blit(features, rigidTargets, material, RigidTargetsPass);
                        material.SetTexture("_RigidTargets", rigidTargets);
                    }
                    Graphics.Blit(features, signals, material, warpSignals);
                    Graphics.Blit(signals, stats, material, coefficients);
                    material.SetTexture("_LiaoWarped", signals);
                    material.SetTexture("_LiaoStats", stats);
                    Graphics.Blit(features, forces, material, rigidTargets != null ? RigidForcePass : force);
                    material.SetTexture("_GuideAtlas", null);
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
                material.SetTexture("_LiaoWarped", null);
                material.SetTexture("_LiaoForce", null);
                material.SetTexture("_LiaoPins", null);
                material.SetTexture("_LiaoPrior", null);
                material.SetTexture("_LiaoDetail", null);
                material.SetTexture("_RigidTargets", null);
                material.SetTexture("_StationaryEvidence", null);
                if (stationary != null) ProfiledRenderTexture.ReleaseTemporary(stationary);
                material.SetTexture("_BoundaryLinks", null);
                if (boundaryLinks != null) ProfiledRenderTexture.ReleaseTemporary(boundaryLinks);
                if (rigidTargets != null) ProfiledRenderTexture.ReleaseTemporary(rigidTargets);
                if (detail != null) ProfiledRenderTexture.ReleaseTemporary(detail);
                material.SetTexture("_Flow", null);
                if (scratch != null) ProfiledRenderTexture.ReleaseTemporary(scratch);
                if (signals != null) ProfiledRenderTexture.ReleaseTemporary(signals);
                if (stats != null) ProfiledRenderTexture.ReleaseTemporary(stats);
                if (forces != null) ProfiledRenderTexture.ReleaseTemporary(forces);
                if (seed != null) ProfiledRenderTexture.ReleaseTemporary(seed);
                if (pins != null) ProfiledRenderTexture.ReleaseTemporary(pins);
                if (refinedSeed != null) ProfiledRenderTexture.ReleaseTemporary(refinedSeed);
                if (nearSeed != null) ProfiledRenderTexture.ReleaseTemporary(nearSeed);
            }
        }

        private enum SeedSearch { Coarse, Refine, NearIdentity }

        private static void ScoreLiaoSeed(RenderTexture features, RenderTexture seed, SeedSearch search)
        {
            // Coarse grid: 9x9 translations and five uniform scales. Refinement:
            // 5x5 fractional offsets and three scales independently on each axis.
            int width = search == SeedSearch.Coarse ? 45 : search == SeedSearch.Refine ? 15 : 10;
            var scores = HdrUtility.Temporary(width, search == SeedSearch.Refine ? 15 : 9);
            try
            {
                SetLevel(features.width, features.height);
                material.SetInteger("_LiaoSeedRefine", (int)search);
                Graphics.Blit(features, scores, material, 12);
                Graphics.Blit(scores, seed, material, 13);
            }
            finally { ProfiledRenderTexture.ReleaseTemporary(scores); }
        }

        private static RenderTexture BuildDetailPrior(RenderTexture features, int width, int height)
        {
            const int score = 18, choose = 19, decode = 20, checkNeighbours = 21;
            int columns = (width + 1) / 2, rows = (height + 1) / 2;
            RenderTexture scores = null, consistent = null, winner = null, prior = null;
            try
            {
                // Seven angles and nine offsets per axis, plus a column for the inherited field.
                material.SetVector("_RotationGrid", new Vector4(columns, rows, 1f / columns, 1f / rows));
                scores = HdrUtility.Temporary(64 * columns, 9 * rows);
                winner = HdrUtility.Temporary(columns, rows);
                prior = HdrUtility.Temporary(width, height);
                material.SetTexture("_RotationFeatures", features);
                Graphics.Blit(features, scores, material, score);
                Graphics.Blit(scores, winner, material, choose);
                material.SetTexture("_RotationScores", scores);
                consistent = HdrUtility.Temporary(columns, rows);
                Graphics.Blit(winner, consistent, material, checkNeighbours);
                material.SetTexture("_RotationIndependent", winner);
                Graphics.Blit(consistent, prior, material, decode);
                var result = prior;
                prior = null;
                return result;
            }
            finally
            {
                if (scores != null) ProfiledRenderTexture.ReleaseTemporary(scores);
                material.SetTexture("_RotationFeatures", null);
                material.SetTexture("_RotationScores", null);
                material.SetTexture("_RotationIndependent", null);
                if (consistent != null) ProfiledRenderTexture.ReleaseTemporary(consistent);
                if (winner != null) ProfiledRenderTexture.ReleaseTemporary(winner);
                if (prior != null) ProfiledRenderTexture.ReleaseTemporary(prior);
            }
        }

        private static void SetLevel(int w, int h) =>
            material.SetVector("_Level", new Vector4(w, h, 1f / w, 1f / h));

        private static Vector2 EdgeOptions(MorphSegment segment) => new Vector2(segment.radius, segment.boundary ? 1 : 0);

        private static void BindAnchors(IReadOnlyList<MorphAnchor> anchors, IReadOnlyList<MorphSegment> segments = null)
        {
            int points = anchors?.Count ?? 0;
            int radiusOffset = points + 3 * (segments?.Count ?? 0);
            bool hasPointRadius = false;
            for (int i = 0; i < points && !hasPointRadius; i++) hasPointRadius = anchors[i].radius > 0;
            material.SetInteger("_PointRadiusOffset", hasPointRadius ? radiusOffset : 0);
            int count = radiusOffset + (hasPointRadius ? points : 0);
            material.SetInteger("_SegmentOffset", points);
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
                int end = i - points;
                Vector4 value;
                if (end < 0) value = MorphGroupLayerBehaviour.Coordinates(anchors[i]);
                else if (i >= radiusOffset) value = new Vector4(anchors[i - radiusOffset].radius, 0, 0, 0);
                else
                {
                    var segment = segments[end / 3];
                    value = end % 3 == 2 ? new Vector4(Mathf.Max(1, segment.chordCount), segment.radius, segment.boundary ? 1 : 0, 0) : segment.Pair(end % 3 == 1);
                }
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
