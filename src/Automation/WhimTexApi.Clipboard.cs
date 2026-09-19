using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
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
            internal readonly List<string> Warnings = new List<string>();
            // Drawing layers that own no pixels yet: the image is fetched before the tree is pasted.
            internal readonly List<(string url, Layer layer, bool fit)> Images = new List<(string, Layer, bool)>();
            internal void Compile()
            {
                foreach (var effect in Effects)
                    try { effect.ApplyAgentDraft(); }
                    catch (Exception error) { throw new FormatException(effect.name + ": " + error.Message, error); }
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
                var textureTargets = new List<(ShaderFXParameter parameter, string id)>();
                int count = 0;
                ReadLayers(root["layers"], result.Document.layers, 0);
                foreach (var target in targets)
                {
                    Require(ids.TryGetValue(target.id, out var layer), target.path + ": unknown target " + target.id);
                    target.effect.inputMode = EffectInputMode.Specific;
                    target.effect.TargetLayerId = layer.Id;
                }
                ValidateTargets(result.Document, null);
                foreach (var target in textureTargets)
                {
                    Require(ids.TryGetValue(target.id, out var layer), "Unknown FX texture layer: " + target.id);
                    target.parameter.textureSource = ShaderFXTextureSource.Layer;
                    target.parameter.textureLayerId = layer.Id;
                }
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
                            Keys(node, "id", "type", "name", "properties", "transform", "children", "target", "fx", "url", "asset", "contentOmitted");
                            string type = Text(node, "type");
                            Require(type == "color" || type == "gradient" || type == "noise" || type == "shape" ||
                                type == "outline" || type == "sdf" || type == "normalMap" || type == "blur" ||
                                type == "makeSeamless" || type == "shaderProcessor" || type == "drawing" || type == "file" || type == "group",
                                "Unsupported procedural layer type: " + type);
                            Layer layer = LayerTypeRegistry.Find(type).CreateLayer();
                            layer.AssignNewId();
                            layer.layerName = Text(node, "name", result.Document.AllocateLayerName(layer));
                            Require(layer.layerName.Length <= 128, "name must be at most 128 characters.");
                            destination.Add(layer);
                            if (node["contentOmitted"] != null)
                            {
                                Require(type == "drawing" || type == "file", "contentOmitted is only supported on Drawing and File layers.");
                                if (Bool(node, "contentOmitted"))
                                {
                                    Require(node["url"] == null && node["asset"] == null, "contentOmitted cannot be combined with url or asset.");
                                    result.Warnings.Add(layer.layerName + ": image content was not copied; the layer will be empty.");
                                }
                            }
                            if (node["asset"] != null)
                            {
                                Require(layer.Behaviour is FileLayerBehaviour, "asset is only supported on File layers.");
                                var file = (FileLayerBehaviour)layer.Behaviour;
                                ReadPortableFileAsset(file, Obj(node["asset"], "asset"), result.Warnings);
                            }
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
                                if (properties["fillMode"] != null)
                                {
                                    Require(layer.Behaviour is ColorFillLayerBehaviour, "fillMode requires a Color Fill layer.");
                                    ((ColorFillLayerBehaviour)layer.Behaviour).mode = Enum(properties, "fillMode", ColorFillLayerBehaviour.FillMode.Color);
                                    properties.Remove("fillMode");
                                }
                                SetLayer(result.Document, layer, properties);
                                if (options != null) SetClipboardGradient(layer, Obj(options, "gradientOptions"));
                            }
                            if (node["transform"] != null)
                            {
                                var transform = Obj(node["transform"], "transform");
                                Keys(transform, "position", "scale", "pivot", "rotation", "tiling", "matrix");
                                SetTransform(result.Document, layer, transform);
                            }
                            if (node["url"] != null)
                            {
                                Require(layer.Behaviour is DrawingLayerBehaviour, "url is only supported on Drawing layers.");
                                Require(result.Images.Count < 16, "At most 16 linked images per paste.");
                                Require(Uri.TryCreate(Text(node, "url"), UriKind.Absolute, out Uri link) &&
                                    (link.Scheme == "http" || link.Scheme == "https"), "url must be an absolute http or https link.");
                                // A decoded web image is 8-bit sRGB, exactly like the plain URL paste.
                                if (node["properties"]?["colorRange"] == null) layer.colorRange = LayerColorRange.Standard;
                                result.Images.Add((link.AbsoluteUri, layer, node["transform"]?["scale"] == null && node["transform"]?["matrix"] == null));
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
                                    Keys(spec, "name", "code", "enabled", "gradients", "textures");
                                    Require(spec["code"]?.Type == JTokenType.String, "fx.code must be a string.");
                                    string code = (string)spec["code"];
                                    Require(code.Length > 0 && code.Length <= 65536, "fx.code must be 1..65,536 characters.");
                                    ShaderFXSourceBuilder.ValidatePortableSource(code);
                                    var values = ShaderFXMetadata.Parse(code, false, out _);
                                    Require(values.Count <= 32, "At most 32 parameters per clipboard shader.");
                                    ShaderFX shader = ShaderFX.CreateAgentDraft(result.Document, code, values);
                                    result.Effects.Add(shader);
                                    shader.Active = Bool(spec, "enabled", true);
                                    if (spec["gradients"] != null)
                                        foreach (var field in Obj(spec["gradients"], "gradients").Properties())
                                        {
                                            var p = FindPortableParameter(shader, field.Name, ShaderFXParameterType.Gradient);
                                            Require(p != null, "Unknown gradient parameter: " + field.Name);
                                            p.gradientValue = ReadGradient(field.Value);
                                        }
                                    if (spec["textures"] != null)
                                        foreach (var field in Obj(spec["textures"], "textures").Properties())
                                        {
                                            var p = FindPortableParameter(shader, field.Name, ShaderFXParameterType.Texture2D);
                                            Require(p != null, "Unknown texture parameter: " + field.Name);
                                            var binding = Obj(field.Value, "texture binding");
                                            Keys(binding, "layer");
                                            textureTargets.Add((p, Text(binding, "layer")));
                                        }
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

        private static ShaderFXParameter FindPortableParameter(ShaderFX shader, string name, ShaderFXParameterType type)
        {
            foreach (var parameter in shader.Parameters)
                if (parameter.name == name && parameter.type == type) return parameter;
            return null;
        }

        private static void ReadPortableFileAsset(FileLayerBehaviour file, JObject asset, List<string> warnings)
        {
            Keys(asset, "guid", "localId");
            string guid = Text(asset, "guid");
            Require(guid != null && System.Text.RegularExpressions.Regex.IsMatch(guid, "\\A[0-9a-fA-F]{32}\\z"), "asset.guid must be a 32-digit hexadecimal GUID.");
            string id = Text(asset, "localId");
            long localId = 0;
            Require(id == null || long.TryParse(id, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out localId), "asset.localId must be a decimal Int64 string.");
            file.portableAssetGuid = guid;
            file.portableAssetLocalId = id;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
            {
                if (id == null) file.sourceTexture = AssetDatabase.LoadMainAssetAtPath(path) as Texture2D;
                else foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (candidate is Texture2D texture && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string _, out long foundId) && foundId == localId)
                    { file.sourceTexture = texture; break; }
            }
            if (file.sourceTexture == null)
                warnings.Add(file.layerName + ": the referenced texture asset was not found in this project; the File layer will be empty.");
        }

        internal static void PreparePortableDestination(ProceduralClipboard data, TextureCompositor destination)
        {
            foreach (var layer in Enumerate(data.Document.layers))
                if (layer.Behaviour is FileLayerBehaviour file && file.sourceTexture != null &&
                    WhimTexDocumentService.IsOwnOutput(destination, file.sourceTexture))
                {
                    file.sourceTexture = null;
                    data.Warnings.Add(layer.layerName + ": a document cannot reference its own texture; the File layer will be empty.");
                }
        }

        internal static string WritePortableClipboard(TextureCompositor document, List<Layer> requested)
            => WritePortableClipboardReport(document, requested, out _);

        internal static string WritePortableClipboardReport(TextureCompositor document, List<Layer> requested, out List<string> warnings)
        {
            var messages = new List<string>();
            warnings = messages;
            Require(document != null && requested != null && requested.Count > 0, "Select layers to copy.");
            var selected = new HashSet<Layer>(requested);
            var roots = new List<Layer>();
            var included = new HashSet<string>();
            void Collect(List<Layer> list, bool inside)
            {
                foreach (var layer in list)
                {
                    bool take = inside || selected.Contains(layer);
                    if (take)
                    {
                        Require(layer != null && layer.Behaviour != null && !ContainsReservation(layer), "Missing or reserved layers cannot be copied.");
                        if (!inside) roots.Add(layer);
                        included.Add(layer.Id);
                    }
                    if (layer?.IsGroup == true) Collect(layer.layers, take);
                }
            }
            Collect(document.layers, false);
            var snapshots = new Dictionary<string, JObject>();
            foreach (JObject item in (JArray)Snapshot(document, "")["layers"]) snapshots[(string)item["id"]] = item;
            string Reference(string id, string label)
            {
                Require(!string.IsNullOrEmpty(id) && included.Contains(id), label + ": include the referenced source layer in the selection.");
                return id;
            }
            JObject Transform(TextureTransform t)
            {
                var json = new JObject { ["pivot"] = new JArray(t.pivot.x, t.pivot.y), ["tiling"] = t.tiling.ToString() };
                if (t.storage == TransformStorage.Projective)
                {
                    var m = t.matrix;
                    json["matrix"] = new JArray(m.m00,m.m01,m.m02,m.m10,m.m11,m.m12,m.m20,m.m21,m.m22);
                }
                else
                {
                    json["position"] = new JArray(t.position.x,t.position.y);
                    json["scale"] = new JArray(t.scale.x,t.scale.y);
                    json["rotation"] = t.rotation;
                }
                return json;
            }
            JObject Write(Layer layer, bool root)
            {
                try
                {
                    var snapshot = snapshots[layer.Id];
                    var properties = (JObject)snapshot["settings"].DeepClone();
                    properties.Remove("name"); properties.Remove("brush"); properties.Remove("source");
                    if (layer.Behaviour is ColorFillLayerBehaviour fill) properties["fillMode"] = fill.mode.ToString();
                    if (snapshot["gradientKeys"] != null) properties["gradient"] = snapshot["gradientKeys"].DeepClone();
                    if (layer.Behaviour is GradientLayerBehaviour gradient)
                        properties["gradientOptions"] = new JObject { ["type"] = gradient.gradientType.ToString(),
                            ["repetitions"] = gradient.circularRepetitions, ["wrap"] = gradient.circularWrapMode.ToString() };
                    var node = new JObject { ["id"] = layer.Id, ["type"] = TypeName(layer), ["name"] = layer.layerName,
                        ["properties"] = properties, ["transform"] = Transform(root ? document.GetCanvasTransform(layer) : layer.transform) };
                    if (layer.Behaviour is DrawingLayerBehaviour drawing)
                    {
                        if (!string.IsNullOrEmpty(drawing.PortableImageUrl)) node["url"] = drawing.PortableImageUrl;
                        else
                        {
                            node["contentOmitted"] = true;
                            messages.Add(layer.layerName + ": Drawing pixels cannot be included; copied as an empty Drawing layer.");
                        }
                    }
                    if (layer.Behaviour is FileLayerBehaviour file)
                    {
                        string guid = file.portableAssetGuid, localId = file.portableAssetLocalId;
                        if (file.sourceTexture != null)
                        {
                            guid = null; localId = null;
                            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(file.sourceTexture, out string foundGuid, out long foundId))
                            { guid = foundGuid; localId = foundId.ToString(System.Globalization.CultureInfo.InvariantCulture); }
                        }
                        if (!string.IsNullOrEmpty(guid))
                        {
                            var asset = new JObject { ["guid"] = guid };
                            if (localId != null) asset["localId"] = localId;
                            node["asset"] = asset;
                            messages.Add(layer.layerName + ": copied an asset reference, not pixels. The same texture GUID/local ID must exist in the receiving project; otherwise the File layer will be empty.");
                        }
                        else
                        {
                            node["contentOmitted"] = true;
                            messages.Add(layer.layerName + ": no saved texture asset reference; copied as an empty File layer.");
                        }
                    }
                    if (layer.clippingMask) Reference(document.GetClippingBase(layer)?.Id, "Clipping mask");
                    if (layer.Behaviour is TargetedLayerBehaviour targeted)
                    {
                        string target = targeted.TargetLayerId;
                        if (targeted.inputMode == EffectInputMode.Previous && document.TryFindLayer(layer, out var container, out int index))
                            target = index + 1 < container.Count ? container[index + 1]?.Id : null;
                        node["target"] = Reference(target, "Target");
                    }
                    if (layer.IsGroup && layer.layers.Count > 0)
                    {
                        var children = new JArray();
                        foreach (var child in layer.layers) children.Add(Write(child, false));
                        node["children"] = children;
                    }
                    var effects = new JArray();
                    foreach (var modifier in layer.modifiers)
                    {
                        Require(modifier is ShaderFX, "Only HLSL Shader FX can be copied; Material/missing FX cannot be embedded.");
                        var fx = (ShaderFX)modifier;
                        Require(!fx.HasPendingChanges && !fx.LastApplyFailed, "Apply or fix pending shader changes before copying.");
                        var gradients = new JObject(); var textures = new JObject();
                        foreach (var p in fx.Parameters)
                        {
                            if (p.type == ShaderFXParameterType.Gradient) gradients[p.name] = GradientSnapshot(p.gradientValue);
                            if (p.type != ShaderFXParameterType.Texture2D) continue;
                            Require(p.textureSource != ShaderFXTextureSource.Texture || p.textureValue == null,
                                "FX texture assets cannot be embedded; use a layer, Self or None.");
                            if (p.textureSource == ShaderFXTextureSource.Layer)
                                textures[p.name] = new JObject { ["layer"] = Reference(p.textureLayerId, p.name) };
                        }
                        var entry = new JObject { ["name"] = fx.name, ["enabled"] = fx.Active,
                            ["code"] = ShaderFXPresetWriter.BuildPortableSource(fx) };
                        if (gradients.Count > 0) entry["gradients"] = gradients;
                        if (textures.Count > 0) entry["textures"] = textures;
                        effects.Add(entry);
                    }
                    if (effects.Count > 0) node["fx"] = effects;
                    return node;
                }
                catch (Exception error) { throw new FormatException(layer.layerName + ": " + error.Message, error); }
            }
            var layers = new JArray();
            foreach (var layer in roots) layers.Add(Write(layer, true));
            string text = new JObject { ["format"] = "whimtex.layers", ["version"] = 1,
                ["canvas"] = new JObject { ["width"] = document.width, ["height"] = document.height, ["filter"] = document.outputFilter.ToString() },
                ["layers"] = layers }.ToString();
            // Validate through the same parser as paste; never replace the clipboard with partial data.
            using (ReadProceduralClipboard(text, document.width, document.height)) { }
            return text;
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
