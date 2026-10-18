using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private void BuildVectorProperties(VisualElement root, Layer layer, VectorLayerBehaviour vector, Action<string, Action> apply)
        {
            var bindings = toolkitInspectorBindings;
            VectorShape selected = SelectedVectorShape(vector);
            void Rebuild() { ResetToolkitLayerInspector(); toolkitRefreshRequested = true; vectorManipulator?.Refresh(); }
            void Change(string title, Action action) => apply(title, action);
            void EnumControl<T>(VisualElement parent, string label, Func<T> read, Action<T> write) where T : Enum
            {
                var field = WhimTexUI.ConfigureField(new EnumField(label, read())); parent.Add(field);
                bindings.Track(field, () => (Enum)read());
                field.RegisterValueChangedCallback(e => Change("Change Vector " + label, () => write((T)e.newValue)));
            }
            EnumControl(root, "Edge Mode", () => vector.edgeMode, value => vector.edgeMode = value);
            var featherSettings = new VisualElement(); root.Add(featherSettings);
            var feather = WhimTexUI.ConfigureField(new FloatField("Feather (px)") { name = "vectorFeather",
                tooltip = "Soften fill and stroke boundaries in layer reference pixels. Scales with the figure; does not blur its colors." });
            featherSettings.Add(feather);
            bindings.Track(feather, () => vector.feather);
            feather.RegisterValueChangedCallback(e => Change("Change Vector Feather", () =>
                vector.feather = ShapeLayerBehaviour.Limit(e.newValue, 0, 8192, vector.feather)));
            EnumControl(featherSettings, "Feather Position", () => vector.featherPosition, value => vector.featherPosition = value);
            bindings.Add(() => featherSettings.SetEnabled(vector.edgeMode != VectorEdgeMode.Step && !activeDocument.IsBooleanStyleSuppressed(layer)));
            var tools = new VisualElement(); tools.AddToClassList("whimtex-vector-actions"); root.Add(tools);
            tools.Add(new Button(() => SetCanvasTool(CanvasTool.VectorPen)) { text = "Pen" });
            tools.Add(new Button(() => SetCanvasTool(CanvasTool.Transform)) { text = "Transform" });
            tools.Add(new Button(() => SetCanvasTool(CanvasTool.VectorNodes)) { text = "Nodes" });
            tools.Add(new Button(() => SetCanvasTool(CanvasTool.VectorBrush)) { text = "Brush" });

            if (selected == null)
            {
                WhimTexUI.AddHelpBox(root, "Select a figure or draw one with Pen or Vector Brush.", HelpBoxMessageType.Info);
                return;
            }
            EnumControl(root, "Fill Rule", () => selected.fillRule, value => selected.fillRule = value);
            var appearance = new VisualElement(); root.Add(appearance);
            BuildVectorStyle(appearance, selected, Change, bindings);
            bindings.Add(() => appearance.SetEnabled(!activeDocument.IsBooleanStyleSuppressed(layer)));

            var draft = vector.brushDraft;
            if (draft?.shapeId != selected.id) return;
            var brush = new Foldout { text = "Original Mask", value = true }; root.Add(brush);
            brush.Add(new Label($"{draft.width} × {draft.height} original mask for this figure."));
            AddVectorMaskEditingButton(brush, () => layer, bindings, "vectorPropertiesEditMask");
            var rebuildStatus = new VisualElement(); rebuildStatus.AddToClassList("whimtex-vector-actions"); brush.Add(rebuildStatus);
            var status = new Label(); rebuildStatus.Add(status);
            bindings.Add(() => status.text = IsVectorMaskRebuilding ? "Rebuilding mask…" : "");
            var cancelRebuild = new Button(() => CancelVectorMaskRebuild()) { text = "Cancel Rebuild" }; rebuildStatus.Add(cancelRebuild);
            bindings.Add(() => cancelRebuild.SetEnabled(IsVectorMaskRebuilding));
            void RebuildDraft(float tolerance, float expansion)
            {
                if (IsVectorMaskRebuilding) { bindings.Refresh(true); return; }
                try
                {
                    vectorManipulator?.Cancel();
                    var next = draft.DeepClone(); next.tolerance = tolerance; next.expansion = expansion;
                    var referenceSize = vector.referenceSize;
                    string originalJson = JsonUtility.ToJson(selected);
                    float oldTolerance = draft.tolerance, oldExpansion = draft.expansion;
                    int oldWidth = draft.width, oldHeight = draft.height;
                    var oldFrame = draft.frame; var oldCoverage = draft.coverage;
                    BeginVectorMaskRebuild(next, referenceSize, layer, "vector.brush_rebuild",
                        () => layer.Behaviour == vector && vector.brushDraft == draft && vector.shapes.Contains(selected) &&
                            vector.referenceSize == referenceSize && draft.frame == oldFrame && draft.tolerance == oldTolerance &&
                            draft.expansion == oldExpansion && draft.width == oldWidth && draft.height == oldHeight &&
                            ReferenceEquals(draft.coverage, oldCoverage) &&
                            !WhimTexApi.IsLayerContentLocked(activeDocument, layer) && JsonUtility.ToJson(selected) == originalJson,
                        geometry => Change("Adjust Vector Brush Figure", () =>
                        {
                            selected.contours = geometry.contours; draft.tolerance = tolerance; draft.expansion = expansion;
                            vectorManipulator?.Cancel();
                        }), () => bindings.Refresh(true));
                }
                catch (Exception error) { ShowNotification(new GUIContent(error.Message)); bindings.Refresh(true); }
            }
            var tolerance = WhimTexUI.ConfigureField(new FloatField("Tolerance (px)") { isDelayed = true,
                tooltip = "Refit Bezier curves from the original mask. Lower values retain more detail; higher values usually use fewer anchors. Measured in layer reference pixels." }); brush.Add(tolerance);
            bindings.Track(tolerance, () => draft.tolerance);
            bindings.Add(() => tolerance.SetEnabled(!IsVectorMaskRebuilding));
            tolerance.RegisterValueChangedCallback(e => RebuildDraft(ShapeLayerBehaviour.Limit(e.newValue, VectorBrushDraft.MinTolerance, VectorBrushDraft.MaxTolerance, draft.tolerance), draft.expansion));
            var expansion = WhimTexUI.ConfigureField(new FloatField("Expand (px)") { isDelayed = true }); brush.Add(expansion);
            bindings.Track(expansion, () => draft.expansion);
            bindings.Add(() => expansion.SetEnabled(!IsVectorMaskRebuilding));
            expansion.RegisterValueChangedCallback(e => RebuildDraft(draft.tolerance, ShapeLayerBehaviour.Limit(e.newValue, -VectorBrushDraft.MaxExpansion, VectorBrushDraft.MaxExpansion, draft.expansion)));
            brush.Add(new Button(() => Change("Finalize Vector Brush Figure", () => { vector.brushDraft = null; Rebuild(); })) { text = "Release Mask", tooltip = "Keep the editable vector figure and discard only its procedural source mask." });
        }

        private static void BuildVectorStyle(VisualElement root, VectorShape shape, Action<string, Action> apply, WhimTexUI.ValueBindings bindings)
        {
            var fill = new Foldout { text = "Fill", value = true }; root.Add(fill);
            fill.Add(new FigureFillEditor(shape, apply, bindings));
            var stroke = new Foldout { text = "Stroke", value = true }; root.Add(stroke);
            var enabled = WhimTexUI.ConfigureField(new Toggle("Enabled")); stroke.Add(enabled); bindings.Track(enabled, () => shape.stroke);
            enabled.RegisterValueChangedCallback(e => apply("Toggle Stroke", () => shape.stroke = e.newValue));
            var options = new VisualElement(); stroke.Add(options); bindings.Add(() => options.SetEnabled(shape.stroke));
            options.Add(new FigureFillEditor(shape, apply, bindings, stroke: true));
            var width = WhimTexUI.ConfigureField(new FloatField("Width (px)") { isDelayed = true }); options.Add(width);
            bindings.Track(width, () => shape.strokeWidth);
            width.RegisterValueChangedCallback(e => apply("Change Stroke Width", () =>
            { shape.MakeDashPatternRelative(); shape.strokeWidth = ShapeLayerBehaviour.Limit(e.newValue, 0, 8192, shape.strokeWidth); }));
            options.Add(new StrokeStyleEditor(() => StrokeStyle.From(shape), value =>
            {
                shape.strokePosition = value.strokePosition; shape.join = value.join; shape.cap = value.cap;
                shape.miterLimit = value.miterLimit; shape.dashOffset = value.dashOffset;
            }, new StrokeDashEditor(shape, apply), apply, bindings));
        }
    }
}
