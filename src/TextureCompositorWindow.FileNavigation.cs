using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private TextureCompositorWindow OpenNewDocument()
        {
            FinishPreviewTransform();
            FinishPaintingStroke();
            var window = CreateWindow<TextureCompositorWindow>("WhimTex", typeof(TextureCompositorWindow));
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
            TextureCompositor document = TextureCompositor.FindDocument(file.sourceTexture);
            if (document == null) return false;
            WhimTexUI.ConsumeEvent(evt);
            activeLayerDrag?.Cancel();
            FinishPreviewTransform();
            FinishPaintingStroke();
            SelectOnlyLayer(layer.Id);
            RefreshToolkitInterface();
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && document != null) OpenReferencedDocument(document);
            };
            return true;
        }

        private static TextureCompositorWindow OpenReferencedDocument(TextureCompositor document)
        {
            if (document == null) return null;
            TextureCompositorWindow existing = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (candidate.compositor == document && (existing == null || candidate.agentFocusOrder > existing.agentFocusOrder))
                    existing = candidate;
            if (existing != null)
            {
                existing.RefreshDocumentTitle(true);
                existing.Show();
                existing.Focus();
                return existing;
            }
            var window = CreateWindow<TextureCompositorWindow>("WhimTex", typeof(TextureCompositorWindow));
            window.SetCompositor(document);
            window.Focus();
            return window;
        }
    }
}
