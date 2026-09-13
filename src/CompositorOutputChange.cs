using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.SpriteEditor
{
    internal sealed class CompositorOutputChange
    {
        private readonly TextureCompositor source;
        private readonly Texture2D texture;
        private Dictionary<Texture2D, TextureCompositor> documents;

        internal CompositorOutputChange(TextureCompositor source)
        {
            this.source = source;
            texture = source.OutputTexture;
        }

        internal bool ShouldRefresh(TextureCompositor consumer)
        {
            if (consumer == null || consumer == source || texture == null || !UsesTexture(consumer.layers, texture)) return false;
            if (consumer.OutputTexture == null) return true;
            if (documents == null)
            {
                documents = new Dictionary<Texture2D, TextureCompositor>();
                foreach (var document in Resources.FindObjectsOfTypeAll<TextureCompositor>())
                    if (document.OutputTexture != null) documents[document.OutputTexture] = document;
            }
            // Cyclic links remain readable snapshots, but never drive an automatic feedback loop.
            return !DependsOnTexture(source.layers, consumer.OutputTexture, new HashSet<TextureCompositor> { source });
        }

        private static bool UsesTexture(List<Layer> layers, Texture2D texture)
        {
            if (layers == null) return false;
            foreach (var layer in layers)
            {
                if (layer?.Behaviour is FileLayerBehaviour file && file.sourceTexture == texture) return true;
                if (layer?.IsGroup == true && UsesTexture(layer.layers, texture)) return true;
            }
            return false;
        }

        private bool DependsOnTexture(List<Layer> layers, Texture2D target, HashSet<TextureCompositor> visited)
        {
            if (layers == null) return false;
            foreach (var layer in layers)
            {
                if (layer?.Behaviour is FileLayerBehaviour file && file.sourceTexture != null)
                {
                    if (file.sourceTexture == target) return true;
                    if (documents.TryGetValue(file.sourceTexture, out var dependency) && visited.Add(dependency) &&
                        DependsOnTexture(dependency.layers, target, visited)) return true;
                }
                if (layer?.IsGroup == true && DependsOnTexture(layer.layers, target, visited)) return true;
            }
            return false;
        }
    }
}
