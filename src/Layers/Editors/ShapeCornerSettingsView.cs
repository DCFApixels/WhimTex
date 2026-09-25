using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class ShapeCornerSettingsView
    {
        internal static VisualElement Build(ShapeLayerBehaviour layer, Action<string, Action> apply,
            WhimTexUI.ValueBindings bindings)
        {
            var root = new VisualElement();
            root.Add(new Label("Roundness (%)"));
            var layout = new VisualElement();
            layout.AddToClassList("whimtex-shape-corners");
            root.Add(layout);
            var diagram = new CornerDiagram();
            layout.Add(diagram);
            string[] names = { "Top Left", "Top Right", "Bottom Right", "Bottom Left" };
            string[] labels = { "TL", "TR", "BR", "BL" };
            string[] positions = { "tl", "tr", "br", "bl" };
            var fields = new FloatField[4];
            for (int i = 0; i < fields.Length; i++)
            {
                int corner = i;
                var field = new FloatField(labels[i]) { name = "corner" + i,
                    tooltip = names[i] + ": 0–100%. Drag the label to adjust. Corners are local to the shape, before Transform." };
                field.AddToClassList("whimtex-shape-corner-field");
                field.AddToClassList("whimtex-shape-corner-" + positions[i]);
                fields[i] = field;
                bindings.Track(field, () => layer.GetCornerRoundness()[corner] * 100f);
                field.RegisterValueChangedCallback(evt =>
                {
                    Vector4 next = ShapeLayerBehaviour.AdjustCorner(layer.GetCornerRoundness(), corner, evt.newValue / 100f, layer.linkCorners);
                    apply("Change Shape Corners", () => layer.cornerRoundness = next);
                    for (int j = 0; j < fields.Length; j++) fields[j].SetValueWithoutNotify(next[j] * 100f);
                    diagram.SetCorners(next);
                });
                layout.Add(field);
            }
            var icon = new WhimTexLinkIcon();
            var link = new Button(() => apply("Link Shape Corners", () => layer.linkCorners = !layer.linkCorners)) { name = "linkCorners" };
            link.AddToClassList("whimtex-shape-corner-link");
            link.Add(icon);
            layout.Add(link);
            bindings.Add(() =>
            {
                diagram.SetCorners(layer.GetCornerRoundness());
                icon.SetLinked(layer.linkCorners);
                link.tooltip = layer.linkCorners
                    ? "Linked: changes scale all corners proportionally, stopping at 100%. From zero, add the same amount to all. Click to unlink."
                    : "Unlinked: edit each corner separately. Click to link without changing the values.";
            });
            return root;
        }

        private sealed class CornerDiagram : VisualElement
        {
            private Vector4 corners;
            internal CornerDiagram()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-shape-corner-diagram");
                generateVisualContent += Draw;
            }
            internal void SetCorners(Vector4 value)
            {
                if (corners == value) return;
                corners = value;
                MarkDirtyRepaint();
            }
            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 4f || contentRect.height < 4f) return;
                var p = context.painter2D;
                float x = contentRect.x + 1f, y = contentRect.y + 1f;
                float w = contentRect.width - 2f, h = contentRect.height - 2f;
                Vector4 r = corners * (Mathf.Min(w, h) * .5f);
                const float k = .55228475f;
                Color color = resolvedStyle.color;
                p.strokeColor = new Color(color.r, color.g, color.b, .65f);
                p.fillColor = new Color(color.r, color.g, color.b, .07f);
                p.lineWidth = 1.3f;
                p.BeginPath();
                p.MoveTo(new Vector2(x + r.x, y));
                p.LineTo(new Vector2(x + w - r.y, y));
                p.BezierCurveTo(new Vector2(x + w - r.y * (1f - k), y), new Vector2(x + w, y + r.y * (1f - k)), new Vector2(x + w, y + r.y));
                p.LineTo(new Vector2(x + w, y + h - r.z));
                p.BezierCurveTo(new Vector2(x + w, y + h - r.z * (1f - k)), new Vector2(x + w - r.z * (1f - k), y + h), new Vector2(x + w - r.z, y + h));
                p.LineTo(new Vector2(x + r.w, y + h));
                p.BezierCurveTo(new Vector2(x + r.w * (1f - k), y + h), new Vector2(x, y + h - r.w * (1f - k)), new Vector2(x, y + h - r.w));
                p.LineTo(new Vector2(x, y + r.x));
                p.BezierCurveTo(new Vector2(x, y + r.x * (1f - k)), new Vector2(x + r.x * (1f - k), y), new Vector2(x + r.x, y));
                p.ClosePath(); p.Fill(); p.Stroke();
            }
        }

    }
}
