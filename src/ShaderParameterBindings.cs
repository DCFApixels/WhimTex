using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderParameterBindings : IDisposable
    {
        private readonly Dictionary<int, WhimTexGradientTexture> gradients = new Dictionary<int, WhimTexGradientTexture>();
        private readonly Dictionary<int, WhimTexCurveTexture> curves = new Dictionary<int, WhimTexCurveTexture>();
        private Texture2D transparent;

        internal void Apply(Material material, ShaderFXParameter value, ShaderFXParameter declaration, Vector2 dimensions, float pixelScale = 1f)
        {
            switch (declaration.type)
            {
                case ShaderFXParameterType.Gradient:
                    int gradientId = declaration.GradientPropertyId;
                    if (!gradients.TryGetValue(gradientId, out var gradient))
                        gradients.Add(gradientId, gradient = new WhimTexGradientTexture());
                    value.gradientValue ??= new WhimTexGradient();
                    material.SetTexture(gradientId, gradient.GetTexture(value.gradientValue));
                    material.SetFloat(declaration.GradientWrapPropertyId, (float)value.gradientValue.WrapMode);
                    break;
                case ShaderFXParameterType.Curve:
                    int curveId = declaration.CurvePropertyId;
                    if (!curves.TryGetValue(curveId, out var curve))
                        curves.Add(curveId, curve = new WhimTexCurveTexture());
                    value.curveValue ??= WhimTexCurveTexture.Default();
                    material.SetTexture(curveId, curve.GetTexture(value.curveValue));
                    break;
                case ShaderFXParameterType.Texture2D when value.textureSource == ShaderFXTextureSource.None:
                    if (transparent == null)
                    {
                        transparent = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                        transparent.SetPixel(0, 0, Color.clear);
                        transparent.Apply(false, true);
                    }
                    material.SetTexture(declaration.PropertyId, transparent);
                    break;
                default:
                    value.SetValue(material, declaration, dimensions, pixelScale);
                    break;
            }
        }

        public void Dispose()
        {
            foreach (var gradient in gradients.Values) gradient.Dispose();
            foreach (var curve in curves.Values) curve.Dispose();
            gradients.Clear();
            curves.Clear();
            if (transparent != null) UnityEngine.Object.DestroyImmediate(transparent);
            transparent = null;
        }
    }
}
