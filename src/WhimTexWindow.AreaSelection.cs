using System;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [NonSerialized] private CanvasSelection areaSelection;
        [NonSerialized] private WhimTexDocument areaSelectionOwner;
        [NonSerialized] private Texture2D areaSelectionTexture;
        [NonSerialized] private int areaSelectionTextureRevision = -1;
        private AreaSelectionManipulator areaSelectionManipulator;
        private AreaSelectionOverlay areaSelectionOverlay;
        private SelectionCombine areaSelectionMode;
        private enum SelectionEdgeMode { Aliased, Antialiased }
        [SerializeField] private SelectionEdgeMode selectionEdgeMode = SelectionEdgeMode.Aliased;
        private enum MarqueeShape { Rectangle, Ellipse, UvIsland }
        [SerializeField] private MarqueeShape marqueeShape;
        private static AreaClipboard areaClipboard;
        private sealed class AreaClipboard
        {
            internal Color[] pixels;
            internal RectInt region;
            internal Vector2Int canvas;
            internal uint systemRevision;
            internal Guid token;
            internal bool systemImage;
            internal string systemText;
        }
        private bool IsAreaSelectionTool => canvasTool == CanvasTool.RectangleSelect || canvasTool == CanvasTool.PolygonSelect || IsUvSelectionTool;

        private CanvasSelection GetAreaSelection()
        {
            if (activeDocument == null) return null;
            if (areaSelection == null || areaSelectionOwner != activeDocument ||
                areaSelection.Width != activeDocument.width || areaSelection.Height != activeDocument.height)
            {
                ResetAreaSelection();
                areaSelectionOwner = activeDocument;
                areaSelection = new CanvasSelection(activeDocument.width, activeDocument.height);
            }
            return areaSelection;
        }
        private void ResetAreaSelection()
        {
            ResetQuickSelection();
            ReleaseQuickSelectionMask();
            areaSelectionManipulator?.Cancel();
            if (areaSelectionTexture != null) DestroyImmediate(areaSelectionTexture);
            areaSelectionTexture = null;
            areaSelectionTextureRevision = -1;
            areaSelection = null;
            areaSelectionOwner = null;
            areaSelectionOverlay?.Invalidate();
        }
        private Texture GetAreaSelectionTexture()
        {
            CanvasSelection selection = GetAreaSelection();
            if (selection == null || !selection.Active) return null;
            if (quickSelectionMaskTexture != null && quickSelectionMaskRevision == selection.Revision && quickSelectionMaskTexture.IsCreated())
                return quickSelectionMaskTexture;
            if (quickSelectionMaskTexture != null) ReleaseQuickSelectionMask();
            if (areaSelectionTexture != null && areaSelectionTextureRevision == selection.Revision) return areaSelectionTexture;
            using var profile = WhimTexProfiler.Measure("selection.upload", activeDocument);
            if (areaSelectionTexture != null) DestroyImmediate(areaSelectionTexture);
            bool compact = SystemInfo.SupportsTextureFormat(TextureFormat.R8);
            areaSelectionTexture = new Texture2D(selection.Width, selection.Height, compact ? TextureFormat.R8 : TextureFormat.RGBA32, false, true)
            {
                name = "WhimTex selection", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            if (compact) areaSelectionTexture.LoadRawTextureData(selection.Coverage);
            else
            {
                var colors = new Color32[selection.Coverage.Length];
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(selection.Coverage[i], 0, 0, 255);
                areaSelectionTexture.SetPixels32(colors);
            }
            areaSelectionTexture.Apply(false, false);
            areaSelectionTextureRevision = selection.Revision;
            return areaSelectionTexture;
        }
        private void ChangeAreaSelection(Action<CanvasSelection> action)
        {
            ResetQuickSelection();
            ReleaseQuickSelectionMask();
            if (!HasCanvasLayers && !(activeDocument != null && canvasTool == CanvasTool.Comment)) return;
            using var profile = WhimTexProfiler.Measure("selection.change", activeDocument);
            areaSelectionManipulator?.Cancel();
            FinishPaintingStroke();
            FinishCanvasTransform();
            try { action(GetAreaSelection()); }
            catch (Exception exception) { ShowNotification(new GUIContent(exception.Message)); }
            if (areaSelection != null && !areaSelection.Active && areaSelectionTexture != null)
            {
                DestroyImmediate(areaSelectionTexture);
                areaSelectionTexture = null;
                areaSelectionTextureRevision = -1;
            }
            areaSelectionOverlay?.Invalidate();
            toolkitHeaderBindings.Refresh();
            toolkitCanvas?.Focus();
        }
        private void BuildAreaSelectionTools()
        {
            areaSelectionOverlay = new AreaSelectionOverlay(this);
            toolkitCanvas.Add(areaSelectionOverlay);
            areaSelectionManipulator = new AreaSelectionManipulator(this);
            toolkitCanvas.AddManipulator(areaSelectionManipulator);
            toolkitCanvas.ViewChanged += areaSelectionOverlay.MarkDirtyRepaint;
            quickSelectionManipulator = new QuickSelectionManipulator(this);
            toolkitCanvas.AddManipulator(quickSelectionManipulator);
        }
        private void RegisterAreaSelectionCommands(VisualElement root)
        {
            root.UnregisterCallback<ValidateCommandEvent>(ValidateAreaCommand, TrickleDown.TrickleDown);
            root.UnregisterCallback<ExecuteCommandEvent>(ExecuteAreaCommand, TrickleDown.TrickleDown);
            root.RegisterCallback<ValidateCommandEvent>(ValidateAreaCommand, TrickleDown.TrickleDown);
            root.RegisterCallback<ExecuteCommandEvent>(ExecuteAreaCommand, TrickleDown.TrickleDown);
        }
        private bool CanHandleAreaCommand(string command, VisualElement target)
        {
            if (IsTextInputTarget(target) || IsTextInputTarget(rootVisualElement.panel?.focusController?.focusedElement as VisualElement)) return false;
            return activeDocument != null && (command == "Copy" || command == "Paste" || command == "SelectAll");
        }
        private void ValidateAreaCommand(ValidateCommandEvent evt)
        {
            if (!CanHandleAreaCommand(evt.commandName, evt.target as VisualElement)) return;
            WhimTexUI.ConsumeEvent(evt);
        }
        private void ExecuteAreaCommand(ExecuteCommandEvent evt)
        {
            if (!CanHandleAreaCommand(evt.commandName, evt.target as VisualElement)) return;
            WhimTexUI.ConsumeEvent(evt);
            if (evt.commandName == "Copy") CopySelection(false);
            else if (evt.commandName == "Paste") PasteAreaSelection();
            else ChangeAreaSelection(s => s.All());
        }
        private void AddAreaSelectionSettings(CanvasTool tool)
        {
            var row = CreateCanvasSettingsRow();
            row.AddToClassList("whimtex-area-settings");
            BindCanvasSettingsRow(row, tool);
            if (tool == CanvasTool.PolygonSelect)
            {
                var lasso = new EnumField(lassoMode) { name = "lassoMode", tooltip = "Polygonal places editable points; Freehand draws an outline until the mouse button is released." };
                TwoChoiceDropdown.Attach(lasso);
                lasso.AddToClassList("whimtex-shape-kind");
                toolkitHeaderBindings.Track(lasso, () => (Enum)lassoMode);
                lasso.RegisterValueChangedCallback(evt => SetLassoMode((LassoMode)evt.newValue));
                row.Add(lasso);
                var curve = new EnumField(lassoCurveMode) { name = "lassoCurveMode",
                    tooltip = "Straight connects points with lines. Smooth passes through them with an automatically smoothed curve." };
                TwoChoiceDropdown.Attach(curve);
                curve.AddToClassList("whimtex-shape-kind");
                toolkitHeaderBindings.Track(curve, () => (Enum)lassoCurveMode);
                toolkitHeaderBindings.Add(() => curve.EnableInClassList("whimtex-hidden", lassoMode == LassoMode.Freehand));
                curve.RegisterValueChangedCallback(evt =>
                {
                    lassoCurveMode = (LassoCurveMode)evt.newValue;
                    areaSelectionManipulator?.RefreshPolygonCurve();
                    toolkitHeaderBindings.Refresh();
                    toolkitCanvas?.Focus();
                });
                row.Add(curve);
                var corner = new Toggle("Corner") { name = "lassoCornerPoint",
                    tooltip = "Make the selected point a sharp corner. C toggles the point under the cursor, or the selected point." };
                toolkitHeaderBindings.Track(corner, () => areaSelectionManipulator?.SelectedPointIsCorner ?? false);
                toolkitHeaderBindings.Add(() =>
                {
                    corner.EnableInClassList("whimtex-hidden", lassoMode == LassoMode.Freehand || lassoCurveMode != LassoCurveMode.Smooth);
                    corner.SetEnabled(areaSelectionManipulator?.HasSelectedPolygonPoint ?? false);
                });
                corner.RegisterValueChangedCallback(evt =>
                {
                    areaSelectionManipulator?.SetSelectedPointCorner(evt.newValue);
                    toolkitCanvas?.Focus();
                });
                row.Add(corner);
            }
            var mode = new EnumField(areaSelectionMode);
            TwoChoiceDropdown.Attach(mode);
            mode.AddToClassList("whimtex-area-mode");
            mode.tooltip = "Selection operation. Shift adds, Alt subtracts, Shift+Alt intersects.";
            toolkitHeaderBindings.Track(mode, () => (Enum)areaSelectionMode);
            mode.RegisterValueChangedCallback(evt => areaSelectionMode = (SelectionCombine)evt.newValue);
            row.Add(mode);
            var edges = new EnumField(selectionEdgeMode) { name = "selectionEdgeMode",
                tooltip = "Selection edges. Aliased selects whole pixels; Antialiased gives boundary pixels partial coverage. Applies to the next selection." };
            TwoChoiceDropdown.Attach(edges);
            edges.AddToClassList("whimtex-shape-kind");
            toolkitHeaderBindings.Track(edges, () => (Enum)selectionEdgeMode);
            edges.RegisterValueChangedCallback(evt =>
            {
                selectionEdgeMode = (SelectionEdgeMode)evt.newValue;
                toolkitHeaderBindings.Refresh();
            });
            row.Add(edges);
            row.Add(WhimTexUI.CreateButton("All", () => ChangeAreaSelection(s => s.All())));
            row.Add(WhimTexUI.CreateButton("Deselect", () => ChangeAreaSelection(s => s.Clear())));
            row.Add(WhimTexUI.CreateButton("Invert", () => ChangeAreaSelection(s => s.Invert())));
            row.Add(WhimTexUI.CreateButton("Copy", () => CopySelection(false)));
            row.Add(WhimTexUI.CreateButton("Copy Merged", () => CopyAreaSelection(true)));
            row.Add(WhimTexUI.CreateButton("Paste", PasteAreaSelection));
            var contentFill = WhimTexUI.CreateButton("Content-Aware Fill", OpenContentAwareFill);
            contentFill.tooltip = "Fill the selection or an inner border using nearby texture details. Creates a new Drawing Layer.";
            toolkitHeaderBindings.Add(() => contentFill.SetEnabled(GetAreaSelection() is CanvasSelection s &&
                s.Active && s.Bounds.width > 0 && s.Bounds.height > 0));
            row.Add(contentFill);
            if (tool == CanvasTool.PolygonSelect)
            {
                var close = WhimTexUI.CreateButton("Close", () => areaSelectionManipulator?.CompletePolygon());
                toolkitHeaderBindings.Add(() => close.EnableInClassList("whimtex-hidden", lassoMode == LassoMode.Freehand));
                row.Add(close);
            }
            var status = new Label();
            status.AddToClassList("whimtex-area-status");
            toolkitHeaderBindings.Add(() =>
            {
                CanvasSelection selected = GetAreaSelection();
                status.text = selected != null && selected.Active
                    ? $"{selected.Bounds.width} × {selected.Bounds.height}" : "No selection";
            });
            row.Add(status);
            toolkitCanvasViewHeader.Add(row);
        }
        private bool HandleAreaSelectionKey(KeyDownEvent evt)
        {
            if (activeDocument == null || IsTextInputTarget(evt.target as VisualElement) ||
                IsTextInputTarget(rootVisualElement.panel?.focusController?.focusedElement as VisualElement)) return false;
            bool action = evt.ctrlKey || evt.commandKey;
            if (!action && areaSelectionManipulator != null && areaSelectionManipulator.HasGesture)
            {
                if (evt.keyCode == KeyCode.Escape) areaSelectionManipulator.Cancel();
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) areaSelectionManipulator.CompletePolygon();
                else if (evt.keyCode == KeyCode.Backspace) areaSelectionManipulator.RemoveVertex();
                else if (evt.keyCode == KeyCode.C && IsSmoothLasso) areaSelectionManipulator.TogglePolygonCorner();
                else return false;
            }
            else if (action && !evt.altKey)
            {
                if (evt.keyCode == KeyCode.C) CopySelection(evt.shiftKey);
                else if (evt.keyCode == KeyCode.V && !evt.shiftKey) PasteAreaSelection();
                else if (evt.keyCode == KeyCode.D && !evt.shiftKey) ChangeAreaSelection(s => s.Clear());
                else if (evt.keyCode == KeyCode.A && !evt.shiftKey) ChangeAreaSelection(s => s.All());
                else if (evt.keyCode == KeyCode.I && evt.shiftKey) ChangeAreaSelection(s => s.Invert());
                else return false;
            }
            else return false;
            toolkitHeaderBindings.Refresh();
            WhimTexUI.ConsumeEvent(evt);
            return true;
        }
        private bool TrySelectLayerAlpha(VisualElement row, Layer layer, PointerDownEvent evt)
        {
            if (evt.button != 0 || (!evt.ctrlKey && !evt.commandKey)) return false;
            var thumbnail = row.Q<VisualElement>(className: "whimtex-layer-thumbnail") ??
                row.Q<VisualElement>(className: "whimtex-group-foldout");
            if (thumbnail == null || !thumbnail.worldBound.Contains(evt.position)) return false;
            SelectionCombine combine = evt.shiftKey && evt.altKey ? SelectionCombine.Intersect :
                evt.shiftKey ? SelectionCombine.Add : evt.altKey ? SelectionCombine.Subtract : SelectionCombine.Replace;
            ChangeAreaSelection(selection =>
            {
                selection.ValidateSize();
                Texture2D rendered = activeDocument.RenderAreaSelectionAlphaSource(layer);
                try
                {
                    using var pixels = HdrUtility.ReadPixels(rendered, Allocator.Temp);
                    var mask = new byte[selection.Width * selection.Height];
                    for (int i = 0; i < mask.Length; i++) mask[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(pixels[i].a) * 255f);
                    selection.Set(mask, combine);
                }
                finally { if (rendered != null) DestroyImmediate(rendered); }
            });
            WhimTexUI.ConsumeEvent(evt);
            return true;
        }
        private void CopySelection(bool merged)
        {
            if(DeferUntilQuickSelectionReady(()=>CopySelection(merged)))return;
            if (activeDocument == null) return;
            if (merged || GetAreaSelection().Active)
            {
                CopyAreaSelection(merged);
                return;
            }
            areaSelectionManipulator?.Cancel();
            FinishPaintingStroke(); FinishCanvasTransform();
            try
            {
                NormalizeLayerSelection();
                var roots = GetSelectedRoots();
                LayerClipboard.Copy(activeDocument, roots);
                areaClipboard = null;
                ShowNotification(new GUIContent(roots.Count == 1 ? "Layer copied" : "Layers copied"));
            }
            catch (Exception exception) { ShowNotification(new GUIContent("Copy failed: " + exception.Message)); }
        }

        private void CopyAreaSelection(bool merged)
        {
            if(DeferUntilQuickSelectionReady(()=>CopyAreaSelection(merged)))return;
            if (!HasCanvasLayers) return;
            areaSelectionManipulator?.Cancel();
            FinishPaintingStroke(); FinishCanvasTransform();
            Texture2D rendered = null;
            try
            {
                CanvasSelection selection = GetAreaSelection();
                selection.ValidateSize();
                Layer layer = GetSelectedLayer();
                if (!merged && layer == null) throw new InvalidOperationException("Select a layer to copy, or use Copy Merged.");
                RectInt region = selection.Active ? selection.Bounds : new RectInt(0, 0, activeDocument.width, activeDocument.height);
                if (region.width == 0 || region.height == 0) throw new InvalidOperationException("The selection is empty.");
                rendered = activeDocument.RenderAreaSelectionSource(merged ? null : layer);
                using var pixels = HdrUtility.ReadPixels(rendered, Allocator.Temp);
                var copy = new Color[region.width * region.height];
                for (int y = 0, i = 0; y < region.height; y++)
                for (int x = 0; x < region.width; x++, i++)
                {
                    int index = (region.y + y) * selection.Width + region.x + x;
                    Color color = pixels[index];
                    if (selection.Active) color.a *= selection.Coverage[index] / 255f;
                    copy[i] = color.a > 0f ? color : Color.clear;
                }
                var next = new AreaClipboard { pixels = copy, region = region,
                    canvas = new Vector2Int(selection.Width, selection.Height), token = Guid.NewGuid() };
                next.systemImage = ImageClipboard.TryPublishPixels(copy, region.width, region.height, next.token, out next.systemRevision);
                if (!next.systemImage)
                {
                    next.systemRevision = ImageClipboard.Revision;
                    next.systemText = GUIUtility.systemCopyBuffer;
                }
                areaClipboard = next;
                LayerClipboard.Clear();
                ShowNotification(new GUIContent(next.systemImage ? "Image copied" : "Copied to WhimTex clipboard"));
            }
            catch (Exception exception) { ShowNotification(new GUIContent("Copy failed: " + exception.Message)); }
            finally { if (rendered != null) DestroyImmediate(rendered); }
        }
        private void PasteAreaSelection()
        {
            if (activeDocument == null) return;
            areaSelectionManipulator?.Cancel();
            FinishPaintingStroke(); FinishCanvasTransform();
            Texture2D texture = null;
            try
            {
                if (TryPasteCommentLink(GUIUtility.systemCopyBuffer)) return;
                var copy = GetCurrentAreaClipboard();
                if (copy != null)
                {
                    PasteAreaClipboard(copy);
                    return;
                }
                string clipboardText = GUIUtility.systemCopyBuffer;
                if (TryPasteBrushClipboard(clipboardText)) return;
                if (TryPasteImageUrl(clipboardText)) return;
                if (WhimTexApi.IsProceduralClipboard(clipboardText))
                {
                    WhimTexApi.ProceduralClipboard generated = WhimTexApi.ReadProceduralClipboard(clipboardText, activeDocument.width, activeDocument.height);
                    try
                    {
                        bool resize = generated.HasCanvas && (activeDocument.width != generated.Document.width || activeDocument.height != generated.Document.height ||
                            !HasCanvasLayers && activeDocument.GetPixelScale() != generated.Document.GetPixelScale());
                        if (resize && HasCanvasLayers)
                            resize = EditorUtility.DisplayDialog("Canvas size from JSON",
                                $"Change canvas from {activeDocument.width} × {activeDocument.height} to {generated.Document.width} × {generated.Document.height}?\n\nKeep Current still pastes the layers without resizing the canvas.",
                                "Apply Size", "Keep Current");
                        if (generated.Effects.Count > 0 && !EditorUtility.DisplayDialog("Paste custom Shader FX",
                            $"This JSON contains {generated.Effects.Count} custom GPU shader(s). Only paste code you trust: expensive shaders can freeze rendering.\n\nCompile and paste?",
                            "Compile and Paste", "Cancel")) return;
                        generated.Compile();
                        PasteProceduralClipboard(generated, resize);
                    }
                    finally { generated.Dispose(); }
                    return;
                }
                WhimTexDocument copiedLayers = LayerClipboard.Current;
                if (copiedLayers != null)
                {
                    PasteCopiedLayers(copiedLayers);
                    return;
                }
                texture = ImageClipboard.ReadImage();
                if (texture == null) { ShowNotification(new GUIContent("No image in the clipboard.")); return; }
                Texture2D source = texture;
                bool insertedImage = false;
                ExecuteContextChange("Paste Clipboard Image", () =>
                {
                    if (!HasCanvasLayers) { activeDocument.width = source.width; activeDocument.height = source.height; }
                    var layer = DrawingLayerBehaviour.FromMergedTexture(source);
                    layer.colorRange = LayerColorRange.Standard;
                    layer.blendRange = LayerBlendRange.Standard;
                    layer.layerName = activeDocument.AllocateLayerName(layer);
                    // Keep all source pixels; placement is 1:1 and centered, not resampled or clipped.
                    var placement = TextureTransform.Default;
                    placement.scale = new Vector2((float)source.width / activeDocument.width, (float)source.height / activeDocument.height);
                    layer.transform = placement;
                    Undo.RegisterCreatedObjectUndo(source, "Paste Clipboard Image");
                    activeDocument.layers.Insert(0, layer);
                    SelectOnlyLayer(layer.Id);
                    insertedImage = true;
                });
                if (insertedImage && activeDocument.layers.Exists(layer => layer?.Behaviour is DrawingLayerBehaviour drawing && drawing.StoredTexture == source))
                    texture = null;
            }
            catch (Exception exception) { ReportClipboardPasteError("Paste failed", exception); }
            finally { if (texture != null) DestroyImmediate(texture, true); }
        }

        private static AreaClipboard GetCurrentAreaClipboard()
        {
            var copy = areaClipboard;
            if (copy == null) return null;
            bool current = copy.systemImage ? ImageClipboard.IsCurrentImage(copy.token, copy.systemRevision) :
                copy.systemRevision == ImageClipboard.Revision && copy.systemText == GUIUtility.systemCopyBuffer;
            if (current) return copy;
            areaClipboard = null;
            return null;
        }

        private static Texture2D BuildAreaClipboardTexture(AreaClipboard copy, int width, int height)
        {
            if (width < 1 || height < 1 || (long)width * height > CanvasSelection.MaximumPixels)
                throw new InvalidOperationException("Paste canvas exceeds the supported image size.");
            int left = copy.canvas == new Vector2Int(width, height) ? copy.region.x : (width - copy.region.width) / 2;
            int bottom = copy.canvas == new Vector2Int(width, height) ? copy.region.y : (height - copy.region.height) / 2;
            var pixels = new Color[width * height];
            for (int y = 0; y < copy.region.height; y++)
            {
                int outputY = bottom + y;
                if (outputY < 0 || outputY >= height) continue;
                int first = Mathf.Max(0, -left), end = Mathf.Min(copy.region.width, width - left);
                if (end > first) Array.Copy(copy.pixels, y * copy.region.width + first, pixels, outputY * width + left + first, end - first);
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            try { texture.SetPixels(pixels); texture.Apply(false, false); return texture; }
            catch { DestroyImmediate(texture); throw; }
        }

        private void PasteAreaClipboard(AreaClipboard copy)
        {
            GetAreaSelection().ValidateSize();
            Texture2D texture = BuildAreaClipboardTexture(copy, activeDocument.width, activeDocument.height);
            try
            {
                Texture2D pasted = texture;
                bool inserted = false;
                ExecuteContextChange("Paste Drawing Layer", () =>
                {
                    DrawingLayerBehaviour layer = DrawingLayerBehaviour.FromMergedTexture(pasted);
                    layer.layerName = activeDocument.AllocateLayerName(layer);
                    Undo.RegisterCreatedObjectUndo(pasted, "Paste Drawing Layer");
                    activeDocument.layers.Insert(0, layer);
                    SelectOnlyLayer(layer.Id);
                    inserted = true;
                });
                if (inserted && activeDocument.layers.Exists(layer => layer?.Behaviour is DrawingLayerBehaviour drawing && drawing.StoredTexture == pasted)) texture = null;
            }
            finally { if (texture != null) DestroyImmediate(texture, true); }
        }
        private void ReportClipboardPasteError(string operation, Exception exception)
        {
            Debug.LogError("[WhimTex] " + operation + ":\n" + exception, this);
            ShowNotification(new GUIContent(operation + ": " + exception.Message));
        }
        private void LimitFillToArea(DrawingLayerBehaviour layer, NativeArray<byte> valid, int width, int height)
        {
            CanvasSelection selection = GetAreaSelection();
            if (selection == null || !selection.Active) return;
            Vector2 origin = MapLayerToDocumentUv(Vector2.zero, layer);
            Vector2 dx = (MapLayerToDocumentUv(Vector2.right, layer) - origin) / width;
            Vector2 dy = (MapLayerToDocumentUv(Vector2.up, layer) - origin) / height;
            for (int y = 0, i = 0; y < height; y++)
            for (int x = 0; x < width; x++, i++)
                if (selection.Sample(origin + (x + .5f) * dx + (y + .5f) * dy, tiledCanvas) <= 0f)
                    valid[i] = 0;
        }
        private void MaskFillToArea(DrawingLayerBehaviour layer, NativeArray<Color> source, NativeArray<Color> output, int width, int height)
        {
            CanvasSelection selection = GetAreaSelection();
            if (selection == null || !selection.Active) return;
            Vector2 origin = MapLayerToDocumentUv(Vector2.zero, layer);
            Vector2 dx = (MapLayerToDocumentUv(Vector2.right, layer) - origin) / width;
            Vector2 dy = (MapLayerToDocumentUv(Vector2.up, layer) - origin) / height;
            for (int y = 0, i = 0; y < height; y++)
            for (int x = 0; x < width; x++, i++)
            {
                float coverage = selection.Sample(origin + (x + .5f) * dx + (y + .5f) * dy, tiledCanvas);
                Color before = source[i], after = output[i];
                if (coverage <= 0f) { output[i] = before; continue; }
                if (coverage >= 1f) continue;
                float alpha = Mathf.Lerp(before.a, after.a, coverage);
                Color result = Color.Lerp(before * before.a, after * after.a, coverage);
                output[i] = alpha > 0f ? new Color(result.r / alpha, result.g / alpha, result.b / alpha, alpha) : Color.clear;
            }
        }
    }
}
