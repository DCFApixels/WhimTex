using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    public enum BlurType { Gaussian, Linear, Circular }

    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "BlurLayerBehaviour")]
    [Serializable]
    public sealed partial class BlurLayerBehaviour : TargetedLayerBehaviour
    {
        public enum EdgeMode { Transparent, Clamp, Repeat, Mirror }
        public enum MotionDirection { Centered, Forward, Backward }
        public const float MaximumRadius = 256f;
        public const float MaximumDistance = 512f;
        public const float MaximumStrength = 4f;

        public float strength = 1f;
        public float radius = 8f;
        public float distance = 16f;
        public float angle;
        public float arc = 15f;
        public Vector2 center = new Vector2(.5f, .5f);
        public MotionDirection direction;
        public EdgeMode edges;

        public BlurType mode;

        public override string ToString() => "Blur";
        internal override bool RequiresColorInput => true;
        internal override RenderTexture Render(in LayerRenderContext context) => mode == BlurType.Gaussian
            ? GaussianBlurRenderer.RenderBlur(this, context) : MotionBlurRenderer.RenderBlur(this, context);
    }
}
