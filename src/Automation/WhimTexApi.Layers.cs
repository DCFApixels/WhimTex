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
                if (settings["gradient"] != null) sdf.gradient = ReadGradient(settings["gradient"], WhimTexGradientMode.Linear);
            }
            if (layer?.Behaviour is GradientLayerBehaviour gradient && settings["gradient"] != null) gradient.gradient = ReadGradient(settings["gradient"]);
        }

        internal static WhimTexGradient ReadGradient(JToken token, WhimTexGradientMode defaultMode = WhimTexGradientMode.Classic, float maximumColor = 107f)
        {
            if (token is JObject data)
            {
                Keys(data, "colors", "alphas", "mode", "smoothness", "colorSpace");
                var result = ReadGradient(data["colors"], defaultMode, maximumColor);
                result.Mode = Enum(data, "mode", defaultMode);
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
            gradient.Mode = defaultMode;
            for(int i=0;i<values.Count;i++)
            {
                gradient.SetMidpoint(false,i,Number((JObject)values[i],"midpoint",.5f,.01f,.99f));
                gradient.SetMidpoint(true,i,Number((JObject)values[i],"alphaMidpoint",.5f,.01f,.99f));
            }
            return gradient;
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

        private static void SetTransform(TextureCompositor document, Layer layer, JObject settings)
        {
            Require(!layer.IsGroup, "Groups do not have a transform.");
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
