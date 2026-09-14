using System;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private EffectRenderCache previewEffectCache;
        [NonSerialized] private double effectInteractiveUntil;
        [NonSerialized] private bool effectRefinementPending;

        private bool EffectsAreInteractive => paintingLayer != null ||
            previewTransformManipulator != null && previewTransformManipulator.IsDragging ||
            EditorApplication.timeSinceStartup < effectInteractiveUntil;

        private void RequestEffectRefinement()
        {
            if (!effectRefinementPending || EffectsAreInteractive) return;
            effectRefinementPending = false;
            RequestPreview(true);
        }

        private void ReleaseEffectCache()
        {
            previewEffectCache?.Dispose();
            previewEffectCache = null;
            effectRefinementPending = false;
            effectInteractiveUntil = 0d;
        }
    }
}
