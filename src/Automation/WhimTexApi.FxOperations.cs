using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        // Preset IDs are GUIDs for project/package assets and paths for user-library files.
        public static string FxCatalog(string query = null, string presetId = null) => Respond(() =>
        {
            var result = Success();
            var entries = new JArray();
            foreach (var entry in ShaderFXCatalog.GetEntries())
            {
                string id = PresetId(entry);
                if (presetId != null && id != presetId) continue;
                if (!string.IsNullOrEmpty(query) && entry.menuPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var item = new JObject { ["id"] = id, ["path"] = entry.path, ["name"] = entry.menuPath,
                    ["kind"] = entry.assetPreset ? "asset" : "hlsl", ["error"] = entry.error };
                if (presetId != null && !entry.HasError)
                {
                    ShaderFX effect = null;
                    try
                    {
                        effect = ReadPreset(null, entry, false);
                        var layer = new Layer(new ColorFillLayerBehaviour());
                        layer.modifiers.Add(effect);
                        item["effect"] = LiveFxSnapshot(layer, null)[0];
                    }
                    catch (Exception error) { item["error"] = error.Message; }
                    finally { if (effect != null) Object.DestroyImmediate(effect); }
                }
                entries.Add(item);
            }
            Require(presetId == null || entries.Count == 1, "Preset ID not found. Read the catalog again.", "preset_not_found");
            result["presets"] = entries;
            return result;
        });

        private static string PresetId(ShaderFXCatalog.Entry entry) => string.IsNullOrEmpty(entry.guid) ? entry.path : entry.guid;

        private static ShaderFX ReadPreset(TextureCompositor owner, ShaderFXCatalog.Entry entry, bool execute)
        {
            Require(!entry.HasError, entry.error ?? "Invalid preset.", "invalid_preset");
            if (execute) return ShaderFX.FromCatalog(owner, entry);
            if (entry.assetPreset)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ShaderFX>(entry.path);
                Require(asset != null, "Preset asset no longer exists.", "preset_not_found");
                return asset.CloneForDocument(owner);
            }
            string source = ShaderFXCatalog.ReadSource(entry.path);
            var parameters = ShaderFXMetadata.Parse(source, true, out _);
            return ShaderFX.CreateAgentDraft(owner, source, parameters, entry.path);
        }

        private static void ApplyFxOperations(TextureCompositor document, Layer layer, JToken token, bool execute, Dictionary<string, Layer> aliases)
        {
            Require(token is JArray edits && edits.Count <= 32, "edits must be an array of at most 32 FX operations.");
            foreach (var item in (JArray)token)
            {
                var spec = Obj(item, "FX edit");
                string op = Text(spec, "op");
                if (op == "apply" || op == "applyAll")
                {
                    Keys(spec, "op", "index", "allowRasterize");
                    Require(op != "applyAll" || spec["index"] == null, "applyAll does not take index.");
                    int last = op == "applyAll" ? layer.modifiers.Count - 1 : RequiredFxIndex(layer, spec);
                    Require(last >= 0, "No FX to apply.");
                    Require(layer.Behaviour is DrawingLayerBehaviour || Bool(spec, "allowRasterize"),
                        "Applying FX rasterizes this layer/group. Supply allowRasterize:true to consent.", "rasterize_required");
                    if (execute) document.ApplyLayerFX(layer, last);
                    else
                    {
                        var remaining = layer.modifiers.GetRange(last + 1, layer.modifiers.Count - last - 1);
                        var placeholder = new DrawingLayerBehaviour();
                        placeholder.CopyRasterizedIdentityFrom(layer);
                        placeholder.transform = layer.transform;
                        layer.AdoptContent(placeholder);
                        layer.modifiers = remaining;
                    }
                    continue;
                }
                if (op == "remove" || op == "move")
                {
                    if (op == "remove") Keys(spec, "op", "index");
                    else Keys(spec, "op", "index", "toIndex");
                    int index = RequiredFxIndex(layer, spec);
                    Require(op != "move" || spec["toIndex"] != null, "move requires toIndex.");
                    int destination = op == "move" ? Int(spec, "toIndex", -1, 0, layer.modifiers.Count - 1) : 0;
                    var modifier = layer.modifiers[index];
                    layer.modifiers.RemoveAt(index);
                    if (op == "move") layer.modifiers.Insert(destination, modifier);
                    continue;
                }
                Require(op == "add" || op == "replace" || op == "set" || op == "copy", "Unknown FX edit: " + op);
                Keys(spec, "op", "index", "code", "includeBasePath", "presetId", "parameters", "enabled", "sourceLayer", "sourceIndex");
                bool insert = op == "add" || op == "copy";
                Require(op == "copy" || spec["sourceLayer"] == null && spec["sourceIndex"] == null, "Only copy takes sourceLayer/sourceIndex.");
                int at = insert ? Int(spec, "index", layer.modifiers.Count, 0, layer.modifiers.Count) : RequiredFxIndex(layer, spec);
                ShaderFX effect = null;
                try
                {
                    if (op == "set" || op == "copy")
                    {
                        Require(spec["code"] == null && spec["presetId"] == null && spec["includeBasePath"] == null,
                            "set/copy edits values without replacing code.");
                        var sourceLayer = op == "copy" ? Resolve(document, Text(spec, "sourceLayer"), aliases) : layer;
                        Require(sourceLayer != null, "Copy source layer not found.", "layer_not_found");
                        Require(op != "copy" || spec["sourceIndex"] != null && sourceLayer.modifiers.Count > 0, "copy requires an existing sourceIndex.");
                        int sourceIndex = op == "copy" ? Int(spec, "sourceIndex", -1, 0, sourceLayer.modifiers.Count - 1) : at;
                        Require(sourceLayer.modifiers[sourceIndex] is ShaderFX, "This operation requires a Shader FX, not a Material.");
                        // Copy-on-write never changes a shared external ShaderFX asset or another layer's values.
                        effect = ((ShaderFX)sourceLayer.modifiers[sourceIndex]).CloneForDocument(document);
                    }
                    else
                    {
                        Require((spec["code"] != null) != (spec["presetId"] != null), "Supply exactly one of code or presetId.");
                        if (spec["presetId"] != null)
                        {
                            Require(spec["includeBasePath"] == null, "A preset supplies its own include base.");
                            string id = Text(spec, "presetId");
                            var entry = ShaderFXCatalog.GetEntries().FirstOrDefault(e => PresetId(e) == id);
                            Require(entry != null, "Preset ID not found.", "preset_not_found");
                            effect = ReadPreset(document, entry, execute);
                        }
                        else
                        {
                            Require(spec["code"].Type == JTokenType.String, "code must be a string.");
                            string code = (string)spec["code"];
                            Require(!string.IsNullOrWhiteSpace(code) && code.Length <= 65536, "code must contain 1..65536 characters.");
                            string sourcePath = spec["includeBasePath"] == null ? null : ResolveShaderFXIncludeBasePath(Text(spec, "includeBasePath"));
                            Require(sourcePath != null || !ShaderFXSourceBuilder.HasRelativeIncludes(code), "Relative includes require includeBasePath.");
                            effect = ShaderFX.CreateAgentDraft(document, code, ShaderFXMetadata.Parse(code, false, out _), sourcePath);
                            if (execute)
                            {
                                try { effect.ApplyAgentDraft(); }
                                catch (Exception error) { throw new WhimTexApiException("shader_compile_failed", error.Message); }
                            }
                        }
                    }
                    SetFxValues(effect, spec["parameters"], document);
                    if (spec["enabled"] != null) effect.Active = Bool(spec, "enabled");
                    Require(effect.Parameters.Count <= MaxFxParameters, "At most 128 FX parameters are supported.", "resource_limit");
                    foreach (var parameter in effect.TextureLayerParameters())
                        Require(document.IsUsableShaderTexture(layer, parameter.textureLayerId), "Texture input creates a cycle.", "invalid_target");
                    Require(!insert || layer.modifiers.Count < 32, "At most 32 FX entries per layer.", "resource_limit");
                    document.AdoptAgentShaderFX(effect, execute ? UndoName : null);
                    if (insert) layer.modifiers.Insert(at, effect); else layer.modifiers[at] = effect;
                    effect.NotifyValuesChanged();
                    effect = null;
                }
                finally { if (effect != null) Object.DestroyImmediate(effect); }
            }
        }

        private static int RequiredFxIndex(Layer layer, JObject spec)
        {
            Require(spec["index"] != null && layer.modifiers.Count > 0, "An explicit existing FX index is required.");
            return Int(spec, "index", -1, 0, layer.modifiers.Count - 1);
        }

        private static void SetFxValues(ShaderFX effect, JToken token, TextureCompositor document)
        {
            if (token == null) return;
            var values = Obj(token, "parameters (name/value object)");
            foreach (var property in values.Properties())
            {
                var target = effect.Parameters.FirstOrDefault(p => p.name == property.Name);
                Require(target != null, "Unknown FX parameter: " + property.Name);
                var parsed = ReadLiveFxParameters(new JArray(new JObject { ["name"] = target.name,
                    ["type"] = target.type.ToString(), ["value"] = property.Value.DeepClone() }), document)[0];
                if (target.type == ShaderFXParameterType.Float || target.type == ShaderFXParameterType.Enum)
                {
                    Require(target.Clamp(parsed.floatValue) == parsed.floatValue, "Parameter outside its hard range: " + target.name);
                    if (target.type == ShaderFXParameterType.Enum)
                        Require(target.controls.Any(c => Array.IndexOf(c.optionValues, parsed.floatValue) >= 0), "Invalid enum value: " + target.name);
                }
                target.floatValue = parsed.floatValue; target.colorValue = parsed.colorValue; target.vectorValue = parsed.vectorValue;
                target.textureValue = parsed.textureValue; target.textureSource = parsed.textureSource; target.textureLayerId = parsed.textureLayerId;
                target.gradientValue = parsed.gradientValue; target.curveValue = parsed.curveValue; target.transformValue = parsed.transformValue;
            }
        }
    }
}
