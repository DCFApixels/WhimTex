using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexLinkIcon : VisualElement
    {
        private bool linked;
        internal WhimTexLinkIcon()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("whimtex-tool-icon");
            generateVisualContent += Draw;
        }
        internal void SetLinked(bool value)
        {
            if (linked == value) return;
            linked = value;
            MarkDirtyRepaint();
        }
        private void Draw(MeshGenerationContext context)
        {
            if (contentRect.width < 1f || contentRect.height < 1f) return;
            var p = context.painter2D;
            Vector2 P(float x, float y) => contentRect.position + new Vector2(x * contentRect.width / 22f, y * contentRect.height / 22f);
            p.strokeColor = resolvedStyle.color;
            p.lineWidth = 1.3f; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(P(10, 7)); p.LineTo(P(7, 7)); p.BezierCurveTo(P(1, 7), P(1, 15), P(7, 15)); p.LineTo(P(10, 15));
            p.MoveTo(P(12, 7)); p.LineTo(P(15, 7)); p.BezierCurveTo(P(21, 7), P(21, 15), P(15, 15)); p.LineTo(P(12, 15));
            if (linked) { p.MoveTo(P(7, 11)); p.LineTo(P(15, 11)); }
            else { p.MoveTo(P(7, 11)); p.LineTo(P(9, 11)); p.MoveTo(P(13, 11)); p.LineTo(P(15, 11)); }
            p.Stroke();
            if (!linked)
            {
                p.BeginPath(); p.MoveTo(P(6, 3)); p.LineTo(P(16, 19)); p.Stroke();
            }
        }
    }
}
