using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        // Deliberately not an ExecuteJson envelope: clipboard data can only create a detached tree.
        internal sealed class ProceduralClipboard : IDisposable
        {
            internal TextureCompositor Document;
            internal bool HasCanvas;
            internal FilterMode? CanvasFilter;
            internal readonly List<ShaderFX> Effects = new List<ShaderFX>();
            // Drawing layers that own no pixels yet: the image is fetched before the tree is pasted.
            internal readonly List<(string url, Layer layer)> Images = new List<(string, Layer)>();
            internal void Compile()
            {
                foreach (var effect in Effects)
                    try { effect.ApplyAgentDraft(); }
                    catch (Exception error) { throw new FormatException(effect.name + ": " + error.Message, error); }
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
                Images.Clear();
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
                text.IndexOf("whimtex.layers", StringComparison.Ordinal) >= 0;
        }

        internal static ProceduralClipboard ReadProceduralClipboard(string text, int width, int height)
        {
            Require(text != null && text.Length <= 1024 * 1024, "Layer JSON must be at most 1 MiB.");
            text = text.Trim().TrimStart('\uFEFF');
            if (text.StartsWith("```"))
            {
                int newline = text.IndexOf('\n');
                Require(newline >= 0 && (text.Substring(0, newline).Trim() == "```json" || text.Substring(0, newline).Trim() == "```") &&
                    text.EndsWith("```"), "Copy one complete JSON block, without surrounding prose.");
                text = text.Substring(newline + 1, text.Length - newline - 4).Trim();
            }
            JObject root = Parse(text);
            Keys(root, "format", "version", "canvas", "layers");
            Require(Text(root, "format") == "whimtex.layers", "format must be whimtex.layers.");
            Require(Int(root, "version", 0, 1, 1) == 1, "Supported layer JSON version: 1.");
            var result = new ProceduralClipboard();
            try
            {
                if (root["canvas"] != null)
                {
                    var canvas = Obj(root["canvas"], "canvas");
                    Keys(canvas, "width", "height", "filter");
                    if (canvas["filter"] != null)
                        result.CanvasFilter = Enum(canvas, "filter", FilterMode.Bilinear);
                    width = Int(canvas, "width", 0, 1, 16384);
                    height = Int(canvas, "height", 0, 1, 16384);
                    Require(width > 0 && height > 0 && (long)width * height <= MaxCanvasPixels,
                        "canvas requires width and height, at most 16,777,216 pixels.");
                    result.HasCanvas = true;
                }
                result.Document = ScriptableObject.CreateInstance<TextureCompositor>();
                result.Document.hideFlags = HideFlags.HideAndDontSave;
                result.Document.width = width;
                result.Document.height = height;
                var ids = new Dictionary<string, Layer>(StringComparer.Ordinal);
                var targets = new List<(TargetedLayerBehaviour effect, string id, string path)>();
                int count = 0;
                ReadLayers(root["layers"], result.Document.layers, 0);
                foreach (var target in targets)
                {
                    Require(ids.TryGetValue(target.id, out var layer), target.path + ": unknown target " + target.id);
                    target.effect.inputMode = EffectInputMode.Specific;
                    target.effect.TargetLayerId = layer.Id;
                }
                ValidateTargets(result.Document, null);
                return result;

                void ReadLayers(JToken token, List<Layer> destination, int depth)
                {
                    Require(depth <= 8, "At most 8 nested groups are supported.");
                    Require(token is JArray array && array.Count > 0, "layers/children must be a nonempty array.");
                    foreach (JToken item in (JArray)token)
                    {
                        Require(++count <= 128, "At most 128 layers are supported per paste.");
                        try
                        {
                            var node = Obj(item, item.Path);
                            Keys(node, "id", "type", "name", "properties", "transform", "children", "target", "fx", "url");
                            string type = Text(node, "type");
                            Require(type == "color" || type == "gradient" || type == "noise" || type == "shape" ||
                                type == "outline" || type == "sdf" || type == "normalMap" || type == "blur" ||
                                type == "makeSeamless" || type == "shaderProcessor" || type == "drawing" || type == "group",
                                "Unsupported procedural layer type: " + type);
                            Layer layer = LayerTypeRegistry.Find(type).CreateLayer();
                            layer.AssignNewId();
                            layer.layerName = Text(node, "name", result.Document.AllocateLayerName(layer));
                            Require(layer.layerName.Length <= 128, "name must be at most 128 characters.");
                            destination.Add(layer);
                            string id = Text(node, "id");
                            if (id != null)
                            {
                                Require(id.Length > 0 && id.Length <= 64 && !ids.ContainsKey(id), "id must be unique and 1..64 characters.");
                                ids.Add(id, layer);
                            }
                            if (node["properties"] != null)
                            {
                                var properties = (JObject)Obj(node["properties"], "properties").DeepClone();
                                Require(properties["name"] == null && properties["source"] == null && properties["brush"] == null,
                                    "Use name on the layer; source and brush are not supported.");
                                JToken options = properties["gradientOptions"];
                                properties.Remove("gradientOptions");
                                SetLayer(result.Document, layer, properties);
                                if (options != null) SetClipboardGradient(layer, Obj(options, "gradientOptions"));
                            }
                            if (node["transform"] != null)
                            {
                                var transform = Obj(node["transform"], "transform");
                                Keys(transform, "position", "scale", "pivot", "rotation", "tiling");
                                SetTransform(result.Document, layer, transform);
                            }
                            if (node["url"] != null)
                            {
                                Require(layer.Behaviour is DrawingLayerBehaviour, "url is only supported on Drawing layers.");
                                Require(result.Images.Count < 16, "At most 16 linked images per paste.");
                                Require(Uri.TryCreate(Text(node, "url"), UriKind.Absolute, out Uri link) &&
                                    (link.Scheme == "http" || link.Scheme == "https"), "url must be an absolute http or https link.");
                                Require(node["transform"]?["scale"] == null,
                                    "A URL Drawing layer derives its scale from the downloaded image; set position, pivot, rotation or tiling instead.");
                                // A decoded web image is 8-bit sRGB, exactly like the plain URL paste.
                                if (node["properties"]?["colorRange"] == null) layer.colorRange = LayerColorRange.Standard;
                                result.Images.Add((link.AbsoluteUri, layer));
                            }
                            if (layer.Behaviour is TargetedLayerBehaviour effect)
                            {
                                string target = Text(node, "target");
                                if (target != null) targets.Add((effect, target, item.Path));
                                else effect.inputMode = EffectInputMode.Previous;
                            }
                            else Require(node["target"] == null, "target is only supported on targeted effect layers.");
                            if (node["children"] != null)
                            {
                                Require(layer.IsGroup, "Only groups have children.");
                                ReadLayers(node["children"], layer.layers, depth + 1);
                            }
                            if (node["fx"] != null)
                            {
                                Require(node["fx"] is JArray, "fx must be an array.");
                                foreach (JToken entry in (JArray)node["fx"])
                                {
                                    Require(result.Effects.Count < 16, "At most 16 custom shaders per paste.");
                                    JObject spec = Obj(entry, "fx");
                                    Keys(spec, "name", "code");
                                    Require(spec["code"]?.Type == JTokenType.String, "fx.code must be a string.");
                                    string code = (string)spec["code"];
                                    Require(code.Length > 0 && code.Length <= 65536, "fx.code must be 1..65,536 characters.");
                                    // No preprocessor or texture GUIDs: pasted effects must be self-contained.
                                    Require(code.IndexOf('#') < 0 && code.IndexOf("guid:", StringComparison.OrdinalIgnoreCase) < 0 && code.IndexOf('\\') < 0,
                                        "Clipboard HLSL cannot contain preprocessor directives, includes, line continuations or asset GUIDs.");
                                    var values = ShaderFXMetadata.Parse(code, false, out _);
                                    Require(values.Count <= 32, "At most 32 parameters per clipboard shader.");
                                    ShaderFX shader = ShaderFX.CreateAgentDraft(result.Document, code, values);
                                    result.Effects.Add(shader);
                                    shader.name = Text(spec, "name", layer.layerName + " FX");
                                    layer.modifiers.Add(shader);
                                }
                            }
                        }
                        catch (Exception error) { throw new FormatException(item.Path + ": " + error.Message, error); }
                    }
                }
            }
            catch { result.Dispose(); throw; }
        }

        private static void SetClipboardGradient(Layer layer, JObject options)
        {
            Require(layer.Behaviour is GradientLayerBehaviour, "gradientOptions requires a Gradient layer.");
            Keys(options, "type", "repetitions", "wrap", "mode", "smoothness");
            var gradient = (GradientLayerBehaviour)layer.Behaviour;
            gradient.gradientType = Enum(options, "type", gradient.gradientType);
            gradient.circularRepetitions = Number(options, "repetitions", gradient.circularRepetitions, .00001f, 1000f);
            gradient.circularWrapMode = Enum(options, "wrap", gradient.circularWrapMode);
            gradient.gradient.Mode = Enum(options, "mode", gradient.gradient.Mode);
            gradient.gradient.Smoothness = Number(options, "smoothness", gradient.gradient.Smoothness, 0, 1);
        }
    }
}
