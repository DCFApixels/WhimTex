using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum MorphMode { Raster, Vector }
    public enum MorphRasterMethod { LocalContrast, Liao }
    public enum MorphInterpolation { Linear, AutomaticCurve }

    [Serializable]
    public sealed class MorphAnchor
    {
        public Vector2 from, to;
    }

    [Serializable]
    public sealed partial class MorphSegment
    {
        public Vector2 fromStart, fromEnd, toStart, toEnd;
        internal Vector4 Pair(bool end) => end ? new Vector4(fromEnd.x, fromEnd.y, toEnd.x, toEnd.y) :
            new Vector4(fromStart.x, fromStart.y, toStart.x, toStart.y);
    }

    [Serializable]
    public sealed partial class MorphTransition
    {
        public string fromId, toId;
        public float duration = 1f;
        public List<MorphSegment> segments = new List<MorphSegment>();
        public List<MorphAnchor> anchors = new List<MorphAnchor>();
    }

    [Serializable]
    public sealed class MorphGroupLayerBehaviour : LayerBehaviour
    {
        public MorphMode mode;
        public MorphRasterMethod rasterMethod;
        public bool classicLiao;
        public bool preserveGeometry;
        public MorphInterpolation interpolation;
        public List<MorphTransition> transitions = new List<MorphTransition>();

        internal override bool IsGroup => true;
        internal override void InitializeLayer(Layer layer) => layer.compositing = GroupCompositing.Isolated;
        public override bool UsesTime
        {
            get
            {
                var children = BoundOwner?.children;
                if (children == null) return false;
                int first = children.FindIndex(IsKey);
                return first >= 0 && children.FindIndex(first + 1, IsKey) >= 0;
            }
        }

        internal static bool IsKey(Layer layer) => layer?.enabled == true &&
            layer.Behaviour != null && layer.Behaviour is not PendingLayerBehaviour;

        public MorphTransition Find(string fromId, string toId)
        {
            foreach (var pair in transitions)
                if (pair.fromId == fromId && pair.toId == toId) return pair;
            return null;
        }

        public MorphTransition GetOrCreate(string fromId, string toId)
        {
            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId) || fromId == toId)
                throw new ArgumentException("A transition requires two distinct, assigned layer IDs.");
            var pair = Find(fromId, toId);
            if (pair != null) return pair;
            pair = new MorphTransition { fromId = fromId, toId = toId };
            transitions.Add(pair);
            return pair;
        }

        internal void Sample(double seconds, out Layer from, out Layer to, out MorphTransition transition, out float t)
        {
            from = to = null;
            transition = null;
            t = 0;
            foreach (var child in Owner.layers)
            {
                if (!IsKey(child)) continue;
                if (from != null)
                {
                    var pair = Find(from.Id, child.Id);
                    float duration = pair?.duration ?? 1f;
                    if (seconds < duration)
                    {
                        to = child;
                        transition = pair;
                        t = Mathf.Clamp01((float)(seconds / duration));
                        return;
                    }
                    seconds -= duration;
                }
                from = child;
            }
        }

        internal static bool CanMorphGeometry(Layer from, Layer to) => SupportsGeometry(from) && SupportsGeometry(to);

        internal bool NeedsRasterFor(Layer from, Layer to) => !CanMorphGeometry(from, to) ||
            preparedFrom == from && preparedTo == to && geometricFallback;

        private static bool SupportsGeometry(Layer layer) =>
            (layer?.Behaviour is ShapeLayerBehaviour or VectorLayerBehaviour) && !layer.HasFx &&
            !layer.clippingMask && layer.channelMapping.IsIdentity &&
            layer.EffectiveTiling == TransformTilingMode.Unbounded;

        internal void Validate()
        {
            if (mode != MorphMode.Raster && mode != MorphMode.Vector || !Enum.IsDefined(typeof(MorphRasterMethod), rasterMethod) ||
                !Enum.IsDefined(typeof(MorphInterpolation), interpolation) || transitions == null)
                throw new InvalidOperationException("Invalid Morph Group settings.");
            var keys = new HashSet<(string, string)>();
            foreach (var pair in transitions)
            {
                if (pair == null || string.IsNullOrWhiteSpace(pair.fromId) || string.IsNullOrWhiteSpace(pair.toId) ||
                    pair.fromId == pair.toId || !keys.Add((pair.fromId, pair.toId)))
                    throw new InvalidOperationException("Morph transitions require unique ordered pairs of distinct layer IDs.");
                if (!float.IsFinite(pair.duration) || pair.duration <= 0 || pair.anchors == null)
                    throw new InvalidOperationException("Morph transitions require a positive finite duration and a list of anchors.");
                pair.ValidateGuides();
                foreach (var point in pair.anchors)
                    if (point == null || !VectorGeometry.Finite(point.from) || !VectorGeometry.Finite(point.to))
                        throw new InvalidOperationException("Morph anchor coordinates must be finite.");
            }
        }

        internal void RemapChildren(Dictionary<string, string> ids)
        {
            for (int i = transitions.Count - 1; i >= 0; i--)
            {
                var pair = transitions[i];
                if (ids.TryGetValue(pair.fromId, out var from) && ids.TryGetValue(pair.toId, out var to))
                { pair.fromId = from; pair.toId = to; }
                else transitions.RemoveAt(i);
            }
        }

        [NonSerialized] internal MorphRasterRenderer.FlowCache rasterFlow;
        [NonSerialized] private Layer preparedFrom, preparedTo;
        [NonSerialized] private ulong fromStamp, toStamp;
        [NonSerialized] private Vector2 preparedSize;
        [NonSerialized] private bool geometricFallback;
        [NonSerialized] private Vector4[] preparedAnchors;
        [NonSerialized] private MorphVectorGeometry.Prepared prepared;

        internal VectorLayerBehaviour Interpolate(Layer a, Layer b, Vector2 size, List<MorphAnchor> anchors,
            ulong stampA, ulong stampB, float t)
        {
            int count = anchors?.Count ?? 0;
            bool reuse = stampA != 0 && stampB != 0 && a == preparedFrom && b == preparedTo &&
                fromStamp == stampA && toStamp == stampB && size == preparedSize && preparedAnchors?.Length == count;
            for (int i = 0; reuse && i < count; i++)
                reuse = preparedAnchors[i].Equals(Coordinates(anchors[i]));
            if (!reuse)
            {
                prepared = MorphVectorGeometry.Prepare(a, b, size, anchors);
                preparedFrom = a; preparedTo = b;
                fromStamp = stampA; toStamp = stampB; preparedSize = size;
                if (preparedAnchors?.Length != count) preparedAnchors = new Vector4[count];
                for (int i = 0; i < count; i++) preparedAnchors[i] = Coordinates(anchors[i]);
            }
            var geometry = prepared?.Sample(t);
            geometricFallback = geometry == null;
            return geometry;
        }

        internal static Vector4 Coordinates(MorphAnchor anchor) =>
            new Vector4(anchor.from.x, anchor.from.y, anchor.to.x, anchor.to.y);

        internal override void ReleaseTransientResources()
        {
            rasterFlow?.Clear(); rasterFlow = null;
            prepared = null; preparedFrom = preparedTo = null; preparedAnchors = null;
        }

        internal override RenderTexture Render(in LayerRenderContext context) =>
            context.activeDocument.RenderMorphGroup(this, context);
    }
}
