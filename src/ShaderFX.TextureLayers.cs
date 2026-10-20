using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        private readonly struct ParameterShape
        {
            internal readonly ShaderFXParameter parameter;
            readonly string name;
            readonly ShaderFXParameterType type;
            internal ParameterShape(ShaderFXParameter p) { parameter = p; name = p?.name; type = p == null ? default : p.type; }
            internal bool Matches(ShaderFXParameter p) => ReferenceEquals(parameter, p) && (p == null || name == p.name && type == p.type);
        }
        internal readonly struct ParameterBinding
        {
            internal readonly ShaderFXParameter value, declaration;
            internal ParameterBinding(ShaderFXParameter value, ShaderFXParameter declaration) { this.value = value; this.declaration = declaration; }
        }
        [NonSerialized] private ParameterShape[] draftShape, appliedShape;
        [NonSerialized] private ParameterBinding[] parameterPlan, texturePlan;

        // Lists and declarations are publicly mutable through editor/API/Undo paths.
        // Validate structure on use; cache only the matching, never parameter values.
        internal ParameterBinding[] PrepareParameterPlan()
        {
            static bool Matches(ParameterShape[] shape, List<ShaderFXParameter> list)
            {
                if (shape == null || shape.Length != list.Count) return false;
                for (int i = 0; i < shape.Length; i++) if (!shape[i].Matches(list[i])) return false;
                return true;
            }
            if (Matches(draftShape, parameters) && Matches(appliedShape, appliedParameters)) return parameterPlan;
            draftShape = new ParameterShape[parameters.Count]; appliedShape = new ParameterShape[appliedParameters.Count];
            var values = new Dictionary<(string, ShaderFXParameterType), ShaderFXParameter>();
            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i]; draftShape[i] = new ParameterShape(p);
                // Preserve the first-match rule, including duplicate draft declarations.
                if (p != null) values.TryAdd((p.name, p.type), p);
            }
            var all = new List<ParameterBinding>(appliedParameters.Count); var textures = new List<ParameterBinding>();
            for (int i = 0; i < appliedParameters.Count; i++)
            {
                var p = appliedParameters[i]; appliedShape[i] = new ParameterShape(p);
                if (p == null) continue;
                if (!values.TryGetValue((p.name, p.type), out var value)) value = p;
                var binding = new ParameterBinding(value, p); all.Add(binding);
                if (p.type == ShaderFXParameterType.Texture2D) textures.Add(binding);
            }
            parameterPlan = all.ToArray(); texturePlan = textures.ToArray();
            return parameterPlan;
        }

        internal readonly struct TextureParameterEnumerable : IEnumerable<ShaderFXParameter>
        {
            readonly ParameterBinding[] bindings;
            readonly bool layersOnly;
            internal TextureParameterEnumerable(ParameterBinding[] bindings, bool layersOnly) { this.bindings = bindings; this.layersOnly = layersOnly; }
            public Enumerator GetEnumerator() => new Enumerator(bindings, layersOnly);
            IEnumerator<ShaderFXParameter> IEnumerable<ShaderFXParameter>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            internal struct Enumerator : IEnumerator<ShaderFXParameter>
            {
                readonly ParameterBinding[] bindings;
                readonly bool layersOnly;
                int index;
                internal Enumerator(ParameterBinding[] bindings, bool layersOnly) { this.bindings = bindings; this.layersOnly = layersOnly; index = -1; }
                public ShaderFXParameter Current => bindings[index].value;
                object IEnumerator.Current => Current;
                public bool MoveNext()
                {
                    while (++index < bindings.Length)
                        if (!layersOnly || Current.textureSource == ShaderFXTextureSource.Layer) return true;
                    return false;
                }
                public void Reset() => index = -1;
                public void Dispose() { }
            }
        }
        private ParameterBinding[] PrepareTexturePlan()
        {
            // Most FX have no texture declarations. Draft changes cannot introduce
            // a live input until it is applied; avoid validating unrelated values here.
            bool hasTextures = false;
            foreach (var p in appliedParameters)
                if (p != null && p.type == ShaderFXParameterType.Texture2D) { hasTextures = true; break; }
            if (!hasTextures) return Array.Empty<ParameterBinding>();
            PrepareParameterPlan(); return texturePlan;
        }
        internal TextureParameterEnumerable TextureLayerParameters() => new TextureParameterEnumerable(PrepareTexturePlan(), true);

        // Persistence/preflight must also validate drafts before a shader has been applied.
        internal IEnumerable<ShaderFXParameter> StoredTextureLayerParameters()
        {
            foreach (var value in parameters)
                if (value != null && value.type == ShaderFXParameterType.Texture2D &&
                    value.textureSource == ShaderFXTextureSource.Layer) yield return value;
        }

        internal TextureParameterEnumerable TextureParameters() => new TextureParameterEnumerable(PrepareTexturePlan(), false);

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

    public sealed partial class WhimTexDocument
    {
        internal bool IsUsableStoredShaderTexture(Layer consumer, string sourceId)
        {
            var source = FindLayer(sourceId);
            return consumer != null && source?.Behaviour != null && !(source.Behaviour is PendingLayerBehaviour) &&
                !LayerDependsOn(source, consumer, new HashSet<Layer>(), storedFx: true);
        }

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
                    if (layer.fx != null && layer.fx.Contains(effect))
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

        internal ShaderTextureBindings BindShaderTextureLayers(ShaderFX effect, Layer consumer, in LayerRenderContext context, RenderTexture self)
        {
            ShaderTextureBindings bindings = null;
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                foreach (var parameter in effect.TextureParameters())
                {
                    if (parameter.textureSource == ShaderFXTextureSource.Texture) continue;
                    bindings ??= ShaderTextureBindings.Rent();
                    if (parameter.textureSource == ShaderFXTextureSource.Self && self != null)
                    {
                        bindings.inputs.Add((parameter.PropertyId, self));
                        continue;
                    }
                    // Validate each distinct requested layer only once in this binding scope.
                    // Never retain graph validity between renders: layers/FX may change directly.
                    string request = parameter.textureSource == ShaderFXTextureSource.Layer ? parameter.textureLayerId ?? "" : "";
                    if (bindings.requests.TryGetValue(request, out var requested))
                    {
                        bindings.inputs.Add((parameter.PropertyId, requested)); continue;
                    }
                    string key = parameter.textureSource == ShaderFXTextureSource.Layer && IsUsableShaderTexture(consumer, parameter.textureLayerId) ? parameter.textureLayerId : "";
                    if (!bindings.sources.TryGetValue(key, out var pixels))
                    {
                        var source = key.Length == 0 ? null : FindLayer(key);
                        if (source != null) pixels = RenderShaderTextureSource(source, consumer, context.width, context.height, context.scaleMultiplier);
                        pixels ??= GetClearRenderTexture(context.width, context.height);
                        bindings.sources.Add(key, pixels);
                    }
                    bindings.requests.Add(request, pixels);
                    bindings.inputs.Add((parameter.PropertyId, pixels));
                }
                return bindings;
            }
            catch { bindings?.Dispose(); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }

        // Isolate the render factory's captured state from the allocation-free None/Self path.
        private RenderTexture RenderShaderTextureSource(Layer source, Layer consumer, int w, int h, float scale)
        {
            if (!TryFindLayer(source, out var list, out int index)) return null;
            return CachedEffectRender(source, "fx-input", w, h, scale, false, () =>
            {
                var stack = new HashSet<Layer> { consumer };
                return source.IsGroup
                    ? RenderGroupEffectInput(source, w, h, scale, stack, true, includeDisabled: true)
                    : RenderStandalone(list, index, w, h, scale, stack, includeDisabled: true);
            });
        }
    }

    internal sealed class ShaderTextureBindings : IDisposable
    {
        private static readonly Stack<ShaderTextureBindings> pool = new Stack<ShaderTextureBindings>();
        internal readonly Dictionary<string, RenderTexture> sources = new Dictionary<string, RenderTexture>();
        internal readonly Dictionary<string, RenderTexture> requests = new Dictionary<string, RenderTexture>();
        internal readonly List<(int property, RenderTexture texture)> inputs = new List<(int, RenderTexture)>();
        private Material material;
        private bool rented;
        internal static ShaderTextureBindings Rent()
        {
            var value = pool.Count == 0 ? new ShaderTextureBindings() : pool.Pop();
            value.rented = true; return value;
        }
        internal void Apply(Material target)
        {
            material = target;
            foreach (var input in inputs) target.SetTexture(input.property, input.texture);
        }
        public void Dispose()
        {
            if (!rented) return;
            rented = false;
            if (material != null)
                foreach (var input in inputs) material.SetTexture(input.property, null);
            foreach (var texture in sources.Values) ProfiledRenderTexture.ReleaseTemporary(texture);
            sources.Clear(); requests.Clear(); inputs.Clear(); material = null;
            // Reentrant layer renders own distinct leases; bound idle storage is capped.
            if (pool.Count < 16 && inputs.Capacity <= 256 && sources.EnsureCapacity(0) <= 256 && requests.EnsureCapacity(0) <= 256) pool.Push(this);
        }
    }
}
