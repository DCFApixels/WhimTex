using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        internal const long SmudgeMaximumCarryPixels = 18 * 1024 * 1024;
        [NonSerialized] private RenderTexture smudgeCarry, smudgeNext, smudgeBackdrop, smudgeSample, smudgeSampleBackdrop;
        [NonSerialized] private BrushSpacingState smudgeSpacing;
        [NonSerialized] private ProjectiveMatrix smudgeToCanvas, smudgeToSource;
        [NonSerialized] private Vector2 smudgeLastCenter;
        [NonSerialized] private Vector2 smudgeInitialPhase;
        [NonSerialized] private float smudgeDiameter;
        [NonSerialized] private bool smudgeWrap;
        [NonSerialized] private bool smudgeStrokeChanged;
        internal bool SmudgeStrokeChanged => smudgeStrokeChanged;

        // Color travels on the source's pixel grid, independently of the continuous
        // canvas-space brush mask. Resampling both ways at every dab causes diffusion.
        internal void BeginSmudgeStroke(Vector2 sourceUv, int width, int height, float size,
            RenderTexture canvasSample = null, bool tiled = false, float mixing = 1f)
        {
            ReleaseSmudgeStroke();
            RenderTexture surface = EnsurePaintSurface(width, height);
            smudgeToCanvas = Owner.PixelCanvasTransform.ToMatrix(width, height);
            if (surface == null || !smudgeToCanvas.TryInverse(out smudgeToSource)) return;
            Material material = WhimTexMaterials.SmudgeBrush;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Smudge Brush shader is unavailable.");
            smudgeDiameter = Mathf.Clamp(size, 1f, 4096f);
            smudgeMixing = Mathf.Clamp01(mixing);
            smudgeWrap = tiled;
            smudgeSpacing = default;
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                if (smudgeMixing >= 1 || canvasSample != null)
                    smudgeBackdrop = SmudgeTemporary(surface.width, surface.height);
                if (canvasSample != null)
                {
                    smudgeSample = SmudgeTemporary(width, height, smudgeMixing > 0 && smudgeMixing < 1 ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf);
                    if (smudgeMixing >= 1 && (surface.width != width || surface.height != height))
                        smudgeSampleBackdrop = SmudgeTemporary(width, height);
                    Material conversion = WhimTexMaterials.AlphaConversion;
                    conversion.SetFloat("_Mode", 0);
                    conversion.SetFloat("_DecodeSource", 0);
                    Graphics.Blit(canvasSample, smudgeSample, conversion);
                }
                ConfigureSmudge(material, width, height, null);
                smudgeLastCenter = smudgeToCanvas.Point(sourceUv);
                smudgeTransportLastCenter = smudgeLastCenter;
                if (smudgeWrap) smudgeLastCenter = TiledCanvasUtility.Wrap(smudgeLastCenter);
                if (smudgeMixing < 1)
                    smudgeTransport = new SmudgeTransport(smudgeSample != null ? smudgeSample : surface, smudgeMixing > 0, smudgeSample != null);
                RenderTexture colorSource = SmudgeColorSource(surface);
                Vector2 colorCenter = smudgeSample != null ? smudgeLastCenter : (Vector2)smudgeToSource.Point(smudgeLastCenter);
                var colorPixel = Vector2.Scale(colorCenter, new Vector2(colorSource.width, colorSource.height));
                smudgeInitialPhase = new Vector2(colorPixel.x - Mathf.Floor(colorPixel.x), colorPixel.y - Mathf.Floor(colorPixel.y));
                if (smudgeMixing > 0)
                {
                    EnsureSmudgeCarry(material, surface, smudgeLastCenter, width, height);
                    PickupSmudge(material, surface, smudgeLastCenter, smudgeCarry);
                }
            }
            catch { ReleaseSmudgeStroke(); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousSrgb; }
        }

        internal void SmudgeSegment(Vector2 fromSourceUv, Vector2 toSourceUv, int width, int height,
            float hardness, float strength, float flow, Texture selection = null)
        {
            if (StrokeWritesNothing) return;
            if (smudgeCarry == null && smudgeTransport == null) return;
            Vector2 from = smudgeToCanvas.Point(fromSourceUv), to = smudgeToCanvas.Point(toSourceUv);
            Vector2 dimensions = new Vector2(width, height);
            float distance = Vector2.Scale(to - from, dimensions).magnitude;
            if (!float.IsFinite(distance) || distance <= 0f) return;
            double spacing = SmudgeSpacing(smudgeDiameter);
            int count = smudgeSpacing.Sample(distance, spacing, false, out double first);
            if (count > 32768) throw new InvalidOperationException("Smudge stroke segment is too long.");
            if (count == 0) return;
            if (strength <= 0f || flow <= 0f)
            {
                if (smudgeTransport != null) smudgeTransportLastCenter = Vector2.Lerp(from, to, (float)((first + (count - 1) * spacing) / distance));
                return;
            }
            RenderTexture surface = EnsurePaintSurface(width, height);
            Material material = WhimTexMaterials.SmudgeBrush;
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                ConfigureSmudge(material, width, height, selection);
                material.SetFloat("_Hardness", Mathf.Clamp01(hardness));
                material.SetFloat("_Strength", Mathf.Clamp01(strength));
                material.SetFloat("_FrozenCarry", strength >= 1f ? 1 : 0);
                material.SetFloat("_Flow", Mathf.Clamp01(flow));
                for (int i = 0; i < count; i++)
                {
                    Vector2 center = Vector2.Lerp(from, to, (float)((first + i * spacing) / distance));
                    if (smudgeTransport != null)
                    {
                        TransportSmudgeDab(material, surface, center, width, height, hardness, strength, flow, selection);
                        continue;
                    }
                    if (smudgeWrap) center = TiledCanvasUtility.Wrap(center);
                    EnsureSmudgeCarry(material, surface, center, width, height);
                    material.SetTexture("_Carry", smudgeCarry);
                    DepositSmudge(material, surface, smudgeToCanvas, smudgeToSource, center, width, height);
                    if (smudgeSample != null)
                        DepositSmudge(material, smudgeSample, ProjectiveMatrix.Identity, ProjectiveMatrix.Identity, center, width, height);
                    // Refresh from the painted result, including soft edges and Flow.
                    // Full Strength keeps the initial image unchanged for the whole drag.
                    if (strength < 1f)
                    {
                        PickupSmudge(material, surface, center, smudgeNext, Mathf.Clamp01(strength));
                        (smudgeCarry, smudgeNext) = (smudgeNext, smudgeCarry);
                    }
                    smudgeLastCenter = center;
                    paintSurfaceDirty = true;
                    smudgeStrokeChanged = true;
                    unchecked { paintSurfaceRevision++; }
                }
                ApplyPaintWriteProtection();
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousSrgb; }
        }

        internal static double SmudgeSpacing(float diameter) => Math.Max(1d, diameter * .025d);

        private static RenderTexture SmudgeTemporary(int width, int height, RenderTextureFormat format = RenderTextureFormat.ARGBHalf)
        {
            var result = RenderTexture.GetTemporary(width, height, 0, format, RenderTextureReadWrite.Linear);
            result.filterMode = FilterMode.Bilinear;
            result.wrapMode = TextureWrapMode.Clamp;
            return result;
        }

        private void ConfigureSmudge(Material material, int width, int height, Texture selection)
        {
            material.SetVector("_TipSize", new Vector4(smudgeDiameter / width, smudgeDiameter / height, 0, 0));
            material.SetFloat("_Wrap", smudgeWrap ? 1 : 0);
            material.SetTexture("_Selection", selection != null ? selection : Texture2D.whiteTexture);
            smudgeToSource.SetShader(material, "_SelectionRow");
        }

        private void PickupSmudge(Material material, RenderTexture surface, Vector2 center, RenderTexture target, float retention = 0f)
        {
            RenderTexture source = SmudgeColorSource(surface);
            ProjectiveMatrix toColor = smudgeSample != null ? ProjectiveMatrix.Identity : smudgeToSource;
            SetSmudgeGrid(material, source, center, target.width, target.height);
            material.SetFloat("_PickupRetention", retention);
            // Initial capture can write to smudgeCarry itself; never bind that
            // destination as an input texture when retention is disabled.
            material.SetTexture("_Carry", retention > 0f ? smudgeCarry : null);
            toColor.SetShader(material, "_ColorRow");
            (smudgeSample != null ? ProjectiveMatrix.Identity : smudgeToCanvas).SetShader(material, "_CanvasRow");
            Graphics.Blit(source, target, material, 0);
        }

        private void SetSmudgeGrid(Material material, RenderTexture source, Vector2 center, int width, int height)
        {
            Vector2 p = smudgeSample != null ? center : (Vector2)smudgeToSource.Point(center);
            float x = Mathf.Floor(p.x * source.width) - width / 2;
            float y = Mathf.Floor(p.y * source.height) - height / 2;
            material.SetVector("_ColorOrigin", new Vector4(x, y, 0, 0));
            material.SetVector("_ColorSize", new Vector4(width, height, 0, 0));
            material.SetVector("_ColorDimensions", new Vector4(source.width, source.height, 0, 0));
            material.SetVector("_ColorPhase", new Vector4(p.x * source.width - Mathf.Floor(p.x * source.width) - smudgeInitialPhase.x,
                p.y * source.height - Mathf.Floor(p.y * source.height) - smudgeInitialPhase.y, 0, 0));
        }

        // A rotated or scaled native Drawing needs a rectangular source-space tip,
        // not a downscaled canvas-space square. Projective footprints can grow as
        // the stroke moves; preserve carried pixels without stretching them.
        internal static Vector2Int SmudgeCarrySize(ProjectiveMatrix toColor, int sourceWidth, int sourceHeight,
            Vector2 center, float diameter, int canvasWidth, int canvasHeight)
        {
            Double2 middle = toColor.Point(center);
            double rx = 0, ry = 0, minW = double.PositiveInfinity, maxW = double.NegativeInfinity;
            for (int corner = 0; corner < 4; corner++)
            {
                var canvas = new Double2(center.x + ((corner & 1) == 0 ? -.5 : .5) * diameter / canvasWidth,
                    center.y + ((corner & 2) == 0 ? -.5 : .5) * diameter / canvasHeight);
                double w = toColor.m20 * canvas.x + toColor.m21 * canvas.y + toColor.m22;
                minW = Math.Min(minW, w); maxW = Math.Max(maxW, w);
                Double2 p = toColor.Point(canvas);
                rx = Math.Max(rx, Math.Abs(p.x - middle.x) * sourceWidth);
                ry = Math.Max(ry, Math.Abs(p.y - middle.y) * sourceHeight);
            }
            if (!ProjectiveMatrix.Finite(rx) || !ProjectiveMatrix.Finite(ry) || minW <= 0 && maxW >= 0)
                throw new InvalidOperationException("Smudge tip crosses the layer's projection horizon. Use a smaller tip or adjust the layer transform.");
            // Even dimensions keep the accumulator's anchor stable when it grows.
            // Padding covers the fractional mask center and bilinear edge samples.
            double width = Math.Ceiling((rx + 2) / 4) * 8, height = Math.Ceiling((ry + 2) / 4) * 8;
            if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize || width * height > SmudgeMaximumCarryPixels)
                throw new InvalidOperationException("Smudge native-pixel tip exceeds the buffer budget. Use a smaller tip or adjust the layer transform.");
            return new Vector2Int((int)width, (int)height);
        }

        private void EnsureSmudgeCarry(Material material, RenderTexture surface, Vector2 center, int width, int height)
        {
            RenderTexture source = SmudgeColorSource(surface);
            var size = SmudgeCarrySize(smudgeSample != null ? ProjectiveMatrix.Identity : smudgeToSource,
                source.width, source.height, center, smudgeDiameter, width, height);
            if (smudgeCarry != null)
            {
                size.x = Math.Max(size.x, smudgeCarry.width); size.y = Math.Max(size.y, smudgeCarry.height);
                if (size.x == smudgeCarry.width && size.y == smudgeCarry.height) return;
                if ((long)size.x * size.y > SmudgeMaximumCarryPixels)
                    throw new InvalidOperationException("Smudge native-pixel tip exceeds the buffer budget. Use a smaller tip or adjust the layer transform.");
            }
            RenderTexture carry = null, next = null;
            try
            {
                var format = smudgeMixing > 0 && smudgeMixing < 1 ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf;
                carry = SmudgeTemporary(size.x, size.y, format);
                next = SmudgeTemporary(size.x, size.y, format);
                if (smudgeCarry != null)
                {
                    PickupSmudge(material, surface, smudgeLastCenter, next);
                    material.SetTexture("_Carry", smudgeCarry);
                    material.SetVector("_OldCarrySize", new Vector4(smudgeCarry.width, smudgeCarry.height, 0, 0));
                    Graphics.Blit(next, carry, material, 2);
                }
                ReleaseSmudgeTexture(ref smudgeCarry); ReleaseSmudgeTexture(ref smudgeNext);
                smudgeCarry = carry; smudgeNext = next;
            }
            catch
            {
                ReleaseSmudgeTexture(ref carry); ReleaseSmudgeTexture(ref next);
                throw;
            }
        }

        private void DepositSmudge(Material material, RenderTexture target, ProjectiveMatrix toCanvas,
            ProjectiveMatrix toSource, Vector2 center, int width, int height)
        {
            RenderTexture backdrop = target == smudgeSample && smudgeSampleBackdrop != null ? smudgeSampleBackdrop : smudgeBackdrop;
            {
                toCanvas.SetShader(material, "_DepositRow");
                material.SetFloat("_NativeDeposit", smudgeSample == null ? 1 : 0);
                material.SetTexture("_Backdrop", backdrop);
                bool broadWrap = smudgeWrap && (smudgeDiameter > width || smudgeDiameter > height);
                material.SetFloat("_PeriodicTip", broadWrap ? 1 : 0);
                int copies = smudgeWrap && !broadWrap ? 1 : 0;
                for (int y = -copies; y <= copies; y++) for (int x = -copies; x <= copies; x++)
                {
                    Vector2 dab = center + new Vector2(x, y);
                    Vector2 radius = new Vector2(smudgeDiameter / width, smudgeDiameter / height) * .5f;
                    Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
                    double minimumW = double.PositiveInfinity, maximumW = double.NegativeInfinity;
                    bool finite = true;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        Vector2 canvas = dab + new Vector2((corner & 1) == 0 ? -radius.x : radius.x,
                            (corner & 2) == 0 ? -radius.y : radius.y);
                        double w = toSource.m20 * canvas.x + toSource.m21 * canvas.y + toSource.m22;
                        minimumW = Math.Min(minimumW, w); maximumW = Math.Max(maximumW, w);
                        Vector2 p = toSource.Point(canvas);
                        finite &= float.IsFinite(p.x) && float.IsFinite(p.y);
                        min = Vector2.Min(min, p); max = Vector2.Max(max, p);
                    }
                    // A tip crossing the inverse projection's horizon has no finite corner bounds.
                    if (broadWrap || !finite || minimumW <= 0 && maximumW >= 0) { min = Vector2.zero; max = Vector2.one; }
                    int left = Mathf.Clamp(Mathf.FloorToInt(min.x * target.width) - 1, 0, target.width);
                    int bottom = Mathf.Clamp(Mathf.FloorToInt(min.y * target.height) - 1, 0, target.height);
                    int right = Mathf.Clamp(Mathf.CeilToInt(max.x * target.width) + 1, 0, target.width);
                    int top = Mathf.Clamp(Mathf.CeilToInt(max.y * target.height) + 1, 0, target.height);
                    if (right <= left || top <= bottom) continue;
                    RenderTexture.active = null;
                    if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0)
                        Graphics.CopyTexture(target, 0, 0, left, bottom, right - left, top - bottom, backdrop, 0, 0, left, bottom);
                    else Graphics.Blit(target, backdrop);
                    material.SetVector("_Center", dab);
                    SetSmudgeGrid(material, smudgeSample != null ? smudgeSample : target, dab, smudgeCarry.width, smudgeCarry.height);
                    RenderTexture.active = target;
                    GL.PushMatrix();
                    try
                    {
                        GL.LoadOrtho();
                        if (!material.SetPass(1)) throw new InvalidOperationException("Cannot render Smudge Brush.");
                        float l = left / (float)target.width, b = bottom / (float)target.height;
                        float r = right / (float)target.width, t = top / (float)target.height;
                        GL.Begin(GL.QUADS);
                        GL.TexCoord2(l, b); GL.Vertex3(l, b, 0);
                        GL.TexCoord2(l, t); GL.Vertex3(l, t, 0);
                        GL.TexCoord2(r, t); GL.Vertex3(r, t, 0);
                        GL.TexCoord2(r, b); GL.Vertex3(r, b, 0);
                        GL.End();
                    }
                    finally { GL.PopMatrix(); }
                }
            }
        }

        private void ReleaseSmudgeStroke()
        {
            smudgeTransport?.Dispose(); smudgeTransport = null;
            ReleaseSmudgeTexture(ref smudgeCarry); ReleaseSmudgeTexture(ref smudgeNext);
            ReleaseSmudgeTexture(ref smudgeBackdrop); ReleaseSmudgeTexture(ref smudgeSample); ReleaseSmudgeTexture(ref smudgeSampleBackdrop);
            smudgeSpacing = default;
            smudgeStrokeChanged = false;
        }

        private RenderTexture SmudgeColorSource(RenderTexture surface) => smudgeSample != null ? smudgeSample : smudgeTransport != null ? smudgeTransport.Image : surface;

        private static void ReleaseSmudgeTexture(ref RenderTexture texture)
        {
            if (texture != null) RenderTexture.ReleaseTemporary(texture);
            texture = null;
        }
    }
}
