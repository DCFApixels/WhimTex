using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        [NonSerialized] private volatile bool undoDeserialized;
        [NonSerialized] private Dictionary<UnityEngine.Object, int> nativeUndoVersions;
        internal static bool IsRefreshingUndo { get; private set; }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }
        void ISerializationCallbackReceiver.OnAfterDeserialize() => undoDeserialized = true;

        internal void ResetUndoTrackingAfterLoad()
        {
            undoDeserialized = false;
            CaptureNativeUndoVersions();
        }

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
                    // A baseline must not materialize lazy TIFF pixels just to track Undo.
                    if (layer?.Behaviour is DrawingLayerBehaviour drawing && !drawing.HasDeferredTexture)
                    {
                        foreach (var texture in drawing.StoredPixelTextures) Track(texture);
                        Track(drawing.PixelUndoObject);
                    }
                    else if (layer?.Behaviour is FileLayerBehaviour) Track(layer.SamplingSource);
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

        internal static void CaptureSynchronizedPaintVersions(Texture2D original, Texture2D[] tiles)
        {
            // Async CPU patches already have matching GPU pixels. They are not native Undo.
            if (IsRefreshingUndo) return;
            foreach (var document in Resources.FindObjectsOfTypeAll<WhimTexDocument>())
            {
                var versions = document.nativeUndoVersions;
                if (original == null || versions == null || !versions.ContainsKey(original)) continue;
                versions[original] = EditorUtility.GetDirtyCount(original);
                if (tiles != null) foreach (var tile in tiles)
                    if (tile != null && versions.ContainsKey(tile)) versions[tile] = EditorUtility.GetDirtyCount(tile);
            }
        }

        private List<Texture2D> CollectNativeDrawingTextures()
        {
            List<Texture2D> restored = null;
            VisitDrawingLayers(layers, drawing =>
            {
                if (drawing.HasDeferredTexture) return;
                foreach (var texture in drawing.StoredPixelTextures)
                    if (texture.isReadable && (nativeUndoVersions == null || !nativeUndoVersions.TryGetValue(texture, out int version) ||
                        EditorUtility.GetDirtyCount(texture) != version))
                        (restored ??= new List<Texture2D>()).Add(texture);
            });
            return restored;
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
                var documents = Resources.FindObjectsOfTypeAll<WhimTexDocument>();
                var restored = new List<Texture2D>[documents.Length];
                bool upload = false;
                // Collect before any global Drain can change another document's dirty counts.
                for (int i = 0; i < documents.Length; i++)
                {
                    var document = documents[i];
                    if (document.undoDeserialized) document.NormalizeModel();
                    restored[i] = document.CollectNativeDrawingTextures();
                    upload |= restored[i] != null;
                }
                // Native Texture2D Undo restores CPU bytes without uploading. Finish
                // committed transfers before upload and before rebuilding derived surfaces.
                if (upload) PaintTileTransfer.Drain();
                for (int i = 0; i < documents.Length; i++)
                {
                    var document = documents[i];
                    bool pixelsChanged = restored[i] != null;
                    if (pixelsChanged) foreach (var texture in restored[i]) if (texture != null) texture.Apply(false, false);
                    VisitDrawingLayers(document.layers, drawing => pixelsChanged |= drawing.ReconcilePixelUndo());
                    bool modelChanged = pixelsChanged || document.undoDeserialized || document.NativeUndoVersionsChanged();
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
                    using var profile = WhimTexProfiler.Measure("undo.model_restore", document);
                    WhimTexProfiler.Changed(document, modelChanged ? "Undo/Redo document" : "Undo/Redo FX");
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
