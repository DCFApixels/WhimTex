using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        internal IEnumerable<ShaderFXParameter> TextureLayerParameters()
        {
            foreach (var applied in appliedParameters)
            {
                if (applied == null || applied.type != ShaderFXParameterType.Texture2D) continue;
                var value = applied;
                foreach (var draft in parameters)
                    if (draft != null && draft.name == applied.name && draft.type == applied.type) { value = draft; break; }
                if (value.textureSource == ShaderFXTextureSource.Layer) yield return value;
            }
        }

        internal bool HasLayerTextureSources
        {
            get { foreach (var parameter in TextureLayerParameters()) return true; return false; }
        }

        internal void RemapTextureLayers(Dictionary<string, string> ids, bool clearExternal)
        {
            void Remap(List<ShaderFXParameter> values)
            {
                foreach (var p in values)
                {
                    if (p == null || p.textureSource != ShaderFXTextureSource.Layer || string.IsNullOrEmpty(p.textureLayerId)) continue;
                    if (ids.TryGetValue(p.textureLayerId, out string id)) p.textureLayerId = id;
                    else if (clearExternal) p.textureLayerId = null;
                }
            }
            Remap(parameters); Remap(appliedParameters);
        }
    }

    public sealed partial class TextureCompositor
    {
        internal bool IsUsableShaderTexture(Layer consumer, string sourceId)
        {
            var source = FindLayer(sourceId);
            return consumer != null && source?.Behaviour != null && !(source.Behaviour is PendingLayerBehaviour) &&
                !LayerDependsOn(source, consumer, new HashSet<Layer>());
        }

        internal bool IsUsableShaderTexture(ShaderFX effect, string sourceId)
        {
            var source = FindLayer(sourceId);
            if (source?.Behaviour == null || source.Behaviour is PendingLayerBehaviour) return false;
            bool found = false;
            bool Check(List<Layer> list)
            {
                foreach (var layer in list)
                {
                    if (layer == null) continue;
                    if (layer.modifiers != null && layer.modifiers.Contains(effect))
                    {
                        found = true;
                        if (!IsUsableShaderTexture(layer, sourceId)) return false;
                    }
                    if (layer.IsGroup && !Check(layer.children)) return false;
                }
                return true;
            }
            return Check(layers) && found;
        }

        internal void GetShaderTextureOptions(ShaderFX effect, List<Layer> result)
        {
            result.Clear(); result.Add(null);
            void Visit(List<Layer> list)
            {
                foreach (var layer in list)
                {
                    if (layer == null) continue;
                    if (IsUsableShaderTexture(effect, layer.Id)) result.Add(layer);
                    if (layer.IsGroup) Visit(layer.children);
                }
            }
            Visit(layers);
        }

        internal ShaderTextureBindings BindShaderTextureLayers(ShaderFX effect, Layer consumer, in LayerRenderContext context)
        {
            ShaderTextureBindings bindings = null;
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                foreach (var parameter in effect.TextureLayerParameters())
                {
                    bindings ??= new ShaderTextureBindings();
                    string key = IsUsableShaderTexture(consumer, parameter.textureLayerId) ? parameter.textureLayerId : "";
                    if (!bindings.sources.TryGetValue(key, out var pixels))
                    {
                        var source = key.Length == 0 ? null : FindLayer(key);
                        int w = context.width, h = context.height;
                        float scale = context.scaleMultiplier;
                        if (source != null && TryFindLayer(source, out var list, out int index))
                        {
                            pixels = CachedEffectRender(source, "fx-input", w, h, scale, false, () =>
                            {
                                var stack = new HashSet<Layer> { consumer };
                                return source.IsGroup
                                    ? RenderGroupEffectInput(source, w, h, scale, stack, true, includeDisabled: true)
                                    : RenderStandalone(list, index, w, h, scale, stack, includeDisabled: true);
                            });
                        }
                        pixels ??= GetClearRenderTexture(w, h);
                        bindings.sources.Add(key, pixels);
                    }
                    bindings.inputs.Add((parameter.name, pixels));
                }
                return bindings;
            }
            catch { bindings?.Dispose(); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }
    }

    internal sealed class ShaderTextureBindings : IDisposable
    {
        internal readonly Dictionary<string, RenderTexture> sources = new Dictionary<string, RenderTexture>();
        internal readonly List<(string name, RenderTexture texture)> inputs = new List<(string, RenderTexture)>();
        private Material material;
        internal void Apply(Material target)
        {
            material = target;
            foreach (var input in inputs) target.SetTexture(input.name, input.texture);
        }
        public void Dispose()
        {
            if (material != null)
                foreach (var input in inputs) material.SetTexture(input.name, null);
            foreach (var texture in sources.Values) RenderTexture.ReleaseTemporary(texture);
            sources.Clear(); inputs.Clear(); material = null;
        }
    }
}
