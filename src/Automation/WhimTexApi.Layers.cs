using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static Layer ApplyOperation(TextureCompositor document, JObject operation, Dictionary<string, Layer> aliases, bool execute)
        {
            string op = Text(operation, "op");
            if (op == "add")
            {
                Keys(operation, "op", "type", "as", "parent", "index", "settings", "transform");
                string type = Text(operation, "type");
                var descriptor = LayerTypeRegistry.Find(type);
                Require(descriptor != null, "Unknown layer type: " + type);
                Layer added = descriptor.CreateLayer();
                string alias = Text(operation, "as");
                if (alias != null)
                {
                    Require(alias.Length > 0 && alias.Length <= 64 && !alias.StartsWith("@"), "Alias must be 1..64 characters without a leading @.");
                    Require(!aliases.ContainsKey(alias), "Duplicate alias: " + alias);
                }
                List<Layer> container = Container(document, Text(operation, "parent"), aliases);
                int index = Int(operation, "index", 0, 0, container.Count);
                added.AssignNewId();
                added.layerName = document.AllocateLayerName(added);
                container.Insert(index, added);
                if (alias != null) aliases.Add(alias, added);
                if (operation["settings"] != null) SetLayer(document, added, Obj(operation["settings"], "settings"));
                if (operation["transform"] != null) SetTransform(document, added, Obj(operation["transform"], "transform"));
                if (execute && added?.Behaviour is DrawingLayerBehaviour drawing)
                {
                    drawing.InitializeCanvas(document.width, document.height);
                    drawing.MakeTexturePersistent(document);
                    Undo.RegisterCreatedObjectUndo(drawing.StoredTexture, UndoName);
                }
                return added;
            }

            Layer layer = Resolve(document, Text(operation, "layer"), aliases);
            if (layer?.Behaviour is PendingLayerBehaviour)
            {
                Require(op == "move" || op == "set", "Reserved layers only support moving, renaming and visibility changes.", "layer_locked");
                if (op == "set") Keys(Obj(operation["settings"], "settings"), "name", "enabled");
            }
            switch (op)
            {
                case "compact":
                    Keys(operation, "op", "layer");
                    Require(layer?.Behaviour is DrawingLayerBehaviour, "compact requires a Drawing layer.");
                    if (execute) ((DrawingLayerBehaviour)layer).ConvertTo8Bit();
                    else layer.colorRange = LayerColorRange.Standard;
                    break;
                case "set":
                    Keys(operation, "op", "layer", "settings");
                    SetLayer(document, layer, Obj(operation["settings"], "settings"));
                    break;
                case "transform":
                    Keys(operation, "op", "layer", "transform");
                    SetTransform(document, layer, Obj(operation["transform"], "transform"));
                    break;
                case "target":
                    Keys(operation, "op", "layer", "input", "target");
                    Require(layer?.Behaviour is TargetedLayerBehaviour, "target requires an effect layer.");
                    var effect = (TargetedLayerBehaviour)layer;
                    effect.inputMode = Enum(operation, "input", EffectInputMode.Specific);
                    Require(effect.inputMode != EffectInputMode.Previous || operation["target"] == null, "Previous input does not take a target.");
                    effect.TargetLayerId = effect.inputMode == EffectInputMode.Specific
                        ? Resolve(document, Text(operation, "target"), aliases).Id : null;
                    break;
                case "stroke":
                    Keys(operation, "op", "layer", "points", "space", "erase", "brush", "pencil");
                    Require(layer?.Behaviour is DrawingLayerBehaviour, "stroke requires a Drawing layer.");
                    Paint(document, (DrawingLayerBehaviour)layer, operation, execute);
                    break;
                case "move":
                    Keys(operation, "op", "layer", "parent", "index");
                    List<Layer> destination = Container(document, Text(operation, "parent"), aliases);
                    if (layer?.AsGroup() is Layer movingGroup)
                        Require(!ContainsContainer(movingGroup, destination), "A group cannot be moved into itself or its descendants.");
                    Require(document.TryFindLayer(layer, out List<Layer> source, out int sourceIndex), "Layer not found.");
                    source.RemoveAt(sourceIndex);
                    destination.Insert(Int(operation, "index", 0, 0, destination.Count), layer);
                    break;
                default:
                    throw new WhimTexApiException("invalid_request", "Unknown operation: " + op);
            }
            if (execute && layer?.Behaviour is DrawingLayerBehaviour changedDrawing) changedDrawing.SetColorRange(layer.colorRange);
            return layer;
        }

        private static bool ContainsContainer(Layer group, List<Layer> candidate)
        {
            if (group.layers == candidate) return true;
            foreach (Layer child in group.layers)
                if (child?.AsGroup() is Layer nested && ContainsContainer(nested, candidate)) return true;
            return false;
        }

        private static Layer Resolve(TextureCompositor document, string reference, Dictionary<string, Layer> aliases)
        {
            Require(!string.IsNullOrWhiteSpace(reference), "A layer ID or @alias is required.");
            Layer layer;
            if (reference.StartsWith("@")) aliases.TryGetValue(reference.Substring(1), out layer);
            else layer = document.FindLayer(reference);
            Require(layer != null, "Layer not found: " + reference, "layer_not_found");
            return layer;
        }

        private static List<Layer> Container(TextureCompositor document, string parent, Dictionary<string, Layer> aliases)
        {
            if (string.IsNullOrEmpty(parent)) return document.layers;
            Layer layer = Resolve(document, parent, aliases);
            Require(layer?.IsGroup == true, "parent must reference a group.");
            return ((Layer)layer).layers;
        }

        private static void SetLayer(TextureCompositor document, Layer layer, JObject settings)
        {
            Keys(settings, "name", "enabled", "clippingMask", "opacity", "blend", "filter", "source", "colorRange", "blendRange", "swizzle", "compositing", "color", "brush",
                "metric", "outlineWidth", "outlineSoftness", "outlinePosition", "outlineOffset", "fillCenter", "fillColor", "sourceChannel", "threshold",
                "distancePosition", "inverted", "maxDistance", "gradient", "normalMap", "blur", "makeSeamless", "noise", "shape");
            foreach (var property in settings.Properties())
            {
                string key = property.Name;
                bool valid = key == "name" || key == "enabled" || key == "clippingMask" || key == "opacity" || key == "blend" ||
                    key == "colorRange" || key == "blendRange" || key == "swizzle" || key == "compositing" && layer?.IsGroup == true || !layer.IsGroup &&
                    (key == "opacity" || key == "blend" || key == "filter" ||
                    key == "source" && layer?.Behaviour is FileLayerBehaviour || key == "brush" && layer?.Behaviour is DrawingLayerBehaviour ||
                    key == "color" && (layer?.Behaviour is ColorFillLayerBehaviour || layer?.Behaviour is OutlineLayerBehaviour) ||
                    key == "metric" && (layer?.Behaviour is SDFLayerBehaviour || layer?.Behaviour is OutlineLayerBehaviour) ||
                    key == "normalMap" && layer?.Behaviour is NormalMapLayerBehaviour ||
                    key == "blur" && layer?.Behaviour is BlurLayerBehaviour ||
                    key == "makeSeamless" && layer?.Behaviour is MakeSeamlessLayerBehaviour ||
                    key == "noise" && layer?.Behaviour is NoiseLayerBehaviour ||
                    key == "shape" && layer?.Behaviour is ShapeLayerBehaviour ||
                    (key == "outlineWidth" || key == "outlineSoftness" || key == "outlinePosition" ||
                     key == "outlineOffset" || key == "fillCenter" || key == "fillColor") && layer?.Behaviour is OutlineLayerBehaviour ||
                    (key == "sourceChannel" || key == "threshold" || key == "distancePosition" || key == "inverted" || key == "maxDistance") && layer?.Behaviour is SDFLayerBehaviour ||
                    key == "gradient" && (layer?.Behaviour is GradientLayerBehaviour || layer?.Behaviour is SDFLayerBehaviour));
                Require(valid, key + " is not supported by " + TypeName(layer) + " layers.");
            }
            layer.layerName = Text(settings, "name", layer.layerName);
            layer.enabled = Bool(settings, "enabled", layer.enabled);
            layer.clippingMask = Bool(settings, "clippingMask", layer.clippingMask);
            Require(!(layer?.Behaviour is ShaderProcessorLayerBehaviour) || !layer.clippingMask, "Shader Processor is a stack operation and cannot be a clipping layer.");
            layer.opacity = Number(settings, "opacity", layer.opacity, 0f, 1f);
            layer.blendMode = Enum(settings, "blend", layer.blendMode);
            if (settings["swizzle"] != null)
            {
                Require(settings["swizzle"] is JArray array && array.Count == 4,
                    "swizzle must contain four channel names in output RGBA order.");
                var values = (JArray)settings["swizzle"];
                var swizzle = new LayerSwizzle();
                for (int channel = 0; channel < 4; channel++)
                {
                    int source = values[channel].Type == JTokenType.String
                        ? System.Array.IndexOf(LayerSwizzle.Labels, (string)values[channel]) : -1;
                    Require(source >= 0, "Invalid swizzle channel. Use " + string.Join(", ", LayerSwizzle.Labels) + ".");
                    swizzle[channel] = (SwizzleChannel)source;
                }
                layer.swizzle = swizzle;
            }
            if (layer?.AsGroup() is Layer group)
                group.compositing = Enum(settings, "compositing", group.compositing);
            layer.filterMode = Enum(settings, "filter", layer.filterMode);
            if (layer?.Behaviour is FileLayerBehaviour file && settings["source"] != null)
            {
                string path = ReadAssetPath(Text(settings, "source"));
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Require(texture != null, "No imported Texture2D at " + path + ". Import the image first.", "texture_not_found");
                Require(!ReferenceEquals(TextureCompositor.FindDocument(texture), document), "A document cannot sample its own output texture.");
                file.AssignSourceTexture(texture, document);
            }
            layer.colorRange = Enum(settings, "colorRange", layer.colorRange);
            layer.blendRange = Enum(settings, "blendRange", layer.blendRange);
            if (layer?.Behaviour is ColorFillLayerBehaviour fill && settings["color"] != null) fill.color = Color(settings["color"]);
            if (layer?.Behaviour is NormalMapLayerBehaviour normal && settings["normalMap"] != null)
                SetNormalMap(normal, Obj(settings["normalMap"], "normalMap"));
            if (layer?.Behaviour is BlurLayerBehaviour blur && settings["blur"] != null)
                SetBlur(blur, Obj(settings["blur"], "blur"));
            if (layer?.Behaviour is MakeSeamlessLayerBehaviour seamless && settings["makeSeamless"] != null)
                SetMakeSeamless(seamless, Obj(settings["makeSeamless"], "makeSeamless"));
            if (layer?.Behaviour is NoiseLayerBehaviour noise && settings["noise"] != null)
                SetNoise(noise, Obj(settings["noise"], "noise"));
            if (layer?.Behaviour is ShapeLayerBehaviour shape && settings["shape"] != null)
                SetShape(shape, Obj(settings["shape"], "shape"));
            if (layer?.Behaviour is DrawingLayerBehaviour drawing && settings["brush"] != null) SetBrush(document, drawing, Obj(settings["brush"], "brush"));
            if (layer?.Behaviour is OutlineLayerBehaviour outline)
            {
                outline.metric = Enum(settings, "metric", outline.metric);
                outline.outlineWidth = Number(settings, "outlineWidth", outline.outlineWidth, 0f, 16384f);
                outline.outlineSoftness = Number(settings, "outlineSoftness", outline.outlineSoftness, 0f, 16384f);
                outline.outlinePosition = Enum(settings, "outlinePosition", outline.outlinePosition);
                outline.outlineOffset = Number(settings, "outlineOffset", outline.outlineOffset, -16384f, 16384f);
                outline.fillCenter = Bool(settings, "fillCenter", outline.fillCenter);
                if (settings["fillColor"] != null) outline.fillColor = Color(settings["fillColor"]);
                if (settings["color"] != null) outline.outlineColor = Color(settings["color"]);
            }
            if (layer?.Behaviour is SDFLayerBehaviour sdf)
            {
                sdf.metric = Enum(settings, "metric", sdf.metric);
                sdf.sourceChannel = Enum(settings, "sourceChannel", sdf.sourceChannel);
                sdf.threshold = (byte)Int(settings, "threshold", sdf.threshold, 0, 255);
                sdf.distancePosition = Enum(settings, "distancePosition", sdf.distancePosition);
                sdf.inverted = Bool(settings, "inverted", sdf.inverted);
                sdf.maxDistanceNormalization = Number(settings, "maxDistance", sdf.maxDistanceNormalization, 0f, 16384f);
                if (settings["gradient"] != null) sdf.gradient = ReadGradient(settings["gradient"]);
            }
            if (layer?.Behaviour is GradientLayerBehaviour gradient && settings["gradient"] != null) gradient.gradient = ReadGradient(settings["gradient"]);
        }

        private static Gradient ReadGradient(JToken token)
        {
            Require(token is JArray keys && keys.Count >= 2 && keys.Count <= 8, "gradient must contain 2..8 {time, color} stops.");
            var values = (JArray)token;
            var colors = new GradientColorKey[values.Count];
            var alphas = new GradientAlphaKey[values.Count];
            float previous = -1f;
            for (int i = 0; i < values.Count; i++)
            {
                JObject stop = Obj(values[i], "gradient stop");
                Keys(stop, "time", "color");
                float time = Number(stop["time"], "time", 0f, 1f);
                Require(time > previous, "Gradient stop times must be strictly increasing.");
                var color = Color(stop["color"]);
                colors[i] = new GradientColorKey(color, time);
                alphas[i] = new GradientAlphaKey(color.a, time);
                previous = time;
            }
            return GradientUtility.Create(colors, alphas);
        }

        private static void SetTransform(TextureCompositor document, Layer layer, JObject settings)
        {
            Require(!layer.IsGroup, "Groups do not have a transform.");
            Keys(settings, "reset", "position", "scale", "pivot", "rotation", "tiling", "originalAspect");
            TextureTransform transform = Bool(settings, "reset") ? TextureTransform.Default : layer.transform;
            if (settings["position"] != null) transform.position = Vector(settings["position"], "position");
            if (settings["scale"] != null) transform.scale = Vector(settings["scale"], "scale");
            if (settings["pivot"] != null) transform.pivot = Vector(settings["pivot"], "pivot");
            Require(Mathf.Abs(transform.scale.x) >= 0.00001f && Mathf.Abs(transform.scale.y) >= 0.00001f, "Transform scale must be nonzero.");
            transform.rotation = Number(settings, "rotation", transform.rotation, -360000f, 360000f);
            transform.tiling = Enum(settings, "tiling", transform.tiling);
            layer.transform = transform;
            if (Bool(settings, "originalAspect"))
            {
                Require(layer.TryGetOriginalAspectTransform(document, out TextureTransform fitted), "Original Aspect requires a valid source and transform.");
                layer.transform = fitted;
            }
        }
    }
}
