using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        private const string OutputSpriteName = "Output Sprite";

        [SerializeField, HideInInspector] private Texture2D outputTexture;
        [SerializeField, HideInInspector] private Sprite outputSprite;

        public Texture2D OutputTexture => outputTexture;
        public Sprite OutputSprite => outputSprite;

        [NonSerialized] private LiveOutputSession liveOutput;
        internal static event Action<CompositorOutputChange> OutputTextureChanged;

        private void NotifyOutputTextureChanged()
        {
            if (outputTexture != null && OutputTextureChanged != null)
                OutputTextureChanged.Invoke(new CompositorOutputChange(this));
        }

        internal void PublishLiveOutput(RenderTexture source)
        {
            if (source == null || outputTexture == null || !AssetDatabase.Contains(this)) return;
            if (liveOutput != null && !liveOutput.Matches(outputTexture)) StopLiveOutput();
            liveOutput ??= new LiveOutputSession(outputTexture);
            liveOutput.SetFilter(outputFilter);
            liveOutput.Publish(source);
            NotifyOutputTextureChanged();
        }

        internal void StopLiveOutput()
        {
            LiveOutputSession previous = liveOutput;
            liveOutput = null;
            previous?.Dispose();
            if (previous != null) NotifyOutputTextureChanged();
        }

        internal bool HasUnsavedAssetChanges()
        {
            if (EditorUtility.IsDirty(this) || outputTexture == null || outputSprite == null ||
                EditorUtility.IsDirty(outputTexture) || EditorUtility.IsDirty(outputSprite)) return true;
            foreach (ShaderFX effect in embeddedShaderFX)
                if (effect != null && EditorUtility.IsDirty(effect)) return true;
            return HasDirtyPixels(layers);

            bool HasDirtyPixels(System.Collections.Generic.List<Layer> source)
            {
                foreach (Layer layer in source)
                {
                    if (layer?.Behaviour is DrawingLayerBehaviour drawing && drawing.StoredTexture != null &&
                        EditorUtility.IsDirty(drawing.StoredTexture)) return true;
                    if (layer?.AsGroup() is Layer group && HasDirtyPixels(group.layers)) return true;
                }
                return false;
            }
        }

        internal static TextureCompositor FindDocument(UnityEngine.Object asset)
        {
            if (asset is TextureCompositor document)
                return document;
            if (!(asset is Texture2D) && !(asset is Sprite))
                return null;
            string path = AssetDatabase.GetAssetPath(asset);
            if (!string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase))
                return null;
            foreach (UnityEngine.Object child in AssetDatabase.LoadAllAssetsAtPath(path))
                if (child is TextureCompositor compositor)
                    return compositor;
            return null;
        }

        internal bool TrySaveWithOutput(string newAssetPath = null)
        {
            try
            {
                SaveWithOutput(newAssetPath);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                EditorUtility.DisplayDialog("WhimTex save failed", exception.Message, "OK");
                return false;
            }
        }

        internal void SaveWithOutput(string newAssetPath)
        {
            bool createAsset = !string.IsNullOrEmpty(newAssetPath);
            string path = createAsset ? newAssetPath : AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(path) ||
                !string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save the document as a Unity .asset file first.");
            if (createAsset && (AssetDatabase.Contains(this) || File.Exists(path) ||
                AssetDatabase.LoadMainAssetAtPath(path) != null))
                throw new InvalidOperationException("Save As requires a new document and an unused asset path.");

            StopLiveOutput();
            Undo.FlushUndoRecordObjects();
            RemoveUnusedEmbeddedShaderFX();
            SyncDrawingLayerTextures();
            Texture2D rendered = null;
            Sprite generatedSprite = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                rendered = Compose();
                if (rendered == null)
                    throw new InvalidOperationException("The compositor returned no output texture.");
                if (createAsset)
                {
                    outputTexture = null;
                    outputSprite = null;
                    AssetDatabase.CreateAsset(this, path);
                    if (AssetDatabase.GetAssetPath(this) != path)
                        throw new InvalidOperationException("Unity could not create the document asset at " + path);
                }
                PersistDrawingLayerTextures(reimport: false);
                PersistEmbeddedShaderFX();
                RecoverOutputReferences(path);

                rendered.name = Path.GetFileNameWithoutExtension(path);
                rendered.hideFlags = HideFlags.None;
                if (outputTexture == null)
                {
                    AssetDatabase.AddObjectToAsset(rendered, this);
                    if (AssetDatabase.GetAssetPath(rendered) != path)
                        throw new InvalidOperationException("Unity could not store the output texture in " + path);
                    outputTexture = rendered;
                    rendered = null;
                }
                else
                {
                    rendered.wrapModeU = outputTexture.wrapModeU;
                    rendered.wrapModeV = outputTexture.wrapModeV;
                    rendered.anisoLevel = outputTexture.anisoLevel;
                    EditorUtility.CopySerialized(rendered, outputTexture);
                }

                generatedSprite = Sprite.Create(outputTexture,
                    new Rect(0f, 0f, outputTexture.width, outputTexture.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                if (generatedSprite == null)
                    throw new InvalidOperationException("Unity could not create the output sprite.");
                generatedSprite.name = OutputSpriteName;
                generatedSprite.hideFlags = HideFlags.None;
                if (outputSprite == null)
                {
                    AssetDatabase.AddObjectToAsset(generatedSprite, this);
                    if (AssetDatabase.GetAssetPath(generatedSprite) != path)
                        throw new InvalidOperationException("Unity could not store the output sprite in " + path);
                    outputSprite = generatedSprite;
                    generatedSprite = null;
                }
                else
                {
                    EditorUtility.CopySerialized(generatedSprite, outputSprite);
                }

                EditorUtility.SetDirty(outputTexture);
                EditorUtility.SetDirty(outputSprite);
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssetIfDirty(this);
                AssetDatabase.SetMainObject(outputTexture, path);
                AssetDatabase.WriteImportSettingsIfDirty(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                TextureCompositorProjectPreview.ClearCache();
                NotifyOutputTextureChanged();
                Changed?.Invoke(this);
            }
            finally
            {
                RenderTexture.active = previous;
                if (generatedSprite != null && !AssetDatabase.Contains(generatedSprite))
                    DestroyImmediate(generatedSprite);
                if (rendered != null && !AssetDatabase.Contains(rendered))
                    DestroyImmediate(rendered);
            }
        }

        private void RecoverOutputReferences(string path)
        {
            if (outputTexture != null && AssetDatabase.GetAssetPath(outputTexture) != path)
                outputTexture = null;
            if (outputSprite != null && AssetDatabase.GetAssetPath(outputSprite) != path)
                outputSprite = null;
            if (outputTexture == null)
                outputTexture = AssetDatabase.LoadMainAssetAtPath(path) as Texture2D;
            if (outputSprite == null && outputTexture != null)
            {
                foreach (UnityEngine.Object child in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (child is Sprite sprite && sprite.name == OutputSpriteName && sprite.texture == outputTexture)
                    {
                        outputSprite = sprite;
                        break;
                    }
            }
        }
    }
}
