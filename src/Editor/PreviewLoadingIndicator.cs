using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class PreviewLoadingIndicator : VisualElement
    {
        static readonly HashSet<PreviewLoadingIndicator> active = new();
        static double repaintAt;
        readonly Image preview;
        internal bool IsUpdating { get; private set; }

        internal PreviewLoadingIndicator(Image preview)
        {
            this.preview = preview;
            name = "preview-loading";
            pickingMode = PickingMode.Ignore;
            AddToClassList("whimtex-preview-loading");
            AddToClassList("whimtex-hidden");
            generateVisualContent += Draw;
            RegisterCallback<AttachToPanelEvent>(_ => UpdateSubscription());
            RegisterCallback<DetachFromPanelEvent>(_ => Remove());
        }

        internal void SetUpdating(bool value)
        {
            if (IsUpdating == value) return;
            IsUpdating = value;
            preview.tintColor = value ? new Color(.72f, .72f, .72f, 1) : Color.white;
            EnableInClassList("whimtex-hidden", !value);
            UpdateSubscription();
            if (value) MarkDirtyRepaint();
        }

        void UpdateSubscription()
        {
            if (!IsUpdating || panel == null) { Remove(); return; }
            if (active.Add(this) && active.Count == 1)
                EditorApplication.update += RepaintActive;
        }

        void Remove()
        {
            if (active.Remove(this) && active.Count == 0)
                EditorApplication.update -= RepaintActive;
        }

        static void RepaintActive()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < repaintAt) return;
            repaintAt = now + 1d / 12;
            foreach (var indicator in active) indicator.MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext context)
        {
            if (!IsUpdating) return;
            var painter = context.painter2D;
            Vector2 center = contentRect.center;
            float radius = Mathf.Max(0, Mathf.Min(contentRect.width, contentRect.height) * .5f - 4);
            painter.lineWidth = 2;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = new Color(1, 1, 1, .2f);
            painter.BeginPath(); painter.Arc(center, radius, Angle.Degrees(0), Angle.Degrees(360)); painter.Stroke();
            float angle = (float)(EditorApplication.timeSinceStartup % 1) * 360;
            painter.strokeColor = resolvedStyle.color;
            painter.BeginPath(); painter.Arc(center, radius, Angle.Degrees(angle), Angle.Degrees(angle + 250)); painter.Stroke();
        }
    }
}
