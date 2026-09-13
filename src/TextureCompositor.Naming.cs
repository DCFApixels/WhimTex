using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.SpriteEditor
{
    public sealed partial class TextureCompositor
    {
        [Serializable]
        private sealed class LayerNameCounter
        {
            public string prefix;
            public int next = 1;
        }

        [SerializeField, HideInInspector] private List<LayerNameCounter> layerNameCounters = new List<LayerNameCounter>();
        [SerializeField, HideInInspector] private int nextCopyNumber = 1;

        internal static string LayerMenuName(Layer layer) =>
            LayerTypeRegistry.Find(layer?.Behaviour?.GetType())?.MenuName ?? LayerNamePrefix(layer);

        private static string LayerNamePrefix(Layer layer) =>
            LayerTypeRegistry.Find(layer?.Behaviour?.GetType())?.NamePrefix ??
            (layer?.IsGroup == true ? "Group" : layer?.Behaviour == null ? "Missing Behaviour" : ObjectNames.NicifyVariableName(layer.Behaviour.GetType().Name));

        private static readonly string[] ShapeNamePrefixes = Enum.GetNames(typeof(ShapeLayerBehaviour.ShapeKind));

        internal string AllocateLayerName(Layer layer, string displayPrefix = null) => AllocateName(LayerNamePrefix(layer), displayPrefix);
        internal string AllocateGroupName() => AllocateName("Group");

        private string AllocateName(string prefix, string displayPrefix = null)
        {
            SynchronizeNextAutomaticNumbers();
            LayerNameCounter counter = GetNameCounter(prefix);
            int number = counter.next;
            if (number == int.MaxValue)
                throw new InvalidOperationException("Layer name counter is exhausted.");
            counter.next++;
            return (displayPrefix ?? prefix) + " " + number;
        }

        private string AllocateDuplicateName(Layer source)
        {
            SynchronizeNextAutomaticNumbers();
            if (nextCopyNumber == int.MaxValue)
                throw new InvalidOperationException("Copy name counter is exhausted.");
            return source.layerName + " Copy " + nextCopyNumber++;
        }

        private LayerNameCounter GetNameCounter(string prefix)
        {
            layerNameCounters ??= new List<LayerNameCounter>();
            LayerNameCounter result = null;
            foreach (LayerNameCounter counter in layerNameCounters)
            {
                if (counter == null)
                    continue;
                if (counter.prefix == prefix)
                {
                    result ??= counter;
                    result.next = Mathf.Max(result.next, counter.next);
                }
            }
            if (result != null)
                return result;
            LayerNameCounter created = new LayerNameCounter { prefix = prefix };
            layerNameCounters.Add(created);
            return created;
        }

        private void SynchronizeNextAutomaticNumbers()
        {
            nextCopyNumber = Mathf.Max(1, nextCopyNumber);
            Scan(layers);

            void Scan(List<Layer> source)
            {
                if (source == null)
                    return;
                foreach (Layer layer in source)
                {
                    if (layer == null)
                        continue;
                    string name = layer.layerName ?? string.Empty;
                    string prefix = LayerNamePrefix(layer);
                    LayerNameCounter counter = GetNameCounter(prefix);
                    counter.next = Mathf.Max(1, counter.next);
                    if (name.StartsWith(prefix + " ", StringComparison.Ordinal) &&
                        int.TryParse(name.Substring(prefix.Length + 1), out int number))
                        counter.next = SynchronizeCounter(counter.next, number);
                    // Shape names may retain an earlier kind after the user changes Properties.
                    // All kinds still advance the one Shape counter, including imported layers.
                    if (layer.Behaviour is ShapeLayerBehaviour)
                        foreach (string shapePrefix in ShapeNamePrefixes)
                            if (name.StartsWith(shapePrefix + " ", StringComparison.Ordinal) &&
                                int.TryParse(name.Substring(shapePrefix.Length + 1), out int shapeNumber))
                                counter.next = SynchronizeCounter(counter.next, shapeNumber);
                    int copySuffix = name.LastIndexOf(" Copy ", StringComparison.Ordinal);
                    if (copySuffix >= 0 && int.TryParse(name.Substring(copySuffix + 6), out int copyNumber))
                        nextCopyNumber = SynchronizeCounter(nextCopyNumber, copyNumber);
                    if (layer?.AsGroup() is Layer group)
                        Scan(group.layers);
                }
            }
        }
    }
}
