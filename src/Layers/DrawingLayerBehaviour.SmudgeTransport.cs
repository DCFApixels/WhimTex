using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        [NonSerialized] private SmudgeTransport smudgeTransport;
        [NonSerialized] private Vector2 smudgeTransportLastCenter;
        [NonSerialized] private float smudgeMixing;

        // Coordinates and positive color contributions are separate. Deformation
        // composes coordinates, never repeatedly resamples the original RGB image.
        private sealed class SmudgeTransport : IDisposable
        {
            private RenderTexture snapshot, coordinates, paint;
            private RectInt bounds;
            private readonly bool mixing;
            internal readonly RenderTexture Image;
            private readonly bool ownsImage;
            private readonly int width, height;
            private readonly Material material;

            internal SmudgeTransport(RenderTexture input, bool mixed, bool borrowImage)
            {
                mixing = mixed; width = input.width; height = input.height;
                material = WhimTexMaterials.SmudgeTransport;
                if ((long)width * height > SmudgeMaximumCarryPixels)
                    throw new InvalidOperationException("Smudge deformation snapshot exceeds the native-pixel buffer budget. Use a smaller source or Mixing 100%.");
                if (material == null || !material.shader.isSupported || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
                    throw new InvalidOperationException("Smudge deformation requires floating-point render textures.");
                ownsImage = !borrowImage && mixed;
                try
                {
                    snapshot = RenderTexture.GetTemporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBHalf, 0)
                    { sRGB = false, useMipMap = true, autoGenerateMips = false });
                    snapshot.filterMode = FilterMode.Trilinear; snapshot.wrapMode = TextureWrapMode.Clamp; snapshot.anisoLevel = 8;
                    Graphics.Blit(input, snapshot); snapshot.GenerateMips();
                    Image = ownsImage ? SmudgeTemporary(width, height, RenderTextureFormat.ARGBFloat) : input;
                    if (ownsImage) Graphics.Blit(input, Image);
                }
                catch { Dispose(); throw; }
            }

            internal void Configure(ProjectiveMatrix toCanvas, ProjectiveMatrix toSource, ProjectiveMatrix selectionTransform,
                Texture selection, Vector2 center, Vector2 delta, float diameter, int canvasWidth, int canvasHeight,
                float hardness, float strength, float flow, float mix, bool wrap)
            {
                toCanvas.SetShader(material, "_CanvasRow"); toSource.SetShader(material, "_SourceRow");
                selectionTransform.SetShader(material, "_SelectionRow");
                material.SetTexture("_Selection", selection != null ? selection : Texture2D.whiteTexture);
                material.SetVector("_Dimensions", new Vector4(width, height, 0, 0));
                material.SetVector("_TipSize", new Vector4(diameter / canvasWidth, diameter / canvasHeight, 0, 0));
                material.SetVector("_Center", center); material.SetVector("_Delta", delta);
                material.SetFloat("_Hardness", Mathf.Clamp01(hardness)); material.SetFloat("_Strength", Mathf.Clamp01(strength));
                material.SetFloat("_Flow", Mathf.Clamp01(flow)); material.SetFloat("_Mixing", mix);
                material.SetFloat("_Wrap", wrap ? 1 : 0); material.SetFloat("_FrozenCarry", strength >= 1 ? 1 : 0);
            }

            internal void Warp(RectInt region)
            {
                EnsureBounds(region);
                Apply(region, 1, mixing ? 2 : -1);
                Reconstruct(region);
            }

            internal void Deposit(RectInt region, Material smudge, RenderTexture carry)
            {
                material.SetTexture("_Carry", carry);
                foreach (string name in GridUniforms) material.SetVector(name, smudge.GetVector(name));
                Apply(region, 4, 5);
                Reconstruct(region);
            }

            private static readonly string[] GridUniforms = { "_ColorOrigin", "_ColorSize", "_ColorDimensions", "_ColorPhase" };
            private Vector4 NativeUv(RectInt rect) => new Vector4(rect.x / (float)width, rect.y / (float)height, rect.width / (float)width, rect.height / (float)height);

            private void Bind()
            {
                material.SetTexture("_Original", snapshot); material.SetTexture("_Map", coordinates);
                material.SetTexture("_Paint", paint != null ? paint : Texture2D.blackTexture);
                material.SetVector("_GridRect", new Vector4(bounds.x, bounds.y, bounds.width, bounds.height));
            }

            private void Reconstruct(RectInt region)
            {
                Bind(); DrawSmudgeRegion(material, 3, Image, region, NativeUv(region));
            }

            private void Apply(RectInt region, int mapPass, int paintPass)
            {
                RenderTexture nextMap = null, nextPaint = null;
                try
                {
                    nextMap = SmudgeTemporary(region.width, region.height, RenderTextureFormat.ARGBFloat);
                    Bind(); DrawSmudgeRegion(material, mapPass, nextMap, new RectInt(0, 0, region.width, region.height), NativeUv(region));
                    if (paintPass >= 0)
                    {
                        nextPaint = SmudgeTemporary(region.width, region.height, RenderTextureFormat.ARGBFloat);
                        DrawSmudgeRegion(material, paintPass, nextPaint, new RectInt(0, 0, region.width, region.height), NativeUv(region));
                    }
                    CopyRegion(nextMap, new RectInt(0, 0, region.width, region.height), coordinates, region.x - bounds.x, region.y - bounds.y);
                    if (nextPaint != null) CopyRegion(nextPaint, new RectInt(0, 0, region.width, region.height), paint, region.x - bounds.x, region.y - bounds.y);
                }
                finally { ReleaseSmudgeTexture(ref nextMap); ReleaseSmudgeTexture(ref nextPaint); }
            }

            private void EnsureBounds(RectInt region)
            {
                if (coordinates != null && bounds.xMin <= region.xMin && bounds.yMin <= region.yMin && bounds.xMax >= region.xMax && bounds.yMax >= region.yMax) return;
                int left = Math.Min(region.xMin, coordinates != null ? bounds.xMin : region.xMin);
                int bottom = Math.Min(region.yMin, coordinates != null ? bounds.yMin : region.yMin);
                int right = Math.Max(region.xMax, coordinates != null ? bounds.xMax : region.xMax);
                int top = Math.Max(region.yMax, coordinates != null ? bounds.yMax : region.yMax);
                // Tile-aligned growth amortizes allocations while retaining native resolution.
                var enlarged = new RectInt(Math.Max(0, left / 128 * 128), Math.Max(0, bottom / 128 * 128), 0, 0);
                enlarged.width = Math.Min(width, (right + 127) / 128 * 128) - enlarged.x;
                enlarged.height = Math.Min(height, (top + 127) / 128 * 128) - enlarged.y;
                if ((long)enlarged.width * enlarged.height > SmudgeMaximumCarryPixels)
                    throw new InvalidOperationException("Smudge stroke history exceeds the native-pixel buffer budget. Finish this stroke before continuing.");
                RenderTexture map = null, color = null;
                try
                {
                    map = SmudgeTemporary(enlarged.width, enlarged.height, RenderTextureFormat.ARGBFloat);
                    DrawSmudgeRegion(material, 0, map, new RectInt(0, 0, map.width, map.height), NativeUv(enlarged));
                    if (mixing)
                    {
                        color = SmudgeTemporary(enlarged.width, enlarged.height, RenderTextureFormat.ARGBFloat);
                        RenderTexture.active = color; GL.Clear(false, true, Color.clear);
                    }
                    if (coordinates != null)
                    {
                        var all = new RectInt(0, 0, bounds.width, bounds.height);
                        CopyRegion(coordinates, all, map, bounds.x - enlarged.x, bounds.y - enlarged.y);
                        if (paint != null) CopyRegion(paint, all, color, bounds.x - enlarged.x, bounds.y - enlarged.y);
                    }
                    ReleaseSmudgeTexture(ref coordinates); ReleaseSmudgeTexture(ref paint);
                    coordinates = map; paint = color; bounds = enlarged;
                }
                catch { ReleaseSmudgeTexture(ref map); ReleaseSmudgeTexture(ref color); throw; }
            }

            internal void CopyRegion(RenderTexture source, RectInt region, RenderTexture target, int x, int y)
            {
                if (source == target && region.x == x && region.y == y) return;
                RenderTexture.active = null;
                if (source.format == target.format && (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0)
                    Graphics.CopyTexture(source, 0, 0, region.x, region.y, region.width, region.height, target, 0, 0, x, y);
                else
                {
                    material.SetTexture("_MainTex", source);
                    DrawSmudgeRegion(material, 6, target, new RectInt(x, y, region.width, region.height),
                        new Vector4(region.x / (float)source.width, region.y / (float)source.height, region.width / (float)source.width, region.height / (float)source.height));
                }
            }

            public void Dispose()
            {
                if (material != null)
                    foreach (string name in new[] { "_Original", "_Map", "_Paint", "_Carry", "_Backdrop", "_MainTex", "_Selection" }) material.SetTexture(name, null);
                ReleaseSmudgeTexture(ref snapshot); ReleaseSmudgeTexture(ref coordinates); ReleaseSmudgeTexture(ref paint);
                if (ownsImage && Image != null) RenderTexture.ReleaseTemporary(Image);
            }
        }

        private static void DrawSmudgeRegion(Material material, int pass, RenderTexture target, RectInt rect, Vector4 uv)
        {
            RenderTexture.active = target;
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                if (!material.SetPass(pass)) throw new InvalidOperationException("Cannot render Smudge deformation.");
                float l = rect.xMin / (float)target.width, r = rect.xMax / (float)target.width;
                float b = rect.yMin / (float)target.height, t = rect.yMax / (float)target.height;
                GL.Begin(GL.QUADS);
                GL.TexCoord2(uv.x, uv.y); GL.Vertex3(l, b, 0);
                GL.TexCoord2(uv.x, uv.y + uv.w); GL.Vertex3(l, t, 0);
                GL.TexCoord2(uv.x + uv.z, uv.y + uv.w); GL.Vertex3(r, t, 0);
                GL.TexCoord2(uv.x + uv.z, uv.y); GL.Vertex3(r, b, 0);
                GL.End();
            }
            finally { GL.PopMatrix(); }
        }

        private RectInt SmudgeTransportRegion(ProjectiveMatrix toSource, RenderTexture image, Vector2 center, int width, int height)
        {
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            int copies = smudgeWrap ? 1 : 0;
            for (int y = -copies; y <= copies; y++) for (int x = -copies; x <= copies; x++)
            {
                var dab = center + new Vector2(x, y);
                var radius = new Vector2(smudgeDiameter / width, smudgeDiameter / height) * .5f;
                if (smudgeWrap && (dab.x + radius.x < 0 || dab.x - radius.x > 1 || dab.y + radius.y < 0 || dab.y - radius.y > 1)) continue;
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector2 canvas = center + new Vector2(x + ((corner & 1) == 0 ? -.5f : .5f) * smudgeDiameter / width,
                        y + ((corner & 2) == 0 ? -.5f : .5f) * smudgeDiameter / height);
                    Vector2 p = toSource.Point(canvas);
                    if (!float.IsFinite(p.x) || !float.IsFinite(p.y)) return new RectInt(0, 0, image.width, image.height);
                    min = Vector2.Min(min, p); max = Vector2.Max(max, p);
                }
            }
            int left = Mathf.Clamp(Mathf.FloorToInt(min.x * image.width) - 2, 0, image.width);
            int bottom = Mathf.Clamp(Mathf.FloorToInt(min.y * image.height) - 2, 0, image.height);
            int right = Mathf.Clamp(Mathf.CeilToInt(max.x * image.width) + 2, 0, image.width);
            int top = Mathf.Clamp(Mathf.CeilToInt(max.y * image.height) + 2, 0, image.height);
            return new RectInt(left, bottom, Math.Max(0, right - left), Math.Max(0, top - bottom));
        }

        private void TransportSmudgeDab(Material smudge, RenderTexture surface, Vector2 unwrappedCenter, int width, int height,
            float hardness, float strength, float flow, Texture selection)
        {
            Vector2 delta = unwrappedCenter - smudgeTransportLastCenter;
            smudgeTransportLastCenter = unwrappedCenter;
            Vector2 center = smudgeWrap ? TiledCanvasUtility.Wrap(unwrappedCenter) : unwrappedCenter;
            var toCanvas = smudgeSample != null ? ProjectiveMatrix.Identity : smudgeToCanvas;
            var toSource = smudgeSample != null ? ProjectiveMatrix.Identity : smudgeToSource;
            RenderTexture image = smudgeTransport.Image;
            RectInt region = SmudgeTransportRegion(toSource, image, center, width, height);
            if (region.width <= 0 || region.height <= 0) return;
            if (smudgeMixing > 0) EnsureSmudgeCarry(smudge, surface, center, width, height);
            else SmudgeCarrySize(toSource, image.width, image.height, center, smudgeDiameter, width, height);
            smudgeTransport.Configure(toCanvas, toSource, smudgeToSource, selection, center, delta, smudgeDiameter,
                width, height, hardness, strength, flow, smudgeMixing, smudgeWrap);
            smudgeTransport.Warp(region);
            if (smudgeMixing > 0)
            {
                SetSmudgeGrid(smudge, image, center, smudgeCarry.width, smudgeCarry.height);
                smudgeTransport.Deposit(region, smudge, smudgeCarry);
            }
            if (smudgeSample == null)
                smudgeTransport.CopyRegion(image, region, surface, region.x, region.y);
            else
            {
                RectInt targetRegion = SmudgeTransportRegion(smudgeToSource, surface, center, width, height);
                if (targetRegion.width > 0 && targetRegion.height > 0)
                {
                    smudgeTransport.CopyRegion(surface, targetRegion, smudgeBackdrop, targetRegion.x, targetRegion.y);
                    var material = WhimTexMaterials.SmudgeTransport;
                    material.SetTexture("_MainTex", image); material.SetTexture("_Backdrop", smudgeBackdrop);
                    smudgeToCanvas.SetShader(material, "_CanvasRow");
                    DrawSmudgeRegion(material, 7, surface, targetRegion, new Vector4(targetRegion.x / (float)surface.width,
                        targetRegion.y / (float)surface.height, targetRegion.width / (float)surface.width, targetRegion.height / (float)surface.height));
                }
            }
            if (smudgeMixing > 0 && strength < 1)
            {
                PickupSmudge(smudge, surface, center, smudgeNext, Mathf.Clamp01(strength));
                (smudgeCarry, smudgeNext) = (smudgeNext, smudgeCarry);
            }
            smudgeLastCenter = center;
            paintSurfaceDirty = true; smudgeStrokeChanged = true;
            unchecked { paintSurfaceRevision++; }
        }
    }
}
