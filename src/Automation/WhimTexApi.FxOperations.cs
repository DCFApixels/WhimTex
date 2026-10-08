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
                        layer.fx.Add(effect);
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

        private static ShaderFX ReadPreset(WhimTexDocument owner, ShaderFXCatalog.Entry entry, bool execute)
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

        private static void ApplyFxOperations(WhimTexDocument document, Layer layer, JToken token, bool execute, Dictionary<string, Layer> aliases)
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
                    int last = op == "applyAll" ? layer.fx.Count - 1 : RequiredFxIndex(layer, spec);
                    Require(last >= 0, "No FX to apply.");
                    Require(layer.Behaviour is DrawingLayerBehaviour || Bool(spec, "allowRasterize"),
                        "Applying FX rasterizes this layer/group. Supply allowRasterize:true to consent.", "rasterize_required");
                    if (execute) document.ApplyLayerFX(layer, last);
                    else
                    {
                        var remaining = layer.fx.GetRange(last + 1, layer.fx.Count - last - 1);
                        var placeholder = new DrawingLayerBehaviour();
                        placeholder.CopyRasterizedIdentityFrom(layer);
                        placeholder.transform = layer.transform;
                        layer.AdoptContent(placeholder);
                        layer.fx = remaining;
                    }
                    continue;
                }
                if (op == "remove" || op == "move")
                {
                    if (op == "remove") Keys(spec, "op", "index");
                    else Keys(spec, "op", "index", "toIndex");
                    int index = RequiredFxIndex(layer, spec);
                    Require(op != "move" || spec["toIndex"] != null, "move requires toIndex.");
                    int destination = op == "move" ? Int(spec, "toIndex", -1, 0, layer.fx.Count - 1) : 0;
                    var fxEntry = layer.fx[index];
                    layer.fx.RemoveAt(index);
                    if (op == "move") layer.fx.Insert(destination, fxEntry);
                    continue;
                }
                Require(op == "add" || op == "replace" || op == "set" || op == "copy", "Unknown FX edit: " + op);
                Keys(spec, "op", "index", "code", "includeBasePath", "presetId", "parameters", "enabled", "sourceLayer", "sourceIndex");
                bool insert = op == "add" || op == "copy";
                Require(op == "copy" || spec["sourceLayer"] == null && spec["sourceIndex"] == null, "Only copy takes sourceLayer/sourceIndex.");
                int at = insert ? Int(spec, "index", layer.fx.Count, 0, layer.fx.Count) : RequiredFxIndex(layer, spec);
                ShaderFX effect = null;
                try
                {
                    if (op == "set" || op == "copy")
                    {
                        Require(spec["code"] == null && spec["presetId"] == null && spec["includeBasePath"] == null,
                            "set/copy edits values without replacing code.");
                        var sourceLayer = op == "copy" ? Resolve(document, Text(spec, "sourceLayer"), aliases) : layer;
                        Require(sourceLayer != null, "Copy source layer not found.", "layer_not_found");
                        Require(op != "copy" || spec["sourceIndex"] != null && sourceLayer.fx.Count > 0, "copy requires an existing sourceIndex.");
                        int sourceIndex = op == "copy" ? Int(spec, "sourceIndex", -1, 0, sourceLayer.fx.Count - 1) : at;
                        Require(sourceLayer.fx[sourceIndex] is ShaderFX, "This operation requires a Shader FX, not a Material.");
                        // Copy-on-write never changes a shared external ShaderFX asset or another layer's values.
                        effect = ((ShaderFX)sourceLayer.fx[sourceIndex]).CloneForDocument(document);
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
                    Require(!insert || layer.fx.Count < 32, "At most 32 FX entries per layer.", "resource_limit");
                    document.AdoptAgentShaderFX(effect, execute ? UndoName : null);
                    if (insert) layer.fx.Insert(at, effect); else layer.fx[at] = effect;
                    effect.NotifyValuesChanged();
                    effect = null;
                }
                finally { if (effect != null) Object.DestroyImmediate(effect); }
            }
        }

        private static int RequiredFxIndex(Layer layer, JObject spec)
        {
            Require(spec["index"] != null && layer.fx.Count > 0, "An explicit existing FX index is required.");
            return Int(spec, "index", -1, 0, layer.fx.Count - 1);
        }

        private static ShaderFXParameter ReadFxParameterValue(ShaderFXParameter target, JToken token, WhimTexDocument owner)
        {
            var value = target.Copy();
            var spec = new JObject { ["value"] = token };
                switch (value.type)
                {
                    case ShaderFXParameterType.Curve:
                        Require(spec["value"].Type == JTokenType.String, "Curve value must be linear, easeIn, easeOut, easeInOut, one or a keys(...) string.");
                        value.curveValue = WhimTexCurveTexture.Parse((string)spec["value"]);
                        break;
                    case ShaderFXParameterType.Gradient: value.gradientValue = ReadGradient(spec["value"]); break;
                    case ShaderFXParameterType.Bool:
                        Require(spec["value"].Type == JTokenType.Boolean, "Bool value must be true or false.");
                        value.floatValue = (bool)spec["value"] ? 1f : 0f;
                        break;
                    case ShaderFXParameterType.Enum:
                    case ShaderFXParameterType.Float: value.floatValue = Number(spec["value"], "value", -1000000, 1000000); break;
                    case ShaderFXParameterType.Color: value.colorValue = AgentJson.Color(spec["value"]); break;
                    case ShaderFXParameterType.Vector2:
                    case ShaderFXParameterType.Point:
                    case ShaderFXParameterType.Vector3:
                    case ShaderFXParameterType.Normal:
                        int components = value.type == ShaderFXParameterType.Vector2 || value.type == ShaderFXParameterType.Point ? 2 : 3;
                        Require(spec["value"] is JArray values && values.Count == components, "Wrong vector component count.");
                        value.vectorValue = Vector4.zero;
                        for (int i=0;i<components;i++) value.vectorValue[i] = Number(spec["value"][i], "component", -1000000, 1000000);
                        if (value.type == ShaderFXParameterType.Normal) value.vectorValue = ShaderFXParameter.NormalizeNormal(value.vectorValue);
                        break;
                    case ShaderFXParameterType.Vector:
                        Require(spec["value"] is JArray vector && vector.Count == 4, "Vector value must have four components.");
                        value.vectorValue = new Vector4(Number(spec["value"][0], "x", -1000000, 1000000), Number(spec["value"][1], "y", -1000000, 1000000),
                            Number(spec["value"][2], "z", -1000000, 1000000), Number(spec["value"][3], "w", -1000000, 1000000));
                        break;
                    case ShaderFXParameterType.Texture2D:
                        if (spec["value"] is JObject layerSource)
                        {
                            Keys(layerSource, "layer");
                            string layerId = Text(layerSource, "layer");
                            Require(owner != null && owner.FindLayer(layerId)?.Behaviour != null, "Texture source layer not found.", "invalid_target");
                            value.textureSource = ShaderFXTextureSource.Layer;
                            value.textureLayerId = layerId;
                            break;
                        }
                        string path = Text(spec, "value");
                        if (path == "self" || path == "none")
                        {
                            value.textureSource = path == "self" ? ShaderFXTextureSource.Self : ShaderFXTextureSource.None;
                            break;
                        }
                        value.textureSource = ShaderFXTextureSource.Texture;
                        Require(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal), "Texture value must be a project asset path.");
                        ValidateSegments(path);
                        value.textureValue = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        Require(value.textureValue != null, "Texture parameter asset not found.");
                        Require(!string.Equals(path, DocumentAssetPath(owner), StringComparison.OrdinalIgnoreCase), "An FX cannot sample its own document output.", "invalid_target");
                        break;
                    case ShaderFXParameterType.Transform2D:
                        JObject area = Obj(spec["value"], "Transform2D value");
                        Keys(area, "position", "size", "rotation", "matrix");
                        var dimensions = owner != null ? new Vector2(owner.width, owner.height) : Vector2.one;
                        if (area["matrix"] != null)
                        {
                            Require(area["position"] == null && area["size"] == null && area["rotation"] == null,
                                "matrix cannot be combined with position, size or rotation.");
                            Require(area["matrix"] is JArray && ((JArray)area["matrix"]).Count == 9, "matrix must contain nine row-major numbers.");
                            var a = (JArray)area["matrix"];
                            var m = new ProjectiveMatrix {
                                m00=TransformNumber(a[0]),m01=TransformNumber(a[1]),m02=TransformNumber(a[2]),
                                m10=TransformNumber(a[3]),m11=TransformNumber(a[4]),m12=TransformNumber(a[5]),
                                m20=TransformNumber(a[6]),m21=TransformNumber(a[7]),m22=TransformNumber(a[8]) };
                            Require(value.transformValue.TrySetMatrix(m), "Transform2D matrix must be invertible with no horizon crossing its rectangle.");
                            break;
                        }
                        foreach (string field in new[] { "position", "size" })
                        {
                            if (area[field] == null) continue;
                            Require(area[field] is JArray pair && pair.Count == 2, field + " must have two components.");
                            var v = TransformVector(area[field], field);
                            if (field == "position") value.transformValue.EditPosition(v, dimensions);
                            else
                            {
                                Require(Math.Abs(v.x) >= 0.00001 && Math.Abs(v.y) >= 0.00001, "Transform2D size cannot be zero.");
                                value.transformValue.EditSize(v, dimensions);
                            }
                        }
                        if (area["rotation"] != null) value.transformValue.EditRotation(TransformNumber(area["rotation"]), dimensions);
                        break;
                }

            return value;
        }

        private static void SetFxValues(ShaderFX effect, JToken token, WhimTexDocument document)
        {
            if (token == null) return;
            var values = Obj(token, "parameters (name/value object)");
            foreach (var property in values.Properties())
            {
                var target = effect.Parameters.FirstOrDefault(p => p.name == property.Name);
                Require(target != null, "Unknown FX parameter: " + property.Name);
                var parsed = ReadFxParameterValue(target, property.Value.DeepClone(), document);
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
