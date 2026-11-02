using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        internal bool CanGenerateRegionalShaderInput(Layer layer) =>
            layer.Behaviour is ShaderLayerBehaviour shader && shader.IsPassthrough && layer.ReadsCompositeBelow &&
            (layer.EffectiveTiling == TransformTilingMode.Clip || layer.EffectiveTiling == TransformTilingMode.Unbounded) &&
            layer.transform.ToMatrix(width, height).Equals(ProjectiveMatrix.Identity);

        // Composite Below in a pass-through group also consumes the group's external
        // backdrop. Do not depend on the whole parent (which contains this layer).
        internal IEnumerable<Layer> CompositeBackdropLayers(Layer consumer, bool includeDisabled = false)
        {
            Layer current = consumer;
            while (TryFindLayer(current, out var container, out int index))
            {
                for (int i = index + 1; i < container.Count; i++)
                    if (container[i] != null && (includeDisabled || container[i].enabled) &&
                        container[i].Behaviour is not PendingLayerBehaviour)
                        yield return container[i];
                if (!TryFindParentGroup(container, out Layer parent, out _, out _) ||
                    !parent.IsPassThrough || IsGroupIsolatedByClipping(parent)) yield break;
                current = parent;
            }
        }

    }
}
