using System;
using System.Collections.Generic;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        // Deliberately not an ExecuteJson envelope: clipboard data can only create a detached tree.
        internal sealed class ProceduralClipboard : IDisposable
        {
            internal WhimTexDocument Document;
            internal bool HasCanvas;
            internal readonly List<ShaderFX> Effects = new List<ShaderFX>();
            internal readonly List<string> Warnings = new List<string>();
            internal void Compile()
            {
                foreach (var effect in Effects)
                {
                    effect.TryPrepareDocumentEffect(out string warning);
                    if (warning != null && !Warnings.Contains(warning))
                        Warnings.Add(warning);
                }
                ValidateTargets(Document, null);
                foreach (var effect in Effects)
                    foreach (var parameter in effect.TextureLayerParameters())
                        Require(Document.IsUsableShaderTexture(effect, parameter.textureLayerId),
                            "Invalid or cyclic FX texture source: " + parameter.name);
            }
            public void Dispose()
            {
                if (Document != null)
                {
                    // Decoded images belong to the detached layers and are not destroyed with the document.
                    DestroyOwnedTextures(Document.layers);
                    UnityEngine.Object.DestroyImmediate(Document);
                }
                foreach (var effect in Effects)
                    if (effect != null) UnityEngine.Object.DestroyImmediate(effect);
                Effects.Clear();
                Document = null;
            }

            private static void DestroyOwnedTextures(List<Layer> layers)
            {
                foreach (Layer layer in layers)
                {
                    if (layer == null) continue;
                    if (layer.Behaviour is DrawingLayerBehaviour drawing && drawing.StoredTexture != null &&
                        !UnityEditor.AssetDatabase.Contains(drawing.StoredTexture))
                        UnityEngine.Object.DestroyImmediate(drawing.StoredTexture);
                    if (layer.AsGroup() is Layer group) DestroyOwnedTextures(group.layers);
                }
            }
        }

        internal static bool IsProceduralClipboard(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string start = text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            return (start.StartsWith("{") || start.StartsWith("```")) &&
                (text.IndexOf("\"format\"", StringComparison.Ordinal) >= 0 ||
                 text.IndexOf("'format'", StringComparison.Ordinal) >= 0 || text.IndexOf(WhimTexDocumentJson.Format, StringComparison.Ordinal) >= 0);
        }

        internal static ProceduralClipboard ReadProceduralClipboard(string text, int width, int height)
        {
            using var read = WhimTexDocumentJson.ReadForInsertion(text, width, height, false);
            var data = new ProceduralClipboard { HasCanvas = read.HasCanvasSize };
            data.Effects.AddRange(read.Effects);
            data.Warnings.AddRange(read.Warnings);
            data.Document = read.TakeDocument();
            return data;
        }

        internal static void PreparePortableDestination(ProceduralClipboard data, WhimTexDocument destination)
        {
            foreach (var layer in Enumerate(data.Document.layers))
                if (layer.Behaviour is FileLayerBehaviour file && file.sourceTexture != null &&
                    WhimTexDocumentService.IsOwnOutput(destination, file.sourceTexture))
                {
                    file.sourceTexture = null;
                    data.Warnings.Add(layer.layerName + ": a document cannot reference its own texture; the File layer will be empty.");
                }
        }

        internal static string WritePortableClipboard(WhimTexDocument document, List<Layer> requested)
            => WritePortableClipboardReport(document, requested, out _);

        internal static string WritePortableClipboardReport(WhimTexDocument document, List<Layer> requested, out List<string> warnings)
        {
            var result = WhimTexDocumentJson.WriteLayers(document, requested,
                new WhimTexJsonWriteOptions { AllowDrawingOmission = true });
            warnings = new List<string>(result.Warnings);
            return result.Json;
        }

    }
}
