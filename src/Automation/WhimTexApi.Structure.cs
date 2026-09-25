using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static Layer EditLayerStructure(TextureCompositor document, Layer layer, JObject operation,
            Dictionary<string, Layer> aliases, bool execute)
        {
            string op = Text(operation, "op");
            Require(!ContainsReservation(layer) && !IsLayerContentLocked(document, layer), "Layer contains active agent work.", "layer_locked");
            if (op == "delete")
            {
                Keys(operation, "op", "layer");
                document.TryFindLayer(layer, out var container, out _);
                if (execute) document.DestroyLayerAssets(layer);
                container.Remove(layer);
                layer.ReleaseTransientResources();
                foreach (string key in aliases.Where(p => document.FindLayer(p.Value.Id) == null).Select(p => p.Key).ToArray()) aliases.Remove(key);
                return layer;
            }
            string alias = Text(operation, "as");
            Require(alias == null || alias.Length > 0 && alias.Length <= 64 && !alias.StartsWith("@") && !aliases.ContainsKey(alias), "Invalid or duplicate alias.");
            Layer result = layer;
            if (op == "duplicate")
            {
                Keys(operation, "op", "layer", "as");
                result = document.DuplicateLayersForAgent(new List<Layer> { layer }, execute)[layer];
            }
            else if (op == "merge")
            {
                Keys(operation, "op", "layer", "others", "keepSources", "as");
                Require(operation["others"] is JArray others && others.Count > 0 && others.Count <= 1023, "others must list additional layer IDs or aliases.");
                var selected = new List<Layer> { layer };
                foreach (var other in (JArray)operation["others"])
                {
                    Require(other.Type == JTokenType.String, "others must contain layer IDs or aliases.");
                    var item = Resolve(document, (string)other, aliases);
                    Require(!selected.Contains(item) && !ContainsReservation(item) && !IsLayerContentLocked(document, item), "Duplicate or locked merge source.");
                    selected.Add(item);
                }
                result = document.MergeLayersForAgent(selected, Bool(operation, "keepSources"), execute);
            }
            else
            {
                Keys(operation, "op", "layer", "as");
                Require(!(layer.Behaviour is ShaderProcessorLayerBehaviour), "Use FX apply/applyAll to bake a Shader Processor with its backdrop.");
                if (!(layer.Behaviour is DrawingLayerBehaviour))
                {
                    if (execute)
                    {
                        var pixels = document.RasterizeLayer(layer, false);
                        var drawing = DrawingLayerBehaviour.FromRasterizedLayer(layer, pixels, false,
                            layer.IsGroup && document.IsGroupIsolatedByClipping(layer));
                        drawing.MakeTexturePersistent(document);
                        Undo.RegisterCreatedObjectUndo(pixels, UndoName);
                        Undo.RegisterCompleteObjectUndo(document, UndoName);
                        document.DestroyLayerAssets(layer);
                        layer.ReleaseTransientResources();
                        layer.AdoptContent(drawing);
                    }
                    else
                    {
                        var drawing = new DrawingLayerBehaviour();
                        drawing.CopyRasterizedIdentityFrom(layer);
                        drawing.transform = layer.transform;
                        drawing.modifiers = new List<UnityEngine.Object>(layer.modifiers);
                        layer.AdoptContent(drawing);
                    }
                }
            }
            if (alias != null) aliases.Add(alias, result);
            return result;
        }
    }
}
