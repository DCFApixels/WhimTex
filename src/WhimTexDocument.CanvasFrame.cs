using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum CanvasOrientation { FlipHorizontal, FlipVertical, RotateLeft, RotateRight }

    public sealed partial class WhimTexDocument
    {
        internal const int MaximumCanvasFrameDimension = 16384;
        internal const long MaximumCanvasFramePixels = 16777216;

        /// <summary>
        /// Changes the canvas boundary without resampling or deleting source content.
        /// The frame uses current canvas pixels, with its origin at the bottom left.
        /// The caller owns Undo and change notification.
        /// </summary>
        public bool ApplyCanvasFrame(RectInt frame) => ApplyCanvasFrame(frame, GetPixelScale());
        public bool ApplyCanvasFrame(RectInt frame, float nextPixelScale)
        {
            var canvasToFrame = ProjectiveMatrix.Translate(-(double)frame.x / frame.width, -(double)frame.y / frame.height) *
                ProjectiveMatrix.Scale((double)width / frame.width, (double)height / frame.height);
            return ApplyCanvasChange(frame.width, frame.height, canvasToFrame, nextPixelScale, preserveTextPixels: true);
        }

        /// <summary>
        /// Stretches the whole canvas to new dimensions without resampling or
        /// truncating owned source images. The caller owns Undo and change notification.
        /// </summary>
        public bool ApplyCanvasScale(int nextWidth, int nextHeight) => ApplyCanvasScale(nextWidth, nextHeight, GetPixelScale());
        public bool ApplyCanvasScale(int nextWidth, int nextHeight, float nextPixelScale) =>
            ApplyCanvasChange(nextWidth, nextHeight, ProjectiveMatrix.Identity, nextPixelScale, preserveTextPixels: false);

        /// <summary>Changes logical pixel lengths without changing canvas geometry. The caller owns Undo and change notification.</summary>
        public bool ApplyPixelScale(float nextPixelScale) =>
            ApplyCanvasChange(width, height, ProjectiveMatrix.Identity, nextPixelScale, preserveTextPixels: true);

        /// <summary>
        /// Reflects or rotates the whole canvas without resampling source content.
        /// Left is counterclockwise in bottom-left canvas coordinates. The caller
        /// owns Undo and change notification.
        /// </summary>
        public bool ApplyCanvasOrientation(CanvasOrientation orientation)
        {
            int nextWidth = width, nextHeight = height;
            ProjectiveMatrix canvasToNext;
            switch (orientation)
            {
                case CanvasOrientation.FlipHorizontal:
                    canvasToNext = new ProjectiveMatrix { m00 = -1, m02 = 1, m11 = 1, m22 = 1 };
                    break;
                case CanvasOrientation.FlipVertical:
                    canvasToNext = new ProjectiveMatrix { m00 = 1, m11 = -1, m12 = 1, m22 = 1 };
                    break;
                case CanvasOrientation.RotateLeft:
                    canvasToNext = new ProjectiveMatrix { m01 = -1, m02 = 1, m10 = 1, m22 = 1 };
                    nextWidth = height; nextHeight = width;
                    break;
                case CanvasOrientation.RotateRight:
                    canvasToNext = new ProjectiveMatrix { m01 = 1, m10 = -1, m12 = 1, m22 = 1 };
                    nextWidth = height; nextHeight = width;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(orientation), "Unknown canvas orientation.");
            }
            return ApplyCanvasChange(nextWidth, nextHeight, canvasToNext, GetPixelScale(), preserveTextPixels: true);
        }

        private bool ApplyCanvasChange(int nextWidth, int nextHeight, ProjectiveMatrix canvasToNext, float nextPixelScale, bool preserveTextPixels)
        {
            using var profile = WhimTexProfiler.Measure("edit.canvas_frame", this);
            ValidatePixelScale(nextPixelScale);
            float oldPixelScale = GetPixelScale();
            if (nextWidth < 1 || nextHeight < 1 || nextWidth > MaximumCanvasFrameDimension ||
                nextHeight > MaximumCanvasFrameDimension || (long)nextWidth * nextHeight > MaximumCanvasFramePixels)
                throw new ArgumentOutOfRangeException("size", "Canvas dimensions must be 1..16384 per axis and at most 16,777,216 pixels.");
            if (width < 1 || height < 1 || width > MaximumCanvasFrameDimension || height > MaximumCanvasFrameDimension ||
                (long)width * height > MaximumCanvasFramePixels)
                throw new InvalidOperationException("Current canvas dimensions exceed the supported canvas frame limits.");
            if (nextWidth == width && nextHeight == height && nextPixelScale == oldPixelScale && canvasToNext.Equals(ProjectiveMatrix.Identity)) return false;

            var oldSize = new Vector2Int(width, height);
            if (!canvasToNext.TryInverse(out var nextToCanvas))
                throw new InvalidOperationException("Cannot invert the canvas coordinate change.");
            var textBasis = !preserveTextPixels ? ProjectiveMatrix.Identity : ProjectiveMatrix.Translate(.5, .5) *
                ProjectiveMatrix.Scale((double)nextWidth / width, (double)nextHeight / height) *
                ProjectiveMatrix.Translate(-.5, -.5);
            textBasis.TryInverse(out var inverseTextBasis);
            var candidates = new List<CanvasFrameLayer>();
            var visited = new HashSet<Layer>();
            CanvasCommentAnchor[] commentAnchors = PrepareCommentAnchors(canvasToNext);
            var commentMasks = new List<(WhimTexCommentMask mask, ProjectiveMatrix placement)>();
            if (comments != null) foreach (var comment in comments) foreach (var message in comment.messages)
                if (message.attachments != null) foreach (var attachment in message.attachments)
                    if (attachment.mask != null)
                    {
                        var placement = canvasToNext * attachment.mask.localToCanvas;
                        WhimTexCommentMask.ValidatePlacement(placement);
                        commentMasks.Add((attachment.mask, placement));
                    }

            void Prepare(List<Layer> list, bool root, int depth, ProjectiveMatrix parent)
            {
                if (list == null) return;
                if (depth > 128) throw new InvalidOperationException("The layer hierarchy exceeds the supported depth of 128.");
                foreach (Layer layer in list)
                {
                    if (layer == null) continue;
                    if (!visited.Add(layer))
                        throw new InvalidOperationException("A layer occurs more than once in the document hierarchy.");
                    layer.ValidateSourceCanvasSize();
                    TextureTransform next = layer.transform;
                    ProjectiveMatrix matrix = next.ToMatrix(width, height);
                    ProjectiveMatrix oldWorld = parent * matrix;
                    bool input = layer.RequiresInput || layer.Behaviour is ShaderProcessorLayerBehaviour;
                    if (root)
                    {
                        matrix = canvasToNext * matrix;
                        if (input)
                        {
                            matrix = matrix * nextToCanvas;
                            if (!canvasToNext.TryPoint(next.pivot, out next.pivot))
                                throw new InvalidOperationException("Cannot preserve the layer pivot while changing the canvas frame.");
                        }
                    }
                    if (layer.Behaviour is TextLayerBehaviour)
                    {
                        // Text source coordinates are normalized from fixed local pixels.
                        // Its renderer and editing handles continue to use the current canvas.
                        matrix = matrix * textBasis;
                        if (!inverseTextBasis.TryPoint(next.pivot, out next.pivot))
                            throw new InvalidOperationException("Cannot preserve the text pivot while changing the canvas frame.");
                    }
                    if (!next.TrySetMatrix(matrix))
                        throw new InvalidOperationException("Cannot preserve the transform of layer '" + layer.layerName + "' while changing the canvas frame.");
                    Vector2 sourceScale = layer.sourceCanvasScale;
                    Vector2Int sourceSize = layer.sourceCanvasSize == Vector2Int.zero ? oldSize : layer.sourceCanvasSize;
                    if (!preserveTextPixels)
                    {
                        // Measure the source axes, including groups and quarter turns, in
                        // actual canvas pixels. A nonuniform resize does not follow local X/Y
                        // after rotation. Perspective uses its derivative at the source centre.
                        if (!oldWorld.TryPoint(new Double2(.5, .5), out var center))
                            throw new InvalidOperationException("Cannot preserve the source pixel metric at its projective centre.");
                        double denominator = oldWorld.m20 * .5 + oldWorld.m21 * .5 + oldWorld.m22;
                        double x0 = (oldWorld.m00 - center.x * oldWorld.m20) / denominator;
                        double x1 = (oldWorld.m10 - center.y * oldWorld.m20) / denominator;
                        double y0 = (oldWorld.m01 - center.x * oldWorld.m21) / denominator;
                        double y1 = (oldWorld.m11 - center.y * oldWorld.m21) / denominator;
                        double Ratio(double a, double b) => Math.Sqrt(a * a * nextWidth * nextWidth + b * b * nextHeight * nextHeight) /
                            Math.Sqrt(a * a * width * width + b * b * height * height);
                        sourceScale = new Vector2((float)(sourceScale.x * Ratio(x0, x1)),
                            (float)(sourceScale.y * Ratio(y0, y1)));
                        if (!float.IsFinite(sourceScale.x) || !float.IsFinite(sourceScale.y) || sourceScale.x <= 0 || sourceScale.y <= 0)
                            throw new InvalidOperationException("The layer source pixel metric exceeds its supported range.");
                    }
                    if ((double)sourceSize.x * sourceScale.x > float.MaxValue || (double)sourceSize.y * sourceScale.y > float.MaxValue)
                        throw new InvalidOperationException("The layer source pixel metric exceeds its supported range.");
                    candidates.Add(new CanvasFrameLayer(layer, next,
                        sourceSize,
                        layer.sourcePixelScale == 0f ? oldPixelScale : layer.sourcePixelScale, sourceScale));
                    if (layer.IsGroup) Prepare(layer.children, false, depth + 1, oldWorld);
                }
            }

            // Validate the entire hierarchy before changing any model data.
            Prepare(layers, true, 0, ProjectiveMatrix.Identity);
            foreach (CanvasCommentAnchor candidate in commentAnchors) candidate.comment.anchor = candidate.anchor;
            foreach (var candidate in commentMasks) candidate.mask.localToCanvas = candidate.placement;
            foreach (CanvasFrameLayer candidate in candidates)
            {
                candidate.layer.transform = candidate.transform;
                candidate.layer.sourceCanvasSize = candidate.sourceSize;
                candidate.layer.sourcePixelScale = candidate.sourcePixelScale;
                candidate.layer.sourceCanvasScale = candidate.sourceScale;
            }
            width = nextWidth;
            height = nextHeight;
            pixelScale = nextPixelScale;
            RefreshTransformHierarchy();
            return true;
        }

        private readonly struct CanvasFrameLayer
        {
            internal readonly Layer layer;
            internal readonly TextureTransform transform;
            internal readonly Vector2Int sourceSize;
            internal readonly float sourcePixelScale;
            internal readonly Vector2 sourceScale;
            internal CanvasFrameLayer(Layer layer, TextureTransform transform, Vector2Int sourceSize, float sourcePixelScale, Vector2 sourceScale)
            { this.layer = layer; this.transform = transform; this.sourceSize = sourceSize; this.sourcePixelScale = sourcePixelScale; this.sourceScale = sourceScale; }
        }
    }
}
