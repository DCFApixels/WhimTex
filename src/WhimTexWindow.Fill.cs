using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private const int MaximumFillPixels = 16777216;

        private void AddPaintColorFields(VisualElement row)
        {
            ColorField primary = CompactField(WhimTexColorInputs.Bind(new ColorField(), toolkitHeaderBindings, () => paintSettings.brushColor), 54f);
            primary.tooltip = PrimaryBrushColorContent.tooltip;
            primary.PickerContext = this;
            primary.RegisterValueChangedCallback(evt => ApplyPaintToolChange(
                () => paintSettings.brushColor = evt.newValue));
            row.Add(primary);
            ColorField secondary = CompactField(WhimTexColorInputs.Bind(new ColorField(), toolkitHeaderBindings, () => paintSettings.secondaryBrushColor), 54f);
            secondary.tooltip = SecondaryBrushColorContent.tooltip;
            secondary.RegisterValueChangedCallback(evt => ApplyPaintToolChange(
                () => paintSettings.secondaryBrushColor = evt.newValue));
            row.Add(secondary);
        }

        private void AddFillSettings()
        {
            VisualElement row = CreateCanvasSettingsRow();
            row.AddToClassList("whimtex-fill-settings");
            BindCanvasSettingsRow(row, CanvasTool.Fill);
            AddPaintColorFields(row);
            Toggle allLayers = new Toggle("All Layers");
            allLayers.AddToClassList("whimtex-fill-all-layers");
            allLayers.tooltip = "On: sample the full-resolution visible composition. Off: sample this layer's stored pixels. Both paint only this Drawing layer.";
            toolkitHeaderBindings.Track(allLayers, () => paintSettings.fillSampleMode == FillSampleMode.AllLayers);
            allLayers.RegisterValueChangedCallback(evt => ApplyPaintToolChange(
                () => paintSettings.fillSampleMode = evt.newValue ? FillSampleMode.AllLayers : FillSampleMode.CurrentLayer));
            row.Add(allLayers);
            Toggle contiguous = new Toggle("Contiguous");
            contiguous.AddToClassList("whimtex-fill-contiguous");
            contiguous.tooltip = "On: fill only the connected area at the clicked pixel. Off: fill all similar pixels across the layer, even in separate areas. Uses the All Layers setting and Tolerance.";
            toolkitHeaderBindings.Track(contiguous, () => paintSettings.fillContiguous);
            contiguous.RegisterValueChangedCallback(evt => ApplyPaintToolChange(
                () => paintSettings.fillContiguous = evt.newValue));
            row.Add(contiguous);
            Slider tolerance = new Slider("Tolerance", 0f, 255f) { showInputField = true };
            tolerance.AddToClassList("whimtex-fill-tolerance");
            tolerance.tooltip = "Color/alpha similarity to the clicked pixel (0–255). Low values stop at small differences; high values include more colors. Contiguous limits matching to the connected area.";
            toolkitHeaderBindings.Track(tolerance, () => (float)paintSettings.fillTolerance);
            tolerance.RegisterValueChangedCallback(evt => ApplyPaintToolChange(
                () => paintSettings.fillTolerance = Mathf.Clamp(Mathf.RoundToInt(evt.newValue), 0, 255)));
            row.Add(tolerance);
            Toggle antialias = new Toggle("Antialias");
            antialias.AddToClassList("whimtex-fill-antialias");
            antialias.tooltip = "Soften the fill edge with partial pixel coverage. Disable for hard pixel-art edges.";
            toolkitHeaderBindings.Track(antialias, () => paintSettings.fillAntialias);
            antialias.RegisterValueChangedCallback(evt => ApplyPaintToolChange(
                () => paintSettings.fillAntialias = evt.newValue));
            row.Add(antialias);
            IntegerField expand = new IntegerField("Expand (px)");
            expand.AddToClassList("whimtex-fill-expand");
            expand.tooltip = "Grow the detected area by 0–32 source pixels to overlap outlines. Unlike Tolerance, this does not change which colors are connected.";
            toolkitHeaderBindings.Track(expand, () => paintSettings.fillExpand);
            expand.RegisterValueChangedCallback(evt => ApplyPaintToolChange(
                () => paintSettings.fillExpand = Mathf.Clamp(evt.newValue, 0, 32)));
            row.Add(expand);
            AddPaintInputSettings(row, path: false, usesPressure: false);
            toolkitCanvasViewHeader.Add(row);
        }

        private bool HandleFillPointerDown(PointerDownEvent evt)
        {
            if (!IsCanvasFillEnabled || evt.button != 0 || evt.altKey || activeDocument == null)
                return false;
            if (!CanvasContainsPaintPoint(evt.localPosition)) return false;
            WhimTexUI.ConsumeEvent(evt);
            Focus();
            toolkitCanvas.Focus();
            DrawingLayerBehaviour layer = (DrawingLayerBehaviour)GetSelectedLayer();
            if (!TiledCanvasUtility.IsInvertible(activeDocument.GetPaintTransform(layer)) ||
                !TryMapCanvasToLayerUv(evt.localPosition, toolkitCanvas.ImageRect, layer, out Vector2 uv))
            {
                ShowNotification(new GUIContent("Fill inside the layer's source frame, or apply its transform first."));
                return true;
            }
            if (tiledCanvas)
            {
                uv = TiledCanvasUtility.CanonicalSource(uv, activeDocument.GetPaintTransform(layer), activeDocument.width, activeDocument.height);
                if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                {
                    ShowNotification(new GUIContent("Fill inside a visible copy of the layer's source frame."));
                    return true;
                }
            }
            Color foreground = WhimTexColorInputs.DisplayColor(paintSettings.brushColor);
            Color color = HdrUtility.DecodePaintColor(foreground);
            if (color.a == 0 || paintSettings.dynamics.writeChannels == 0) return true;
            FinishPaintingStroke();
            FinishCanvasTransform();
            lineAnchorLayer = null;
            Texture2D composite = null;
            int undoGroup = -1;
            try
            {
                Texture2D stored = layer.StoredTexture;
                int width = stored != null ? stored.width : activeDocument.width;
                int height = stored != null ? stored.height : activeDocument.height;
                int length = checked(width * height);
                if (length > MaximumFillPixels || (paintSettings.fillSampleMode == FillSampleMode.AllLayers &&
                    (long)activeDocument.width * activeDocument.height > MaximumFillPixels))
                    throw new InvalidOperationException("Fill supports up to 16,777,216 pixels (for example, 4096 × 4096) per source/reference image.");
                using var source = stored != null ? HdrUtility.ReadPixels(stored, Allocator.TempJob) : new NativeArray<Color>(length, Allocator.TempJob);
                using var reference = new NativeArray<Color>(length, Allocator.TempJob);
                using var valid = new NativeArray<byte>(length, Allocator.TempJob);
                using var output = new NativeArray<Color>(length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                if (paintSettings.fillSampleMode == FillSampleMode.AllLayers)
                {
                    composite = activeDocument.ComposeCanvas();
                    using var compositePixels = HdrUtility.ReadPixels(composite, Allocator.TempJob);
                    new HdrFloodFillUtility.ProjectReferenceJob
                    {
                        composite = compositePixels, reference = reference, valid = valid,
                        width = width, height = height, compositeWidth = composite.width, compositeHeight = composite.height,
                        sourceToCanvas = activeDocument.GetPaintTransform(layer).ToMatrix(activeDocument.width, activeDocument.height)
                    }.Schedule(length, 256).Complete();
                }
                else
                {
                    new HdrFloodFillUtility.CopyReferenceJob
                    {
                        source = source, reference = reference, valid = valid, standard = layer.colorRange == LayerColorRange.Standard
                    }.Schedule(length, 256).Complete();
                }
                int seed = Mathf.Clamp(Mathf.FloorToInt(uv.y * height), 0, height - 1) * width +
                    Mathf.Clamp(Mathf.FloorToInt(uv.x * width), 0, width - 1);
                LimitFillToArea(layer, valid, width, height);
                if (!HdrFloodFillUtility.Fill(source, reference, valid, output, width, height, seed, color,
                    paintSettings.fillTolerance, paintSettings.fillExpand, paintSettings.fillAntialias, paintSettings.fillContiguous, layer.colorRange == LayerColorRange.Standard)) return true;
                MaskFillToArea(layer, source, output, width, height);
                if (paintSettings.dynamics.writeChannels != 15 || paintSettings.dynamics.lockAlpha)
                {
                    var protectedOutput = output;
                    for (int i = 0; i < length; i++)
                        protectedOutput[i] = DrawingLayerBehaviour.ProtectPaintColor(source[i], output[i], paintSettings.dynamics.writeChannels, paintSettings.dynamics.lockAlpha);
                }
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Fill Drawing Layer");
                Undo.RecordObject(activeDocument, "Fill Drawing Layer");
                layer.ApplyFillPixels(output, width, height, "Fill Drawing Layer");
                temporaryDocumentDirty = true;
                activeDocument.MarkChanged();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                RequestCanvasRender(true);
            }
            catch (Exception exception)
            {
                if (undoGroup >= 0)
                {
                    Undo.FlushUndoRecordObjects();
                    Undo.RevertAllDownToGroup(undoGroup);
                    activeDocument.InvalidateDrawingLayerSurfaces();
                    RequestCanvasRender(true);
                }
                ShowNotification(new GUIContent("Fill failed: " + exception.Message));
                Debug.LogException(exception);
            }
            finally
            {
                if (composite != null) DestroyImmediate(composite);
                if (undoGroup >= 0) Undo.IncrementCurrentGroup();
            }
            return true;
        }
    }
}
