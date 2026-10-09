using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static DCFApixels.WhimTex.ShapeLayerBehaviour;

namespace DCFApixels.WhimTex
{
    internal static class ShapeCornerSettingsView
    {
        internal static VisualElement Build(ShapeLayerBehaviour layer, WhimTexDocument document,
            Action<string, Action> apply, WhimTexUI.ValueBindings bindings)
        {
            var root = new Foldout { text = "Corners (%)", value = true };
            var layout = new VisualElement(); layout.AddToClassList("whimtex-shape-corners"); root.Add(layout);
            var diagram = new CornerDiagram { name = "cornerDiagram" };
            layout.Add(diagram);
            var linkIcon = new WhimTexLinkIcon();
            var link = new Button(() => apply("Link Shape Corners", () => layer.linkCorners = !layer.linkCorners))
            {
                name = "linkCorners", tooltip = "Link corner amounts proportionally, not styles. From zero, add the same amount to each corner."
            };
            link.AddToClassList("whimtex-shape-corner-link"); link.Add(linkIcon); layout.Add(link);
            var fields = new VisualElement { pickingMode = PickingMode.Ignore }; fields.AddToClassList("whimtex-shape-corner-fields"); layout.Add(fields);
            var cornerBindings = new WhimTexUI.ValueBindings();
            ShapeKind builtKind = (ShapeKind)(-1); int builtCount = -1, selected = 0;
            bool selecting = false;
            Corner Get(int index) => layer.kind == ShapeKind.Rectangle ? layer.rectangleCorners[index] :
                layer.kind == ShapeKind.Polygon ? layer.polygonCorners[index] :
                index == 0 ? layer.outerCorner : layer.innerCorner;

            void BuildFields(int count)
            {
                fields.Clear(); cornerBindings.Clear();
                string[] rectangleNames = { "Top Left", "Top Right", "Bottom Right", "Bottom Left" };
                string[] rectangleLabels = { "TL", "TR", "BR", "BL" };
                int visibleCount = selecting ? 1 : count;
                for (int i = 0; i < visibleCount; i++)
                {
                    int index = selecting ? selected : i;
                    string label = layer.kind == ShapeKind.Rectangle ? rectangleLabels[index] :
                        layer.kind == ShapeKind.Star || layer.kind == ShapeKind.Sector ? (index == 0 ? "Outer" : "Inner") :
                        layer.kind == ShapeKind.Polygon ? (index + 1).ToString() : "All";
                    string title = layer.kind == ShapeKind.Rectangle ? rectangleNames[index] :
                        layer.kind == ShapeKind.Polygon ? "Vertex " + (index + 1) : label + " corners";
                    var row = new VisualElement(); row.AddToClassList("whimtex-shape-corner-control");
                    row.AddToClassList("whimtex-shape-corner-" + Slot(layer.kind, count, index, selecting)); fields.Add(row);
                    var amount = new FloatField(label) { name = "cornerAmount" + index,
                        tooltip = title + " — Size (%). Zero is sharp. Increasing this corner can reduce its neighbours; decreasing it does not restore them." };
                    var style = new EnumField(Get(index).style) { name = "cornerStyle" + index, tooltip = title + " — Round / Bevel" };
                    style.AddToClassList("whimtex-shape-corner-style");
                    var icon = new CornerStyleIcon();
                    style.Q<VisualElement>(className: EnumField.inputUssClassName).Insert(0, icon);
                    TwoChoiceDropdown.Attach(style);
                    row.Add(amount); row.Add(style);
                    cornerBindings.Track(style, () => (Enum)Get(index).style);
                    cornerBindings.Track(amount, () => Get(index).amount * 100f);
                    cornerBindings.Add(() => icon.SetStyle(Get(index).style));
                    style.RegisterValueChangedCallback(evt => apply("Change Shape Corner Style", () =>
                    {
                        Corner value = Get(index); value.style = (CornerStyle)evt.newValue;
                        layer.SetCorner(index, value, layer.GeometryHalfSize(document));
                        amount.SetValueWithoutNotify(Get(index).amount * 100f);
                    }));
                    amount.RegisterValueChangedCallback(evt => apply("Change Shape Corners", () =>
                    {
                        Corner value = Get(index); value.amount = evt.newValue / 100f;
                        layer.SetCorner(index, value, layer.GeometryHalfSize(document));
                        amount.SetValueWithoutNotify(Get(index).amount * 100f);
                    }));
                }
            }
            void Refresh()
            {
                bool rectangle = layer.kind == ShapeKind.Rectangle, polygon = layer.kind == ShapeKind.Polygon;
                bool star = layer.kind == ShapeKind.Star, sector = layer.kind == ShapeKind.Sector;
                bool visible = rectangle || polygon || star || (sector && layer.sweepAngle < 360);
                root.EnableInClassList("whimtex-hidden", !visible);
                if (!visible) return;
                layer.EnsureCorners();
                int count = rectangle ? 4 : polygon ? layer.sides : 2;
                selecting = polygon && count > 8;
                layout.EnableInClassList("whimtex-shape-corners-radial", polygon && !selecting);
                layout.EnableInClassList("whimtex-shape-corners-select", selecting);
                layout.EnableInClassList("whimtex-shape-corners-groups", star || sector);
                link.EnableInClassList("whimtex-hidden", !rectangle && !polygon);
                link.EnableInClassList("whimtex-shape-corner-link-edge", selecting);
                linkIcon.SetLinked(layer.linkCorners);
                if (builtKind != layer.kind || builtCount != count)
                {
                    builtKind = layer.kind; builtCount = count; selected = Mathf.Clamp(selected, 0, count - 1);
                    BuildFields(count);
                }
                cornerBindings.Refresh();
                diagram.UpdateContour(layer, layer.GeometryHalfSize(document), selecting, selected);
            }
            diagram.Selected += index =>
            {
                if (!selecting || selected == index) return;
                selected = index; BuildFields(builtCount); Refresh();
            };
            layer.EnsureCorners(); bindings.Add(Refresh); Refresh();
            return root;
        }

        private static string Slot(ShapeKind kind, int count, int index, bool selecting)
        {
            if (selecting) return "s";
            if (kind == ShapeKind.Rectangle) return new[] { "nw", "ne", "se", "sw" }[index];
            if (kind == ShapeKind.Star || kind == ShapeKind.Sector) return index == 0 ? "nw" : "se";
            switch (count)
            {
                case 3: return new[] { "n", "sw", "se" }[index];
                case 4: return new[] { "n", "w", "s", "e" }[index];
                case 5: return new[] { "n", "nw", "sw", "se", "ne" }[index];
                case 6: return new[] { "n", "nw", "sw", "s", "se", "ne" }[index];
                case 7: return new[] { "n", "nw", "w", "sw", "se", "e", "ne" }[index];
                default: return new[] { "n", "nw", "w", "sw", "s", "se", "e", "ne" }[index];
            }
        }

        private sealed class CornerStyleIcon : VisualElement
        {
            private CornerStyle value;
            internal CornerStyleIcon()
            {
                pickingMode = PickingMode.Ignore; AddToClassList("whimtex-shape-corner-icon");
                generateVisualContent += Draw;
            }
            internal void SetStyle(CornerStyle next) { if (next == value) return; value = next; MarkDirtyRepaint(); }
            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 1 || contentRect.height < 1) return;
                Vector2 P(float x, float y) => contentRect.position + new Vector2(x * contentRect.width / 18, y * contentRect.height / 18);
                var p = context.painter2D; p.strokeColor = resolvedStyle.color; p.lineWidth = 1.6f;
                p.BeginPath(); p.MoveTo(P(3, 15)); p.LineTo(P(3, 9));
                if (value == CornerStyle.Round) p.BezierCurveTo(P(3, 5.7f), P(5.7f, 3), P(9, 3));
                else p.LineTo(P(9, 3));
                p.LineTo(P(15, 3)); p.Stroke();
            }
        }

        private sealed class CornerDiagram : VisualElement
        {
            private readonly ShapeContour contour = new ShapeContour();
            private readonly List<Vector2> points = new List<Vector2>();
            private readonly List<Vector2> vertices = new List<Vector2>();
            private Vector2 halfSize;
            private bool selectable;
            private int selected;
            internal event Action<int> Selected;
            internal CornerDiagram()
            {
                AddToClassList("whimtex-shape-corner-diagram");
                generateVisualContent += Draw;
                RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (!selectable || evt.button != 0) return;
                    int nearest = -1; float distance = 100;
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        float candidate = (Project(vertices[i]) - (Vector2)evt.localPosition).sqrMagnitude;
                        if (candidate < distance) { distance = candidate; nearest = i; }
                    }
                    if (nearest < 0) return;
                    Focus(); Selected?.Invoke(nearest); evt.StopPropagation();
                });
                RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (!selectable || (evt.keyCode != KeyCode.LeftArrow && evt.keyCode != KeyCode.RightArrow)) return;
                    Selected?.Invoke((selected + vertices.Count + (evt.keyCode == KeyCode.RightArrow ? 1 : -1)) % vertices.Count);
                    evt.StopPropagation();
                });
            }
            internal void UpdateContour(ShapeLayerBehaviour shape, Vector2 size, bool select, int index)
            {
                halfSize = size; selectable = select; selected = index; focusable = select;
                tooltip = select ? "Click a vertex to edit its corner. Left / Right selects the previous / next vertex." : "Shape corner preview";
                contour.CopyPreview(shape, size, points); vertices.Clear();
                if (shape.kind == ShapeKind.Polygon)
                    for (int i = 0; i < shape.sides; i++)
                    {
                        float angle = Mathf.PI * .5f + i * Mathf.PI * 2 / shape.sides;
                        vertices.Add(Vector2.Scale(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), size));
                    }
                MarkDirtyRepaint();
            }
            private Vector2 Project(Vector2 value)
            {
                float scale = Mathf.Min(contentRect.width, contentRect.height) * .42f / Mathf.Max(halfSize.x, halfSize.y);
                return contentRect.center + new Vector2(value.x, -value.y) * scale;
            }
            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 1 || contentRect.height < 1 || points.Count < 2) return;
                var p = context.painter2D; Color color = resolvedStyle.color;
                p.strokeColor = color; p.fillColor = new Color(color.r, color.g, color.b, .12f); p.lineWidth = 1.5f;
                p.BeginPath(); p.MoveTo(Project(points[0]));
                for (int i = 1; i < points.Count; i++) p.LineTo(Project(points[i]));
                p.ClosePath(); p.Fill(); p.Stroke();
                for (int i = 0; i < vertices.Count; i++)
                {
                    p.fillColor = selectable && i == selected ? new Color(.3f, .65f, 1) : color;
                    p.BeginPath(); p.Arc(Project(vertices[i]), selectable && i == selected ? 4 : 2.5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
                    if (!selectable || i == selected)
                        context.DrawText((i + 1).ToString(), Project(vertices[i] * .7f) - new Vector2(3, 6), 10, color, null);
                }
            }
        }
    }
}
