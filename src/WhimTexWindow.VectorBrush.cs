using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private sealed partial class VectorManipulator
        {
            VectorBrushBuilder brushBuilder;
            Texture2D brushPreview;
            Color32[] brushPixels;
            float brushRadius;
            Color brushColor;

            bool BeginBrush(Vector2 point)
            {
                CancelBrush();
                float scale = Mathf.Min(1, VectorBrushDraft.MaxDimension / Mathf.Max(behaviour.referenceSize.x, behaviour.referenceSize.y));
                int width = Mathf.Max(1, Mathf.RoundToInt(behaviour.referenceSize.x * scale));
                int height = Mathf.Max(1, Mathf.RoundToInt(behaviour.referenceSize.y * scale));
                brushBuilder = new VectorBrushBuilder(); brushBuilder.Begin(width, height);
                brushRadius = Mathf.Clamp(owner.vectorBrushSize * scale * .5f, .5f, 1024);
                brushColor = WhimTexColorInputs.DisplayColor(owner.vectorFillColor);
                brushPreview = new Texture2D(width, height, TextureFormat.RGBA32, false)
                { name = "Vector stroke preview", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                brushPixels = new Color32[width * height];
                UpdateBrush(point);
                return true;
            }

            void UpdateBrush(Vector2 point)
            {
                if (brushBuilder == null) return;
                if (!brushBuilder.AddPoint(new Vector2(point.x * brushBuilder.Width, point.y * brushBuilder.Height), brushRadius)) return;
                RectInt dirty = brushBuilder.ConsumeDirtyRect();
                for (int y = dirty.yMin; y < dirty.yMax; y++) for (int x = dirty.xMin; x < dirty.xMax; x++)
                {
                    int i = y * brushBuilder.Width + x;
                    brushPixels[i] = new Color32(255, 255, 255, brushBuilder.Coverage[i]);
                }
                brushPreview.SetPixels32(brushPixels); brushPreview.Apply(false, false);
                overlay.MarkDirtyRepaint();
            }

            void FinishBrush()
            {
                if (brushBuilder == null) return;
                try
                {
                    using var measure = WhimTexProfiler.Measure("vector.brush_trace", document, layer);
                    var draft = brushBuilder.Finish();
                    draft.tolerance = Mathf.Clamp(owner.vectorBrushTolerance, VectorBrushDraft.MinTolerance, VectorBrushDraft.MaxTolerance);
                    VectorShape shape = VectorBrushGeometry.Rebuild(draft, behaviour.referenceSize);
                    if (shape.contours.Count > 0)
                    {
                        shape.fillColor = brushColor;
                        owner.CommitVectorShape(layer, shape, "Create Vector Brush Figure", draft: draft);
                    }
                    if (brushBuilder.IsFull) owner.ShowNotification(new GUIContent("Stroke sample limit reached; start another stroke to continue."));
                }
                catch (Exception error) { owner.ShowNotification(new GUIContent(error.Message)); }
                finally { CancelBrush(); }
            }

            void CancelBrush()
            {
                brushBuilder = null; brushPixels = null;
                if (brushPreview != null) UnityEngine.Object.DestroyImmediate(brushPreview);
                brushPreview = null;
            }

            void DrawBrush(MeshGenerationContext context)
            {
                if (brushPreview == null) return;
                var mesh = context.Allocate(4, 6, brushPreview);
                Vector2[] uv = { new(0, 1), new(1, 1), new(1, 0), new(0, 0) };
                foreach (Vector2 p in uv)
                {
                    Vector2 view = View(p);
                    mesh.SetNextVertex(new Vertex { position = new Vector3(view.x, view.y, Vertex.nearZ), tint = brushColor, uv = p });
                }
                mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
                mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
            }
        }
    }
}
