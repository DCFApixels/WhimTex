using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        // Owned by one CanvasElement; refreshes UI only, never requests image rendering.
        private sealed class CanvasOverlayHost
        {
            readonly CanvasElement canvas;
            readonly List<(VisualElement element, Action update)> entries = new();
            bool listening, refreshing;
            internal CanvasOverlayHost(CanvasElement canvas)
            {
                this.canvas = canvas;
                canvas.RegisterCallback<AttachToPanelEvent>(_ => { Listen(true); Refresh(); });
                canvas.RegisterCallback<DetachFromPanelEvent>(_ => Listen(false));
                Listen(true);
            }
            void Listen(bool enabled)
            {
                if (listening == enabled) return;
                listening = enabled;
                // CanvasElement already publishes layout changes through ViewChanged.
                if (enabled) canvas.ViewChanged += Refresh;
                else canvas.ViewChanged -= Refresh;
            }
            internal VisualElement Create(Action<MeshGenerationContext> draw, Action update = null)
            {
                var element = new VisualElement { pickingMode = PickingMode.Ignore };
                element.StretchToParentSize();
                element.generateVisualContent += draw;
                Add(element, update);
                return element;
            }
            internal void Add(VisualElement element, Action update = null)
            {
                canvas.Add(element);
                entries.Add((element, update));
            }
            internal void Refresh()
            {
                if (refreshing) return;
                refreshing = true;
                try
                {
                    foreach (var entry in entries)
                    {
                        entry.update?.Invoke();
                        entry.element.MarkDirtyRepaint();
                    }
                }
                finally { refreshing = false; }
            }
        }
    }
}
