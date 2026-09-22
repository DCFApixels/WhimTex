using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "SharpenLayerBehaviour")]
    [Serializable]
    public sealed class SharpenLayerBehaviour : TargetedLayerBehaviour
    {
        public enum Algorithm { Gaussian, Adaptive }
        public enum EdgeMode { Transparent, Clamp, Repeat, Mirror }
        public enum ChannelMode { RGB, Luminance }
        public const float MaximumStrength = 4f;
        public const float MaximumRadius = 32f;

        public Algorithm algorithm = Algorithm.Gaussian;
        public float strength = 1f;
        public float radius = 1f;
        public float threshold;
        public float noiseReduction;
        public float haloSuppression;
        public ChannelMode channelMode = ChannelMode.RGB;
        public EdgeMode edges = EdgeMode.Clamp;

        public override string ToString() => "Sharpen";
        internal override bool RequiresColorInput => true;
        internal override RenderTexture Render(in LayerRenderContext context) =>
            SharpenRenderer.RenderSharpen(this, context);
    }
}
