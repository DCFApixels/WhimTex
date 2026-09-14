using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        private static partial class PaintBrushRenderer
        {
            private const int SdfGradientWidth = 1024;
            private static Texture2D sdfGradientTexture;
            private static Gradient sdfGradientSnapshot;
            private static Color[] sdfGradientPixels;
            private static bool sdfGradientStandardInputs;
            private static bool sdfGradientValid;

            private static Texture2D GetBrushSdfGradient(BrushDynamics dynamics, bool standardInputs)
            {
                Gradient source = dynamics.tipGradient ??= BrushDynamics.DefaultTipGradient();
                if (sdfGradientValid && sdfGradientTexture != null && sdfGradientSnapshot != null &&
                    sdfGradientStandardInputs == standardInputs && sdfGradientSnapshot.Equals(source))
                    return sdfGradientTexture;

                sdfGradientValid = false;
                sdfGradientSnapshot ??= new Gradient();
                sdfGradientSnapshot.SetKeys(source.colorKeys, source.alphaKeys);
                sdfGradientSnapshot.mode = source.mode;
                sdfGradientSnapshot.colorSpace = source.colorSpace;
                sdfGradientStandardInputs = standardInputs;
                Gradient evaluated = sdfGradientSnapshot;
                if (standardInputs)
                {
                    var colors = source.colorKeys;
                    for (int i = 0; i < colors.Length; i++)
                        colors[i].color = WhimTexColorInputs.StandardColor(colors[i].color);
                    evaluated = new Gradient { mode = source.mode, colorSpace = source.colorSpace };
                    evaluated.SetKeys(colors, source.alphaKeys);
                }
                if (sdfGradientTexture == null)
                    sdfGradientTexture = new Texture2D(SdfGradientWidth, 1, TextureFormat.RGBAHalf, true, true)
                    {
                        name = "Brush SDF Gradient", hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
                    };
                sdfGradientPixels ??= new Color[SdfGradientWidth];
                for (int i = 0; i < SdfGradientWidth; i++)
                {
                    Color color = HdrUtility.DecodePaintColor(evaluated.Evaluate(i / (float)(SdfGradientWidth - 1)));
                    sdfGradientPixels[i] = new Color(color.r * color.a, color.g * color.a, color.b * color.a, color.a);
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
