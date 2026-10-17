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
            VectorBrushDraft brushSource;
            VectorShape brushOriginal;
            string brushOriginalJson;
            bool brushErasing;
            bool brushFinishAfterRebuild;
            Rect brushPreviewFrame;
            VectorBrushShape strokeBrushShape;
            Vector2 brushLastPoint, brushRawView, brushAxisAnchor, brushAxisPointerAnchor;
            bool brushShift;
            int brushAxis;
            WhimTexDocument brushLineDocument;
            Layer brushLineLayer;
            Vector2 brushLineUv, brushLineDimensions;
            ProjectiveMatrix brushLineTransform;
            bool brushLineErase;

            internal VectorBrushShape CursorBrushShape => brushBuilder != null ? strokeBrushShape : owner.vectorBrushShape;
            internal bool IsBrushErasing => brushBuilder != null && brushErasing;

            bool ValidBrushSource => brushOriginal == null || behaviour.brushDraft == brushSource &&
                behaviour.shapes.Contains(brushOriginal) && JsonUtility.ToJson(brushOriginal) == brushOriginalJson;

            internal void ResetBrushLine() { brushLineDocument = null; brushLineLayer = null; }

            internal void FinishBrushEditing()
            {
                if (owner.IsVectorMaskRebuilding) { brushFinishAfterRebuild = true; return; }
                if (brushBuilder == null) return;
                if (Valid && ValidBrushSource) { brushFinishAfterRebuild = true; ReleasePointer(); FinishBrush(); }
                else Cancel();
            }

            bool BeginBrush(Vector2 point, bool shift, Vector2 view, bool erase)
            {
                CancelBrush();
                try
                {
                    brushErasing = erase;
                    brushOriginal = owner.IsEditingVectorMask(layer) ? owner.SelectedVectorShape(behaviour) : null;
                    brushOriginalJson = brushOriginal == null ? null : JsonUtility.ToJson(brushOriginal);
                    brushSource = brushOriginal == null ? null : behaviour.brushDraft;
                    float scale = Mathf.Min(1, VectorBrushDraft.MaxDimension / Mathf.Max(behaviour.referenceSize.x, behaviour.referenceSize.y));
                    int width = Mathf.Max(1, Mathf.RoundToInt(behaviour.referenceSize.x * scale));
                    int height = Mathf.Max(1, Mathf.RoundToInt(behaviour.referenceSize.y * scale));
                    strokeBrushShape = owner.vectorBrushShape;
                    brushBuilder = new VectorBrushBuilder();
                    if (brushSource != null) brushBuilder.Begin(brushSource, strokeBrushShape, erase);
                    else brushBuilder.BeginEmpty(width, height, strokeBrushShape,
                        Mathf.Clamp(owner.vectorBrushTolerance, VectorBrushDraft.MinTolerance, VectorBrushDraft.MaxTolerance), erase);
                    brushBuilder.SetCanvasClip(preparedTransform);
                    RefreshBrushSize();
                    brushColor = erase ? new Color(1, .25f, .2f, .65f) : brushOriginal?.fillColor ?? WhimTexColorInputs.DisplayColor(owner.vectorFillColor);
                    bool connect = shift && brushLineDocument == document && insertionAnchor == brushLineLayer && brushLineErase == erase &&
                        ReferenceEquals(document.FindLayer(brushLineLayer.Id), brushLineLayer) && brushLineDimensions == dimensions &&
                        brushLineLayer.RenderTransform.ToMatrix(document.width, document.height).Equals(brushLineTransform);
                    if (connect) UpdateBrush(layer.RenderTransform.Unmap(brushLineUv, dimensions));
                    UpdateBrush(point);
                    if (brushBuilder == null) return false;
                    brushRawView = view;
                    SetBrushShift(shift);
                    return true;
                }
                catch (Exception error) { owner.ShowNotification(new GUIContent(error.Message)); CancelBrush(); return false; }
            }

            internal bool SetBrushShift(bool shift)
            {
                if (brushBuilder == null || owner.IsVectorMaskRebuilding) return false;
                if (brushShift != shift)
                {
                    brushShift = shift; brushAxis = 0;
                    brushAxisAnchor = brushLastPoint;
                    brushAxisPointerAnchor = brushRawView;
                }
                return true;
            }

            Vector2 BrushPosition(Vector2 view, bool shift, bool control)
            {
                SetBrushShift(shift);
                if (shift && brushAxis == 0)
                {
                    Vector2 delta = view - brushAxisPointerAnchor;
                    if (delta.sqrMagnitude >= 4f) brushAxis = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? 1 : 2;
                }
                brushRawView = view;
                // Do not let point snapping bend the screen-axis constraint.
                return shift ? Local(ConstrainBrushView(view), true) : Local(view, control);
            }

            Vector2 ConstrainBrushView(Vector2 view)
            {
                Vector2 anchor = View(brushAxisAnchor);
                return brushAxis == 1 ? new Vector2(view.x, anchor.y) :
                    brushAxis == 2 ? new Vector2(anchor.x, view.y) : anchor;
            }

            internal Vector2 BrushCursorPosition(Vector2 view) => brushBuilder != null && !owner.IsVectorMaskRebuilding ? View(brushLastPoint) : view;

            internal void RefreshBrushSize()
            {
                if (brushBuilder == null) return;
                float scale = brushBuilder.Width / (brushBuilder.Frame.width * behaviour.referenceSize.x);
                brushRadius = Mathf.Clamp(owner.vectorBrushSize * scale * .5f, .5f, 1024);
            }

            void UpdateBrush(Vector2 point)
            {
                if (brushBuilder == null) return;
                if (brushBuilder.IsFull) return;
                brushLastPoint = point;
                try
                {
                    if (!brushBuilder.AddPoint(brushBuilder.ToPixel(point), brushRadius)) return;
                    bool resized = brushPreview == null || brushPreview.width != brushBuilder.Width || brushPreview.height != brushBuilder.Height || brushPreviewFrame != brushBuilder.Frame;
                    if (resized)
                    {
                        if (brushPreview != null) UnityEngine.Object.DestroyImmediate(brushPreview);
                        brushPreview = new Texture2D(brushBuilder.Width, brushBuilder.Height, TextureFormat.RGBA32, false)
                        { name = "Vector stroke preview", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                        brushPixels = new Color32[brushBuilder.Width * brushBuilder.Height];
                    }
                    brushPreviewFrame = brushBuilder.Frame;
                    RectInt dirty = brushBuilder.ConsumeDirtyRect();
                    if (resized) dirty = new RectInt(0, 0, brushBuilder.Width, brushBuilder.Height);
                    for (int y = dirty.yMin; y < dirty.yMax; y++) for (int x = dirty.xMin; x < dirty.xMax; x++)
                    {
                        int i = y * brushBuilder.Width + x, old = 0;
                        if (brushSource != null)
                        {
                            Vector2 local = brushPreviewFrame.min + Vector2.Scale(new Vector2((x + .5f) / brushBuilder.Width, (y + .5f) / brushBuilder.Height), brushPreviewFrame.size);
                            int sx = Mathf.FloorToInt((local.x - brushSource.frame.xMin) * brushSource.width / brushSource.frame.width);
                            int sy = Mathf.FloorToInt((local.y - brushSource.frame.yMin) * brushSource.height / brushSource.frame.height);
                            if (sx >= 0 && sx < brushSource.width && sy >= 0 && sy < brushSource.height) old = brushSource.coverage[sy * brushSource.width + sx];
                        }
                        int change = brushErasing ? old - brushBuilder.Coverage[i] : brushBuilder.Coverage[i] - old;
                        brushPixels[i] = new Color32(255, 255, 255, (byte)Mathf.Max(0, change));
                    }
                    brushPreview.SetPixels32(brushPixels); brushPreview.Apply(false, false);
                    overlay.MarkDirtyRepaint();
                }
                catch (Exception error) { owner.ShowNotification(new GUIContent(error.Message)); Cancel(); }
            }

            void FinishBrush()
            {
                if (brushBuilder == null || owner.IsVectorMaskRebuilding) return;
                WhimTexHangDiagnostics.Start("stroke_finish", layer?.Id);
                using var diagnostic = WhimTexHangDiagnostics.Enter("vector.brush.finish");
                bool pending = false;
                try
                {
                    if (!brushBuilder.HasChanges || !ValidBrushSource) return;
                    var draft = brushBuilder.Finish();
                    var referenceSize = behaviour.referenceSize;
                    var sourceFrame = brushSource?.frame ?? default;
                    float sourceTolerance = brushSource?.tolerance ?? 0, sourceExpansion = brushSource?.expansion ?? 0;
                    int sourceWidth = brushSource?.width ?? 0, sourceHeight = brushSource?.height ?? 0;
                    var sourceCoverage = brushSource?.coverage;
                    Vector2 endUv = layer.RenderTransform.Map(brushLastPoint, dimensions);
                    bool erasing = brushErasing, full = brushBuilder.IsFull;
                    owner.BeginVectorMaskRebuild(draft, referenceSize, layer, "vector.brush_trace",
                        () => Valid && ValidBrushSource && behaviour.referenceSize == referenceSize &&
                            (brushSource == null || brushSource.frame == sourceFrame && brushSource.tolerance == sourceTolerance &&
                                brushSource.expansion == sourceExpansion && brushSource.width == sourceWidth && brushSource.height == sourceHeight &&
                                ReferenceEquals(brushSource.coverage, sourceCoverage)),
                        shape =>
                    {
                        if (shape.contours.Count == 0 && brushOriginal == null) return;
                        if (brushOriginal != null)
                        {
                            var geometry = shape.contours;
                            shape = CopyVectorShape(brushOriginal); shape.contours = geometry;
                            shape.fillRule = VectorFillRule.EvenOdd;
                        }
                        else shape.fillColor = brushColor;
                        var editedLayer = layer; var editedDocument = document; var editedBehaviour = behaviour;
                        bool finishEditing = brushFinishAfterRebuild;
                        owner.CommitVectorShape(editedLayer, shape, brushOriginal == null ? "Create Vector Brush Figure" : erasing ? "Erase Vector Mask" : "Paint Vector Mask", draft: draft);
                        if (ReferenceEquals(editedDocument.FindLayer(editedLayer.Id), editedLayer) && editedBehaviour.brushDraft == draft)
                        {
                            owner.vectorBrushEditingDocument = finishEditing ? null : editedDocument;
                            owner.vectorBrushEditingLayerId = finishEditing ? null : editedLayer.Id;
                            if (finishEditing) ResetBrushLine();
                            else
                            {
                                brushLineDocument = editedDocument; brushLineLayer = editedLayer; brushLineErase = erasing;
                                brushLineUv = endUv; brushLineDimensions = new Vector2(editedDocument.width, editedDocument.height);
                                brushLineTransform = editedLayer.RenderTransform.ToMatrix(editedDocument.width, editedDocument.height);
                            }
                            owner.toolkitRefreshRequested = true;
                        }
                        if (full) owner.ShowNotification(new GUIContent("Stroke sample limit reached; start another stroke to continue."));
                    }, CancelBrush);
                    pending = true;
                }
                catch (Exception error) { owner.ShowNotification(new GUIContent(error.Message)); }
                finally { if (!pending) CancelBrush(); }
            }

            void CancelBrush()
            {
                brushBuilder = null; brushPixels = null;
                brushSource = null; brushOriginal = null; brushOriginalJson = null;
                brushFinishAfterRebuild = false;
                brushShift = false; brushAxis = 0;
                if (brushPreview != null) UnityEngine.Object.DestroyImmediate(brushPreview);
                brushPreview = null;
                overlay.MarkDirtyRepaint();
                if (owner.canvasTool == CanvasTool.VectorBrush) owner.RefreshCanvasPointerCursor();
            }

            void DrawBrush(MeshGenerationContext context)
            {
                if (brushPreview == null) return;
                var mesh = context.Allocate(4, 6, brushPreview);
                Vector2[] uv = { new(0, 1), new(1, 1), new(1, 0), new(0, 0) };
                foreach (Vector2 p in uv)
                {
                    Vector2 view = View(brushPreviewFrame.min + Vector2.Scale(p, brushPreviewFrame.size));
                    mesh.SetNextVertex(new Vertex { position = new Vector3(view.x, view.y, Vertex.nearZ), tint = brushColor, uv = p });
                }
                mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
                mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
            }
        }
    }
}
