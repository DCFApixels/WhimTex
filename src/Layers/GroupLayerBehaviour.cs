using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "GroupLayerBehaviour")]
    [Serializable]
    public sealed class GroupLayerBehaviour : LayerBehaviour
    {
        public List<Layer> layers { get => Owner.layers; set => Owner.children = value; }
        public GroupCompositing compositing { get => Owner.compositing; set => Owner.compositing = value; }

        internal override bool IsGroup => true;
        internal bool IsPassThrough => compositing == GroupCompositing.PassThrough && swizzle.IsIdentity;
        internal BlendMode EffectiveBlendMode => compositing == GroupCompositing.PassThrough ? BlendMode.Normal : blendMode;

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            // The compositor owns both pass-through and isolated group evaluation.
            return null;
        }
    }
    public enum GroupCompositing { PassThrough, Isolated }
}
