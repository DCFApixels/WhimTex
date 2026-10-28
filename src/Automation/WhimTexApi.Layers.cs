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
        private static Layer ApplyOperation(WhimTexDocument document, JObject operation, Dictionary<string, Layer> aliases, bool execute, string workId = null, CanvasSelection attachmentSelection = null)
        {
            string op = Text(operation, "op");
            if (op == "comment" || op == "commentCreate")
            {
                ApplyCommentOperation(document, operation, aliases, workId, attachmentSelection);
                return null;
            }
            if (op == "resize")
            {
                ResizeCanvas(document, operation);
                return null; // Document-level operation, including an empty canvas.
            }
            if (op == "add")
            {
                Keys(operation, "op", "type", "as", "parent", "index", "settings", "transform");
                string type = Text(operation, "type");
                var descriptor = LayerTypeRegistry.Find(type);
                Require(descriptor != null, "Unknown layer type: " + type);
                Layer added = descriptor.CreateLayer();
                if (added.Behaviour is VectorLayerBehaviour addedVector)
                    addedVector.referenceSize = new Vector2(document.width, document.height);
                if (added.Behaviour is BooleanGroupLayerBehaviour addedBoolean)
                    addedBoolean.referenceSize = new Vector2(document.width, document.height);
                string alias = Text(operation, "as");
                if (alias != null)
                {
                    Require(alias.Length > 0 && alias.Length <= 64 && !alias.StartsWith("@"), "Alias must be 1..64 characters without a leading @.");
                    Require(!aliases.ContainsKey(alias), "Duplicate alias: " + alias);
                }
                List<Layer> container = Container(document, Text(operation, "parent"), aliases);
                int index = Int(operation, "index", 0, 0, container.Count);
                Require(document.CanInsertInto(added, container), "Boolean Group accepts only closed Shape/Vector geometry, editable Text and nested Boolean Groups.");
                if (operation["settings"] != null) SetLayer(document, added, Obj(operation["settings"], "settings"));
                Require(document.CanInsertInto(added, container), "Boolean Group operands require closed Shape/Vector geometry, editable Text or nested Boolean Groups; close open paths before insertion.");
                added.AssignNewId();
                if (operation["settings"]?["name"] == null) added.layerName = document.AllocateLayerName(added);
                container.Insert(index, added);
                if (alias != null) aliases.Add(alias, added);
                if (operation["transform"] != null) SetTransform(document, added, Obj(operation["transform"], "transform"));
                if (execute && added?.Behaviour is DrawingLayerBehaviour drawing)
                {
                    drawing.InitializeCanvas(document.width, document.height);
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
                case "mesh":
                    ApplyMeshOperation(layer, operation);
                    break;
                case "convertToVector":
                    Keys(operation, "op", "layer");
                    Require(layer.Behaviour is ShapeLayerBehaviour || layer.Behaviour is TextLayerBehaviour,
                        "convertToVector requires a Shape or Text layer.");
                    if (layer.Behaviour is TextLayerBehaviour textSource)
                    {
                        // Prepare every glyph before replacing the source, including Frame overflow clipping.
                        TextToVector.ApplyPrepared(layer, TextToVector.PrepareLayer(textSource, document));
                    }
                    else
                    {
                        layer.SetBehaviour(ShapeToVector.Convert((ShapeLayerBehaviour)layer.Behaviour, document));
                    }
                    VectorLayerStructure.FitFrame(layer, document.width, document.height);
                    break;
                case "fx":
                    Keys(operation, "op", "layer", "edits");
                    ApplyFxOperations(document, layer, operation["edits"], execute, aliases);
                    break;
                case "delete":
                case "duplicate":
                case "merge":
                case "convertToDrawing":
                    layer = EditLayerStructure(document, layer, operation, aliases, execute);
                    break;
                case "blurStroke":
                case "smudgeStroke":
                case "healStroke":
                    PaintRepair(document, layer, operation, execute);
                    break;
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
                    Require(effect.inputMode == EffectInputMode.Specific || operation["target"] == null, "Previous and AllBelow inputs do not take a target.");
                    effect.TargetLayerId = effect.inputMode == EffectInputMode.Specific
                        ? Resolve(document, Text(operation, "target"), aliases).Id : null;
                    break;
                case "stroke":
                    Keys(operation, "op", "layer", "points", "pressures", "space", "erase", "brush", "pencil");
                    Require(layer?.Behaviour is DrawingLayerBehaviour, "stroke requires a Drawing layer.");
                    Paint(document, (DrawingLayerBehaviour)layer, operation, execute);
                    break;
                case "move":
                    Keys(operation, "op", "layer", "parent", "index");
                    List<Layer> destination = Container(document, Text(operation, "parent"), aliases);
                    if (layer?.AsGroup() is Layer movingGroup)
                        Require(!ContainsContainer(movingGroup, destination), "A group cannot be moved into itself or its descendants.");
                    Require(document.CanInsertInto(layer, destination), "Boolean Group accepts only closed Shape/Vector geometry, editable Text and nested Boolean Groups.");
                    Require(document.TryFindLayer(layer, out List<Layer> source, out int sourceIndex), "Layer not found.");
                    int destinationIndex = Int(operation, "index", 0, 0, destination.Count - (ReferenceEquals(source, destination) ? 1 : 0));
                    document.PreserveTransformForMove(layer, destination);
                    source.RemoveAt(sourceIndex);
                    destination.Insert(destinationIndex, layer);
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

        private static void ResizeCanvas(WhimTexDocument document, JObject operation)
        {
            Keys(operation, "op", "width", "height", "preserveLayout");
            Require(operation["width"] != null && operation["height"] != null, "resize requires width and height.");
            int width = Int(operation, "width", 0, 1, 16384);
            int height = Int(operation, "height", 0, 1, 16384);
            Require((long)width * height <= MaxCanvasPixels, "resize exceeds 16,777,216 canvas pixels.", "resource_limit");
            bool preserveLayout = Bool(operation, "preserveLayout", true);
            if (width == document.width && height == document.height) return;
            Require(document.width > 0 && document.height > 0, "Current canvas dimensions must be positive.");
            if (preserveLayout)
            {
                bool proportional = (long)width * document.height == (long)height * document.width;
                foreach (var layer in Enumerate(document.layers))
                {
                    var transform = layer.transform;
                    if (transform.storage == TransformStorage.Projective) continue; // Already normalized UV.
                    if (proportional)
                        transform.position = new Double2(transform.position.x * width / document.width,
                            transform.position.y * height / document.height);
                    else
                        Require(transform.TrySetMatrix(transform.ToMatrix(document.width, document.height)),
                            "Cannot preserve this layer transform while resizing.");
                    layer.transform = transform;
                }
            }
            document.width = width;
            document.height = height;
            document.RefreshTransformHierarchy();
        }

        private static Layer Resolve(WhimTexDocument document, string reference, Dictionary<string, Layer> aliases)
        {
            Require(!string.IsNullOrWhiteSpace(reference), "A layer ID or @alias is required.");
            Layer layer;
            if (reference.StartsWith("@")) aliases.TryGetValue(reference.Substring(1), out layer);
            else layer = document.FindLayer(reference);
            Require(layer != null && document.FindLayer(layer.Id) == layer, "Layer not found: " + reference, "layer_not_found");
            return layer;
        }

        private static List<Layer> Container(WhimTexDocument document, string parent, Dictionary<string, Layer> aliases)
        {
            if (string.IsNullOrEmpty(parent)) return document.layers;
            Layer layer = Resolve(document, parent, aliases);
            Require(layer?.IsGroup == true, "parent must reference a group.");
            return ((Layer)layer).layers;
        }

        private static void SetLayer(WhimTexDocument document, Layer layer, JObject settings)
        {
            Keys(settings, "name", "enabled", "clippingMask", "opacity", "blend", "filter", "source", "colorRange", "blendRange", "channelMapping", "compositing", "color", "brush",
                "fillMode", "fillPattern", "metric", "sharpMode", "miterLimit", "contourMergeDistance", "outlineWidth", "outlineSoftness", "outlinePosition", "outlineOffset", "fillCenter", "fillColor", "sourceChannel", "threshold",
                "distancePosition", "inverted", "maxDistance", "sourceOffset", "sourceEdges", "contourOffset", "insideDistance", "outsideDistance", "profile", "encoding", "gradient", "gradientType", "freeform", "mesh", "normalMap", "blur", "sharpen", "makeSeamless", "noise", "shape", "vector", "booleanGroup", "booleanOperation", "text");
            foreach (var property in settings.Properties())
            {
                string key = property.Name;
                bool valid = key == "name" || key == "enabled" || key == "clippingMask" || key == "opacity" || key == "blend" ||
                    key == "colorRange" || key == "blendRange" || key == "channelMapping" || key == "booleanOperation" ||
                    key == "booleanGroup" && layer?.Behaviour is BooleanGroupLayerBehaviour ||
                    key == "filter" && layer?.Behaviour is BooleanGroupLayerBehaviour ||
                    key == "compositing" && layer?.IsGroup == true || !layer.IsGroup &&
                    (key == "opacity" || key == "blend" || key == "filter" ||
                    key == "source" && layer?.Behaviour is FileLayerBehaviour || key == "brush" && layer?.Behaviour is DrawingLayerBehaviour ||
                    key == "color" && (layer?.Behaviour is ColorFillLayerBehaviour || layer?.Behaviour is OutlineLayerBehaviour) ||
                    (key == "fillMode" || key == "fillPattern") && layer?.Behaviour is ColorFillLayerBehaviour ||
                    (key == "metric" || key == "sharpMode" || key == "miterLimit") && (layer?.Behaviour is SDFLayerBehaviour || layer?.Behaviour is OutlineLayerBehaviour) ||
                    key == "normalMap" && layer?.Behaviour is NormalMapLayerBehaviour ||
                    key == "blur" && layer?.Behaviour is BlurLayerBehaviour ||
                    key == "sharpen" && layer?.Behaviour is SharpenLayerBehaviour ||
                    key == "makeSeamless" && layer?.Behaviour is MakeSeamlessLayerBehaviour ||
                    key == "noise" && layer?.Behaviour is NoiseLayerBehaviour ||
                    key == "shape" && layer?.Behaviour is ShapeLayerBehaviour ||
                    key == "vector" && layer?.Behaviour is VectorLayerBehaviour ||
                    key == "text" && layer?.Behaviour is TextLayerBehaviour ||
                    (key == "sourceChannel" || key == "outlineWidth" || key == "outlineSoftness" || key == "outlinePosition" ||
                     key == "outlineOffset" || key == "fillCenter" || key == "fillColor") && layer?.Behaviour is OutlineLayerBehaviour ||
                    (key == "sourceChannel" || key == "threshold" || key == "distancePosition" || key == "inverted" || key == "maxDistance" || key == "sourceOffset" || key == "sourceEdges" || key == "contourOffset" || key == "contourMergeDistance" || key == "insideDistance" || key == "outsideDistance" || key == "profile" || key == "encoding") && layer?.Behaviour is SDFLayerBehaviour ||
                    (key == "gradientType" || key == "freeform" || key == "mesh") && layer?.Behaviour is GradientLayerBehaviour ||
                    key == "gradient" && (layer?.Behaviour is GradientLayerBehaviour || layer?.Behaviour is SDFLayerBehaviour));
                Require(valid, key + " is not supported by " + TypeName(layer) + " layers.");
            }
            layer.layerName = Text(settings, "name", layer.layerName);
            layer.enabled = Bool(settings, "enabled", layer.enabled);
            layer.clippingMask = Bool(settings, "clippingMask", layer.clippingMask);
            Require(!(layer?.Behaviour is ShaderProcessorLayerBehaviour) || !layer.clippingMask, "Shader Processor is a stack operation and cannot be a clipping layer.");
            layer.opacity = Number(settings, "opacity", layer.opacity, 0f, 1f);
            layer.blendMode = Enum(settings, "blend", layer.blendMode);
            layer.booleanOperation = Enum(settings, "booleanOperation", layer.booleanOperation);
            if (settings["channelMapping"] != null)
            {
                Require(settings["channelMapping"] is JArray array && array.Count == 4,
                    "channelMapping must contain four channel names in output RGBA order.");
                var values = (JArray)settings["channelMapping"];
                var channelMapping = new LayerChannelMapping();
                for (int channel = 0; channel < 4; channel++)
                {
                    int source = values[channel].Type == JTokenType.String
                        ? System.Array.IndexOf(LayerChannelMapping.Labels, (string)values[channel]) : -1;
                    Require(source >= 0, "Invalid channelMapping channel. Use " + string.Join(", ", LayerChannelMapping.Labels) + ".");
                    channelMapping[channel] = (ChannelMappingSource)source;
                }
                layer.channelMapping = channelMapping;
            }
            if (layer?.AsGroup() is Layer group)
            {
                group.compositing = Enum(settings, "compositing", group.compositing);
                Require(!(layer.Behaviour is BooleanGroupLayerBehaviour) || group.compositing == GroupCompositing.Isolated,
                    "Boolean Group requires Isolated compositing.");
            }
            layer.filterMode = Enum(settings, "filter", layer.filterMode);
            if (layer?.Behaviour is FileLayerBehaviour file && settings["source"] != null)
            {
                string path = Text(settings, "source");
                if (!System.IO.Path.IsPathRooted(path)) path = ReadAssetPath(path);
                file.AssignSourcePath(path, document);
            }
            layer.colorRange = Enum(settings, "colorRange", layer.colorRange);
            layer.blendRange = Enum(settings, "blendRange", layer.blendRange);
            if (layer?.Behaviour is ColorFillLayerBehaviour fill)
            {
                if (settings["color"] != null) fill.color = Color(settings["color"]);
                fill.mode = Enum(settings, "fillMode", fill.mode);
                if (settings["fillPattern"] != null)
                    SetFillPattern(fill.pattern ??= new FillPatternSettings(), Obj(settings["fillPattern"], "fillPattern"));
            }
            if (layer?.Behaviour is NormalMapLayerBehaviour normal && settings["normalMap"] != null)
                SetNormalMap(normal, Obj(settings["normalMap"], "normalMap"));
            if (layer?.Behaviour is BlurLayerBehaviour blur && settings["blur"] != null)
                SetBlur(blur, Obj(settings["blur"], "blur"));
            if (layer?.Behaviour is SharpenLayerBehaviour sharpen && settings["sharpen"] != null)
                SetSharpen(sharpen, Obj(settings["sharpen"], "sharpen"));
            if (layer?.Behaviour is MakeSeamlessLayerBehaviour seamless && settings["makeSeamless"] != null)
                SetMakeSeamless(seamless, Obj(settings["makeSeamless"], "makeSeamless"));
            if (layer?.Behaviour is NoiseLayerBehaviour noise && settings["noise"] != null)
                SetNoise(noise, Obj(settings["noise"], "noise"));
            if (layer?.Behaviour is ShapeLayerBehaviour shape && settings["shape"] != null)
                SetShape(shape, Obj(settings["shape"], "shape"));
            if (layer?.Behaviour is VectorLayerBehaviour vector && settings["vector"] != null)
                WhimTexDocumentJson.PatchVector(vector, Obj(settings["vector"], "vector"), document);
            if (layer?.Behaviour is BooleanGroupLayerBehaviour booleanGroup && settings["booleanGroup"] != null)
            {
                WhimTexDocumentJson.PatchBooleanGroup(booleanGroup, Obj(settings["booleanGroup"], "booleanGroup"), document);
                booleanGroup.Validate();
            }
            if (layer?.Behaviour is TextLayerBehaviour text && settings["text"] != null)
                SetText(text, Obj(settings["text"], "text"));
            if (layer?.Behaviour is DrawingLayerBehaviour drawing && settings["brush"] != null) SetBrush(document, drawing, Obj(settings["brush"], "brush"));
            if (layer?.Behaviour is OutlineLayerBehaviour outline)
            {
                outline.metric = Enum(settings, "metric", outline.metric);
                outline.sharpMode = Enum(settings, "sharpMode", outline.sharpMode);
                outline.miterLimit = Number(settings, "miterLimit", outline.miterLimit, 1f, 64f);
                outline.sourceChannel = Enum(settings, "sourceChannel", outline.sourceChannel);
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
                sdf.sharpMode = Enum(settings, "sharpMode", sdf.sharpMode);
                sdf.miterLimit = Number(settings, "miterLimit", sdf.miterLimit, 1f, 64f);
                sdf.contourMergeDistance = Number(settings, "contourMergeDistance", sdf.contourMergeDistance, 0f, 2f);
                sdf.sourceChannel = Enum(settings, "sourceChannel", sdf.sourceChannel);
                sdf.threshold = (byte)Int(settings, "threshold", sdf.threshold, 0, 255);
                sdf.distancePosition = Enum(settings, "distancePosition", sdf.distancePosition);
                sdf.inverted = Bool(settings, "inverted", sdf.inverted);
                sdf.maxDistanceNormalization = Number(settings, "maxDistance", sdf.maxDistanceNormalization, 0f, 16384f);
                sdf.contourOffset = Number(settings, "contourOffset", sdf.contourOffset, -16384, 16384);
                sdf.insideDistance = Number(settings, "insideDistance", sdf.insideDistance, 0, 16384);
                sdf.outsideDistance = Number(settings, "outsideDistance", sdf.outsideDistance, 0, 16384);
                sdf.sourceEdges = Enum(settings, "sourceEdges", sdf.sourceEdges);
                if (settings["sourceOffset"] != null)
                {
                    var offset = Vector(settings["sourceOffset"], "sourceOffset");
                    Require(Mathf.Abs(offset.x) <= 16384 && Mathf.Abs(offset.y) <= 16384, "sourceOffset must be within -16384..16384.");
                    sdf.sourceOffset = offset;
                }
                if (settings["profile"] != null) sdf.profile = WhimTexCurveTexture.Parse(Text(settings, "profile"));
                sdf.encoding = Enum(settings, "encoding", sdf.encoding);
                Require(sdf.encoding != SDFLayerBehaviour.OutputEncoding.MultiChannelData || sdf.metric == DistanceMetric.MTSDF,
                    "MultiChannelData requires metric MTSDF.");
                if (settings["gradient"] != null) sdf.gradient = ReadGradient(settings["gradient"]);
            }
            if (layer?.Behaviour is GradientLayerBehaviour gradient)
            {
                gradient.gradientType = Enum(settings, "gradientType", gradient.gradientType);
                if (settings["gradient"] != null) gradient.gradient = ReadGradient(settings["gradient"]);
                if (settings["freeform"] != null) gradient.freeform = ReadFreeform(settings["freeform"]);
                if (settings["mesh"] != null) gradient.mesh = ReadMesh(settings["mesh"]);
                WhimTexDocumentJson.ValidateMeshStorage(gradient);
            }
        }

        private static void ApplyMeshOperation(Layer layer, JObject operation)
        {
            Require(layer?.Behaviour is GradientLayerBehaviour, "mesh requires a Gradient layer.");
            var gradient = (GradientLayerBehaviour)layer.Behaviour;
            Require(gradient.mesh != null, "Mesh gradient data is missing.");
            gradient.mesh.Validate();
            string action = Text(operation, "action");
            Require(action == "nodeMode" || action == "moveEdge", "Unknown mesh action: " + action);
            if (action == "nodeMode") Keys(operation, "op", "layer", "action", "node", "mode");
            else Keys(operation, "op", "layer", "action", "node", "direction", "delta");
            Require(operation["node"] != null, "mesh requires an explicit node index.");
            int node = Int(operation, "node", -1, 0, gradient.mesh.NodeCount - 1);
            var next = gradient.mesh.Clone();
            if (action == "nodeMode")
            {
                Require(operation["mode"] != null, "nodeMode requires mode: Smooth or Corner.");
                next.SetNodeMode(node, Enum(operation, "mode", MeshGradientNodeMode.Smooth));
            }
            else
            {
                Require(operation["direction"] != null, "moveEdge requires direction: 0=left, 1=right, 2=down, 3=up.");
                int direction = Int(operation, "direction", -1, 0, 3);
                Require(next.HandleIndex(node, direction) >= 0, "The selected node has no edge in that direction.");
                next.MoveEdge(node, direction, ReadMeshVector(operation["delta"], "mesh delta"));
            }
            next.Validate();
            gradient.mesh = next;
        }

        private static Vector2 ReadMeshVector(JToken token, string name)
        {
            Require(token is JArray xy && xy.Count == 2, name + " must contain two finite components.");
            return new Vector2(Number(token[0], name + " x", -float.MaxValue, float.MaxValue),
                Number(token[1], name + " y", -float.MaxValue, float.MaxValue));
        }

        private static MeshGradient ReadMesh(JToken token)
        {
            var data = Obj(token, "mesh"); Keys(data, "columns", "rows", "colorMode", "positions", "colors", "hueChroma", "nodeModes", "patchPriorities");
            foreach (string field in new[] { "columns", "rows", "colorMode", "positions", "colors", "nodeModes" })
                Require(data[field] != null, "mesh." + field + " is required; provide the complete control lattice.");
            int columns = Int(data, "columns", 1, 1, MeshGradient.MaxCells), rows = Int(data, "rows", 1, 1, MeshGradient.MaxCells);
            int count = (3 * columns + 1) * (3 * rows + 1);
            Require(data["positions"] is JArray positions && positions.Count == count,
                "mesh.positions length must equal (3 * columns + 1) * (3 * rows + 1).");
            Require(data["colors"] is JArray colors && colors.Count == count,
                "mesh.colors length must equal (3 * columns + 1) * (3 * rows + 1).");
            bool implicitSmooth = data["nodeModes"].Type == JTokenType.Null;
            Require(implicitSmooth || data["nodeModes"] is JArray modes && modes.Count == (columns + 1) * (rows + 1),
                "mesh.nodeModes must be null (all Smooth), or have (columns + 1) * (rows + 1) entries.");
            var result = new MeshGradient
            {
                columns = columns, rows = rows,
                colorMode = Enum(data, "colorMode", WhimTexGradientColorMode.Perceptual),
                positions = new Vector2[count], colors = new Color[count],
                nodeModes = implicitSmooth ? null : new MeshGradientNodeMode[(columns + 1) * (rows + 1)],
                patchPriorities = null
            };
            for (int i = 0; i < count; i++)
            {
                JToken position = data["positions"][i], color = data["colors"][i];
                Require(color is JArray rgba && rgba.Count == 4, "Mesh color must contain four working-space coefficients.");
                result.positions[i] = ReadMeshVector(position, "mesh position");
                // These are cubic coefficients, not display colors. In particular, alpha controls may overshoot.
                result.colors[i] = new Color(
                    Number(color[0], "mesh color 0", -float.MaxValue, float.MaxValue),
                    Number(color[1], "mesh color 1", -float.MaxValue, float.MaxValue),
                    Number(color[2], "mesh color 2", -float.MaxValue, float.MaxValue),
                    Number(color[3], "mesh alpha", -float.MaxValue, float.MaxValue));
            }
            if (data["hueChroma"] != null && data["hueChroma"].Type != JTokenType.Null)
            {
                Require(data["hueChroma"] is JArray chroma && chroma.Count == count,
                    "mesh.hueChroma length must equal (3 * columns + 1) * (3 * rows + 1).");
                result.hueChroma = new float[count];
                for (int i = 0; i < count; i++) result.hueChroma[i] = Number(data["hueChroma"][i], "mesh hueChroma", -float.MaxValue, float.MaxValue);
            }
            for (int i = 0; i < (result.nodeModes?.Length ?? 0); i++)
            {
                JToken mode = data["nodeModes"][i];
                Require(mode.Type == JTokenType.String && ((string)mode == "Smooth" || (string)mode == "Corner"),
                    "mesh.nodeModes entries must be Smooth or Corner.");
                result.nodeModes[i] = (string)mode == "Smooth" ? MeshGradientNodeMode.Smooth : MeshGradientNodeMode.Corner;
            }
            if (data["patchPriorities"] != null && data["patchPriorities"].Type != JTokenType.Null)
            {
                Require(data["patchPriorities"] is JArray priorities && priorities.Count == columns * rows,
                    "mesh.patchPriorities must be null, or have columns * rows entries.");
                result.patchPriorities = new Vector3[columns * rows];
                for (int i = 0; i < result.patchPriorities.Length; i++)
                {
                    JToken value = data["patchPriorities"][i];
                    Require(value is JArray coefficients && coefficients.Count == 3, "Mesh priority must have three coefficients.");
                    result.patchPriorities[i] = new Vector3(
                        Number(value[0], "mesh priority constant", 0f, 1f),
                        Number(value[1], "mesh priority u", 0f, 1f),
                        Number(value[2], "mesh priority v", 0f, 1f));
                }
            }
            result.Validate();
            return result.Clone();
        }

        private static FreeformGradient ReadFreeform(JToken token)
        {
            var data = Obj(token, "freeform"); Keys(data, "colorMode", "points");
            Require(data["points"] is JArray points && points.Count >= 1 && points.Count <= FreeformGradient.MaxPoints,
                "freeform.points must contain 1..64 points.");
            var result = new FreeformGradient { colorMode = Enum(data, "colorMode", WhimTexGradientColorMode.Perceptual) }; result.points.Clear();
            foreach (var item in (JArray)data["points"])
            {
                var point = Obj(item, "freeform point"); Keys(point, "position", "color", "influence");
                Require(point["position"] is JArray position && position.Count == 2, "Point position must contain x and y.");
                var xy = (JArray)point["position"];
                var numbers = new JObject { ["x"] = xy[0].DeepClone(), ["y"] = xy[1].DeepClone() };
                var uv = new Vector2(Number(numbers, "x", 0, -10000, 10000), Number(numbers, "y", 0, -10000, 10000));
                Require(point["color"] != null, "Point color is required.");
                result.points.Add(new FreeformGradientPoint(uv, AgentJson.Color(point["color"]),
                    Number(point, "influence", .35f, FreeformGradient.MinInfluence, FreeformGradient.MaxInfluence)));
            }
            result.Validate(); return result;
        }

        internal static WhimTexGradient ReadGradient(JToken token, WhimTexGradientColorMode defaultColorMode = WhimTexGradientColorMode.Perceptual, float maximumColor = 107f)
        {
            if (token is JObject data)
            {
                Require(data["mode"] == null, "Gradient mode is no longer supported. Use colorMode and interpolation.");
                Keys(data, "colors", "alphas", "colorMode", "interpolation", "huePath", "chromaCorrection", "wrapMode", "smoothness", "colorSpace", "customKeys", "customColorMode");
                var result = ReadGradient(data["colors"], defaultColorMode, maximumColor);
                result.ColorMode = Enum(data, "colorMode", defaultColorMode);
                var interpolation = Enum(data, "interpolation", WhimTexGradientInterpolation.Balanced);
                result.HuePath = Enum(data, "huePath", WhimTexGradientHuePath.Shortest);
                result.ChromaCorrection = Number(data, "chromaCorrection", 0f, 0f, 1f);
                result.WrapMode = Enum(data, "wrapMode", WhimTexGradientWrapMode.Clamp);
                result.Smoothness = Number(data, "smoothness", 1f, 0f, 1f);
                result.ColorSpace = Enum(data, "colorSpace", ColorSpace.Gamma);
                if (data["alphas"] != null)
                {
                    Require(data["alphas"] is JArray alphaValues && alphaValues.Count >= 1 && alphaValues.Count <= 64, "alphas must have 1..64 keys.");
                    var a = (JArray)data["alphas"];
                    var alphaKeys = new GradientAlphaKey[a.Count];
                    for (int i=0;i<a.Count;i++)
                    {
                        var key=Obj(a[i], "alpha key"); Keys(key,"time","alpha","midpoint");
                        alphaKeys[i]=new GradientAlphaKey(Number(key["alpha"],"alpha",0,1),Number(key["time"],"time",0,1));
                        Require(i==0 || alphaKeys[i].time > alphaKeys[i-1].time,"Alpha times must increase.");
                    }
                    result.SetKeys(result.ColorKeys,alphaKeys);
                    for(int i=0;i<a.Count;i++) result.SetMidpoint(true,i,Number((JObject)a[i],"midpoint",.5f,.01f,.99f));
                }
                var customKeys = ReadCustomGradientKeys(data["customKeys"]);
                bool hasCustom = customKeys != null && customKeys.Length != 0;
                Require(!hasCustom || data["customColorMode"] != null, "customColorMode is required with customKeys.");
                Require(interpolation != WhimTexGradientInterpolation.Custom || hasCustom,
                    "Custom interpolation requires explicit customKeys; convert an automatic gradient with the C# API or curve editor first.");
                var customMode = Enum(data, "customColorMode", WhimTexGradientColorMode.Perceptual);
                result.SetCustomData(customKeys, customMode);
                // Select Custom only after restoring both datasets: the setter may prepare initial curves.
                result.Interpolation = interpolation;
                return result;
            }
            Require(token is JArray keys && keys.Count >= 1 && keys.Count <= 64, "gradient must contain 1..64 {time, color} stops.");
            var values = (JArray)token;
            var colors = new GradientColorKey[values.Count];
            var alphas = new GradientAlphaKey[values.Count];
            float previous = -1f;
            for (int i = 0; i < values.Count; i++)
            {
                JObject stop = Obj(values[i], "gradient stop");
                Keys(stop, "time", "color", "midpoint", "alphaMidpoint");
                float time = Number(stop["time"], "time", 0f, 1f);
                Require(time > previous, "Gradient stop times must be strictly increasing.");
                var rgba = stop["color"];
                Require(rgba is JArray components && components.Count == 4, "color must be [r,g,b,a].");
                var color = new UnityEngine.Color(Number(rgba[0], "r", -maximumColor, maximumColor),
                    Number(rgba[1], "g", -maximumColor, maximumColor), Number(rgba[2], "b", -maximumColor, maximumColor),
                    Number(rgba[3], "a", 0, 1));
                colors[i] = new GradientColorKey(color, time);
                alphas[i] = new GradientAlphaKey(color.a, time);
                previous = time;
            }
            var gradient = GradientUtility.Create(colors, alphas);
            gradient.ColorMode = defaultColorMode;
            for(int i=0;i<values.Count;i++)
            {
                gradient.SetMidpoint(false,i,Number((JObject)values[i],"midpoint",.5f,.01f,.99f));
                gradient.SetMidpoint(true,i,Number((JObject)values[i],"alphaMidpoint",.5f,.01f,.99f));
            }
            return gradient;
        }

        private static WhimTexGradientCurveKey[] ReadCustomGradientKeys(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            Require(token is JArray values && values.Count <= 256, "customKeys must be an array with at most 256 keys.");
            var data = (JArray)token;
            var result = new WhimTexGradientCurveKey[data.Count];
            for (int i = 0; i < data.Count; i++)
            {
                var key = Obj(data[i], "custom gradient key");
                Keys(key, "id", "channel", "time", "value", "inTangent", "outTangent", "tangentMode");
                Require(key["id"] != null && key["channel"] != null && key["time"] != null && key["value"] != null && key["tangentMode"] != null,
                    "Custom keys require id, channel, time, value and tangentMode.");
                int channel = Int(key, "channel", 0, 0, 3);
                result[i] = new WhimTexGradientCurveKey
                {
                    id = Int(key, "id", 0, 1, int.MaxValue), channel = channel,
                    time = Number(key["time"], "time", 0, 1),
                    value = Number(key["value"], "value", channel == 3 ? 0 : -float.MaxValue, channel == 3 ? 1 : float.MaxValue),
                    inTangent = Number(key, "inTangent", 0, -float.MaxValue, float.MaxValue),
                    outTangent = Number(key, "outTangent", 0, -float.MaxValue, float.MaxValue),
                    tangentMode = Enum(key, "tangentMode", WhimTexGradientTangentMode.Auto)
                };
            }
            // SetCustomData validates IDs, channel coverage and collision spacing.
            return result;
        }

        internal static JObject WriteGradient(WhimTexGradient gradient)
        {
            if (gradient == null) throw new ArgumentNullException(nameof(gradient));
            gradient.Evaluate(0);
            var colors = new JArray();
            var alphas = new JArray();
            var c = gradient.AutomaticColorKeys;
            var a = gradient.AutomaticAlphaKeys;
            for (int i = 0; i < c.Length; i++)
                colors.Add(new JObject { ["time"] = c[i].time, ["color"] = Json(c[i].color), ["midpoint"] = gradient.GetMidpoint(false, i) });
            for (int i = 0; i < a.Length; i++)
                alphas.Add(new JObject { ["time"] = a[i].time, ["alpha"] = a[i].alpha, ["midpoint"] = gradient.GetMidpoint(true, i) });
            var result = new JObject
            {
                ["colors"] = colors, ["alphas"] = alphas, ["colorMode"] = gradient.AutomaticColorMode.ToString(),
                ["interpolation"] = gradient.Interpolation.ToString(), ["huePath"] = gradient.HuePath.ToString(),
                ["chromaCorrection"] = gradient.ChromaCorrection, ["wrapMode"] = gradient.WrapMode.ToString(),
                ["smoothness"] = gradient.Smoothness, ["colorSpace"] = gradient.ColorSpace.ToString()
            };
            if (gradient.HasCustomCurves)
            {
                var keys = new JArray();
                foreach (var key in gradient.CustomKeys)
                    keys.Add(new JObject { ["id"] = key.id, ["channel"] = key.channel,
                        ["time"] = key.time, ["value"] = key.value, ["inTangent"] = key.inTangent,
                        ["outTangent"] = key.outTangent, ["tangentMode"] = key.tangentMode.ToString() });
                result["customColorMode"] = gradient.CustomColorMode.ToString();
                result["customKeys"] = keys;
            }
            return result;
        }

        private static double TransformNumber(JToken token)
        {
            Require(token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float), "Transform values must be numbers.");
            double value=token.Value<double>();
            Require(ProjectiveMatrix.Finite(value) && System.Math.Abs(value)<=1e15, "Transform value is non-finite or too large.");
            return value;
        }

        private static Double2 TransformVector(JToken token,string name)
        {
            Require(token is JArray && ((JArray)token).Count==2,name+" must contain two numbers.");
            return new Double2(TransformNumber(token[0]),TransformNumber(token[1]));
        }

        private static void SetTransform(WhimTexDocument document, Layer layer, JObject settings)
        {
            Keys(settings, "reset", "position", "scale", "pivot", "rotation", "tiling", "originalAspect", "matrix");
            TextureTransform transform = Bool(settings, "reset") ? TextureTransform.Default : layer.transform;
            var size = new Vector2(document.width, document.height);
            if (settings["pivot"] != null) Require(transform.TrySetPivot(TransformVector(settings["pivot"], "pivot")),"Invalid pivot.");
            if (settings["matrix"] != null)
            {
                Require(settings["position"] == null && settings["scale"] == null && settings["rotation"] == null && !Bool(settings,"originalAspect"),
                    "matrix cannot be combined with position, scale, rotation or originalAspect.");
                Require(settings["matrix"] is JArray && ((JArray)settings["matrix"]).Count == 9, "matrix must contain nine row-major numbers.");
                var a = (JArray)settings["matrix"];
                var m = new ProjectiveMatrix {
                    m00=TransformNumber(a[0]),m01=TransformNumber(a[1]),m02=TransformNumber(a[2]),
                    m10=TransformNumber(a[3]),m11=TransformNumber(a[4]),m12=TransformNumber(a[5]),
                    m20=TransformNumber(a[6]),m21=TransformNumber(a[7]),m22=TransformNumber(a[8]) };
                Require(transform.TrySetMatrix(m), "matrix must be invertible with no horizon crossing the source rectangle.");
            }
            else
            {
                if (settings["position"] != null) transform.EditPosition(TransformVector(settings["position"], "position"),size);
                if (settings["scale"] != null)
                {
                    var value=TransformVector(settings["scale"],"scale");
                    Require(System.Math.Abs(value.x)>=0.00001 && System.Math.Abs(value.y)>=0.00001,"Transform scale must be nonzero.");
                    transform.EditScale(value,size);
                }
                if (settings["rotation"] != null)
                {
                    double value=TransformNumber(settings["rotation"]);
                    Require(System.Math.Abs(value)<=360000,"rotation must be within [-360000,360000].");
                    transform.EditRotation(value,size);
                }
            }
            Require(TiledCanvasUtility.IsInvertible(transform), "Invalid transform.");
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
