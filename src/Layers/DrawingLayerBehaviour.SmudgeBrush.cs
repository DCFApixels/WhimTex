using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        [NonSerialized] private RenderTexture smudgeCarry, smudgePickup, smudgeNext, smudgeBackdrop, smudgeSample, smudgeSampleBackdrop;
        [NonSerialized] private BrushSpacingState smudgeSpacing;
        [NonSerialized] private ProjectiveMatrix smudgeToCanvas, smudgeToSource;
        [NonSerialized] private float smudgeDiameter;
        [NonSerialized] private bool smudgeWrap;
        [NonSerialized] private bool smudgeStrokeChanged;
        internal bool SmudgeStrokeChanged => smudgeStrokeChanged;

        // The small premultiplied tip is carried along the path. A visible-stack
        // sample is frozen at pickup, then receives our own deposits as feedback.
        internal void BeginSmudgeStroke(Vector2 sourceUv, int width, int height, float size,
            RenderTexture canvasSample = null, bool tiled = false)
        {
            ReleaseSmudgeStroke();
            RenderTexture surface = EnsurePaintSurface(width, height);
            smudgeToCanvas = Owner.PixelCanvasTransform.ToMatrix(width, height);
            if (surface == null || !smudgeToCanvas.TryInverse(out smudgeToSource)) return;
            Material material = WhimTexMaterials.SmudgeBrush;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Smudge Brush shader is unavailable.");
            smudgeDiameter = Mathf.Clamp(size, 1f, 4096f);
            smudgeWrap = tiled;
            smudgeSpacing = default;
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                int tipSize = Mathf.Clamp(Mathf.CeilToInt(smudgeDiameter), 1, 1024);
                smudgeCarry = SmudgeTemporary(tipSize, tipSize);
                smudgePickup = SmudgeTemporary(tipSize, tipSize);
                smudgeNext = SmudgeTemporary(tipSize, tipSize);
                smudgeBackdrop = SmudgeTemporary(surface.width, surface.height);
                if (canvasSample != null)
                {
                    smudgeSample = SmudgeTemporary(width, height);
                    if (surface.width != width || surface.height != height)
                        smudgeSampleBackdrop = SmudgeTemporary(width, height);
                    Material conversion = WhimTexMaterials.AlphaConversion;
                    conversion.SetFloat("_Mode", 0);
                    conversion.SetFloat("_DecodeSource", 0);
                    Graphics.Blit(canvasSample, smudgeSample, conversion);
                }
                ConfigureSmudge(material, width, height, null);
                PickupSmudge(material, surface, smudgeToCanvas.Point(sourceUv), smudgeCarry);
            }
            catch { ReleaseSmudgeStroke(); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousSrgb; }
        }

        internal void SmudgeSegment(Vector2 fromSourceUv, Vector2 toSourceUv, int width, int height,
            float hardness, float strength, float flow, Texture selection = null)
        {
            if (smudgeCarry == null) return;
            Vector2 from = smudgeToCanvas.Point(fromSourceUv), to = smudgeToCanvas.Point(toSourceUv);
            Vector2 dimensions = new Vector2(width, height);
            float distance = Vector2.Scale(to - from, dimensions).magnitude;
            if (!float.IsFinite(distance) || distance <= 0f) return;
            double spacing = Math.Max(1d, smudgeDiameter * .08d);
            int count = smudgeSpacing.Sample(distance, spacing, false, out double first);
            if (count > 32768) throw new InvalidOperationException("Smudge stroke segment is too long.");
            if (count == 0 || strength <= 0f || flow <= 0f) return;
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
                material.SetFloat("_Flow", Mathf.Clamp01(flow));
                for (int i = 0; i < count; i++)
                {
                    Vector2 center = Vector2.Lerp(from, to, (float)((first + i * spacing) / distance));
                    if (smudgeWrap) center = TiledCanvasUtility.Wrap(center);
                    PickupSmudge(material, surface, center, smudgePickup);
                    material.SetTexture("_Carry", smudgeCarry);
                    Graphics.Blit(smudgePickup, smudgeNext, material, 1);
                    (smudgeCarry, smudgeNext) = (smudgeNext, smudgeCarry);
                    material.SetTexture("_Carry", smudgeCarry);
                    DepositSmudge(material, surface, smudgeToCanvas, smudgeToSource, center, width, height);
                    if (smudgeSample != null)
                        DepositSmudge(material, smudgeSample, ProjectiveMatrix.Identity, ProjectiveMatrix.Identity, center, width, height);
                }
                paintSurfaceDirty = true;
                smudgeStrokeChanged = true;
                unchecked { paintSurfaceRevision++; }
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousSrgb; }
        }

        private static RenderTexture SmudgeTemporary(int width, int height)
        {
            var result = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
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

        private void PickupSmudge(Material material, RenderTexture surface, Vector2 center, RenderTexture target)
        {
            if (smudgeWrap) center = TiledCanvasUtility.Wrap(center);
            material.SetVector("_Center", center);
            (smudgeSample != null ? ProjectiveMatrix.Identity : smudgeToSource).SetShader(material, "_PickupRow");
            Graphics.Blit(smudgeSample != null ? smudgeSample : surface, target, material, 0);
        }

        private void DepositSmudge(Material material, RenderTexture target, ProjectiveMatrix toCanvas,
            ProjectiveMatrix toSource, Vector2 center, int width, int height)
        {
            RenderTexture backdrop = target == smudgeSample && smudgeSampleBackdrop != null ? smudgeSampleBackdrop : smudgeBackdrop;
            {
                toCanvas.SetShader(material, "_DepositRow");
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
                    RenderTexture.active = target;
                    GL.PushMatrix();
                    try
                    {
                        GL.LoadOrtho();
                        if (!material.SetPass(2)) throw new InvalidOperationException("Cannot render Smudge Brush.");
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
            Release(ref smudgeCarry); Release(ref smudgePickup); Release(ref smudgeNext);
            Release(ref smudgeBackdrop); Release(ref smudgeSample); Release(ref smudgeSampleBackdrop);
            smudgeSpacing = default;
            smudgeStrokeChanged = false;
            static void Release(ref RenderTexture texture)
            {
                if (texture != null) RenderTexture.ReleaseTemporary(texture);
                texture = null;
            }
        }
    }
}
