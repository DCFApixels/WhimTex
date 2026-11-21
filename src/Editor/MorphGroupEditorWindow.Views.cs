using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class MorphGroupEditorWindow
    {
        private WhimTexSplitView CreateColumns()
        {
            var endpoints = SplitColumns(a, b, 0, .5f, "morphEndpointsSplit");
            endpoints.style.minWidth = 92;
            var all = SplitColumns(endpoints, result, 1, 1f / 3, "morphResultSplit");
            all.AddToClassList("whimtex-morph-views");
            return all;
        }

        private static WhimTexSplitView SplitColumns(VisualElement first, VisualElement second, int fixedPane,
            float fraction, string name)
        {
            var split = new WhimTexSplitView(first, second, fixedPane, 100, true) { name = name };
            bool resizingWindow = false;
            split.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                float width = evt.newRect.width;
                if (width <= 0 || Mathf.Approximately(width, evt.oldRect.width)) return;
                resizingWindow = true;
                split.FixedPaneSize = width * fraction;
                resizingWindow = false;
            });
            split.FixedPaneSizeChanged += size =>
            {
                if (!resizingWindow && split.contentRect.width > 0)
                    fraction = Mathf.Clamp01(size / split.contentRect.width);
            };
            return split;
        }

        private partial class PreviewPane
        {
            private float zoom = 1;
            // Image-space coordinate under the viewport center, measured from the top left.
            private Vector2 viewCenter = new Vector2(.5f, .5f);
            private int panPointer = -1;
            private Vector2 panStart, panCenter;
            private Rect lastFrame;
            protected virtual bool EditingImage => false;

            internal void ResetView()
            {
                EndPan();
                zoom = 1;
                viewCenter = new Vector2(.5f, .5f);
                FitFrame();
            }

            private Vector2 ImageSize()
            {
                var doc = editor.document;
                var area = Surface.contentRect;
                if (doc == null || doc.width <= 0 || doc.height <= 0 || !(area.width > 0 && area.height > 0))
                    return Vector2.zero;
                // Keep the existing ten-point clearance for endpoint markers when fitted.
                float fit = Mathf.Min(Mathf.Max(1, area.width - 20) / doc.width, Mathf.Max(1, area.height - 20) / doc.height);
                return new Vector2(doc.width, doc.height) * (fit * zoom);
            }

            protected void FitFrame()
            {
                var size = ImageSize();
                if (size == Vector2.zero) return;
                var desired = new Rect(Surface.contentRect.center - Vector2.Scale(viewCenter, size), size);
                if (desired.Equals(lastFrame)) return;
                if (desired.position != lastFrame.position)
                    frame.style.translate = new Translate(desired.x, desired.y);
                if (desired.size != lastFrame.size)
                {
                    frame.style.width = size.x;
                    frame.style.height = size.y;
                }
                lastFrame = desired;
            }

            private void AddNavigation()
            {
                Surface.focusable = true;
                caption.tooltip = "Scroll to zoom. Drag with the middle mouse button to pan. Double-click the middle button to fit.";
                Surface.RegisterCallback<WheelEvent>(evt =>
                {
                    if (Texture == null || EditingImage || panPointer >= 0 || evt.delta.y == 0 || !float.IsFinite(evt.delta.y)) return;
                    Vector2 size = ImageSize();
                    if (size == Vector2.zero) return;
                    float next = Mathf.Clamp(zoom * Mathf.Pow(1.1f, -Mathf.Clamp(evt.delta.y, -20, 20)), .125f, 32);
                    Vector2 offset = (Surface.WorldToLocal(evt.mousePosition) - Surface.contentRect.center) / size;
                    viewCenter += offset * (1 - zoom / next);
                    zoom = next;
                    FitFrame();
                    evt.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
                Surface.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 2 || panPointer >= 0 || EditingImage || Texture == null) return;
                    Surface.Focus();
                    if (evt.clickCount == 2) ResetView();
                    else
                    {
                        panPointer = evt.pointerId;
                        panCenter = viewCenter;
                        panStart = Surface.WorldToLocal(evt.position);
                        Surface.CapturePointer(panPointer);
                    }
                    evt.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
                Surface.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (panPointer != evt.pointerId) return;
                    if ((evt.pressedButtons & 4) != 0) PanTo(evt.position);
                    else EndPan();
                    evt.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
                Surface.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (panPointer != evt.pointerId || evt.button != 2) return;
                    PanTo(evt.position);
                    EndPan();
                    evt.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
                Surface.RegisterCallback<PointerCaptureOutEvent>(evt =>
                {
                    if (panPointer == evt.pointerId) panPointer = -1;
                });
                Surface.RegisterCallback<PointerCancelEvent>(evt => { if (panPointer == evt.pointerId) EndPan(); });
            }

            private void PanTo(Vector2 world)
            {
                var size = ImageSize();
                if (size == Vector2.zero) return;
                viewCenter = panCenter - (Surface.WorldToLocal(world) - panStart) / size;
                FitFrame();
            }

            internal void EndPan()
            {
                int id = panPointer;
                panPointer = -1;
                if (id >= 0 && Surface.HasPointerCapture(id)) Surface.ReleasePointer(id);
            }
        }
    }
}
