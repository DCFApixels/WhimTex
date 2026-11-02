using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal static class ShaderGraphStorage
    {
        // Storage owns both modes; rendering depends only on the selected mode.
        // Concrete enumerators keep interactive dependency/stamp walks allocation-free.
        internal static TextureParameters TextureLayerParameters(ShaderGraph graph) => new TextureParameters(graph?.parameters, null, true);
        internal static TextureParameters AllTextureParameters(ShaderGraph graph) => new TextureParameters(graph?.parameters, null, false);
        internal static TextureParameters ActiveTextureLayerParameters(ShaderLayerBehaviour shader)
            => new TextureParameters(ActiveParameters(shader), null, true);
        internal static TextureParameters ActiveTextureParameters(ShaderLayerBehaviour shader)
            => new TextureParameters(ActiveParameters(shader), null, false);
        internal static TextureParameters StoredTextureLayerParameters(ShaderLayerBehaviour shader)
            => new TextureParameters(shader?.graph?.parameters, shader?.codeParameters, true);
        internal static TextureParameters StoredTextureParameters(ShaderLayerBehaviour shader)
            => new TextureParameters(shader?.graph?.parameters, shader?.codeParameters, false);
        private static List<ShaderFXParameter> ActiveParameters(ShaderLayerBehaviour shader)
            => shader?.mode == ShaderLayerMode.Code ? shader.codeParameters : shader?.graph?.parameters;

        internal readonly struct TextureParameters
        {
            readonly List<ShaderFXParameter> values, additional;
            readonly bool layersOnly;
            internal TextureParameters(List<ShaderFXParameter> values, List<ShaderFXParameter> additional, bool layersOnly)
            { this.values = values; this.additional = additional; this.layersOnly = layersOnly; }
            public Enumerator GetEnumerator() => new Enumerator(values, additional, layersOnly);
            internal struct Enumerator
            {
                List<ShaderFXParameter> values;
                readonly List<ShaderFXParameter> additional;
                readonly bool layersOnly;
                int index;
                bool inAdditional;
                internal Enumerator(List<ShaderFXParameter> values, List<ShaderFXParameter> additional, bool layersOnly)
                { this.values = values; this.additional = additional; this.layersOnly = layersOnly; index = -1; inAdditional = false; }
                public ShaderFXParameter Current => values[index];
                public bool MoveNext()
                {
                    while (true)
                    {
                        while (values != null && ++index < values.Count)
                            if (values[index] is ShaderFXParameter p && p.type == ShaderFXParameterType.Texture2D &&
                                (!layersOnly || p.textureSource == ShaderFXTextureSource.Layer)) return true;
                        if (inAdditional) return false;
                        values = additional; index = -1; inAdditional = true;
                    }
                }
            }
        }

        internal static void RemapTextureLayers(ShaderGraph graph, Dictionary<string, string> ids, bool clearExternal)
            => RemapTextureLayers(TextureLayerParameters(graph), ids, clearExternal);

        internal static void RemapTextureLayers(ShaderLayerBehaviour shader, Dictionary<string, string> ids, bool clearExternal)
        {
            RemapTextureLayers(StoredTextureLayerParameters(shader), ids, clearExternal);
            if (shader == null || string.IsNullOrEmpty(shader.inputLayerId)) return;
            if (ids.TryGetValue(shader.inputLayerId, out string id)) shader.inputLayerId = id;
            else if (clearExternal) shader.inputLayerId = null;
        }

        private static void RemapTextureLayers(TextureParameters parameters, Dictionary<string, string> ids, bool clearExternal)
        {
            foreach (var parameter in parameters)
            {
                if (string.IsNullOrEmpty(parameter.textureLayerId)) continue;
                if (ids.TryGetValue(parameter.textureLayerId, out string id)) parameter.textureLayerId = id;
                else if (clearExternal) parameter.textureLayerId = null;
            }
        }

        internal static void ValidateInputs(WhimTexDocument document, Layer layer, ShaderGraph graph)
            => ValidateInputs(document, layer, AllTextureParameters(graph));

        internal static void ValidateInputs(WhimTexDocument document, Layer layer, ShaderLayerBehaviour shader)
        {
            ValidateInputs(document, layer, StoredTextureParameters(shader));
            if (!document.IsUsableShaderInput(layer, shader, true))
                throw new WhimTexDocumentException("Invalid or cyclic Shader Input: " + layer.Id);
        }

        private static void ValidateInputs(WhimTexDocument document, Layer layer, TextureParameters parameters)
        {
            foreach (var parameter in parameters)
            {
                if (parameter.textureSource == ShaderFXTextureSource.Self)
                    throw new WhimTexDocumentException("A Shader layer cannot sample its own output: " + layer.Id + "/" + parameter.name);
                if (parameter.textureSource == ShaderFXTextureSource.Layer && !string.IsNullOrEmpty(parameter.textureLayerId) &&
                    !document.IsUsableStoredShaderTexture(layer, parameter.textureLayerId))
                    throw new WhimTexDocumentException("Invalid or cyclic Shader texture input: " + layer.Id + "/" + parameter.name);
                if (parameter.textureSource == ShaderFXTextureSource.Texture &&
                    WhimTexDocumentService.IsOwnOutput(document, parameter.textureValue))
                    throw new WhimTexDocumentException("A Shader layer cannot sample its own document output: " + layer.Id + "/" + parameter.name);
            }
        }
    }
}
