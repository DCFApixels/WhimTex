using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class PreviewLoadingIndicator : VisualElement
    {
        const double ShimmerDuration = .75;
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
            preview.tintColor = value ? new Color(.62f, .62f, .62f, 1) : Color.white;
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
            Rect rect = contentRect;
            if (rect.width <= 0 || rect.height <= 0) return;
            if (preview.image is Texture texture && texture.width > 0 && texture.height > 0)
            {
                float scale = Mathf.Min(rect.width / texture.width, rect.height / texture.height);
                Vector2 size = new Vector2(texture.width, texture.height) * scale;
                rect = new Rect(rect.center - size * .5f, size);
            }
            double time = EditorApplication.timeSinceStartup;
            DrawShimmer(context, rect, (float)(time % ShimmerDuration / ShimmerDuration));
            var painter = context.painter2D;
            Vector2 center = rect.center;
            float discRadius = Mathf.Min(12, Mathf.Min(rect.width, rect.height) * .5f);
            float radius = Mathf.Max(0, discRadius - 4);
            painter.fillColor = new Color(24 / 255f, 24 / 255f, 24 / 255f, .8f);
            painter.BeginPath(); painter.Arc(center, discRadius, Angle.Degrees(0), Angle.Degrees(360)); painter.Fill();
            painter.lineWidth = 2;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = new Color(1, 1, 1, .2f);
            painter.BeginPath(); painter.Arc(center, radius, Angle.Degrees(0), Angle.Degrees(360)); painter.Stroke();
            float angle = (float)(time % 1) * 360;
            painter.strokeColor = resolvedStyle.color;
            painter.BeginPath(); painter.Arc(center, radius, Angle.Degrees(angle), Angle.Degrees(angle + 250)); painter.Stroke();
        }

        static void DrawShimmer(MeshGenerationContext context, Rect rect, float phase)
        {
            const int rows = 16;
            var mesh = context.Allocate((rows + 1) * 2, rows * 6);
            float center = Mathf.Lerp(-.5f, 1.5f, phase);
            for (int y = 0; y <= rows; y++)
            {
                float v = (float)y / rows;
                float distance = Mathf.Clamp01(Mathf.Abs(v - center) / .42f);
                float alpha = .14f * (.5f + .5f * Mathf.Cos(distance * Mathf.PI));
                for (int x = 0; x <= 1; x++)
                {
                    mesh.SetNextVertex(new Vertex
                    {
                        position = new Vector3(rect.xMin + x * rect.width, rect.yMin + v * rect.height, Vertex.nearZ),
                        tint = new Color(1, 1, 1, alpha)
                    });
                }
                if (y == rows) continue;
                ushort i = (ushort)(y * 2);
                mesh.SetNextIndex(i); mesh.SetNextIndex((ushort)(i + 1)); mesh.SetNextIndex((ushort)(i + 3));
                mesh.SetNextIndex(i); mesh.SetNextIndex((ushort)(i + 3)); mesh.SetNextIndex((ushort)(i + 2));
            }
        }
    }
}
