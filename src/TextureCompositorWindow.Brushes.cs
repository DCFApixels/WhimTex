using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [SerializeField] private bool brushesExpanded;
        [NonSerialized] private Button brushTab;
        [NonSerialized] private VisualElement brushDrawer;
        [NonSerialized] private WhimTexUI.ValueBindings brushSettingsBindings;

        private void BuildBrushTab(VisualElement tabs)
        {
            brushTab = new Button(() =>
            {
                brushesExpanded = !brushesExpanded;
                if (brushesExpanded) postFxExpanded = uvExpanded = false;
                RefreshPostFxPanel();
            }) { tooltip = "Brushes: tip, spacing, scatter, size variation, tint and blending." };
            brushTab.AddToClassList("whimtex-post-fx-tab");
            tabs.Add(brushTab);
        }

        private void BuildBrushDrawer(VisualElement panel)
        {
            brushSettingsBindings = new WhimTexUI.ValueBindings();
            brushDrawer = new VisualElement();
            brushDrawer.AddToClassList("whimtex-post-fx-drawer");
            brushDrawer.Add(CreatePaneHeader("Brushes", "brushesTitle"));
            BuildBrushPresetControls(brushDrawer);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("whimtex-post-fx-settings");
            brushDrawer.Add(scroll);
            panel.Add(brushDrawer);

            scroll.Add(CreateBrushSectionHeader("Tip", () => paintSettings.ResetBrushTip(),
                "Reset Tip: use a procedural brush in Hardness mode, clear the texture, use Alpha, disable texture SDF and restore the default gradient. Size and Hardness are unchanged."));
            BuildBrushSourceControls(scroll);
            var size = WhimTexUI.ConfigureField(new FloatField("Size")
            {
                value = paintSettings.brushSize,
                tooltip = "Brush diameter in canvas pixels. Drag the label to adjust. Shared with Size in the preview header."
            });
            size.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() =>
                paintSettings.brushSize = Mathf.Max(1f, evt.newValue)));
            brushSettingsBindings.Track(size, () => paintSettings.brushSize);
            var tip = WhimTexUI.ConfigureField(new ObjectField("Texture")
                { objectType = typeof(Texture2D), allowSceneObjects = false, value = paintSettings.dynamics.tip,
                  tooltip = "No texture: procedural brush. Assign a texture: textured brush. Source texture import settings are not changed." });
            tip.RegisterValueChangedCallback(evt =>
            {
                var texture = evt.newValue as Texture2D;
                if (texture != null && compositor != null && ReferenceEquals(TextureCompositor.FindDocument(texture), compositor))
                {
                    tip.SetValueWithoutNotify(paintSettings.dynamics.tip);
                    ShowNotification(new GUIContent("Choose a texture other than this document's output."));
                    return;
                }
                ApplyPaintToolChange(() => paintSettings.SetBrushTip(texture));
            });
            brushSettingsBindings.Track(tip, () => (UnityEngine.Object)paintSettings.dynamics.tip);
            brushSettingsBindings.Add(() => tip.EnableInClassList("whimtex-brush-setting--hidden", paintSettings.dynamics.source != BrushTipSource.Standard));
            scroll.Add(tip);
            scroll.Add(size);
            var proceduralMode = WhimTexUI.ConfigureField(new DropdownField("Mode",
                new System.Collections.Generic.List<string> { "Hardness", "Gradient" }, (int)paintSettings.dynamics.proceduralMode)
            {
                tooltip = "Procedural brush: Hardness controls a soft circular tip; Gradient maps color and opacity from its center (0) to its outer edge (1)."
            });
            proceduralMode.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() =>
                paintSettings.dynamics.proceduralMode = evt.newValue == "Gradient" ? BrushProceduralMode.SdfGradient : BrushProceduralMode.Hardness));
            brushSettingsBindings.Track(proceduralMode, () => paintSettings.dynamics.proceduralMode == BrushProceduralMode.SdfGradient ? "Gradient" : "Hardness");
            brushSettingsBindings.Add(() => proceduralMode.EnableInClassList("whimtex-brush-setting--hidden", paintSettings.dynamics.tip != null));
            scroll.Add(proceduralMode);
            var channel = WhimTexUI.ConfigureField(new EnumField("Tip Channel", paintSettings.dynamics.tipChannel));
            channel.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.tipChannel = (BrushTipChannel)evt.newValue));
            brushSettingsBindings.Track(channel, () => (Enum)paintSettings.dynamics.tipChannel);
            brushSettingsBindings.Add(() => channel.SetEnabled(paintSettings.dynamics.tip != null));
            scroll.Add(channel);
            var sdf = WhimTexUI.ConfigureField(new Toggle("SDF")
            {
                value = paintSettings.dynamics.tipSdf,
                tooltip = "Map the selected distance field through Gradient. Alpha/Color use alpha; Luminance modes use brightness. Gradient alpha controls coverage and its RGB multiplies the brush color."
            });
            sdf.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.tipSdf = evt.newValue));
            brushSettingsBindings.Track(sdf, () => paintSettings.dynamics.tipSdf);
            brushSettingsBindings.Add(() => sdf.SetEnabled(paintSettings.dynamics.tip != null));
            brushSettingsBindings.Add(() => sdf.EnableInClassList("whimtex-brush-setting--hidden", paintSettings.dynamics.tip == null));
            scroll.Add(sdf);
            var hardness = AddBrushPercent(scroll, "Hardness", () => paintSettings.brushHardness, v => paintSettings.brushHardness = v,
                "Edge hardness of the procedural brush. Textured brushes use their own coverage or Gradient.");
            brushSettingsBindings.Add(() => hardness.SetEnabled(paintSettings.dynamics.tip == null));
            brushSettingsBindings.Add(() => hardness.EnableInClassList("whimtex-brush-setting--hidden", paintSettings.dynamics.UsesSdfGradient));
            var sdfGradient = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new WhimTexGradientValueField("Gradient")
            {
                tooltip = "Left = interior (0), right = outer edge (1). Textured brushes invert the selected distance field. Alpha keys shape coverage; color keys multiply the palette color and Tint."
            }, brushSettingsBindings, () => paintSettings.dynamics.tipGradient));
            sdfGradient.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.tipGradient = evt.newValue));
            brushSettingsBindings.Add(() => sdfGradient.EnableInClassList("whimtex-brush-setting--hidden", !paintSettings.dynamics.UsesSdfGradient));
            scroll.Add(sdfGradient);

            scroll.Add(CreateBrushSectionHeader("Stamps", () => paintSettings.ResetBrushStamps(),
                "Reset Stamps: Random, Fixed rotation, zero Angle Offset, no flips, scatter or size/angle variation. Spacing is unchanged."));
            var spacing = WhimTexUI.ConfigureField(new FloatField("Spacing (%)") { value = paintSettings.brushSpacing * 100f,
                tooltip = "Distance between stamp centers as a percentage of brush diameter. 100% is one diameter. The minimum distance is one pixel." });
            spacing.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() =>
                paintSettings.brushSpacing = Mathf.Clamp(float.IsNaN(evt.newValue) ? 16f : evt.newValue, 1f, 400f) * .01f));
            brushSettingsBindings.Track(spacing, () => paintSettings.brushSpacing * 100f);
            scroll.Add(spacing);
            var distance = new Label();
            distance.AddToClassList("whimtex-brush-spacing-info");
            brushSettingsBindings.Add(() => distance.text = $"Stamp every {Mathf.Max(1f, paintSettings.brushSize * paintSettings.brushSpacing):0.##} px");
            scroll.Add(distance);
            var algorithm = WhimTexUI.ConfigureField(new EnumField("Randomization", paintSettings.dynamics.randomAlgorithm)
            {
                tooltip = "Random uses ordinary randomness. Sobol spreads variation more evenly across stamps. Applies to scatter, size, angle and tint."
            });
            algorithm.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.randomAlgorithm = (BrushRandomAlgorithm)evt.newValue));
            brushSettingsBindings.Track(algorithm, () => (Enum)paintSettings.dynamics.randomAlgorithm);
            scroll.Add(algorithm);
            AddBrushPercent(scroll, "Scatter", () => paintSettings.dynamics.scatter, v => paintSettings.dynamics.scatter = v,
                "Random offset within a disk, measured in brush diameters. 0 keeps every stamp on the stroke.", 400f);
            var scatterBias = WhimTexUI.ConfigureField(new Slider("Scatter Bias", -100f, 100f)
            {
                value = paintSettings.dynamics.scatterBias * 100f, showInputField = true,
                tooltip = "Negative: concentrate stamp centers near the stroke. 0: uniform across the disk. Positive: concentrate near the outer edge. Scatter sets the maximum distance."
            });
            scatterBias.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.scatterBias = evt.newValue * .01f));
            brushSettingsBindings.Track(scatterBias, () => paintSettings.dynamics.scatterBias * 100f);
            brushSettingsBindings.Add(() => scatterBias.SetEnabled(paintSettings.dynamics.scatter > 0f));
            scroll.Add(scatterBias);
            AddBrushPercent(scroll, "Size Jitter", () => paintSettings.dynamics.sizeJitter, v => paintSettings.dynamics.sizeJitter = v,
                "Random size around the chosen diameter. 50% produces sizes from 50% to 150%.");
            var rotation = WhimTexUI.ConfigureField(new EnumField("Rotation", paintSettings.dynamics.rotationMode)
            {
                tooltip = "Fixed keeps the texture's original orientation. Stroke Direction aligns its right-facing axis with the stroke. The first click uses the original orientation. Angle Offset and then Angle Jitter are added afterwards."
            });
            rotation.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.rotationMode = (BrushRotationMode)evt.newValue));
            brushSettingsBindings.Track(rotation, () => (Enum)paintSettings.dynamics.rotationMode);
            brushSettingsBindings.Add(() => rotation.SetEnabled(paintSettings.dynamics.tip != null));
            scroll.Add(rotation);
            var offset = WhimTexUI.ConfigureField(new Slider("Angle Offset (°)", -180f, 180f)
            {
                value = paintSettings.dynamics.angleOffset, showInputField = true,
                tooltip = "Constant angle added after Rotation and before Angle Jitter. 90° turns the tip across the stroke in Stroke Direction mode. Requires a texture tip."
            });
            offset.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.angleOffset = evt.newValue));
            brushSettingsBindings.Track(offset, () => paintSettings.dynamics.angleOffset);
            brushSettingsBindings.Add(() => offset.SetEnabled(paintSettings.dynamics.tip != null));
            scroll.Add(offset);
            var angle = WhimTexUI.ConfigureField(new Slider("Angle Jitter (°)", 0f, 180f)
            {
                value = paintSettings.dynamics.angleJitter, showInputField = true,
                tooltip = "Random offset added after Rotation and Angle Offset, from minus to plus this angle. 180° covers all directions. A procedural brush is unchanged by rotation."
            });
            angle.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.angleJitter = evt.newValue));
            brushSettingsBindings.Track(angle, () => paintSettings.dynamics.angleJitter);
            brushSettingsBindings.Add(() => angle.SetEnabled(paintSettings.dynamics.tip != null));
            scroll.Add(angle);

            var flipX = WhimTexUI.ConfigureField(new Slider("Flip X", 0f, 1f)
            {
                value = paintSettings.dynamics.flipX, showInputField = true,
                tooltip = "Chance to mirror each texture stamp horizontally in the tip's local axes: 0 never, 0.5 half, 1 always. Uses Randomization."
            });
            flipX.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.flipX = evt.newValue));
            brushSettingsBindings.Track(flipX, () => paintSettings.dynamics.flipX);
            brushSettingsBindings.Add(() => flipX.SetEnabled(paintSettings.dynamics.tip != null));
            scroll.Add(flipX);
            var flipY = WhimTexUI.ConfigureField(new Slider("Flip Y", 0f, 1f)
            {
                value = paintSettings.dynamics.flipY, showInputField = true,
                tooltip = "Chance to mirror each texture stamp vertically in the tip's local axes: 0 never, 0.5 half, 1 always. Uses Randomization."
            });
            flipY.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.flipY = evt.newValue));
            brushSettingsBindings.Track(flipY, () => paintSettings.dynamics.flipY);
            brushSettingsBindings.Add(() => flipY.SetEnabled(paintSettings.dynamics.tip != null));
            scroll.Add(flipY);

            scroll.Add(CreateBrushSectionHeader("Color", () => paintSettings.ResetBrushColor(),
                "Reset Color: opaque white Tint, Normal blending applied per stroke. Palette colors, Opacity and Flow are unchanged."));
            AddBrushPercent(scroll, "Opacity", () => paintSettings.dynamics.opacity, v => paintSettings.dynamics.opacity = v,
                "Maximum strength of one stroke. Release and start a new stroke to build up further. Shared with the preview header.");
            AddBrushPercent(scroll, "Flow", () => paintSettings.dynamics.flow, v => paintSettings.dynamics.flow = v,
                "Strength of each stamp. Overlapping stamps build up within the stroke. Shared with the preview header.");
            var tintRow = new VisualElement();
            tintRow.AddToClassList("whimtex-brush-tint-row");
            var gradient = WhimTexUI.ConfigureField(new WhimTexGradientValueField("Tint") { value = paintSettings.dynamics.tintGradient,
                tooltip = "Different color or alpha keys give each stamp a random tint. Identical keys give one tint. Opaque white leaves the palette color unchanged." });
            gradient.AddToClassList("whimtex-brush-tint-gradient");
            gradient.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.tintGradient = evt.newValue));
            brushSettingsBindings.Track(gradient, () => paintSettings.dynamics.tintGradient);
            tintRow.Add(gradient);
            var resetTint = new Button(() =>
            {
                ApplyPaintToolChange(() => paintSettings.dynamics.ResetTint());
                gradient.SetValueWithoutNotify(paintSettings.dynamics.tintGradient);
            }) { text = "↺", tooltip = "Reset Tint to opaque white." };
            resetTint.AddToClassList("whimtex-brush-tint-reset");
            tintRow.Add(resetTint);
            scroll.Add(tintRow);
            var modes = new System.Collections.Generic.List<string>();
            foreach (BlendMode mode in Enum.GetValues(typeof(BlendMode)))
                if (mode != BlendMode.Overwrite && mode != BlendMode.None) modes.Add(mode.ToString());
            var blend = WhimTexUI.ConfigureField(new DropdownField(modes, modes.IndexOf(paintSettings.dynamics.blend.ToString())) { label = "Blend" });
            blend.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.blend = (BlendMode)Enum.Parse(typeof(BlendMode), evt.newValue)));
            brushSettingsBindings.Track(blend, () => paintSettings.dynamics.blend.ToString());
            brushSettingsBindings.Add(() => blend.SetEnabled(paintSettings.tool != PaintToolMode.Eraser));
            scroll.Add(blend);
            var application = WhimTexUI.ConfigureField(new DropdownField("Apply Blend",
                new System.Collections.Generic.List<string> { "Per Stroke", "Per Stamp" }, (int)paintSettings.dynamics.blendApplication)
            {
                tooltip = "Per Stroke blends the accumulated stroke with the layer once. Per Stamp blends each stamp with the evolving layer, including earlier stamps. Opacity controls the whole result; Flow controls each stamp. Dense spacing costs more in Per Stamp mode."
            });
            application.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.blendApplication =
                evt.newValue == "Per Stamp" ? BrushBlendApplication.Stamp : BrushBlendApplication.Stroke));
            brushSettingsBindings.Track(application, () => paintSettings.dynamics.blendApplication == BrushBlendApplication.Stamp ? "Per Stamp" : "Per Stroke");
            brushSettingsBindings.Add(() => application.SetEnabled(paintSettings.tool != PaintToolMode.Eraser));
            scroll.Add(application);
            BuildBrushStrokePreview(brushDrawer);
            brushSettingsBindings.Refresh(true);
        }

        private VisualElement CreateBrushSectionHeader(string title, Action reset, string tooltip)
        {
            var header = new VisualElement();
            header.AddToClassList("whimtex-brush-section-header");
            var label = new Label(title);
            label.AddToClassList("whimtex-brush-section-title");
            header.Add(label);
            var button = new Button(() =>
            {
                ApplyPaintToolChange(reset);
                brushSettingsBindings?.Refresh(true);
            }) { text = "↺", tooltip = tooltip };
            button.AddToClassList("whimtex-brush-section-reset");
            header.Add(button);
            return header;
        }

        private Slider AddBrushPercent(VisualElement root, string label, Func<float> read, Action<float> write, string tooltip, float max = 100f)
        {
            var field = WhimTexUI.ConfigureField(new Slider(label, 0f, max)
                { value = read() * 100f, showInputField = true, tooltip = tooltip });
            field.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => write(evt.newValue * .01f)));
            brushSettingsBindings.Track(field, () => read() * 100f);
            root.Add(field);
            return field;
        }

        private void AddBrushEdgeHeader(VisualElement row)
        {
            var edge = new VisualElement();
            edge.AddToClassList("whimtex-brush-edge");
            var hardness = new FloatField("Hardness")
            {
                tooltip = "Hardness (%). Drag the label or enter 0–100. Textured brushes use their own coverage or Gradient."
            };
            hardness.AddToClassList("whimtex-brush-edge-field");
            toolkitHeaderBindings.Track(hardness, () => paintSettings.brushHardness * 100f);
            hardness.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() =>
                paintSettings.brushHardness = Mathf.Clamp01((float.IsNaN(evt.newValue) ? 80f : evt.newValue) * .01f)));
            edge.Add(hardness);

            var gradient = WhimTexColorInputs.Bind(new WhimTexGradientValueField("Gradient")
            {
                tooltip = "Gradient. Click to edit. Left = interior (0), right = outer edge (1). Alpha shapes coverage; RGB multiplies the brush color and Tint."
            }, toolkitHeaderBindings, () => paintSettings.dynamics.tipGradient);
            gradient.AddToClassList("whimtex-brush-edge-field");
            gradient.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => paintSettings.dynamics.tipGradient = evt.newValue));
            edge.Add(gradient);

            var mode = new Button(() =>
            {
                if (paintSettings.dynamics.tip != null) return;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Hardness"), paintSettings.dynamics.proceduralMode == BrushProceduralMode.Hardness,
                    () => ApplyPaintToolChange(() => paintSettings.dynamics.proceduralMode = BrushProceduralMode.Hardness));
                menu.AddItem(new GUIContent("Gradient"), paintSettings.dynamics.proceduralMode == BrushProceduralMode.SdfGradient,
                    () => ApplyPaintToolChange(() => paintSettings.dynamics.proceduralMode = BrushProceduralMode.SdfGradient));
                menu.ShowAsContext();
            }) { text = "▾", tooltip = "Procedural brush edge: Hardness or Gradient. Both settings are preserved when switching. For a textured brush, toggle SDF in Tip settings." };
            mode.AddToClassList("whimtex-brush-edge-mode");
            edge.Add(mode);
            toolkitHeaderBindings.Add(() =>
            {
                bool sdf = paintSettings.dynamics.UsesSdfGradient;
                hardness.EnableInClassList("whimtex-brush-setting--hidden", sdf);
                gradient.EnableInClassList("whimtex-brush-setting--hidden", !sdf);
                hardness.SetEnabled(paintSettings.dynamics.tip == null);
                mode.SetEnabled(paintSettings.dynamics.tip == null);
            });
            row.Add(edge);
        }

        private void AddBrushHeaderPercent(VisualElement row, string label, Func<float> read, Action<float> write, string tooltip)
        {
            var field = CompactField(new FloatField(label) { value = read() * 100f, tooltip = tooltip }, 94f);
            field.AddToClassList("whimtex-brush-strength");
            toolkitHeaderBindings.Track(field, () => read() * 100f);
            field.RegisterValueChangedCallback(evt => ApplyPaintToolChange(() => write(
                Mathf.Clamp01((float.IsNaN(evt.newValue) ? 100f : evt.newValue) * .01f))));
            row.Add(field);
        }
    }
}
