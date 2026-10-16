using System;
using System.Collections.Generic;
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
        [SerializeField] private float vectorBrushTolerance = .75f;
        private VectorManipulator vectorManipulator;
        private readonly Dictionary<CanvasTool, Button> vectorButtons = new();
        private static bool IsVectorTool(CanvasTool tool) => tool == CanvasTool.VectorPen || tool == CanvasTool.VectorSelect || tool == CanvasTool.VectorNodes || tool == CanvasTool.VectorBrush;
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
            Add(CanvasTool.VectorSelect, "vectorSelectTool", "Vector Select. Select and drag a figure. Corner handles resize; the round handle rotates. Delete removes the figure.");
            Add(CanvasTool.VectorNodes, "vectorNodesTool", "Vector Nodes. Drag points or tangent handles. Shift selects more points; drag empty space to select points. Double-click an edge to insert a point. Alt-click deletes a point; Alt-drag breaks tangent symmetry.");
            Add(CanvasTool.VectorPen, "vectorPenTool", "Vector Pen. Click for corners; drag for curved nodes. Click the first point to close, Enter to finish an open path, Escape to cancel. Creates a Vector layer when needed.");
            Add(CanvasTool.VectorBrush, "vectorBrushTool", "Vector Brush. Paint a mask, release to create an editable vector figure. The last stroke retains its original mask for precision and expansion adjustments in Properties. Escape cancels.");
        }

        private void RefreshVectorTools()
        {
            foreach (var pair in vectorButtons)
            {
                pair.Value.EnableInClassList("whimtex-tool-button--selected", canvasTool == pair.Key);
                pair.Value.EnableInClassList("whimtex-tool-button--unavailable", activeDocument == null);
            }
            vectorManipulator?.Refresh();
        }

        private void AddVectorSettings()
        {
            foreach (CanvasTool tool in new[] { CanvasTool.VectorPen, CanvasTool.VectorSelect, CanvasTool.VectorNodes, CanvasTool.VectorBrush })
            {
                var row = CreateCanvasSettingsRow(); BindCanvasSettingsRow(row, tool);
                if (tool == CanvasTool.VectorPen || tool == CanvasTool.VectorBrush)
                {
                    var color = new WhimTexColorField("Fill") { value = vectorFillColor };
                    color.AddToClassList("whimtex-view-field"); row.Add(color);
                    toolkitHeaderBindings.Track(color, () => vectorFillColor);
                    color.RegisterValueChangedCallback(e => vectorFillColor = e.newValue);
                }
                if (tool == CanvasTool.VectorPen)
                {
                    row.Add(new Button(() => vectorManipulator?.FinishPen(false)) { text = "Finish Open" });
                    row.Add(new Button(() => vectorManipulator?.FinishPen(true)) { text = "Close Path" });
                }
                if (tool == CanvasTool.VectorBrush)
                {
                    var size = new FloatField("Size") { value = vectorBrushSize };
                    size.AddToClassList("whimtex-view-field"); row.Add(size);
                    size.RegisterValueChangedCallback(e => { vectorBrushSize = ShapeLayerBehaviour.Limit(e.newValue, 1, 2048, 24); size.SetValueWithoutNotify(vectorBrushSize); });
                    var precision = new FloatField("Tolerance") { value = vectorBrushTolerance, tooltip = "Bezier fitting tolerance in layer reference pixels. Lower values follow the mask more closely; higher values usually use fewer anchors." };
                    precision.AddToClassList("whimtex-view-field"); row.Add(precision);
                    precision.RegisterValueChangedCallback(e => { vectorBrushTolerance = ShapeLayerBehaviour.Limit(e.newValue, VectorBrushDraft.MinTolerance, VectorBrushDraft.MaxTolerance, .75f); precision.SetValueWithoutNotify(vectorBrushTolerance); });
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
                new VectorLayerBehaviour { referenceSize = vector.referenceSize, edgeMode = vector.edgeMode, shapes = shapes }.Validate();
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
                AddLayer(container, index, layer, "Vector");
            }
            else if (!WhimTexApi.IsLayerContentLocked(activeDocument, layer))
            {
                ExecuteModelChange(action, () =>
                {
                    int index = vector.shapes.FindIndex(s => s.id == shape.id);
                    if (index < 0) vector.shapes.Add(shape); else vector.shapes[index] = shape;
                    if (clearDraft && vector.brushDraft?.shapeId == shape.id) vector.brushDraft = null;
                    if (draft != null) vector.brushDraft = draft;
                });
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
                    // Shape generates outside its frame even with Clip. Preserve those pixels.
                    if (layer.transform.tiling == TransformTilingMode.Clip) layer.transform.tiling = TransformTilingMode.Unbounded;
                });
                SelectVectorShape(converted.shapes.Count > 0 ? converted.shapes[0] : null);
                SetCanvasTool(CanvasTool.VectorNodes);
            }
            catch (Exception error) { ShowNotification(new GUIContent(error.Message)); }
        }

        private static void DrawVectorToolIcon(Painter2D p, CanvasTool tool, Func<float, float, Vector2> point)
        {
            if (tool == CanvasTool.VectorSelect || tool == CanvasTool.VectorNodes)
            {
                p.BeginPath(); p.MoveTo(point(5, 3)); p.LineTo(point(18, 13)); p.LineTo(point(12, 14)); p.LineTo(point(9, 21)); p.ClosePath();
                if (tool == CanvasTool.VectorSelect) p.Fill(); else p.Stroke();
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
