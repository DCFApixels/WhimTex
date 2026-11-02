using System;
using System.Collections.Generic;
using System.IO;
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
                ValidateShaderProgramStorage(shader.program, effectContext: false);
                if (!Enum.IsDefined(typeof(ShaderLayerInputMode), shader.inputMode))
                    throw new WhimTexDocumentException("Unknown Shader Input mode.");
            }
            else if (value is ShaderFX effect) ValidateShaderProgramStorage(effect.Program);
            else if (value is ShaderProgram program) ValidateShaderProgramStorage(program);
            // Standalone graph/program payloads have no host yet. Layer validation below
            // rejects Self in both stored modes; FX may bind it to their incoming image.
            else if (value is ShaderGraph graph) ShaderGraphCompiler.ValidateStorage(graph, effectContext: true);
            else if (value is ShaderGraphSource source) ShaderGraphSources.ValidateSource(source);
        }

        internal static void ValidateShaderProgramStorage(ShaderProgram program, bool effectContext = true)
        {
            if (program == null) throw new WhimTexDocumentException("Shader program data is missing.");
            if (!Enum.IsDefined(typeof(ShaderLayerMode), program.mode))
                throw new WhimTexDocumentException("Unknown Shader program mode.");
            if (program.graph == null) throw new WhimTexDocumentException("Shader graph data is missing.");
            ShaderGraphCompiler.ValidateStorage(program.graph, effectContext);
            if (program.code == null || program.codeParameters == null)
                throw new WhimTexDocumentException("Shader Code data is missing.");
            ShaderGraphSources.CodeByteCount(program.code);
            if (program.compiledCode != null) ShaderGraphSources.CodeByteCount(program.compiledCode);
            ValidateShaderCodeParameters(program.codeParameters);
            if (!effectContext)
                foreach (var parameter in program.codeParameters)
                    if (parameter.type == ShaderFXParameterType.Texture2D && parameter.textureSource == ShaderFXTextureSource.Self)
                        throw new WhimTexDocumentException("A Shader layer cannot sample its own output: " + parameter.name);
            if (string.IsNullOrEmpty(program.presetId) ? program.presetVersion != 0 :
                !(Guid.TryParseExact(program.presetId, "N", out var id) || Guid.TryParseExact(program.presetId, "D", out id)) || id == Guid.Empty || program.presetVersion < 1)
                throw new WhimTexDocumentException("A Shader preset reference requires a nonempty GUID and positive version, or null and zero.");
        }

        // The same codec serves documents, presets and API snapshots, including inactive data.
        internal static JObject ShaderProgramSnapshot(ShaderProgram program, WhimTexDocument document = null, string sourcePath = null)
            => new Writer(new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }, document)
                .ObjectFields(program, sourcePath: sourcePath);

        internal static ShaderProgram ReadShaderProgram(JObject data)
            => (ShaderProgram)new Reader().Value(data, typeof(ShaderProgram));

        internal static string PortableShaderCode(string code, string sourcePath, bool draft)
        {
            if (string.IsNullOrEmpty(code)) return code;
            try
            {
                string portable = ShaderFXIconMetadata.MakePortable(code, sourcePath);
                return portable.Contains("#") ? ShaderFXSourceBuilder.ExportIncludes(portable, sourcePath) : portable;
            }
            // An invalid edit must remain editable. Executable code must have a complete backup.
            catch (Exception error) when (draft && (error is IOException || error is InvalidOperationException || error is FormatException))
            { return code; }
        }

        internal static void ValidateStoredShaderPrograms(WhimTexDocument document, List<Layer> roots = null)
        {
            if (document == null) return;
            int count = 0;
            void Visit(List<Layer> layers)
            {
                if (layers == null) return;
                foreach (var layer in layers)
                {
                    if (layer == null) continue;
                    if (++count > 1024) throw new WhimTexDocumentException("At most 1024 layers are supported.");
                    if (layer.Behaviour is ShaderLayerBehaviour shader)
                    {
                        ValidateShaderProgramInputs(document, layer, shader.program, effectContext: false);
                        ShaderGraphStorage.ValidateInputs(document, layer, shader);
                    }
                    if (layer.fx != null)
                        foreach (var effect in layer.fx)
                            if (effect != null) ValidateShaderProgramInputs(document, layer, effect.Program, effectContext: true);
                    Visit(layer.children);
                }
            }
            Visit(roots ?? document.layers);
        }

        internal static void ValidateShaderProgramInputs(WhimTexDocument document, Layer layer, ShaderProgram program, bool effectContext)
        {
            ValidateShaderProgramStorage(program, effectContext);
            var textures = new ShaderGraphStorage.TextureParameters(program.graph.parameters, program.codeParameters, false);
            foreach (var parameter in textures)
            {
                if (!effectContext && parameter.textureSource == ShaderFXTextureSource.Self)
                    throw new WhimTexDocumentException("A Shader layer cannot sample its own output: " + parameter.name);
                if (parameter.textureSource == ShaderFXTextureSource.Layer && !string.IsNullOrEmpty(parameter.textureLayerId) &&
                    !document.IsUsableStoredShaderTexture(layer, parameter.textureLayerId))
                    throw new WhimTexDocumentException("Invalid or cyclic Shader texture input: " + parameter.name);
                if (parameter.textureSource == ShaderFXTextureSource.Texture && WhimTexDocumentService.IsOwnOutput(document, parameter.textureValue))
                    throw new WhimTexDocumentException("A Shader program cannot sample its own document output: " + parameter.name);
            }
        }

        internal static JObject ShaderGraphSnapshot(ShaderGraph graph, WhimTexDocument document)
            => new Writer(new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }, document).ObjectFields(graph);

        internal static ShaderGraph ReadShaderGraph(JObject data)
            => (ShaderGraph)new Reader().Value(data, typeof(ShaderGraph));

        internal static JObject ShaderLayerSnapshot(ShaderLayerBehaviour shader, WhimTexDocument document)
            => new Writer(new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }, document).ObjectFields(shader);

        internal static UnityEngine.Texture2D ReadShaderInputTexture(JToken data)
        {
            if (data.Type != JTokenType.Null && !(data is JObject asset && asset["$asset"] is JObject))
                throw new WhimTexDocumentException("shader.inputTexture requires an $asset identity or null.");
            return (UnityEngine.Texture2D)new Reader().Value(data, typeof(UnityEngine.Texture2D));
        }

        internal static void SetShaderInputTexture(ShaderLayerBehaviour shader, UnityEngine.Texture2D texture, JToken data)
        {
            shader.inputTexture = texture;
            const string field = nameof(ShaderLayerBehaviour.inputTexture);
            if (MissingAssets.TryGetValue(shader, out var missing)) missing.Remove(field);
            if (texture == null && data.Type != JTokenType.Null)
                MissingAssets.GetOrCreateValue(shader)[field] = data.DeepClone();
        }

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
                    !Enum.IsDefined(typeof(ShaderFXTextureSource), parameter.textureSource))
                    throw new WhimTexDocumentException("Unknown Shader Code texture source.");
                for (int i = 0; i < parameter.Components; i++)
                    ShaderFXNumericValue.Check(parameter.ValueType, parameter.GetNumber(i));
            }
        }
    }
}
