using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        [NonSerialized] private volatile bool undoDeserialized;
        [NonSerialized] private Dictionary<UnityEngine.Object, int> nativeUndoVersions;
        internal static bool IsRefreshingUndo { get; private set; }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }
        void ISerializationCallbackReceiver.OnAfterDeserialize() => undoDeserialized = true;

        private void CaptureNativeUndoVersions()
        {
            nativeUndoVersions ??= new Dictionary<UnityEngine.Object, int>();
            nativeUndoVersions.Clear();
            Capture(layers);

            void Capture(List<Layer> source)
            {
                if (source == null) return;
                foreach (Layer layer in source)
                {
                    if (layer == null) continue;
                    if (layer?.Behaviour is DrawingLayerBehaviour drawing) Track(drawing.StoredTexture);
                    else if (layer?.Behaviour is FileLayerBehaviour) Track(layer.SamplingSource);
                    if (layer.modifiers != null)
                        foreach (UnityEngine.Object modifier in layer.modifiers)
                            if (modifier is Material) Track(modifier);
                    if (layer?.AsGroup() is Layer group) Capture(group.layers);
                }
            }

            void Track(UnityEngine.Object value)
            {
                if (value != null) nativeUndoVersions[value] = EditorUtility.GetDirtyCount(value);
            }
        }

        private bool NativeUndoVersionsChanged()
        {
            if (nativeUndoVersions == null) return false;
            foreach (var entry in nativeUndoVersions)
                if (entry.Key == null || EditorUtility.GetDirtyCount(entry.Key) != entry.Value)
                    return true;
            return false;
        }

        [InitializeOnLoadMethod]
        private static void RegisterModelUndoRefresh()
        {
            Undo.undoRedoPerformed -= RefreshModelsAfterUndo;
            Undo.undoRedoPerformed += RefreshModelsAfterUndo;
        }

        private static void RefreshModelsAfterUndo()
        {
            var changedEffects = new List<ShaderFX>();
            foreach (ShaderFX effect in Resources.FindObjectsOfTypeAll<ShaderFX>())
                if (effect.ConsumeUndoChanges())
                    changedEffects.Add(effect);

            IsRefreshingUndo = true;
            try
            {
                foreach (TextureCompositor document in Resources.FindObjectsOfTypeAll<TextureCompositor>())
                {
                    bool modelChanged = document.undoDeserialized || document.NativeUndoVersionsChanged();
                    document.undoDeserialized = false;
                    bool effectChanged = false;
                    foreach (ShaderFX effect in changedEffects)
                        if (ContainsShaderFX(document.layers, effect))
                        {
                            effectChanged = true;
                            break;
                        }
                    if (!modelChanged && !effectChanged)
                        continue;
                    if (modelChanged)
                    {
                        document.NormalizeModel();
                        document.InvalidateDrawingLayerSurfaces();
                    }
                    document.CaptureNativeUndoVersions();
                    Changed?.Invoke(document);
                }
            }
            finally
            {
                IsRefreshingUndo = false;
            }
        }
    }
}
