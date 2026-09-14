using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class DrawingLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(DrawingLayerBehaviour);

        public static void Open(DrawingLayerBehaviour layer, TextureCompositor compositor)
        {
            OpenPropertiesWindow<DrawingLayerEditorWindow>(layer, compositor);
        }

        protected override void BuildSettings(VisualElement root, Layer source)
        {
            BuildFields(root, (DrawingLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings);
        }

        internal static void BuildFields(
            VisualElement root, DrawingLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings)
        {
            WhimTexUI.ApplyWindowStyles(root);
            root.Add(WhimTexUI.CreateHeading("Symmetry & Repeat"));

            PopupField<PaintRepeatMode> mode = WhimTexUI.ConfigureField(new PopupField<PaintRepeatMode>(
                "Mode", new List<PaintRepeatMode>
                {
                    PaintRepeatMode.None, PaintRepeatMode.Mirror, PaintRepeatMode.Horizontal,
                    PaintRepeatMode.Vertical, PaintRepeatMode.Grid, PaintRepeatMode.Radial
                }, layer.repeatMode));
            bindings.Track(mode, () => layer.repeatMode);
            mode.RegisterValueChangedCallback(evt => applyChange("Change Drawing Pattern Mode", () =>
            {
                layer.repeatMode = evt.newValue;
                if (layer.UsesMirrorPattern && !layer.mirrorAcrossVerticalAxis && !layer.mirrorAcrossHorizontalAxis)
                    layer.mirrorAcrossVerticalAxis = true;
            }));
            root.Add(mode);

            Toggle mirrorX = WhimTexUI.ConfigureField(new Toggle("Mirror X"));
            mirrorX.tooltip = "Reflect across the vertical axis through Center, rotated by Angle.";
            bindings.Track(mirrorX, () => layer.mirrorAcrossVerticalAxis);
            mirrorX.RegisterValueChangedCallback(evt => applyChange(
                "Change Drawing Symmetry", () => layer.mirrorAcrossVerticalAxis = evt.newValue));
            root.Add(mirrorX);

            Toggle mirrorY = WhimTexUI.ConfigureField(new Toggle("Mirror Y"));
            mirrorY.tooltip = "Reflect across the horizontal axis through Center, rotated by Angle.";
            bindings.Track(mirrorY, () => layer.mirrorAcrossHorizontalAxis);
            mirrorY.RegisterValueChangedCallback(evt => applyChange(
                "Change Drawing Symmetry", () => layer.mirrorAcrossHorizontalAxis = evt.newValue));
            root.Add(mirrorY);

            Vector2Field center = WhimTexUI.ConfigureField(new Vector2Field("Center"));
            center.tooltip = "Center of symmetry and repetition in layer coordinates (0–1).";
            bindings.Track(center, () => layer.patternCenter);
            center.RegisterValueChangedCallback(evt => applyChange(
                "Change Pattern Center", () => layer.patternCenter = new Vector2(
                    Mathf.Clamp01(evt.newValue.x), Mathf.Clamp01(evt.newValue.y))));
            root.Add(center);

            Slider mirrorAngle = WhimTexUI.ConfigureField(new Slider("Angle (°)", 0f, 360f)
            {
                showInputField = true,
                tooltip = "Rotate both mirror axes counterclockwise around Center. 0° keeps the vertical and horizontal axes."
            });
            bindings.Track(mirrorAngle, () => layer.mirrorAngle);
            mirrorAngle.RegisterValueChangedCallback(evt => applyChange(
                "Change Mirror Angle", () => layer.mirrorAngle = Mathf.Clamp(evt.newValue, 0f, 360f)));
            root.Add(mirrorAngle);

            Slider startAngle = WhimTexUI.ConfigureField(new Slider("Start Angle (°)", 0f, 360f)
            {
                showInputField = true,
                tooltip = "Rotate radial sector boundaries counterclockwise. 0° starts to the left and preserves the original layout."
            });
            bindings.Track(startAngle, () => layer.radialStartAngle);
            startAngle.RegisterValueChangedCallback(evt => applyChange(
                "Change Radial Start Angle", () => layer.radialStartAngle = Mathf.Clamp(evt.newValue, 0f, 360f)));
            root.Add(startAngle);

            IntegerField count = WhimTexUI.ConfigureField(new IntegerField("Count"));
            bindings.Track(count, () => layer.repeatCount);
            count.RegisterValueChangedCallback(evt => applyChange(
                "Change Repeat Count", () => layer.repeatCount = Mathf.Clamp(evt.newValue, 2, 64)));
            root.Add(count);

            IntegerField countY = WhimTexUI.ConfigureField(new IntegerField("Count Y"));
            bindings.Track(countY, () => layer.repeatSecondaryCount);
            countY.RegisterValueChangedCallback(evt => applyChange(
                "Change Repeat Count", () => layer.repeatSecondaryCount = Mathf.Clamp(evt.newValue, 2, 64)));
            root.Add(countY);

            EnumField elementMode = WhimTexUI.ConfigureField(new EnumField("Elements", layer.repeatElementMode));
            bindings.Track(elementMode, () => (Enum)layer.repeatElementMode);
            elementMode.RegisterValueChangedCallback(evt => applyChange(
                "Change Repeat Element Mode", () => layer.repeatElementMode = (PaintRepeatElementMode)evt.newValue));
            root.Add(elementMode);

            EnumField boundary = WhimTexUI.ConfigureField(new EnumField("Edges", layer.repeatBoundaryMode));
            boundary.tooltip = "Continue lets a stroke cross symmetry or repeat boundaries. " +
                "Clip keeps the stroke inside its initial region and clips each copy's brush footprint to its region.";
            bindings.Track(boundary, () => (Enum)layer.repeatBoundaryMode);
            boundary.RegisterValueChangedCallback(evt => applyChange(
                "Change Repeat Boundary", () => layer.repeatBoundaryMode = (PaintRepeatBoundaryMode)evt.newValue));
            root.Add(boundary);

            bindings.Add(() =>
            {
                bool repeating = layer.UsesRepeatedPattern;
                bool grid = layer.repeatMode == PaintRepeatMode.Grid;
                mirrorX.EnableInClassList("whimtex-pattern-field--hidden", !layer.UsesMirrorPattern);
                mirrorY.EnableInClassList("whimtex-pattern-field--hidden", !layer.UsesMirrorPattern);
                mirrorAngle.EnableInClassList("whimtex-pattern-field--hidden", !layer.UsesMirrorPattern);
                center.EnableInClassList("whimtex-pattern-field--hidden",
                    !layer.UsesMirrorPattern && layer.repeatMode != PaintRepeatMode.Radial);
                startAngle.EnableInClassList("whimtex-pattern-field--hidden", layer.repeatMode != PaintRepeatMode.Radial);
                count.EnableInClassList("whimtex-pattern-field--hidden", !repeating);
                count.label = grid ? "Count X" : "Count";
                countY.EnableInClassList("whimtex-pattern-field--hidden", !grid);
                elementMode.EnableInClassList("whimtex-pattern-field--hidden", !repeating);
                boundary.EnableInClassList("whimtex-pattern-field--hidden", !repeating && !layer.UsesMirrorPattern);
            });
        }
    }
}
