using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPanelWorkspace
    {
        private IVisualElementScheduledItem persistence;
        private bool savePending, disposed;
        private int gestures;
        private string savedLayout;
        private void InitializePersistence()
        {
            savedLayout = JsonUtility.ToJson(layout);
            persistence = viewport.schedule.Execute(Flush); persistence.Pause();
            viewport.RegisterCallback<DetachFromPanelEvent>(OnViewportDetached);
        }
        private void OnViewportDetached(DetachFromPanelEvent e) { if (e.target == viewport) Dispose(); }
        private void BeginGesture() { gestures++; persistence?.Pause(); }
        private void EndGesture()
        {
            HideDropHint(); draggedTab = null;
            gestures = System.Math.Max(0, gestures - 1);
            if (gestures == 0 && savePending && !disposed) persistence?.ExecuteLater(200);
        }
        private void RequestSave()
        {
            if (disposed) return;
            savePending = true;
            if (gestures == 0) persistence?.ExecuteLater(200);
        }
        internal void Flush()
        {
            persistence?.Pause();
            if (!savePending) return;
            string json = JsonUtility.ToJson(layout);
            if (json != savedLayout) { save(); savedLayout = json; }
            savePending = false;
        }
        public void Dispose()
        {
            if (disposed) return;
            ClearTabPreview(); tabPreviewOverlay?.RemoveFromHierarchy();
            if (beforeDragLayout != null)
            { JsonUtility.FromJsonOverwrite(beforeDragLayout, layout); beforeDragLayout = null; }
            Flush(); disposed = true;
            viewport.UnregisterCallback<DetachFromPanelEvent>(OnViewportDetached);
            foreach (var pane in views.Values) pane.ReleaseSplits();
        }
    }
}
