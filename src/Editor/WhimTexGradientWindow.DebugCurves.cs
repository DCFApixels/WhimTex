#if WHIMTEX_DEEP_DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexGradientWindow
    {
        [SerializeField] private int debugCurveChannels = 15;
        private VisualElement debugCurvePlot;
        private Label debugCurveRange;
        private readonly Button[] debugCurveButtons = new Button[4];
        private readonly List<float> debugCurveTimes = new List<float>(800);
        private readonly List<Color> debugCurveValues = new List<Color>(800);
        private WhimTexGradient debugCurveSource;
        private uint debugCurveRevision;
        private float debugCurveMin, debugCurveMax = 1;

        private static readonly Color[] DebugCurveColors =
        {
            new Color(1, .38f, .38f), new Color(.4f, .85f, .45f),
            new Color(.38f, .65f, 1), new Color(.86f, .86f, .86f)
        };

        private void BuildDebugCurves()
        {
            var block = new VisualElement { name = "gradientDebugCurves" };
            block.AddToClassList("whimtex-gradient-curves");
            var toolbar = new VisualElement();
            toolbar.AddToClassList("whimtex-gradient-curves-toolbar");
            debugCurveRange = new Label { name = "gradientCurveRange", pickingMode = PickingMode.Ignore };
            debugCurveRange.AddToClassList("whimtex-gradient-curves-range");
            toolbar.Add(debugCurveRange);
            for (int channel = 0; channel < 4; channel++)
            {
                int index = channel;
                string label = "RGBA"[channel].ToString();
                var button = new Button(() => ToggleDebugCurve(index))
                    { text = label, name = "gradientCurve" + label, tooltip = "Show or hide the " + label + " curve" };
                button.AddToClassList("whimtex-gradient-curve-toggle");
                button.style.color = DebugCurveColors[channel];
                debugCurveButtons[channel] = button;
                toolbar.Add(button);
            }
            block.Add(toolbar);
            debugCurvePlot = new VisualElement
            {
                name = "gradientCurvePlot", pickingMode = PickingMode.Ignore,
                tooltip = "Gradient samples from 0 to 1 in its stored color space. RGB is not multiplied by alpha. Preview EV is excluded; the vertical range includes HDR values."
            };
            debugCurvePlot.AddToClassList("whimtex-gradient-curves-plot");
            debugCurvePlot.generateVisualContent += DrawDebugCurves;
            block.Add(debugCurvePlot);
            rootVisualElement.Add(block);
        }

        private void ToggleDebugCurve(int channel)
        {
            debugCurveChannels ^= 1 << channel;
            RefreshDebugCurves();
        }

        private void RefreshDebugCurves()
        {
            if (debugCurvePlot == null || gradient == null) return;
            if (!ReferenceEquals(debugCurveSource, gradient) || debugCurveRevision != gradient.Revision)
            {
                debugCurveTimes.Clear();
                debugCurveValues.Clear();
                for (int i = 0; i <= 512; i++) debugCurveTimes.Add(i / 512f);
                foreach (var key in colors) AddDebugCurveTime(key.time);
                foreach (var key in alphas) AddDebugCurveTime(key.time);
                debugCurveTimes.Sort();
                foreach (float time in debugCurveTimes) debugCurveValues.Add(gradient.Evaluate(time));
                debugCurveSource = gradient;
                debugCurveRevision = gradient.Revision;
            }
            debugCurveMin = 0;
            debugCurveMax = 1;
            foreach (var sample in debugCurveValues)
                for (int channel = 0; channel < 4; channel++)
                    if ((debugCurveChannels & (1 << channel)) != 0)
                    {
                        float value = sample[channel];
                        if (float.IsNaN(value) || float.IsInfinity(value)) continue;
                        debugCurveMin = Mathf.Min(debugCurveMin, value);
                        debugCurveMax = Mathf.Max(debugCurveMax, value);
                    }
            for (int channel = 0; channel < 4; channel++)
                debugCurveButtons[channel].EnableInClassList("whimtex-gradient-curve-toggle--active",
                    (debugCurveChannels & (1 << channel)) != 0);
            string space = gradient.ColorSpace == ColorSpace.Linear ? "Linear" : "Gamma";
            debugCurveRange.text = space + "  " + debugCurveMin.ToString("G4", CultureInfo.InvariantCulture) +
                " … " + debugCurveMax.ToString("G4", CultureInfo.InvariantCulture);
            debugCurvePlot.MarkDirtyRepaint();
        }

        private void AddDebugCurveTime(float time)
        {
            debugCurveTimes.Add(time);
            if (gradient.Mode == WhimTexGradientMode.Fixed && time < 1)
                debugCurveTimes.Add(time == 0 ? float.Epsilon :
                    BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(time) + 1));
        }

        private void DrawDebugCurves(MeshGenerationContext context)
        {
            Rect rect = debugCurvePlot.contentRect;
            if (rect.width < 2 || rect.height < 4 || debugCurveValues.Count == 0) return;
            rect.yMin += 2;
            rect.yMax -= 2;
            var painter = context.painter2D;
            painter.lineWidth = 1;
            painter.strokeColor = new Color(1, 1, 1, .09f);
            painter.BeginPath();
            for (int i = 0; i <= 4; i++)
            {
                float x = rect.xMin + rect.width * i / 4;
                float y = rect.yMin + rect.height * i / 4;
                painter.MoveTo(new Vector2(x, rect.yMin)); painter.LineTo(new Vector2(x, rect.yMax));
                painter.MoveTo(new Vector2(rect.xMin, y)); painter.LineTo(new Vector2(rect.xMax, y));
            }
            painter.Stroke();
            painter.strokeColor = new Color(1, 1, 1, .22f);
            painter.BeginPath();
            float zeroY = DebugCurveY(rect, 0);
            painter.MoveTo(new Vector2(rect.xMin, zeroY)); painter.LineTo(new Vector2(rect.xMax, zeroY));
            painter.Stroke();
            painter.lineWidth = 1.5f;
            for (int order = 0; order < 4; order++)
            {
                int channel = (order + 3) % 4;
                if ((debugCurveChannels & (1 << channel)) == 0) continue;
                painter.strokeColor = DebugCurveColors[channel];
                painter.BeginPath();
                bool started = false;
                for (int i = 0; i < debugCurveValues.Count; i++)
                {
                    float value = debugCurveValues[i][channel];
                    if (float.IsNaN(value) || float.IsInfinity(value)) { started = false; continue; }
                    var point = new Vector2(rect.xMin + rect.width * debugCurveTimes[i], DebugCurveY(rect, value));
                    if (started) painter.LineTo(point); else painter.MoveTo(point);
                    started = true;
                }
                painter.Stroke();
            }
        }

        private float DebugCurveY(Rect rect, float value) =>
            rect.yMax - (value - debugCurveMin) / (debugCurveMax - debugCurveMin) * rect.height;
    }
}
#endif
