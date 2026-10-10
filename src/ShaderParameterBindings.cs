using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderParameterBindings : IDisposable
    {
        private readonly Dictionary<string, WhimTexGradientTexture> gradients = new Dictionary<string, WhimTexGradientTexture>();
        private readonly Dictionary<string, WhimTexCurveTexture> curves = new Dictionary<string, WhimTexCurveTexture>();
        private Texture2D transparent;

        internal void Apply(Material material, ShaderFXParameter value, ShaderFXParameter declaration, Vector2 dimensions)
        {
            string prefix = declaration.type == ShaderFXParameterType.Gradient || declaration.type == ShaderFXParameterType.Curve
                ? declaration.InternalPrefix : null;
            switch (declaration.type)
            {
                case ShaderFXParameterType.Gradient:
                    if (!gradients.TryGetValue(prefix, out var gradient))
                        gradients.Add(prefix, gradient = new WhimTexGradientTexture());
                    value.gradientValue ??= new WhimTexGradient();
                    material.SetTexture(prefix + "Gradient", gradient.GetTexture(value.gradientValue));
                    material.SetFloat(prefix + "GradientWrap", (float)value.gradientValue.WrapMode);
                    break;
                case ShaderFXParameterType.Curve:
                    if (!curves.TryGetValue(prefix, out var curve))
                        curves.Add(prefix, curve = new WhimTexCurveTexture());
                    value.curveValue ??= WhimTexCurveTexture.Default();
                    material.SetTexture(prefix + "Curve", curve.GetTexture(value.curveValue));
                    break;
                case ShaderFXParameterType.Texture2D when value.textureSource == ShaderFXTextureSource.None:
                    if (transparent == null)
                    {
                        transparent = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                        transparent.SetPixel(0, 0, Color.clear);
                        transparent.Apply(false, true);
                    }
                    material.SetTexture(declaration.name, transparent);
                    break;
                default:
                    value.SetValue(material, declaration, dimensions);
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
