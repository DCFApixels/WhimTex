using System;
using System.Collections.Generic;
using UnityEngine;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class VectorLayerBehaviour : LayerBehaviour
    {
        public List<VectorShape> shapes = new List<VectorShape>();
        public Vector2 referenceSize = Vector2.one * 256;
        public VectorEdgeMode edgeMode;
        public float feather;
        public VectorFeatherPosition featherPosition = VectorFeatherPosition.Centered;
        // An absent draft must remain null across native serialization and Undo.
        [SerializeReference] public VectorBrushDraft brushDraft;

        [NonSerialized] private Texture2D raster;
        [NonSerialized] private ProceduralLayerThumbnail thumbnail;
        [NonSerialized] private ulong rasterHash;
        [NonSerialized] private ProjectiveMatrix rasterMatrix;
        [NonSerialized] private bool rasterValid, rasterClipped;
        [NonSerialized] private RenderTexture painted;
        [NonSerialized] private List<VectorRasterizer.Coverage> coverage;
        [NonSerialized] private ulong coverageHash;
        [NonSerialized] private bool coverageValid, cpuFallback;
        internal int CoverageBuildCount { get; private set; }
        [NonSerialized] private VectorRasterizer.PreparedShape[] preparedContours;
        internal VectorRasterizer.PreparedShape[] PreparedContours => preparedContours;

        // Only transient Boolean render models use this path; editable/serialized data stays unchanged.
        internal void SetPreparedContours(List<Paths64> paths)
        {
            Validate();
            if (paths == null || paths.Count != shapes.Count)
                throw new InvalidOperationException("Prepared Vector geometry must match its styles.");
            var next = new VectorRasterizer.PreparedShape[paths.Count];
            int contours = 0, anchors = 0;
            for (int i = 0; i < next.Length; i++)
            {
                if (shapes[i].contours.Count != 0) throw new InvalidOperationException("Prepared Vector styles cannot contain editable geometry.");
                next[i] = new VectorRasterizer.PreparedShape(paths[i], referenceSize, ref contours, ref anchors);
            }
            ReleaseTransientResources();
            preparedContours = next;
        }

        public override TransformTilingMode SourceTiling => TransformTilingMode.Unbounded;

        /// <summary>Validates without repairing, deleting, renumbering or clamping source data.</summary>
        public void Validate()
        {
            VectorGeometry.ValidateReferenceSize(referenceSize);
            if ((uint)edgeMode > 1) throw new InvalidOperationException("Unknown Vector edge mode.");
            if (!VectorGeometry.Finite(feather) || feather < 0 || feather > 8192 || (uint)featherPosition > 2)
                throw new InvalidOperationException("Vector Feather must be finite in [0,8192] and use a known position.");
            if (shapes == null || shapes.Count > VectorGeometry.MaxShapes) throw VectorGeometry.Limit("shape count");
            if (preparedContours != null)
            {
                if (preparedContours.Length != shapes.Count) throw new InvalidOperationException("Prepared Vector style count changed.");
                for (int i = 0; i < preparedContours.Length; i++)
                    if (!preparedContours[i].reference.Equals(referenceSize) || shapes[i]?.contours == null || shapes[i].contours.Count != 0)
                        throw new InvalidOperationException("Prepared Vector geometry must be replaced, not edited.");
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int contours = 0, points = 0;
            foreach (var shape in shapes)
            {
                if (shape == null) throw new InvalidOperationException("Vector shapes cannot contain null.");
                ValidateId(shape.id, ids);
                if ((uint)shape.fillMode > (uint)VectorFillMode.Freeform || (uint)shape.strokeMode > (uint)VectorFillMode.Freeform || (uint)shape.fillRule > 1 || (uint)shape.strokePosition > 2 ||
                    (uint)shape.join > 2 || (uint)shape.cap > 2) throw new InvalidOperationException("Unknown Vector style enum value.");
                ValidateColor(shape.fillColor); ValidateColor(shape.strokeColor);
                FigureFill.ValidateFreeform(shape.fillMode, shape.freeform);
                FigureFill.ValidateFreeform(shape.strokeMode, shape.strokeFreeform);
                ValidateCoordinate(shape.gradientStart); ValidateCoordinate(shape.gradientEnd);
                ValidateCoordinate(shape.strokeGradientStart); ValidateCoordinate(shape.strokeGradientEnd);
                if (!VectorGeometry.Finite(shape.strokeGradientRepetitions) || shape.strokeGradientRepetitions < .0001f)
                    throw new InvalidOperationException("Vector strokeGradientRepetitions must be finite and at least 0.0001.");
                if (shape.strokeGradient == null && FigureFill.IsPaletteGradient(shape.strokeMode))
                    throw new InvalidOperationException("Vector gradient strokes require a gradient.");
                if (!VectorGeometry.Finite(shape.gradientRepetitions) || shape.gradientRepetitions < .0001f)
                    throw new InvalidOperationException("Vector gradientRepetitions must be finite and at least 0.0001.");
                if (shape.gradient == null && FigureFill.IsPaletteGradient(shape.fillMode))
                    throw new InvalidOperationException("Vector gradient fills require a gradient.");
                if (!VectorGeometry.Finite(shape.strokeWidth) || shape.strokeWidth < 0 || shape.strokeWidth > VectorGeometry.MaxCoordinate ||
                    !VectorGeometry.Finite(shape.miterLimit) || shape.miterLimit < 1 || shape.miterLimit > 10000 ||
                    !VectorGeometry.Finite(shape.dashOffset) || Math.Abs(shape.dashOffset) > VectorGeometry.MaxCoordinate)
                    throw new InvalidOperationException("Vector stroke width, miter limit or dash offset is outside its supported range.");
                if (shape.dashPattern != null)
                {
                    if (shape.dashPattern.Count > 256) throw VectorGeometry.Limit("dash pattern count");
                    foreach (float dash in shape.dashPattern)
                        if (!VectorGeometry.Finite(dash) || dash <= 0 || dash > VectorGeometry.MaxCoordinate)
                            throw new InvalidOperationException("Vector dash lengths must be finite positive values in the declared pattern units.");
                }
                if (shape.contours == null || (contours += shape.contours.Count) > VectorGeometry.MaxContours)
                    throw VectorGeometry.Limit("contour count");
                foreach (var contour in shape.contours)
                {
                    if (contour == null) throw new InvalidOperationException("Vector contours cannot contain null.");
                    ValidateId(contour.id, ids);
                    if (contour.points == null || (points += contour.points.Count) > VectorGeometry.MaxPoints)
                        throw VectorGeometry.Limit("anchor count");
                    foreach (var point in contour.points)
                    {
                        VectorGeometry.ValidatePoint(point); ValidateId(point.id, ids);
                        if ((uint)point.tangentMode > 2) throw new InvalidOperationException("Unknown Vector tangent mode.");
                        ValidateCoordinate(point.position);
                        ValidateCoordinate(point.position + point.inTangent);
                        ValidateCoordinate(point.position + point.outTangent);
                    }
                }
            }
        }

        private static void ValidateId(string id, HashSet<string> ids)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 256 || !ids.Add(id))
                throw new InvalidOperationException("Vector shape, contour and point IDs must be nonempty and unique within the layer (maximum 256 characters).");
        }
        private void ValidateCoordinate(Vector2 value)
        {
            value = Vector2.Scale(value, referenceSize);
            if (!VectorGeometry.Finite(value) || Math.Abs(value.x) > VectorGeometry.MaxCoordinate || Math.Abs(value.y) > VectorGeometry.MaxCoordinate)
                throw VectorGeometry.Limit("coordinate range");
        }
        private static void ValidateColor(Color color)
        {
            if (!VectorGeometry.Finite(color.r) || !VectorGeometry.Finite(color.g) || !VectorGeometry.Finite(color.b) ||
                !VectorGeometry.Finite(color.a) || color.a < 0 || color.a > 1)
                throw new InvalidOperationException("Vector colors require finite RGB and alpha in [0,1].");
        }

        public override Texture2D GetPreviewTexture(int size)
        {
            ulong hash = ComputeContentHash();
            thumbnail ??= new ProceduralLayerThumbnail();
            return thumbnail.Get(this, size, unchecked((int)(hash ^ (hash >> 32))));
        }

        internal override RenderTexture Render(in LayerRenderContext context) => RenderWithOwner(context, Owner);

        internal RenderTexture RenderWithOwner(in LayerRenderContext context, Layer owner)
        {
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                var applied = context.applyTransform ? owner.RenderTransform : TextureTransform.Default;
                // Repeated/mirrored/clamped image extension uses the ordinary sampling contract.
                // Clip and Unbounded rasterize transformed geometry directly on the destination grid.
                bool sampleExtension = context.applyTransform && applied.tiling != TransformTilingMode.Clip && applied.tiling != TransformTilingMode.Unbounded;
                var matrix = context.applyTransform && !sampleExtension
                    ? applied.ToMatrix(context.activeDocument.width, context.activeDocument.height) : ProjectiveMatrix.Identity;
                if (context.applyTransform && !sampleExtension && context.activeDocument.RenderingViewport)
                    matrix = context.activeDocument.CanvasToViewport * matrix;
                bool clip = sampleExtension || (context.applyTransform && applied.tiling == TransformTilingMode.Clip);
                ulong hash = ComputeContentHash();
                Texture output = cpuFallback ? raster : painted;
                bool gridChanged = output == null || output.width != context.width || output.height != context.height ||
                    !rasterMatrix.Equals(matrix) || rasterClipped != clip;
                bool paintStorageLost = !cpuFallback && painted != null && !painted.IsCreated();
                if (!rasterValid || gridChanged || paintStorageLost ||
                    rasterHash != hash || !rasterMatrix.Equals(matrix) || rasterClipped != clip)
                {
                    using var rasterProfile = WhimTexProfiler.Measure("vector.raster", context.activeDocument, owner,
                        detail: "Vector coverage cache and paint");
                    rasterValid = false;
                    Validate();
                    VectorRasterizer.ValidateSize(context.width, context.height);
                    ulong geometry = ComputeGeometryHash();
                    if (!coverageValid || gridChanged || coverageHash != geometry)
                    {
                        ReleaseCoverage();
                        using var preparation = WhimTexProfiler.Measure("vector.coverage", context.activeDocument, owner);
                        CoverageBuildCount++;
                        var material = WhimTexMaterials.VectorPaint;
                        cpuFallback = material == null || !material.shader.isSupported ||
                            !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat);
                        if (!cpuFallback)
                        {
                            try { coverage = VectorRasterizer.Prepare(this, context.width, context.height, matrix, clip); }
                            catch (VectorRasterizer.CoverageBudgetExceeded) { cpuFallback = true; }
                        }
                        coverageHash = geometry; coverageValid = true;
                    }
                    if (cpuFallback)
                    {
                        if (painted != null) { UnityEngine.Object.DestroyImmediate(painted); painted = null; }
                        if (raster == null || raster.width != context.width || raster.height != context.height)
                        {
                            if (raster != null) UnityEngine.Object.DestroyImmediate(raster);
                            raster = new Texture2D(context.width, context.height, TextureFormat.RGBAFloat, false, true)
                            { name = "Vector raster", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                        }
                        VectorRasterizer.Render(this, raster, matrix, clip); raster.Apply(false, false);
                        output = raster;
                    }
                    else
                    {
                        if (raster != null) { UnityEngine.Object.DestroyImmediate(raster); raster = null; }
                        if (painted == null || painted.width != context.width || painted.height != context.height)
                        {
                            if (painted != null) UnityEngine.Object.DestroyImmediate(painted);
                            painted = new RenderTexture(context.width, context.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                            { name = "Vector paint", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                        }
                        if (!painted.IsCreated()) painted.Create();
                        using var paint = WhimTexProfiler.Measure("vector.paint", context.activeDocument, owner);
                        using (paint.Gpu()) VectorPaintRenderer.Render(this, coverage, matrix, painted);
                        output = painted;
                    }
                    output.filterMode = edgeMode == VectorEdgeMode.Step ? FilterMode.Point : FilterMode.Bilinear;
                    rasterHash = hash; rasterMatrix = matrix; rasterClipped = clip; rasterValid = true;
                }
                var renderedContext = sampleExtension ? context : new LayerRenderContext(context.activeDocument, context.input,
                    context.width, context.height, context.scaleMultiplier, applyTransform: false, applyFx: context.applyFx,
                    transformFxCoordinates: context.transformFxCoordinates);
                return owner.ApplyTransformAndFx(output, renderedContext);
            }
            catch
            {
                if (!rasterValid) ReleaseTransientResources();
                throw;
            }
            finally { GL.sRGBWrite = srgb; RenderTexture.active = previous; }
        }

        internal override void ReleaseTransientResources()
        {
            if (raster != null) UnityEngine.Object.DestroyImmediate(raster);
            raster = null; rasterValid = false;
            if (painted != null) UnityEngine.Object.DestroyImmediate(painted);
            painted = null; ReleaseCoverage();
            thumbnail?.Dispose(); thumbnail = null;
        }

        private void ReleaseCoverage()
        {
            if (coverage != null) foreach (var entry in coverage) entry.Dispose();
            coverage = null; coverageValid = false;
        }

        // Hash data, not edit revisions: Undo and deserialization can restore any prior values.
        internal ulong ComputeContentHash() => ComputeHash(false, false);
        internal ulong ComputeGeometryHash(bool booleanOperand = false) => ComputeHash(true, booleanOperand);
        private ulong ComputeHash(bool geometryOnly, bool booleanOperand)
        {
            var hash = new ContentHash();
            if (shapes != null && shapes.Count > VectorGeometry.MaxShapes) throw VectorGeometry.Limit("shape count");
            int contourCount = 0, pointCount = 0;
            hash.Add(referenceSize);
            if (preparedContours != null) foreach (var contour in preparedContours)
            { hash.Add(unchecked((int)contour.hash)); hash.Add(unchecked((int)(contour.hash >> 32))); }
            if (!booleanOperand)
            {
                hash.Add((int)edgeMode);
                if (edgeMode != VectorEdgeMode.Step && feather > 0) { hash.Add(feather); hash.Add((int)featherPosition); }
            }
            if (!geometryOnly) hash.Add((int)filterMode);
            hash.Add(shapes?.Count ?? -1);
            if (shapes != null) foreach (var shape in shapes)
            {
                hash.Add(shape == null ? 0 : 1); if (shape == null) continue;
                hash.Add(shape.id); hash.Add(shape.visible ? 1 : 0); hash.Add((int)shape.fillRule);
                if (!geometryOnly)
                {
                    hash.Add((int)shape.fillMode);
                    hash.Add(shape.fillColor); hash.Add(shape.gradient?.GetHashCode() ?? 0); hash.Add(shape.gradientStart); hash.Add(shape.gradientEnd);
                    hash.Add(shape.gradientRepetitions);
                    hash.Add(shape.freeform?.ContentHash() ?? 0); hash.Add(shape.strokeFreeform?.ContentHash() ?? 0);
                    hash.Add((int)shape.strokeMode); hash.Add(shape.strokeGradient?.GetHashCode() ?? 0);
                    hash.Add(shape.strokeGradientStart); hash.Add(shape.strokeGradientEnd); hash.Add(shape.strokeGradientRepetitions);
                    hash.Add(shape.strokeColor);
                }
                if (!booleanOperand)
                {
                    hash.Add(shape.fillMode != VectorFillMode.None ? 1 : 0);
                    hash.Add(shape.stroke && shape.strokeMode != VectorFillMode.None && shape.strokeWidth > 0 ? 1 : 0);
                    hash.Add(shape.strokeWidth); hash.Add((int)shape.strokePosition);
                    hash.Add((int)shape.join); hash.Add((int)shape.cap); hash.Add(shape.miterLimit); hash.Add(shape.dashOffset);
                    hash.Add(shape.dashPatternRelative ? 1 : 0);
                    hash.Add(shape.dashPattern?.Count ?? -1);
                    if (shape.dashPattern != null && shape.dashPattern.Count > 256) throw VectorGeometry.Limit("dash pattern count");
                    if (shape.dashPattern != null) foreach (var dash in shape.dashPattern) hash.Add(dash);
                }
                hash.Add(shape.contours?.Count ?? -1);
                if ((contourCount += shape.contours?.Count ?? 0) > VectorGeometry.MaxContours) throw VectorGeometry.Limit("contour count");
                if (shape.contours != null) foreach (var contour in shape.contours)
                {
                    hash.Add(contour == null ? 0 : 1); if (contour == null) continue;
                    hash.Add(contour.id); hash.Add(contour.closed ? 1 : 0); hash.Add(contour.points?.Count ?? -1);
                    if ((pointCount += contour.points?.Count ?? 0) > VectorGeometry.MaxPoints) throw VectorGeometry.Limit("anchor count");
                    if (contour.points != null) foreach (var point in contour.points)
                    {
                        hash.Add(point == null ? 0 : 1); if (point == null) continue;
                        hash.Add(point.id); hash.Add(point.position); hash.Add(point.inTangent); hash.Add(point.outTangent); hash.Add((int)point.tangentMode);
                    }
                }
            }
            return hash.Value;
        }

        private struct ContentHash
        {
            internal ulong Value;
            internal void Add(int value) { unchecked { Value = (Value ^ (uint)value) * 1099511628211UL + 14695981039346656037UL; } }
            internal void Add(float value) => Add(value.GetHashCode());
            internal void Add(string value)
            {
                if (value != null && value.Length > 256) throw VectorGeometry.Limit("ID length");
                Add(value?.Length ?? -1); if (value != null) foreach (char c in value) Add((int)c);
            }
            internal void Add(Vector2 value) { Add(value.x); Add(value.y); }
            internal void Add(Color value) { Add(value.r); Add(value.g); Add(value.b); Add(value.a); }
        }
    }
}
