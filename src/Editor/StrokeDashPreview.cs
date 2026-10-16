using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class StrokeDashPreview : VisualElement
    {
        private IReadOnlyList<float> pattern = Array.Empty<float>();
        private float width, offset;
        private VectorStrokeCap cap;

        internal StrokeDashPreview()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("whimtex-dashes-preview");
            generateVisualContent += Draw;
        }

        internal void Set(IReadOnlyList<float> pattern, float width, VectorStrokeCap cap, float offset)
        { this.pattern = pattern; this.width = width; this.cap = cap; this.offset = offset; MarkDirtyRepaint(); }

        private void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || h <= 0 || width <= 0) return;
            float scale = Mathf.Min(1, Mathf.Max(1, h - 10) / width);
            double dashScale = width * (double)scale;
            float radius = width * scale * .5f, y = h * .5f;
            var p = context.painter2D;
            Color color = resolvedStyle.color;
            p.fillColor = color;
            void Bar(double start, double end)
            {
                float a = (float)start, b = (float)end;
                if (cap == VectorStrokeCap.Square) { a -= radius; b += radius; }
                // Keep tessellation coordinates bounded; overflow clips the ends of the sample.
                a = Mathf.Max(-radius, a); b = Mathf.Min(w + radius, b);
                if (b <= a) return;
                p.BeginPath(); p.MoveTo(new Vector2(a, y - radius)); p.LineTo(new Vector2(b, y - radius));
                if (cap == VectorStrokeCap.Round) p.Arc(new Vector2(b, y), radius, -90, 90);
                p.LineTo(new Vector2(b, y + radius)); p.LineTo(new Vector2(a, y + radius));
                if (cap == VectorStrokeCap.Round) p.Arc(new Vector2(a, y), radius, 90, 270);
                p.ClosePath(); p.Fill();
            }
            int count = (pattern?.Count ?? 0);
            if (count == 0) { Bar(4, w - 4); return; }
            int cycle = count * ((count & 1) == 0 ? 1 : 2);
            double period = 0, painted = 0;
            for (int i = 0; i < cycle; i++)
            {
                if (!float.IsFinite(pattern[i % count]) || pattern[i % count] <= 0) return;
                double length = pattern[i % count] * dashScale;
                period += length; if ((i & 1) == 0) painted += length;
            }
            // Below the preview's sampling resolution, show average coverage instead of
            // creating thousands of off-screen/subpixel paths on each keystroke.
            if (w / period * cycle > 4096)
            {
                const int slices = 16;
                for (int row = 0; row < slices; row++)
                {
                    float dy = -radius + (row + .5f) * radius * 2 / slices;
                    float reach = cap == VectorStrokeCap.Round ? Mathf.Sqrt(Mathf.Max(0, radius * radius - dy * dy)) :
                        cap == VectorStrokeCap.Square ? radius : 0;
                    double coverage = painted;
                    for (int i = 1; i < cycle; i += 2) coverage += Math.Min(pattern[i % count] * dashScale, 2 * reach);
                    Color averaged = color; averaged.a *= (float)(coverage / period); p.fillColor = averaged;
                    float top = y - radius + row * radius * 2 / slices, bottom = y - radius + (row + 1) * radius * 2 / slices;
                    p.BeginPath(); p.MoveTo(new Vector2(0, top)); p.LineTo(new Vector2(w, top));
                    p.LineTo(new Vector2(w, bottom)); p.LineTo(new Vector2(0, bottom)); p.ClosePath(); p.Fill();
                }
                return;
            }
            double phase = (offset * (double)scale - radius) % period;
            if (phase < 0) phase += period;
            int index = 0;
            while (phase >= pattern[index % count] * dashScale)
            { phase -= pattern[index % count] * dashScale; index++; }
            double x = -radius - phase;
            for (int step = 0; step < 4096 && x < w + radius; step++, index = (index + 1) % cycle)
            {
                double end = x + pattern[index % count] * dashScale;
                if ((index & 1) == 0) Bar(x, end);
                x = end;
            }
        }
    }
}
