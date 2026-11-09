using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderParameterBindings : IDisposable
    {
        internal readonly struct Binding
        {
            internal readonly ShaderParameter value, declaration;
            internal Binding(ShaderParameter value, ShaderParameter declaration)
            { this.value = value; this.declaration = declaration; }
        }

        // Cache references, not values. Public lists can change through editing, API calls or Undo.
        internal sealed class Plan
        {
            readonly bool byId;
            readonly Dictionary<(string, ShaderParameterType), ShaderParameter> lookup = new();
            Shape[] valueShape, declarationShape;
            Binding[] bindings;
            internal Binding[] Textures { get; private set; }
            internal Plan(bool byId) { this.byId = byId; }

            readonly struct Shape
            {
                readonly ShaderParameter parameter;
                readonly string key;
                readonly ShaderParameterType type;
                readonly string valueType;
                internal Shape(ShaderParameter p, bool byId)
                { parameter = p; key = byId ? p?.id : p?.name; type = p == null ? default : p.type; valueType = byId ? p?.ValueType : null; }
                internal bool Matches(ShaderParameter p, bool byId) => ReferenceEquals(parameter, p) &&
                    (p == null || key == (byId ? p.id : p.name) && type == p.type && (!byId || valueType == p.ValueType));
            }

            bool Matches(Shape[] shape, IReadOnlyList<ShaderParameter> values)
            {
                if (shape == null || shape.Length != (values?.Count ?? 0)) return false;
                for (int i = 0; i < shape.Length; i++) if (!shape[i].Matches(values[i], byId)) return false;
                return true;
            }

            (string, ShaderParameterType) Key(ShaderParameter p) => (byId ? p.id : p.name, byId ? default : p.type);

            internal Binding[] Prepare(IReadOnlyList<ShaderParameter> values, IReadOnlyList<ShaderParameter> declarations)
            {
                if (Matches(valueShape, values) && Matches(declarationShape, declarations)) return bindings;
                valueShape = new Shape[values?.Count ?? 0]; declarationShape = new Shape[declarations?.Count ?? 0];
                lookup.Clear();
                for (int i = 0; i < valueShape.Length; i++)
                {
                    var p = values[i]; valueShape[i] = new Shape(p, byId);
                    if (p == null || byId && p.id == null) continue;
                    // Code FX keep the first name/type match; graph IDs retain their last-match rule.
                    if (byId) lookup[Key(p)] = p;
                    else lookup.TryAdd(Key(p), p);
                }
                int count = 0, textures = 0;
                for (int i = 0; i < declarationShape.Length; i++)
                {
                    var p = declarations[i]; declarationShape[i] = new Shape(p, byId);
                    if (p == null) continue;
                    count++;
                    if (p.type == ShaderParameterType.Texture2D) textures++;
                }
                bindings = count == 0 ? Array.Empty<Binding>() : new Binding[count];
                Textures = textures == 0 ? Array.Empty<Binding>() : new Binding[textures];
                count = textures = 0;
                for (int i = 0; i < declarationShape.Length; i++)
                {
                    var p = declarations[i];
                    if (p == null) continue;
                    lookup.TryGetValue(Key(p), out var value);
                    if (value == null || byId && (value.type != p.type || value.ValueType != p.ValueType)) value = p;
                    var binding = new Binding(value, p);
                    bindings[count++] = binding;
                    if (p.type == ShaderParameterType.Texture2D) Textures[textures++] = binding;
                }
                lookup.Clear();
                return bindings;
            }
        }

        private readonly Dictionary<int, WhimTexGradientTexture> gradients = new Dictionary<int, WhimTexGradientTexture>();
        private readonly Dictionary<int, WhimTexCurveTexture> curves = new Dictionary<int, WhimTexCurveTexture>();
        private Texture2D transparent;

        internal static void BindContext(Material material, in LayerRenderContext context, bool effectContext = false)
        {
            var document = context.activeDocument;
            bool viewportInput = effectContext && document.RenderingViewport;
            float width = viewportInput ? Mathf.Ceil(document.width / context.scaleMultiplier) : context.width;
            float height = viewportInput ? Mathf.Ceil(document.height / context.scaleMultiplier) : context.height;
            material.SetVector("_InputSize", new Vector4(width, height, 1f / width, 1f / height));
            material.SetVector("_CanvasSize", new Vector4(document.width, document.height, 1f / document.width, 1f / document.height));
            material.SetFloat("_RenderScale", context.scaleMultiplier);
            material.SetFloat("_PixelScale", context.pixelScale);
            context.shaderContext.Bind(material);
            if (!effectContext) return;
            var viewport = document.ViewportToCanvas;
            material.SetVector("_WhimTex_Viewport", viewportInput
                ? new Vector4((float)viewport.m02, (float)viewport.m12, (float)viewport.m00, (float)viewport.m11)
                : new Vector4(0, 0, 1, 1));
            if (viewportInput) ShaderTextureView.ForContext(null, context).BindMetadata(material, ShaderTextureView.MainMapId, ShaderTextureView.MainSizeId);
            else ShaderTextureView.BindFull(material, ShaderTextureView.MainMapId, ShaderTextureView.MainSizeId);
        }

        internal Texture2D Transparent
        {
            get
            {
                if (transparent == null)
                {
                    transparent = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                    transparent.SetPixel(0, 0, Color.clear);
                    transparent.Apply(false, true);
                }
                return transparent;
            }
        }

        internal void Apply(Material material, Binding[] plan, in LayerRenderContext context, bool retainValues = false)
        {
            var dimensions = new Vector2(context.activeDocument.width, context.activeDocument.height);
            foreach (var binding in plan)
            {
                var value = binding.value; var declaration = binding.declaration;
                Apply(material, value, declaration, dimensions, context.pixelScale, textureViews: true);
                if (!retainValues || ReferenceEquals(value, declaration)) continue;
                RetainValues(value, declaration);
            }
        }

        internal static void RetainValues(ShaderParameter value, ShaderParameter declaration)
        {
            // Retain the last bound values when a later graph/layer draft changes a declaration.
            declaration.floatValue = value.floatValue; declaration.vectorValue = value.vectorValue;
            declaration.colorValue = value.colorValue; declaration.pixelUnits = value.pixelUnits;
            declaration.transformValue = value.transformValue;
            declaration.gradientValue = value.gradientValue; declaration.curveValue = value.curveValue;
            declaration.integerValues = value.integerValues;
            declaration.textureSource = value.textureSource;
            declaration.textureLayerId = value.textureLayerId;
            declaration.CopyTextureValue(value);
        }

        internal void Apply(Material material, ShaderParameter value, ShaderParameter declaration, Vector2 dimensions, float pixelScale = 1f, bool textureViews = false)
        {
            switch (declaration.type)
            {
                case ShaderParameterType.Gradient:
                    int gradientId = declaration.GradientPropertyId;
                    if (!gradients.TryGetValue(gradientId, out var gradient))
                        gradients.Add(gradientId, gradient = new WhimTexGradientTexture());
                    value.gradientValue ??= new WhimTexGradient();
                    material.SetTexture(gradientId, gradient.GetTexture(value.gradientValue));
                    material.SetFloat(declaration.GradientWrapPropertyId, (float)value.gradientValue.WrapMode);
                    break;
                case ShaderParameterType.Curve:
                    int curveId = declaration.CurvePropertyId;
                    if (!curves.TryGetValue(curveId, out var curve))
                        curves.Add(curveId, curve = new WhimTexCurveTexture());
                    value.curveValue ??= WhimTexCurveTexture.Default();
                    material.SetTexture(curveId, curve.GetTexture(value.curveValue));
                    break;
                case ShaderParameterType.Texture2D when value.textureSource == ShaderTextureSource.None:
                    material.SetTexture(declaration.PropertyId, Transparent);
                    break;
                default:
                    value.SetValue(material, declaration, dimensions, pixelScale);
                    break;
            }
            if (textureViews && declaration.type == ShaderParameterType.Texture2D)
                ShaderTextureView.BindFull(material, declaration.TextureMapId, declaration.TextureSizeId);
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
