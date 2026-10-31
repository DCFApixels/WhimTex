using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal static class ShaderGraphStorage
    {
        // The same stored inputs participate in validation, remapping and render stamps.
        // Keep enumeration allocation-free because stamps run during interactive editing.
        internal static TextureParameters TextureLayerParameters(ShaderGraph graph) => new TextureParameters(graph, true);
        internal static TextureParameters AllTextureParameters(ShaderGraph graph) => new TextureParameters(graph, false);

        internal readonly struct TextureParameters
        {
            readonly List<ShaderFXParameter> values;
            readonly bool layersOnly;
            internal TextureParameters(ShaderGraph graph, bool layersOnly) { values = graph?.parameters; this.layersOnly = layersOnly; }
            public Enumerator GetEnumerator() => new Enumerator(values, layersOnly);
            internal struct Enumerator
            {
                readonly List<ShaderFXParameter> values;
                readonly bool layersOnly;
                int index;
                internal Enumerator(List<ShaderFXParameter> values, bool layersOnly) { this.values = values; this.layersOnly = layersOnly; index = -1; }
                public ShaderFXParameter Current => values[index];
                public bool MoveNext()
                {
                    while (values != null && ++index < values.Count)
                        if (values[index] is ShaderFXParameter p && p.type == ShaderFXParameterType.Texture2D &&
                            (!layersOnly || p.textureSource == ShaderFXTextureSource.Layer)) return true;
                    return false;
                }
            }
        }

        internal static void RemapTextureLayers(ShaderGraph graph, Dictionary<string, string> ids, bool clearExternal)
        {
            foreach (var parameter in TextureLayerParameters(graph))
            {
                if (string.IsNullOrEmpty(parameter.textureLayerId)) continue;
                if (ids.TryGetValue(parameter.textureLayerId, out string id)) parameter.textureLayerId = id;
                else if (clearExternal) parameter.textureLayerId = null;
            }
        }

        internal static void ValidateInputs(WhimTexDocument document, Layer layer, ShaderGraph graph)
        {
            foreach (var parameter in TextureLayerParameters(graph))
                if (!string.IsNullOrEmpty(parameter.textureLayerId) && !document.IsUsableStoredShaderTexture(layer, parameter.textureLayerId))
                    throw new WhimTexDocumentException("Invalid or cyclic Shader texture input: " + layer.Id + "/" + parameter.name);
            foreach (var parameter in AllTextureParameters(graph))
                if (parameter.textureSource == ShaderFXTextureSource.Texture &&
                    WhimTexDocumentService.IsOwnOutput(document, parameter.textureValue))
                    throw new WhimTexDocumentException("A Shader layer cannot sample its own document output: " + layer.Id + "/" + parameter.name);
        }
    }
}
