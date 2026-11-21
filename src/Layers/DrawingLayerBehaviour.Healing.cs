using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        internal sealed class HealingStrokeBuffer : IDisposable
        {
            private RenderTexture mask;
            internal RenderTexture PreviewTexture => mask != null && mask.IsCreated() ? mask : null;
            internal RenderTexture Texture => PreviewTexture ?? RestoreTexture();
            internal readonly bool tiled;
            private readonly bool[] columns, rows;
            private readonly List<PaintStamp> stamps = new List<PaintStamp>();
            private readonly float size, hardness;
            private readonly ProjectiveMatrix inverse;
            private Texture selection;
            internal Func<Texture> RestoreSelection { private get; set; }
            private List<Vector2> points = new List<Vector2>();

            internal HealingStrokeBuffer(int width, int height, float size, float hardness, bool tiled,
                ProjectiveMatrix inverse, Texture selection)
            {
                this.size = size; this.hardness = hardness; this.tiled = tiled;
                this.inverse = inverse; this.selection = selection;
                columns = new bool[width]; rows = new bool[height];
                try { RestoreTexture(); }
                catch { Dispose(); throw; }
            }

            private RenderTexture RestoreTexture()
            {
                if (points == null) return null;
                ValidateSelection();
                if (mask == null)
                    mask = new RenderTexture(columns.Length, rows.Length, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    {
                        name = "Healing stroke mask", hideFlags = HideFlags.HideAndDontSave,
                        wrapMode = tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear
                    };
                if (!mask.Create()) throw new InvalidOperationException("Cannot allocate the healing stroke buffer.");
                try
                {
                    Clear(mask);
                    for (int i = 0; i < points.Count; i++)
                        DrawSegment(mask, points[Math.Max(0, i - 1)], points[i]);
                    return mask;
                }
                catch { mask.Release(); throw; }
            }

            private void ValidateSelection()
            {
                static bool Available(Texture candidate) => candidate != null && (!(candidate is RenderTexture rt) || rt.IsCreated());
                if (ReferenceEquals(selection, null) || Available(selection)) return;
                var restored = RestoreSelection?.Invoke();
                if (!Available(restored))
                    throw new InvalidOperationException("The selection mask was lost. Repeat the healing stroke.");
                selection = restored;
            }

            internal void Add(Vector2 point)
            {
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y)) return;
                ValidateSelection();
                Vector2 from = points.Count == 0 ? point : points[points.Count - 1];
                DrawSegment(Texture, from, point);
                points.Add(point);
                MarkAxis(columns, Mathf.Min(from.x, point.x) - size * .5f, Mathf.Max(from.x, point.x) + size * .5f);
                MarkAxis(rows, Mathf.Min(from.y, point.y) - size * .5f, Mathf.Max(from.y, point.y) + size * .5f);
            }

            private void DrawSegment(RenderTexture target, Vector2 from, Vector2 point)
            {
                float distance = Vector2.Distance(from, point);
                int count = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(.5f, size * .05f)));
                if (count > 32768) throw new InvalidOperationException("Stroke segment too long. Zoom in or use a shorter stroke.");
                stamps.Clear();
                for (int i = 0; i <= count; i++)
                {
                    Vector2 center = Vector2.Lerp(from, point, i / (float)count) / new Vector2(columns.Length, rows.Length);
                    if (tiled) center = TiledCanvasUtility.Wrap(center);
                    stamps.Add(new PaintStamp { center = center, size = size, color = Color.white });
                }
                PaintBrushRenderer.Draw(target, stamps, size, hardness, false, PencilShape.Circle, Color.white,
                    false, columns.Length, rows.Length, Vector2.zero, tiled, TextureTransform.Default,
                    false, false, selection, maskCanvasToSource: inverse);
            }

            private void MarkAxis(bool[] axis, float min, float max)
            {
                int start = Mathf.FloorToInt(min), end = Mathf.CeilToInt(max);
                if (tiled && end - (long)start >= axis.Length) { Array.Fill(axis, true); return; }
                if (!tiled) { start = Mathf.Max(0, start); end = Mathf.Min(axis.Length, end); }
                for (int i = start; i < end; i++) axis[(i % axis.Length + axis.Length) % axis.Length] = true;
            }

            internal RectInt Bounds(int margin)
            {
                Vector2Int x = AxisBounds(columns, margin), y = AxisBounds(rows, margin);
                var result = new RectInt(x.x, y.x, x.y, y.y);
                HealingBrushUtility.CheckWorkingSize(result);
                return result;
            }

            private Vector2Int AxisBounds(bool[] axis, int margin)
            {
                int first = Array.IndexOf(axis, true);
                if (first < 0) return Vector2Int.zero;
                if (!tiled)
                {
                    int left = Mathf.Max(0, first - margin), right = Mathf.Min(axis.Length, Array.LastIndexOf(axis, true) + 1 + margin);
                    return new Vector2Int(left, right - left);
                }
                // Cut the periodic domain inside the largest untouched gap, not at the texture edge.
                int gap = 0, longest = 0, end = first;
                for (int step = 1; step <= axis.Length; step++)
                {
                    int i = (first + step) % axis.Length;
                    if (axis[i]) gap = 0;
                    else if (++gap > longest) { longest = gap; end = i + 1; }
                }
                int length = Mathf.Min(axis.Length, axis.Length - longest + 2 * margin);
                int padding = (length - (axis.Length - longest)) / 2;
                return new Vector2Int((end - padding + axis.Length) % axis.Length, length);
            }

            public void Dispose()
            {
                points = null;
                RestoreSelection = null;
                if (mask == null) return;
                mask.Release(); UnityEngine.Object.DestroyImmediate(mask); mask = null;
            }
        }

        internal void ApplyHealingPatch(Texture2D patch, Texture2D coverage, RectInt region,
            int canvasWidth, int canvasHeight, ProjectiveMatrix sourceToCanvas, bool tiled = false)
        {
            if (StrokeWritesNothing) return;
            Material material = WhimTexMaterials.HealingBrush;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Healing Brush shader is unavailable.");
            RenderTexture surface = EnsurePaintSurface(canvasWidth, canvasHeight);
            if (!tiled && sourceToCanvas.TryInverse(out var inverse))
            {
                Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
                double minW = double.PositiveInfinity, maxW = double.NegativeInfinity;
                for (int i = 0; i < 4; i++)
                {
                    var point = new Vector2(((i & 1) == 0 ? region.xMin : region.xMax) / (float)canvasWidth,
                        ((i & 2) == 0 ? region.yMin : region.yMax) / (float)canvasHeight);
                    double w = inverse.m20 * point.x + inverse.m21 * point.y + inverse.m22;
                    minW = Math.Min(minW, w); maxW = Math.Max(maxW, w);
                    Vector2 source = inverse.Point(point);
                    min = Vector2.Min(min, source); max = Vector2.Max(max, source);
                }
                if (minW <= 0 && maxW >= 0 || !float.IsFinite(min.x + min.y + max.x + max.y))
                    CapturePixelEdit(new RectInt(0, 0, surface.width, surface.height));
                else CapturePixelEditUv(Rect.MinMaxRect(min.x, min.y, max.x, max.y));
            }
            else CapturePixelEdit(new RectInt(0, 0, surface.width, surface.height));
            RenderTexture result = ProfiledRenderTexture.GetTemporary(surface.descriptor);
            RenderTexture previous = RenderTexture.active;
            try
            {
                material.SetTexture("_Patch", patch);
                material.SetTexture("_Coverage", coverage);
                material.SetVector("_PatchRect", new Vector4(region.x, region.y, region.width, region.height));
                material.SetVector("_CanvasSize", new Vector4(canvasWidth, canvasHeight, 0, 0));
                material.SetFloat("_WrapPatch", tiled ? 1 : 0);
                sourceToCanvas.SetShader(material, "_SourceToCanvas");
                Graphics.Blit(surface, result, material);
                ApplyPixelEditOutput(result);
                ApplyPaintWriteProtection();
                paintSurfaceDirty = true;
                InvalidateAlphaCoverage();
                unchecked { paintSurfaceRevision++; }
                FinishStrokeStorage();
            }
            finally
            {
                material.SetTexture("_Patch", null);
                material.SetTexture("_Coverage", null);
                RenderTexture.active = previous;
                ProfiledRenderTexture.ReleaseTemporary(result);
            }
        }
    }
}
