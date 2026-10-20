using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private void BuildBooleanProperties(VisualElement root, Layer layer, BooleanGroupLayerBehaviour group, Action<string, Action> apply)
        {
            var mode = WhimTexUI.ConfigureField(new StrokeStyleField<VectorBooleanOperation>("Mode",
                (VectorBooleanOperation.Combine, LayerActionIcon.Kind.BooleanCombine, "Combine\nKeep separate figures, drawn bottom to top, without merging their contours."),
                (VectorBooleanOperation.Union, LayerActionIcon.Kind.BooleanUnion, "Union\nCombine the filled areas of all direct children."),
                (VectorBooleanOperation.Subtract, LayerActionIcon.Kind.BooleanSubtract, "Subtract\nCut the upper children out of the bottom enabled child."),
                (VectorBooleanOperation.Intersect, LayerActionIcon.Kind.BooleanIntersect, "Intersect\nKeep only the area shared by the enabled children."),
                (VectorBooleanOperation.Xor, LayerActionIcon.Kind.BooleanXor, "Xor\nExclude overlapping areas. With more than two children, keep odd coverage.")));
            mode.name = "booleanGroupMode";
            root.Add(mode);
            var mixed = new Label("Mixed") { name = "booleanGroupModeMixed", pickingMode = PickingMode.Ignore,
                tooltip = "Children use different operations. Choose an icon to apply one operation to all direct children." };
            mode.Q<VisualElement>(className: BaseField<VectorBooleanOperation>.inputUssClassName).Add(mixed);
            toolkitInspectorBindings.Track(mode, () => BooleanGroupLayerBehaviour.ReadMode(layer));
            toolkitInspectorBindings.Add(() =>
            {
                mixed.EnableInClassList("whimtex-hidden", layer.children.Count == 0 || (int)BooleanGroupLayerBehaviour.ReadMode(layer) >= 0);
                mode.SetEnabled(CanChangeBooleanGroupMode(layer));
            });
            mode.RegisterValueChangedCallback(e =>
            {
                if ((uint)e.newValue > (uint)VectorBooleanOperation.Combine || !CanChangeBooleanGroupMode(layer)) return;
                apply("Change Boolean Group Mode", () =>
                {
                    foreach (var child in layer.children) child.booleanOperation = e.newValue;
                });
            });
            var source = WhimTexUI.ConfigureField(new EnumField("Appearance", group.appearance) { name = "booleanGroupAppearance",
                tooltip = "Combine only: use the group's Fill and Stroke, or keep each layer's own appearance. Other Boolean modes always use the group style. Child FX remain ignored." });
            root.Add(source); toolkitInspectorBindings.Track(source, () => (Enum)group.appearance);
            source.RegisterValueChangedCallback(e => apply("Change Boolean Group Appearance", () => group.appearance = (BooleanGroupAppearance)e.newValue));
            toolkitInspectorBindings.Add(() => source.SetEnabled(BooleanGroupLayerBehaviour.ReadMode(layer) == VectorBooleanOperation.Combine));
            WhimTexUI.AddHelpBox(root, "Bottom child is the base. Combine keeps figures separate; the other modes resolve one region. Group FX are applied after the result.", HelpBoxMessageType.Info);
            var edges = WhimTexUI.ConfigureField(new EnumField("Edge Mode", group.edgeMode)); root.Add(edges);
            toolkitInspectorBindings.Track(edges, () => (Enum)group.edgeMode);
            edges.RegisterValueChangedCallback(e => apply("Change Boolean Edge Mode", () => group.edgeMode = (VectorEdgeMode)e.newValue));
            var appearance = new VisualElement(); root.Add(appearance);
            BuildVectorStyle(appearance, group.style, apply, toolkitInspectorBindings);
            toolkitInspectorBindings.Add(() =>
            {
                bool ownStyle = !(group.appearance == BooleanGroupAppearance.Layers && BooleanGroupLayerBehaviour.ReadMode(layer) == VectorBooleanOperation.Combine);
                appearance.SetEnabled(ownStyle && !activeDocument.IsBooleanStyleSuppressed(layer));
                edges.SetEnabled(ownStyle);
            });
        }

        private bool CanChangeBooleanGroupMode(Layer layer)
        {
            if (layer.children.Count == 0) return false;
            foreach (var child in layer.children)
                if (WhimTexApi.IsLayerContentLocked(activeDocument, child)) return false;
            return true;
        }

        private void GroupBooleanLayers(List<Layer> selected, VectorBooleanOperation operation)
        {
            if (activeDocument == null || selected.Count == 0) return;
            try
            {
                BooleanGroupLayerBehaviour.ValidateOperands(selected, new HashSet<Layer>(), 0);
                if (!activeDocument.TryFindLayer(selected[0], out var container, out int index)) return;
                foreach (var child in selected)
                    if (!activeDocument.TryFindLayer(child, out var source, out _) || !ReferenceEquals(container, source))
                        throw new InvalidOperationException("Select operands from the same group before creating a Boolean Group.");
                foreach (var child in selected)
                    if (WhimTexApi.IsLayerContentLocked(activeDocument, child)) return;
                FinishCanvasTransform(); FinishPaintingStroke(); vectorManipulator?.Cancel();
                var basis = selected[selected.Count - 1];
                var model = new BooleanGroupLayerBehaviour { referenceSize = new Vector2(activeDocument.width, activeDocument.height), style = CopyBooleanStyle(basis) };
                var group = new Layer(model);
                ExecuteContextChange("Create Boolean Group", () =>
                {
                    group.layerName = activeDocument.AllocateLayerName(group);
                    foreach (var child in selected) { container.Remove(child); child.booleanOperation = operation; }
                    group.children.AddRange(selected);
                    container.Insert(Mathf.Min(index, container.Count), group);
                    activeDocument.NormalizeModel(); SelectOnlyLayer(group.Id); groupExpansion[group.Id] = true;
                });
            }
            catch (Exception error) { ShowNotification(new GUIContent(error.Message)); }
        }

        private static VectorShape CopyBooleanStyle(Layer layer)
        {
            VectorShape result;
            if (layer.Behaviour is VectorLayerBehaviour vector && vector.shapes.Count > 0) result = vector.shapes[0].Clone(false);
            else if (layer.Behaviour is BooleanGroupLayerBehaviour group) result = group.style.Clone(false);
            else if (layer.Behaviour is TextLayerBehaviour text) result = new VectorShape { fillColor = text.color };
            else if (layer.Behaviour is ShapeLayerBehaviour shape)
                result = new VectorShape { fillMode = shape.fill ? shape.fillMode : VectorFillMode.None, fillColor = shape.fillColor,
                    gradient = shape.gradient?.Clone(), gradientStart = shape.gradientStart, gradientEnd = shape.gradientEnd,
                    gradientRepetitions = shape.gradientRepetitions,
                    strokeMode = shape.strokeMode, strokeGradient = shape.strokeGradient?.Clone(),
                    strokeGradientStart = shape.strokeGradientStart, strokeGradientEnd = shape.strokeGradientEnd,
                    strokeGradientRepetitions = shape.strokeGradientRepetitions,
                    freeform = shape.freeform?.Clone(), strokeFreeform = shape.strokeFreeform?.Clone(),
                    stroke = shape.stroke, strokeColor = shape.strokeColor, strokeWidth = shape.strokeWidth,
                    strokePosition = shape.strokePosition == ShapeLayerBehaviour.StrokePosition.Inside ? VectorStrokePosition.Inside :
                        shape.strokePosition == ShapeLayerBehaviour.StrokePosition.Outside ? VectorStrokePosition.Outside : VectorStrokePosition.Center,
                    join = shape.strokeJoin, cap = shape.strokeCap, miterLimit = shape.miterLimit,
                    dashPattern = shape.dashPattern == null ? new List<float>() : new List<float>(shape.dashPattern),
                    dashPatternRelative = true, dashOffset = shape.dashOffset };
            else result = new VectorShape();
            result.contours.Clear(); result.visible = true; return result;
        }
    }
}
