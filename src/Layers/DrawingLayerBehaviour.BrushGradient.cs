using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        private static partial class PaintBrushRenderer
        {
            private const int SdfGradientWidth = 1024;
            private static Texture2D sdfGradientTexture;
            private static WhimTexGradient sdfGradientSnapshot;
            private static Color[] sdfGradientPixels;
            private static bool sdfGradientStandardInputs;
            private static bool sdfGradientValid;

            private static Texture2D GetBrushSdfGradient(BrushDynamics dynamics, bool standardInputs)
            {
                WhimTexGradient source = dynamics.tipGradient ??= BrushDynamics.DefaultTipGradient();
                if (sdfGradientValid && sdfGradientTexture != null && sdfGradientSnapshot != null &&
                    sdfGradientStandardInputs == standardInputs && sdfGradientSnapshot.Equals(source))
                    return sdfGradientTexture;

                sdfGradientValid = false;
                sdfGradientSnapshot = source.Clone();
                sdfGradientStandardInputs = standardInputs;
                WhimTexGradient evaluated = sdfGradientSnapshot;
                if (standardInputs)
                {
                    var colors = source.ColorKeys;
                    for (int i = 0; i < colors.Length; i++)
                        colors[i].color = WhimTexColorInputs.StandardColor(colors[i].color);
                    evaluated = source.Clone();
                    evaluated.SetKeys(colors, source.AlphaKeys);
                }
                if (sdfGradientTexture == null)
                    sdfGradientTexture = new Texture2D(SdfGradientWidth, 2, TextureFormat.RGBAHalf, true, true)
                    {
                        name = "Brush SDF Gradient", hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
                    };
                sdfGradientPixels ??= new Color[SdfGradientWidth * 2];
                for (int i = 0; i < SdfGradientWidth; i++)
                {
                    Color color = HdrUtility.DecodePaintColor(evaluated.EvaluateEncoded(i / (float)(SdfGradientWidth - 1)));
                    sdfGradientPixels[i + SdfGradientWidth] = sdfGradientPixels[i] = new Color(color.r * color.a, color.g * color.a, color.b * color.a, color.a);
                }
                sdfGradientTexture.SetPixels(sdfGradientPixels);
                sdfGradientTexture.Apply(true, false);
                sdfGradientValid = true;
                return sdfGradientTexture;
            }

            private static void ReleaseBrushSdfGradient()
            {
                if (sdfGradientTexture != null) Object.DestroyImmediate(sdfGradientTexture);
                sdfGradientTexture = null;
                sdfGradientSnapshot = null;
                sdfGradientPixels = null;
                sdfGradientValid = false;
            }
        }
    }
}
