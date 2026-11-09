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
        // Declared preset IDs work in both libraries; anonymous entries use paths for selection only.
        public static string FxCatalog(string query = null, string presetId = null) => Respond(() =>
        {
            var result = Success();
            var entries = new JArray();
            foreach (var entry in ShaderPresetCatalog.GetEntries())
            {
                string id = PresetId(entry);
                if (presetId != null && id != presetId) continue;
                if (!string.IsNullOrEmpty(query) && entry.menuPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (entry.group == null || entry.group.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                var item = new JObject { ["id"] = id, ["path"] = entry.path, ["name"] = entry.menuPath,
                    ["kind"] = ShaderPresetIO.IsProgramPath(entry.path) ? "program" : "hlsl", ["mode"] = entry.mode.ToString(), ["group"] = entry.group, ["error"] = entry.error,
                    ["presetId"] = entry.id, ["presetVersion"] = entry.version, ["linkable"] = !entry.HasError && !string.IsNullOrEmpty(entry.id) };
                item["icon"] = FxIconSnapshot(entry.icon, entry.path);
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

        private static string PresetId(ShaderPresetCatalog.Entry entry) => entry.HasError || string.IsNullOrEmpty(entry.id) ? entry.path : entry.id;

        private static ShaderPresetCatalog.Entry FindShaderPreset(string id)
        {
            var entry = ShaderPresetCatalog.GetEntries().FirstOrDefault(e => PresetId(e) == id);
            Require(entry != null, "Preset ID not found.", "preset_not_found");
            return entry;
        }

        private static JObject FxIconSnapshot(ShaderIconMetadata.Descriptor icon, string source)
        {
            if (icon == null) return new JObject { ["kind"] = "default" };
            bool png = icon.reference?.StartsWith("data:", StringComparison.Ordinal) == true;
            ShaderIconCache.Get(icon, source, out string warning);
            return new JObject {
                ["kind"] = icon.svg != null ? "inlineSvg" : png ? "embeddedPng" :
                    icon.reference?.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) == true ? "guid" : "file",
                ["reference"] = png ? null : icon.reference,
                ["warning"] = icon.error ?? warning
            };
        }

        private static ShaderFX ReadPreset(WhimTexDocument owner, ShaderPresetCatalog.Entry entry, bool execute)
        {
            Require(!entry.HasError, entry.error ?? "Invalid preset.", "invalid_preset");
            if (execute) return ShaderFX.FromCatalog(owner, entry);
            var loaded = ShaderPresetIO.Load(entry);
            var effect = ShaderFX.CreateAgentDraft(owner, loaded.program.draftCode, loaded.program.draftCodeParameters, loaded.path);
            effect.ReplaceProgram(loaded.program);
            return effect;
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
                Keys(spec, "op", "index", "program", "mode", "draftGraph", "draftCode", "workingCode", "draftCodeParameters", "compile", "includeBasePath", "presetId", "parameters", "randomizeSeeds", "enabled", "sourceLayer", "sourceIndex");
                bool insert = op == "add" || op == "copy";
                Require(op == "copy" || spec["sourceLayer"] == null && spec["sourceIndex"] == null, "Only copy takes sourceLayer/sourceIndex.");
                int at = insert ? Int(spec, "index", layer.fx.Count, 0, layer.fx.Count) : RequiredFxIndex(layer, spec);
                ShaderFX effect = null;
                try
                {
                    if (op == "set" || op == "copy")
                    {
                        Require(spec["presetId"] == null && spec["includeBasePath"] == null,
                            "set/copy retains its include base; use replace to load a preset or change includeBasePath.");
                        var sourceLayer = op == "copy" ? Resolve(document, Text(spec, "sourceLayer"), aliases) : layer;
                        Require(sourceLayer != null, "Copy source layer not found.", "layer_not_found");
                        Require(op != "copy" || spec["sourceIndex"] != null && sourceLayer.fx.Count > 0, "copy requires an existing sourceIndex.");
                        int sourceIndex = op == "copy" ? Int(spec, "sourceIndex", -1, 0, sourceLayer.fx.Count - 1) : at;
                        Require(sourceLayer.fx[sourceIndex] != null, "The Shader effect is missing.");
                        // Copy-on-write never changes another layer's parameter values.
                        effect = ((ShaderFX)sourceLayer.fx[sourceIndex]).CloneForDocument(document);
                        if (HasFxProgramEdit(spec))
                        {
                            effect.ReplaceProgram(ReadFxProgramEdit(effect.Program, spec, document, aliases));
                            CompileFxDraft(effect, execute && Bool(spec, "compile", true));
                        }
                        else if (Bool(spec, "compile")) CompileFxDraft(effect, execute);
                    }
                    else
                    {
                        effect = CreateFxProgramDraft(document, spec, execute, aliases);
                    }
                    SetFxValues(effect, spec["parameters"], document);
                    RandomizeFxSeeds(effect, spec["randomizeSeeds"], spec["parameters"], execute);
                    if (spec["enabled"] != null) effect.Active = Bool(spec, "enabled");
                    Require(effect.Parameters.Count <= MaxFxParameters, "At most 128 FX parameters are supported.", "resource_limit");
                    WhimTexDocumentJson.ValidateShaderProgramInputs(document, layer, effect.Program, effectContext: true);
                    Require(!insert || layer.fx.Count < 32, "At most 32 FX entries per layer.", "resource_limit");
                    document.AdoptAgentShaderFX(effect, execute ? UndoName : null);
                    if (insert) layer.fx.Insert(at, effect); else layer.fx[at] = effect;
                    effect.NotifyValuesChanged();
                    effect = null;
                }
                finally { if (effect != null) Object.DestroyImmediate(effect); }
            }
        }

        private static bool HasFxProgramEdit(JObject spec) => spec["program"] != null || spec["mode"] != null ||
            spec["draftGraph"] != null || spec["draftCode"] != null || spec["workingCode"] != null || spec["draftCodeParameters"] != null;

        private static ShaderProgram ReadFxProgramEdit(ShaderProgram current, JObject spec, WhimTexDocument document, Dictionary<string, Layer> aliases)
        {
            var program = ReadShaderProgramEdit(current, spec, document, aliases);
            if (program.mode == ShaderProgramMode.Code && Bool(spec, "compile", true))
            {
                var declared = ShaderMetadata.Parse(program.draftCode, false, out _);
                ShaderMetadata.PreserveValues(declared, program.draftCodeParameters);
                WhimTexDocumentJson.PreserveShaderCodeParameterAssets(program.draftCodeParameters, declared);
                program.draftCodeParameters = declared;
            }
            return program;
        }

        private static void CompileFxDraft(ShaderFX effect, bool execute)
        {
            if (!execute) return;
            try { effect.CompileAgentDraft(); }
            catch (Exception error) { throw new WhimTexApiException("shader_compile_failed", error.Message); }
        }

        private static ShaderFX CreateFxProgramDraft(WhimTexDocument document, JObject spec, bool execute, Dictionary<string, Layer> aliases)
        {
            if (spec["presetId"] != null)
            {
                Require(!HasFxProgramEdit(spec) && spec["includeBasePath"] == null,
                    "presetId supplies the complete program and its include base.");
                var entry = FindShaderPreset(Text(spec, "presetId"));
                return ReadPreset(document, entry, execute && Bool(spec, "compile", true));
            }
            Require(spec["program"] != null || spec["draftGraph"] != null || spec["draftCode"] != null ||
                Enum(spec, "mode", ShaderProgramMode.Code) == ShaderProgramMode.Graph,
                "Supply program, draftGraph, draftCode or presetId (or mode:Graph for an empty effect graph).");
            var initial = ShaderProgram.CreateEffect();
            if (spec["draftGraph"] != null && spec["mode"] == null) initial.mode = ShaderProgramMode.Graph;
            var program = ReadFxProgramEdit(initial, spec, document, aliases);
            string sourcePath = spec["includeBasePath"] == null ? null : ResolveShaderFXIncludeBasePath(Text(spec, "includeBasePath"));
            if (spec["draftCode"] != null)
            {
                Require(!string.IsNullOrWhiteSpace(program.draftCode) && program.draftCode.Length <= 65536, "draftCode must contain 1..65536 characters.");
                Require(sourcePath != null || !ShaderSourceBuilder.HasRelativeIncludes(program.draftCode), "Relative includes require includeBasePath.");
            }
            var effect = ShaderFX.CreateAgentDraft(document, program.draftCode, program.draftCodeParameters, sourcePath);
            try
            {
                effect.ReplaceProgram(program);
                CompileFxDraft(effect, execute && Bool(spec, "compile", true));
                return effect;
            }
            catch { Object.DestroyImmediate(effect); throw; }
        }

        private static int RequiredFxIndex(Layer layer, JObject spec)
        {
            Require(spec["index"] != null && layer.fx.Count > 0, "An explicit existing FX index is required.");
            return Int(spec, "index", -1, 0, layer.fx.Count - 1);
        }

        private static ShaderParameter ReadFxParameterValue(ShaderParameter target, JToken token, WhimTexDocument owner)
        {
            var value = target.Copy();
            if (target.controls.Any(c => c.type == ShaderParameterType.Toggle || c.type == ShaderParameterType.Color ||
                c.type == ShaderParameterType.Enum || c.type == ShaderParameterType.Seed))
            {
                ReadTypedFxValue(value, token);
                return value;
            }
            var spec = new JObject { ["value"] = token };
                switch (value.type)
                {
                    case ShaderParameterType.Curve:
                        Require(spec["value"].Type == JTokenType.String, "Curve value must be linear, easeIn, easeOut, easeInOut, one or a keys(...) string.");
                        value.curveValue = WhimTexCurveTexture.Parse((string)spec["value"]);
                        break;
                    case ShaderParameterType.Gradient: value.gradientValue = ReadGradient(spec["value"]); break;
                    case ShaderParameterType.Toggle:
                        Require(spec["value"].Type == JTokenType.Boolean, "Bool value must be true or false.");
                        value.floatValue = (bool)spec["value"] ? 1f : 0f;
                        break;
                    case ShaderParameterType.Enum:
                    case ShaderParameterType.Float:
                        int limit = ShaderSeed.Control(target) != null ? ShaderSeed.Limit : 1000000;
                        value.floatValue = Number(spec["value"], "value", -limit, limit); break;
                    case ShaderParameterType.Color: value.colorValue = AgentJson.Color(spec["value"]); break;
                    case ShaderParameterType.Vector2:
                    case ShaderParameterType.Point:
                    case ShaderParameterType.Vector3:
                    case ShaderParameterType.Normal:
                        int components = value.type == ShaderParameterType.Vector2 || value.type == ShaderParameterType.Point ? 2 : 3;
                        Require(spec["value"] is JArray values && values.Count == components, "Wrong vector component count.");
                        value.vectorValue = Vector4.zero;
                        for (int i=0;i<components;i++) value.vectorValue[i] = Number(spec["value"][i], "component", -1000000, 1000000);
                        if (value.type == ShaderParameterType.Normal) value.vectorValue = ShaderParameter.NormalizeNormal(value.vectorValue);
                        if (target.IsInterval)
                            Require(ShaderParameter.IsValidInterval(value.vectorValue, target.minimum, target.maximum, target.cyclicRange),
                                target.cyclicRange ? "Cyclic endpoints must be within the limits, or one period apart with at least one endpoint within the limits: " + target.name : "Interval must satisfy min <= x <= y <= max: " + target.name);
                        break;
                    case ShaderParameterType.Vector:
                        Require(spec["value"] is JArray vector && vector.Count == 4, "Vector value must have four components.");
                        value.vectorValue = new Vector4(Number(spec["value"][0], "x", -1000000, 1000000), Number(spec["value"][1], "y", -1000000, 1000000),
                            Number(spec["value"][2], "z", -1000000, 1000000), Number(spec["value"][3], "w", -1000000, 1000000));
                        break;
                    case ShaderParameterType.Texture2D:
                        if (spec["value"] is JObject layerSource)
                        {
                            Keys(layerSource, "layer");
                            string layerId = Text(layerSource, "layer");
                            Require(owner != null && owner.FindLayer(layerId)?.Behaviour != null, "Texture source layer not found.", "invalid_target");
                            value.textureSource = ShaderTextureSource.Layer;
                            value.textureLayerId = layerId;
                            break;
                        }
                        string path = Text(spec, "value");
                        if (path == "self" || path == "none")
                        {
                            value.textureSource = path == "self" ? ShaderTextureSource.Self : ShaderTextureSource.None;
                            break;
                        }
                        value.textureSource = ShaderTextureSource.Texture;
                        Require(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal), "Texture value must be a project asset path.");
                        ValidateSegments(path);
                        value.SetTextureValue(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                        Require(value.textureValue != null, "Texture parameter asset not found.");
                        Require(!string.Equals(path, DocumentAssetPath(owner), StringComparison.OrdinalIgnoreCase), "An FX cannot sample its own document output.", "invalid_target");
                        break;
                    case ShaderParameterType.Transform2D:
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

        private static void RandomizeFxSeeds(ShaderFX effect, JToken token, JToken values, bool execute)
        {
            if (token == null) return;
            Require(token is JArray seeds && seeds.Count > 0 && seeds.Count <= 128, "randomizeSeeds must contain 1..128 parameter names.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in (JArray)token)
            {
                Require(item.Type == JTokenType.String, "randomizeSeeds entries must be names.");
                string name = (string)item;
                Require(names.Add(name), "Duplicate randomized Seed: " + name);
                Require(values?[name] == null, "Cannot set and randomize the same Seed: " + name);
                var parameter = effect.Parameters.FirstOrDefault(p => p.name == name);
                Require(parameter != null, "Unknown FX parameter: " + name);
                var control = ShaderSeed.Control(parameter);
                Require(control != null, "Parameter is not declared seed: " + name);
                ShaderSeed.Validate(control);
                // Validation/dry runs must not consume randomness or change the stored value.
                if (execute) parameter.SetNumber(0, ShaderSeed.NextValue(control));
            }
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
                if (target.type == ShaderParameterType.Float || target.type == ShaderParameterType.Enum)
                {
                    Require(target.IsInteger || target.Clamp(parsed.floatValue) == parsed.floatValue, "Parameter outside its hard range: " + target.name);
                    if (target.type == ShaderParameterType.Enum)
                        Require(target.controls.Any(c => Array.IndexOf(c.optionValues, parsed.GetNumber()) >= 0), "Invalid enum value: " + target.name);
                }
                target.integerValues = parsed.integerValues;
                target.floatValue = parsed.floatValue; target.colorValue = parsed.colorValue; target.vectorValue = parsed.vectorValue;
                target.CopyTextureValue(parsed); target.textureSource = parsed.textureSource; target.textureLayerId = parsed.textureLayerId;
                target.gradientValue = parsed.gradientValue; target.curveValue = parsed.curveValue; target.transformValue = parsed.transformValue;
            }
        }
    }
}
