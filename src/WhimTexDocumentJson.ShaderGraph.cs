using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexDocumentJson
    {
        internal static void ValidateShaderGraphStorage(object value)
        {
            if (value is ShaderLayerBehaviour shader)
            {
                if (shader.graph == null) throw new WhimTexDocumentException("Shader graph data is missing.");
                shader.graph.Validate();
                if (!Enum.IsDefined(typeof(ShaderLayerMode), shader.mode))
                    throw new WhimTexDocumentException("Unknown Shader layer mode.");
                if (shader.code == null || shader.compiledCode == null || shader.codeParameters == null)
                    throw new WhimTexDocumentException("Shader Code data is missing.");
                ValidateShaderCodeParameters(shader.codeParameters);
            }
            else if (value is ShaderGraph graph) graph.Validate();
            else if (value is ShaderGraphSource source) ShaderGraphSources.ValidateSource(source);
        }

        internal static JObject ShaderGraphSnapshot(ShaderGraph graph, WhimTexDocument document)
            => new Writer(new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }, document).ObjectFields(graph);

        internal static ShaderGraph ReadShaderGraph(JObject data)
            => (ShaderGraph)new Reader().Value(data, typeof(ShaderGraph));

        internal static JObject ShaderLayerSnapshot(ShaderLayerBehaviour shader, WhimTexDocument document)
            => new Writer(new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }, document).ObjectFields(shader);

        internal static List<ShaderFXParameter> ReadShaderCodeParameters(JArray data)
        {
            var parameters = (List<ShaderFXParameter>)new Reader().Value(data, typeof(List<ShaderFXParameter>));
            ValidateShaderCodeParameters(parameters);
            return parameters;
        }

        internal static void PreserveShaderCodeParameterAssets(IReadOnlyList<ShaderFXParameter> previous,
            IReadOnlyList<ShaderFXParameter> next)
        {
            if (previous == null || next == null) return;
            foreach (var parameter in next)
                if (parameter != null)
                    foreach (var old in previous)
                        if (old != null && old.id == parameter.id && old.type == parameter.type &&
                            MissingAssets.TryGetValue(old, out var missing))
                        {
                            foreach (var asset in missing)
                                MissingAssets.GetOrCreateValue(parameter)[asset.Key] = asset.Value.DeepClone();
                            break;
                        }
        }

        private static void ValidateShaderCodeParameters(List<ShaderFXParameter> parameters)
        {
            if (parameters == null) throw new WhimTexDocumentException("Shader Code parameters are missing.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in parameters)
            {
                if (parameter == null || string.IsNullOrEmpty(parameter.name) ||
                    !Regex.IsMatch(parameter.name, @"\A[A-Za-z_][A-Za-z0-9_]*\z") || !names.Add(parameter.name) ||
                    !Enum.IsDefined(typeof(ShaderFXParameterType), parameter.type))
                    throw new WhimTexDocumentException("Invalid or duplicate Shader Code parameter.");
                if (parameter.type == ShaderFXParameterType.Texture2D &&
                    (!Enum.IsDefined(typeof(ShaderFXTextureSource), parameter.textureSource) || parameter.textureSource == ShaderFXTextureSource.Self))
                    throw new WhimTexDocumentException("Shader Code textures require an asset or another layer.");
                for (int i = 0; i < parameter.Components; i++)
                    ShaderFXNumericValue.Check(parameter.ValueType, parameter.GetNumber(i));
            }
        }
    }
}
