using System;
using System.Collections;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal static class ShaderProgramStorage
    {
        // Native Undo retains diagnostics/ownership; document formats restore these at runtime.
        internal static bool IsTransientField(string name) => name is
            "codeCompilationStatus" or "graphCompilationStatus" or "embeddedOwner";

        internal static ShaderGraph CopyWorkingGraph(ShaderGraph graph)
        {
            var copy = (ShaderGraph)CopyData(graph);
            WhimTexDocumentJson.PreserveShaderCodeParameterAssets(graph.parameters, copy.parameters);
            return copy;
        }
        private static object CopyData(object value)
        {
            if (value == null || value is string || value is UnityEngine.Object || value.GetType().IsValueType) return value;
            if (value is ShaderParameter parameter) return parameter.Copy();
            if (value is Array array)
            {
                var copy = Array.CreateInstance(value.GetType().GetElementType(), array.Length);
                for (int i = 0; i < array.Length; i++) copy.SetValue(CopyData(array.GetValue(i)), i);
                return copy;
            }
            if (value is IList list)
            {
                var copy = (IList)Activator.CreateInstance(value.GetType(), list.Count);
                foreach (var item in list) copy.Add(CopyData(item));
                return copy;
            }
            var result = Activator.CreateInstance(value.GetType());
            foreach (var field in WhimTexDocumentJson.Fields(value.GetType())) field.SetValue(result, CopyData(field.GetValue(value)));
            return result;
        }
        // Storage owns both modes; rendering depends only on the selected mode.
        // Concrete enumerators keep interactive dependency/stamp walks allocation-free.
        internal static TextureParameters TextureLayerParameters(ShaderGraph graph) => new TextureParameters(graph?.parameters, null, true, graph: graph);
        internal static TextureParameters AllTextureParameters(ShaderGraph graph) => new TextureParameters(graph?.parameters, null, false, graph: graph);
        internal static TextureParameters InlineTextureLayerParameters(ShaderProgram program)
            => new TextureParameters(null, null, true, graph: program.draftGraph, retainedGraph: program.workingGraph);
        internal static TextureParameters ActiveTextureLayerParameters(ShaderLayerBehaviour shader)
            => new TextureParameters(ActiveParameters(shader), WorkingParameters(shader), true,
                graph: shader?.mode == ShaderProgramMode.Graph ? shader.draftGraph : null,
                retainedGraph: shader?.mode == ShaderProgramMode.Graph ? shader.program.workingGraph : null);
        internal static TextureParameters ActiveTextureParameters(ShaderLayerBehaviour shader)
            => new TextureParameters(ActiveParameters(shader), WorkingParameters(shader), false,
                graph: shader?.mode == ShaderProgramMode.Graph ? shader.draftGraph : null,
                retainedGraph: shader?.mode == ShaderProgramMode.Graph ? shader.program.workingGraph : null);
        internal static TextureParameters StoredTextureLayerParameters(ShaderLayerBehaviour shader)
            => StoredTextureParameters(shader?.program, true);
        internal static TextureParameters StoredTextureParameters(ShaderLayerBehaviour shader)
            => StoredTextureParameters(shader?.program);
        internal static TextureParameters StoredTextureParameters(ShaderProgram program, bool layersOnly = false)
            => new TextureParameters(program?.draftGraph?.parameters, program?.draftCodeParameters, layersOnly,
                program?.workingGraph?.parameters, program?.workingCodeParameters, program?.draftGraph, program?.workingGraph);
        private static List<ShaderParameter> WorkingParameters(ShaderLayerBehaviour shader)
            => shader?.mode == ShaderProgramMode.Code ? shader.program.workingCodeParameters : shader?.program.workingGraph?.parameters;
        private static List<ShaderParameter> ActiveParameters(ShaderLayerBehaviour shader)
            => shader?.mode == ShaderProgramMode.Code ? shader.draftCodeParameters : shader?.draftGraph?.parameters;

        internal readonly struct TextureParameters
        {
            readonly List<ShaderParameter> values, additional, workingGraph, workingCode;
            readonly ShaderGraph graph, retainedGraph;
            readonly bool layersOnly;
            internal TextureParameters(List<ShaderParameter> values, List<ShaderParameter> additional, bool layersOnly,
                List<ShaderParameter> workingGraph = null, List<ShaderParameter> workingCode = null,
                ShaderGraph graph = null, ShaderGraph retainedGraph = null)
            { this.values = values; this.additional = additional; this.layersOnly = layersOnly; this.workingGraph = workingGraph; this.workingCode = workingCode; this.graph = graph; this.retainedGraph = retainedGraph; }
            public Enumerator GetEnumerator() => new Enumerator(values, additional, layersOnly, workingGraph, workingCode, graph, retainedGraph);
            internal struct Enumerator
            {
                List<ShaderParameter> values;
                readonly List<ShaderParameter> additional, workingGraph, workingCode;
                readonly ShaderGraph graph, retainedGraph;
                readonly bool layersOnly;
                int index;
                int section;
                int graphSection, nodeIndex, inputIndex;
                internal Enumerator(List<ShaderParameter> values, List<ShaderParameter> additional, bool layersOnly,
                    List<ShaderParameter> workingGraph, List<ShaderParameter> workingCode, ShaderGraph graph, ShaderGraph retainedGraph)
                { this.values = values; this.additional = additional; this.layersOnly = layersOnly; this.workingGraph = workingGraph; this.workingCode = workingCode;
                    this.graph = graph; this.retainedGraph = retainedGraph; index = -1; section = 0; graphSection = nodeIndex = inputIndex = 0; Current = null; }
                public ShaderParameter Current { get; private set; }
                public bool MoveNext()
                {
                    while (section < 4)
                    {
                        while (values != null && ++index < values.Count)
                            if (values[index] is ShaderParameter p && p.type == ShaderParameterType.Texture2D &&
                                (!layersOnly || p.textureSource == ShaderTextureSource.Layer)) { Current = p; return true; }
                        ++section;
                        values = section == 1 ? additional : section == 2 ? workingGraph : workingCode; index = -1;
                    }
                    while (section < 6)
                    {
                        var owner = section == 4 ? graph : retainedGraph;
                        while (owner != null && graphSection <= (owner.subGraphs?.Count ?? 0))
                        {
                            var nodes = graphSection == 0 ? owner.nodes : owner.subGraphs[graphSection - 1]?.nodes;
                            while (nodes != null && nodeIndex < nodes.Count)
                            {
                                var node = nodes[nodeIndex];
                                if (inputIndex == 0)
                                {
                                    inputIndex = 1;
                                    if (node?.kind == ShaderGraphNodeKind.Value && node.literal?.type == ShaderParameterType.Texture2D &&
                                        (!layersOnly || node.literal.textureSource == ShaderTextureSource.Layer))
                                    { Current = node.literal; return true; }
                                }
                                var inputs = node?.inputs;
                                while (!layersOnly && inputs != null && inputIndex <= inputs.Count)
                                {
                                    var value = inputs[inputIndex++ - 1]?.resource;
                                    if (value?.type == ShaderParameterType.Texture2D) { Current = value; return true; }
                                }
                                nodeIndex++; inputIndex = 0;
                            }
                            graphSection++; nodeIndex = 0;
                        }
                        section++; graphSection = nodeIndex = inputIndex = 0;
                    }
                    return false;
                }
            }
        }

        internal static void RemapTextureLayers(ShaderGraph graph, Dictionary<string, string> ids, bool clearExternal)
            => RemapTextureLayers(TextureLayerParameters(graph), ids, clearExternal);

        internal static void RemapTextureLayers(ShaderProgram program, Dictionary<string, string> ids, bool clearExternal)
            => RemapTextureLayers(StoredTextureParameters(program, true), ids, clearExternal);

        internal static void RemapTextureLayers(List<ShaderParameter> parameters, Dictionary<string, string> ids, bool clearExternal)
            => RemapTextureLayers(new TextureParameters(parameters, null, true), ids, clearExternal);

        internal static void RemapTextureLayers(ShaderLayerBehaviour shader, Dictionary<string, string> ids, bool clearExternal)
        {
            RemapTextureLayers(shader?.program, ids, clearExternal);
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
                if (parameter.textureSource == ShaderTextureSource.Self)
                    throw new WhimTexDocumentException("A Shader layer cannot sample its own output: " + layer.Id + "/" + parameter.name);
                if (parameter.textureSource == ShaderTextureSource.Layer && !string.IsNullOrEmpty(parameter.textureLayerId) &&
                    !document.IsUsableStoredShaderTexture(layer, parameter.textureLayerId))
                    throw new WhimTexDocumentException("Invalid or cyclic Shader texture input: " + layer.Id + "/" + parameter.name);
                if (parameter.textureSource == ShaderTextureSource.Texture &&
                    WhimTexDocumentService.IsOwnOutput(document, parameter.textureValue))
                    throw new WhimTexDocumentException("A Shader layer cannot sample its own document output: " + layer.Id + "/" + parameter.name);
            }
        }
    }
}
