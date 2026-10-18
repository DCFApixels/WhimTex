using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [SerializeField] private string vectorShapeId;
        [SerializeField] private string vectorSelectionLayerId;
        [SerializeField] private Color vectorFillColor = Color.white;
        [SerializeField] private float vectorBrushSize = 24f;
        [SerializeField] private VectorBrushShape vectorBrushShape;
        [SerializeField] private float vectorBrushTolerance = .75f;
        private VectorManipulator vectorManipulator;
        private WhimTexDocument vectorBrushEditingDocument;
        private string vectorBrushEditingLayerId;
        private readonly Dictionary<CanvasTool, Button> vectorButtons = new();
        private VectorMaskRebuild vectorMaskRebuild;
        private Task vectorMaskWorker;
        private static readonly Unity.Profiling.ProfilerMarker vectorMaskWorkerMarker = new("WhimTex.VectorMask.RebuildWorker");
        private bool IsVectorMaskRebuilding => vectorMaskRebuild != null || vectorMaskWorker?.IsCompleted == false;

        private sealed class VectorMaskRebuild
        {
            internal readonly CancellationTokenSource cancellation = new();
            internal Task<VectorShape> task;
            internal WhimTexDocument document;
            internal Layer selection;
            internal CanvasTool tool;
            internal Func<bool> valid;
            internal Action<VectorShape> apply;
            internal Action finished;
            internal string phase;
            internal Layer layer;
        }

        private void BeginVectorMaskRebuild(VectorBrushDraft draft, Vector2 referenceSize, Layer layer,
            string phase, Func<bool> valid, Action<VectorShape> apply, Action finished)
        {
            if (IsVectorMaskRebuilding) throw new InvalidOperationException("Wait for the current mask rebuild or cancel it first.");
            WhimTexHangDiagnostics.Start(phase, layer?.Id);
            using var diagnostic = WhimTexHangDiagnostics.Enter("vector.mask.schedule");
            WhimTexHangDiagnostics.Note("vector.mask.input", new { draft.width, draft.height, draft.tolerance,
                draft.expansion, referenceWidth = referenceSize.x, referenceHeight = referenceSize.y });
            var input = draft.DeepClone();
            input.Validate();
            var job = new VectorMaskRebuild { document = activeDocument, selection = GetSelectedLayer(), tool = canvasTool,
                valid = valid, apply = apply, finished = finished, phase = phase, layer = layer };
            var token = job.cancellation.Token;
            try
            {
                job.task = Task.Run(() =>
                {
                    using var marker = vectorMaskWorkerMarker.Auto();
                    using var workerDiagnostic = WhimTexHangDiagnostics.Enter("vector.mask.worker");
                    return VectorBrushGeometry.Rebuild(input, referenceSize, token);
                }, token);
                vectorMaskWorker = job.task;
                vectorMaskRebuild = job;
                toolkitRefreshRequested = true;
            }
            catch { job.cancellation.Dispose(); throw; }
        }

        private void UpdateVectorMaskRebuild()
        {
            var job = vectorMaskRebuild;
            if (job == null)
            {
                if (vectorMaskWorker?.IsCompleted == true) { vectorMaskWorker = null; toolkitRefreshRequested = true; }
                return;
            }
            if (activeDocument == null || activeDocument != job.document || GetSelectedLayer() != job.selection || canvasTool != job.tool)
            { CancelVectorMaskRebuild(); return; }
            if (!job.task.IsCompleted) return;
            vectorMaskRebuild = null; vectorMaskWorker = null;
            using var diagnostic = WhimTexHangDiagnostics.Enter("vector.mask.complete");
            try
            {
                var shape = job.task.GetAwaiter().GetResult();
                bool valid;
                using (WhimTexHangDiagnostics.Enter("vector.mask.validate_result")) valid = job.valid();
                if (!valid)
                { ShowNotification(new GUIContent("Mask rebuild discarded: the source figure changed.")); return; }
                using var measure = WhimTexProfiler.Measure(job.phase + ".commit", job.document, job.layer, detail: "Commit background mask rebuild; excludes worker time");
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                try { job.apply(shape); }
                finally
                {
                    using var undoProfile = WhimTexProfiler.Measure("undo.finalize", job.document, job.layer, detail: job.phase);
                    using (WhimTexHangDiagnostics.Enter("undo.flush")) Undo.FlushUndoRecordObjects();
                    using (WhimTexHangDiagnostics.Enter("undo.collapse")) Undo.CollapseUndoOperations(group);
                    Undo.IncrementCurrentGroup();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { ShowNotification(new GUIContent(error.Message)); }
            finally
            {
                using var cleanupDiagnostic = WhimTexHangDiagnostics.Enter("vector.mask.cleanup");
                job.cancellation.Dispose(); job.finished?.Invoke();
                toolkitRefreshRequested = true;
            }
        }

        private void CancelVectorMaskRebuild(bool notify = true)
        {
            var job = vectorMaskRebuild;
            if (job == null) return;
            vectorMaskRebuild = null;
            job.cancellation.Cancel();
            job.task.ContinueWith(task => { _ = task.Exception; job.cancellation.Dispose(); }, TaskScheduler.Default);
            job.finished?.Invoke();
            toolkitRefreshRequested = true;
            if (notify) ShowNotification(new GUIContent("Pending vector mask rebuild cancelled. The previous figure is unchanged."));
        }

        private bool CanSaveVectorMask()
        {
            if (vectorMaskRebuild == null) return true;
            ShowNotification(new GUIContent("Wait for the vector mask rebuild, or cancel it, before saving."));
            return false;
        }
        private static bool IsVectorTool(CanvasTool tool) => tool == CanvasTool.VectorPen || tool == CanvasTool.VectorNodes || tool == CanvasTool.VectorBrush;
        private static VectorShape CopyVectorShape(VectorShape shape) => shape.Clone();

        private void BuildVectorTools()
        {
            vectorManipulator?.Cancel();
            vectorManipulator = new VectorManipulator(this);
            toolkitCanvas.AddManipulator(vectorManipulator);
        }

        private void AddVectorToolButtons(VisualElement toolbar)
        {
            vectorButtons.Clear();
            void Add(CanvasTool tool, string name, string help)
            {
                var button = CreateCanvasToolButton(name, tool, help);
                vectorButtons.Add(tool, button); toolbar.Add(button);
            }
            Add(CanvasTool.VectorNodes, "vectorNodesTool", "Vector Nodes. Drag points or tangent handles. Shift selects more points; drag empty space to select points. Double-click an edge to insert a point. Alt-click deletes a point; Alt-drag breaks tangent symmetry.");
            Add(CanvasTool.VectorPen, "vectorPenTool", "Vector Pen. Click for corners; drag for curved nodes. Click the first point to close, Enter to finish an open path, Escape to cancel. Creates a Vector layer when needed.");
            Add(CanvasTool.VectorBrush, "vectorBrushTool", "Vector Brush. Left mouse adds to the active mask; right mouse erases. Enter or Finish Editing ends the figure; the next left stroke creates a new one. Edit Mask resumes a saved mask. [ and ] change size; Shift draws straight lines. Escape cancels the current stroke.");
        }

        private void RefreshVectorTools()
        {
            if (vectorBrushEditingDocument != activeDocument || vectorBrushEditingLayerId != GetSelectedLayer()?.Id ||
                canvasTool != CanvasTool.VectorBrush || !CanEditVectorMask(GetSelectedLayer()))
            { vectorBrushEditingDocument = null; vectorBrushEditingLayerId = null; }
            foreach (var pair in vectorButtons)
            {
                pair.Value.EnableInClassList("whimtex-tool-button--selected", canvasTool == pair.Key);
                pair.Value.EnableInClassList("whimtex-tool-button--unavailable", activeDocument == null);
            }
            vectorManipulator?.Refresh();
        }

        private void AddVectorSettings()
        {
            foreach (CanvasTool tool in new[] { CanvasTool.VectorPen, CanvasTool.VectorNodes, CanvasTool.VectorBrush })
            {
                var row = CreateCanvasSettingsRow(); BindCanvasSettingsRow(row, tool);
                if (tool == CanvasTool.VectorPen || tool == CanvasTool.VectorBrush)
                {
                    var color = new WhimTexColorField("Fill") { value = vectorFillColor };
                    if (tool == CanvasTool.VectorBrush) CompactField(color, 104f);
                    else color.AddToClassList("whimtex-view-field");
                    row.Add(color);
                    toolkitHeaderBindings.Track(color, () => vectorFillColor);
                    if (tool == CanvasTool.VectorBrush) toolkitHeaderBindings.Add(() => color.SetEnabled(!IsEditingVectorMask(GetSelectedLayer())));
                    color.RegisterValueChangedCallback(e => vectorFillColor = e.newValue);
                }
                if (tool == CanvasTool.VectorPen)
                {
                    row.Add(new Button(() => vectorManipulator?.FinishPen(false)) { text = "Finish Open" });
                    row.Add(new Button(() => vectorManipulator?.FinishPen(true)) { text = "Close Path" });
                }
                if (tool == CanvasTool.VectorBrush)
                {
                    var size = CompactField(new FloatField("Size")
                    {
                        name = "vectorBrushSize", value = vectorBrushSize,
                        tooltip = "Brush diameter in canvas pixels (1–2048). Drag the label or use [ and ] to adjust."
                    }, 76f);
                    size.AddToClassList("whimtex-brush-size"); row.Add(size);
                    toolkitHeaderBindings.Track(size, () => vectorBrushSize);
                    size.RegisterValueChangedCallback(e => SetVectorBrushSize(e.newValue));
                    var brushShape = CompactField(new EnumField("Shape", vectorBrushShape) { name = "vectorBrushShape" }, 94f);
                    row.Add(brushShape);
                    toolkitHeaderBindings.Track(brushShape, () => (Enum)vectorBrushShape);
                    brushShape.RegisterValueChangedCallback(e =>
                    {
                        vectorBrushShape = (VectorBrushShape)e.newValue;
                        RefreshCanvasPointerCursor();
                    });
                    var precision = CompactField(new FloatField("Tolerance") { name = "vectorBrushTolerance", value = vectorBrushTolerance, tooltip = "Bezier fitting tolerance in layer reference pixels. Lower values follow the mask more closely; higher values usually use fewer anchors." }, 100f);
                    row.Add(precision);
                    toolkitHeaderBindings.Track(precision, () => vectorBrushTolerance);
                    toolkitHeaderBindings.Add(() => precision.SetEnabled(!IsEditingVectorMask(GetSelectedLayer())));
                    precision.RegisterValueChangedCallback(e => { vectorBrushTolerance = ShapeLayerBehaviour.Limit(e.newValue, VectorBrushDraft.MinTolerance, VectorBrushDraft.MaxTolerance, .75f); precision.SetValueWithoutNotify(vectorBrushTolerance); });
                    AddVectorMaskEditingButton(row, () => GetSelectedLayer(), toolkitHeaderBindings, "vectorBrushEditMask");
                    var status = new Label(); row.Add(status);
                    toolkitHeaderBindings.Add(() => status.text = IsVectorMaskRebuilding ? "Rebuilding mask…" : "");
                }
                if (tool == CanvasTool.VectorNodes)
                    foreach (VectorTangentMode mode in Enum.GetValues(typeof(VectorTangentMode)))
                    {
                        var choice = mode;
                        row.Add(new Button(() => vectorManipulator?.SetTangentMode(choice)) { text = mode.ToString() });
                    }
                row.Add(new Button(() => vectorManipulator?.Cancel()) { text = "Cancel" });
                toolkitCanvasViewHeader.Add(row);
            }
        }

        private bool CanEditVectorMask(Layer layer) => activeDocument != null && layer?.Behaviour is VectorLayerBehaviour vector &&
            vector.brushDraft != null && vector.shapes.Exists(s => s.id == vector.brushDraft.shapeId) &&
            !WhimTexApi.IsLayerContentLocked(activeDocument, layer);

        private bool IsEditingVectorMask(Layer layer) => layer != null && vectorBrushEditingDocument == activeDocument &&
            vectorBrushEditingLayerId == layer.Id && GetSelectedLayer() == layer && CanEditVectorMask(layer);

        private void EditVectorMask(Layer layer)
        {
            if (layer != GetSelectedLayer() || !CanEditVectorMask(layer)) return;
            vectorManipulator?.Cancel();
            SetCanvasTool(CanvasTool.VectorBrush);
            vectorBrushEditingDocument = activeDocument; vectorBrushEditingLayerId = layer.Id;
            vectorManipulator?.ResetBrushLine();
            toolkitRefreshRequested = true; RefreshToolkitInterface(true);
            toolkitCanvas?.Focus();
        }

        private void FinishVectorMaskEditing()
        {
            vectorManipulator?.FinishBrushEditing();
            vectorBrushEditingDocument = null; vectorBrushEditingLayerId = null;
            vectorManipulator?.ResetBrushLine();
            toolkitRefreshRequested = true; RefreshToolkitInterface(true);
            toolkitCanvas?.Focus();
        }

        private void AddVectorMaskEditingButton(VisualElement parent, Func<Layer> readLayer, WhimTexUI.ValueBindings bindings, string name)
        {
            var button = new Button(() =>
            {
                if (IsEditingVectorMask(readLayer())) FinishVectorMaskEditing();
                else EditVectorMask(readLayer());
            }) { name = name };
            parent.Add(button);
            bindings.Add(() =>
            {
                bool editing = IsEditingVectorMask(readLayer());
                button.text = editing ? "Finish Editing" : "Edit Mask";
                button.tooltip = editing ? "Finish this figure (Enter). Keep its Original Mask; the next left stroke starts a new figure."
                    : "Resume painting and erasing this figure's saved Original Mask. Figures without a source mask are not rasterized automatically.";
                button.SetEnabled(editing || !IsVectorMaskRebuilding && CanEditVectorMask(readLayer()));
            });
        }

        private void SetVectorBrushSize(float size)
        {
            vectorBrushSize = ShapeLayerBehaviour.Limit(size, 1, 2048, 24);
            toolkitCanvasViewHeader?.Q<FloatField>("vectorBrushSize")?.SetValueWithoutNotify(vectorBrushSize);
            vectorManipulator?.RefreshBrushSize();
            if (canvasTool == CanvasTool.VectorBrush) toolkitCanvas?.SetRoundCursorSize(vectorBrushSize);
        }

        private bool HandleVectorBrushSizeKey(KeyDownEvent evt)
        {
            if (canvasTool != CanvasTool.VectorBrush || evt.ctrlKey || evt.commandKey || evt.altKey) return false;
            bool smaller = evt.keyCode == KeyCode.LeftBracket || evt.character == '[';
            bool larger = evt.keyCode == KeyCode.RightBracket || evt.character == ']';
            if (!smaller && !larger) return false;
            float step = PaintToolSettings.GetSizeShortcutStep(vectorBrushSize);
            SetVectorBrushSize(Mathf.Round(vectorBrushSize + (larger ? step : -step)));
            WhimTexUI.ConsumeEvent(evt);
            return true;
        }

        private VectorShape SelectedVectorShape(VectorLayerBehaviour vector)
        {
            string layerId = GetSelectedLayer()?.Id;
            if (vectorSelectionLayerId != layerId)
            {
                vectorSelectionLayerId = layerId;
                vectorShapeId = vector.shapes.Count > 0 ? vector.shapes[vector.shapes.Count - 1].id : null;
            }
            var found = vector.shapes.Find(s => s.id == vectorShapeId);
            return found ?? (vector.shapes.Count == 1 ? vector.shapes[0] : null);
        }

        private void SelectVectorShape(VectorShape shape)
        {
            if (vectorShapeId == shape?.id) return;
            vectorShapeId = shape?.id;
            vectorSelectionLayerId = GetSelectedLayer()?.Id;
            ResetToolkitLayerInspector(); toolkitRefreshRequested = true;
            vectorManipulator?.Refresh();
        }

        private void CommitVectorShape(Layer layer, VectorShape shape, string action, bool clearDraft = true, VectorBrushDraft draft = null)
        {
            if (activeDocument == null || layer?.Behaviour is not VectorLayerBehaviour vector) return;
            try
            {
                var shapes = new List<VectorShape>(vector.shapes);
                int slot = shapes.FindIndex(s => s.id == shape.id);
                if (slot < 0) shapes.Add(shape); else shapes[slot] = shape;
                new VectorLayerBehaviour { referenceSize = vector.referenceSize, edgeMode = vector.edgeMode,
                    feather = vector.feather, featherPosition = vector.featherPosition, shapes = shapes }.Validate();
                if (activeDocument.IsBooleanOperand(layer))
                    foreach (var contour in shape.contours)
                        if (!contour.closed && contour.points.Count > 1) throw new InvalidOperationException("Boolean operands require closed contours.");
                draft?.Validate();
            }
            catch (Exception error) { ShowNotification(new GUIContent(error.Message)); return; }
            using var measure = WhimTexProfiler.Measure("vector.edit", activeDocument, layer, detail: action);
            if (activeDocument.FindLayer(layer.Id) == null)
            {
                vector.shapes.Add(shape);
                vector.brushDraft = draft;
                List<Layer> container = activeDocument.layers; int index = 0;
                var anchor = GetSelectedLayer();
                if (anchor != null && activeDocument.TryFindLayer(anchor, out var selected, out int selectedIndex))
                { container = selected; index = selectedIndex; }
                if (anchor?.Behaviour is BooleanGroupLayerBehaviour) { container = anchor.children; index = 0; }
                if (!activeDocument.CanInsertInto(layer, container))
                { ShowNotification(new GUIContent("Boolean operands require a closed path. Finish a closed path or draw outside this group.")); return; }
                activeDocument.PlaceCanvasTransform(layer, container);
                AddLayer(container, index, layer, "Vector", completeUndo: draft != null);
            }
            else if (!WhimTexApi.IsLayerContentLocked(activeDocument, layer))
            {
                bool changesMask = draft != null || clearDraft && vector.brushDraft?.shapeId == shape.id;
                ExecuteModelChange(action, () =>
                {
                    int index = vector.shapes.FindIndex(s => s.id == shape.id);
                    if (index < 0) vector.shapes.Add(shape); else vector.shapes[index] = shape;
                    if (clearDraft && vector.brushDraft?.shapeId == shape.id) vector.brushDraft = null;
                    if (draft != null) vector.brushDraft = draft;
                }, completeUndo: changesMask);
            }
            SelectVectorShape(shape);
            ResetToolkitLayerInspector(); toolkitRefreshRequested = true;
        }

        private void ConvertShapeToVector(Layer layer)
        {
            if (layer?.Behaviour is not ShapeLayerBehaviour source || activeDocument == null || WhimTexApi.IsLayerContentLocked(activeDocument, layer)) return;
            try
            {
                var converted = ShapeToVector.Convert(source, activeDocument);
                ExecuteModelChange("Convert Shape to Vector", () =>
                {
                    layer.SetBehaviour(converted);
                });
                SelectVectorShape(converted.shapes.Count > 0 ? converted.shapes[0] : null);
                SetCanvasTool(CanvasTool.VectorNodes);
            }
            catch (Exception error) { ShowNotification(new GUIContent(error.Message)); }
        }

        private void ConvertTextToVector(Layer layer)
        {
            if (layer?.Behaviour is not TextLayerBehaviour source || activeDocument == null || WhimTexApi.IsLayerContentLocked(activeDocument, layer)) return;
            try
            {
                var converted = TextToVector.PrepareLayer(source, activeDocument);
                ExecuteModelChange("Convert Text to Vector", () =>
                {
                    TextToVector.ApplyPrepared(layer, converted);
                }, completeUndo: true);
                var vector = layer.Behaviour as VectorLayerBehaviour;
                SelectVectorShape(vector?.shapes.Count > 0 ? vector.shapes[0] : null);
                SetCanvasTool(layer.IsGroup ? CanvasTool.Transform : CanvasTool.VectorNodes);
            }
            catch (Exception error) { ShowNotification(new GUIContent(error.Message)); }
        }

        private static void DrawVectorToolIcon(Painter2D p, CanvasTool tool, Func<float, float, Vector2> point)
        {
            if (tool == CanvasTool.VectorNodes)
            {
                p.BeginPath(); p.MoveTo(point(5, 3)); p.LineTo(point(18, 13)); p.LineTo(point(12, 14)); p.LineTo(point(9, 21)); p.ClosePath();
                p.Stroke();
            }
            else
            {
                p.BeginPath(); p.MoveTo(point(3, 19)); p.BezierCurveTo(point(4, 3), point(19, 22), point(21, 5)); p.Stroke();
                if (tool == CanvasTool.VectorBrush) { p.lineWidth = 4; p.BeginPath(); p.MoveTo(point(7, 8)); p.LineTo(point(15, 16)); p.Stroke(); }
                else foreach (var xy in new[] { point(3, 19), point(21, 5) })
                { p.BeginPath(); p.Arc(xy, 2, 0, 360); p.Fill(); }
            }
        }
    }
}
