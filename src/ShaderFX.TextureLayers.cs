using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using ParameterBinding = DCFApixels.WhimTex.ShaderParameterBindings.Binding;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        [NonSerialized] private ShaderParameterBindings.Plan parameterPlan;
        internal ParameterBinding[] PrepareParameterPlan()
        {
            parameterPlan ??= new ShaderParameterBindings.Plan(byId: false);
            return parameterPlan.Prepare(parameters, workingParameters);
        }

        internal readonly struct TextureParameterEnumerable : IEnumerable<ShaderParameter>
        {
            readonly ParameterBinding[] bindings;
            readonly bool layersOnly;
            internal TextureParameterEnumerable(ParameterBinding[] bindings, bool layersOnly) { this.bindings = bindings; this.layersOnly = layersOnly; }
            public Enumerator GetEnumerator() => new Enumerator(bindings, layersOnly);
            IEnumerator<ShaderParameter> IEnumerable<ShaderParameter>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            internal struct Enumerator : IEnumerator<ShaderParameter>
            {
                readonly ParameterBinding[] bindings;
                readonly bool layersOnly;
                int index;
                internal Enumerator(ParameterBinding[] bindings, bool layersOnly) { this.bindings = bindings; this.layersOnly = layersOnly; index = -1; }
                public ShaderParameter Current => bindings[index].value;
                object IEnumerator.Current => Current;
                public bool MoveNext()
                {
                    while (++index < bindings.Length)
                        if (!layersOnly || Current.textureSource == ShaderTextureSource.Layer) return true;
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
            foreach (var p in workingParameters)
                if (p != null && p.type == ShaderParameterType.Texture2D) { hasTextures = true; break; }
            if (!hasTextures) return Array.Empty<ParameterBinding>();
            PrepareParameterPlan(); return parameterPlan.Textures;
        }
        internal TextureParameterEnumerable TextureLayerParameters() => new TextureParameterEnumerable(PrepareTexturePlan(), true);

        // Persistence/preflight must also validate drafts before a shader has been applied.
        internal IEnumerable<ShaderParameter> StoredTextureLayerParameters()
        {
            foreach (var value in ShaderProgramStorage.StoredTextureParameters(Program, true))
                yield return value;
        }

        internal TextureParameterEnumerable TextureParameters() => new TextureParameterEnumerable(PrepareTexturePlan(), false);

        internal bool HasLayerTextureSources
        {
            get { foreach (var parameter in TextureLayerParameters()) return true; return false; }
        }

        internal void RemapTextureLayers(Dictionary<string, string> ids, bool clearExternal)
        {
            ShaderProgramStorage.RemapTextureLayers(Program, ids, clearExternal);
            if (!ReferenceEquals(workingParameters, Program.workingCodeParameters))
                ShaderProgramStorage.RemapTextureLayers(workingParameters, ids, clearExternal);
        }
    }

    public sealed partial class WhimTexDocument
    {
        internal bool IsUsableShaderInput(Layer consumer, ShaderLayerBehaviour shader, bool stored = false)
        {
            if (consumer == null || shader == null) return false;
            switch (shader.inputMode)
            {
                case ShaderLayerInputMode.None: return true;
                case ShaderLayerInputMode.Texture: return !WhimTexDocumentService.IsOwnOutput(this, shader.inputTexture);
                case ShaderLayerInputMode.Layer:
                    return string.IsNullOrEmpty(shader.inputLayerId) || (stored
                        ? IsUsableStoredShaderTexture(consumer, shader.inputLayerId)
                        : IsUsableShaderTexture(consumer, shader.inputLayerId));
                case ShaderLayerInputMode.CompositeBelow:
                    var visited = new HashSet<Layer>();
                    foreach (var below in CompositeBackdropLayers(consumer, includeDisabled: stored))
                        if (LayerDependsOn(below, consumer, visited, stored)) return false;
                    return true;
                default: return false;
            }
        }

        internal ShaderLayerInput ResolveShaderInput(ShaderLayerBehaviour shader, in LayerRenderContext context)
        {
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try { return ResolveShaderInputCore(shader, context); }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }

        private ShaderLayerInput ResolveShaderInputCore(ShaderLayerBehaviour shader, in LayerRenderContext context)
        {
            if (!IsUsableShaderInput(shader.Owner, shader)) return default;
            switch (shader.inputMode)
            {
                case ShaderLayerInputMode.Texture: return new ShaderLayerInput(shader.inputTexture);
                case ShaderLayerInputMode.Layer:
                    var layer = FindLayer(shader.inputLayerId);
                    if (layer == null) return default;
                    var image = RenderShaderTextureSource(layer, shader.Owner, context.width, context.height, context.scaleMultiplier);
                    return new ShaderLayerInput(image, context, owned: true);
                case ShaderLayerInputMode.CompositeBelow:
                    if (context.input != null) return new ShaderLayerInput(context.input, context, owned: false);
                    if (!TryFindLayer(shader.Owner, out _, out _)) return default;
                    var stack = new HashSet<Layer> { shader.Owner };
                    var composite = RenderBackdropBefore(shader.Owner, stack, context.width, context.height, context.scaleMultiplier);
                    return new ShaderLayerInput(composite, context, owned: true);
                default: return default;
            }
        }

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
            // BindEffect refreshes these declarations from public values and retains the
            // generated uniform names used by graph texture ports.
            if (effect.Mode == ShaderProgramMode.Graph)
                return BindShaderTextureLayers(effect.BoundGraphParameters, consumer, context, self);
            ShaderTextureBindings bindings = null;
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                foreach (var parameter in effect.TextureParameters())
                    BindShaderTextureParameter(parameter, consumer, context, self, ref bindings);
                return bindings;
            }
            catch { bindings?.Dispose(); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }

        internal ShaderTextureBindings BindShaderTextureLayers(IReadOnlyList<ShaderParameter> parameters, Layer consumer, in LayerRenderContext context, RenderTexture self)
        {
            ShaderTextureBindings bindings = null;
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                for (int i = 0; i < parameters.Count; i++)
                    BindShaderTextureParameter(parameters[i], consumer, context, self, ref bindings);
                return bindings;
            }
            catch { bindings?.Dispose(); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }

        private void BindShaderTextureParameter(ShaderParameter parameter, Layer consumer, in LayerRenderContext context,
            RenderTexture self, ref ShaderTextureBindings bindings)
        {
            if (parameter == null || parameter.type != ShaderParameterType.Texture2D || parameter.textureSource == ShaderTextureSource.Texture) return;
            bindings ??= ShaderTextureBindings.Rent();
            if (parameter.textureSource == ShaderTextureSource.Self && self != null)
            {
                bindings.inputs.Add((parameter, ShaderTextureView.ForContext(self, context))); return;
            }
            // Validate once per source per render, without retaining graph validity between renders.
            string request = parameter.textureSource == ShaderTextureSource.Layer ? parameter.textureLayerId ?? "" : "";
            if (bindings.requests.TryGetValue(request, out var requested))
            {
                bindings.inputs.Add((parameter, ShaderTextureView.ForContext(requested, context))); return;
            }
            string key = parameter.textureSource == ShaderTextureSource.Layer && IsUsableShaderTexture(consumer, parameter.textureLayerId) ? parameter.textureLayerId : "";
            if (!bindings.sources.TryGetValue(key, out var pixels))
            {
                var source = key.Length == 0 ? null : FindLayer(key);
                if (source != null) pixels = RenderShaderTextureSource(source, consumer, context.width, context.height, context.scaleMultiplier);
                pixels ??= GetClearRenderTexture(context.width, context.height);
                bindings.sources.Add(key, pixels);
            }
            bindings.requests.Add(request, pixels);
            bindings.inputs.Add((parameter, ShaderTextureView.ForContext(pixels, context)));
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

    internal readonly struct ShaderLayerInput : IDisposable
    {
        readonly Texture texture;
        internal Texture Texture => texture;
        readonly RenderTexture owned;
        readonly ShaderTextureView? view;
        internal ShaderLayerInput(Texture texture) { this.texture = texture; owned = null; view = null; }
        internal ShaderLayerInput(RenderTexture texture, in LayerRenderContext context, bool owned)
        {
            this.texture = texture; this.owned = owned ? texture : null;
            view = texture != null ? ShaderTextureView.ForContext(texture, context) : (ShaderTextureView?)null;
        }
        internal Texture Bind(Material material, Texture fallback)
        {
            var source = texture != null ? texture : fallback;
            material.SetTexture("_MainTex", source);
            material.SetFloat("_WhimTex_InputFilter", source.filterMode == FilterMode.Point ? 0 : 1);
            if (view.HasValue) view.Value.BindMetadata(material, ShaderTextureView.MainMapId, ShaderTextureView.MainSizeId);
            else ShaderTextureView.BindFull(material, ShaderTextureView.MainMapId, ShaderTextureView.MainSizeId);
            if (texture != null)
            {
                var size = view?.texelSize ?? new Vector4(1f / texture.width, 1f / texture.height, texture.width, texture.height);
                material.SetVector("_InputSize", new Vector4(size.z, size.w, size.x, size.y));
            }
            return source;
        }
        public void Dispose() { if (owned != null) ProfiledRenderTexture.ReleaseTemporary(owned); }
    }

    internal sealed class ShaderTextureBindings : IDisposable
    {
        private static readonly Stack<ShaderTextureBindings> pool = new Stack<ShaderTextureBindings>();
        internal readonly Dictionary<string, RenderTexture> sources = new Dictionary<string, RenderTexture>();
        internal readonly Dictionary<string, RenderTexture> requests = new Dictionary<string, RenderTexture>();
        internal readonly List<(ShaderParameter parameter, ShaderTextureView view)> inputs =
            new List<(ShaderParameter, ShaderTextureView)>();
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
            foreach (var input in inputs)
            {
                target.SetTexture(input.parameter.PropertyId, input.view.texture);
                input.view.BindMetadata(target, input.parameter.TextureMapId, input.parameter.TextureSizeId);
            }
        }
        public void Dispose()
        {
            if (!rented) return;
            rented = false;
            if (material != null)
                foreach (var input in inputs)
                {
                    material.SetTexture(input.parameter.PropertyId, null);
                    material.SetVector(input.parameter.TextureMapId, ShaderTextureView.Identity);
                    material.SetVector(input.parameter.TextureSizeId, Vector4.zero);
                }
            foreach (var texture in sources.Values) ProfiledRenderTexture.ReleaseTemporary(texture);
            sources.Clear(); requests.Clear(); inputs.Clear(); material = null;
            // Reentrant layer renders own distinct leases; bound idle storage is capped.
            if (pool.Count < 16 && inputs.Capacity <= 256 && sources.EnsureCapacity(0) <= 256 && requests.EnsureCapacity(0) <= 256) pool.Push(this);
        }
    }
}
