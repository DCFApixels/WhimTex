using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private WhimTexWindow OpenNewDocument()
        {
            FinishCanvasTransform();
            FinishPaintingStroke();
            var window = CreateWindow<WhimTexWindow>("WhimTex", typeof(WhimTexWindow));
            window.RefreshDocumentTitle(true);
            window.Focus();
            return window;
        }

        private bool TryOpenFileLayerDocument(VisualElement row, Layer layer, PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.clickCount != 2 || evt.altKey || evt.ctrlKey || evt.commandKey || evt.shiftKey ||
                !(layer?.Behaviour is FileLayerBehaviour file) || file.sourceTexture == null ||
                FindLayerDragControl(row, evt.target as VisualElement) != null ||
                !IsLayerDragArea(row, evt.target as VisualElement)) return false;
            Texture2D source = file.sourceTexture;
            if (!WhimTexDocumentService.IsDocumentAsset(source)) return false;
            WhimTexUI.ConsumeEvent(evt);
            activeLayerDrag?.Cancel();
            FinishCanvasTransform();
            FinishPaintingStroke();
            SelectOnlyLayer(layer.Id);
            RefreshToolkitInterface();
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && source != null)
                    OpenWhimTexDocumentPath(UnityEditor.AssetDatabase.GetAssetPath(source));
            };
            return true;
        }

        private static WhimTexWindow OpenReferencedDocument(WhimTexDocument document)
        {
            if (document == null) return null;
            if (UnityEditor.AssetDatabase.Contains(document))
                throw new System.InvalidOperationException("Editable documents must be in-memory models, not Unity assets.");
            WhimTexWindow existing = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
                if (candidate.activeDocument == document && (existing == null || candidate.agentFocusOrder > existing.agentFocusOrder))
                    existing = candidate;
            if (existing != null)
            {
                existing.RefreshDocumentTitle(true);
                existing.Show();
                existing.Focus();
                return existing;
            }
            var window = CreateWindow<WhimTexWindow>("WhimTex", typeof(WhimTexWindow));
            window.SetDocument(document);
            window.Focus();
            return window;
        }
    }
}
