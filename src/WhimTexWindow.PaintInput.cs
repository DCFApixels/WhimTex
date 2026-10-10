using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [NonSerialized] private StrokeSmoother paintingSmoother;
        [NonSerialized] private bool paintingSmootherActive;

        private void BeginPaintingInput(Vector2 sourceUv)
        {
            var size = new Vector2(activeDocument.width, activeDocument.height);
            paintingSmoother.Begin(Vector2.Scale(paintingLayer.Owner.PixelCanvasTransform.Map(sourceUv, size), size), paintingPressure);
            paintingSmootherActive = true;
        }

        private void PaintFilteredLayerPoint(Vector2 sourceUv, bool exact, bool finish = false)
        {
            if (!paintingSmootherActive || !hasLastPaintingUv) BeginPaintingInput(sourceUv);
            var size = new Vector2(activeDocument.width, activeDocument.height);
            var transform = paintingLayer.Owner.PixelCanvasTransform;
            paintingSmoother.Move(Vector2.Scale(transform.Map(sourceUv, size), size), paintingPressure, paintSettings.smoothing, exact);
            paintingPressure = paintingSmoother.Pressure;
            Vector2 FilteredUv() => transform.Unmap(paintingSmoother.Position / size, size);
            Vector2 point = FilteredUv();
            if (float.IsFinite(point.x) && float.IsFinite(point.y)) PaintTowardsLayerPoint(point);
            if (!finish || !paintSettings.smoothing.finishStroke || exact || !hasLastPaintingUv) return;
            paintingSmoother.Finish(true);
            point = FilteredUv();
            if (float.IsFinite(point.x) && float.IsFinite(point.y)) PaintTowardsLayerPoint(point);
        }

        private void AddPaintInputSettings(VisualElement row, bool path = true, bool usesPressure = true)
        {
            var mode = CompactField(new EnumField("Smoothing", paintSettings.smoothing.mode), 100f);
            mode.name = "strokeSmoothing";
            mode.tooltip = "None follows the pointer. Smooth filters small deviations. Stabilizer follows behind a fixed-length rope. These affect the path, not image sharpness; Shift draws exact lines.";
            toolkitHeaderBindings.Track(mode, () => (Enum)paintSettings.smoothing.mode);
            mode.RegisterValueChangedCallback(e => ApplyPaintToolChange(() => paintSettings.smoothing.mode = (StrokeSmoothingMode)e.newValue));
            row.Add(mode);
            var distance = CompactField(new FloatField("Distance (px)"), 90f);
            distance.name = "strokeSmoothingDistance";
            distance.tooltip = "Filter distance or stabilizer rope length in canvas pixels (1–256), independent of Canvas View zoom.";
            toolkitHeaderBindings.Track(distance, () => paintSettings.smoothing.distance);
            distance.RegisterValueChangedCallback(e => ApplyPaintToolChange(() => paintSettings.smoothing.distance = e.newValue));
            row.Add(distance);
            var pressure = CompactField(new Toggle("Smooth Pressure"), 110f);
            pressure.name = "strokeSmoothPressure";
            pressure.tooltip = "Filter pressure changes independently of path smoothing. The tool's Pressure option must also be enabled to use pressure.";
            toolkitHeaderBindings.Track(pressure, () => paintSettings.smoothing.smoothPressure);
            pressure.RegisterValueChangedCallback(e => ApplyPaintToolChange(() => paintSettings.smoothing.smoothPressure = e.newValue));
            row.Add(pressure);
            var finish = CompactField(new Toggle("Finish Stroke"), 100f);
            finish.name = "strokeFinish";
            finish.tooltip = "On release, finish the delayed path at the pointer. Off leaves the stroke at the filtered position. Losing pointer capture never adds a tail.";
            toolkitHeaderBindings.Track(finish, () => paintSettings.smoothing.finishStroke);
            finish.RegisterValueChangedCallback(e => ApplyPaintToolChange(() => paintSettings.smoothing.finishStroke = e.newValue));
            row.Add(finish);
            var lockAlpha = CompactField(new Toggle("Lock Alpha"), 90f);
            lockAlpha.name = "paintLockAlpha";
            lockAlpha.tooltip = "Preserve the layer's alpha and fully transparent pixels. Recolor existing pixels without changing their silhouette; erasing cannot change alpha.";
            toolkitHeaderBindings.Track(lockAlpha, () => paintSettings.dynamics.lockAlpha);
            lockAlpha.RegisterValueChangedCallback(e => ApplyPaintToolChange(() => paintSettings.dynamics.lockAlpha = e.newValue));
            row.Add(lockAlpha);
            row.Add(new Label("Write") { tooltip = "Enabled channels may change. Disabled channels retain their previous values. This is separate from channel visibility in the footer." });
            string[] names = { "R", "G", "B", "A" };
            for (int i = 0; i < 4; i++)
            {
                int bit = 1 << i;
                var button = new Button(() => ApplyPaintToolChange(() => paintSettings.dynamics.writeChannels ^= bit))
                { name = "paintWrite" + names[i], text = names[i], tooltip = "Allow writing " + names[i] + ". Off preserves this channel; Lock Alpha takes priority over A." };
                button.AddToClassList("whimtex-channel-button");
                toolkitHeaderBindings.Add(() => button.EnableInClassList("whimtex-channel-button--enabled", (paintSettings.dynamics.writeChannels & bit) != 0));
                row.Add(button);
            }
            toolkitHeaderBindings.Add(() =>
            {
                mode.style.display = path ? DisplayStyle.Flex : DisplayStyle.None;
                pressure.style.display = path && usesPressure ? DisplayStyle.Flex : DisplayStyle.None;
                distance.style.display = path && (paintSettings.smoothing.mode != StrokeSmoothingMode.None || paintSettings.smoothing.smoothPressure && usesPressure) ? DisplayStyle.Flex : DisplayStyle.None;
                finish.style.display = path && paintSettings.smoothing.mode != StrokeSmoothingMode.None ? DisplayStyle.Flex : DisplayStyle.None;
            });
        }
    }
}
