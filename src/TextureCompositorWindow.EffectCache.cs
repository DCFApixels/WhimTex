using System;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private EffectRenderCache canvasEffectCache;
        [NonSerialized] private double effectInteractiveUntil;
        [NonSerialized] private bool effectRefinementPending;

        private bool EffectsAreInteractive => paintingLayer != null ||
            canvasTransformManipulator != null && canvasTransformManipulator.IsDragging ||
            EditorApplication.timeSinceStartup < effectInteractiveUntil;

        private void RequestEffectRefinement()
        {
            if (!effectRefinementPending || EffectsAreInteractive) return;
            effectRefinementPending = false;
            RequestCanvasRender(true);
        }

        private void ReleaseEffectCache()
        {
            canvasEffectCache?.Dispose();
            canvasEffectCache = null;
            effectRefinementPending = false;
            effectInteractiveUntil = 0d;
        }
    }
}
